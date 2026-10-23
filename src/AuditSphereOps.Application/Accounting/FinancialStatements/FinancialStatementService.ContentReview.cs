using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record StatementContentReviewEntry(Guid Id, string Decision, string Reason, Guid ReviewedByUserId, DateTimeOffset ReviewedAt,
  string SnapshotSha256Hex);

/// <summary>
/// The independent content review of one draft presentation. Approved is true only when the latest decision on this exact draft snapshot is
/// an approval. ReviewedChecks names the presentation checks an approval satisfies; every other check must pass on its own.
/// </summary>
public sealed record StatementContentReviewState(Guid FinancialPackageId, Guid PresentationRevisionId, string SnapshotSha256Hex,
  bool Approved, bool CanReview, Guid? LatestReviewId, IReadOnlyList<string> ReviewedChecks,
  IReadOnlyList<StatementContentReviewEntry> Reviews);

/// <summary>
/// Final-content readiness for a signing candidate (CP-12–14/17, F-03). A draft presentation may be incomplete. A signing candidate may be
/// created only when every presentation check passes: mechanical checks on their own, and the professional content checks (note and
/// disclosure content, equity evidence) through an independent reviewer's approval of the exact draft snapshot. Formal decisions and
/// release recheck that the candidate carries no unresolved check, so a candidate created before this rule cannot gain approval.
/// </summary>
public static partial class FinancialStatementService
{
  public const string StatementReadinessProfileVersion = "statement-candidate-readiness.v1";
  public const string NoteContentCheck = "NOTE_CONTENT";
  public const string EquitySourceLineageCheck = "EQUITY_SOURCE_LINEAGE";
  public const string EquityEvidenceReviewCheck = "EQUITY_EVIDENCE_REVIEW";
  private const int MaxStatementContentReviewEntries = 50;

  /// <summary>Presentation checks that only an independent professional review of the exact snapshot can satisfy.</summary>
  public static readonly IReadOnlyList<string> ContentReviewedChecks = [NoteContentCheck, EquitySourceLineageCheck];
  private static readonly string[] ContentReviewPendingWarnings = ["NOTE_CONTENT_PENDING", "EQUITY_READINESS_PENDING"];

  /// <summary>
  /// The checks a signing candidate carries. With an approved content review, the note-content check passes on that review and the equity
  /// lineage check is replaced by a truthful reviewed-manual-evidence check; preparer evidence is never relabelled as ledger-derived. Without
  /// one, the draft checks are carried unchanged and still fail. Pure.
  /// </summary>
  internal static IReadOnlyList<PresentationCheck> CandidateChecks(IReadOnlyList<PresentationCheck> draftChecks,
    FinancialStatementContentReview? approvedReview)
  {
    if (approvedReview is null) return draftChecks;
    return draftChecks.Select(check => check.Code switch
    {
      NoteContentCheck when !check.Passed => new PresentationCheck(NoteContentCheck, true,
        $"Disclosure responses, applicability and note schedules in this snapshot were approved by independent content review {approvedReview.Id:D}."),
      EquitySourceLineageCheck when !check.Passed => new PresentationCheck(EquityEvidenceReviewCheck, true,
        $"Equity movements rest on preparer-entered evidence references examined in independent content review {approvedReview.Id:D}. They are reviewed manual evidence, not resolved to accepted general-ledger records."),
      _ => check,
    }).ToList();
  }

  /// <summary>Codes of every check that does not pass, in presentation order.</summary>
  internal static IReadOnlyList<string> UnresolvedChecks(IReadOnlyList<PresentationCheck> checks) =>
    checks.Where(x => !x.Passed).Select(x => x.Code).Distinct(StringComparer.Ordinal).ToList();

  /// <summary>True when the stored presentation snapshot decodes and every one of its checks passes.</summary>
  internal static bool PresentationChecksAllPass(FinancialStatementPresentationRevision revision)
  {
    try
    {
      var presentation = JsonSerializer.Deserialize<FinancialStatementPresentation>(revision.SnapshotJson, PresentationSnapshotJson);
      return presentation is { Checks.Count: > 0 } && presentation.Checks.All(x => x.Passed);
    }
    catch (JsonException)
    {
      return false;
    }
  }

  /// <summary>The latest content decision on the draft when it approves the draft's exact snapshot; otherwise null.</summary>
  internal static async Task<FinancialStatementContentReview?> ApprovedStatementContentReviewAsync(IClientAccountingDbContext db,
    FinancialStatementPresentationRevision draft, CancellationToken ct)
  {
    var latest = await db.FinancialStatementContentReviews.AsNoTracking()
      .Where(x => x.FirmId == draft.FirmId && x.ClientId == draft.ClientId && x.EngagementId == draft.EngagementId &&
        x.PresentationRevisionId == draft.Id)
      .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
    return latest is { Decision: FinancialStatementContentReviewDecisions.Approved } && latest.SnapshotSha256Hex == draft.SnapshotSha256Hex
      ? latest
      : null;
  }

  public static async Task<CommandResult<Guid>> RecordStatementContentReviewAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid packageId, Guid revisionId, string? decision, string? reason, Guid requestId, string? expectedSnapshotSha256Hex,
    Guid? expectedPreviousReviewId, CancellationToken ct = default)
  {
    var normalized = decision?.Trim().ToUpperInvariant() ?? string.Empty;
    var text = reason?.Trim() ?? string.Empty;
    var snapshot = expectedSnapshotSha256Hex?.Trim().ToLowerInvariant() ?? string.Empty;
    if (packageId == Guid.Empty || revisionId == Guid.Empty || requestId == Guid.Empty ||
        normalized is not (FinancialStatementContentReviewDecisions.Approved or FinancialStatementContentReviewDecisions.Returned) ||
        text.Length > 2000 || (normalized == FinancialStatementContentReviewDecisions.Returned && text.Length == 0) ||
        snapshot.Length != 64 || !snapshot.All(Uri.IsHexDigit))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Choose approve or return, give a reason when returning, and identify the exact draft snapshot you reviewed.");
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == packageId, ct);
    if (package is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, package.ClientId, package.EngagementId, CashFlowSourceReviewRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    // Shared lock order: client safety, then the package, then the reviewer's grants. Authority is checked again once the locks are held.
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    await db.ClientSafetyStates.FromSqlInterpolated(
        $"SELECT * FROM client_safety_states WHERE firm_id = {actor.FirmId} AND id = {package.ClientId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    var locked = await db.FinancialPackages.FromSqlInterpolated(
        $"SELECT * FROM financial_packages WHERE firm_id = {actor.FirmId} AND id = {packageId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (locked is null || locked.ClientId != package.ClientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await db.RoleGrants.FromSqlInterpolated(
        $"SELECT * FROM role_grants WHERE firm_id = {actor.FirmId} AND user_id = {actor.UserId} AND revoked_at IS NULL FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, locked.ClientId, locked.EngagementId, CashFlowSourceReviewRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    // The same original intent recovers the stored decision. The same identity with a different payload is a conflict, never an overwrite.
    var intentHash = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(string.Join('\u001f', "statement-content-review-intent.v1",
      normalized, text, snapshot, expectedPreviousReviewId?.ToString("D") ?? "")));
    var recorded = await db.FinancialStatementContentReviews.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.PresentationRevisionId == revisionId && x.RequestId == requestId, ct);
    if (recorded is not null)
    {
      if (recorded.ReviewedByUserId != actor.UserId)
        return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This request was already used by another reviewer.");
      return recorded.RequestHash == intentHash
        ? CommandResult<Guid>.Ok(recorded.Id)
        : CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This request was already used for a different content decision.");
    }

    var draft = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revisionId && x.FirmId == locked.FirmId && x.ClientId == locked.ClientId && x.EngagementId == locked.EngagementId &&
      x.FinancialPackageId == locked.Id, ct);
    if (draft is null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "The draft presentation revision does not exist.");
    if (draft.Status != FinancialStatementPresentationRevisionStatuses.UnapprovedDraft)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Content review applies to an unapproved draft. A signing candidate carries the review it was created from.");
    if (draft.CreatedByUserId == actor.UserId)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The preparer who pinned this draft cannot review its content.");
    if (!string.Equals(draft.SnapshotSha256Hex, snapshot, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The draft content changed after it was loaded. Reload the draft before deciding.");
    if (!await PresentationSourceIsCurrentAsync(db, draft, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "The draft presentation source is no longer current. Pin a new draft from the current package before review.");
    var latest = await db.FinancialStatementContentReviews.AsNoTracking()
      .Where(x => x.FirmId == draft.FirmId && x.PresentationRevisionId == draft.Id)
      .OrderByDescending(x => x.Sequence).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    if (latest != expectedPreviousReviewId)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "A content decision was recorded after this review was loaded. Reload the review before deciding.");

    var review = new FinancialStatementContentReview
    {
      Id = Guid.CreateVersion7(), FirmId = draft.FirmId, ClientId = draft.ClientId, EngagementId = draft.EngagementId,
      FinancialPackageId = draft.FinancialPackageId, PresentationRevisionId = draft.Id, SnapshotSha256Hex = draft.SnapshotSha256Hex,
      Decision = normalized, Reason = text, ReviewedByUserId = actor.UserId, ReviewedAt = DateTimeOffset.UtcNow,
      RequestId = requestId, RequestHash = intentHash, PreviousReviewId = expectedPreviousReviewId
    };
    db.FinancialStatementContentReviews.Add(review);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_financial_statement_content_review_request", StringComparison.Ordinal) == true)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This content decision was recorded concurrently. Reload the review.");
    }
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(review.Id);
  }

  public static async Task<CommandResult<StatementContentReviewState>> GetStatementContentReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid packageId, Guid revisionId, CancellationToken ct = default)
  {
    var draft = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revisionId && x.FirmId == actor.FirmId && x.FinancialPackageId == packageId, ct);
    if (draft is null)
      return CommandResult<StatementContentReviewState>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, draft.ClientId, draft.EngagementId, CashFlowSourceReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<StatementContentReviewState>.Fail(auth.ErrorCode!, auth.Message!);
    var reviews = await db.FinancialStatementContentReviews.AsNoTracking()
      .Where(x => x.FirmId == draft.FirmId && x.ClientId == draft.ClientId && x.EngagementId == draft.EngagementId &&
        x.PresentationRevisionId == draft.Id)
      .OrderByDescending(x => x.Sequence).Take(MaxStatementContentReviewEntries)
      .Select(x => new StatementContentReviewEntry(x.Id, x.Decision, x.Reason, x.ReviewedByUserId, x.ReviewedAt, x.SnapshotSha256Hex))
      .ToListAsync(ct);
    var reviewAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, draft.ClientId, draft.EngagementId, CashFlowSourceReviewRoles, InternalOnly: true), ct);
    var canReview = reviewAuth.Succeeded && draft.Status == FinancialStatementPresentationRevisionStatuses.UnapprovedDraft &&
      draft.CreatedByUserId != actor.UserId;
    var approved = reviews.Count > 0 && reviews[0].Decision == FinancialStatementContentReviewDecisions.Approved &&
      reviews[0].SnapshotSha256Hex == draft.SnapshotSha256Hex;
    // Review permission and read permission are independent. A failed review check must not
    // allow retained content to escape after the actor's read grant was revoked during the fetch.
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, draft.ClientId, draft.EngagementId, CashFlowSourceReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<StatementContentReviewState>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<StatementContentReviewState>.Ok(new(draft.FinancialPackageId, draft.Id, draft.SnapshotSha256Hex, approved,
      canReview, reviews.Count > 0 ? reviews[0].Id : null, ContentReviewedChecks, reviews));
  }
}
