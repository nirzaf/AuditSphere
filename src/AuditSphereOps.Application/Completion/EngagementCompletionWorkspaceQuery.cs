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
public sealed record CompletionFreeze(string State, DateTimeOffset ReportSignedAt, DateTimeOffset DueAt, string ExternalReadOnly, int DaysRemaining,
  IReadOnlyList<CompletionAmendment> Amendments);
public sealed record EngagementCompletionWorkspace(Guid EngagementId, IReadOnlyList<CompletionGate> Gates, IReadOnlyList<CompletionRepresentation> Representations,
  Guid? PackageId, string PackageStatus, bool PartnerApproved, string EqrStatus, Guid? ReleaseCandidateId, bool CanPrepareRelease,
  IReadOnlyList<ConfirmationDashboardRow> Confirmations, IReadOnlyList<DeliverableView> Deliverables, CompletionClearance? Clearance, CompletionOpinion? Opinion,
  IReadOnlyList<OpinionFsliOption> OpinionAreas, IReadOnlyList<CompletionShared> Shared, IReadOnlyList<SignedLetterView> SignedLetters, BundleAssemblyState? Bundles,
  IReadOnlyList<string> OpinionTypes, CompletionFreeze? Freeze, int FreezeDays, IReadOnlyList<ActivityEvent>? Trail, string TrailCoverageNote, IReadOnlyList<DocumentLockView> Locks);

/// <summary>
/// Engagement completion projection: gate rows from persisted reviews, representations, EQR and release state, plus
/// the deliverables chain and the file-freeze/activity records. A paid invoice never clears a professional gate.
/// </summary>
public static class EngagementCompletionWorkspaceQuery
{
  private static readonly string[] CompletionRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"];

  public static async Task<CommandResult<EngagementCompletionWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, DateTimeOffset now, CancellationToken ct = default)
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
      foreach (var stage in new[] { FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewStages.PartnerApproval })
      {
        var review = reviews.Where(x => x.Stage == stage).OrderByDescending(x => x.DecidedAt).FirstOrDefault();
        var status = review?.Decision ?? "PENDING";
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
      .OrderByDescending(r => r.CreatedAt).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);

    var dashboard = await AuditDeliverableService.ConfirmationDashboardAsync(db, actor, engagementId, ct);
    var clearance = await AuditDeliverableService.CurrentClearanceAsync(db, actor, engagementId, ct);
    var opinion = await AuditDeliverableService.CurrentOpinionAsync(db, actor, engagementId, ct);
    var shared = (await AuditDeliverableService.SharedAsync(db, actor, engagementId, ct)).Select(s => new CompletionShared(s.Title,
      s.Comments.Where(c => c.ResolvedAt is null).Select(c => new CompletionComment(c.Id, c.Body)).ToList())).Where(s => s.OpenComments.Count > 0).ToList();
    var bundles = await AuditDeliverableService.BundleStateAsync(db, actor, engagementId, ct);
    var freeze = await FileFreezeService.GetAsync(db, actor, engagementId, now, ct);
    var trail = await EngagementActivityQuery.GetAsync(db, actor, engagementId, ct);
    return CommandResult<EngagementCompletionWorkspace>.Ok(new(engagementId, gates, representations, package?.Id, package?.Status ?? "—", partnerApproved,
      eqr?.Status ?? "NOT_ASSIGNED", candidate,
      actor.Roles.Any(x => x is "Administrator" or "Partner") && package?.Status == AccountingPackageStates.PackageValidated &&
        gates.Where(g => g.Name.StartsWith("Package ", StringComparison.Ordinal)).All(g => g.Status == FinancialPackageReviewDecisions.Approved) && package is not null,
      dashboard.Succeeded ? dashboard.Value! : [], await AuditDeliverableService.ListAsync(db, actor, engagementId, ct),
      clearance is null ? null : new CompletionClearance(clearance.ClearedAt),
      opinion is null ? null : new CompletionOpinion(opinion.OpinionType, AuditOpinionTypes.Label(opinion.OpinionType), opinion.FocusArea),
      await AuditDeliverableService.AffectedFinancialStatementAreasAsync(db, actor, engagementId, ct), shared,
      await AuditDeliverableService.SignedRepresentationsAsync(db, actor, engagementId, ct), bundles.Succeeded ? bundles.Value : null, AuditOpinionTypes.All,
      freeze is null ? null : new CompletionFreeze(freeze.State, freeze.ReportSignedAt, freeze.DueAt, freeze.ExternalReadOnly, freeze.DaysRemaining,
        freeze.Amendments.Select(a => new CompletionAmendment(a.Id, a.Reason, a.OpenedAt, a.ClosedAt)).ToList()),
      FileFreezeService.FreezeDays, trail.Succeeded ? trail.Value : null, EngagementActivityQuery.ExternalCoverageNote,
      await EngagementActivityQuery.LocksAsync(db, actor, engagementId, ct)));
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
