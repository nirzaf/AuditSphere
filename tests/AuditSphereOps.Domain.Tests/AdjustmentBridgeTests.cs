using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TbWorker = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

// NT-09/10, AT-07/10, §17.4, Appendix D: CSV import → validation → AJ post →
// re-upload source bridge. The reflected journal must never be re-applied and the
// profit must stay 175,000 — not drop to 170,000.
public sealed class TrialBalanceCsvParserTests
{
  [Fact]
  [Trait("Profile", "Unit")]
  public void AppendixD_Parses14Rows_WithD2ControlTotals()
  {
    var parsed = TrialBalanceCsvImporter.Parse(AdjustmentBridgeTests.TrialBalanceV1Csv);
    Assert.Equal(14, parsed.Rows.Count);
    Assert.Equal("QAR", parsed.Currency);
    var calc = TrialBalanceCalculator.Parse(parsed.Rows);
    Assert.True(calc.Balanced);
    Assert.Equal(0m, calc.SignedSum);
    Assert.Equal(1820000m, calc.TotalDebits);
    Assert.Equal(1820000m, calc.TotalCreditsAbs);
    // Leading zeros survive as strings, never as numbers.
    Assert.Contains(parsed.Rows, r => r.AccountCode == "100101");
    Assert.NotEqual(parsed.RawFileSha256Hex, parsed.NormalizedDatasetDigest);
    Assert.Equal(parsed.NormalizedDatasetDigest, parsed.SourceHash);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void SourceIdentity_ChangesWhenRawEvidenceChanges()
  {
    var renamed = AdjustmentBridgeTests.TrialBalanceV1Csv.Replace("Bank,150000", "Operating Bank,150000");
    var original = TrialBalanceCsvImporter.Parse(AdjustmentBridgeTests.TrialBalanceV1Csv);
    var changed = TrialBalanceCsvImporter.Parse(renamed);

    Assert.Equal(original.Rows.Select(x => x.Amount), changed.Rows.Select(x => x.Amount));
    Assert.NotEqual(original.RawFileSha256Hex, changed.RawFileSha256Hex);
    Assert.NotEqual(original.NormalizedDatasetDigest, changed.NormalizedDatasetDigest);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void DebitCreditProfile_PreservesSourceSidesAndComputesSignedBalance()
  {
    var csv = "AccountCode,AccountName,Debit,Credit,Currency,Entity,MappingCode\n" +
      "100101,Bank,125.50,0,QAR,DEMO,CA_CASH\n" +
      "400100,Revenue,0,125.50,QAR,DEMO,PL_REVENUE\n";
    var parsed = TrialBalanceCsvImporter.Parse(csv, TrialBalanceImportProfile.DebitCreditV1);

    Assert.Equal(TrialBalanceLayouts.DebitCredit, parsed.SourceLayout);
    Assert.Equal("tb-debit-credit.v1", parsed.ImportProfileVersion);
    Assert.Equal(125.50m, parsed.Rows[0].Amount);
    Assert.Equal(125.50m, parsed.Rows[0].SourceDebit);
    Assert.Equal(0m, parsed.Rows[0].SourceCredit);
    Assert.Equal(-125.50m, parsed.Rows[1].Amount);
  }

  [Theory]
  [Trait("Profile", "Unit")]
  [InlineData("AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\n")] // header only
  [InlineData("")] // empty
  [InlineData("AccountCode,AccountName,NetClosingBalance,Currency,Entity\n100101,Bank,150000,QAR,DEMO")] // missing column
  public void MalformedFiles_AreRejected(string csv)
  {
    Assert.Throws<InvalidOperationException>(() => TrialBalanceCsvImporter.Parse(csv));
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void MixedCurrency_DuplicateAccount_ExcessScale_Formula_AreRejected()
  {
    static string Row(string code, string amount, string currency = "QAR", string entity = "DEMO") =>
      $"{code},Name,{amount},{currency},{entity},MAP";
    const string header = "AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode";
    Assert.Throws<InvalidOperationException>(() => TrialBalanceCsvImporter.Parse(
      $"{header}\n{Row("100101", "100", "QAR")}\n{Row("110100", "-100", "USD")}"));
    Assert.Throws<InvalidOperationException>(() => TrialBalanceCsvImporter.Parse(
      $"{header}\n{Row("100101", "100")}\n{Row("100101", "-100")}"));
    Assert.Throws<InvalidOperationException>(() => TrialBalanceCsvImporter.Parse(
      $"{header}\n{Row("100101", "100.1234567")}\n{Row("110100", "-100.1234567")}"));
    Assert.Throws<InvalidOperationException>(() => TrialBalanceCsvImporter.Parse(
      $"{header}\n=100101,Name,100,QAR,DEMO,MAP\n110100,Name,-100,QAR,DEMO,MAP"));
    Assert.Throws<InvalidOperationException>(() => TrialBalanceCsvImporter.Parse(
      $"{header}\n100101,Name,NaN,QAR,DEMO,MAP\n110100,Name,-100,QAR,DEMO,MAP"));
  }
}

[Trait("Profile", "Database")]
public sealed class AdjustmentBridgeTests
{
  public const string TrialBalanceV1Csv = """
    AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode
    100101,Bank,150000,QAR,DEMO,CA_CASH
    110100,Trade Receivables,300000,QAR,DEMO,CA_AR
    120100,Inventory,200000,QAR,DEMO,CA_INVENTORY
    150100,Property Plant Equipment Cost,150000,QAR,DEMO,NCA_PPE_COST
    159100,Accumulated Depreciation,-50000,QAR,DEMO,NCA_PPE_ACCDEP
    200100,Trade Payables,-240000,QAR,DEMO,CL_AP
    220100,Loan,-30000,QAR,DEMO,NCL_LOAN
    300100,Share Capital,-250000,QAR,DEMO,EQ_CAPITAL
    310100,Opening Retained Earnings,-50000,QAR,DEMO,EQ_RETAINED
    400100,Revenue,-1200000,QAR,DEMO,PL_REVENUE
    500100,Cost of Sales,800000,QAR,DEMO,PL_COS
    510100,Payroll Expense,190000,QAR,DEMO,PL_PAYROLL
    520100,Depreciation Expense,20000,QAR,DEMO,PL_DEPRECIATION
    530100,Finance Costs,10000,QAR,DEMO,PL_FINANCE
    """;

  // Replacement source already containing AJ-001 (25,000 and -55,000).
  public const string TrialBalanceV2Csv = """
    AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode
    100101,Bank,150000,QAR,DEMO,CA_CASH
    110100,Trade Receivables,300000,QAR,DEMO,CA_AR
    120100,Inventory,200000,QAR,DEMO,CA_INVENTORY
    150100,Property Plant Equipment Cost,150000,QAR,DEMO,NCA_PPE_COST
    159100,Accumulated Depreciation,-55000,QAR,DEMO,NCA_PPE_ACCDEP
    200100,Trade Payables,-240000,QAR,DEMO,CL_AP
    220100,Loan,-30000,QAR,DEMO,NCL_LOAN
    300100,Share Capital,-250000,QAR,DEMO,EQ_CAPITAL
    310100,Opening Retained Earnings,-50000,QAR,DEMO,EQ_RETAINED
    400100,Revenue,-1200000,QAR,DEMO,PL_REVENUE
    500100,Cost of Sales,800000,QAR,DEMO,PL_COS
    510100,Payroll Expense,190000,QAR,DEMO,PL_PAYROLL
    520100,Depreciation Expense,25000,QAR,DEMO,PL_DEPRECIATION
    530100,Finance Costs,10000,QAR,DEMO,PL_FINANCE
    """;

  private sealed record Scope(Guid FirmId, Guid ClientId, Guid EngagementId, Guid PeriodId, Guid BookId);
  private sealed record Users(AppUser Preparer, AppUser Reviewer);

  private static async Task<Scope> SeedScopeAsync(AuditSphereDbContext db, string name)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient
      { Id = clientId, FirmId = firmId, LegalName = name, CreatedAt = DateTimeOffset.UtcNow });
    db.Engagements.Add(new Engagement
      { Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
        ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
      { Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedAt = DateTimeOffset.UtcNow });
    db.ClientReportingBooks.Add(new ClientReportingBook
      { Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId, Code = "STAT",
        Basis = "STATUTORY", InclusionRule = "STATUTORY_ONLY", Currency = "QAR",
        Status = AccountingWorkflowStates.Active, CreatedAt = DateTimeOffset.UtcNow });
    db.FirmSafetyStates.Add(new() { Id = firmId });
    db.ClientSafetyStates.Add(new() { Id = clientId, FirmId = firmId });
    await db.SaveChangesAsync();
    return new Scope(firmId, clientId, engagementId, periodId, bookId);
  }

  private static async Task<AppUser> SeedUserAsync(AuditSphereDbContext db, Guid firmId)
  {
    var user = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId,
      Subject = "sub-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-" + Guid.NewGuid().ToString("N"),
      Email = $"u-{Guid.NewGuid():N}@example.test", DisplayName = "Synthetic",
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return user;
  }

  private static async Task GrantAsync(AuditSphereDbContext db, Scope scope, AppUser user, string role)
  {
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = user.Id, Role = role,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
    });
    await db.SaveChangesAsync();
  }

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static async Task ValidateAsync(PgTestSchema pg, Guid firmId)
  {
    var factory = new OperationContextFactory(new DbFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(firmId, "Test");
    var handler = new TrialBalanceValidationHandler();
    var discovery = new TrialBalanceDiscovery(factory, store, handler, options);
    var worker = new TbWorker(new OperationDispatcher(store, new([handler], options), options),
      [discovery], NullLogger<TbWorker>.Instance);
    while (await worker.ProcessNextAsync()) { }
  }

  private sealed class DbFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }

  private static decimal Profit(IReadOnlyDictionary<string, decimal> balances) =>
    -(balances["400100"] + balances["500100"] + balances["510100"] + balances["520100"] + balances["530100"]);

  private static async Task<(Scope Scope, Users Users)> SeedFirmWithStaffAsync(PgTestSchema pg)
  {
    Scope scope;
    AppUser preparer, reviewer;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      scope = await SeedScopeAsync(db, "BRIDGE CLIENT " + Guid.NewGuid().ToString("N")[..8]);
      preparer = await SeedUserAsync(db, scope.FirmId);
      reviewer = await SeedUserAsync(db, scope.FirmId);
      await GrantAsync(db, scope, preparer, "AccountingPreparer");
      await GrantAsync(db, scope, reviewer, "AccountingReviewer");
    }
    return (scope, new Users(preparer, reviewer));
  }

  private static Task<CommandResult<Guid>> ImportAsync(
    PgTestSchema pg, Users users, Scope scope, string csv, bool asPreparer = true)
  {
    var actor = Actor(asPreparer ? users.Preparer : users.Reviewer,
      asPreparer ? "AccountingPreparer" : "AccountingReviewer");
    return TrialBalanceImportService.ImportAsync(
      new AuditSphereDbContext(pg.Options), actor, scope.ClientId, scope.EngagementId, csv,
      new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"));
  }

  [Fact]
  public async Task Import_AppendixD_CreatesPendingDataset_DuplicateIsReusedNeverAppended()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);
    var first = await ImportAsync(pg, users, scope, TrialBalanceV1Csv);
    Assert.True(first.Succeeded);
    var repeat = await ImportAsync(pg, users, scope, TrialBalanceV1Csv);
    Assert.False(repeat.Succeeded);
    Assert.Equal("import.duplicate", repeat.ErrorCode);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await verify.TrialBalanceDatasets.CountAsync());
    Assert.Equal(14, await verify.TrialBalanceRows.CountAsync());
    var dataset = await verify.TrialBalanceDatasets.SingleAsync();
    Assert.Equal("Pending", dataset.ValidationStatus);
    Assert.Equal("Raw", dataset.SourceKind);
    Assert.Equal(1, dataset.Revision);
    Assert.NotEmpty(dataset.Sha256Hex);
    Assert.Equal(dataset.NormalizedDatasetDigest, dataset.Sha256Hex);
    Assert.NotEqual(dataset.RawFileSha256Hex, dataset.NormalizedDatasetDigest);
    Assert.Equal("DEMO", dataset.LegalEntityKey);
    Assert.Equal(scope.PeriodId, dataset.PeriodId);
    Assert.Equal(scope.BookId, dataset.BookId);
    Assert.Equal("STATUTORY", dataset.Basis);
  }

  [Fact]
  public async Task Import_RejectsContextOutsidePeriodBasis()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);

    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await TrialBalanceImportService.ImportAsync(db,
      Actor(users.Preparer, "AccountingPreparer"), scope.ClientId, scope.EngagementId,
      TrialBalanceV1Csv, new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "TAX"));

    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.ImportRejected, result.ErrorCode);
    Assert.Empty(await db.TrialBalanceDatasets.ToListAsync());
  }

  [Fact]
  public async Task MultiEntityBatch_SealsIndependentDatasetsWithOneSourceReceipt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);
    var csv = "AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\n" +
      "100101,Bank,100,QAR,ENTITY-A,CA_CASH\n" +
      "400100,Revenue,-100,QAR,ENTITY-A,PL_REVENUE\n" +
      "100101,Bank,200,QAR,ENTITY-B,CA_CASH\n" +
      "400100,Revenue,-200,QAR,ENTITY-B,PL_REVENUE\n";

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await TrialBalanceImportService.ImportBatchAsync(db, Actor(users.Preparer, "AccountingPreparer"),
        scope.ClientId, scope.EngagementId, csv, TrialBalanceImportProfile.SignedNetV1,
        new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"));
      Assert.True(result.Succeeded, result.Message);
      Assert.Equal(2, result.Value!.Count);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var batch = await db.TrialBalanceImportBatches.SingleAsync();
      Assert.Equal(TrialBalanceImportStates.Sealed, batch.Status);
      Assert.Equal(2, batch.EntityCount);
      Assert.Equal(scope.PeriodId, batch.PeriodId);
      Assert.Equal(scope.BookId, batch.BookId);
      Assert.Equal("STATUTORY", batch.Basis);
      var datasets = await db.TrialBalanceDatasets.OrderBy(x => x.LegalEntityKey).ToListAsync();
      Assert.Equal(["ENTITY-A", "ENTITY-B"], datasets.Select(x => x.LegalEntityKey));
      Assert.All(datasets, x =>
      {
        Assert.Equal(batch.Id, x.ImportBatchId);
        Assert.Equal(TrialBalanceImportStates.Sealed, x.ImportState);
        Assert.Equal(TrialBalanceLayouts.SignedNet, x.SourceLayout);
      });
      Assert.Equal(4, await db.TrialBalanceRows.CountAsync());

      var duplicate = await TrialBalanceImportService.ImportBatchAsync(db, Actor(users.Preparer, "AccountingPreparer"),
        scope.ClientId, scope.EngagementId, csv, TrialBalanceImportProfile.SignedNetV1,
        new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"));
      Assert.False(duplicate.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ImportDuplicate, duplicate.ErrorCode);
    }
  }

  [Fact]
  public async Task Import_RejectsMixedLegalEntitiesBeforePromotion()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);
    var mixed = TrialBalanceV1Csv.Replace("DEMO,CA_AR", "OTHER,CA_AR");

    var result = await ImportAsync(pg, users, scope, mixed);

    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.ImportRejected, result.ErrorCode);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await verify.TrialBalanceDatasets.ToListAsync());
    Assert.Empty(await verify.TrialBalanceRows.ToListAsync());
  }

  [Fact]
  public async Task FullCycle_ValidatePostReflect_ProfitStays175k()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);
    var preparer = Actor(users.Preparer, "AccountingPreparer");
    var reviewer = Actor(users.Reviewer, "AccountingReviewer");

    // Import TB-1 and validate through the real worker path.
    Guid base1;
    await using (var db = new AuditSphereDbContext(pg.Options))
      base1 = (await TrialBalanceImportService.ImportAsync(
        db, preparer, scope.ClientId, scope.EngagementId, TrialBalanceV1Csv,
        new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"))).Value;
    await ValidateAsync(pg, scope.FirmId);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Equal("Accepted", (await db.TrialBalanceDatasets.SingleAsync(d => d.Id == base1)).ValidationStatus);

    // AJ-001: depreciation 5,000 / accumulated depreciation 5,000.
    var lines = new List<(string, decimal, decimal)> { ("520100", 5000m, 0m), ("159100", 0m, 5000m) };
    Guid journal;
    await using (var db = new AuditSphereDbContext(pg.Options))
      journal = (await AdjustmentJournalService.CreateDraftAsync(
        db, preparer, base1, "AJ-001", lines)).Value;
    // A reviewer posts; the preparer can never post their own journal.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var selfPost = await AdjustmentJournalService.PostAsync(db, preparer, journal);
      Assert.False(selfPost.Succeeded);
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
    }

    // TB-1 has no reflection decision yet: planning blocks on UNKNOWN.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, base1,
        [new PlanLineInput("AJ-001", 1)])).Value;
      var blocked = await AdjustmentPlanService.FinalizeAsync(db, preparer, planId);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
    }

    // Reviewer confirms AJ-001 is NOT in the TB-1 source; the plan applies it once.
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, base1,
        "AJ-001", 1, ReflectionStates.NotReflected, string.Empty)).Succeeded);
    FinalizedPlan first;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, base1,
        [new PlanLineInput("AJ-001", 1)])).Value;
      first = (await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Value!;
    }
    Assert.Equal(1, first.AppliedJournalCount);
    Assert.Equal(175000m, Profit(first.Balances));
    Assert.Equal(95000m, first.Balances["150100"] + first.Balances["159100"]);

    // Replacement TB-2 already contains AJ-001. Reviewer verifies with evidence;
    // the new plan contributes zero additional adjustment.
    Guid base2;
    await using (var db = new AuditSphereDbContext(pg.Options))
      base2 = (await TrialBalanceImportService.ImportAsync(
        db, preparer, scope.ClientId, scope.EngagementId, TrialBalanceV2Csv,
        new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"))).Value;
    await ValidateAsync(pg, scope.FirmId);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(2, (await db.TrialBalanceDatasets.SingleAsync(d => d.Id == base2)).Revision);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, base2,
        "AJ-001", 1, ReflectionStates.Reflected, "client-ledger posting JE-2026-0841; lines 520100/159100 bridged")).Succeeded);
      var planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, base2,
        [new PlanLineInput("AJ-001", 1)])).Value;
      var second = (await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Value!;
      Assert.Equal(0, second.AppliedJournalCount);
      Assert.Equal(175000m, Profit(second.Balances)); // not 170,000
      Assert.Equal(95000m, second.Balances["150100"] + second.Balances["159100"]);
    }
  }

  [Fact]
  public async Task UnbalancedImport_ValidationRejects_OriginalRowsPreserved()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);
    var bad = TrialBalanceV1Csv.Replace("530100,Finance Costs,10000,", "530100,Finance Costs,9999,");
    Guid dataset;
    await using (var db = new AuditSphereDbContext(pg.Options))
      dataset = (await TrialBalanceImportService.ImportAsync(
        db, Actor(users.Preparer, "AccountingPreparer"),
        scope.ClientId, scope.EngagementId, bad,
        new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"))).Value;
    await ValidateAsync(pg, scope.FirmId);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var result = await verify.TrialBalanceDatasets.SingleAsync(d => d.Id == dataset);
    Assert.Equal("Rejected", result.ValidationStatus);
    Assert.False(result.Balanced);
    Assert.Equal(14, await verify.TrialBalanceRows.CountAsync(r => r.DatasetId == dataset));
  }

  [Fact]
  public async Task StalePlan_AfterReflectionChange_IsBlocked()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);
    var preparer = Actor(users.Preparer, "AccountingPreparer");
    var reviewer = Actor(users.Reviewer, "AccountingReviewer");
    Guid base1;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      base1 = (await TrialBalanceImportService.ImportAsync(
        db, preparer, scope.ClientId, scope.EngagementId, TrialBalanceV1Csv,
        new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"))).Value;
    }
    await ValidateAsync(pg, scope.FirmId);
    var lines = new List<(string, decimal, decimal)> { ("520100", 5000m, 0m), ("159100", 0m, 5000m) };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, base1, "AJ-001", lines)).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, base1,
        "AJ-001", 1, ReflectionStates.NotReflected, string.Empty)).Succeeded);
    }
    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, base1,
        [new PlanLineInput("AJ-001", 1)])).Value;
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, base1,
        "AJ-001", 1, ReflectionStates.Reflected, "late-arriving posting evidence JE-9")).Succeeded);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var stale = await AdjustmentPlanService.FinalizeAsync(db, preparer, planId);
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    }
  }

  [Fact]
  public async Task CrossScope_JournalAndImport_AreDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);
    Scope other;
    await using (var db = new AuditSphereDbContext(pg.Options))
      other = await SeedScopeAsync(db, "OTHER CLIENT " + Guid.NewGuid().ToString("N")[..8]);
    // Same firm but no assignment for the other client: import denied.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var denied = await TrialBalanceImportService.ImportAsync(db,
        Actor(users.Preparer, "AccountingPreparer"), other.ClientId, other.EngagementId, TrialBalanceV1Csv,
        new TrialBalanceImportContext(other.PeriodId, other.BookId, "STATUTORY"));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
    // A foreign actor cannot touch this engagement at all.
    AppUser foreign;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      foreign = await SeedUserAsync(db, Guid.NewGuid());
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = foreign.FirmId, UserId = foreign.Id, Role = "AccountingPreparer",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = foreign.Id
      });
      await db.SaveChangesAsync();
      var denied = await TrialBalanceImportService.ImportAsync(db,
        new ActorContext(foreign.Id, foreign.FirmId, foreign.SessionEpoch, ["AccountingPreparer"]),
        scope.ClientId, scope.EngagementId, TrialBalanceV1Csv,
        new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"));
      Assert.False(denied.Succeeded);
    }
  }

  [Fact]
  public async Task Guards_RejectUnbalancedJournal_DuplicateNumber_DuplicatePlanLine()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);
    var preparer = Actor(users.Preparer, "AccountingPreparer");
    var reviewer = Actor(users.Reviewer, "AccountingReviewer");
    Guid base1;
    await using (var db = new AuditSphereDbContext(pg.Options))
      base1 = (await TrialBalanceImportService.ImportAsync(
        db, preparer, scope.ClientId, scope.EngagementId, TrialBalanceV1Csv,
        new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"))).Value;
    await ValidateAsync(pg, scope.FirmId);
    // One fresh context per command (§28.3): a failed command never poisons the next.
    async Task<CommandResult<Guid>> DraftAsync(string number, List<(string, decimal, decimal)> journalLines)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await AdjustmentJournalService.CreateDraftAsync(db, preparer, base1, number, journalLines);
    }
    var unbalanced = await DraftAsync("AJ-001", [("520100", 5000m, 0m)]);
    Assert.False(unbalanced.Succeeded);
    var balanced = new List<(string, decimal, decimal)> { ("520100", 5000m, 0m), ("159100", 0m, 5000m) };
    var ok = await DraftAsync("AJ-001", balanced);
    Assert.True(ok.Succeeded);
    var duplicate = await DraftAsync("AJ-001", balanced);
    Assert.False(duplicate.Succeeded);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, ok.Value)).Succeeded);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var doublePost = await AdjustmentJournalService.PostAsync(db, reviewer, ok.Value);
      Assert.False(doublePost.Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, base1,
        "AJ-001", 1, ReflectionStates.NotReflected, string.Empty)).Succeeded);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var twoRevisions = await AdjustmentPlanService.CreatePlanAsync(db, preparer, base1,
        [new PlanLineInput("AJ-001", 1), new PlanLineInput("AJ-001", 1)]);
      Assert.False(twoRevisions.Succeeded);
    }
    // The database itself refuses a second reconciliation identity for one base/journal.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.JournalSourceReconciliations.Add(new JournalSourceReconciliation
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, ClientId = scope.ClientId,
        EngagementId = scope.EngagementId, BaseDatasetId = base1,
        LogicalJournalNumber = "AJ-001", JournalRevision = 1,
        State = ReflectionStates.NotReflected, CreatedAt = DateTimeOffset.UtcNow
      });
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
  }

  [Fact]
  public async Task ClientBookCorrection_RequiresManagementDecision_AndSupportsReversal()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (scope, users) = await SeedFirmWithStaffAsync(pg);
    var preparer = Actor(users.Preparer, "AccountingPreparer");
    var reviewer = Actor(users.Reviewer, "AccountingReviewer");
    AppUser clientUser;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      clientUser = await SeedUserAsync(db, scope.FirmId);
      clientUser.UserKind = "Client";
      db.Users.Update(clientUser);
      await db.SaveChangesAsync();
      await GrantAsync(db, scope, clientUser, "ClientUser");
    }

    Guid dataset;
    await using (var db = new AuditSphereDbContext(pg.Options))
      dataset = (await TrialBalanceImportService.ImportAsync(db, preparer, scope.ClientId, scope.EngagementId, TrialBalanceV1Csv,
        new TrialBalanceImportContext(scope.PeriodId, scope.BookId, "STATUTORY"))).Value;
    await ValidateAsync(pg, scope.FirmId);

    Guid journal;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, dataset, "AJ-CB-001",
        [("520100", 5000m, 0m), ("159100", 0m, 5000m)],
        purpose: AdjustmentJournalPurposes.ClientBookCorrection,
        origin: AdjustmentJournalOrigins.ClientRequested,
        reason: "Client correction for depreciation",
        evidenceReference: "client-email-001")).Value;
      var blocked = await AdjustmentJournalService.PostAsync(db, reviewer, journal);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
    }

    var clientActor = Actor(clientUser, "ClientUser");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var decision = await AdjustmentJournalService.RecordManagementDecisionAsync(db, clientActor,
        new AdjustmentJournalService.ManagementDecisionRequest(journal, ManagementDecisionStates.Accepted,
          ManagementDecisionEvidenceModes.SignedIn, "management-session-decision-001"));
      Assert.True(decision.Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);

    Guid reversal;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      reversal = (await AdjustmentJournalService.CreateReversalDraftAsync(db, preparer, journal, "AJ-CB-002")).Value;
      var saved = await db.AdjustmentJournals.SingleAsync(x => x.Id == reversal);
      var lines = await db.AdjustmentLines.Where(x => x.JournalId == reversal).OrderBy(x => x.AccountCode).ToListAsync();
      Assert.Equal(journal, saved.ReversalOfJournalId);
      Assert.Equal(AdjustmentJournalPurposes.ClientBookCorrection, saved.Purpose);
      Assert.Equal(2, lines.Count);
      Assert.Equal(5000m, lines.Sum(x => x.Debit));
      Assert.Equal(5000m, lines.Sum(x => x.Credit));
    }
  }
}
