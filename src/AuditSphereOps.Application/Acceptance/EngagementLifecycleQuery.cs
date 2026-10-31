using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
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

/// <summary>Optional guidance for one existing blocker; its index refers to the same report's blocker list.</summary>
public sealed record EngagementLifecycleBlockerDetail(
  int BlockerIndex, string Code, string Cause, string ResponsibleRole, string Guidance);

public sealed record EngagementLifecycleSummary(
  string CanonicalStage,
  string StageLabel,
  int StageIndex,
  IReadOnlyList<string> BlockedReasons,
  string ResponsibleRole,
  string DeepLink,
  int? ComplianceCountdownDays,
  bool IsArchived,
  bool IsLegacyUnverified,
  string? ComplianceWarning = null,
  string? LocalArchiveState = null,
  string? ProviderProtectionState = null,
  IReadOnlyList<EngagementLifecycleBlockerDetail>? BlockerDetails = null);

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
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "Auditor", "EngagementLeader", "CommercialManager", "FinanceManager"];
  private static readonly string[] DetailRoles = ["Administrator", "Partner", "Manager", "SeniorManager"];
  internal const string RestrictedBlockers = "Some engagement readiness details are restricted to firm-wide management.";

  internal static AuthorizationRequest ReadRequest(ActorContext actor, Guid engagementId) =>
    new(actor.FirmId, EngagementId: engagementId, RequiredRoles: Roles, InternalOnly: true);

  internal static AuthorizationRequest DetailRequest(ActorContext actor, Guid engagementId) =>
    new(actor.FirmId, EngagementId: engagementId, RequiredRoles: DetailRoles, InternalOnly: true, RequireFirmWide: true);

  internal static Task<CommandResult> AuthorizeReadAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, ReadRequest(actor, engagementId), ct);

  internal static Task<CommandResult> AuthorizeDetailedBlockersAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, DetailRequest(actor, engagementId), ct);

  internal static Task<EngagementProjectionAuthorization> AuthorizeProjectionAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid clientId, Guid engagementId, AuthorizationRequest? action, string? staffingLevel, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeEngagementProjectionAsync(db, actor, clientId, engagementId,
      ReadRequest(actor, engagementId), DetailRequest(actor, engagementId), action, staffingLevel, ct);

  internal static Task<AuthorizationDecision.EngagementSnapshot> CaptureProjectionAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid clientId, Guid engagementId, AuthorizationRequest? action, string? staffingLevel, CancellationToken ct) =>
    AuthorizationDecision.CaptureEngagementProjectionAsync(db, actor, clientId, engagementId,
      ReadRequest(actor, engagementId), DetailRequest(actor, engagementId), action, staffingLevel, ct);

  public static Task<CommandResult<EngagementLifecycleReport>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default) =>
    GetCoreAsync(db, actor, engagementId, null, ct);

  internal static async Task<(CommandResult<EngagementLifecycleReport> Result, bool IncludesVisibleHolds)> GetForProgressAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var includesVisibleHolds = false;
    var result = await GetCoreAsync(db, actor, engagementId, value => includesVisibleHolds = value, ct);
    return (result, includesVisibleHolds);
  }

  private static async Task<CommandResult<EngagementLifecycleReport>> GetCoreAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Action<bool>? reportVisibleHolds, CancellationToken ct)
  {
    var auth = await AuthorizeReadAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return CommandResult<EngagementLifecycleReport>.Fail(auth.ErrorCode!, auth.Message!);

    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return CommandResult<EngagementLifecycleReport>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");

    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    if (client is null) return CommandResult<EngagementLifecycleReport>.Fail(ErrorCodes.ScopeDenied, "Client unavailable.");

    var now = DateTimeOffset.UtcNow;
    var detailAuthorization = await AuthorizeDetailedBlockersAsync(db, actor, engagementId, ct);
    var canViewDetailedBlockers = detailAuthorization.Succeeded;

    async Task<CommandResult<EngagementLifecycleReport>> ProjectAsync(EngagementLifecycleSummary summary, bool includesHolds = false)
    {
      var currentRead = await AuthorizeReadAsync(db, actor, engagementId, ct);
      if (!currentRead.Succeeded) return CommandResult<EngagementLifecycleReport>.Fail(currentRead.ErrorCode!, currentRead.Message!);
      var final = await AuthorizeProjectionAsync(db, actor, client.Id, engagementId, null, null, ct);
      if (!final.Read.Succeeded) return CommandResult<EngagementLifecycleReport>.Fail(final.Read.ErrorCode!, final.Read.Message!);
      var detailStillAuthorized = canViewDetailedBlockers && final.CanViewDetails;
      if (!detailStillAuthorized && summary.BlockedReasons.Count > 0)
        summary = summary with { BlockedReasons = [RestrictedBlockers], BlockerDetails = null };
      reportVisibleHolds?.Invoke(includesHolds && detailStillAuthorized);
      return Success(engagement, client.LegalName, summary, now);
    }

    var holds = await db.EngagementHolds.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && !x.Released)
      .Select(x => x.Reason).ToListAsync(ct);

    // 11. Archived Read-Only & 10. Compliance Countdown. The countdown is anchored to the Partner's report-signature date
    // (the same anchor as FileFreezeService). The file is archived only when the local freeze has committed; elapsed time
    // alone never archives it, and an active document lock is a working lock, not an archive.
    var freeze = await db.EngagementFileFreezes.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    var release = await db.Releases.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
        db.ReleaseCandidates.Any(candidate => candidate.FirmId == x.FirmId && candidate.Id == x.ReleaseCandidateId &&
          candidate.TargetKind == ReleaseTargetKinds.FinancialPackage))
      .OrderByDescending(x => x.ReleasedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var signedReport = freeze is null
      ? await db.AuditDeliverables.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
          x.Kind == DeliverableKinds.IndependentAuditorsReport && x.SignedFromDeliverableId != null)
        .OrderByDescending(x => x.Version).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct)
      : await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == client.Id &&
          x.EngagementId == engagementId && x.Id == freeze.ReportDeliverableId && x.Kind == DeliverableKinds.IndependentAuditorsReport &&
          x.SignedFromDeliverableId != null, ct);
    var completionLink = $"/app/engagements/{engagementId}/completion";

    if (freeze is { State: FileFreezeStates.Frozen })
    {
      return await ProjectAsync(new EngagementLifecycleSummary(
        CanonicalEngagementStages.ArchivedReadOnly, "Archived (Read-Only)", 11, [],
        "None", $"/app/records/archives/{engagementId}", 0, true, false, ComplianceWarningFor(freeze),
        freeze.State, freeze.ExternalReadOnly));
    }

    if (freeze is not null)
    {
      var remaining = freeze.State == FileFreezeStates.Scheduled
        ? Math.Max(0, (int)Math.Ceiling((freeze.DueAt - now).TotalDays)) : (int?)null;
      var deliveredBundle = release is null || signedReport is null || release.PinnedArtifactId is null
        ? null
        : await db.CommercialDeliverableBundles.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == client.Id &&
            x.EngagementId == engagementId && x.SignedReportId == signedReport.Id && x.FinancialPackageReleaseId == release.Id &&
            x.FinancialPackageArtifactId == release.PinnedArtifactId && x.FinancialStatementCompositionId == release.PinnedCompositionId &&
            x.CompositionManifestDigest == release.PinnedCompositionManifestDigest)
          .OrderByDescending(x => x.AssembledAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
      // Persisted bundles are listed and downloadable through the client portal. This proves availability, not that a client
      // opened or downloaded the bundle; the portal has no separate receipt for either action.
      var bundleAvailableToClient = deliveredBundle is { Content.Length: > 0 } &&
        Hashing.Sha256Hex(deliveredBundle.Content) == deliveredBundle.ContentSha256 &&
        Hashing.Sha256Hex(deliveredBundle.ManifestJson) == deliveredBundle.ManifestSha256;
      var countdownBlocked = new List<string>();
      var countdownDetails = new List<EngagementLifecycleBlockerDetail>();
      if (holds.Count > 0) countdownBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      if (!bundleAvailableToClient)
      {
        if (deliveredBundle is null)
          AddGuidedBlocker(countdownBlocked, countdownDetails, "FINAL_BUNDLE_NOT_AVAILABLE",
            "The current signed report and financial release are not yet assembled as a final bundle available in the client portal.",
            "Engagement Partner",
            "Waiting for the authorized Engagement Partner to assemble the current bundle through the existing completion workflow; this projection does not release it.");
        else
          AddGuidedBlocker(countdownBlocked, countdownDetails, "FINAL_BUNDLE_INTEGRITY_FAILED",
            "The final client bundle failed its stored-byte or manifest integrity check and is not treated as delivered.",
            "Unknown",
            "No safe delivery action is offered here. Keep the bundle undelivered and use the existing authorized integrity review; this projection does not replace or release bytes.");
      }
      if (freeze.State == FileFreezeStates.Scheduled && now >= freeze.DueAt)
        AddGuidedBlocker(countdownBlocked, countdownDetails, "LOCAL_FREEZE_NOT_COMMITTED",
          "The freeze is due but has not committed; the file is not archived until the local freeze succeeds.",
          "Background freeze worker",
          "No manual archive action is offered by this projection. Wait for the existing local freeze worker to commit before treating the file as archived.");
      var phaseDelivered = PostSignatureStage(freeze.State == FileFreezeStates.Scheduled, bundleAvailableToClient) ==
        CanonicalEngagementStages.ComplianceCountdown;
      return await ProjectAsync(new EngagementLifecycleSummary(
        PostSignatureStage(freeze.State == FileFreezeStates.Scheduled, bundleAvailableToClient),
        phaseDelivered ? "Compliance Countdown" : "Deliverable Release", phaseDelivered ? 10 : 9, countdownBlocked,
        "EngagementPartner", completionLink, remaining, false, false, ComplianceWarningFor(freeze),
        freeze.State, freeze.ExternalReadOnly,
        BlockerDetails: countdownDetails.Count == 0 ? null : countdownDetails), includesHolds: holds.Count > 0);
    }

    if (release is not null || signedReport is not null)
    {
      var releaseBlockers = new List<string>();
      var releaseDetails = new List<EngagementLifecycleBlockerDetail>();
      if (signedReport is null)
        AddGuidedBlocker(releaseBlockers, releaseDetails, "PARTNER_REPORT_SIGNATURE_REQUIRED",
          "The compliance countdown starts when the Partner signs the Independent Auditor's Report.",
          "Engagement Partner",
          "Waiting for the authorized Engagement Partner to sign the current report. This projection neither creates a signature nor starts the countdown.");
      else
        AddGuidedBlocker(releaseBlockers, releaseDetails, "LOCAL_COMPLIANCE_FREEZE_MISSING",
          "The signed report has no scheduled local compliance freeze; review the signature and archive workflow.",
          "Unknown",
          "No archive action is offered from this projection. Keep the file unarchived until the existing authorized workflow records a valid local freeze.");
      return await ProjectAsync(new EngagementLifecycleSummary(
        CanonicalEngagementStages.DeliverableRelease, "Deliverable Release", 9, releaseBlockers,
        "EngagementPartner", completionLink, null, false, false, BlockerDetails: releaseDetails));
    }

    // 9. Deliverable Release & 8. Partner Approval
    var partnerClearance = await AuditDeliverableService.CurrentClearanceAsync(db, actor, engagementId, ct);
    var opinion = await AuditDeliverableService.CurrentOpinionAsync(db, actor, engagementId, ct);

    // Manager handover (STE lifecycle 7 -> 8): Partner Approval begins once current manager-reviewed evidence,
    // a current SRM and the completion prerequisites are clear. Partner clearance remains a separate outstanding action.
    var openReviewNotes = await (from note in db.ProcedureReviewNotes.AsNoTracking()
      where note.FirmId == actor.FirmId && note.EngagementId == engagementId
      let latestEvent = db.ProcedureReviewNoteEvents.AsNoTracking()
        .Where(e => e.FirmId == actor.FirmId && e.NoteId == note.Id)
        .OrderByDescending(e => e.CreatedAt).FirstOrDefault()
      where latestEvent == null || latestEvent.Kind != ReviewNoteEventKinds.Resolved
      select note.Id).CountAsync(ct);

    var procedures = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == client.Id &&
      x.EngagementId == engagementId).ToListAsync(ct);
    var applicability = procedures.Count == 0 ? new Dictionary<Guid, string>() :
      await AuditProgramService.EffectiveApplicabilityStatusesAsync(db, actor.FirmId, client.Id, engagementId, procedures, ct);
    var procedureResults = await db.AuditProcedureResults.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == client.Id &&
      x.EngagementId == engagementId).ToListAsync(ct);
    var procedureReviews = await db.AuditProcedureReviews.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == client.Id &&
      x.EngagementId == engagementId).ToListAsync(ct);
    var procedureProgress = procedures.Select(procedure =>
    {
      var latest = procedureResults.Where(x => x.AuditProcedureId == procedure.Id).OrderByDescending(x => x.Revision)
        .ThenByDescending(x => x.Id).FirstOrDefault();
      var current = latest is not null && latest.Revision == procedure.CurrentResultRevision ? latest : null;
      var review = current is null ? null : procedureReviews.Where(x => x.AuditProcedureResultId == current.Id && x.ResultRevision == current.Revision)
        .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault();
      return (Procedure: procedure, IsSubmitted: IsCurrentProcedureSubmittedForReview(procedure, current, review,
        applicability.GetValueOrDefault(procedure.Id) == AuditApplicabilityStatuses.NotApplicableApproved));
    }).ToArray();
    var allSubmittedForReview = procedures.Count > 0 && procedureProgress.All(x => x.IsSubmitted);

    var handoverBlockers = new List<string>();
    var handoverDetails = new List<EngagementLifecycleBlockerDetail>();
    if (openReviewNotes > 0)
      AddGuidedBlocker(handoverBlockers, handoverDetails, "OPEN_REVIEW_NOTES_REQUIRE_RESOLUTION",
        $"{openReviewNotes} open review note(s) require resolution", "Preparer and authorized reviewer",
        "Waiting for the preparer to respond and an authorized reviewer to resolve each open thread. This projection does not respond to or resolve notes.");
    var completion = await AuditFieldworkService.EvaluateCompletionForAuthorizedScopeAsync(db, actor.FirmId, client.Id, engagementId, ct);
    handoverBlockers.AddRange(completion.Succeeded
      ? completion.Value!.Blockers.Select(b => $"Completion gate: {b}")
      : ["Completion gate could not be evaluated for your role."]);
    if (await AuditDeliverableService.CurrentSummaryReviewMemorandumAsync(db, actor, engagementId, ct) is null)
      AddGuidedBlocker(handoverBlockers, handoverDetails, "CURRENT_SRM_REQUIRED",
        "A current Summary Review Memorandum is required for Partner handover.", "Manager, Partner, or Administrator",
        "Waiting for an authorized Manager, Partner, or Administrator to prepare the current factual memorandum after review. It does not record Partner clearance or an audit opinion.");

    if (PartnerHandoverReady(handoverBlockers.Count))
    {
      var partnerBlocked = new List<string>();
      var partnerDetails = new List<EngagementLifecycleBlockerDetail>();
      if (holds.Count > 0) partnerBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      if (partnerClearance is null)
        AddGuidedBlocker(partnerBlocked, partnerDetails, "PARTNER_CURRENT_SRM_CLEARANCE_REQUIRED",
          "Partner clearance of the current Summary Review Memorandum is pending.", "Engagement Partner",
          "Waiting for the authorized Engagement Partner to clear the exact current memorandum. This projection does not record clearance.");
      if (opinion is null)
        AddGuidedBlocker(partnerBlocked, partnerDetails, "PARTNER_AUDIT_OPINION_REQUIRED",
          "Partner audit opinion decision pending", "Engagement Partner",
          "Waiting for an authorized Engagement Partner to make and record the professional opinion decision; this projection does not choose an opinion.");
      else
        AddGuidedBlocker(partnerBlocked, partnerDetails, "PARTNER_REPORT_SIGNATURE_REQUIRED",
          "Partner signature of the current Independent Auditor's Report pending", "Engagement Partner",
          "Waiting for the authorized Engagement Partner to sign the current report. This projection does not create or apply a signature.");
      return await ProjectAsync(new EngagementLifecycleSummary(
        CanonicalEngagementStages.PartnerApproval, "Partner Approval & Signing", 8, partnerBlocked,
        "EngagementPartner", $"/app/engagements/{engagementId}/completion", null, false, false,
        BlockerDetails: partnerDetails.Count == 0 ? null : partnerDetails), includesHolds: holds.Count > 0);
    }

    if (allSubmittedForReview || partnerClearance is not null)
    {
      var mgrBlocked = new List<string>();
      var mgrDetails = new List<EngagementLifecycleBlockerDetail>();
      if (holds.Count > 0) mgrBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      var handoverBlockerOffset = mgrBlocked.Count;
      mgrBlocked.AddRange(handoverBlockers);
      mgrDetails.AddRange(handoverDetails.Select(detail => detail with
      {
        BlockerIndex = detail.BlockerIndex + handoverBlockerOffset
      }));
      return await ProjectAsync(new EngagementLifecycleSummary(
        CanonicalEngagementStages.ManagerialReview, "Managerial Review", 7, mgrBlocked,
        "AuditManager", $"/app/engagements/{engagementId}/audit-fieldwork", null, false, false,
        BlockerDetails: mgrDetails.Count == 0 ? null : mgrDetails), includesHolds: holds.Count > 0);
    }

    // Planning approval means the CURRENT materiality is Partner-approved and not stale (STE 3.2), not merely calculated.
    var materiality = await MaterialityEngineService.GetLatestAsync(db, actor.FirmId, engagementId, ct);
    var planApproved = !engagement.ProfessionalWorkBlocked && (materiality is not null
      ? materiality.State == MaterialityCalculationStates.Approved
      : await IndependentlyApprovedLegacyMaterialityAsync(db, actor.FirmId, engagementId, ct));

    if (planApproved && procedures.Count > 0)
    {
      var fwBlocked = new List<string>();
      if (holds.Count > 0) fwBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      var pendingApplicability = procedures.Count(x => applicability.GetValueOrDefault(x.Id, x.ApplicabilityStatus) is
        AuditApplicabilityStatuses.Pending or AuditApplicabilityStatuses.NotApplicablePendingReview or
        AuditApplicabilityStatuses.NotApplicableUnverified);
      if (pendingApplicability > 0) fwBlocked.Add($"{pendingApplicability} procedure applicability decision(s) remain unresolved");
      var pendingCount = procedureProgress.Count(x => !x.IsSubmitted &&
        applicability.GetValueOrDefault(x.Procedure.Id, x.Procedure.ApplicabilityStatus) == AuditApplicabilityStatuses.Applicable);
      if (pendingCount > 0) fwBlocked.Add($"{pendingCount} substantive audit procedure(s) pending execution");
      var returnedCount = procedureProgress.Count(x =>
        applicability.GetValueOrDefault(x.Procedure.Id, x.Procedure.ApplicabilityStatus) == AuditApplicabilityStatuses.Applicable &&
        (x.Procedure.Status == AuditProcedureStatuses.ChangesRequired ||
         procedureResults.Any(result => result.AuditProcedureId == x.Procedure.Id && result.Revision == x.Procedure.CurrentResultRevision &&
           result.Status == AuditProcedureResultStatuses.ChangesRequired)));
      if (returnedCount > 0) fwBlocked.Add($"{returnedCount} audit procedure(s) returned for preparer rework");
      return await ProjectAsync(new EngagementLifecycleSummary(
        CanonicalEngagementStages.FieldworkExecution, "Fieldwork Execution", 6, fwBlocked,
        "SeniorAuditor", $"/app/engagements/{engagementId}/audit-fieldwork", null, false, false), includesHolds: holds.Count > 0);
    }

    // 5. Portal Active & Planning
    var activation = await db.EngagementActivations.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    if (activation is not null)
    {
      var planBlocked = new List<string>();
      var planDetails = new List<EngagementLifecycleBlockerDetail>();
      if (holds.Count > 0) planBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      var hasTb = await db.TrialBalanceDatasets.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
      if (!hasTb)
        AddGuidedBlocker(planBlocked, planDetails, "TRIAL_BALANCE_INTAKE_ACCEPTANCE_REQUIRED",
          "Trial balance intake and acceptance required", "Authorized accounting preparer and reviewer",
          "Waiting for an authorized user to provide the source and a scoped accounting reviewer to independently accept the exact sealed dataset; this projection imports and accepts nothing.");
      if (!planApproved && materiality?.State == MaterialityCalculationStates.Stale)
        AddGuidedBlocker(planBlocked, planDetails, "MATERIALITY_STALE_REAPPROVAL_REQUIRED",
          "Materiality is stale: the mapping or trial balance changed. Recalculate and re-approve before planning can advance.",
          "Authorized planning role and Partner",
          "Waiting for an authorized planning role to recalculate from current inputs and a Partner to record current planning approval. This projection does neither.");
      else if (!planApproved)
        AddGuidedBlocker(planBlocked, planDetails, "MATERIALITY_CALCULATION_PARTNER_APPROVAL_REQUIRED",
          "Materiality calculation and formal Partner planning approval required", "Authorized planning role and Partner",
          "Waiting for an authorized planning role to calculate materiality and a Partner to separately record planning approval. This projection does not calculate or approve materiality.");
      if (engagement.ProfessionalWorkBlocked) planBlocked.Add("Engagement professional work is currently blocked");
      return await ProjectAsync(new EngagementLifecycleSummary(
        CanonicalEngagementStages.PortalActivePlanning, "Portal Active & Planning", 5, planBlocked,
        "AuditManager", $"/app/engagements/{engagementId}/audit-plan", null, false, false,
        BlockerDetails: planDetails.Count == 0 ? null : planDetails), includesHolds: holds.Count > 0);
    }

    // 4. Advance Billing & 3. Dual-Key Pending. Both keys are read from the evidence the protected engagement-letter and
    // fee-agreement guards use: the newest non-pending Partner decision must be an unconditional Accepted decision at the
    // current client generation, and client acceptance must cite the exact dispatched offer. A stale, conditional or
    // declined key never advances the stage.
    var acceptance = await EngagementRiskEvidence.LatestAsync(db, actor.FirmId, engagement, ct);
    if (acceptance is not { Decision: "Accepted", DecidedByUserId: not null, DecidedAt: not null } ||
      acceptance.Generation != await DualKeyEvidence.InputGenerationAsync(db, actor.FirmId, engagement.PracticeClientId, ct)) acceptance = null;
    var proposal = await DualKeyEvidence.ResolveEngagementProposalAsync(db, actor.FirmId, engagementId,
      engagement.PracticeClientId, engagement.ServiceRoute, engagement.PeriodStart, engagement.PeriodEnd, ct);
    var commercialAccepted = proposal is { SentAt: not null } &&
      await DualKeyEvidence.CommercialAcceptanceStandsAsync(db, actor.FirmId, proposal, ct);

    if (acceptance is not null && proposal is not null && commercialAccepted)
    {
      var feeBlocked = new List<string>();
      var feeDetails = new List<EngagementLifecycleBlockerDetail>();
      if (holds.Count > 0) feeBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      // Both keys are current; the stage still needs the authorized letter for that exact risk clearance and the linked fee agreement.
      var letterIssued = await db.CommercialDocuments.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ProposalId == proposal.Id && x.Kind == CommercialDocumentKinds.EngagementLetter && x.AcceptanceDecisionId == acceptance.Id, ct);
      if (!letterIssued)
        AddGuidedBlocker(feeBlocked, feeDetails, "ENGAGEMENT_LETTER_REQUIRED",
          "Issue the Engagement Letter for the current accepted offer", "Firm-wide Partner",
          "Waiting for a current firm-wide Partner to generate the letter through the existing commercial-document workflow; this projection does not generate or sign it.");
      var agreement = await db.EngagementFeeAgreements.AsNoTracking()
        .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id, ct);
      if (agreement is null)
        AddGuidedBlocker(feeBlocked, feeDetails, "FEE_AGREEMENT_REQUIRED",
          "Create the fee agreement and its 50% advance milestone", "Firm-wide commercial role",
          "Waiting for an authorized firm-wide commercial role to create the reviewed agreement from the exact accepted offer and approved quotation; this projection creates no agreement or milestone.");
      else if (agreement.EngagementId != engagementId)
        AddGuidedBlocker(feeBlocked, feeDetails, "FEE_AGREEMENT_LINK_REQUIRED",
          "Link the fee agreement to this engagement", "Firm-wide commercial role",
          "Waiting for an authorized firm-wide commercial role to link the agreement to this engagement; this projection changes no link.");
      // Use the activation predicate: retained paid evidence plus the effect of approved allocation reversals.
      var advancePaid = agreement is not null && await AuditSphereOps.Application.Practice.FeeAgreementService.AdvanceCurrentlyPaidAsync(db, actor.FirmId, agreement.Id, ct);
      if (!advancePaid)
        AddGuidedBlocker(feeBlocked, feeDetails, "ADVANCE_PAYMENT_ALLOCATION_REQUIRED",
          "50% advance invoice payment allocation required to activate portal onboarding", "FinanceManager or FinanceReviewer",
          "Waiting for an authorized finance reviewer to record and apply the advance payment through the existing fee workflow; this projection records no payment or allocation.");
      return await ProjectAsync(new EngagementLifecycleSummary(
        CanonicalEngagementStages.AdvanceBilling, "Advance Billing & Payment", 4, feeBlocked,
        "FinanceManager", $"/app/finance", null, false, false,
        BlockerDetails: feeDetails.Count == 0 ? null : feeDetails), includesHolds: holds.Count > 0);
    }

    if (proposal is { SentAt: not null, Status: CrmStates.ProposalSent or CrmStates.ProposalAccepted or CrmStates.ProposalDeclined })
    {
      var dualBlocked = new List<string>();
      var details = new List<EngagementLifecycleBlockerDetail>();
      if (acceptance is null)
        AddGuidedBlocker(dualBlocked, details, "PARTNER_RISK_ACCEPTANCE_REQUIRED",
          "Unconditional Partner risk acceptance decision required", "Partner",
          "Waiting for current unconditional Partner risk acceptance. An authorized Partner determines the appropriate next step.");
      if (proposal.Status == CrmStates.ProposalDeclined)
        AddGuidedBlocker(dualBlocked, details, "CLIENT_DECLINED_PROPOSAL",
          "The client declined the dispatched proposal; revise and resend or close the opportunity.", "Commercial roles",
          "Waiting for an authorized commercial role to decide whether to revise and dispatch a new offer or close the opportunity; this projection does not change proposal state.");
      else if (proposal.Status != CrmStates.ProposalAccepted)
        AddGuidedBlocker(dualBlocked, details, "CLIENT_COMMERCIAL_ACCEPTANCE_REQUIRED",
          "Client commercial acceptance of proposal required", "Dispatched client respondent",
          "Waiting for the client's response to the exact dispatched offer. Staff must record acceptance only from the existing source evidence; this projection records no response.");
      else if (!commercialAccepted)
        dualBlocked.Add("Client acceptance does not cite the current dispatched offer or quotation revision; re-record acceptance against the exact current offer.");
      return await ProjectAsync(new EngagementLifecycleSummary(
        CanonicalEngagementStages.DualKeyPending, "Dual-Key Acceptance Pending", 3, dualBlocked,
        "EngagementPartner", $"/app/assessments/{engagement.PracticeClientId}", null, false, false,
        BlockerDetails: details.Count == 0 ? null : details));
    }

    // 2. Proposal Generation & 1. Lead Ingestion
    var proposalBlockers = new List<string>();
    var proposalDetails = new List<EngagementLifecycleBlockerDetail>();
    AddGuidedBlocker(proposalBlockers, proposalDetails, "COMMERCIAL_PROPOSAL_PREPARATION_REQUIRED",
      "Commercial proposal preparation and dispatch required", "Commercial roles",
      "Waiting for an authorized commercial role to prepare and dispatch the proposal through the existing workflow. This projection does not dispatch or record client acceptance.");
    return await ProjectAsync(new EngagementLifecycleSummary(
      CanonicalEngagementStages.ProposalGeneration, "Proposal Generation", 2,
      proposalBlockers,
      "CommercialManager", $"/app/clients/{engagement.PracticeClientId}", null, false, false,
      BlockerDetails: proposalDetails));
  }

  private static void AddGuidedBlocker(
    List<string> blockers,
    List<EngagementLifecycleBlockerDetail> details,
    string code,
    string cause,
    string responsibleRole,
    string guidance)
  {
    var index = blockers.Count;
    blockers.Add(cause);
    details.Add(new(index, code, cause, responsibleRole, guidance));
  }

  internal static bool IsCurrentProcedureSubmittedForReview(
    AuditProcedure procedure, AuditProcedureResult? currentResult, AuditProcedureReview? currentReview,
    bool currentNotApplicableApprovalVerified = false)
  {
    if (procedure.ApplicabilityStatus == AuditApplicabilityStatuses.NotApplicableApproved)
      return currentNotApplicableApprovalVerified;
    if (procedure.ApplicabilityStatus != AuditApplicabilityStatuses.Applicable || currentResult is null ||
        currentResult.Revision != procedure.CurrentResultRevision || procedure.CurrentResultRevision <= 0)
      return false;
    if (procedure.Status == AuditProcedureStatuses.ChangesRequired ||
        currentResult.Status == AuditProcedureResultStatuses.ChangesRequired ||
        currentReview?.Decision == AuditProcedureReviewDecisions.ChangesRequired)
      return false;
    return (procedure.Status is AuditProcedureStatuses.Submitted or AuditProcedureStatuses.InReview or AuditProcedureStatuses.Reviewed) &&
      (currentResult.Status is AuditProcedureResultStatuses.Submitted or AuditProcedureResultStatuses.Reviewed);
  }

  internal static string PostSignatureStage(bool freezeScheduled, bool exactBundleAvailable) =>
    freezeScheduled && exactBundleAvailable
      ? CanonicalEngagementStages.ComplianceCountdown
      : CanonicalEngagementStages.DeliverableRelease;

  internal static bool PartnerHandoverReady(int completionBlockerCount) => completionBlockerCount == 0;

  /// <summary>
  /// A manually entered (pre-engine) materiality counts as approved only when its status is Approved and a separate Partner
  /// with a valid grant recorded the approval. A bare status flag never advances planning.
  /// </summary>
  private static async Task<bool> IndependentlyApprovedLegacyMaterialityAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var latest = await db.MaterialityAssessments.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    return latest is { Status: MaterialityStatuses.Approved } && await MaterialityEngineService.HasIndependentPartnerApprovalAsync(db, latest, ct);
  }

  /// <summary>
  /// Local archive state is never presented as provider immutability: until a provider read-only state is observed, the
  /// lifecycle carries a compliance warning naming the provider status.
  /// </summary>
  private static string? ComplianceWarningFor(EngagementFileFreeze freeze) =>
    freeze.ExternalReadOnly == ExternalReadOnlyStates.Observed
      ? null
      : $"Provider protection state is {freeze.ExternalReadOnly}. AuditSphere does not claim provider immutability.";

  private static CommandResult<EngagementLifecycleReport> Success(
    Engagement engagement, string clientName, EngagementLifecycleSummary summary, DateTimeOffset now) =>
    CommandResult<EngagementLifecycleReport>.Ok(new(
      engagement.Id, engagement.PracticeClientId, clientName, engagement.ServiceRoute, summary, now));
}
