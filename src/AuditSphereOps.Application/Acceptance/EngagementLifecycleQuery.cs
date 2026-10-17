using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
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
  bool IsLegacyUnverified,
  string? ComplianceWarning = null,
  string? LocalArchiveState = null,
  string? ProviderProtectionState = null);

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
    var detailAuthorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId,
        RequiredRoles: ["Administrator", "Partner", "Manager", "SeniorManager"], InternalOnly: true, RequireFirmWide: true), ct);
    var canViewDetailedBlockers = detailAuthorization.Succeeded;

    CommandResult<EngagementLifecycleReport> Project(EngagementLifecycleSummary summary)
    {
      if (!canViewDetailedBlockers && summary.BlockedReasons.Count > 0)
        summary = summary with { BlockedReasons = ["Some engagement readiness details are restricted to firm-wide management."] };
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
      return Project(new EngagementLifecycleSummary(
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
      if (holds.Count > 0) countdownBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      if (!bundleAvailableToClient)
        countdownBlocked.Add(deliveredBundle is null
          ? "The current signed report and financial release are not yet assembled as a final bundle available in the client portal."
          : "The final client bundle failed its stored-byte or manifest integrity check and is not treated as delivered.");
      if (freeze.State == FileFreezeStates.Scheduled && now >= freeze.DueAt)
        countdownBlocked.Add("The freeze is due but has not committed; the file is not archived until the local freeze succeeds.");
      var phaseDelivered = PostSignatureStage(freeze.State == FileFreezeStates.Scheduled, bundleAvailableToClient) ==
        CanonicalEngagementStages.ComplianceCountdown;
      return Project(new EngagementLifecycleSummary(
        PostSignatureStage(freeze.State == FileFreezeStates.Scheduled, bundleAvailableToClient),
        phaseDelivered ? "Compliance Countdown" : "Deliverable Release", phaseDelivered ? 10 : 9, countdownBlocked,
        "EngagementPartner", completionLink, remaining, false, false, ComplianceWarningFor(freeze),
        freeze.State, freeze.ExternalReadOnly));
    }

    if (release is not null || signedReport is not null)
    {
      return Project(new EngagementLifecycleSummary(
        CanonicalEngagementStages.DeliverableRelease, "Deliverable Release", 9,
        [signedReport is null
          ? "The compliance countdown starts when the Partner signs the Independent Auditor's Report."
          : "The signed report has no scheduled local compliance freeze; review the signature and archive workflow."],
        "EngagementPartner", completionLink, null, false, false));
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
    if (openReviewNotes > 0) handoverBlockers.Add($"{openReviewNotes} open review note(s) require resolution");
    var completion = await AuditFieldworkService.EvaluateCompletionForAuthorizedScopeAsync(db, actor.FirmId, client.Id, engagementId, ct);
    handoverBlockers.AddRange(completion.Succeeded
      ? completion.Value!.Blockers.Select(b => $"Completion gate: {b}")
      : ["Completion gate could not be evaluated for your role."]);
    if (await AuditDeliverableService.CurrentSummaryReviewMemorandumAsync(db, actor, engagementId, ct) is null)
      handoverBlockers.Add("A current Summary Review Memorandum is required for Partner handover.");

    if (PartnerHandoverReady(handoverBlockers.Count))
    {
      var partnerBlocked = new List<string>();
      if (holds.Count > 0) partnerBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      if (partnerClearance is null) partnerBlocked.Add("Partner clearance of the current Summary Review Memorandum is pending.");
      if (opinion is null) partnerBlocked.Add("Partner audit opinion decision pending");
      else partnerBlocked.Add("Partner signature of the current Independent Auditor's Report pending");
      return Project(new EngagementLifecycleSummary(
        CanonicalEngagementStages.PartnerApproval, "Partner Approval & Signing", 8, partnerBlocked,
        "EngagementPartner", $"/app/engagements/{engagementId}/completion", null, false, false));
    }

    if (allSubmittedForReview || partnerClearance is not null)
    {
      var mgrBlocked = new List<string>();
      if (holds.Count > 0) mgrBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      mgrBlocked.AddRange(handoverBlockers);
      return Project(new EngagementLifecycleSummary(
        CanonicalEngagementStages.ManagerialReview, "Managerial Review", 7, mgrBlocked,
        "AuditManager", $"/app/engagements/{engagementId}/audit-fieldwork", null, false, false));
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
      return Project(new EngagementLifecycleSummary(
        CanonicalEngagementStages.FieldworkExecution, "Fieldwork Execution", 6, fwBlocked,
        "SeniorAuditor", $"/app/engagements/{engagementId}/audit-fieldwork", null, false, false));
    }

    // 5. Portal Active & Planning
    var activation = await db.EngagementActivations.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    if (activation is not null)
    {
      var planBlocked = new List<string>();
      if (holds.Count > 0) planBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      var hasTb = await db.TrialBalanceDatasets.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
      if (!hasTb) planBlocked.Add("Trial balance intake and acceptance required");
      if (!planApproved && materiality?.State == MaterialityCalculationStates.Stale)
        planBlocked.Add("Materiality is stale: the mapping or trial balance changed. Recalculate and re-approve before planning can advance.");
      else if (!planApproved) planBlocked.Add("Materiality calculation and formal Partner planning approval required");
      if (engagement.ProfessionalWorkBlocked) planBlocked.Add("Engagement professional work is currently blocked");
      return Project(new EngagementLifecycleSummary(
        CanonicalEngagementStages.PortalActivePlanning, "Portal Active & Planning", 5, planBlocked,
        "AuditManager", $"/app/engagements/{engagementId}/audit-plan", null, false, false));
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
      if (holds.Count > 0) feeBlocked.AddRange(holds.Select(h => $"Open hold: {h}"));
      // Both keys are current; the stage still needs the authorized letter for that exact risk clearance and the linked fee agreement.
      var letterIssued = await db.CommercialDocuments.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ProposalId == proposal.Id && x.Kind == CommercialDocumentKinds.EngagementLetter && x.AcceptanceDecisionId == acceptance.Id, ct);
      if (!letterIssued) feeBlocked.Add("Issue the Engagement Letter for the current accepted offer");
      var agreement = await db.EngagementFeeAgreements.AsNoTracking()
        .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id, ct);
      if (agreement is null) feeBlocked.Add("Create the fee agreement and its 50% advance milestone");
      else if (agreement.EngagementId != engagementId) feeBlocked.Add("Link the fee agreement to this engagement");
      // Activation reads the milestone state (PAID), never a receipt total or a user-entered flag, so the same state is required here.
      var advancePaid = agreement is not null && await db.FeeMilestones.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.AgreementId == agreement.Id && x.Kind == FeeMilestoneKinds.Advance && x.State == FeeMilestoneStates.Paid, ct);
      if (!advancePaid) feeBlocked.Add("50% advance invoice payment allocation required to activate portal onboarding");
      return Project(new EngagementLifecycleSummary(
        CanonicalEngagementStages.AdvanceBilling, "Advance Billing & Payment", 4, feeBlocked,
        "FinanceManager", $"/app/finance", null, false, false));
    }

    if (proposal is { SentAt: not null, Status: CrmStates.ProposalSent or CrmStates.ProposalAccepted or CrmStates.ProposalDeclined })
    {
      var dualBlocked = new List<string>();
      if (acceptance is null) dualBlocked.Add("Unconditional Partner risk acceptance decision required");
      if (proposal.Status == CrmStates.ProposalDeclined)
        dualBlocked.Add("The client declined the dispatched proposal; revise and resend or close the opportunity.");
      else if (proposal.Status != CrmStates.ProposalAccepted)
        dualBlocked.Add("Client commercial acceptance of proposal required");
      else if (!commercialAccepted)
        dualBlocked.Add("Client acceptance does not cite the current dispatched offer or quotation revision; re-record acceptance against the exact current offer.");
      return Project(new EngagementLifecycleSummary(
        CanonicalEngagementStages.DualKeyPending, "Dual-Key Acceptance Pending", 3, dualBlocked,
        "EngagementPartner", $"/app/assessments/{engagement.PracticeClientId}", null, false, false));
    }

    // 2. Proposal Generation & 1. Lead Ingestion
    return Project(new EngagementLifecycleSummary(
      CanonicalEngagementStages.ProposalGeneration, "Proposal Generation", 2,
      ["Commercial proposal preparation and dispatch required"],
      "CommercialManager", $"/app/clients/{engagement.PracticeClientId}", null, false, false));
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
