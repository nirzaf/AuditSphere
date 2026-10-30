using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// WF-00/WF-01 characterization of the seven-phase projection. The mapper is pure; the query runs against PostgreSQL with the
/// real canonical lifecycle evaluator, so the visible phase can never disagree with the protected stage.
/// </summary>
public sealed class EngagementWorkflowProjectionTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task LifecycleAndWorkflowRefuseSessionChangedDuringFinalReadAuthorization(bool workflow)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(seed.Staff, "Staff");
    await using (var baseline = new AuditSphereDbContext(pg.Options))
    {
      var before = await EngagementWorkflowWorkspaceQuery.GetAsync(baseline, actor, seed.EngagementId);
      Assert.True(before.Succeeded, before.Message);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, before.Value!.CanonicalStage);
      Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], before.Value.Blockers);
      Assert.Null(before.Value.BlockerDetails);
      Assert.Empty(before.Value.NextActions);
    }
    var race = new FinalSessionAuthorizationRace(workflow, actor.UserId, seed.ClientId, async () =>
    {
      int changed;
      await using (var writer = new AuditSphereDbContext(pg.Options))
      await using (var transaction = await writer.Database.BeginTransactionAsync())
      {
        changed = await writer.Users.Where(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.SessionEpoch == actor.SessionEpoch)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
        await transaction.CommitAsync();
      }
      await using var verifier = new AuditSphereDbContext(pg.Options);
      var committedEpoch = await verifier.Users.AsNoTracking().Where(x => x.Id == actor.UserId && x.FirmId == actor.FirmId)
        .Select(x => (long?)x.SessionEpoch).SingleOrDefaultAsync();
      return (changed, committedEpoch);
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(race).Options);
    void AssertBoundaryAndCommit()
    {
      Assert.True(race.AnchorObserved);
      if (workflow) { Assert.True(race.ActionClientReadObserved); Assert.True(race.ActionGrantReadObserved); }
      Assert.True(race.FinalActorReadObserved);
      Assert.True(race.FinalGrantBoundaryReached);
      Assert.True(race.MutationCommitted);
      Assert.Equal(1, race.UpdatedRows);
      Assert.Equal(actor.SessionEpoch + 1, race.CommittedEpoch);
    }
    if (workflow)
    {
      var result = await EngagementWorkflowWorkspaceQuery.GetAsync(reader, actor, seed.EngagementId);
      AssertBoundaryAndCommit();
      Assert.False(result.Succeeded);
      Assert.Null(result.Value);
    }
    else
    {
      var result = await EngagementLifecycleQuery.GetAsync(reader, actor, seed.EngagementId);
      AssertBoundaryAndCommit();
      Assert.False(result.Succeeded);
      Assert.Null(result.Value);
    }
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task LifecycleAndWorkflowRefuseLostScopeOrSessionAfterInitialRead(bool workflow, bool changeEpoch)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(seed.Staff, "Staff");
    var marker = workflow ? "FROM workflow_task_bindings" : "FROM engagement_holds";
    var race = new LifecycleAuthorizationRace(marker, async () =>
    {
      await using var writer = new AuditSphereDbContext(pg.Options);
      if (changeEpoch)
        await writer.Users.Where(x => x.Id == actor.UserId && x.FirmId == actor.FirmId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
      else
        await writer.RoleGrants.Where(x => x.UserId == actor.UserId && x.FirmId == actor.FirmId && x.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(race).Options);
    if (workflow)
    {
      var result = await EngagementWorkflowWorkspaceQuery.GetAsync(reader, actor, seed.EngagementId);
      Assert.False(result.Succeeded);
      Assert.Null(result.Value);
      Assert.Equal(changeEpoch ? ErrorCodes.GenerationStale : ErrorCodes.ScopeDenied, result.ErrorCode);
    }
    else
    {
      var result = await EngagementLifecycleQuery.GetAsync(reader, actor, seed.EngagementId);
      Assert.False(result.Succeeded);
      Assert.Null(result.Value);
      Assert.Equal(changeEpoch ? ErrorCodes.GenerationStale : ErrorCodes.ScopeDenied, result.ErrorCode);
    }
    Assert.True(race.Fired);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task LifecycleAndWorkflowRedactWhenFirmWideDetailGrantIsRevoked(bool workflow)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var user = PbcSeed.User(seed.FirmId, "Staff");
    var detailGrant = PbcSeed.Grant(seed.FirmId, user, "Manager");
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.Add(user);
      setup.RoleGrants.Add(detailGrant);
      setup.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, user, "Staff", seed.ClientId, seed.EngagementId));
      await setup.SaveChangesAsync();
    }
    var actor = PbcSeed.Actor(user, "Manager");
    var marker = workflow ? "FROM workflow_task_bindings" : "FROM engagement_holds";
    var race = new LifecycleAuthorizationRace(marker, async () =>
    {
      await using var writer = new AuditSphereDbContext(pg.Options);
      await writer.RoleGrants.Where(x => x.Id == detailGrant.Id && x.FirmId == seed.FirmId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(race).Options);
    if (workflow)
    {
      var result = await EngagementWorkflowWorkspaceQuery.GetAsync(reader, actor, seed.EngagementId);
      Assert.True(result.Succeeded, result.Message);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, result.Value!.CanonicalStage);
      Assert.Equal(EngagementWorkflowPhaseStatuses.Blocked, result.Value.CurrentPhase.Status);
      Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], result.Value.Blockers);
      Assert.Null(result.Value.BlockerDetails);
    }
    else
    {
      var result = await EngagementLifecycleQuery.GetAsync(reader, actor, seed.EngagementId);
      Assert.True(result.Succeeded, result.Message);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, result.Value!.Summary.CanonicalStage);
      Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], result.Value.Summary.BlockedReasons);
      Assert.Null(result.Value.Summary.BlockerDetails);
    }
    Assert.True(race.Fired);
  }

  private static readonly (string Stage, int Index, string Phase, int PhaseIndex)[] ExpectedMapping =
  [
    (CanonicalEngagementStages.LeadIngestion, 1, "CLIENT_ONBOARDING", 1),
    (CanonicalEngagementStages.ProposalGeneration, 2, "CLIENT_ONBOARDING", 1),
    (CanonicalEngagementStages.DualKeyPending, 3, "CLIENT_ONBOARDING", 1),
    (CanonicalEngagementStages.AdvanceBilling, 4, "CLIENT_ONBOARDING", 1),
    (CanonicalEngagementStages.PortalActivePlanning, 5, "AUDIT_PLANNING", 2),
    (CanonicalEngagementStages.FieldworkExecution, 6, "AUDIT_FIELDWORK", 3),
    (CanonicalEngagementStages.ManagerialReview, 7, "MANAGER_REVIEW", 4),
    (CanonicalEngagementStages.PartnerApproval, 8, "PARTNER_SIGNOFF", 5),
    (CanonicalEngagementStages.DeliverableRelease, 9, "REPORT_DELIVERY", 6),
    (CanonicalEngagementStages.ComplianceCountdown, 10, "ARCHIVE_CLOSE", 7),
    (CanonicalEngagementStages.ArchivedReadOnly, 11, "ARCHIVE_CLOSE", 7),
  ];

  [Fact]
  public void EveryCanonicalStageMapsToExactlyOneVisiblePhaseAndUnknownStagesFailClosed()
  {
    Assert.Equal(7, EngagementDisplayPhaseMapper.PhaseCount);
    Assert.Equal([1, 2, 3, 4, 5, 6, 7], EngagementDisplayPhaseMapper.Phases.Select(p => p.Index));

    var covered = EngagementDisplayPhaseMapper.Phases.SelectMany(p => p.CanonicalStages).ToList();
    Assert.Equal(11, covered.Count);
    Assert.Equal(11, covered.Distinct(StringComparer.Ordinal).Count());

    foreach (var (stage, index, phaseCode, phaseIndex) in ExpectedMapping)
    {
      Assert.True(EngagementDisplayPhaseMapper.TryResolve(stage, index, out var phase), stage);
      Assert.Equal(phaseCode, phase!.Code);
      Assert.Equal(phaseIndex, phase.Index);
    }

    Assert.False(EngagementDisplayPhaseMapper.TryResolve("FUTURE_STAGE", 12, out _));
    Assert.False(EngagementDisplayPhaseMapper.TryResolve(null, 1, out _));
    Assert.False(EngagementDisplayPhaseMapper.TryResolve(CanonicalEngagementStages.DualKeyPending, 4, out _));
  }

  [Fact]
  public async Task WorkflowProjectionMatchesCanonicalStageAndEnforcesEngagementScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");
    var unauthorized = PbcSeed.Actor(seed.Staff, "Staff");
    var scopedStaff = PbcSeed.User(seed.FirmId, "Staff");
    var now = DateTimeOffset.UtcNow;
    var engagementId = Guid.NewGuid();

    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
    db.Users.Add(scopedStaff);
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ServiceProfileId = "CommercialAudit", Status = "Draft",
      ProfessionalWorkBlocked = true, CreatedAt = now
    });
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, scopedStaff, "Staff", seed.ClientId, engagementId));
    await db.SaveChangesAsync();

    var canonical = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
    var workflow = await EngagementWorkflowWorkspaceQuery.GetAsync(db, partner, engagementId);
    Assert.True(workflow.Succeeded, workflow.Message);
    var report = workflow.Value!;

    // Projection never disagrees with the canonical stage it was derived from.
    Assert.Equal(canonical.Value!.Summary.CanonicalStage, report.CanonicalStage);
    Assert.Equal(canonical.Value.Summary.StageIndex, report.CanonicalIndex);
    Assert.Equal(canonical.Value.Summary.BlockedReasons, report.Blockers);

    // Neither the canonical lifecycle endpoint nor its workflow projection disclose firm-wide blocker details to scoped staff.
    var scopedActor = PbcSeed.Actor(scopedStaff, "Staff");
    var scopedLifecycle = await EngagementLifecycleQuery.GetAsync(db, scopedActor, engagementId);
    Assert.True(scopedLifecycle.Succeeded, scopedLifecycle.Message);
    Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], scopedLifecycle.Value!.Summary.BlockedReasons);
    var scoped = await EngagementWorkflowWorkspaceQuery.GetAsync(db, scopedActor, engagementId);
    Assert.True(scoped.Succeeded, scoped.Message);
    Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], scoped.Value!.Blockers);
    Assert.DoesNotContain(canonical.Value.Summary.BlockedReasons, reason => scoped.Value.Blockers.Contains(reason));

    // A draft with no dispatched proposal sits in the first visible phase, which is blocked, and later phases have not started.
    Assert.Equal(7, report.Phases.Count);
    Assert.Equal("CLIENT_ONBOARDING", report.CurrentPhase.Code);
    Assert.Equal(EngagementWorkflowPhaseStatuses.Blocked, report.CurrentPhase.Status);
    Assert.Equal(EngagementWorkflowPhaseStatuses.NotStarted, report.Phases[1].Status);
    Assert.DoesNotContain(report.Phases, p => p.Status == EngagementWorkflowPhaseStatuses.Completed);
    Assert.Null(report.Archive);

    // Only allowlisted internal routes leave the projection.
    Assert.All(report.NextActions, action => Assert.StartsWith("/app/", action.Href, StringComparison.Ordinal));

    // Scope: a staff user without the engagement grant learns nothing about the engagement.
    var denied = await EngagementWorkflowWorkspaceQuery.GetAsync(db, unauthorized, engagementId);
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
  }

  [Fact]
  public async Task WorkflowShowsNearestOpenTaskDatesAndOnlyTheResponsibleRoleGetsTheCanonicalAction()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(seed.Staff, "Staff");
    var commercialManager = PbcSeed.User(seed.FirmId, "Staff");
    var admin = PbcSeed.Actor(seed.Admin, "Administrator");
    var onboardingDue = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8);
    var onboardingEarlier = onboardingDue.AddDays(-3);
    var planningDue = onboardingDue.AddDays(4);
    Guid completedTask;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(commercialManager);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, commercialManager, "CommercialManager", seed.ClientId, seed.EngagementId));
      var completed = await EngagementWorkflowTaskService.CreateAsync(db, staff, seed.EngagementId,
        new("Completed onboarding work", WorkflowTaskPhases.ClientOnboarding, 1, onboardingEarlier, IdempotencyKey: Guid.NewGuid()));
      Assert.True(completed.Succeeded, completed.Message);
      completedTask = completed.Value;
      Assert.True((await EngagementWorkflowTaskService.CreateAsync(db, staff, seed.EngagementId,
        new("Open onboarding work", WorkflowTaskPhases.ClientOnboarding, 2, onboardingDue, IdempotencyKey: Guid.NewGuid()))).Succeeded);
      Assert.True((await EngagementWorkflowTaskService.CreateAsync(db, staff, seed.EngagementId,
        new("In-progress planning work", WorkflowTaskPhases.AuditPlanning, 1, planningDue, IdempotencyKey: Guid.NewGuid()))).Succeeded);
      await db.WorkTasks.Where(x => x.Id == completedTask)
        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, PracticeTimeStates.TaskCompleted));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var report = await EngagementWorkflowWorkspaceQuery.GetAsync(db, staff, seed.EngagementId);
      Assert.True(report.Succeeded, report.Message);
      Assert.Equal(onboardingDue, report.Value!.Phases[0].OperationalDueDate);
      Assert.Equal(planningDue, report.Value.Phases[1].OperationalDueDate);
      Assert.Equal(EngagementWorkflowPhaseStatuses.Blocked, report.Value.CurrentPhase.Status);
      Assert.Empty(report.Value.NextActions);

      // An administrator without the stage's commercial role does not get the proposal action merely from broad read access.
      var administrator = await EngagementWorkflowWorkspaceQuery.GetAsync(db, admin, seed.EngagementId);
      Assert.True(administrator.Succeeded, administrator.Message);
      Assert.Empty(administrator.Value!.NextActions);

      var commercial = await EngagementWorkflowWorkspaceQuery.GetAsync(db,
        PbcSeed.Actor(commercialManager, "CommercialManager"), seed.EngagementId);
      Assert.True(commercial.Succeeded, commercial.Message);
      var action = Assert.Single(commercial.Value!.NextActions);
      Assert.Equal("OPEN_CANONICAL_ACTION", action.Key);
      Assert.Equal($"/app/clients/{seed.ClientId}", action.Href);

      // Completing operational tasks removes their displayed due date, but the canonical proposal gate remains blocked.
      await db.WorkTasks.Where(x => x.EngagementId == seed.EngagementId && x.Status != PracticeTimeStates.TaskCompleted)
        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, PracticeTimeStates.TaskCompleted));
      var completedWork = await EngagementWorkflowWorkspaceQuery.GetAsync(db, staff, seed.EngagementId);
      Assert.True(completedWork.Succeeded, completedWork.Message);
      Assert.Null(completedWork.Value!.Phases[0].OperationalDueDate);
      Assert.Equal(EngagementWorkflowPhaseStatuses.Blocked, completedWork.Value.CurrentPhase.Status);
    }
  }
}

internal sealed class LifecycleAuthorizationRace(string marker, Func<Task> change, int skipMatches = 0,
  string? armMarker = null) : DbCommandInterceptor
{
  public bool Fired { get; private set; }
  private bool armed = armMarker is null;
  private int matches;

  public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
    CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
  {
    if (!armed && command.CommandText.Contains(armMarker!, StringComparison.Ordinal)) armed = true;
    if (!Fired && armed && command.CommandText.Contains(marker, StringComparison.Ordinal) && matches++ >= skipMatches)
    {
      Fired = true;
      await change();
    }
    return result;
  }
}

internal sealed class FinalSessionAuthorizationRace(bool workflow, Guid actorId, Guid clientId,
  Func<Task<(int Rows, long? Epoch)>> mutate) : DbCommandInterceptor
{
  public bool AnchorObserved { get; private set; }
  public bool ActionClientReadObserved { get; private set; }
  public bool ActionGrantReadObserved { get; private set; }
  public bool FinalActorReadObserved { get; private set; }
  public bool FinalGrantBoundaryReached { get; private set; }
  public bool MutationCommitted { get; private set; }
  public int UpdatedRows { get; private set; }
  public long? CommittedEpoch { get; private set; }

  private static bool HasId(DbCommand command, Guid id) =>
    command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid value && value == id);

  public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
    DbDataReader result, CancellationToken cancellationToken = default)
  {
    var text = command.CommandText;
    if (!AnchorObserved && text.Contains(workflow ? "FROM workflow_task_bindings" : "FROM proposals", StringComparison.OrdinalIgnoreCase))
      AnchorObserved = true;
    else if (AnchorObserved && workflow && !ActionClientReadObserved &&
      text.Contains("FROM practice_clients", StringComparison.OrdinalIgnoreCase) && HasId(command, clientId))
      ActionClientReadObserved = true;
    else if (AnchorObserved && workflow && ActionClientReadObserved && !ActionGrantReadObserved &&
      text.Contains("FROM role_grants", StringComparison.OrdinalIgnoreCase) && HasId(command, actorId))
      ActionGrantReadObserved = true;
    else if (AnchorObserved && (!workflow || ActionGrantReadObserved) && !FinalActorReadObserved &&
      text.Contains("FROM users", StringComparison.OrdinalIgnoreCase) && HasId(command, actorId))
      FinalActorReadObserved = true;
    return ValueTask.FromResult(result);
  }

  public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
    CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
  {
    if (!FinalGrantBoundaryReached && FinalActorReadObserved &&
        command.CommandText.Contains("FROM role_grants", StringComparison.OrdinalIgnoreCase) && HasId(command, actorId))
    {
      FinalGrantBoundaryReached = true;
      (UpdatedRows, CommittedEpoch) = await mutate();
      MutationCommitted = true;
    }
    return result;
  }
}
