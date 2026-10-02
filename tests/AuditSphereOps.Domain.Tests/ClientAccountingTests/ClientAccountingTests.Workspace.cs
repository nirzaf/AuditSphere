using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task ChartWorkspace_PagesAccountsAndPreservesScopedHierarchy()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var actor = Actor(scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var created = await ClientAccountingService.CreateChartVersionAsync(db, actor, scope.ClientA, "LEDGER", new(2026, 1, 1));
    Assert.True(created.Succeeded, created.Message);
    Assert.True((await ClientAccountingService.AddAccountsAsync(db, actor, created.Value,
      [new("assets", "1000", "Assets", "ASSET", "DEBIT", false),
       new("cash", "1100", "Cash", "ASSET", "DEBIT", true, "assets"),
       new("bank", "1200", "Bank", "ASSET", "DEBIT", true, "assets")])).Succeeded);
    var charts = await ChartWorkspaceQuery.GetAsync(db, actor, scope.ClientA);
    Assert.Equal("1", Assert.Single(charts.Value!.Items).Version);
    var first = await ClientAccountingService.GetChartRevisionAccountsAsync(db, actor, created.Value, 1, 1, expectedClientId: scope.ClientA);
    Assert.True(first.Succeeded, first.Message);
    Assert.Equal(3, first.Value!.TotalCount);
    Assert.Equal(2, Assert.Single(first.Value.Items).ChildCount);
    var second = await ClientAccountingService.GetChartRevisionAccountsAsync(db, actor, created.Value, 2, 1, expectedClientId: scope.ClientA);
    Assert.Equal("1000", Assert.Single(second.Value!.Items).ParentAccountCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientAccountingService.GetChartRevisionAccountsAsync(db, actor, created.Value, expectedClientId: scope.ClientB)).ErrorCode);
    Assert.False((await ClientAccountingService.GetChartRevisionAccountsAsync(db, actor, created.Value, 10001)).Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, (await ClientAccountingService.CreateChartVersionAsync(db, actor,
      scope.ClientA, "CHANGED", new(2026, 1, 1), expectedLatestVersion: 0)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientAccountingService.AddAccountsAsync(db, actor, created.Value,
      [new("other", "1300", "Other", "ASSET", "DEBIT", true)], expectedClientId: scope.ClientB, expectedVersion: 1)).ErrorCode);
    Assert.Equal(ErrorCodes.StaleRevision, (await ClientAccountingService.AddAccountsAsync(db, actor, created.Value,
      [new("other", "1300", "Other", "ASSET", "DEBIT", true)], expectedClientId: scope.ClientA, expectedVersion: 2)).ErrorCode);
    var preparerReview = await ChartPublicationQuery.GetAsync(db, actor, scope.ClientA, created.Value);
    Assert.False(preparerReview.Value!.CanPublish);
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var snapshot = await ChartPublicationQuery.GetAsync(db, reviewer, scope.ClientA, created.Value);
    Assert.True(snapshot.Value!.CanPublish);
    Assert.Equal(3, snapshot.Value.AccountCount);
    Assert.True((await ClientAccountingService.AddAccountsAsync(db, actor, created.Value,
      [new("other", "1300", "Other", "ASSET", "DEBIT", true)], expectedClientId: scope.ClientA, expectedVersion: 1)).Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, (await ClientAccountingService.PublishChartVersionAsync(db, reviewer, created.Value,
      expectedClientId: scope.ClientA, expectedVersion: 1, expectedDigest: snapshot.Value.Digest)).ErrorCode);
    var current = await ChartPublicationQuery.GetAsync(db, reviewer, scope.ClientA, created.Value);
    var cashId = await db.ClientAccounts.Where(x => x.ChartVersionId == created.Value && x.StableIdentity == "cash").Select(x => x.Id).SingleAsync();
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientAccountingService.AddSourceAccountAliasesAsync(db, actor, created.Value,
      [new(cashId, "LEDGER", "CASH", "Cash")], expectedClientId: scope.ClientB, expectedVersion: 1)).ErrorCode);
    Assert.True((await ClientAccountingService.AddSourceAccountAliasesAsync(db, actor, created.Value,
      [new(cashId, "LEDGER", "CASH", "Cash")], expectedClientId: scope.ClientA, expectedVersion: 1)).Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, (await ClientAccountingService.PublishChartVersionAsync(db, reviewer, created.Value,
      expectedClientId: scope.ClientA, expectedVersion: 1, expectedDigest: current.Value!.Digest)).ErrorCode);
    var aliasReview = await ChartPublicationQuery.GetAsync(db, reviewer, scope.ClientA, created.Value);
    Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, created.Value,
      expectedClientId: scope.ClientA, expectedVersion: 1, expectedDigest: aliasReview.Value!.Digest)).Succeeded);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task RollforwardSources_SelectExactValidatedPackageAndRejectChangedHash()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var actor = Actor(scope.Preparer, "AccountingPreparer");
    Guid periodId, packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, actor,
        new(scope.ClientA, "2026", new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, Actor(scope.Reviewer, "AccountingReviewer"), periodId, "Synthetic closed fixture")).Succeeded);
      packageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "eligible");
      var draftId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "draft");
      db.FinancialPackages.Local.Single(x => x.Id == draftId).Status = "REVIEW_REQUIRED";
      var wrongPeriod = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "other-period");
      db.FinancialPackages.Local.Single(x => x.Id == wrongPeriod).PeriodStart = "2025-01-01";
      db.FinancialPackages.Local.Single(x => x.Id == wrongPeriod).PeriodEnd = "2025-12-31";
      await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "other-currency", "USD");
      await AddPackageAsync(db, scope, scope.ClientB, scope.EngagementB, 100m, "CASH", "other-client");
      await db.SaveChangesAsync();
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    var sources = await RollforwardSourceQuery.GetAsync(verify, actor, scope.ClientA, periodId);
    Assert.True(sources.Succeeded, sources.Message);
    var source = Assert.Single(sources.Value!.Items);
    Assert.Equal(packageId, source.Id);
    Assert.Equal("QAR", source.Currency);
    var changedHash = await ClientAccountingService.RollForwardPeriodAsync(verify, actor,
      new(scope.ClientA, periodId, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR",
        new string('a', 64), 100m, 100m, "Reviewed source", packageId, 2));
    Assert.Equal(ErrorCodes.GenerationStale, changedHash.ErrorCode);
    var result = await ClientAccountingService.RollForwardPeriodAsync(verify, actor,
      new(scope.ClientA, periodId, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR",
        source.Hash, 100m, 100m, "Reviewed source", packageId, 2));
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(packageId, (await verify.OpeningBalanceBridges.SingleAsync(x => x.CurrentPeriodId == result.Value)).SourcePackageId);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AccountingPeriod_ReviewedLifecycleRejectsWrongClientAndStaleRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var partner = Actor(scope.Partner, "Partner");
    Guid id;
    await using (var db = new AuditSphereDbContext(pg.Options))
      id = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new(scope.ClientA, "2026", new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"))).Value;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ScopeDenied, (await ClientAccountingService.ClosePeriodAsync(db, reviewer, id,
        "Reviewed close", expectedRevision: 1, expectedClientId: scope.ClientB)).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await PeriodCloseReadinessQuery.GetReadinessAsync(db, reviewer, id,
        expectedClientId: scope.ClientB)).ErrorCode);
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, id, "Reviewed close",
        expectedRevision: 1, expectedClientId: scope.ClientA)).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.StaleRevision, (await ClientAccountingService.ReopenPeriodAsync(db, partner, id,
        "Correction", expectedRevision: 1, expectedClientId: scope.ClientA)).ErrorCode);
      Assert.True((await ClientAccountingService.ReopenPeriodAsync(db, partner, id, "Correction",
        expectedRevision: 2, expectedClientId: scope.ClientA)).Succeeded);
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    var amendment = Assert.Single(await verify.ClientPeriodAmendments.Where(x => x.PeriodId == id).ToListAsync());
    Assert.Equal(2, amendment.PreviousRevision);
    Assert.Equal(3, amendment.AmendmentRevision);
    var workspace = await AccountingWorkspaceQuery.GetAsync(verify, partner, scope.ClientA);
    var projected = Assert.Single(workspace.Value!.Amendments);
    Assert.Equal("2", projected.PreviousRevision);
    Assert.Equal("3", projected.Revision);
    Assert.Equal(scope.Partner.Id, projected.ActorId);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AccountingPeriod_ConcurrentDuplicateAndForeignPriorFailClosed()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var actor = Actor(scope.Preparer, "AccountingPreparer");
    async Task<bool> Create()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return (await ClientAccountingService.CreatePeriodAsync(db, actor,
        new(scope.ClientA, "2026", new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"))).Succeeded;
    }
    Assert.Equal(1, (await Task.WhenAll(Create(), Create())).Count(x => x));
    await using var verify = new AuditSphereDbContext(pg.Options);
    var period = await verify.ClientReportingPeriods.SingleAsync(x => x.ClientId == scope.ClientA);
    var wrongPrior = await ClientAccountingService.CreatePeriodAsync(verify, actor,
      new(scope.ClientB, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", period.Id));
    Assert.Equal(ErrorCodes.ScopeDenied, wrongPrior.ErrorCode);
    var workspace = await AccountingWorkspaceQuery.GetAsync(verify, actor, scope.ClientA);
    Assert.Equal("2026-01-01", Assert.Single(workspace.Value!.Periods).Start);
    async Task<bool> CreateBook()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return (await ClientAccountingService.CreateBookAsync(db, actor,
        new(scope.ClientA, period.Id, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded;
    }
    Assert.Equal(1, (await Task.WhenAll(CreateBook(), CreateBook())).Count(x => x));
    var withBook = await AccountingWorkspaceQuery.GetAsync(verify, actor, scope.ClientA);
    Assert.Equal(period.Id, Assert.Single(withBook.Value!.Books).PeriodId);
    period.Status = "CLOSED";
    await verify.SaveChangesAsync();
    var closed = await ClientAccountingService.CreateBookAsync(verify, actor,
      new(scope.ClientA, period.Id, "SECOND", "STATUTORY", "STATUTORY_ONLY", "QAR"));
    Assert.Equal(ErrorCodes.ProtectedState, closed.ErrorCode);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AccountingWorkspace_ClientScopeExcludesSiblingAndEngagementOnlyBooks()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var scoped = User(scope.FirmId, "client-accounting-reader");
    var engagementOnly = User(scope.FirmId, "engagement-accounting-reader");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.AddRange(scoped, engagementOnly);
    var engagementGrant = Grant(scope.FirmId, engagementOnly, "AccountingPreparer", scope.ClientA);
    engagementGrant.EngagementId = scope.EngagementA;
    db.RoleGrants.AddRange(Grant(scope.FirmId, scoped, "AccountingPreparer", scope.ClientA), engagementGrant);
    await db.SaveChangesAsync();
    var actor = Actor(scoped, "AccountingPreparer");
    var page = await AccountingWorkspaceQuery.ListAsync(db, actor);
    Assert.True(page.Succeeded, page.Message);
    Assert.Equal(scope.ClientA, Assert.Single(page.Value!.Items).Id);
    Assert.Equal(1, page.Value.Total);
    Assert.True((await AccountingWorkspaceQuery.GetAsync(db, actor, scope.ClientA)).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AccountingWorkspaceQuery.GetAsync(db, actor, scope.ClientB)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AccountingWorkspaceQuery.ListAsync(db, Actor(engagementOnly, "AccountingPreparer"))).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AccountingWorkspaceQuery.GetAsync(db, Actor(engagementOnly, "AccountingPreparer"), scope.ClientA)).ErrorCode);
    scoped.SessionEpoch++;
    await db.SaveChangesAsync();
    Assert.False((await AccountingWorkspaceQuery.GetAsync(db, actor, scope.ClientA)).Succeeded);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AccountingProfile_ConcurrentReviewedRevisionsHaveOneWinner()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var actor = Actor(scope.Preparer, "AccountingPreparer");
    Guid id;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await ClientAccountingService.CreateProfileAsync(db, actor,
        new(scope.ClientA, "QA", "QAR", 1, 1, "LEDGER", "SOURCE"));
      Assert.True(result.Succeeded, result.Message);
      id = result.Value;
    }
    async Task<bool> Revise(string source)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return (await ClientAccountingService.ReviseProfileAsync(db, actor, id,
        new(scope.ClientA, "QA", "QAR", 1, 1, source, "SOURCE"), 1)).Succeeded;
    }
    var outcomes = await Task.WhenAll(Revise("LEDGER-A"), Revise("LEDGER-B"));
    Assert.Equal(1, outcomes.Count(x => x));
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2, (await verify.ClientAccountingProfiles.SingleAsync(x => x.Id == id)).Revision);
    var workspace = await AccountingWorkspaceQuery.GetAsync(verify, actor, scope.ClientA);
    Assert.True(workspace.Succeeded, workspace.Message);
    Assert.Equal("2", workspace.Value!.Profile!.Revision);
  }
}
