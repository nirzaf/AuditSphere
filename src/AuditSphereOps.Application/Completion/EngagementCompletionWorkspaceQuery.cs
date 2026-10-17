using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

public sealed record CompletionGate(string Name, string Status, string Tone, string? Authority, DateTimeOffset? Date);
public sealed record CompletionRepresentation(string Code, string Title, string Narrative, bool Obtained);
public sealed record CompletionClearance(DateTimeOffset ClearedAt);
public sealed record CompletionOpinion(string OpinionType, string Label, string? FocusArea);
public sealed record CompletionComment(Guid Id, string Body);
public sealed record CompletionShared(string Title, IReadOnlyList<CompletionComment> OpenComments);
public sealed record CompletionAmendment(Guid Id, string Reason, DateTimeOffset? OpenedAt, DateTimeOffset? ClosedAt);
public sealed record CompletionRelease(Guid CandidateId, string CandidateStatus, string TargetKind, long TargetRevision, long CandidateRevision,
  string ManifestDigest, DateTimeOffset CandidateCreatedAt, Guid? CheckpointId, string CheckpointState, DateTimeOffset? CheckpointVerifiedAt,
  bool CheckpointMatchesCandidate, bool CheckpointReadBackMatchesManifest, Guid? ReleaseId, DateTimeOffset? ReleasedAt,
  Guid? ReleaseCheckpointId, string? ReleaseManifestDigest, bool ReleaseMatchesCandidate, string IssueState);
public sealed record CompletionFreeze(string State, DateTimeOffset ReportSignedAt, DateTimeOffset DueAt, string ExternalReadOnly, int DaysRemaining,
  IReadOnlyList<CompletionAmendment> Amendments, DateTimeOffset? FrozenAt = null);
public sealed record EngagementCompletionWorkspace(Guid EngagementId, IReadOnlyList<CompletionGate> Gates, IReadOnlyList<CompletionRepresentation> Representations,
  Guid? PackageId, string PackageStatus, bool PartnerApproved, string EqrStatus, Guid? ReleaseCandidateId, CompletionRelease? Release, bool CanPrepareRelease,
  IReadOnlyList<ConfirmationDashboardRow> Confirmations, IReadOnlyList<DeliverableView> Deliverables, CompletionClearance? Clearance, CompletionOpinion? Opinion,
  IReadOnlyList<OpinionFsliOption> OpinionAreas, IReadOnlyList<CompletionShared> Shared, IReadOnlyList<SignedLetterView> SignedLetters, BundleAssemblyState? Bundles,
  IReadOnlyList<string> OpinionTypes, CompletionFreeze? Freeze, int FreezeDays, IReadOnlyList<ActivityEvent>? Trail, string TrailCoverageNote, IReadOnlyList<DocumentLockView> Locks,
  HoldingLetterStatus? HoldingLetter = null, ReportSigningStatus? ReportSigning = null);

/// <summary>
/// Engagement completion projection: gate rows from persisted reviews, representations, EQR and release state, plus
/// the deliverables chain and the file-freeze/activity records. A paid invoice never clears a professional gate.
/// </summary>
public static class EngagementCompletionWorkspaceQuery
{
  private static readonly string[] CompletionRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"];

  public static async Task<CommandResult<EngagementCompletionWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, DateTimeOffset now, CancellationToken ct = default,
    IReportSigningCredentialSource? signing = null)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: CompletionRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<EngagementCompletionWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
    var engagement = await db.Engagements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == engagementId && e.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<EngagementCompletionWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var gates = new List<CompletionGate>();
    var openPoints = await db.ReviewPoints.AsNoTracking().CountAsync(rp => rp.EngagementId == engagementId && !rp.Cleared, ct);
    var totalPoints = await db.ReviewPoints.AsNoTracking().CountAsync(rp => rp.EngagementId == engagementId, ct);
    var reviewStatus = totalPoints == 0 ? "NONE_RECORDED" : openPoints == 0 ? "CLEARED" : "BLOCKING";
    gates.Add(new("All review points cleared (blocker)", reviewStatus, reviewStatus == "CLEARED" ? "ok" : reviewStatus == "BLOCKING" ? "blocked" : "neutral",
      openPoints > 0 ? $"{openPoints} open points" : null, null));

    var package = await db.FinancialPackages.AsNoTracking().Where(fp => fp.EngagementId == engagementId && fp.FirmId == actor.FirmId)
      .OrderByDescending(fp => fp.Revision).FirstOrDefaultAsync(ct);
    var partnerApproved = false;
    if (package is not null)
    {
      var reviews = await db.FinancialPackageReviewDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == package.ClientId &&
          x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id && x.PackageRevision == package.Revision &&
          x.PackageGeneration == package.Generation && x.PackageHash == package.CalculationHash).ToListAsync(ct);
      // The gate follows the chain the latest management decision names. An approval defeated by a later return at an earlier stage is
      // shown as pending, because it must be recorded again before release.
      var latestManagement = FinancialPackageReviewService.InRecordedOrder(
        reviews.Where(x => x.Stage == FinancialPackageReviewStages.ManagementApproval)).LastOrDefault();
      var chain = latestManagement is null ? reviews : reviews.Where(x => x.PresentationRevisionId == latestManagement.PresentationRevisionId &&
        x.PresentationCompositionId == latestManagement.PresentationCompositionId &&
        x.CompositionManifestDigest == latestManagement.CompositionManifestDigest).ToList();
      var effective = FinancialPackageReviewService.EffectiveApprovals(chain);
      foreach (var stage in FinancialPackageReviewService.FormalStageOrder)
      {
        var review = effective[stage] ?? FinancialPackageReviewService.InRecordedOrder(chain.Where(x => x.Stage == stage)).LastOrDefault();
        var status = review is null ? "PENDING"
          : effective[stage] is null && review.Decision == FinancialPackageReviewDecisions.Approved ? "PENDING" : review.Decision;
        gates.Add(new($"Package {stage}", status, status == FinancialPackageReviewDecisions.Approved ? "ok"
          : status is FinancialPackageReviewDecisions.ChangesRequired or FinancialPackageReviewDecisions.Rejected ? "blocked" : "pending", review?.EvidenceReference, review?.DecidedAt));
        if (stage == FinancialPackageReviewStages.PartnerApproval) partnerApproved = status == FinancialPackageReviewDecisions.Approved;
      }
    }
    else gates.Add(new("Final financial statements approved", "NO_PACKAGE", "neutral", null, null));

    var representations = await db.WrittenRepresentations.AsNoTracking().Where(r => r.EngagementId == engagementId && r.FirmId == actor.FirmId).OrderBy(r => r.Code)
      .Select(r => new CompletionRepresentation(r.Code, r.Title, r.Narrative, r.Obtained)).ToListAsync(ct);
    var allObtained = representations.Count > 0 && representations.All(r => r.Obtained);
    gates.Add(new("Written representations obtained", representations.Count == 0 ? "PENDING_REQUEST" : allObtained ? "OBTAINED" : "PENDING", allObtained ? "ok" : "pending", null, null));
    var eqr = await db.EqrCases.AsNoTracking().FirstOrDefaultAsync(e => e.EngagementId == engagementId && e.FirmId == actor.FirmId, ct);
    gates.Add(eqr is null ? new("EQR completed", "NOT_ASSIGNED", "neutral", null, null)
      : new("EQR completed", eqr.Status, eqr.Status == "CONCURRED" ? "ok" : "pending", $"Partner {eqr.EqrPartnerUserId.ToString()[..8]}", eqr.CompletedAt));
    var candidate = await db.ReleaseCandidates.AsNoTracking().Where(r => r.EngagementId == engagementId && r.FirmId == actor.FirmId)
      .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
    var release = candidate is null ? null : await ReleaseSummaryAsync(db, candidate, ct);

    var dashboard = await AuditDeliverableService.ConfirmationDashboardAsync(db, actor, engagementId, ct);
    var clearance = await AuditDeliverableService.CurrentClearanceAsync(db, actor, engagementId, ct);
    var opinion = await AuditDeliverableService.CurrentOpinionAsync(db, actor, engagementId, ct);
    // Opinion selection/signing controls are Engagement-Partner capabilities for this engagement;
    // other readers see the current facts without the Partner-only command surface (STE-REM-05, R01/STE).
    var partnerAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, ["Partner"], InternalOnly: true), ct);
    var isEngagementPartner = partnerAuth.Succeeded && await db.EngagementStaffAssignments.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
      x.EngagementId == engagementId && x.UserId == actor.UserId && x.RevokedAt == null &&
      x.StaffingLevel == Domain.Practice.StaffingLevels.EngagementPartner, ct);
    var releaseAuthority = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, ["Administrator", "Partner"], InternalOnly: true), ct);
    var opinionTypes = isEngagementPartner ? AuditOpinionTypes.All : [];
    var shared = (await AuditDeliverableService.SharedAsync(db, actor, engagementId, ct)).Select(s => new CompletionShared(s.Title,
      s.Comments.Where(c => c.ResolvedAt is null).Select(c => new CompletionComment(c.Id, c.Body)).ToList())).Where(s => s.OpenComments.Count > 0).ToList();
    var bundles = await AuditDeliverableService.BundleStateAsync(db, actor, engagementId, ct);
    var freeze = await FileFreezeService.GetAsync(db, actor, engagementId, now, ct);
    var trail = await EngagementActivityQuery.GetAsync(db, actor, engagementId, ct);
    return CommandResult<EngagementCompletionWorkspace>.Ok(new(engagementId, gates, representations, package?.Id, package?.Status ?? "—", partnerApproved,
      eqr?.Status ?? "NOT_ASSIGNED", candidate?.Id, release,
      releaseAuthority.Succeeded && package?.Status == AccountingPackageStates.PackageValidated &&
        gates.Where(g => g.Name.StartsWith("Package ", StringComparison.Ordinal)).All(g => g.Status == FinancialPackageReviewDecisions.Approved) && package is not null,
      dashboard.Succeeded ? dashboard.Value! : [], await AuditDeliverableService.ListAsync(db, actor, engagementId, ct),
      clearance is null ? null : new CompletionClearance(clearance.ClearedAt),
      opinion is null ? null : new CompletionOpinion(opinion.OpinionType, AuditOpinionTypes.Label(opinion.OpinionType), opinion.FocusArea),
      await AuditDeliverableService.AffectedFinancialStatementAreasAsync(db, actor, engagementId, ct), shared,
      await AuditDeliverableService.SignedRepresentationsAsync(db, actor, engagementId, ct), bundles.Succeeded ? bundles.Value : null, opinionTypes,
      freeze is null ? null : new CompletionFreeze(freeze.State, freeze.ReportSignedAt, freeze.DueAt, freeze.ExternalReadOnly, freeze.DaysRemaining,
        freeze.Amendments.Select(a => new CompletionAmendment(a.Id, a.Reason, a.OpenedAt, a.ClosedAt)).ToList(), freeze.FrozenAt),
      FileFreezeService.FreezeDays, trail.Succeeded ? trail.Value : null, EngagementActivityQuery.ExternalCoverageNote,
      await EngagementActivityQuery.LocksAsync(db, actor, engagementId, ct), await HoldingLetterDispatch.StatusAsync(db, actor.FirmId, engagementId, ct),
      ReportCertificateSigning.Status(signing, now)));
  }

  private static async Task<CompletionRelease> ReleaseSummaryAsync(IAuditSphereDbContext db, AuditSphereOps.Domain.Completion.ReleaseCandidate candidate, CancellationToken ct)
  {
    var release = await db.Releases.AsNoTracking().Where(x => x.FirmId == candidate.FirmId && x.ClientId == candidate.ClientId &&
        x.EngagementId == candidate.EngagementId && x.ReleaseCandidateId == candidate.Id)
      .OrderByDescending(x => x.ReleasedAt).FirstOrDefaultAsync(ct);
    var currentCheckpoint = await db.ReleaseCheckpoints.AsNoTracking().Where(x => x.FirmId == candidate.FirmId && x.ClientId == candidate.ClientId &&
        x.EngagementId == candidate.EngagementId && x.ReleaseCandidateId == candidate.Id && x.CandidateRevision == candidate.Revision &&
        x.ManifestDigest == candidate.ManifestDigest)
      .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    var checkpoint = release is null
      ? currentCheckpoint ?? await db.ReleaseCheckpoints.AsNoTracking().Where(x => x.FirmId == candidate.FirmId && x.ClientId == candidate.ClientId &&
          x.EngagementId == candidate.EngagementId && x.ReleaseCandidateId == candidate.Id)
        .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct)
      : await db.ReleaseCheckpoints.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == candidate.FirmId && x.ClientId == candidate.ClientId &&
          x.EngagementId == candidate.EngagementId && x.Id == release.CheckpointId, ct);

    var checkpointMatchesCandidate = checkpoint is not null && checkpoint.CandidateRevision == candidate.Revision &&
      checkpoint.ManifestDigest == candidate.ManifestDigest;
    var readBackMatches = checkpointMatchesCandidate && checkpoint!.VerifiedStatus == "VERIFIED" && checkpoint.VerifiedAt is not null &&
      checkpoint.ReadBackDigest == candidate.ManifestDigest;
    var releaseMatches = release is not null && candidate.Status == AuditSphereOps.Domain.Completion.ReleaseStates.Issued &&
      release.PackageRevision == candidate.TargetRevision && release.ManifestDigest == candidate.ManifestDigest &&
      release.CheckpointId == checkpoint?.Id && readBackMatches;
    var issueState = releaseMatches ? "ISSUED"
      : release is null && candidate.Status != AuditSphereOps.Domain.Completion.ReleaseStates.Issued ? "NOT_ISSUED" : "RECONCILIATION_REQUIRED";

    return new(candidate.Id, candidate.Status, candidate.TargetKind, candidate.TargetRevision, candidate.Revision, candidate.ManifestDigest,
      candidate.CreatedAt, checkpoint?.Id, checkpoint?.VerifiedStatus ?? "ABSENT", checkpoint?.VerifiedAt, checkpointMatchesCandidate,
      readBackMatches, release?.Id, release?.ReleasedAt, release?.CheckpointId, release?.ManifestDigest, releaseMatches, issueState);
  }

  /// <summary>
  /// Creates (or returns the existing) release candidate for the latest financial package, bound to its exact revision,
  /// calculation hash and the current client/firm safety generations. The approval and release services enforce authority.
  /// </summary>
  public static async Task<CommandResult<Guid>> PrepareReleaseCandidateAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid packageId, CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == packageId && x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    if (package is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The financial package is unavailable in the current firm scope.");
    var existing = await db.ReleaseCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.TargetKind == ReleaseTargetKinds.FinancialPackage &&
      x.TargetId == package.Id && x.TargetRevision == package.Revision && x.ManifestDigest == package.CalculationHash, ct);
    if (existing is not null) return CommandResult<Guid>.Ok(existing.Id);
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var client = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == package.ClientId, ct);
    if (firm is null || client is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Release safety state is unavailable.");
    var approval = await ApprovalService.CreateAsync(db, actor, new CreateApprovalRequest(ReleaseTargetKinds.FinancialPackage, package.Id, package.Revision,
      client.InputGeneration, firm.PolicyGeneration, package.CalculationHash), ct);
    if (!approval.Succeeded) return CommandResult<Guid>.Fail(approval.ErrorCode!, approval.Message!);
    return await ReleaseService.CreateCandidateAsync(db, actor, new CreateReleaseCandidateRequest(approval.Value, ReleaseTargetKinds.FinancialPackage,
      package.Id, package.Revision, client.InputGeneration, firm.PolicyGeneration, package.CalculationHash), ct);
  }
}
