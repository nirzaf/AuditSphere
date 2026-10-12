using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// WF-00/WF-01 characterization of the seven-phase projection. The mapper is pure; the query runs against PostgreSQL with the
/// real canonical lifecycle evaluator, so the visible phase can never disagree with the protected stage.
/// </summary>
public sealed class EngagementWorkflowProjectionTests
{
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
}
