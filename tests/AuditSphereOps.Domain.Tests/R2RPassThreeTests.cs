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
public sealed class R2RPassThreeTests
{
  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId,
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
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, preparer, reviewer, outsider);
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
