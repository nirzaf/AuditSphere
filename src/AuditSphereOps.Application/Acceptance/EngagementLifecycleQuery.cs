using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public static class CanonicalEngagementStages
{
  public const string LeadIngestion = "LEAD_INGESTION";
  public const string ProposalGeneration = "PROPOSAL_GENERATION";
  public const string DualKeyPending = "DUAL_KEY_PENDING";
  public const string AdvanceBilling = "ADVANCE_BILLING";
  public const string PortalActivePlanning = "PORTAL_ACTIVE_PLANNING";
  public const string FieldworkExecution = "FIELDWORK_EXECUTION";
  public const string ManagerialReview = "MANAGERIAL_REVIEW";
  public const string PartnerApproval = "PARTNER_APPROVAL";
  public const string DeliverableRelease = "DELIVERABLE_RELEASE";
  public const string ComplianceCountdown = "COMPLIANCE_COUNTDOWN";
  public const string ArchivedReadOnly = "ARCHIVED_READ_ONLY";
}

public sealed record EngagementLifecycleSummary(
  string CanonicalStage,
  string StageLabel,
  int StageIndex,
  IReadOnlyList<string> BlockedReasons,
  string ResponsibleRole,
  string DeepLink,
  int? ComplianceCountdownDays,
  bool IsArchived,
  bool IsLegacyUnverified);

public sealed record EngagementLifecycleReport(
  Guid EngagementId,
  Guid ClientId,
  string ClientName,
  string ServiceRoute,
  EngagementLifecycleSummary Summary,
  DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// AS-COMP-02 Canonical eleven-state business lifecycle projection. Evaluates current authoritative
/// database records across commercial, governance, audit fieldwork, deliverables, and archive boundaries
/// to project truthful progression, actionable blockers, responsible roles, and deep links without
/// manual status mutation.
/// </summary>
public static class EngagementLifecycleQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "Auditor", "EngagementLeader"];

  public static async Task<CommandResult<EngagementLifecycleReport>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: Roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<EngagementLifecycleReport>.Fail(auth.ErrorCode!, auth.Message!);

    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return CommandResult<EngagementLifecycleReport>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");

    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    if (client is null) return CommandResult<EngagementLifecycleReport>.Fail(ErrorCodes.ScopeDenied, "Client unavailable.");

    var now = DateTimeOffset.UtcNow;
    var holds = await db.EngagementHolds.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && !x.Released)
      .Select(x => x.Reason).ToListAsync(ct);

    // 11. Archived Read-Only & 10. Compliance Countdown (60 days post-signature/release)
    var freeze = await db.EngagementFileFreezes.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    var isLocked = freeze?.State == FileFreezeStates.Frozen || await db.DocumentLocks.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.ReleasedAt == null, ct);
    var release = await db.Releases.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    var deliverable = await db.AuditDeliverables.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);

    if (isLocked)
    {
      return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
        CanonicalEngagementStages.ArchivedReadOnly, "Archived (Read-Only)", 11, [],
        "None", $"/app/records/archives/{engagementId}", 0, true, false), now);
    }

    if (release is not null || freeze is not null)
    {
      var releaseDate = release?.ReleasedAt ?? freeze?.FrozenAt ?? freeze?.ReportSignedAt ?? now;
      var daysElapsed = (now - releaseDate).TotalDays;
      if (daysElapsed >= 60)
      {
        return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
          CanonicalEngagementStages.ArchivedReadOnly, "Archived (Read-Only)", 11, [],
          "None", $"/app/records/archives/{engagementId}", 0, true, false), now);
      }

      var remaining = Math.Max(0, (int)Math.Ceiling(60 - daysElapsed));
      return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
        CanonicalEngagementStages.ComplianceCountdown, "Compliance Countdown", 10, [],
        "EngagementPartner", $"/app/engagements/{engagementId}/completion", remaining, false, false), now);
    }

    // 9. Deliverable Release & 8. Partner Approval
    var bundle = await db.CommercialDeliverableBundles.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    var opinion = await db.AuditOpinionDecisions.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    var partnerClearance = await db.PartnerCompletionClearances.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    var hasSignedReport = await db.AuditDeliverables.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Kind == DeliverableKinds.IndependentAuditorsReport && x.SignedFromDeliverableId != null, ct);

    if (partnerClearance is not null && opinion is not null)
    {
      if ((bundle is not null && bundle.Content.Length > 0) || hasSignedReport)
      {
        var blocked = new List<string>();
        if (holds.Count > 0) blocked.AddRange(holds.Select(h => $"Open hold: {h}"));
        blocked.Add("Awaiting final client deliverable release and portal upload freeze");
        return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
          CanonicalEngagementStages.DeliverableRelease, "Deliverable Release", 9, blocked,
          "EngagementPartner", $"/app/engagements/{engagementId}/completion", null, false, false), now);
      }

      var partnerBlocked = new List<string>();
      if (holds.Count > 0) partnerBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      if (bundle is null || bundle.Content.Length == 0) partnerBlocked.Add("Assembly of 5-part final completion bundle required");
      return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
        CanonicalEngagementStages.PartnerApproval, "Partner Approval & Signing", 8, partnerBlocked,
        "EngagementPartner", $"/app/engagements/{engagementId}/completion", null, false, false), now);
    }

    // 7. Managerial Review & 6. Fieldwork Execution
    var openReviewNotes = await (from note in db.ProcedureReviewNotes.AsNoTracking()
      where note.FirmId == actor.FirmId && note.EngagementId == engagementId
      let latestEvent = db.ProcedureReviewNoteEvents.AsNoTracking()
        .Where(e => e.FirmId == actor.FirmId && e.NoteId == note.Id)
        .OrderByDescending(e => e.CreatedAt).FirstOrDefault()
      where latestEvent == null || latestEvent.Kind != ReviewNoteEventKinds.Resolved
      select note.Id).CountAsync(ct);

    var procedures = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).ToListAsync(ct);
    var allExecuted = procedures.Count > 0 && procedures.All(x => x.CurrentResultRevision > 0);

    if (allExecuted)
    {
      var mgrBlocked = new List<string>();
      if (holds.Count > 0) mgrBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      if (openReviewNotes > 0) mgrBlocked.Add($"{openReviewNotes} open review note(s) require resolution");
      if (opinion is null) mgrBlocked.Add("Partner audit opinion decision pending");
      return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
        CanonicalEngagementStages.ManagerialReview, "Managerial Review", 7, mgrBlocked,
        "AuditManager", $"/app/engagements/{engagementId}/audit-fieldwork", null, false, false), now);
    }

    var planApproved = !engagement.ProfessionalWorkBlocked &&
      (await db.MaterialityCalculations.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct) ||
       await db.MaterialityAssessments.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Status == MaterialityStatuses.Approved, ct));

    if (planApproved && procedures.Count > 0)
    {
      var fwBlocked = new List<string>();
      if (holds.Count > 0) fwBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      var pendingCount = procedures.Count(x => x.CurrentResultRevision == 0);
      if (pendingCount > 0) fwBlocked.Add($"{pendingCount} substantive audit procedure(s) pending execution");
      return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
        CanonicalEngagementStages.FieldworkExecution, "Fieldwork Execution", 6, fwBlocked,
        "SeniorAuditor", $"/app/engagements/{engagementId}/audit-fieldwork", null, false, false), now);
    }

    // 5. Portal Active & Planning
    var activation = await db.EngagementActivations.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    if (activation is not null)
    {
      var planBlocked = new List<string>();
      if (holds.Count > 0) planBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      var hasTb = await db.TrialBalanceDatasets.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
      if (!hasTb) planBlocked.Add("Trial balance intake and acceptance required");
      if (!planApproved) planBlocked.Add("Materiality calculation and formal Partner planning approval required");
      if (engagement.ProfessionalWorkBlocked) planBlocked.Add("Engagement professional work is currently blocked");
      return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
        CanonicalEngagementStages.PortalActivePlanning, "Portal Active & Planning", 5, planBlocked,
        "AuditManager", $"/app/engagements/{engagementId}/audit-plan", null, false, false), now);
    }

    // 4. Advance Billing
    var acceptance = await db.AcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == engagement.PracticeClientId &&
        x.ServiceRoute == engagement.ServiceRoute && x.Decision == "Accepted")
      .OrderByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);

    if (acceptance is not null)
    {
      var feeBlocked = new List<string>();
      if (holds.Count > 0) feeBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      feeBlocked.Add("50% advance invoice payment allocation required to activate portal onboarding");
      return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
        CanonicalEngagementStages.AdvanceBilling, "Advance Billing & Payment", 4, feeBlocked,
        "FinanceManager", $"/app/finance", null, false, false), now);
    }

    // 3. Dual-Key Pending
    var proposal = await db.Proposals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == engagement.PracticeClientId)
      .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);

    if (proposal is not null)
    {
      var dualBlocked = new List<string>();
      if (acceptance is null) dualBlocked.Add("Unconditional Partner risk acceptance decision required");
      if (proposal.Status != "Accepted") dualBlocked.Add("Client commercial acceptance of proposal required");
      return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
        CanonicalEngagementStages.DualKeyPending, "Dual-Key Acceptance Pending", 3, dualBlocked,
        "EngagementPartner", $"/app/assessments/{engagement.PracticeClientId}", null, false, false), now);
    }

    // 2. Proposal Generation & 1. Lead Ingestion
    return Success(engagement, client.LegalName, new EngagementLifecycleSummary(
      CanonicalEngagementStages.ProposalGeneration, "Proposal Generation", 2,
      ["Commercial proposal preparation and dispatch required"],
      "CommercialManager", $"/app/clients/{engagement.PracticeClientId}", null, false, false), now);
  }

  private static CommandResult<EngagementLifecycleReport> Success(
    Engagement engagement, string clientName, EngagementLifecycleSummary summary, DateTimeOffset now) =>
    CommandResult<EngagementLifecycleReport>.Ok(new(
      engagement.Id, engagement.PracticeClientId, clientName, engagement.ServiceRoute, summary, now));
}
