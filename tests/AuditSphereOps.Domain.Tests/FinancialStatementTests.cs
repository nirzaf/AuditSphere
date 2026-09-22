using System.IO.Compression;
using System.Xml.Linq;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class FinancialStatementTests
{
  private sealed record Fixture(
    Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId, Guid PeriodId, Guid BookId,
    AppUser Preparer, AppUser Reviewer);

  private sealed class TestDbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }

  [Fact]
  public void OfficeArtifacts_AreDeterministicFormulaFreeAndMacroFree()
  {
    const string canonical = "Package: test\nDisclosure: =2+2\nReference: +SUM(A1:A2)\n";
    foreach (var version in new[] { FinancialPackageArtifactVersions.Workbook, FinancialPackageArtifactVersions.Word })
    {
      var first = FinancialPackageOfficeRenderer.Render(version, canonical);
      var second = FinancialPackageOfficeRenderer.Render(version, canonical);
      Assert.Equal(first.ArtifactBytes, second.ArtifactBytes);
      Assert.Equal(first.ArtifactSha256Hex, Hashing.Sha256Hex(first.ArtifactBytes));

      using var stream = new MemoryStream(first.ArtifactBytes);
      using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
      Assert.DoesNotContain(archive.Entries, x =>
        x.FullName.Contains("externalLinks", StringComparison.OrdinalIgnoreCase) ||
        x.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
      var xml = archive.Entries.Where(x => x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        .Select(x =>
        {
          using var reader = x.Open();
          return XDocument.Load(reader);
        }).ToList();
      Assert.DoesNotContain(xml.SelectMany(x => x.Descendants()), x => x.Name.LocalName == "f");
      var text = string.Concat(xml.SelectMany(x => x.DescendantNodes().OfType<XText>()).Select(x => x.Value));
      Assert.Contains("=2+2", text, StringComparison.Ordinal);
      Assert.Contains("+SUM(A1:A2)", text, StringComparison.Ordinal);
    }
  }

  [Fact]
  public async Task MappingPlanAndPackage_AreScopedDeterministicAndReviewGated()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var incomplete = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping")
        ]));
      Assert.False(incomplete.Succeeded);
      Assert.Equal("mapping.incomplete", incomplete.ErrorCode);

      var sectionMismatch = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "LIABILITIES", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
        ]));
      Assert.False(sectionMismatch.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, sectionMismatch.ErrorCode);
      Assert.Contains("statement sections", sectionMismatch.Message!, StringComparison.OrdinalIgnoreCase);
    }

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
        ]));
      Assert.True(created.Succeeded);
      mappingId = created.Value;
      db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Preparer, "AccountingReviewer"));
      await db.SaveChangesAsync();
      var selfApproval = await FinancialStatementService.ApproveMappingAsync(db, preparer, mappingId, 1);
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, selfApproval.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-001", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-001", 1, ReflectionStates.NotApplicable, string.Empty)).Succeeded);
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-001", 1)])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1");
    FinancialPackageBuildResult first;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded);
      first = built.Value!;
      Assert.Equal(AccountingPackageStates.PackageReviewRequired, first.Status);
      Assert.Equal(100m, first.StatementTotals["ASSETS"]);
      Assert.Equal(-100m, first.StatementTotals["INCOME"]);
      var package = await db.FinancialPackages.SingleAsync(x => x.Id == first.PackageId);
      Assert.Equal(fixture.PeriodId, package.PeriodId);
      Assert.Equal(fixture.BookId, package.BookId);
      Assert.Equal("STATUTORY", package.Basis);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Reviewer, "Reviewer"));
      var snapshotId = await db.AdjustedTrialBalanceSnapshots.Where(x => x.AdjustmentPlanId == planId).Select(x => x.Id).SingleAsync();
      var journal = await db.AdjustmentJournals.SingleAsync(x => x.JournalNumber == "AJ-001" && x.Revision == 1);
      var reflectionId = await db.JournalSourceReconciliations.Where(x => x.BaseDatasetId == fixture.DatasetId && x.LogicalJournalNumber == "AJ-001")
        .Select(x => x.Id).SingleAsync();
      var differenceId = Guid.NewGuid();
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = differenceId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        AccountArea = "Revenue", DifferenceType = "KNOWN", Description = "Journal impact classification", Amount = -10m,
        Currency = "QAR", CreatedByUserId = fixture.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var linked = await AuditFieldworkService.LinkDifferenceToJournalAsync(db,
        new ActorContext(fixture.Reviewer.Id, fixture.FirmId, fixture.Reviewer.SessionEpoch, ["Reviewer"]),
        new LinkDifferenceToJournalRequest(differenceId, journal.Id, journal.Revision, reflectionId, snapshotId,
          AuditDifferenceCorrectionStates.Agreed));
      Assert.True(linked.Succeeded, linked.Message);
      var saved = await db.AuditDifferences.SingleAsync(x => x.Id == differenceId);
      using var impact = System.Text.Json.JsonDocument.Parse(saved.JournalImpactJson!);
      var classification = impact.RootElement.GetProperty("Classification");
      Assert.Equal("journal-impact.v2", impact.RootElement.GetProperty("Schema").GetString());
      Assert.Equal("MAPPED", classification.GetProperty("Status").GetString());
      Assert.Equal(-10m, classification.GetProperty("ProfitEffect").GetDecimal());
      Assert.Equal(0m, classification.GetProperty("EquityEffect").GetDecimal());
      Assert.Equal("INCOME", classification.GetProperty("StatementEffects")[1].GetProperty("StatementSection").GetString());
      Assert.Equal("UNSPECIFIED", classification.GetProperty("DisclosureEffects")[0].GetProperty("DisclosureArea").GetString());
      Assert.Equal(Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(saved.JournalImpactJson!)), saved.JournalImpactHash);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var periodMismatch = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        request with { TemplateVersion = "template-period-mismatch", PeriodStart = "2026-02-01" });
      Assert.False(periodMismatch.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, periodMismatch.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var repeat = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(repeat.Succeeded);
      Assert.Equal(first.PackageId, repeat.Value!.PackageId);
      Assert.Equal(first.CalculationHash, repeat.Value.CalculationHash);
      Assert.Equal(2, await db.AdjustedTrialBalanceRows.CountAsync());
      var checks = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == first.PackageId).ToListAsync();
      Assert.Contains(checks, x => x.Code == "SUPPLEMENTARY_INFORMATION" && !x.Passed);
    }

    var durableRequest = request with { TemplateVersion = "template-durable-v1" };
    var operationFactory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var operationStore = new PostgresOperationStore(operationFactory);
    var operationHandler = new FinancialPackageBuildHandler();
    var workerOptions = new WorkerOptions(fixture.FirmId, "Test");
    Guid operationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await FinancialStatementService.EnqueueFinancialPackageBuildAsync(
        db, preparer, durableRequest, operationStore, operationHandler);
      Assert.True(queued.Succeeded, queued.Message);
      operationId = queued.Value;
    }
    var worker = new WorkerHost(
      new OperationDispatcher(operationStore, new DurableOperationRegistry([operationHandler], workerOptions), workerOptions),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      var durablePackage = await db.FinancialPackages.SingleAsync(x => x.TemplateVersion == "template-durable-v1");
      Assert.Equal(durablePackage.Id.ToString("D"), operation.ResultIdentity);
      Assert.True(await db.OperationEvents.AnyAsync(x => x.OperationId == operationId && x.Kind == "financial.package-built.v1"));

      var retry = await FinancialStatementService.EnqueueFinancialPackageBuildAsync(
        db, preparer, durableRequest, operationStore, operationHandler);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(operationId, retry.Value);
    }

    var completeRequest = request with
    {
      TemplateVersion = "template-v2",
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m)],
        [new DisclosureInput("CASH_POLICY", "Cash and cash equivalents are presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "No commitments were identified in the supplied management information.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-schedule-1")],
        NoteLines: [new NoteLineInput("CASH_NOTE", "CASH", 100m, "note-schedule-1")])
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var complete = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, completeRequest);
      Assert.True(complete.Succeeded);
      Assert.Equal(AccountingPackageStates.PackageValidated, complete.Value!.Status);
      Assert.NotEqual(first.CalculationHash, complete.Value.CalculationHash);
      Assert.Equal(1, await db.FinancialPackageCashFlowLines.CountAsync(x => x.FinancialPackageId == complete.Value.PackageId));
      Assert.Equal(2, await db.FinancialPackageDisclosures.CountAsync(x => x.FinancialPackageId == complete.Value.PackageId));
      Assert.Equal(1, await db.FinancialPackageEquityLines.CountAsync(x => x.FinancialPackageId == complete.Value.PackageId));
      Assert.Equal(1, await db.FinancialPackageNoteLines.CountAsync(x => x.FinancialPackageId == complete.Value.PackageId));
      var historicalPackage = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == first.PackageId);
      Assert.Equal(request.TemplateVersion, historicalPackage.TemplateVersion);
      Assert.Equal(FinancialStatementCalculator.CalculationEngineVersion, historicalPackage.CalculationEngineVersion);
      Assert.Equal(first.CalculationHash, historicalPackage.CalculationHash);
      var currentPackage = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == complete.Value.PackageId);
      Assert.Equal(completeRequest.TemplateVersion, currentPackage.TemplateVersion);
      Assert.Equal(FinancialStatementCalculator.CalculationEngineVersion, currentPackage.CalculationEngineVersion);
      Assert.NotEqual(historicalPackage.Id, complete.Value.PackageId);
      var checks = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == complete.Value.PackageId).ToListAsync();
      Assert.All(checks.Where(x => x.Code is "CASH_FLOW_RECONCILED" or "DISCLOSURES_COMPLETE" or "SUPPLEMENTARY_INFORMATION"), x => Assert.True(x.Passed));
      Assert.Contains(checks, x => x.Code == "STATEMENT_CROSS_CAST" && x.Passed);
      Assert.Contains(checks, x => x.Code == "ACCOUNTING_EQUATION" && x.Passed);
      Assert.Contains(checks, x => x.Code == "EQUITY_ROLLFORWARD" && x.Passed);
      Assert.Contains(checks, x => x.Code == "EQUITY_PROFIT" && x.Passed);
      Assert.Contains(checks, x => x.Code == "COMPARATIVE_CONSISTENCY" && x.Passed);
      Assert.Contains(checks, x => x.Code == "NOTE_TO_FACE_TOTALS" && x.Passed);

      var cashFlowLineId = await db.FinancialPackageCashFlowLines.Where(x => x.FinancialPackageId == complete.Value.PackageId).Select(x => x.Id).SingleAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_cash_flow_lines SET amount = amount + 1 WHERE id = {cashFlowLineId}"));
      var equityLineId = await db.FinancialPackageEquityLines.Where(x => x.FinancialPackageId == complete.Value.PackageId).Select(x => x.Id).SingleAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_equity_lines SET closing_amount = closing_amount + 1 WHERE id = {equityLineId}"));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var invalidSupplement = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        completeRequest with { TemplateVersion = "template-v3", SupplementaryInformation = completeRequest.SupplementaryInformation! with { CashEnding = 200m } });
      Assert.False(invalidSupplement.Succeeded);
      Assert.Equal("package.supplementary.invalid", invalidSupplement.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var lineId = await db.FinancialPackageLines.Select(x => x.Id).FirstAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_lines SET amount = amount + 1 WHERE id = {lineId}"));
    }
  }

  [Fact]
  public async Task AdjustmentInstructions_RequireEvidenceAndCarryExactSourceLineage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    Guid journalId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == fixture.DatasetId);
      dataset.RawFileSha256Hex = new string('a', 64);
      dataset.NormalizedDatasetDigest = new string('b', 64);
      dataset.ImportProfileVersion = "trial-balance.v1";
      dataset.LegalEntityKey = "TEST-ENTITY";
      await db.SaveChangesAsync();

      var created = await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-EXPORT-001", [("=1000", 10m, 0m), ("4000", 0m, 10m)]);
      Assert.True(created.Succeeded, created.Message);
      journalId = created.Value;

      var blocked = await AdjustmentJournalService.BuildInstructionExportAsync(db, reviewer, journalId);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);

      var decision = await AdjustmentJournalService.RecordManagementDecisionAsync(db, reviewer,
        new AdjustmentJournalService.ManagementDecisionRequest(journalId, ManagementDecisionStates.Accepted,
          ManagementDecisionEvidenceModes.Offline, "review-note-1"));
      Assert.True(decision.Succeeded, decision.Message);

      var exported = await AdjustmentJournalService.BuildInstructionExportAsync(db, reviewer, journalId);
      Assert.True(exported.Succeeded, exported.Message);
      Assert.Equal("auditsphere-adjustment-AJ-EXPORT-001-r1.csv", exported.Value!.FileName);
      Assert.Contains("NOT_PROOF_OF_EXTERNAL_POSTING", exported.Value.Csv);
      Assert.Contains("source_raw_sha256", exported.Value.Csv);
      Assert.Contains("FY2026", exported.Value.Csv);
      Assert.Contains("\"'=1000\"", exported.Value.Csv);
      Assert.Contains("review-note-1", exported.Value.Csv);
    }
  }

  [Fact]
  public async Task MappingApplicability_BindsApprovedClientChartVersion()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var allocations = new[]
    {
      new MappingAllocationInput("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
      new MappingAllocationInput("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
    };
    var chartId = Guid.CreateVersion7();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.ClientChartVersions.Add(new ClientChartVersion
      {
        Id = chartId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, Version = 1,
        SourceScope = "LEDGER-2026", Status = AccountingWorkflowStates.Approved,
        EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 12, 31),
        CreatedByUserId = fixture.Preparer.Id, PublishedByUserId = fixture.Reviewer.Id,
        PublishedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var missingApplicability = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", allocations));
      Assert.False(missingApplicability.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, missingApplicability.ErrorCode);

      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", allocations, chartId));
      Assert.True(created.Succeeded, created.Message);
      Assert.Equal(chartId, await db.MappingVersions.Where(x => x.Id == created.Value).Select(x => x.ClientChartVersionId).SingleAsync());
    }
  }

  [Fact]
  public async Task ZeroAdjustmentPlan_ProducesSourceEquivalentPackage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      mappingId = (await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")]))).Value;
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);
    }

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId, [])).Value;
    FinalizedPlan finalizedPlan;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var finalized = await AdjustmentPlanService.FinalizeAsync(db, preparer, planId);
      Assert.True(finalized.Succeeded);
      finalizedPlan = finalized.Value!;
      Assert.Equal(0, finalizedPlan.AppliedJournalCount);
      Assert.Equal(100m, finalizedPlan.Balances["1000"]);
      Assert.Equal(-100m, finalizedPlan.Balances["4000"]);
    }

    var legacyPackageId = Guid.CreateVersion7();
    var legacyPackageHash = Hashing.Sha256Hex("legacy-package-v0");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var snapshotId = Guid.CreateVersion7();
      db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
      {
        Id = snapshotId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        BaseDatasetId = fixture.DatasetId, AdjustmentPlanId = planId, Currency = "QAR",
        ResultHash = finalizedPlan.ResultHash, CreatedByUserId = fixture.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.AdjustedTrialBalanceRows.AddRange(finalizedPlan.Balances.Select(x => new AdjustedTrialBalanceRow
      {
        Id = Guid.CreateVersion7(), SnapshotId = snapshotId, AccountCode = x.Key, Amount = x.Value, Currency = "QAR"
      }));
      db.FinancialPackages.Add(new FinancialPackage
      {
        Id = legacyPackageId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        AdjustedDatasetId = snapshotId, MappingVersionId = mappingId, AdjustmentPlanId = planId,
        PeriodId = fixture.PeriodId, BookId = fixture.BookId, Basis = "STATUTORY", Framework = "IFRS",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", TaxonomyVersion = "tax-v1",
        TemplateVersion = "legacy-template-v0", CalculationEngineVersion = "legacy-engine-v0", CalculationHash = legacyPackageHash,
        Currency = "QAR", Revision = 4, Generation = 9, Status = AccountingPackageStates.PackageReviewRequired,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    await using var packageDb = new AuditSphereDbContext(pg.Options);
    var package = await FinancialStatementService.BuildFinancialPackageAsync(packageDb, preparer,
      new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1"));
    Assert.True(package.Succeeded);
    Assert.Equal(100m, package.Value!.StatementTotals["ASSETS"]);
    Assert.Equal(-100m, package.Value.StatementTotals["INCOME"]);
    Assert.Equal(2, await packageDb.AdjustedTrialBalanceRows.CountAsync());
    var legacy = await packageDb.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == legacyPackageId);
    Assert.Equal("legacy-template-v0", legacy.TemplateVersion);
    Assert.Equal("legacy-engine-v0", legacy.CalculationEngineVersion);
    Assert.Equal(legacyPackageHash, legacy.CalculationHash);
    var canonical = await packageDb.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == package.Value.PackageId);
    Assert.Equal("template-v1", canonical.TemplateVersion);
    Assert.Equal(FinancialStatementCalculator.CalculationEngineVersion, canonical.CalculationEngineVersion);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void PackageRounding_ConservesEachSourceBalanceAndRecordsResidual()
  {
    var lines = FinancialStatementCalculator.BuildPackageLines(
      new Dictionary<string, decimal> { ["1000"] = 0.01m },
      [new("1000", "A", "ASSETS", 0.333333m, "split"),
       new("1000", "B", "ASSETS", 0.333333m, "split"),
       new("1000", "C", "ASSETS", 0.333334m, "split")], "QAR");

    Assert.Equal(0.01m, lines.Sum(x => x.Amount));
    Assert.Equal(0.003334m, lines.Single(x => x.DestinationCode == "C").Amount);
    Assert.Equal(0.000001m, lines.Sum(x => x.RoundingResidual));
  }

  [Fact]
  public async Task FinancialPackage_RendersDeterministicArtifact_WithVerifiableSha256()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(
          fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31",
          [new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
           new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")]));
      mappingId = created.Value;
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);
    }

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-001", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-001", 1, ReflectionStates.NotApplicable, string.Empty)).Succeeded);
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-001", 1)])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }

    var request = new BuildFinancialPackageRequest(
      planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1",
      new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash collections", 100m)],
        [new DisclosureInput("NOTE_1", "Summary of significant accounting policies."),
         new DisclosureInput("NOTE_2", string.Empty, NotApplicable: true, Rationale: "No discontinued operations.")]));

    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded);
      packageId = built.Value!.PackageId;
    }

    var expectedArtifactSha = string.Empty;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var artifact1 = await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId);
      Assert.True(artifact1.Succeeded);
      Assert.NotNull(artifact1.Value);
      expectedArtifactSha = artifact1.Value.ArtifactSha256Hex;
      Assert.NotEmpty(artifact1.Value.ArtifactBytes);
      var storedArtifact = await db.FinancialPackageArtifacts.SingleAsync(x => x.FinancialPackageId == packageId);
      Assert.Equal(storedArtifact.Id, artifact1.Value.ArtifactId);
      Assert.Equal(expectedArtifactSha, storedArtifact.ArtifactSha256Hex);
      Assert.Equal(artifact1.Value.ArtifactBytes, storedArtifact.ArtifactBytes);
      Assert.Equal(artifact1.Value.ArtifactSha256Hex, Hashing.Sha256Hex(artifact1.Value.ArtifactBytes));
      Assert.Contains("=== AUDITSPHEREOPS FINANCIAL STATEMENT PACKAGE ===", artifact1.Value.RenderedText);
      Assert.Contains("Mapping Version ID:", artifact1.Value.RenderedText);
      Assert.Contains("NOTE_1: Summary of significant accounting policies.", artifact1.Value.RenderedText);
      Assert.Contains("[NOT APPLICABLE: No discontinued operations.]", artifact1.Value.RenderedText);

      // Repeat render produces byte-for-byte identical output and digest
      var artifact2 = await FinancialStatementService.RenderPackageArtifactAsync(db, reviewer, packageId);
      Assert.True(artifact2.Succeeded);
      Assert.Equal(artifact1.Value.ArtifactSha256Hex, artifact2.Value!.ArtifactSha256Hex);
      Assert.Equal(artifact1.Value.ArtifactBytes, artifact2.Value.ArtifactBytes);

      foreach (var version in new[] { FinancialPackageArtifactVersions.Workbook, FinancialPackageArtifactVersions.Word })
      {
        var office1 = await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, preparer, packageId, version);
        var office2 = await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, reviewer, packageId, version);
        Assert.True(office1.Succeeded, office1.Message);
        Assert.True(office2.Succeeded, office2.Message);
        Assert.Equal(office1.Value!.ArtifactId, office2.Value!.ArtifactId);
        Assert.Equal(office1.Value.ArtifactBytes, office2.Value.ArtifactBytes);
        Assert.Equal(office1.Value.ArtifactSha256Hex, Hashing.Sha256Hex(office1.Value.ArtifactBytes));
      }
      Assert.Equal(3, await db.FinancialPackageArtifacts.CountAsync(x => x.FinancialPackageId == packageId));

      // Denied to client without accounting roles
      var clientUser = User(fixture.FirmId);
      db.Users.Add(clientUser);
      db.RoleGrants.Add(Grant(fixture.FirmId, clientUser, "ClientUser"));
      await db.SaveChangesAsync();
      var clientActor = Actor(clientUser, "ClientUser");

      var denied = await FinancialStatementService.RenderPackageArtifactAsync(db, clientActor, packageId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
      var deniedOffice = await FinancialStatementService.RenderPackageOfficeArtifactAsync(
        db, clientActor, packageId, FinancialPackageArtifactVersions.Workbook);
      Assert.False(deniedOffice.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, deniedOffice.ErrorCode);
    }

    var renderFactory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var renderStore = new PostgresOperationStore(renderFactory);
    var renderHandler = new FinancialPackageRenderHandler();
    var renderOptions = new WorkerOptions(fixture.FirmId, "Test");
    Guid renderOperationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await FinancialStatementService.EnqueueFinancialPackageRenderAsync(
        db, preparer, packageId, renderStore, renderHandler);
      Assert.True(queued.Succeeded, queued.Message);
      renderOperationId = queued.Value;
    }
    var renderWorker = new WorkerHost(
      new OperationDispatcher(renderStore, new DurableOperationRegistry([renderHandler], renderOptions), renderOptions),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance);
    Assert.True(await renderWorker.ProcessNextAsync());
    Assert.False(await renderWorker.ProcessNextAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == renderOperationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      Assert.Equal(packageId.ToString("D"), operation.ResultIdentity);
      Assert.Equal(expectedArtifactSha, operation.ResultDigest);
      Assert.True(await db.OperationEvents.AnyAsync(x => x.OperationId == renderOperationId && x.Kind == "financial.package-rendered.v1"));

      var retry = await FinancialStatementService.EnqueueFinancialPackageRenderAsync(
        db, preparer, packageId, renderStore, renderHandler);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(renderOperationId, retry.Value);
    }
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var preparer = User(firmId);
    var reviewer = User(firmId);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "FINANCIAL STATEMENT TEST",
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"),
      Grant(firmId, reviewer, "AccountingReviewer"));
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "FY2026",
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
      Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
      CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientReportingBooks.Add(new ClientReportingBook
    {
      Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId,
      Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
      Status = AccountingWorkflowStates.Active, CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    var taxonomyId = Guid.NewGuid();
    db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
    {
      Id = taxonomyId, FirmId = firmId, Code = "tax-v1", Framework = "IFRS", Name = "Test taxonomy",
      Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
      CreatedByUserId = preparer.Id, ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.ReportingTaxonomyNodes.AddRange(
      new ReportingTaxonomyNode
      {
        Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "CASH", Name = "Cash",
        StatementSection = "ASSETS", DisplaySign = "SIGNED", NormalBalance = "DEBIT", IsPosting = true,
        Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
      },
      new ReportingTaxonomyNode
      {
        Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "REVENUE", Name = "Revenue",
        StatementSection = "INCOME", DisplaySign = "SIGNED", NormalBalance = "CREDIT", IsPosting = true,
        Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
      });
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      PeriodId = periodId, BookId = bookId, Basis = "STATUTORY",
      SourceKind = "Raw", Revision = 1, Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.Id
    });
    db.TrialBalanceRows.AddRange(
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000",
        AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST"
      },
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
        AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST"
      });
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, datasetId, periodId, bookId, preparer, reviewer);
  }

  private static AppUser User(Guid firmId) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId,
    Subject = "sub-" + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = $"{Guid.NewGuid():N}@example.test",
    DisplayName = "Synthetic", CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
