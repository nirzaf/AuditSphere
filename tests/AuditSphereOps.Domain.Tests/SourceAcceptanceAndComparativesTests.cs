using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Pass-three R2R contract slices: independent source acceptance selects the
/// reporting source and bumps the input generation; rejected TB imports persist typed
/// row-level issues; chart publication re-validates the full hierarchy under a lock.</summary>
public sealed class SourceAcceptanceAndComparativesTests
{
  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId,
    AppUser Preparer, AppUser Reviewer, AppUser Outsider);

  // ---- M21: source acceptance -------------------------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SourceAcceptance_SelectsSealedSourceAndBumpsGeneration()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var datasetId = await SeedDatasetAsync(pg, fixture, "accepted-source", Accepted: true, sealedState: true);

    Guid decisionId;
    long generationBefore;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      generationBefore = await db.ClientSafetyStates.Where(x => x.Id == fixture.ClientId)
        .Select(x => x.InputGeneration).SingleAsync();

      // Preparer independence: acceptance is a reviewer decision.
      var denied = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, preparer,
        new AcceptSourceRevisionRequest(fixture.ClientId, fixture.EngagementId,
          AccountingSourceKinds.TrialBalance, datasetId, null, "acceptance-evidence-1"));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

      var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
        new AcceptSourceRevisionRequest(fixture.ClientId, fixture.EngagementId,
          AccountingSourceKinds.TrialBalance, datasetId, null, "acceptance-evidence-1"));
      Assert.True(accepted.Succeeded, accepted.Message);
      decisionId = accepted.Value;

      // Double acceptance of the same revision is a conflict, not a second decision.
      var duplicate = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
        new AcceptSourceRevisionRequest(fixture.ClientId, fixture.EngagementId,
          AccountingSourceKinds.TrialBalance, datasetId, null, "acceptance-evidence-2"));
      Assert.False(duplicate.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicate.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var selected = await AccountingSourceAcceptanceService.GetSelectedSourceAsync(db, reviewer,
        fixture.ClientId, fixture.EngagementId, AccountingSourceKinds.TrialBalance);
      Assert.True(selected.Succeeded, selected.Message);
      Assert.NotNull(selected.Value);
      Assert.Equal(decisionId, selected.Value!.DecisionId);
      Assert.Equal(datasetId, selected.Value.TrialBalanceDatasetId);
      Assert.Equal(generationBefore + 1, selected.Value.InputGeneration);
      Assert.Equal(1, await db.SourceAcceptanceDecisions.CountAsync(
        x => x.TrialBalanceDatasetId == datasetId));
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SourceAcceptance_RefusesUnsealedOrUnacceptedSources()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    await using var db = new AuditSphereDbContext(pg.Options);
    var sealedNotAccepted = await SeedDatasetAsync(pg, fixture, "pending-source", Accepted: false, sealedState: true);
    var pending = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
      new AcceptSourceRevisionRequest(fixture.ClientId, fixture.EngagementId,
        AccountingSourceKinds.TrialBalance, sealedNotAccepted, null, "evidence"));
    Assert.False(pending.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, pending.ErrorCode);

    var loading = await SeedDatasetAsync(pg, fixture, "loading-source", Accepted: false, sealedState: false);
    var unsealed = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
      new AcceptSourceRevisionRequest(fixture.ClientId, fixture.EngagementId,
        AccountingSourceKinds.TrialBalance, loading, null, "evidence"));
    Assert.False(unsealed.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unsealed.ErrorCode);

    var selected = await AccountingSourceAcceptanceService.GetSelectedSourceAsync(db, reviewer,
      fixture.ClientId, fixture.EngagementId, AccountingSourceKinds.TrialBalance);
    Assert.True(selected.Succeeded);
    Assert.Null(selected.Value);
  }

  // ---- M21: typed validation issues ---------------------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task RejectedDataset_PersistsTypedIssuesAndQueryReturnsThem()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var datasetId = await SeedDatasetAsync(pg, fixture, "issues-source", Accepted: false, sealedState: true, unbalanced: true);

    // Validation runs as a durable operation; enqueue it through the real store so the
    // request satisfies the operation invariants, then run the handler publish step.
    var operationFactory = new TestDbContextFactory(pg.Options);
    var operationStore = new PostgresOperationStore(operationFactory);
    var handler = new TrialBalanceValidationHandler();
    var workerOptions = new WorkerOptions(fixture.FirmId, "Test");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var discovery = new TrialBalanceDiscovery(operationFactory, operationStore, handler, workerOptions);
      Assert.Equal(1, await discovery.EnqueuePendingAsync(CancellationToken.None));
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.SingleAsync(
        x => x.OperationKind == TrialBalanceValidationHandler.Kind && x.TargetId == datasetId);
      operation.Status = OperationState.CLAIMED;
      operation.AttemptToken = 99;
      operation.LeaseOwner = "test-runner";
      operation.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
      await handler.PublishAsync(db, operation, verifiedRemoteResult: null, CancellationToken.None);
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      Assert.Equal("Rejected", dataset.ValidationStatus);
      var page = await TrialBalanceValidationIssueQuery.GetIssuesAsync(db, preparer, datasetId);
      Assert.True(page.Succeeded, page.Message);
      Assert.True(page.Value!.TotalCount > 0);
      Assert.Contains(page.Value.Items, x => x.Code == "UNBALANCED");
      var outsider = Actor(fixture.Outsider, "AccountingPreparer");
      var denied = await TrialBalanceValidationIssueQuery.GetIssuesAsync(db, outsider, datasetId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
  }

  // ---- M20: chart publish locked re-validation -----------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ChartPublish_RevalidatesHierarchyUnderLock()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    Guid chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, fixture.ClientId,
        "CHART-PUB", new DateOnly(2026, 1, 1))).Value;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Posting parent chain: 1000 -> parent 2000 (also posting) is rejected at publish.
      var parent = new ClientAccount
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
        ChartVersionId = chartId, StableIdentity = "parent", AccountCode = "2000",
        AccountName = "Parent", AccountType = "ASSET", NormalBalance = "DEBIT", IsPosting = true
      };
      var child = new ClientAccount
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
        ChartVersionId = chartId, StableIdentity = "child", AccountCode = "1000",
        AccountName = "Child", AccountType = "ASSET", NormalBalance = "DEBIT", IsPosting = true,
        ParentAccountId = parent.Id
      };
      db.ClientAccounts.AddRange(parent, child);
      await db.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var blocked = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId);
      Assert.False(blocked.Succeeded);
      Assert.Contains("posting parent", blocked.Message!, StringComparison.OrdinalIgnoreCase);
      Assert.Equal(AccountingWorkflowStates.Draft,
        await db.ClientChartVersions.Where(x => x.Id == chartId).Select(x => x.Status).SingleAsync());
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var parent = await db.ClientAccounts.SingleAsync(x => x.ChartVersionId == chartId && x.StableIdentity == "parent");
      parent.IsPosting = false;
      await db.SaveChangesAsync();
      var published = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId);
      Assert.True(published.Succeeded, published.Message);
      Assert.Equal(AccountingWorkflowStates.Approved,
        await db.ClientChartVersions.Where(x => x.Id == chartId).Select(x => x.Status).SingleAsync());
    }
  }

  private sealed class TestDbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : AuditSphereOps.Application.Operations.IAuditSphereDbContextFactory
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
    public Task<IAuditSphereDbContext> CreateAsync(CancellationToken ct = default) =>
      Task.FromResult<IAuditSphereDbContext>(new AuditSphereDbContext(options));
  }

  // ---- M23: reconciliation revision + carry-forward -------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReconciliationRevision_CreatesSuccessorAndCarryForward()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, fixture, preparer);
    var sourceHash = Hashing.Sha256Hex("revision-source");
    var datasetId = Guid.CreateVersion7();

    Guid originalId, revisedId, carriedId, nextPeriodId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY", SourceKind = "Raw", LegalEntityKey = "REV-CLIENT",
        Currency = "QAR", RawFileSha256Hex = sourceHash, NormalizedDatasetDigest = sourceHash, Sha256Hex = sourceHash,
        Balanced = true, ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = fixture.Preparer.Id
      });
      db.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash",
        Amount = 100m, Currency = "QAR", Entity = "REV-CLIENT"
      });
      await db.SaveChangesAsync();
      var sealedDataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      sealedDataset.ValidationStatus = "Accepted";
      sealedDataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();
      originalId = (await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(fixture.ClientId, fixture.EngagementId, periodId, bookId, "CASH",
          datasetId, null, ["1000"], new DateOnly(2026, 12, 31)))).Value;
      Assert.True((await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, originalId,
        [
          new ReconciliationItemInput("ITEM-R1", 50m, "QAR", new DateOnly(2026, 12, 31), "timing", "ev-r1", "OPEN"),
          new ReconciliationItemInput("ITEM-R2", -50m, "QAR", new DateOnly(2026, 12, 31), "timing", "ev-r2", "OPEN")
        ])).Succeeded);

      // Revision: successor starts as Draft with a bumped revision and supersession link.
      revisedId = (await AccountingAnalysisService.ReviseReconciliationAsync(db, preparer, originalId,
        "Bank statement updated with additional deposits.")).Value;
      var revised = await db.AccountingReconciliations.SingleAsync(x => x.Id == revisedId);
      Assert.Equal(2L, revised.Revision);
      Assert.Equal(originalId, revised.SupersedesReconciliationId);
      Assert.Equal(AccountingWorkflowStates.Draft, revised.Status);
      var originalAfter = await db.AccountingReconciliations.AsNoTracking().SingleAsync(x => x.Id == originalId);
      Assert.Equal("RECONCILED", originalAfter.Status);
      Assert.Equal(1L, originalAfter.Revision);
    }

    // Approve the original, then carry forward its items to the next period.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await AccountingAnalysisService.CalculateReconciliationProofAsync(db, preparer, originalId)).Succeeded);
      Assert.True((await AccountingAnalysisService.ApproveReconciliationAsync(db, reviewer, originalId)).Succeeded);
      nextPeriodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(fixture.ClientId, "2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
          "STATUTORY", "QAR"))).Value;
      var itemIds = await db.AccountingReconciliationItems.AsNoTracking()
        .Where(x => x.ReconciliationId == originalId).Select(x => x.Id).ToListAsync();
      carriedId = (await AccountingAnalysisService.CarryForwardReconciliationItemsAsync(db, preparer, originalId,
        nextPeriodId, null, itemIds, "carry-forward-evidence-1")).Value;
      var carried = await db.AccountingReconciliations.SingleAsync(x => x.Id == carriedId);
      Assert.Equal(AccountingWorkflowStates.Draft, carried.Status);
      Assert.Equal(nextPeriodId, carried.PeriodId);
      var carriedItems = await db.AccountingReconciliationItems.Where(x => x.ReconciliationId == carriedId).ToListAsync();
      Assert.Equal(2, carriedItems.Count);
      Assert.Equal("CARRIED_FORWARD", carriedItems[0].Disposition);
      Assert.Equal(0m, carried.SourceTotal);
    }
  }

  // ---- M22: adjusted balance query --------------------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AdjustedBalances_ReturnRawAdjustedAndDelta()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    Guid snapshotId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var planId = Guid.CreateVersion7();
      db.AdjustmentPlans.Add(new AdjustmentPlan
      {
        Id = planId, FirmId = fixture.FirmId, ClientId = fixture.ClientId,
        EngagementId = fixture.EngagementId, BaseDatasetId = fixture.DatasetId,
        Status = "Finalized", ResultHash = Hashing.Sha256Hex("adj-query"),
        CreatedByUserId = fixture.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      var snapshot = new AdjustedTrialBalanceSnapshot
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
        EngagementId = fixture.EngagementId, BaseDatasetId = fixture.DatasetId,
        AdjustmentPlanId = planId, Currency = "QAR", ResultHash = Hashing.Sha256Hex("adj-query"),
        CreatedByUserId = fixture.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      };
      snapshotId = snapshot.Id;
      db.AdjustedTrialBalanceSnapshots.Add(snapshot);
      db.AdjustedTrialBalanceRows.Add(new AdjustedTrialBalanceRow
      {
        Id = Guid.CreateVersion7(), SnapshotId = snapshot.Id, AccountCode = "1000",
        Amount = 90m, Currency = "QAR"
      });
      db.AdjustedTrialBalanceRows.Add(new AdjustedTrialBalanceRow
      {
        Id = Guid.CreateVersion7(), SnapshotId = snapshot.Id, AccountCode = "4000",
        Amount = -100m, Currency = "QAR"
      });
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var page = await AccountingReadQueries.GetAdjustedBalancesAsync(db, preparer, snapshotId);
      Assert.True(page.Succeeded, page.Message);
      Assert.Equal(2, page.Value!.TotalCount);
      var cash = page.Value.Items.Single(x => x.AccountCode == "1000");
      Assert.Equal(100m, cash.RawAmount);
      Assert.Equal(90m, cash.AdjustedAmount);
      Assert.Equal(-10m, cash.Delta);
      var revenue = page.Value.Items.Single(x => x.AccountCode == "4000");
      Assert.Equal(-100m, revenue.RawAmount);
      Assert.Equal(-100m, revenue.AdjustedAmount);
      Assert.Equal(0m, revenue.Delta);
    }
  }

  // ---- M24: comparative difference ---------------------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ComparativeDifference_ReturnsPerLineDeltas()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");

    Guid originalId, revisedId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      originalId = AddValidatedPackageWithLines(db, fixture, "comp-original", [("CASH", "ASSETS", 100m), ("REVENUE", "INCOME", -100m)]);
      revisedId = AddValidatedPackageWithLines(db, fixture, "comp-revised", [("CASH", "ASSETS", 90m), ("REVENUE", "INCOME", -100m), ("EQUIPMENT", "ASSETS", 10m)]);
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var page = await AccountingReadQueries.GetComparativeDifferenceAsync(db, preparer, originalId, revisedId);
      Assert.True(page.Succeeded, page.Message);
      Assert.Equal(3, page.Value!.TotalCount);
      var cash = page.Value.Items.Single(x => x.DestinationCode == "CASH");
      Assert.Equal(100m, cash.OriginalAmount);
      Assert.Equal(90m, cash.RevisedAmount);
      Assert.Equal(-10m, cash.Delta);
      var equipment = page.Value.Items.Single(x => x.DestinationCode == "EQUIPMENT");
      Assert.Equal(0m, equipment.OriginalAmount);
      Assert.Equal(10m, equipment.RevisedAmount);
      Assert.Equal(10m, equipment.Delta);
      var revenue = page.Value.Items.Single(x => x.DestinationCode == "REVENUE");
      Assert.Equal(0m, revenue.Delta);
    }
  }

  private static Guid AddValidatedPackageWithLines(
    AuditSphereDbContext db, Fixture fixture, string suffix,
    (string Destination, string Section, decimal Amount)[] lines)
  {
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var digest = Hashing.Sha256Hex("p4-package-" + suffix);
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      SourceKind = "Raw", Revision = 1, LegalEntityKey = suffix.ToUpperInvariant(), Currency = "QAR",
      RawFileSha256Hex = digest, NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true,
      ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m,
      ImportedAt = now, ImportedByUserId = fixture.Preparer.Id
    });
    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      BaseDatasetId = datasetId, Status = "Finalized", ResultHash = digest,
      CreatedByUserId = fixture.Preparer.Id, CreatedAt = now
    });
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      DatasetId = datasetId, Version = 1, Generation = 1, TaxonomyVersion = "tax-v1",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved,
      CreatedByUserId = fixture.Preparer.Id, ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = now, CreatedAt = now
    });
    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      BaseDatasetId = datasetId, AdjustmentPlanId = planId, Currency = "QAR", ResultHash = digest,
      CreatedByUserId = fixture.Preparer.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      AdjustedDatasetId = adjustedId, MappingVersionId = mappingId, AdjustmentPlanId = planId,
      Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", TaxonomyVersion = "tax-v1",
      TemplateVersion = $"p4-{suffix}", CalculationEngineVersion = "test-engine", CalculationHash = digest,
      Currency = "QAR", Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    foreach (var (dest, section, amount) in lines)
      db.FinancialPackageLines.Add(new FinancialPackageLine
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
        EngagementId = fixture.EngagementId, FinancialPackageId = packageId,
        SourceAccountCode = dest, DestinationCode = dest, StatementSection = section,
        Amount = amount, Fraction = 1m, Currency = "QAR", AdjustedSnapshotId = adjustedId, CreatedAt = now
      });
    return packageId;
  }

  private static async Task<(Guid PeriodId, Guid BookId)> CreateGlFixtureAsync(
    PgTestSchema pg, Fixture fixture, ActorContext preparer)
  {
    Guid periodId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(fixture.ClientId, "QA", "QAR", 1, 1, "LEDGER-P3", "P3-1"))).Succeeded);
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(fixture.ClientId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
          "STATUTORY", "QAR"))).Value;
      var book = await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(fixture.ClientId, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"));
      Assert.True(book.Succeeded, book.Message);
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    var bookId = await verify.ClientReportingBooks.Where(x => x.ClientId == fixture.ClientId && x.PeriodId == periodId)
      .Select(x => x.Id).SingleAsync();
    return (periodId, bookId);
  }

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = null, EngagementId = null, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    var outsider = User(firmId, "outsider");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "PASS THREE CLIENT", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer, outsider);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"), Grant(firmId, reviewer, "AccountingReviewer"));
    var datasetId = Guid.CreateVersion7();
    var hash = Hashing.Sha256Hex("pass-three-seed");
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      SourceKind = "Raw", Revision = 1, Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.Id
    });
    db.TrialBalanceRows.AddRange(
      new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000",
        AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST" },
      new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
        AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST" });
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, datasetId, preparer, reviewer, outsider);
  }

  private static async Task<Guid> SeedDatasetAsync(
    PgTestSchema pg, Fixture fixture, string suffix, bool Accepted, bool sealedState, bool unbalanced = false)
  {
    var datasetId = Guid.CreateVersion7();
    var hash = Hashing.Sha256Hex("pass-three-" + suffix);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Rows must land while the dataset is still LOADING: sealed rows are immutable.
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        SourceKind = "Raw", Revision = 1, LegalEntityKey = suffix.ToUpperInvariant(), Currency = "QAR",
        RawFileSha256Hex = hash, NormalizedDatasetDigest = hash, Sha256Hex = hash, Balanced = true,
        ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
        ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = fixture.Preparer.Id
      });
      db.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000",
        AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST"
      });
      if (!unbalanced)
      {
        db.TrialBalanceRows.Add(new TrialBalanceRow
        {
          Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
          AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST"
        });
      }
      await db.SaveChangesAsync();
      if (sealedState)
      {
        var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
        dataset.ImportState = TrialBalanceImportStates.Sealed;
        dataset.ValidationStatus = Accepted ? "Accepted" : "Pending";
        await db.SaveChangesAsync();
      }
    }
    return datasetId;
  }
}
