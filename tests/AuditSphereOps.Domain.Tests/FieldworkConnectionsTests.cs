using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE package 4 with real role identities: a two-period file split and validated per period, mapping memory
/// proposals with controlled approval, the upload-stage currency review, statements that drill into audit procedures,
/// the integrated sampling log, client evidence linking, the physical file index, ad hoc steps and the mandatory
/// analytical-review and going-concern gate.
/// </summary>
[Trait("Profile", "Database")]
public sealed class FieldworkConnectionsTests
{
  private sealed record World(Guid FirmId, Guid ClientId, Guid EngagementId, AppUser Preparer, AppUser Reviewer, AppUser Senior)
  {
    public ActorContext Prep => new(Preparer.Id, FirmId, Preparer.SessionEpoch, ["AccountingPreparer"]);
    public ActorContext Rev => new(Reviewer.Id, FirmId, Reviewer.SessionEpoch, ["AccountingReviewer"]);
    public ActorContext Auditor => new(Senior.Id, FirmId, Senior.SessionEpoch, ["Senior"]);
    public ActorContext Manager => new(Reviewer.Id, FirmId, Reviewer.SessionEpoch, ["Manager"]);
  }

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = name + Guid.NewGuid().ToString("N"), TenantId = "tenant-fw", Email = $"{name}-{Guid.NewGuid():N}@example.test",
    DisplayName = name, UserKind = "Staff", CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  { Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id };

  private static async Task<CommandResult<SamplingRunView>> RunReviewedSamplingAsync(
    IAuditSphereDbContext db, ActorContext actor, RunSamplingRequest request)
  {
    var preview = await AuditFieldworkService.PreviewSamplingAsync(db, actor, request);
    if (!preview.Succeeded) return CommandResult<SamplingRunView>.Fail(preview.ErrorCode!, preview.Message!);
    return await AuditFieldworkService.RunSamplingAsync(db, actor, request with { ExpectedPreviewDigest = preview.Value!.PreviewDigest });
  }

  private static async Task<World> SeedAsync(PgTestSchema pg)
  {
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var w = new World(firmId, clientId, engagementId, User(firmId, "preparer"), User(firmId, "reviewer"), User(firmId, "senior"));
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Engagements.Where(x => x.Id == engagementId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Active")
      .SetProperty(x => x.ProfessionalWorkBlocked, false).SetProperty(x => x.ServiceRoute, "FinancialStatementAudit").SetProperty(x => x.PeriodEnd, "2026-12-31"));
    db.Users.AddRange(w.Preparer, w.Reviewer, w.Senior);
    db.RoleGrants.AddRange(Grant(firmId, w.Preparer, "AccountingPreparer"), Grant(firmId, w.Reviewer, "AccountingReviewer"),
      Grant(firmId, w.Reviewer, "Manager"), Grant(firmId, w.Senior, "Senior"));
    var taxonomyId = Guid.NewGuid();
    db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
    {
      Id = taxonomyId, FirmId = firmId, Code = "tax-v1", Framework = "IFRS", Name = "Test taxonomy", Status = AccountingWorkflowStates.Approved,
      EffectiveFrom = new DateOnly(2025, 1, 1), CreatedByUserId = w.Preparer.Id, ApprovedByUserId = w.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    foreach (var (code, section, normal) in new[] { ("CASH", "ASSETS", "DEBIT"), ("RECEIVABLES", "ASSETS", "DEBIT"), ("EQUITY", "EQUITY", "CREDIT"),
      ("REVENUE", "INCOME", "CREDIT"), ("EXPENSES", "EXPENSE", "DEBIT") })
      db.ReportingTaxonomyNodes.Add(new ReportingTaxonomyNode
      {
        Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = code, Name = code, StatementSection = section, DisplaySign = "SIGNED",
        NormalBalance = normal, IsPosting = true, Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
      });
    await db.SaveChangesAsync();
    return w;
  }

  private const string TwoPeriodCsv = """
    PeriodCode,AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode
    FY2025,1000,Cash,500,USD,E1,
    FY2025,3000,Share capital,-200,USD,E1,
    FY2025,4000,Sales,-1000,USD,E1,
    FY2025,5000,Expenses,700,USD,E1,
    FY2026,1000,Cash at bank,900,USD,E1,
    FY2026,1100,Receivables,150,USD,E1,
    FY2026,3000,Share capital,-200,USD,E1,
    FY2026,4000,Sales,-1600,USD,E1,
    FY2026,5000,Expenses,750,USD,E1,
    """;

  [Fact]
  public void Split_KeepsEachPeriodSeparate_AndRejectsRowsWithoutAPeriod()
  {
    var (header, rows) = MultiPeriodTrialBalanceService.ReadTable("tb.csv", Encoding.UTF8.GetBytes(TwoPeriodCsv));
    var split = MultiPeriodTrialBalanceService.Split(header, rows);
    Assert.Equal(["FY2025", "FY2026"], split.Keys);
    Assert.DoesNotContain("PeriodCode", split["FY2025"]);
    Assert.Equal(5, split["FY2025"].Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    Assert.Equal(6, split["FY2026"].Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    Assert.Throws<InvalidOperationException>(() => MultiPeriodTrialBalanceService.Split(header, [["", "1000", "Cash", "1", "USD", "E1", ""]]));
    Assert.Throws<InvalidOperationException>(() => MultiPeriodTrialBalanceService.ReadTable("tb.xls", [1, 2]));
  }

  [Fact]
  public async Task MultiPeriodIntake_MappingMemory_AndCurrencyReview()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    Guid fy25, fy26;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, w.Prep, new ClientAccountingProfileRequest(w.ClientId, "QA", "USD", 1, 1, "LEDGER", "L-1"))).Succeeded);
      fy25 = (await ClientAccountingService.CreatePeriodAsync(db, w.Prep, new ReportingPeriodRequest(w.ClientId, "FY2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "STATUTORY", "USD"))).Value;
      var created = await ClientAccountingService.CreatePeriodAsync(db, w.Prep, new ReportingPeriodRequest(w.ClientId, "FY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "USD", fy25));
      Assert.True(created.Succeeded, created.Message);
      fy26 = created.Value;
    }

    // An unbalanced period, or an unknown period, blocks the whole file.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var bad = TwoPeriodCsv.Replace("FY2026,5000,Expenses,750", "FY2026,5000,Expenses,751").Replace("FY2025,", "FY2024,");
      var preview = (await MultiPeriodTrialBalanceService.PreviewAsync(db, w.Prep, w.ClientId, "tb.csv", Encoding.UTF8.GetBytes(bad))).Value!;
      Assert.False(preview.CanImport);
      Assert.Contains(preview.Periods, x => x.PeriodCode == "FY2024" && x.Error!.Contains("No reporting period"));
      Assert.Contains(preview.Periods, x => x.PeriodCode == "FY2026" && !x.Balanced);
      Assert.False((await MultiPeriodTrialBalanceService.ImportAsync(db, w.Prep, w.ClientId, w.EngagementId, "tb.csv", Encoding.UTF8.GetBytes(bad))).Succeeded);
      Assert.False(await db.TrialBalanceDatasets.AnyAsync(x => x.EngagementId == w.EngagementId));
    }

    MultiPeriodImportResult imported;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await MultiPeriodTrialBalanceService.ImportAsync(db, w.Prep, w.ClientId, w.EngagementId, "tb.csv", Encoding.UTF8.GetBytes(TwoPeriodCsv));
      Assert.True(result.Succeeded, result.Message);
      imported = result.Value!;
      Assert.Equal(2, imported.Datasets.Count);
    }
    var ds25 = imported.Datasets.Single(x => x.PeriodCode == "FY2025").DatasetId;
    var ds26 = imported.Datasets.Single(x => x.PeriodCode == "FY2026").DatasetId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.False((await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == ds26)).Balanced);
      // The validation operation's accepted outcome (exercised by its own tests) is applied as a fixture step here.
      await db.TrialBalanceDatasets.Where(x => x.Id == ds25 || x.Id == ds26).ExecuteUpdateAsync(s => s.SetProperty(x => x.Balanced, true).SetProperty(x => x.ValidationStatus, "Accepted"));
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var d25 = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == ds25);
      var d26 = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == ds26);
      Assert.Equal((fy25, fy26), (d25.PeriodId, d26.PeriodId));
      Assert.NotEqual(d25.NormalizedDatasetDigest, d26.NormalizedDatasetDigest);
      Assert.Equal((4, 5), (await db.TrialBalanceRows.CountAsync(x => x.DatasetId == ds25), await db.TrialBalanceRows.CountAsync(x => x.DatasetId == ds26)));
      Assert.Equal(("USD", "USD"), (d25.Currency, d26.Currency)); // balance validation runs as a separate durable operation
    }

    // Mapping memory: approve FY2025, then propose FY2026 from it.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, w.Prep, new CreateMappingVersionRequest(ds25, "tax-v1", "2025-01-01", "2025-12-31",
      [
        new("1000", "CASH", "ASSETS", 1m, "Cash"), new("3000", "EQUITY", "EQUITY", 1m, "Equity"),
        new("4000", "REVENUE", "INCOME", 1m, "Sales", "Revenue"), new("5000", "EXPENSES", "EXPENSE", 1m, "Expenses")
      ]));
      Assert.True(mapping.Succeeded, mapping.Message);
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, w.Rev, mapping.Value, 1)).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var view = (await MappingMemoryService.ProposeAsync(db, w.Prep, ds26)).Value!;
      var byCode = view.Proposals.ToDictionary(x => x.AccountCode);
      Assert.Equal(MappingMemoryStatuses.NameChanged, byCode["1000"].Status);
      Assert.Equal("Cash", byCode["1000"].PriorAccountName);
      Assert.Equal(MappingMemoryStatuses.NewAccount, byCode["1100"].Status);
      Assert.Equal(MappingMemoryStatuses.Reused, byCode["4000"].Status);
      Assert.Equal("REVENUE", byCode["4000"].Allocations.Single().DestinationCode);
      Assert.Equal(ErrorCodes.Accounting.MappingIncomplete, (await MappingMemoryService.CreateDraftAsync(db, w.Prep, ds26, "tax-v1", "2026-01-01", "2026-12-31", [])).ErrorCode);
      var draft = await MappingMemoryService.CreateDraftAsync(db, w.Prep, ds26, "tax-v1", "2026-01-01", "2026-12-31",
        [new MappingAllocationInput("1100", "RECEIVABLES", "ASSETS", 1m, "New receivables account")]);
      Assert.True(draft.Succeeded, draft.Message);
      var created = await db.MappingVersions.AsNoTracking().SingleAsync(x => x.Id == draft.Value);
      Assert.Equal(AccountingPackageStates.MappingDraft, created.Status); // memory proposes; approval stays a reviewer action
      Assert.Equal(5, await db.MappingAllocations.CountAsync(x => x.MappingVersionId == draft.Value));
    }

    // Currency review: approved closing rates USD→QAR at both period ends; a missing rate fails closed.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var setId = Guid.NewGuid();
      db.ExchangeRateSetVersions.Add(new ExchangeRateSetVersion
      {
        Id = setId, FirmId = w.FirmId, Code = "CB-2026", Version = 1, Source = "Central bank closing rates", Status = AccountingWorkflowStates.Approved,
        CreatedByUserId = w.Preparer.Id, ApprovedByUserId = w.Reviewer.Id, CreatedAt = DateTimeOffset.UtcNow, ApprovedAt = DateTimeOffset.UtcNow
      });
      foreach (var (date, rate) in new[] { (new DateOnly(2025, 12, 31), 3.64m), (new DateOnly(2026, 12, 31), 3.65m) })
        db.ExchangeRates.Add(new ExchangeRate
        {
          Id = Guid.NewGuid(), FirmId = w.FirmId, RateSetVersionId = setId, FromCurrency = "USD", ToCurrency = "QAR", RateDate = date, RateType = "CLOSING",
          Rate = rate, Direction = "DIRECT", CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
      Assert.Equal(ErrorCodes.GateBlocked, (await TrialBalanceCurrencyReviewQuery.GetAsync(db, w.Prep, ds26, "EUR")).ErrorCode);
      var review = (await TrialBalanceCurrencyReviewQuery.GetAsync(db, w.Prep, ds26, "QAR", 20m, 100m)).Value!;
      Assert.Equal((3.65m, "Central bank closing rates", new DateOnly(2026, 12, 31)), (review.CurrentRate!.Rate, review.CurrentRate.Source, review.CurrentRate.RateDate));
      Assert.Equal(ds25, review.PriorDatasetId);
      var byCode = review.Lines.ToDictionary(x => x.AccountCode);
      Assert.Equal(-5840m, byCode["4000"].TranslatedAmount);          // 1,600 × 3.65
      Assert.True(byCode["4000"].Highlighted);                         // -5,840 vs -3,640: 60% movement
      Assert.True(byCode["1100"].Highlighted);
      Assert.Equal("New account this period", byCode["1100"].HighlightReason);
      Assert.False(byCode["3000"].Highlighted);                        // -730 vs -728: under both thresholds
    }
  }

  [Fact]
  public async Task Statements_DrillIntoProcedures_AndSamplingPhysicalAndAdHocStepsConnect()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    Guid revenueProcedure, cashProcedure, systematicProcedure, attributeProcedure, staleProcedure, scheduleId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var datasetId = Guid.NewGuid();
      var mappingId = Guid.NewGuid();
      var accounts = new (string Code, decimal Amount, string Dest, string Section, string? Area)[]
        { ("1000", 800m, "CASH", "ASSETS", "Cash and bank"), ("3000", -300m, "EQUITY", "EQUITY", null), ("4000", -1500m, "REVENUE", "INCOME", "Revenue"), ("5000", 1000m, "EXPENSES", "EXPENSE", null) };
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset { Id = datasetId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, SourceKind = "Raw",
        Currency = "QAR", Balanced = true, ValidationStatus = "Accepted", NormalizedDatasetDigest = Hashing.Sha256Hex("fw"), ImportedAt = now, ImportedByUserId = w.Preparer.Id });
      db.TrialBalanceRows.AddRange(accounts.Select(a => new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = a.Code, AccountName = a.Dest, Amount = a.Amount, Currency = "QAR", Entity = "E" }));
      await db.SaveChangesAsync();
      await db.TrialBalanceDatasets.Where(x => x.Id == datasetId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
      db.MappingVersions.Add(new MappingVersion { Id = mappingId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, DatasetId = datasetId, TaxonomyVersion = "tax-v1",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved, CreatedByUserId = w.Preparer.Id, ApprovedByUserId = w.Reviewer.Id, ApprovedAt = now, CreatedAt = now });
      db.MappingAllocations.AddRange(accounts.Select(a => new MappingAllocation { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
        MappingVersionId = mappingId, SourceAccountCode = a.Code, DestinationCode = a.Dest, StatementSection = a.Section, AuditArea = a.Area, Fraction = 1m, Rationale = "m", CreatedAt = now }));
      var programId = Guid.NewGuid();
      var versionId = Guid.NewGuid();
      db.AuditProgramVersions.Add(new AuditProgramVersion { Id = versionId, FirmId = w.FirmId, Version = "2026.1", SourceHash = Hashing.Sha256Hex("program"),
        Status = AuditProgramStatuses.Published, CreatedByUserId = w.Reviewer.Id, ApprovedByUserId = w.Reviewer.Id, CreatedAt = now, ApprovedAt = now });
      await db.SaveChangesAsync();
      db.EngagementAuditPrograms.Add(new EngagementAuditProgram { Id = programId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
        ProgramVersionId = versionId, AdoptedByUserId = w.Reviewer.Id, AdoptedAt = now });
      AuditProcedure Procedure(string code, string section, string title) => new()
      {
        Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, EngagementProgramId = programId, SourceProcedureId = code,
        SourceSectionTitle = section, SourceWording = title, Title = title, ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Planned, CreatedAt = now
      };
      var revenue = Procedure("REV-01", "Revenue", "Test revenue cut-off");
      var cash = Procedure("CSH-01", "Cash and bank", "Agree bank balances to confirmations");
      var systematicStep = Procedure("SMP-01", "Sampling", "Select systematic random transactions");
      var attributeStep = Procedure("SMP-02", "Sampling", "Select attribute-stratified transactions");
      var staleStep = Procedure("SMP-03", "Sampling", "Review stale preview behavior");
      db.AuditProcedures.AddRange(revenue, cash, systematicStep, attributeStep, staleStep);
      systematicProcedure = systematicStep.Id;
      attributeProcedure = attributeStep.Id;
      staleProcedure = staleStep.Id;
      (revenueProcedure, cashProcedure) = (revenue.Id, cash.Id);
      scheduleId = Guid.NewGuid();
      db.AuditSchedules.Add(new AuditSchedule { Id = scheduleId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, ScheduleType = "SALES_LISTING",
        EntityIdentifier = "E", SourceReceiptReference = "PBC-1", Currency = "QAR", SignConvention = "DEBIT_POSITIVE", SourceHash = Hashing.Sha256Hex("s"), RowCount = 20,
        Status = AuditScheduleStatuses.Approved, CreatedByUserId = w.Senior.Id, CreatedAt = now });
      db.AuditScheduleRows.AddRange(Enumerable.Range(1, 20).Select(i => new AuditScheduleRow { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId,
        EngagementId = w.EngagementId, ScheduleId = scheduleId, StableRowId = $"INV-{i:000}", SourceLineNumber = i, AccountCode = i <= 10 ? "4000" : "4010", Description = $"Invoice {i}",
        SignedAmount = i * 100m, Currency = "QAR", TransactionDate = new DateOnly(2026, i <= 10 ? 1 : 2, Math.Min(i, 28)), CreatedAt = now }));
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Statements from the mapped TB, each line resolving to its procedures.
      var statements = (await FinancialStatementDrillDownQuery.GetAsync(db, w.Auditor, w.EngagementId)).Value!;
      Assert.True(statements.Balances);
      Assert.Equal(500m, statements.ProfitOrLoss.Total);
      var revenueLine = statements.ProfitOrLoss.Lines.Single(x => x.DestinationCode == "REVENUE");
      Assert.Equal((1500m, "Revenue"), (revenueLine.Amount, revenueLine.AuditArea));
      Assert.Equal([revenueProcedure], revenueLine.Procedures.Select(x => x.ProcedureId));
      Assert.Equal([cashProcedure], statements.FinancialPosition.Lines.Single(x => x.DestinationCode == "CASH").Procedures.Select(x => x.ProcedureId));

      // Integrated sampling: MUS over the approved schedule, logged and reproducible.
      var run = await RunReviewedSamplingAsync(db, w.Auditor, new(w.EngagementId, revenueProcedure, scheduleId, "MUS", 5000m, null, null, null, "MUS over sales listing"));
      Assert.True(run.Succeeded, run.Message);
      Assert.True(run.Value!.Reproduces);
      Assert.Equal(run.Value.Run.SelectedCount, run.Value.Items.Count);
      Assert.Equal((20, 21000m), (run.Value.Run.PopulationCount, run.Value.Run.PopulationAbsoluteTotal));
      var reloaded = (await AuditFieldworkService.GetSamplingRunAsync(db, w.Manager, run.Value.Run.Id)).Value!;
      Assert.True(reloaded.Reproduces);
      Assert.Equal(run.Value.Items.Select(x => x.StableRowId), reloaded.Items.Select(x => x.StableRowId));
      var random = await RunReviewedSamplingAsync(db, w.Auditor, new(w.EngagementId, cashProcedure, scheduleId, "RANDOM", null, null, 5, 42, "Random five"));
      Assert.True(random.Succeeded, random.Message);
      Assert.Equal(5, random.Value!.Run.SelectedCount);
      var systematic = await RunReviewedSamplingAsync(db, w.Auditor,
        new(w.EngagementId, systematicProcedure, scheduleId, "SYSTEMATIC", null, null, 5, 42, "Systematic random five"));
      Assert.True(systematic.Succeeded, systematic.Message);
      Assert.Equal(5, systematic.Value!.Run.SelectedCount);
      Assert.Equal("audit-systematic-engine.v1", systematic.Value.Run.EngineVersion);
      var systematicReload = await AuditFieldworkService.GetSamplingRunAsync(db, w.Manager, systematic.Value.Run.Id);
      Assert.True(systematicReload.Value!.Reproduces);
      Assert.Equal(systematic.Value.Items.Select(x => x.StableRowId), systematicReload.Value.Items.Select(x => x.StableRowId));

      var attributeRequest = new RunSamplingRequest(w.EngagementId, attributeProcedure, scheduleId, AuditSamplingMethods.AttributeStrata,
        null, null, 5, 17, "Stratify the approved sales sample.",
        [SamplingAttributeFields.Account, SamplingAttributeFields.Currency, SamplingAttributeFields.Direction, SamplingAttributeFields.Month]);
      var attributePreview = await AuditFieldworkService.PreviewSamplingAsync(db, w.Auditor, attributeRequest);
      Assert.True(attributePreview.Succeeded, attributePreview.Message);
      Assert.Equal(5, attributePreview.Value!.Outcome.SelectedCount);
      Assert.Equal(2, attributePreview.Value.Outcome.Strata!.Count);
      Assert.All(attributePreview.Value.Outcome.Strata, x => Assert.Equal(x.Slots, x.SelectedCount));
      var attributeRun = await AuditFieldworkService.RunSamplingAsync(db, w.Auditor,
        attributeRequest with { ExpectedPreviewDigest = attributePreview.Value.PreviewDigest });
      Assert.True(attributeRun.Succeeded, attributeRun.Message);
      Assert.True(attributeRun.Value!.Reproduces);
      Assert.Equal(attributePreview.Value.PreviewDigest, attributeRun.Value.Run.PreviewDigest);
      Assert.Equal(AuditFieldworkService.SamplingOrderingPolicy, attributeRun.Value.Run.OrderingPolicy);
      Assert.Equal(["ACCOUNT", "CURRENCY", "DIRECTION", "MONTH"], System.Text.Json.JsonSerializer.Deserialize<string[]>(attributeRun.Value.Run.AttributeFields!)!);
      var attributeRowId = await db.AuditScheduleRows.Where(x => x.ScheduleId == scheduleId).OrderBy(x => x.SourceLineNumber).Select(x => x.Id).FirstAsync();
      await db.AuditScheduleRows.Where(x => x.Id == attributeRowId).ExecuteUpdateAsync(s => s.SetProperty(x => x.AccountCode, "4999"));
      var changedAttributeReplay = await AuditFieldworkService.GetSamplingRunAsync(db, w.Manager, attributeRun.Value.Run.Id);
      Assert.False(changedAttributeReplay.Value!.Reproduces);
      await db.AuditScheduleRows.Where(x => x.Id == attributeRowId).ExecuteUpdateAsync(s => s.SetProperty(x => x.AccountCode, "4000"));
      var attributeRetry = await AuditFieldworkService.RunSamplingAsync(db, w.Auditor,
        attributeRequest with { ExpectedPreviewDigest = attributePreview.Value.PreviewDigest });
      Assert.True(attributeRetry.Succeeded, attributeRetry.Message);
      Assert.Equal(attributeRun.Value.Run.Id, attributeRetry.Value!.Run.Id);
      Assert.Equal(1, await db.AuditSamplingRuns.CountAsync(x => x.PreviewDigest == attributePreview.Value.PreviewDigest));
      var changedRetry = await AuditFieldworkService.RunSamplingAsync(db, w.Auditor,
        attributeRequest with { SampleSize = 6, ExpectedPreviewDigest = attributePreview.Value.PreviewDigest });
      Assert.False(changedRetry.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedRetry.ErrorCode);

      var staleRequest = new RunSamplingRequest(w.EngagementId, staleProcedure, scheduleId, AuditSamplingMethods.AttributeStrata,
        null, null, 5, 18, "Original reviewed intent.", [SamplingAttributeFields.Account]);
      var stalePreview = await AuditFieldworkService.PreviewSamplingAsync(db, w.Auditor, staleRequest);
      Assert.True(stalePreview.Succeeded, stalePreview.Message);
      var staleAttempt = await AuditFieldworkService.RunSamplingAsync(db, w.Auditor,
        staleRequest with { Rationale = "Changed after preview.", ExpectedPreviewDigest = stalePreview.Value!.PreviewDigest });
      Assert.False(staleAttempt.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleAttempt.ErrorCode);
      Assert.False(await db.AuditSelections.AnyAsync(x => x.ProcedureId == staleProcedure));

      var tooSmall = await AuditFieldworkService.PreviewSamplingAsync(db, w.Auditor,
        staleRequest with { SampleSize = 1, AttributeFields = [SamplingAttributeFields.Account] });
      Assert.False(tooSmall.Succeeded);
      Assert.Contains("at least one slot for each of the 2 strata", tooSmall.Message, StringComparison.OrdinalIgnoreCase);

      var firstPopulationRow = await db.AuditScheduleRows.Where(x => x.ScheduleId == scheduleId).OrderBy(x => x.SourceLineNumber)
        .Select(x => x.Id).FirstAsync();
      await db.AuditScheduleRows.Where(x => x.Id == firstPopulationRow).ExecuteUpdateAsync(s => s.SetProperty(x => x.Currency, "USD"));
      var mixedCurrency = await AuditFieldworkService.PreviewSamplingAsync(db, w.Auditor, attributeRequest);
      Assert.False(mixedCurrency.Succeeded);
      Assert.Contains("mixed currencies", mixedCurrency.Message, StringComparison.OrdinalIgnoreCase);
      await db.AuditScheduleRows.Where(x => x.Id == firstPopulationRow).ExecuteUpdateAsync(s => s.SetProperty(x => x.Currency, "QAR"));

      var atomicRequest = staleRequest with { Seed = 19, Rationale = "Exercise sampling save recovery." };
      var atomicPreview = await AuditFieldworkService.PreviewSamplingAsync(db, w.Auditor, atomicRequest);
      Assert.True(atomicPreview.Succeeded, atomicPreview.Message);
      await db.Database.ExecuteSqlRawAsync("""
        CREATE FUNCTION fail_sampling_run_insert_for_test() RETURNS trigger
        LANGUAGE plpgsql AS $function$
        BEGIN
          RAISE EXCEPTION 'forced sampling run insert failure';
        END
        $function$;
        CREATE TRIGGER fail_sampling_run_insert_for_test
        BEFORE INSERT ON audit_sampling_runs
        FOR EACH ROW EXECUTE FUNCTION fail_sampling_run_insert_for_test();
        """);
      await Assert.ThrowsAsync<DbUpdateException>(async () => await AuditFieldworkService.RunSamplingAsync(db, w.Auditor,
        atomicRequest with { ExpectedPreviewDigest = atomicPreview.Value!.PreviewDigest }));
      Assert.False(await db.AuditSelections.AsNoTracking().AnyAsync(x => x.ProcedureId == staleProcedure));
      Assert.False(await db.AuditSamplingRuns.AsNoTracking().AnyAsync(x => x.PreviewDigest == atomicPreview.Value!.PreviewDigest));
      await db.Database.ExecuteSqlRawAsync("""
        DROP TRIGGER fail_sampling_run_insert_for_test ON audit_sampling_runs;
        DROP FUNCTION fail_sampling_run_insert_for_test();
        """);
      var recoveredRun = await AuditFieldworkService.RunSamplingAsync(db, w.Auditor,
        atomicRequest with { ExpectedPreviewDigest = atomicPreview.Value!.PreviewDigest });
      Assert.True(recoveredRun.Succeeded, recoveredRun.Message);
      var recoveredRetry = await AuditFieldworkService.RunSamplingAsync(db, w.Auditor,
        atomicRequest with { ExpectedPreviewDigest = atomicPreview.Value.PreviewDigest });
      Assert.True(recoveredRetry.Succeeded, recoveredRetry.Message);
      Assert.Equal(recoveredRun.Value!.Run.Id, recoveredRetry.Value!.Run.Id);
      Assert.Equal(1, await db.AuditSelections.AsNoTracking().CountAsync(x => x.ProcedureId == staleProcedure));
      Assert.Equal(1, await db.AuditSamplingRuns.AsNoTracking().CountAsync(x => x.PreviewDigest == atomicPreview.Value.PreviewDigest));

      var recoveredSelection = await db.AuditSelections.AsNoTracking().SingleAsync(x => x.Id == recoveredRun.Value.Run.SelectionId);
      Assert.Equal(AuditSelectionStatuses.Submitted, recoveredSelection.Status);
      Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, w.Manager,
        new ReviewSelectionRequest(recoveredSelection.Id, AuditSelectionStatuses.Reviewed, "Independently reviewed the exact selected rows."))).Succeeded);
      var sampleSet = await AuditSamplingService.GetSampleSetAsync(db, w.Auditor, recoveredSelection.Id);
      Assert.True(sampleSet.Succeeded, sampleSet.Message);
      var sampledItem = Assert.Single(sampleSet.Value!.Items);
      Assert.Equal(AuditItemTestResults.Pending, sampledItem.TestResult);
      var missingFollowUp = await AuditFieldworkService.RecordItemTestAsync(db, w.Auditor,
        new RecordItemTestRequest(sampledItem.SelectionItemId, "Compared the item with source evidence.", ["sales-invoice-001"],
          AuditItemTestResults.Exception, 100m, null, null));
      Assert.False(missingFollowUp.Succeeded);
      Assert.Contains("follow-up", missingFollowUp.Message, StringComparison.OrdinalIgnoreCase);
      var recordedTest = await AuditFieldworkService.RecordItemTestAsync(db, w.Auditor,
        new RecordItemTestRequest(sampledItem.SelectionItemId, "Compared the item with source evidence.", ["sales-invoice-001"],
          AuditItemTestResults.Exception, 100m, "Invoice date falls after year end.", "Assess cut-off and obtain the delivery note."));
      Assert.True(recordedTest.Succeeded, recordedTest.Message);
      var preparedView = await AuditSamplingService.GetSampleSetAsync(db, w.Auditor, recoveredSelection.Id);
      Assert.True(preparedView.Succeeded, preparedView.Message);
      Assert.Equal(AuditItemTestResults.Exception, preparedView.Value!.Items.Single().TestResult);
      Assert.Equal(100m, preparedView.Value.Items.Single().ExceptionAmount);
      Assert.Equal("Assess cut-off and obtain the delivery note.", preparedView.Value.Items.Single().FollowUp);
      Assert.Equal(["sales-invoice-001"], preparedView.Value.Items.Single().EvidenceReferences);
      var reviewerView = await AuditSamplingService.GetSampleSetAsync(db, w.Manager, recoveredSelection.Id);
      Assert.True(reviewerView.Value!.Items.Single().CanReviewTest);
      Assert.True((await AuditFieldworkService.ReviewItemTestAsync(db, w.Manager,
        new ReviewItemTestRequest(recordedTest.Value!.AuditItemTestId, AuditItemTestReviewDecisions.Reviewed,
          "Exception and follow-up reviewed."))).Succeeded);
      var completedSampleSet = await AuditSamplingService.GetSampleSetAsync(db, w.Manager, recoveredSelection.Id);
      Assert.True(completedSampleSet.Value!.Items.Single().TestReviewed);
      Assert.False(completedSampleSet.Value.Items.Single().CanReviewTest);

      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit_sampling_runs SET seed = 7 WHERE id = {random.Value.Run.Id}"));

      // Physical file index: X-1 in Box 3, linked both ways, with movement history.
      var item = await AuditFieldworkService.RegisterPhysicalItemAsync(db, w.Auditor, w.EngagementId, "x-1", "Box 3", "Signed stock count sheets", "Client warehouse");
      Assert.True(item.Succeeded, item.Message);
      Assert.Equal(ErrorCodes.IdempotencyConflict, (await AuditFieldworkService.RegisterPhysicalItemAsync(db, w.Auditor, w.EngagementId, "X-1", "Box 4", "Duplicate", "Office")).ErrorCode);
      Assert.True((await AuditFieldworkService.MovePhysicalItemAsync(db, w.Auditor, item.Value, "Firm archive room, shelf 2", "Returned after fieldwork")).Succeeded);
      Assert.True((await AuditFieldworkService.LinkPhysicalItemAsync(db, w.Auditor, cashProcedure, item.Value)).Succeeded);
      var physical = (await AuditFieldworkService.PhysicalItemsAsync(db, w.Manager, w.EngagementId)).Single();
      Assert.Equal(("X-1", "Box 3", "Firm archive room, shelf 2", 2), (physical.FileIndex, physical.BoxReference, physical.CurrentLocation, physical.Movements.Count));
      Assert.Equal(cashProcedure, physical.Procedures.Single().ProcedureId);

      // Ad hoc step: inserted, edited as a new revision, and counted by completion.
      var adhoc = await AuditFieldworkService.InsertAdHocProcedureAsync(db, w.Auditor, new(w.EngagementId, "Inspect unusual year-end credit note",
        "Obtain and inspect the December credit note to the related party.", "Unusual related-party credit note", "Revenue", null));
      Assert.True(adhoc.Succeeded, adhoc.Message);
      Assert.True((await AuditFieldworkService.EditAdHocProcedureAsync(db, w.Auditor, adhoc.Value, "Inspect the credit note and its approval.")).Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditFieldworkService.EditAdHocProcedureAsync(db, w.Auditor, revenueProcedure, "Rewrite controlled wording")).ErrorCode);
      Assert.Equal(2, await db.AdHocProcedureRevisions.CountAsync(x => x.ProcedureId == adhoc.Value));
      var completion = (await AuditFieldworkService.EvaluateCompletionAsync(db, w.Manager, w.EngagementId)).Value!;
      Assert.Contains("procedure:ADHOC-001:unreviewed", completion.Blockers);
      Assert.Contains(statements.ProfitOrLoss.Lines.Single(x => x.DestinationCode == "REVENUE").AuditArea, "Revenue");

      // Mandatory analytical review and going concern for an audit engagement.
      Assert.Contains("analytical-review:missing", completion.Blockers);
      Assert.Contains("going-concern:missing", completion.Blockers);
      db.GoingConcernAssessments.Add(new GoingConcernAssessment { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
        AssessmentDate = new DateOnly(2027, 2, 1), PeriodCoveredTo = new DateOnly(2027, 6, 30), ForecastReviewOutcome = "Reviewed", DisclosureAdequate = true,
        Conclusion = GoingConcernConclusions.NoMaterialUncertainty, Rationale = "Cash flow forecast reviewed", Currency = "QAR", RecordedByUserId = w.Senior.Id,
        ReviewedByUserId = w.Reviewer.Id, RecordedAt = now, ReviewedAt = now });
      db.AnalyticalReviewVarianceInvestigations.Add(new AnalyticalReviewVarianceInvestigation { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId,
        EngagementId = w.EngagementId, AccountArea = "Revenue", PeriodReference = "FY2026", ExpectedAmount = 1400m, ActualAmount = 1500m, DifferenceAmount = 100m,
        InvestigationThreshold = 200m, ExceedsThreshold = false, Conclusion = VarianceInvestigationConclusions.Explained, Currency = "QAR", RecordedByUserId = w.Senior.Id,
        ReviewedByUserId = w.Reviewer.Id, RecordedAt = now, ReviewedAt = now });
      await db.SaveChangesAsync();
      var after = (await AuditFieldworkService.EvaluateCompletionAsync(db, w.Manager, w.EngagementId)).Value!;
      Assert.DoesNotContain("analytical-review:missing", after.Blockers);
      Assert.Contains("going-concern:horizon-under-12-months", after.Blockers); // only six months past the 2026 year end
    }
  }

  [Fact]
  public async Task ClientEvidence_LinksOnlyTheExactCurrentReceivedVersionOfThisEngagement()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var harness = await PbcSeed.CreateTransferHarnessAsync(pg);
    Assert.True(await harness.Worker.ProcessNextAsync());
    var f = harness.Fixture;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    Guid procedureId, olderId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var received = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == harness.Staged.UploadIntentId);
      Assert.Equal(PbcUploadStates.Received, received.State);
      var versionId = Guid.NewGuid();
      db.AuditProgramVersions.Add(new AuditProgramVersion { Id = versionId, FirmId = f.FirmId, Version = "2026.1", SourceHash = Hashing.Sha256Hex("p"),
        Status = AuditProgramStatuses.Published, CreatedByUserId = f.Staff.Id, ApprovedByUserId = f.Reviewer.Id, CreatedAt = DateTimeOffset.UtcNow, ApprovedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      var programId = Guid.NewGuid();
      db.EngagementAuditPrograms.Add(new EngagementAuditProgram { Id = programId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ProgramVersionId = versionId, AdoptedByUserId = f.Staff.Id, AdoptedAt = DateTimeOffset.UtcNow });
      var procedure = new AuditProcedure { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, EngagementProgramId = programId,
        SourceProcedureId = "CSH-02", SourceSectionTitle = "Cash", Title = "Agree bank statements", ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
        Status = AuditProcedureStatuses.Planned, CreatedAt = DateTimeOffset.UtcNow };
      db.AuditProcedures.Add(procedure);
      await db.SaveChangesAsync();
      procedureId = procedure.Id;
      // An earlier received version of the same file on the same request (same transfer evidence, earlier timestamp).
      olderId = Guid.NewGuid();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var received = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == harness.Staged.UploadIntentId);
      var older = received.GetType().GetProperties().Aggregate(new PbcUploadIntent(), (copy, p) => { if (p.CanWrite) p.SetValue(copy, p.GetValue(received)); return copy; });
      older.Id = olderId;
      older.CreatedAt = received.CreatedAt.AddMinutes(-30);
      older.ExpiresAt = older.CreatedAt.AddHours(24);
      db.PbcUploadIntents.Add(older);
      await db.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var candidates = await AuditFieldworkService.EvidenceCandidatesAsync(db, staff, f.EngagementId);
      Assert.True(candidates.Single(x => x.UploadIntentId == olderId).Superseded);
      Assert.False(candidates.Single(x => x.UploadIntentId == harness.Staged.UploadIntentId).Superseded);
      Assert.Equal(ErrorCodes.GenerationStale, (await AuditFieldworkService.LinkClientEvidenceAsync(db, staff, procedureId, olderId, null)).ErrorCode);
      var link = await AuditFieldworkService.LinkClientEvidenceAsync(db, staff, procedureId, harness.Staged.UploadIntentId, "Bank statements as received");
      Assert.True(link.Succeeded, link.Message);
      Assert.Equal(harness.Staged.DeclaredSha256Hex, (await AuditFieldworkService.ProcedureEvidenceAsync(db, staff, procedureId)).Single().ContentSha256);
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditFieldworkService.LinkClientEvidenceAsync(db, staff, procedureId, Guid.NewGuid(), null)).ErrorCode);
      // The client cannot use the auditor's evidence picker.
      Assert.Empty(await AuditFieldworkService.EvidenceCandidatesAsync(db, PbcSeed.Actor(f.Client, "ClientUser"), f.EngagementId));
    }
    // Another engagement's procedure cannot link this client's upload.
    var sibling = await SiblingClientSeed.SeedAsync(pg, f.FirmId, "SIB");
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditFieldworkService.LinkClientEvidenceAsync(db, staff, procedureId, sibling.UploadIntentId, null)).ErrorCode);
  }
}
