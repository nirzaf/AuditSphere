using System.Text;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

public sealed class EngagementLifecycleQueryTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task WorkflowAndProgressRefuseAmbientOrEnlistedTransactionWithoutCompletingIt(bool explicitEnlistment)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var actor = PbcSeed.Actor(seed.Admin, "Administrator");
    using var transaction = new System.Transactions.CommittableTransaction();
    System.Transactions.TransactionScope? scope = null;
    try
    {
      if (explicitEnlistment)
      {
        await db.Database.OpenConnectionAsync();
        System.Transactions.TransactionsDatabaseFacadeExtensions.EnlistTransaction(db.Database, transaction);
        Assert.Same(transaction, System.Transactions.TransactionsDatabaseFacadeExtensions.GetEnlistedTransaction(db.Database));
      }
      else
      {
        scope = new System.Transactions.TransactionScope(transaction,
          System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        Assert.Equal(transaction.TransactionInformation.LocalIdentifier,
          System.Transactions.Transaction.Current!.TransactionInformation.LocalIdentifier);
      }
      var workflow = await EngagementWorkflowWorkspaceQuery.GetAsync(db, actor, seed.EngagementId);
      var detail = await EngagementProgressQuery.GetAsync(db, actor, seed.EngagementId);
      var client = await EngagementProgressQuery.ForClientAsync(db, actor, seed.ClientId);
      var portfolio = await PortfolioQuery.ProgressAsync(db, actor);
      Assert.Equal(ErrorCodes.GateBlocked, workflow.ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, detail.ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, client.ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, portfolio.ErrorCode);
      Assert.Equal(System.Transactions.TransactionStatus.Active, transaction.TransactionInformation.Status);
      if (explicitEnlistment)
        Assert.Same(transaction, System.Transactions.TransactionsDatabaseFacadeExtensions.GetEnlistedTransaction(db.Database));
      else Assert.Equal(transaction.TransactionInformation.LocalIdentifier,
        System.Transactions.Transaction.Current!.TransactionInformation.LocalIdentifier);
    }
    finally
    {
      scope?.Complete();
      scope?.Dispose();
      transaction.Rollback();
    }
  }

  [Fact]
  public async Task RetainedAuthorityReevaluatesExpiryAndKeepsCoveringAlternatives()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var user = PbcSeed.User(seed.FirmId, "Staff");
    var expires = new DateTimeOffset(DateTimeOffset.UtcNow.AddHours(1).UtcTicks / 10 * 10, TimeSpan.Zero);
    var readGrant = PbcSeed.Grant(seed.FirmId, user, "Staff", seed.ClientId, seed.EngagementId);
    var detailGrant = PbcSeed.Grant(seed.FirmId, user, "SeniorManager"); detailGrant.ExpiresAt = expires;
    var actionGrant = PbcSeed.Grant(seed.FirmId, user, "CommercialManager", seed.ClientId, seed.EngagementId);
    actionGrant.ExpiresAt = expires;
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(user);
    db.RoleGrants.AddRange(readGrant, detailGrant, actionGrant);
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(user, "Staff");
    var read = new AuthorizationRequest(seed.FirmId, seed.ClientId, seed.EngagementId, ["Staff"], InternalOnly: true);
    var detail = new AuthorizationRequest(seed.FirmId, EngagementId: seed.EngagementId,
      RequiredRoles: ["Manager", "SeniorManager"], InternalOnly: true, RequireFirmWide: true);
    var action = new AuthorizationRequest(seed.FirmId, seed.ClientId, seed.EngagementId, ["CommercialManager"], InternalOnly: true);
    AuthorizationDecision.EngagementSnapshot withoutAlternative;
    AuthorizationDecision.ReadSnapshot pageWithoutAlternative;
    await using (var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
    {
      withoutAlternative = await AuthorizationDecision.CaptureEngagementProjectionAsync(db, actor, seed.ClientId,
        seed.EngagementId, read, detail, action, null, CancellationToken.None);
      pageWithoutAlternative = await AuthorizationDecision.CaptureReadSnapshotAsync(db, actor, [read, detail, action], CancellationToken.None);
      await transaction.CommitAsync();
    }
    var before = withoutAlternative.Evaluate(expires.AddTicks(-1));
    var atExpiry = withoutAlternative.Evaluate(expires);
    Assert.True(before.Read.Succeeded); Assert.True(before.CanViewDetails); Assert.True(before.CanOpenAction);
    Assert.True(atExpiry.Read.Succeeded); Assert.False(atExpiry.CanViewDetails); Assert.False(atExpiry.CanOpenAction);
    var pageAtExpiry = pageWithoutAlternative.Evaluate(expires);
    Assert.True(pageAtExpiry[0].Succeeded); Assert.False(pageAtExpiry[1].Succeeded); Assert.False(pageAtExpiry[2].Succeeded);

    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, user, "Manager"));
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, user, "CommercialManager"));
    await db.SaveChangesAsync();
    AuthorizationDecision.EngagementSnapshot withAlternative;
    AuthorizationDecision.ReadSnapshot pageWithAlternative;
    await using (var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
    {
      withAlternative = await AuthorizationDecision.CaptureEngagementProjectionAsync(db, actor, seed.ClientId,
        seed.EngagementId, read, detail, action, null, CancellationToken.None);
      pageWithAlternative = await AuthorizationDecision.CaptureReadSnapshotAsync(db, actor, [read, detail, action], CancellationToken.None);
      await transaction.CommitAsync();
    }
    var stillAuthorized = withAlternative.Evaluate(expires);
    Assert.True(stillAuthorized.Read.Succeeded); Assert.True(stillAuthorized.CanViewDetails); Assert.True(stillAuthorized.CanOpenAction);
    Assert.All(pageWithAlternative.Evaluate(expires), decision => Assert.True(decision.Succeeded));
  }

  [Theory]
  [InlineData("DETAIL")]
  [InlineData("ACTION")]
  public async Task WorkflowReevaluatesExpiryAfterFinalSnapshotCommit(string change)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var detailGrant = PbcSeed.Grant(seed.FirmId, seed.Staff, "SeniorManager");
    var actionGrant = PbcSeed.Grant(seed.FirmId, seed.Staff, "CommercialManager", seed.ClientId, seed.EngagementId);
    var targetGrantId = change == "DETAIL" ? detailGrant.Id : actionGrant.Id;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.RoleGrants.Add(detailGrant);
      if (change == "ACTION") setup.RoleGrants.Add(actionGrant);
      await setup.SaveChangesAsync();
      var baseline = await EngagementWorkflowWorkspaceQuery.GetAsync(setup, PbcSeed.Actor(seed.Staff, "Staff"), seed.EngagementId);
      Assert.True(baseline.Succeeded, baseline.Message);
      Assert.Contains("Commercial proposal preparation and dispatch required", baseline.Value!.Blockers);
      if (change == "ACTION") Assert.Single(baseline.Value.NextActions);
    }
    var race = new WorkflowExpiryBoundary(async () =>
    {
      var expiry = new DateTimeOffset(DateTimeOffset.UtcNow.AddSeconds(5).UtcTicks / 10 * 10, TimeSpan.Zero);
      int updated;
      await using (var writer = new AuditSphereDbContext(pg.Options))
      await using (var transaction = await writer.Database.BeginTransactionAsync())
      {
        updated = await writer.RoleGrants.Where(g => g.Id == targetGrantId && g.FirmId == seed.FirmId && g.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(g => g.ExpiresAt, expiry));
        await transaction.CommitAsync();
      }
      await using var verifier = new AuditSphereDbContext(pg.Options);
      var observed = await verifier.RoleGrants.AsNoTracking().AnyAsync(g => g.Id == targetGrantId && g.ExpiresAt == expiry);
      return (expiry, updated, observed);
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(race, new WorkflowExpiryCommitBoundary(race)).Options);
    var result = await EngagementWorkflowWorkspaceQuery.GetAsync(reader, PbcSeed.Actor(seed.Staff, "Staff"), seed.EngagementId);
    Assert.True(race.AnchorObserved);
    Assert.True(race.MutationCommitted);
    Assert.Equal(1, race.UpdatedRows);
    Assert.True(race.SnapshotBeforeExpiry);
    Assert.True(race.CommitObserved);
    Assert.True(race.ExpiryPassedBeforeRelease);
    Assert.True(result.Succeeded, result.Message);
    if (change == "DETAIL")
    {
      Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], result.Value!.Blockers);
      Assert.Null(result.Value.BlockerDetails);
    }
    else
    {
      Assert.Contains("Commercial proposal preparation and dispatch required", result.Value!.Blockers);
      Assert.Empty(result.Value.NextActions);
    }
  }

  [Theory]
  [InlineData("detail", "DETAIL")]
  [InlineData("portfolio", "READ")]
  [InlineData("client", "ALTERNATIVE")]
  public async Task ProgressReevaluatesExpiryAfterFinalSnapshotCommit(string view, string change)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var detailGrant = PbcSeed.Grant(seed.FirmId, seed.Staff, "SeniorManager");
    Guid targetGrantId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var engagement = await setup.Engagements.SingleAsync(e => e.Id == seed.EngagementId);
      engagement.ServiceRoute = "FinancialStatementAudit";
      setup.RoleGrants.Add(detailGrant);
      if (change == "ALTERNATIVE") setup.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Staff, "Manager"));
      targetGrantId = change == "READ"
        ? await setup.RoleGrants.Where(g => g.UserId == seed.Staff.Id && g.Role == "Staff").Select(g => g.Id).SingleAsync()
        : detailGrant.Id;
      await setup.SaveChangesAsync();
      var baseline = await EngagementProgressQuery.GetAsync(setup, PbcSeed.Actor(seed.Staff, "Staff"), seed.EngagementId);
      Assert.True(baseline.Succeeded, baseline.Message);
      Assert.Contains("Commercial proposal preparation and dispatch required", baseline.Value!.Gates);
    }
    var race = new ProgressExpiryBoundary(seed.Staff.Id, async () =>
    {
      var expiry = new DateTimeOffset(DateTimeOffset.UtcNow.AddSeconds(5).UtcTicks / 10 * 10, TimeSpan.Zero);
      int updated;
      await using (var writer = new AuditSphereDbContext(pg.Options))
      await using (var transaction = await writer.Database.BeginTransactionAsync())
      {
        updated = await writer.RoleGrants.Where(g => g.Id == targetGrantId && g.FirmId == seed.FirmId && g.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(g => g.ExpiresAt, expiry));
        await transaction.CommitAsync();
      }
      await using var verifier = new AuditSphereDbContext(pg.Options);
      var observed = await verifier.RoleGrants.AsNoTracking().AnyAsync(g => g.Id == targetGrantId && g.ExpiresAt == expiry);
      return (expiry, updated, observed);
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(race, new ProgressExpiryCommitBoundary(race)).Options);
    var actor = PbcSeed.Actor(seed.Staff, "Staff");
    EngagementProgressView? progress;
    bool succeeded;
    if (view == "detail")
    {
      var result = await EngagementProgressQuery.GetAsync(reader, actor, seed.EngagementId);
      succeeded = result.Succeeded; progress = result.Value;
    }
    else
    {
      var result = view == "client" ? await EngagementProgressQuery.ForClientAsync(reader, actor, seed.ClientId)
        : await PortfolioQuery.ProgressAsync(reader, actor);
      succeeded = result.Succeeded; progress = result.Value is null ? null : Assert.Single(result.Value.Items);
    }
    Assert.True(race.MutationCommitted);
    Assert.Equal(1, race.UpdatedRows);
    Assert.True(race.GrantReadBeforeExpiry);
    Assert.True(race.FinalCommitObserved);
    Assert.True(race.ExpiryPassedBeforeRelease);
    if (change == "READ") { Assert.False(succeeded); Assert.Null(progress); }
    else
    {
      Assert.True(succeeded); Assert.NotNull(progress); Assert.True(progress.GateOpen);
      if (change == "DETAIL")
      {
        Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], progress.Gates);
        Assert.Equal("Some engagement readiness details are restricted to firm-wide management.", progress.NextAction);
      }
      else Assert.Contains("Commercial proposal preparation and dispatch required", progress.Gates);
    }
  }

  [Fact]
  public async Task WorkflowRefusesCallerOwnedTransactionWithoutCompletingIt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await using var transaction = await db.Database.BeginTransactionAsync();
    var result = await EngagementWorkflowWorkspaceQuery.GetAsync(db, PbcSeed.Actor(seed.Admin, "Administrator"), seed.EngagementId);
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, result.ErrorCode);
    Assert.Null(result.Value);
    Assert.Same(transaction, db.Database.CurrentTransaction);
    await transaction.RollbackAsync();
  }

  [Theory]
  [InlineData("SCOPED_STAFF", true, false)]
  [InlineData("FIRM_PARTNER", true, true)]
  [InlineData("LOWER_MANAGER", true, true)]
  [InlineData("MIXED", true, true)]
  [InlineData("MIXED_SCOPES", true, false)]
  [InlineData("EXPIRED_DETAIL", true, false)]
  [InlineData("NONE", false, false)]
  [InlineData("EXPIRED", false, false)]
  [InlineData("SIBLING", false, false)]
  [InlineData("CLIENT", false, false)]
  public async Task FinalAuthoritySharesScalarRoleAndScopeRules(string scenario, bool expectedRead, bool expectedDetail)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var user = PbcSeed.User(seed.FirmId, scenario == "CLIENT" ? "Client" : "Staff");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(user);
    if (scenario == "FIRM_PARTNER") db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, user, "Partner"));
    else if (scenario == "LOWER_MANAGER") db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, user, "manager"));
    else if (scenario != "NONE")
    {
      var grant = PbcSeed.Grant(seed.FirmId, user, "Staff", seed.ClientId,
        scenario == "SIBLING" ? Guid.NewGuid() : seed.EngagementId);
      if (scenario == "EXPIRED") grant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
      db.RoleGrants.Add(grant);
      if (scenario == "MIXED") db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, user, "SeniorManager"));
      if (scenario == "MIXED_SCOPES")
      {
        db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, user, "Manager", seed.ClientId, seed.EngagementId));
        db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, user, "Staff"));
      }
      if (scenario == "EXPIRED_DETAIL")
      {
        var expiredDetail = PbcSeed.Grant(seed.FirmId, user, "SeniorManager");
        expiredDetail.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        db.RoleGrants.Add(expiredDetail);
      }
    }
    // An actual sibling engagement keeps the denied grant's linkage valid without widening its scope.
    if (scenario == "SIBLING")
    {
      var siblingId = db.RoleGrants.Local.Single(x => x.UserId == user.Id).EngagementId!.Value;
      db.Engagements.Add(new Engagement { Id = siblingId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        Status = "Draft", CreatedAt = DateTimeOffset.UtcNow });
    }
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(user, "Staff");
    var read = new AuthorizationRequest(seed.FirmId, EngagementId: seed.EngagementId,
      RequiredRoles: ["Administrator", "Partner", "Manager", "Senior", "Staff", "Auditor", "EngagementLeader", "CommercialManager", "FinanceManager"], InternalOnly: true);
    var detail = new AuthorizationRequest(seed.FirmId, EngagementId: seed.EngagementId,
      RequiredRoles: ["Administrator", "Partner", "Manager", "SeniorManager"], InternalOnly: true, RequireFirmWide: true);
    var scalarRead = await AuthorizationDecision.AuthorizeAsync(db, actor, read);
    var scalarDetail = await AuthorizationDecision.AuthorizeAsync(db, actor, detail);
    var final = await AuthorizationDecision.AuthorizeEngagementProjectionAsync(db, actor, seed.ClientId, seed.EngagementId,
      read, detail, null, null, CancellationToken.None);
    Assert.Equal(expectedRead, scalarRead.Succeeded);
    Assert.Equal(expectedRead, final.Read.Succeeded);
    Assert.Equal(expectedDetail, scalarDetail.Succeeded);
    Assert.Equal(expectedRead && expectedDetail, final.CanViewDetails);
    Assert.False(final.CanOpenAction);
    if (expectedRead)
    {
      var otherClient = Guid.NewGuid();
      db.PracticeClients.Add(new PracticeClient { Id = otherClient, FirmId = seed.FirmId,
        LegalName = "Synthetic different client", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      var wrongPair = await AuthorizationDecision.AuthorizeEngagementProjectionAsync(db, actor, otherClient, seed.EngagementId,
        read, detail, null, null, CancellationToken.None);
      Assert.False(wrongPair.Read.Succeeded);
      Assert.False(wrongPair.CanViewDetails);
    }
    await using (var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
    {
      var decisions = await AuthorizationDecision.AuthorizeReadSnapshotAsync(db, actor,
        [read, detail, new AuthorizationRequest(seed.FirmId, Guid.NewGuid(), seed.EngagementId,
          ["Staff", "Manager", "Partner", "Administrator"], InternalOnly: true)], CancellationToken.None);
      Assert.Equal(expectedRead, decisions[0].Succeeded);
      Assert.Equal(expectedDetail, decisions[1].Succeeded);
      Assert.False(decisions[2].Succeeded);
      await snapshot.CommitAsync();
    }
  }

  [Theory]
  [InlineData(false, false, "SESSION")]
  [InlineData(false, true, "SESSION")]
  [InlineData(true, false, "SESSION")]
  [InlineData(true, true, "SESSION")]
  [InlineData(false, false, "READ")]
  [InlineData(false, true, "READ")]
  [InlineData(true, false, "READ")]
  [InlineData(true, true, "READ")]
  [InlineData(false, false, "DETAIL")]
  [InlineData(false, true, "DETAIL")]
  [InlineData(true, false, "DETAIL")]
  [InlineData(true, true, "DETAIL")]
  [InlineData(true, false, "ACTION")]
  [InlineData(true, true, "ACTION")]
  public async Task FinalAuthorityUsesOneSnapshot(bool workflow, bool afterSnapshot, string change)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var user = PbcSeed.User(seed.FirmId, "Staff");
    var readGrant = PbcSeed.Grant(seed.FirmId, user, "Staff", seed.ClientId, seed.EngagementId);
    var detailGrant = PbcSeed.Grant(seed.FirmId, user, change == "READ" ? "SeniorManager" : "Manager");
    var actionGrant = PbcSeed.Grant(seed.FirmId, user, "CommercialManager", seed.ClientId, seed.EngagementId);
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.Add(user);
      setup.RoleGrants.Add(readGrant);
      if (change is "READ" or "DETAIL") setup.RoleGrants.Add(detailGrant);
      if (change == "ACTION") setup.RoleGrants.Add(actionGrant);
      await setup.SaveChangesAsync();
    }
    var actor = PbcSeed.Actor(user, "Staff");
    await using (var baseline = new AuditSphereDbContext(pg.Options))
    {
      var report = await EngagementWorkflowWorkspaceQuery.GetAsync(baseline, actor, seed.EngagementId);
      Assert.True(report.Succeeded, report.Message);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, report.Value!.CanonicalStage);
      if (change == "ACTION") Assert.Single(report.Value.NextActions);
      if (change is "READ" or "DETAIL") Assert.Contains("Commercial proposal preparation and dispatch required", report.Value.Blockers);
    }
    var targetGrant = change == "READ" ? readGrant.Id : change == "DETAIL" ? detailGrant.Id : actionGrant.Id;
    var race = new ProjectionSnapshotRace(workflow, afterSnapshot, async () =>
    {
      int updated;
      await using (var writer = new AuditSphereDbContext(pg.Options))
      await using (var tx = await writer.Database.BeginTransactionAsync())
      {
        updated = change == "SESSION"
          ? await writer.Users.Where(x => x.Id == user.Id && x.FirmId == seed.FirmId && x.SessionEpoch == actor.SessionEpoch)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1))
          : await writer.RoleGrants.Where(x => x.Id == targetGrant && x.FirmId == seed.FirmId && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        await tx.CommitAsync();
      }
      await using var verifier = new AuditSphereDbContext(pg.Options);
      var observed = change == "SESSION"
        ? await verifier.Users.AsNoTracking().AnyAsync(x => x.Id == user.Id && x.SessionEpoch == actor.SessionEpoch + 1)
        : await verifier.RoleGrants.AsNoTracking().AnyAsync(x => x.Id == targetGrant && x.RevokedAt != null);
      return (updated, observed);
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(race).Options);
    if (workflow)
    {
      var result = await EngagementWorkflowWorkspaceQuery.GetAsync(reader, actor, seed.EngagementId);
      Assert.True(race.Fired); Assert.True(race.ObservedCommitted); Assert.Equal(1, race.UpdatedRows);
      if (!afterSnapshot && change is ("SESSION" or "READ")) { Assert.False(result.Succeeded); Assert.Null(result.Value); }
      else
      {
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(CanonicalEngagementStages.ProposalGeneration, result.Value!.CanonicalStage);
        Assert.Equal(EngagementWorkflowPhaseStatuses.Blocked, result.Value.CurrentPhase.Status);
        if (change == "DETAIL" && !afterSnapshot)
          Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], result.Value.Blockers);
        else if (change is "DETAIL" or "READ")
          Assert.Contains("Commercial proposal preparation and dispatch required", result.Value.Blockers);
        if (change == "ACTION")
        {
          if (afterSnapshot) Assert.Single(result.Value.NextActions);
          else Assert.Empty(result.Value.NextActions);
        }
      }
    }
    else
    {
      var result = await EngagementLifecycleQuery.GetAsync(reader, actor, seed.EngagementId);
      Assert.True(race.Fired); Assert.True(race.ObservedCommitted); Assert.Equal(1, race.UpdatedRows);
      if (!afterSnapshot && change is ("SESSION" or "READ")) { Assert.False(result.Succeeded); Assert.Null(result.Value); }
      else
      {
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(CanonicalEngagementStages.ProposalGeneration, result.Value!.Summary.CanonicalStage);
        if (change == "DETAIL" && !afterSnapshot)
          Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], result.Value.Summary.BlockedReasons);
        else if (change is "DETAIL" or "READ")
          Assert.Contains("Commercial proposal preparation and dispatch required", result.Value.Summary.BlockedReasons);
      }
    }
  }

  [Fact]
  public void CurrentProcedureProjectionRequiresVerifiedCurrentNotApplicableEvidence()
  {
    var now = DateTimeOffset.UtcNow;
    var notApplicable = new AuditProcedure
    {
      ApplicabilityStatus = AuditApplicabilityStatuses.NotApplicableApproved,
      Status = AuditProcedureStatuses.Planned,
      CurrentResultRevision = 0
    };
    Assert.False(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(notApplicable, null, null));
    Assert.True(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(notApplicable, null, null,
      currentNotApplicableApprovalVerified: true));

    var applicable = new AuditProcedure
    {
      ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
      Status = AuditProcedureStatuses.Submitted,
      CurrentResultRevision = 2
    };
    var submitted = new AuditProcedureResult { Revision = 2, Status = AuditProcedureResultStatuses.Submitted };
    Assert.True(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(applicable, submitted, null));
    Assert.False(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(applicable,
      new AuditProcedureResult { Revision = 1, Status = AuditProcedureResultStatuses.Submitted }, null));

    var returned = new AuditProcedureResult { Revision = 2, Status = AuditProcedureResultStatuses.ChangesRequired };
    var returnReview = new AuditProcedureReview { ResultRevision = 2, Decision = AuditProcedureReviewDecisions.ChangesRequired, CreatedAt = now };
    Assert.False(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(applicable, returned, returnReview));
    Assert.False(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(applicable, submitted, returnReview));
  }

  [Fact]
  public void SignatureClockKeepsDeliveryPhaseUntilExactBundleIsAvailableAndPartnerPhaseDoesNotNeedOpinion()
  {
    Assert.Equal(CanonicalEngagementStages.DeliverableRelease, EngagementLifecycleQuery.PostSignatureStage(false, false));
    Assert.Equal(CanonicalEngagementStages.DeliverableRelease, EngagementLifecycleQuery.PostSignatureStage(true, false));
    Assert.Equal(CanonicalEngagementStages.DeliverableRelease, EngagementLifecycleQuery.PostSignatureStage(false, true));
    Assert.Equal(CanonicalEngagementStages.ComplianceCountdown, EngagementLifecycleQuery.PostSignatureStage(true, true));
    Assert.True(EngagementLifecycleQuery.PartnerHandoverReady(completionBlockerCount: 0));
    Assert.False(EngagementLifecycleQuery.PartnerHandoverReady(completionBlockerCount: 1));
  }

  [Fact]
  public async Task EngagementTraversesCanonicalStagesAndEnforcesScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");
    var unauthorized = PbcSeed.Actor(seed.Staff, "Staff");
    var scopedStaff = PbcSeed.User(seed.FirmId, "Staff");
    var mixedStaff = PbcSeed.User(seed.FirmId, "Staff");
    var now = DateTimeOffset.UtcNow;

    Guid engagementId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));

      var engagement = new Engagement
      {
        Id = Guid.NewGuid(),
        FirmId = seed.FirmId,
        PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit",
        PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31",
        ServiceProfileId = "CommercialAudit",
        Status = "Draft",
        ProfessionalWorkBlocked = true,
        CreatedAt = now
      };
      db.Engagements.Add(engagement);
      db.Users.AddRange(scopedStaff, mixedStaff);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, scopedStaff, "Staff", seed.ClientId, engagement.Id));
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, mixedStaff, "Staff"));
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, mixedStaff, "Partner", seed.ClientId, engagement.Id));
      await db.SaveChangesAsync();
      engagementId = engagement.Id;

      // 1. Initial draft without proposal -> ProposalGeneration
      var report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, report.Value!.Summary.CanonicalStage);
      Assert.Equal(2, report.Value.Summary.StageIndex);
      Assert.Contains("Commercial proposal preparation and dispatch required", report.Value.Summary.BlockedReasons);
      Assert.Null(report.Value.Summary.BlockerDetails);
      Assert.Null(report.Value.Summary.LocalArchiveState);
      Assert.Null(report.Value.Summary.ProviderProtectionState);

      // 2. A draft proposal remains at ProposalGeneration until it has actually been dispatched.
      var lead = new Lead
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, Name = "Synthetic lifecycle client",
        Source = "Referral", CreatedAt = now
      };
      var opportunity = new Opportunity
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, LeadId = lead.Id, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", EntityScope = "TEST", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now
      };
      db.Leads.Add(lead);
      db.Opportunities.Add(opportunity);
      db.Proposals.Add(new Proposal
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        OpportunityId = opportunity.Id, Status = CrmStates.ProposalDraft,
        Fee = 50000m, Currency = "QAR",
        ServiceProfileId = "AUDIT-2026", Scope = "Statutory Financial Audit",
        Deliverables = "Audit Report and Management Letter",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31"
      });
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, report.Value!.Summary.CanonicalStage);
      Assert.Equal(2, report.Value.Summary.StageIndex);

      var proposal = await db.Proposals.SingleAsync(x => x.FirmId == seed.FirmId && x.PracticeClientId == seed.ClientId);
      proposal.Status = CrmStates.ProposalInternalReview;
      await db.SaveChangesAsync();
      var dispatch = await SeedDispatchedOfferAsync(db, proposal, partner.UserId, now, "director@lifecycle.example.test");
      await db.SaveChangesAsync();
      var sentOffer = dispatch.OfferSha256!;

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Equal(3, report.Value.Summary.StageIndex);
      Assert.Equal(["Unconditional Partner risk acceptance decision required", "Client commercial acceptance of proposal required"],
        report.Value.Summary.BlockedReasons);
      var detail = Assert.Single(report.Value.Summary.BlockerDetails!);
      Assert.Equal(0, detail.BlockerIndex);
      Assert.Equal("PARTNER_RISK_ACCEPTANCE_REQUIRED", detail.Code);
      Assert.Equal(report.Value.Summary.BlockedReasons[detail.BlockerIndex], detail.Cause);
      Assert.Equal("Partner", detail.ResponsibleRole);
      Assert.Contains("Waiting", detail.Guidance, StringComparison.Ordinal);

      var workflow = await EngagementWorkflowWorkspaceQuery.GetAsync(db, partner, engagementId);
      Assert.True(workflow.Succeeded, workflow.Message);
      Assert.Equal(report.Value.Summary.CanonicalStage, workflow.Value!.CanonicalStage);
      Assert.Equal(report.Value.Summary.BlockedReasons, workflow.Value.Blockers);
      Assert.Equal(detail, Assert.Single(workflow.Value.BlockerDetails!));
      Assert.Equal(EngagementWorkflowPhaseStatuses.Blocked, workflow.Value.CurrentPhase.Status);

      var scopedActor = PbcSeed.Actor(scopedStaff, "Staff");
      var scopedLifecycle = await EngagementLifecycleQuery.GetAsync(db, scopedActor, engagementId);
      Assert.True(scopedLifecycle.Succeeded, scopedLifecycle.Message);
      Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."],
        scopedLifecycle.Value!.Summary.BlockedReasons);
      Assert.Null(scopedLifecycle.Value.Summary.BlockerDetails);
      var scopedWorkflow = await EngagementWorkflowWorkspaceQuery.GetAsync(db, scopedActor, engagementId);
      Assert.True(scopedWorkflow.Succeeded, scopedWorkflow.Message);
      Assert.Equal(scopedLifecycle.Value.Summary.BlockedReasons, scopedWorkflow.Value!.Blockers);
      Assert.Null(scopedWorkflow.Value.BlockerDetails);
      // A firm-wide staff grant and a scoped Partner grant do not combine into firm-wide Partner authority.
      var mixedLifecycle = await EngagementLifecycleQuery.GetAsync(db, PbcSeed.Actor(mixedStaff, "Partner"), engagementId);
      Assert.True(mixedLifecycle.Succeeded, mixedLifecycle.Message);
      Assert.Equal(scopedLifecycle.Value.Summary.BlockedReasons, mixedLifecycle.Value!.Summary.BlockedReasons);
      Assert.Null(mixedLifecycle.Value.Summary.BlockerDetails);

      // Lose detailed access after the typed fact was evaluated, while the scoped read grant survives.
      foreach (var workflowProjection in new[] { false, true })
      {
        await AssertProjectionAuthorizationRaceAsync(pg, seed, engagementId, workflowProjection, "DETAIL", typedBlocker: true);
        await AssertProjectionAuthorizationRaceAsync(pg, seed, engagementId, workflowProjection, "DETAIL", typedBlocker: true, lateDetail: true);
      }
      await AssertStaffingSnapshotRaceAsync(pg, seed, engagementId, afterSnapshot: false);
      await AssertStaffingSnapshotRaceAsync(pg, seed, engagementId, afterSnapshot: true);

      // 3. Add partner unconditional acceptance -> AdvanceBilling
      var decisionId = Guid.NewGuid();
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = decisionId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", Decision = "Accepted", DecidedByUserId = seed.Admin.Id,
        DecidedAt = now, Path = "NEW_CLIENT", Generation = 1,
        Rationale = "Partner assessment passed",
        EvaluationTemplateVersion = "V2",
        EvaluationSnapshotDigest = new string('a', 64)
      });
      await db.SaveChangesAsync();

      // WF-01 correction: a Partner's unconditional clearance alone never advances billing while the client has not accepted.
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Equal(3, report.Value.Summary.StageIndex);
      Assert.Contains("Client commercial acceptance of proposal required", report.Value.Summary.BlockedReasons);
      Assert.Null(report.Value.Summary.BlockerDetails);
      workflow = await EngagementWorkflowWorkspaceQuery.GetAsync(db, partner, engagementId);
      Assert.True(workflow.Succeeded, workflow.Message);
      Assert.Equal(EngagementWorkflowPhaseStatuses.Blocked, workflow.Value!.CurrentPhase.Status);
      Assert.Null(workflow.Value.BlockerDetails);

      // A real immutable response to the exact dispatch opens the client key.
      var accepted = await PracticeCrmService.RecordProposalResponseAsync(db, partner, proposal.Id,
        new("ACCEPTED", null, sentOffer, "Synthetic director", dispatch.Recipient,
          "Synthetic signed acceptance evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(accepted.Succeeded, accepted.Message);

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);

      // A tampered legacy projection that cites another offer identity cannot shadow its immutable receipt.
      proposal = await db.Proposals.SingleAsync(x => x.FirmId == seed.FirmId && x.Id == proposal.Id);
      var acceptedOffer = proposal.ResponseOfferSha256;
      proposal.ResponseOfferSha256 = new string('d', 64);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Contains("Client acceptance does not cite the current dispatched offer or quotation revision; re-record acceptance against the exact current offer.",
        report.Value.Summary.BlockedReasons);

      // Restoring the projection to the exact immutable receipt returns the workflow to AdvanceBilling.
      proposal.ResponseOfferSha256 = acceptedOffer;
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);
      Assert.Equal(4, report.Value.Summary.StageIndex);
      Assert.Contains("Issue the Engagement Letter for the current accepted offer", report.Value.Summary.BlockedReasons);
      Assert.Contains("Create the fee agreement and its 50% advance milestone", report.Value.Summary.BlockedReasons);

      // An engagement letter can only be issued by the protected command (the database refuses a hand-written letter without
      // a signature, seal and current risk clearance), so the issued-letter branch is covered by the real-command readiness tests.
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);
      Assert.Contains("Issue the Engagement Letter for the current accepted offer", report.Value.Summary.BlockedReasons);

      // 4. Activate engagement -> PortalActivePlanning
      db.EngagementActivations.Add(new EngagementActivation
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        EngagementId = engagementId, AcceptanceDecisionId = decisionId,
        ClientGeneration = 1, AcceptancePath = "NEW_CLIENT",
        ActivatedByUserId = seed.Admin.Id, ActivatedAt = now
      });
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.PortalActivePlanning, report.Value!.Summary.CanonicalStage);
      Assert.Equal(5, report.Value.Summary.StageIndex);
      Assert.Contains("Trial balance intake and acceptance required", report.Value.Summary.BlockedReasons);

      // 5. Add TB & approve materiality & unblock professional work -> FieldworkExecution
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, SourceKind = "Raw", Revision = 1,
        LegalEntityKey = "PBC TEST CLIENT", Currency = "QAR"
      });
      // A status flag alone does not approve planning (STE-GAP-008 B): the legacy assessment needs a separate Partner's recorded approval.
      var independentPartner = PbcSeed.User(seed.FirmId, "Staff");
      db.Users.Add(independentPartner);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, independentPartner, "Partner"));
      await db.SaveChangesAsync();
      var materialityAssessment = new MaterialityAssessment
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, Status = MaterialityStatuses.Approved,
        BenchmarkSource = "TrialBalance", BenchmarkVersion = "1.0",
        Rationale = "Standard revenue benchmark applied",
        BenchmarkAmount = 1000000m, RateApplied = 0.01m,
        OverallMateriality = 10000m, PerformanceMateriality = 7500m,
        ClearlyTrivialThreshold = 500m, ActorId = seed.Admin.Id, CreatedAt = now
      };
      db.MaterialityAssessments.Add(materialityAssessment);
      db.MaterialityApprovals.Add(new MaterialityApproval
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        MaterialityAssessmentId = materialityAssessment.Id, ApprovedByUserId = independentPartner.Id, ApprovedAt = DateTimeOffset.UtcNow
      });
      engagement.ProfessionalWorkBlocked = false;
      engagement.Status = "Active";

      var proc = new AuditProcedure
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, Title = "Test Cash & Bank", Status = AuditProcedureStatuses.Planned,
        ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
        CurrentResultRevision = 0, CreatedAt = now
      };
      var approvedNotApplicable = new AuditProcedure
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        SourceProcedureId = "NA-APPROVED", Title = "Synthetic approved not-applicable procedure",
        Status = AuditProcedureStatuses.Planned, ApplicabilityStatus = AuditApplicabilityStatuses.NotApplicableApproved,
        ApplicabilityRationale = "Outside this engagement's approved scope.", ApplicabilityDecidedByUserId = seed.Admin.Id,
        ApplicabilityDecidedAt = now, CreatedAt = now
      };
      db.AuditProcedures.AddRange(proc, approvedNotApplicable);
      await db.SaveChangesAsync();

      // This is a real current approval, not a legacy cache that merely says NA_APPROVED.
      var applicabilityProposal = new AuditProcedureApplicabilityDecision
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureId = approvedNotApplicable.Id, Revision = 1, DefinitionRevision = 0, InputGeneration = 1,
        Action = AuditApplicabilityActions.ProposeNotApplicable, Rationale = "Outside this engagement's approved scope.",
        CommandId = Guid.NewGuid(), RequestDigest = new string('a', 64), ActorUserId = seed.Staff.Id,
        ActorEpoch = seed.Staff.SessionEpoch, OccurredAt = now
      };
      db.AuditProcedureApplicabilityDecisions.Add(applicabilityProposal);
      await db.SaveChangesAsync();
      approvedNotApplicable.ApplicabilityStatus = AuditApplicabilityStatuses.NotApplicablePendingReview;
      approvedNotApplicable.CurrentApplicabilityRevision = applicabilityProposal.Revision;
      approvedNotApplicable.CurrentApplicabilityDecisionId = applicabilityProposal.Id;
      approvedNotApplicable.ApplicabilityDecidedByUserId = applicabilityProposal.ActorUserId;
      approvedNotApplicable.ApplicabilityDecidedAt = applicabilityProposal.OccurredAt;
      await db.SaveChangesAsync();

      var applicabilityApproval = new AuditProcedureApplicabilityDecision
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureId = approvedNotApplicable.Id, Revision = 2, DefinitionRevision = 0, InputGeneration = 1,
        Action = AuditApplicabilityActions.ApproveNotApplicable, ProposalDecisionId = applicabilityProposal.Id,
        PreviousDecisionId = applicabilityProposal.Id, CommandId = Guid.NewGuid(), RequestDigest = new string('b', 64),
        ActorUserId = seed.Admin.Id, ActorEpoch = seed.Admin.SessionEpoch, OccurredAt = now.AddSeconds(1)
      };
      db.AuditProcedureApplicabilityDecisions.Add(applicabilityApproval);
      approvedNotApplicable.ApplicabilityStatus = AuditApplicabilityStatuses.NotApplicableApproved;
      approvedNotApplicable.CurrentApplicabilityRevision = applicabilityApproval.Revision;
      approvedNotApplicable.CurrentApplicabilityDecisionId = applicabilityApproval.Id;
      approvedNotApplicable.ApplicabilityDecidedByUserId = applicabilityApproval.ActorUserId;
      approvedNotApplicable.ApplicabilityDecidedAt = applicabilityApproval.OccurredAt;
      approvedNotApplicable.ApplicabilityRationale = applicabilityProposal.Rationale;
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.FieldworkExecution, report.Value!.Summary.CanonicalStage);
      Assert.Equal(6, report.Value.Summary.StageIndex);

      // 6. Execute procedures -> ManagerialReview
      proc.CurrentResultRevision = 1;
      proc.Status = AuditProcedureStatuses.Submitted;
      db.AuditProcedureResults.Add(new AuditProcedureResult
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureId = proc.Id, Revision = 1, InputGeneration = 1, WorkPerformed = "Synthetic procedure work submitted.",
        StructuredResultJson = "{}", EvidenceReferencesJson = "[]", Conclusion = "Awaiting manager review.",
        Status = AuditProcedureResultStatuses.Submitted, PreparedByUserId = seed.Admin.Id, SubmittedAt = now
      });
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ManagerialReview, report.Value!.Summary.CanonicalStage);
      Assert.Equal(7, report.Value.Summary.StageIndex);

      // Approved N/A contributes no fictitious result. An unreviewed submitted result remains in Managerial Review.
      Assert.DoesNotContain(report.Value.Summary.BlockedReasons, x => x.Contains("not-applicable", StringComparison.OrdinalIgnoreCase));

      // A current returned result routes back to preparer fieldwork, even though an earlier revision was submitted.
      var returned = new AuditProcedureResult
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureId = proc.Id, Revision = 2, InputGeneration = 1, WorkPerformed = "Synthetic rework required.",
        StructuredResultJson = "{}", EvidenceReferencesJson = "[]", Conclusion = "Revise the evidence linkage.",
        Status = AuditProcedureResultStatuses.ChangesRequired, PreparedByUserId = seed.Admin.Id, SubmittedAt = now.AddSeconds(1)
      };
      proc.CurrentResultRevision = 2;
      proc.Status = AuditProcedureStatuses.ChangesRequired;
      db.AuditProcedureResults.Add(returned);
      db.AuditProcedureReviews.Add(new AuditProcedureReview
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureResultId = returned.Id, AuditProcedureId = proc.Id, ResultRevision = returned.Revision,
        Decision = AuditProcedureReviewDecisions.ChangesRequired, Comment = "Revise the evidence linkage.",
        ReviewerUserId = seed.Admin.Id, CreatedAt = now.AddSeconds(2)
      });
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.FieldworkExecution, report.Value!.Summary.CanonicalStage);
      Assert.Contains(report.Value.Summary.BlockedReasons, x => x.Contains("returned for preparer rework", StringComparison.Ordinal));

      // A new submitted revision returns to Managerial Review; the historical returned revision does not control the projection.
      proc.CurrentResultRevision = 3;
      proc.Status = AuditProcedureStatuses.Submitted;
      db.AuditProcedureResults.Add(new AuditProcedureResult
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureId = proc.Id, Revision = 3, InputGeneration = 1, WorkPerformed = "Synthetic corrected procedure work submitted.",
        StructuredResultJson = "{}", EvidenceReferencesJson = "[]", Conclusion = "Resubmitted for review.",
        Status = AuditProcedureResultStatuses.Submitted, PreparedByUserId = seed.Admin.Id, SubmittedAt = now.AddSeconds(3)
      });
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.ManagerialReview, report.Value!.Summary.CanonicalStage);
      Assert.Equal(7, report.Value.Summary.StageIndex);

      // 7. Add partner clearance and opinion -> PartnerApproval
      var srm = new AuditDeliverable
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, Kind = DeliverableKinds.SummaryReviewMemorandum,
        Version = 1, InputDigest = new string('1', 64), ContentSha256 = new string('2', 64),
        Content = [1, 2, 3], CreatedByUserId = seed.Admin.Id, CreatedAt = now
      };
      db.AuditDeliverables.Add(srm);

      var clearance = new PartnerCompletionClearance
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, SummaryReviewMemorandumId = srm.Id,
        KeyRiskAreasComment = "Key risk areas cleared by partner",
        FinancialStatementNotesComment = "Notes reviewed and cleared",
        PartnerUserId = seed.Admin.Id, ClearedAt = now
      };
      db.PartnerCompletionClearances.Add(clearance);
      db.AuditOpinionDecisions.Add(new AuditOpinionDecision
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, PartnerClearanceId = clearance.Id,
        OpinionType = AuditOpinionTypes.Unmodified, DecidedByUserId = seed.Admin.Id,
        DecidedAt = now
      });
      await db.SaveChangesAsync();

      // A hand-built clearance bound to a stale synthetic SRM is not current and cannot hand over the workflow.
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ManagerialReview, report.Value!.Summary.CanonicalStage);
      Assert.Equal(7, report.Value.Summary.StageIndex);
      Assert.Contains(report.Value.Summary.BlockedReasons,
        x => x.StartsWith("A current Summary Review Memorandum is required for Partner handover.", StringComparison.Ordinal));

      // 8. Sign report deliverable -> DeliverableRelease
      var signedReport = new AuditDeliverable
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, Kind = DeliverableKinds.IndependentAuditorsReport,
        Version = 1, InputDigest = new string('3', 64), ContentSha256 = new string('4', 64),
        Content = [1, 2, 3], SignedFromDeliverableId = srm.Id, CreatedByUserId = seed.Admin.Id, CreatedAt = now
      };
      db.AuditDeliverables.Add(signedReport);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.DeliverableRelease, report.Value!.Summary.CanonicalStage);
      Assert.Equal(9, report.Value.Summary.StageIndex);

      // A signature starts the clock, but Report & Delivery remains current until the exact final bundle is available in the client portal.
      var freeze = new EngagementFileFreeze
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, ReportDeliverableId = signedReport.Id,
        ReportSignedAt = now.AddDays(-10), DueAt = now.AddDays(-10).AddDays(60),
        Revision = 1, ExternalReadOnly = "NOT_REQUESTED",
        State = FileFreezeStates.Scheduled, UpdatedAt = now
      };
      db.EngagementFileFreezes.Add(freeze);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.DeliverableRelease, report.Value!.Summary.CanonicalStage);
      Assert.Equal(9, report.Value.Summary.StageIndex);
      Assert.NotNull(report.Value.Summary.ComplianceCountdownDays);
      Assert.True(report.Value.Summary.ComplianceCountdownDays <= 51);
      Assert.Contains(report.Value.Summary.BlockedReasons, x => x.Contains("final bundle available in the client portal", StringComparison.Ordinal));
      Assert.Equal(FileFreezeStates.Scheduled, report.Value.Summary.LocalArchiveState);
      Assert.Equal(ExternalReadOnlyStates.NotRequested, report.Value.Summary.ProviderProtectionState);
      Assert.Contains("does not claim provider immutability", report.Value.Summary.ComplianceWarning!);

      // The dashboard names the people staffed at the level that owns the step. A revoked assignment and another level are not named.
      var unstaffed = await EngagementWorkflowWorkspaceQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(("EngagementPartner", 0), (unstaffed.Value!.ResponsibleRole, unstaffed.Value.ResponsibleUsers.Count));
      Assert.Equal("REPORT_DELIVERY", unstaffed.Value.CurrentPhase.Code);
      var staffedPartner = PbcSeed.User(seed.FirmId, "Staff"); staffedPartner.DisplayName = "Staffed Partner";
      var formerPartner = PbcSeed.User(seed.FirmId, "Staff"); formerPartner.DisplayName = "Former Partner";
      var staffedManager = PbcSeed.User(seed.FirmId, "Staff"); staffedManager.DisplayName = "Staffed Manager";
      db.Users.AddRange(staffedPartner, formerPartner, staffedManager);
      foreach (var (user, level, revoked) in new[]
      {
        (staffedPartner, StaffingLevels.EngagementPartner, false), (formerPartner, StaffingLevels.EngagementPartner, true),
        (staffedManager, StaffingLevels.AuditManager, false)
      })
      {
        var grant = PbcSeed.Grant(seed.FirmId, user, "Partner", seed.ClientId, engagementId);
        db.RoleGrants.Add(grant);
        db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
        {
          Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId, UserId = user.Id,
          RoleGrantId = grant.Id, StaffingLevel = level, AssignedByUserId = seed.Admin.Id, AssignedAt = now,
          RevokedAt = revoked ? now : null, RevokedByUserId = revoked ? seed.Admin.Id : null
        });
      }
      await db.SaveChangesAsync();
      var staffed = await EngagementWorkflowWorkspaceQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(["Staffed Partner"], staffed.Value!.ResponsibleUsers);
      Assert.Equal(report.Value.Summary.ComplianceCountdownDays, staffed.Value.Archive!.ComplianceCountdownDays);

      // Revoke the displayed person's assignment at the workflow's final authorization boundary.
      var staffedAssignmentId = db.EngagementStaffAssignments.Local.Single(x => x.UserId == staffedPartner.Id).Id;
      var staffingRead = new WorkflowProjectionAnchor(engagementId);
      var observedBeforeMutation = false;
      var staffingRace = new ProjectionSnapshotRace(workflow: true, afterSnapshot: false, async () =>
      {
        observedBeforeMutation = staffingRead.Observed;
        int updated;
        await using (var writer = new AuditSphereDbContext(pg.Options))
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
          updated = await writer.EngagementStaffAssignments.Where(x => x.Id == staffedAssignmentId &&
              x.FirmId == seed.FirmId && x.EngagementId == engagementId && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)
              .SetProperty(x => x.RevokedByUserId, seed.Admin.Id));
          await transaction.CommitAsync();
        }
        await using var verifier = new AuditSphereDbContext(pg.Options);
        var observed = await verifier.EngagementStaffAssignments.AsNoTracking()
          .AnyAsync(x => x.Id == staffedAssignmentId && x.RevokedAt != null);
        return (updated, observed);
      });
      try
      {
        await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
          .AddInterceptors(staffingRead, staffingRace).Options);
        var changed = await EngagementWorkflowWorkspaceQuery.GetAsync(reader, partner, engagementId);
        Assert.True(staffingRace.Fired);
        Assert.True(observedBeforeMutation);
        Assert.Equal(1, staffingRace.UpdatedRows);
        Assert.True(staffingRace.ObservedCommitted);
        Assert.True(changed.Succeeded, changed.Message);
        Assert.Empty(changed.Value!.ResponsibleUsers);
      }
      finally
      {
        await using var restore = new AuditSphereDbContext(pg.Options);
        await restore.EngagementStaffAssignments.Where(x => x.Id == staffedAssignmentId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTimeOffset?)null)
            .SetProperty(x => x.RevokedByUserId, (Guid?)null));
      }

      // An expired clock without final delivery or a committed freeze stays in Report & Delivery; elapsed time is not archive evidence.
      freeze.ReportSignedAt = now.AddDays(-61);
      freeze.DueAt = freeze.ReportSignedAt.AddDays(60);
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DeliverableRelease, report.Value!.Summary.CanonicalStage);
      Assert.Equal(0, report.Value.Summary.ComplianceCountdownDays.GetValueOrDefault());
      Assert.False(report.Value.Summary.IsArchived);
      Assert.Contains(report.Value.Summary.BlockedReasons, x => x.Contains("freeze is due but has not committed", StringComparison.Ordinal));

      // 10. Lock / freeze -> ArchivedReadOnly
      freeze.State = FileFreezeStates.Frozen;
      freeze.FrozenAt = now.AddDays(-1);
      freeze.ReportSignedAt = now.AddDays(-61);
      freeze.DueAt = now.AddDays(-61).AddDays(60);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ArchivedReadOnly, report.Value!.Summary.CanonicalStage);
      Assert.Equal(11, report.Value.Summary.StageIndex);
      Assert.True(report.Value.Summary.IsArchived);
      Assert.Equal(FileFreezeStates.Frozen, report.Value.Summary.LocalArchiveState);
      Assert.Equal(ExternalReadOnlyStates.NotRequested, report.Value.Summary.ProviderProtectionState);
      Assert.Contains("does not claim provider immutability", report.Value.Summary.ComplianceWarning!);

      // Archived reports have no blockers, but still require current engagement and session authority.
      foreach (var workflowProjection in new[] { false, true })
      foreach (var change in new[] { "SCOPE", "EPOCH" })
        await AssertProjectionAuthorizationRaceAsync(pg, seed, engagementId, workflowProjection, change, typedBlocker: false);

      freeze.ExternalReadOnly = ExternalReadOnlyStates.Observed;
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(FileFreezeStates.Frozen, report.Value!.Summary.LocalArchiveState);
      Assert.Equal(ExternalReadOnlyStates.Observed, report.Value.Summary.ProviderProtectionState);
      Assert.Null(report.Value.Summary.ComplianceWarning);

      // 11. Unauthorized actor is scope-denied
      var denied = await EngagementLifecycleQuery.GetAsync(db, unauthorized, engagementId);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
  }

  /// <summary>
  /// Advance billing reads the same evidence as the protected letter guard. Mutating one key at a time shows that a stale client
  /// generation, a conditional Partner decision that supersedes the unconditional one, or a declined proposal returns the
  /// engagement to Dual-Key Pending, and that restoring the evidence restores the stage.
  /// </summary>
  [Fact]
  public async Task AdvanceBillingNeverAdvancesOnStaleConditionalOrDeclinedEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");
    var now = DateTimeOffset.UtcNow;
    Guid engagementId = Guid.NewGuid();
    Guid unconditionalId = Guid.NewGuid();
    var conditionalId = Guid.NewGuid();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
      var lead = new Lead { Id = Guid.NewGuid(), FirmId = seed.FirmId, Name = "Synthetic matrix client", Source = "Referral", CreatedAt = now };
      var opportunity = new Opportunity
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, LeadId = lead.Id, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now
      };
      var proposal = new Proposal
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, OpportunityId = opportunity.Id,
        Status = CrmStates.ProposalInternalReview, Fee = 50000m, Currency = "QAR", ServiceProfileId = "AUDIT-2026",
        Scope = "Statutory Financial Audit", Deliverables = "Audit Report", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        CreatedAt = now
      };
      db.Engagements.Add(new Engagement
      {
        Id = engagementId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ServiceProfileId = "CommercialAudit", Status = "Draft",
        ProfessionalWorkBlocked = true, CreatedAt = now
      });
      db.Leads.Add(lead);
      db.Opportunities.Add(opportunity);
      db.Proposals.Add(proposal);
      await db.SaveChangesAsync();
      var dispatch = await SeedDispatchedOfferAsync(db, proposal, seed.Admin.Id, now, "owner@matrix.example.test");
      await db.SaveChangesAsync();
      var offerHash = dispatch.OfferSha256!;
      var response = await PracticeCrmService.RecordProposalResponseAsync(db, partner, proposal.Id,
        new("ACCEPTED", null, offerHash, "Synthetic director", dispatch.Recipient,
          "Synthetic signed acceptance evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(response.Succeeded, response.Message);
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = unconditionalId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        Decision = "Accepted", DecidedByUserId = seed.Admin.Id, DecidedAt = now, Path = "NEW_CLIENT", Generation = 1,
        Rationale = "Partner assessment passed", EvaluationTemplateVersion = "V2", EvaluationSnapshotDigest = new string('a', 64)
      });
      await db.SaveChangesAsync();

      // Both keys current -> AdvanceBilling (the letter and fee agreement remain blockers, asserted in the traversal test).
      var report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);

      // The client generation moves on: the Partner clearance is stale and must not carry billing.
      await db.ClientSafetyStates.Where(x => x.Id == seed.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, 2));
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Contains("Unconditional Partner risk acceptance decision required", report.Value.Summary.BlockedReasons);
      Assert.Equal("PARTNER_RISK_ACCEPTANCE_REQUIRED", Assert.Single(report.Value.Summary.BlockerDetails!).Code);

      await db.ClientSafetyStates.Where(x => x.Id == seed.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, 1));
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);
      Assert.Null(report.Value.Summary.BlockerDetails);

      // A later conditional decision supersedes the unconditional one for the same generation: it is not a clearance.
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = conditionalId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        Decision = "AcceptedWithConditions", Conditions = "Obtain bank confirmation before fieldwork",
        DecidedByUserId = seed.Admin.Id, DecidedAt = now.AddSeconds(1), Path = "NEW_CLIENT", Generation = 1,
        Rationale = "Conditional assessment", EvaluationTemplateVersion = "V2", EvaluationSnapshotDigest = new string('b', 64)
      });
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Equal("PARTNER_RISK_ACCEPTANCE_REQUIRED", Assert.Single(report.Value.Summary.BlockerDetails!).Code);

      // Acceptance decisions are append-only, so the conditional decision stays; a newer unconditional decision supersedes it.
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        Decision = "Accepted", DecidedByUserId = seed.Admin.Id, DecidedAt = now.AddSeconds(2), Path = "NEW_CLIENT", Generation = 1,
        Rationale = "Conditions discharged", EvaluationTemplateVersion = "V2", EvaluationSnapshotDigest = new string('d', 64)
      });
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);

      // A later immutable decline is the current offer and never inherits the prior proposal's acceptance.
      var declineLead = new Lead { Id = Guid.NewGuid(), FirmId = seed.FirmId, Name = "Synthetic declined proposal", Source = "Referral", CreatedAt = now.AddSeconds(5) };
      var declineOpportunity = new Opportunity { Id = Guid.NewGuid(), FirmId = seed.FirmId, LeadId = declineLead.Id, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now.AddSeconds(5) };
      var declined = new Proposal { Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, OpportunityId = declineOpportunity.Id,
        Status = CrmStates.ProposalInternalReview, Fee = 50000m, Currency = "QAR", ServiceProfileId = "AUDIT-2026",
        Scope = "Statutory Financial Audit", Deliverables = "Audit Report", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = now.AddSeconds(5) };
      db.Leads.Add(declineLead);
      db.Opportunities.Add(declineOpportunity);
      db.Proposals.Add(declined);
      await db.SaveChangesAsync();
      var declineDispatch = await SeedDispatchedOfferAsync(db, declined, seed.Admin.Id, now.AddSeconds(5), "owner@matrix.example.test");
      await db.SaveChangesAsync();
      var declineResponse = await PracticeCrmService.RecordProposalResponseAsync(db, partner, declined.Id,
        new("DECLINED", "Revise scope", declineDispatch.OfferSha256!, "Synthetic director", declineDispatch.Recipient,
          "Synthetic decline evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(declineResponse.Succeeded, declineResponse.Message);
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Contains("The client declined the dispatched proposal; revise and resend or close the opportunity.", report.Value.Summary.BlockedReasons);
    }
  }

  [Fact]
  public async Task PriorPeriodAcceptedOfferDoesNotAdvanceANewPeriodEngagement()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");
    var now = DateTimeOffset.UtcNow;
    var engagementId = Guid.NewGuid();
    var leadId = Guid.NewGuid();
    var opportunityId = Guid.NewGuid();
    var proposalId = Guid.NewGuid();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
      db.Engagements.Add(new Engagement
      {
        Id = engagementId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        PeriodStart = "2027-01-01", PeriodEnd = "2027-12-31", ServiceProfileId = "CommercialAudit",
        Status = "Draft", ProfessionalWorkBlocked = true, CreatedAt = now
      });
      var lead = new Lead
      {
        Id = leadId, FirmId = seed.FirmId, Name = "Synthetic annual workflow client", Source = "Referral", CreatedAt = now
      };
      var opportunity = new Opportunity
      {
        Id = opportunityId, FirmId = seed.FirmId, LeadId = leadId, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Currency = "QAR", CreatedAt = now
      };
      var proposal = new Proposal
      {
        Id = proposalId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, OpportunityId = opportunityId,
        Status = CrmStates.ProposalInternalReview, Fee = 50000m, Currency = "QAR", ServiceProfileId = "AUDIT-2026",
        Scope = "Statutory Financial Audit", Deliverables = "Audit Report", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", CreatedAt = now
      };
      db.Leads.Add(lead);
      db.Opportunities.Add(opportunity);
      db.Proposals.Add(proposal);
      await db.SaveChangesAsync();
      var dispatch = await SeedDispatchedOfferAsync(db, proposal, seed.Admin.Id, now, "owner@annual.example.test");
      await db.SaveChangesAsync();
      var accepted = await PracticeCrmService.RecordProposalResponseAsync(db, partner, proposalId,
        new("ACCEPTED", null, dispatch.OfferSha256!, "Synthetic director", dispatch.Recipient,
          "Synthetic signed acceptance evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(accepted.Succeeded, accepted.Message);
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        Decision = "Accepted", DecidedByUserId = seed.Admin.Id, DecidedAt = now, Path = AcceptancePaths.Continuance,
        Generation = 1, Rationale = "Synthetic prior-period clearance", EvaluationTemplateVersion = "V2",
        EvaluationSnapshotDigest = new string('a', 64)
      });
      await db.SaveChangesAsync();

      var report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded, report.Message);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, report.Value!.Summary.CanonicalStage);
      Assert.Contains("Commercial proposal preparation and dispatch required", report.Value.Summary.BlockedReasons);
    }
  }

  private static async Task AssertStaffingSnapshotRaceAsync(ITestPostgresDatabase pg, PbcSeed.Fixture seed,
    Guid engagementId, bool afterSnapshot)
  {
    var user = PbcSeed.User(seed.FirmId, "Staff");
    var partnerGrant = PbcSeed.Grant(seed.FirmId, user, "Partner", seed.ClientId, engagementId);
    var assignment = new EngagementStaffAssignment
    {
      Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
      UserId = user.Id, StaffingLevel = StaffingLevels.EngagementPartner, RoleGrantId = partnerGrant.Id,
      AssignedByUserId = seed.Admin.Id, AssignedAt = DateTimeOffset.UtcNow
    };
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.Add(user);
      setup.RoleGrants.Add(partnerGrant);
      setup.EngagementStaffAssignments.Add(assignment);
      await setup.SaveChangesAsync();
      var before = await EngagementWorkflowWorkspaceQuery.GetAsync(setup, PbcSeed.Actor(user, "Partner"), engagementId);
      Assert.True(before.Succeeded, before.Message);
      Assert.Single(before.Value!.NextActions);
    }
    var race = new ProjectionSnapshotRace(workflow: true, afterSnapshot, async () =>
    {
      int updated;
      await using (var writer = new AuditSphereDbContext(pg.Options))
      await using (var tx = await writer.Database.BeginTransactionAsync())
      {
        updated = await writer.EngagementStaffAssignments.Where(x => x.Id == assignment.Id && x.FirmId == seed.FirmId && x.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)
            .SetProperty(x => x.RevokedByUserId, seed.Admin.Id));
        await tx.CommitAsync();
      }
      await using var verifier = new AuditSphereDbContext(pg.Options);
      var observed = await verifier.EngagementStaffAssignments.AsNoTracking().AnyAsync(x => x.Id == assignment.Id && x.RevokedAt != null);
      return (updated, observed);
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(race).Options);
    var result = await EngagementWorkflowWorkspaceQuery.GetAsync(reader, PbcSeed.Actor(user, "Partner"), engagementId);
    Assert.True(race.Fired); Assert.True(race.ObservedCommitted); Assert.Equal(1, race.UpdatedRows);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(CanonicalEngagementStages.DualKeyPending, result.Value!.CanonicalStage);
    if (afterSnapshot) Assert.Single(result.Value.NextActions);
    else Assert.Empty(result.Value.NextActions);
  }

  private static async Task AssertProjectionAuthorizationRaceAsync(ITestPostgresDatabase pg, PbcSeed.Fixture seed,
    Guid engagementId, bool workflow, string change, bool typedBlocker, bool lateDetail = false)
  {
    var user = PbcSeed.User(seed.FirmId, "Staff");
    var detailGrant = PbcSeed.Grant(seed.FirmId, user, "Manager");
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.Add(user);
      setup.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, user, "Staff", seed.ClientId, engagementId));
      if (typedBlocker) setup.RoleGrants.Add(detailGrant);
      await setup.SaveChangesAsync();
      var before = await EngagementLifecycleQuery.GetAsync(setup, PbcSeed.Actor(user, "Staff"), engagementId);
      Assert.True(before.Succeeded, before.Message);
      if (typedBlocker)
        Assert.Equal("PARTNER_RISK_ACCEPTANCE_REQUIRED", Assert.Single(before.Value!.Summary.BlockerDetails!).Code);
      else
      {
        Assert.Equal(CanonicalEngagementStages.ArchivedReadOnly, before.Value!.Summary.CanonicalStage);
        Assert.Empty(before.Value.Summary.BlockedReasons);
      }
    }
    var actor = PbcSeed.Actor(user, "Staff");
    var marker = lateDetail ? "U12 engagement projection final authorization" : workflow ? "FROM workflow_task_bindings" : "FROM engagement_holds";
    var race = new LifecycleAuthorizationRace(marker, async () =>
    {
      await using var writer = new AuditSphereDbContext(pg.Options);
      if (change == "EPOCH")
        await writer.Users.Where(x => x.Id == user.Id && x.FirmId == seed.FirmId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
      else
        await writer.RoleGrants.Where(x => x.UserId == user.Id && x.FirmId == seed.FirmId &&
            (change != "DETAIL" || x.Id == detailGrant.Id))
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    }, armMarker: lateDetail && workflow ? "FROM workflow_task_bindings" : null);
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(race).Options);
    if (workflow)
    {
      var result = await EngagementWorkflowWorkspaceQuery.GetAsync(reader, actor, engagementId);
      Assert.True(race.Fired);
      if (change == "DETAIL")
      {
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(CanonicalEngagementStages.DualKeyPending, result.Value!.CanonicalStage);
        Assert.Equal(EngagementWorkflowPhaseStatuses.Blocked, result.Value.CurrentPhase.Status);
        Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], result.Value.Blockers);
        Assert.Null(result.Value.BlockerDetails);
      }
      else
      {
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(change == "EPOCH" ? ErrorCodes.GenerationStale : ErrorCodes.ScopeDenied, result.ErrorCode);
      }
    }
    else
    {
      var result = await EngagementLifecycleQuery.GetAsync(reader, actor, engagementId);
      Assert.True(race.Fired);
      if (change == "DETAIL")
      {
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(CanonicalEngagementStages.DualKeyPending, result.Value!.Summary.CanonicalStage);
        Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], result.Value.Summary.BlockedReasons);
        Assert.Null(result.Value.Summary.BlockerDetails);
      }
      else
      {
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(change == "EPOCH" ? ErrorCodes.GenerationStale : ErrorCodes.ScopeDenied, result.ErrorCode);
      }
    }
  }

  private static async Task<CommercialNotification> SeedDispatchedOfferAsync(
    AuditSphereDbContext db, Proposal proposal, Guid actorUserId, DateTimeOffset createdAt, string recipient)
  {
    var routed = await (from routing in db.ClientContactRoutings.AsNoTracking()
      join contact in db.ClientContacts.AsNoTracking() on new { routing.FirmId, routing.PracticeClientId, Id = routing.ClientContactId }
        equals new { contact.FirmId, contact.PracticeClientId, Id = contact.Id }
      where routing.FirmId == proposal.FirmId && routing.PracticeClientId == proposal.PracticeClientId &&
        routing.Purpose == CorrespondencePurposes.Commercial && routing.IsPrimaryForPurpose && routing.RevokedAt == null && contact.IsActive
      orderby routing.CreatedAt descending, routing.Id descending
      select new { Routing = routing, Contact = contact }).FirstOrDefaultAsync();
    var contactId = routed?.Contact.Id ?? Guid.NewGuid();
    var routingId = routed?.Routing.Id ?? Guid.NewGuid();
    if (routed is null)
    {
      db.ClientContacts.Add(new ClientContact
      {
        Id = contactId, FirmId = proposal.FirmId, PracticeClientId = proposal.PracticeClientId!.Value,
        FullName = "Synthetic authorized commercial contact", Email = recipient, Role = "Director", Title = "Finance Director",
        SignatoryAuthority = "Authorized commercial contact", Primary = true, IsActive = true
      });
      db.ClientContactRoutings.Add(new ClientContactRouting
      {
        Id = routingId, FirmId = proposal.FirmId, PracticeClientId = proposal.PracticeClientId.Value,
        ClientContactId = contactId, Purpose = CorrespondencePurposes.Commercial, IsPrimaryForPurpose = true,
        CreatedByUserId = actorUserId, CreatedAt = createdAt
      });
    }

    var quotationId = Guid.NewGuid();
    var documentId = Guid.NewGuid();
    var bytes = Encoding.UTF8.GetBytes($"Synthetic exact rendered offer for proposal {proposal.Id:D}.");
    var offerHash = Hashing.Sha256Hex(bytes);
    db.QuotationVersions.Add(new QuotationVersion
    {
      Id = quotationId, FirmId = proposal.FirmId, ProposalId = proposal.Id, Revision = 1,
      Currency = proposal.Currency, BaseAmount = proposal.Fee, Fee = proposal.Fee,
      InputHash = Hashing.Sha256Hex($"synthetic quotation {proposal.Id:D}"),
      Status = QuotationStates.Approved, RequiredApprovalsJson = "[]", CreatedByUserId = actorUserId,
      CreatedAt = createdAt, ApprovedAt = createdAt
    });
    db.CommercialDocuments.Add(new CommercialDocument
    {
      Id = documentId, FirmId = proposal.FirmId, ProposalId = proposal.Id, QuotationVersionId = quotationId,
      Kind = CommercialDocumentKinds.Quotation, TemplateVersion = "SYNTHETIC-QUOTATION-v1", ProfileVersion = 1,
      FileName = $"Quotation-{proposal.Id:N}.docx", ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      Bytes = bytes, Sha256Hex = offerHash, CreatedByUserId = actorUserId, CreatedAt = createdAt
    });
    proposal.Status = CrmStates.ProposalSent;
    proposal.SentAt = createdAt;
    proposal.SentOfferSha256 = offerHash;
    var dispatch = new CommercialNotification
    {
      Id = Guid.NewGuid(), FirmId = proposal.FirmId, Kind = CommercialNotificationKinds.Proposal,
      ProposalId = proposal.Id, PracticeClientId = proposal.PracticeClientId, QuotationVersionId = quotationId,
      DocumentId = documentId, OfferSha256 = offerHash, RecipientContactId = contactId, RecipientRoutingId = routingId,
      RecipientAuthorityBasis = ProposalRecipientAuthorityBases.ClientCommercialRouting, Recipient = recipient,
      Subject = "Synthetic exact rendered offer", Body = "Synthetic dispatch evidence", CreatedAt = createdAt
    };
    db.CommercialNotifications.Add(dispatch);
    return dispatch;
  }
}

internal sealed class ProgressExpiryBoundary(Guid actorId, Func<Task<(DateTimeOffset Expiry, int Rows, bool Observed)>> change)
  : DbCommandInterceptor
{
  public bool MutationCommitted { get; private set; }
  public int UpdatedRows { get; private set; }
  public bool GrantReadBeforeExpiry { get; private set; }
  public bool FinalCommitObserved { get; private set; }
  public bool ExpiryPassedBeforeRelease { get; private set; }
  private int commits;
  private DateTimeOffset expiry;

  public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
    DbDataReader result, CancellationToken cancellationToken = default)
  {
    if (MutationCommitted && !FinalCommitObserved && command.Transaction is not null &&
        command.CommandText.Contains("FROM role_grants", StringComparison.OrdinalIgnoreCase) &&
        command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, actorId)))
      GrantReadBeforeExpiry = DateTimeOffset.UtcNow < expiry;
    return ValueTask.FromResult(result);
  }

  public async Task AfterCommitAsync(CancellationToken ct)
  {
    commits++;
    if (commits == 1)
    {
      bool observed;
      (expiry, UpdatedRows, observed) = await change();
      MutationCommitted = observed;
    }
    else if (commits == 2)
    {
      FinalCommitObserved = true;
      var remaining = expiry - DateTimeOffset.UtcNow;
      if (remaining > TimeSpan.Zero) await Task.Delay(remaining + TimeSpan.FromMilliseconds(20), ct);
      ExpiryPassedBeforeRelease = DateTimeOffset.UtcNow >= expiry;
    }
  }
}

internal sealed class ProgressExpiryCommitBoundary(ProgressExpiryBoundary boundary) : DbTransactionInterceptor
{
  public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
    CancellationToken cancellationToken = default) => await boundary.AfterCommitAsync(cancellationToken);
}

internal sealed class WorkflowExpiryBoundary(Func<Task<(DateTimeOffset Expiry, int Rows, bool Observed)>> change) : DbCommandInterceptor
{
  public bool AnchorObserved { get; private set; }
  public bool MutationCommitted { get; private set; }
  public int UpdatedRows { get; private set; }
  public bool SnapshotBeforeExpiry { get; private set; }
  public bool CommitObserved { get; private set; }
  public bool ExpiryPassedBeforeRelease { get; private set; }
  private DateTimeOffset expiry;

  public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
    CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
  {
    if (command.CommandText.Contains("workflow_task_bindings", StringComparison.OrdinalIgnoreCase)) AnchorObserved = true;
    if (AnchorObserved && !MutationCommitted && command.Transaction is not null &&
        command.CommandText.Contains("U12 engagement projection final authorization", StringComparison.Ordinal))
    {
      bool observed;
      (expiry, UpdatedRows, observed) = await change();
      MutationCommitted = observed;
    }
    return result;
  }

  public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
    DbDataReader result, CancellationToken cancellationToken = default)
  {
    if (MutationCommitted && command.Transaction is not null &&
        command.CommandText.Contains("U12 engagement projection final authorization", StringComparison.Ordinal))
      SnapshotBeforeExpiry = DateTimeOffset.UtcNow < expiry;
    return ValueTask.FromResult(result);
  }

  public async Task AfterCommitAsync(CancellationToken ct)
  {
    if (!MutationCommitted || CommitObserved) return;
    CommitObserved = true;
    var remaining = expiry - DateTimeOffset.UtcNow;
    if (remaining > TimeSpan.Zero) await Task.Delay(remaining + TimeSpan.FromMilliseconds(20), ct);
    ExpiryPassedBeforeRelease = DateTimeOffset.UtcNow >= expiry;
  }
}

internal sealed class WorkflowExpiryCommitBoundary(WorkflowExpiryBoundary boundary) : DbTransactionInterceptor
{
  public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
    CancellationToken cancellationToken = default) => await boundary.AfterCommitAsync(cancellationToken);
}

internal sealed class WorkflowProjectionAnchor(Guid engagementId) : DbCommandInterceptor
{
  public bool Observed { get; private set; }

  public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
    DbDataReader result, CancellationToken cancellationToken = default)
  {
    if (command.CommandText.Contains("workflow_task_bindings", StringComparison.OrdinalIgnoreCase) &&
        command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, engagementId)))
      Observed = true;
    return ValueTask.FromResult(result);
  }
}

internal sealed class ProjectionSnapshotRace(bool workflow, bool afterSnapshot, Func<Task<(int Rows, bool Observed)>> change)
  : DbCommandInterceptor
{
  private bool armed = !workflow;
  public bool Fired { get; private set; }
  public int UpdatedRows { get; private set; }
  public bool ObservedCommitted { get; private set; }

  private bool IsSnapshot(DbCommand command) => armed && !Fired &&
    command.CommandText.Contains("U12 engagement projection final authorization", StringComparison.Ordinal);

  private async Task ApplyAsync()
  {
    Fired = true;
    (UpdatedRows, ObservedCommitted) = await change();
  }

  public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
    CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
  {
    if (workflow && command.CommandText.Contains("FROM workflow_task_bindings", StringComparison.OrdinalIgnoreCase)) armed = true;
    if (!afterSnapshot && IsSnapshot(command)) await ApplyAsync();
    return result;
  }

  public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
    DbDataReader result, CancellationToken cancellationToken = default)
  {
    if (afterSnapshot && IsSnapshot(command)) await ApplyAsync();
    return result;
  }
}
