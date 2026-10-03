using System.Data;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record EvidenceActionRequest(Guid RequestId, string ReviewBasis, string Action, Guid? ResultId,
  string? ResultBasis, string Decision, string Reason, string EvidenceReference, bool Reviewed = false);
public sealed record EvidenceActionPreview(string Kind, Guid EvidenceId, string Action, string ReviewBasis,
  string RequestHash, bool CanProceed, IReadOnlyList<string> Blockers);
public sealed record EvidenceActionReceipt(Guid Id, Guid RequestId, string RequestHash, string Kind, Guid EvidenceId,
  string Action, Guid ActorId, Guid? ResultId, Guid? LinkId, string Decision, string Reason,
  string EvidenceReference, DateTimeOffset CreatedAt);
public sealed record EvidenceActionLookup(bool Found, EvidenceActionReceipt? Receipt);
public sealed record EvidenceActionState(string Kind, Guid EvidenceId, string ReviewBasis, bool CanLink, bool CanReview,
  IReadOnlyList<string> Decisions, IReadOnlyList<string> Blockers, int Page, int Count, bool HasMore,
  IReadOnlyList<EvidenceActionReceipt> History);
public sealed record EvidenceProcedureCandidate(Guid ResultId, Guid WorkpaperId, Guid ProcedureId, string ProcedureCode,
  string Status, long Revision, long InputGeneration, bool CurrentIndependentReview, bool AlreadyLinked, string ResultBasis);
public sealed record EvidenceProcedurePage(string Kind, Guid EvidenceId, string ReviewBasis, int Page, int Count,
  bool HasMore, IReadOnlyList<EvidenceProcedureCandidate> Rows);

/// <summary>Native reviewed link/review commands. Preparation and professional decisions remain in
/// the existing analysis service; this boundary adds immutable intent, source fencing and recovery.</summary>
public static partial class AccountingEvidenceWorkspace
{
  private static readonly string[] PrepareRoles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private static readonly string[] ReviewRoles = ["Administrator", "Partner", "Manager", "AccountingReviewer"];
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static Task<CommandResult> Auth(IClientAccountingDbContext db, ActorContext a, AccountingAnalysisReview v,
    string[] roles, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, a, new(a.FirmId, v.ClientId, v.EngagementId, roles, InternalOnly: true), ct);
  private static EvidenceActionReceipt Receipt(AccountingEvidenceAction x) => new(x.Id, x.RequestId, x.RequestHash,
    x.EvidenceKind, x.EvidenceId, x.Action, x.ActorId, x.ResultId, x.LinkId, x.Decision, x.Reason, x.EvidenceReference, x.CreatedAt);
  private static string Hash(ActorContext a, string kind, Guid id, EvidenceActionRequest r) => Hashing.Sha256Hex(JsonSerializer.Serialize(new {
    a.FirmId, a.UserId, a.SessionEpoch, kind, id, r.RequestId, r.ReviewBasis, r.Action, r.ResultId,
    r.ResultBasis, r.Decision, r.Reason, r.EvidenceReference
  }));
  private static bool Valid(EvidenceActionRequest? r) => r is not null && r.RequestId != Guid.Empty &&
    SourceAcceptanceWorkspace.ValidHash(r.ReviewBasis) && r.Action is "LINK" or "REVIEW" &&
    r.Reason is { Length: > 0 and <= 4000 } && r.Reason.Trim().Length > 0 &&
    r.EvidenceReference is { Length: > 0 and <= 2000 } && r.EvidenceReference.Trim().Length > 0 &&
    (r.Action == "LINK" ? r.ResultId is { } result && result != Guid.Empty && SourceAcceptanceWorkspace.ValidHash(r.ResultBasis) && r.Decision == "" :
      r.ResultId is null && r.ResultBasis is null && r.Decision is "APPROVED" or "CHANGES_REQUIRED" or "REJECTED" or "ESCALATED");

  public static async Task<CommandResult<EvidenceActionState>> StateAsync(IClientAccountingDbContext db, ActorContext a,
    string kind, Guid id, int page = 0, CancellationToken ct = default)
  {
    if (page is < 0 or >= 40) return Fail<EvidenceActionState>(ErrorCodes.Accounting.ReconciliationRejected, "Choose a bounded history page.");
    var current = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
    if (!current.Succeeded) return Fail<EvidenceActionState>(current.ErrorCode!, current.Message!);
    var v = current.Value!;
    var book = await BookAsync(db, a, v, ct);
    var periodOpen = await db.ClientReportingPeriods.AsNoTracking().AnyAsync(x => x.FirmId == a.FirmId && x.ClientId == v.ClientId && x.Id == v.PeriodId &&
      (x.Status == "ACTIVE" || x.Status == "DRAFT"), ct);
    var bookOpen = book is null || await db.ClientReportingBooks.AsNoTracking().AnyAsync(x => x.FirmId == a.FirmId && x.ClientId == v.ClientId &&
      x.Id == book && x.PeriodId == v.PeriodId && (x.Status == "ACTIVE" || x.Status == "DRAFT"), ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == a.FirmId && x.EngagementId == v.EngagementId && x.State == "FROZEN", ct);
    var mutable = periodOpen && bookOpen && !frozen;
    var canLink = mutable && (await Auth(db, a, v, PrepareRoles, ct)).Succeeded && v.ReviewedAt is null && v.LinkCount < 500;
    var canReview = (await Auth(db, a, v, ReviewRoles, ct)).Succeeded && v.CreatedByUserId is not null &&
      v.CreatedByUserId != a.UserId && v.ReviewedAt is null && mutable;
    var decisions = !canReview ? Array.Empty<string>() : kind == "JOURNAL_RISK" ? ["ESCALATED"] :
      v.InputsCurrent ? (v.HasCurrentReviewedProcedure ? new[] { "APPROVED", "CHANGES_REQUIRED", "REJECTED" } : ["CHANGES_REQUIRED", "REJECTED"]) : [];
    var blockers = new List<string>();
    if (!mutable) blockers.Add("The exact reporting period/book is closed or the engagement file is frozen. An approved amendment is required before new actions.");
    if (v.ReviewedAt.HasValue) blockers.Add("A human review is already retained. Prepare new evidence instead of overwriting that decision.");
    if (kind == "JOURNAL_RISK") blockers.Add("The legacy flag lacks original source digest/generation. Only a human escalation can be recorded; clearance requires new source-bound evidence.");
    else if (!v.InputsCurrent) blockers.AddRange(v.Blockers);
    if (!v.HasCurrentReviewedProcedure) blockers.Add("Approval requires a linked independently reviewed current procedure result.");
    if (!canReview && v.ReviewedAt is null) blockers.Add("Review requires an authorized reviewer other than the evidence preparer.");
    var history = await db.AccountingEvidenceActions.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.ClientId == v.ClientId &&
      x.EngagementId == v.EngagementId && x.EvidenceKind == kind && x.EvidenceId == id)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(1001).ToListAsync(ct);
    if (history.Count > 1000) return Fail<EvidenceActionState>(ErrorCodes.GateBlocked, "The retained history exceeds the interactive bound.");
    var final = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
    if (!final.Succeeded) return Fail<EvidenceActionState>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != v.ReviewBasis) return Fail<EvidenceActionState>(ErrorCodes.StaleRevision, "The reviewed evidence changed during inspection.");
    return CommandResult<EvidenceActionState>.Ok(new(kind, id, v.ReviewBasis, canLink, canReview && decisions.Length > 0,
      decisions, blockers.Distinct().ToArray(), page, history.Count, history.Count > (page + 1) * 25,
      history.Skip(page * 25).Take(25).Select(Receipt).ToArray()));
  }

  public static async Task<CommandResult<EvidenceActionPreview>> PreviewAsync(IClientAccountingDbContext db, ActorContext a,
    string kind, Guid id, EvidenceActionRequest? request, CancellationToken ct = default)
  {
    if (!Valid(request)) return Fail<EvidenceActionPreview>(ErrorCodes.Accounting.ReconciliationRejected, "Use a supported exact intent, reason and evidence reference.");
    var r = request!;
    if (kind == "JOURNAL_RISK" && r.Reason.Trim().Length > 2000)
      return Fail<EvidenceActionPreview>(ErrorCodes.Accounting.ReconciliationRejected, "A retained journal-risk disposition is bounded to 2000 characters.");
    var state = await StateAsync(db, a, kind, id, ct: ct);
    if (!state.Succeeded) return Fail<EvidenceActionPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (r.ReviewBasis != s.ReviewBasis) return Fail<EvidenceActionPreview>(ErrorCodes.StaleRevision, "Refresh the evidence and preview its current basis before writing.");
    var blockers = new List<string>();
    if (r.Action == "REVIEW")
    {
      if (!s.CanReview || !s.Decisions.Contains(r.Decision)) blockers.Add("The chosen decision is blocked by authority, retained review, current inputs or procedure review.");
    }
    else
    {
      if (!s.CanLink) blockers.Add("Links cannot be added to reviewed evidence or outside current preparation authority.");
      var v = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
      if (!v.Succeeded) return Fail<EvidenceActionPreview>(v.ErrorCode!, v.Message!);
      var candidate = await CandidateAsync(db, a, v.Value!, r.ResultId!.Value, ct);
      if (candidate is null) return Fail<EvidenceActionPreview>(ErrorCodes.ScopeDenied, "This procedure result is unavailable in the current evidence scope.");
      if (candidate.ResultBasis != r.ResultBasis) return Fail<EvidenceActionPreview>(ErrorCodes.StaleRevision, "The selected result changed. Select and preview its current revision.");
      if (candidate.AlreadyLinked) blockers.Add("This exact result is already linked. Inspect the retained link instead.");
    }
    var final = await StateAsync(db, a, kind, id, ct: ct);
    if (!final.Succeeded) return Fail<EvidenceActionPreview>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != s.ReviewBasis) return Fail<EvidenceActionPreview>(ErrorCodes.StaleRevision, "The evidence changed during preview.");
    return CommandResult<EvidenceActionPreview>.Ok(new(kind, id, r.Action, s.ReviewBasis, Hash(a, kind, id, r), blockers.Count == 0, blockers));
  }

  public static async Task<CommandResult<EvidenceActionLookup>> LookupAsync(IClientAccountingDbContext db, ActorContext a,
    string kind, Guid id, Guid requestId, string requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !SourceAcceptanceWorkspace.ValidHash(requestHash)) return Fail<EvidenceActionLookup>(ErrorCodes.Accounting.ReconciliationRejected, "Use the exact retained request reference.");
    var view = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
    if (!view.Succeeded) return Fail<EvidenceActionLookup>(view.ErrorCode!, view.Message!);
    var x = await db.AccountingEvidenceActions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == requestId, ct);
    if (x is not null && (x.EvidenceKind != kind || x.EvidenceId != id || x.RequestHash != requestHash))
      return Fail<EvidenceActionLookup>(ErrorCodes.IdempotencyConflict, "This request reference belongs to a different intent.");
    var final = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
    return final.Succeeded ? CommandResult<EvidenceActionLookup>.Ok(new(x is not null, x is null ? null : Receipt(x))) : Fail<EvidenceActionLookup>(final.ErrorCode!, final.Message!);
  }

  public static async Task<CommandResult<EvidenceActionReceipt>> ExecuteAsync(IClientAccountingDbContext db, ActorContext a,
    string kind, Guid id, EvidenceActionRequest? request, CancellationToken ct = default)
  {
    if (!Valid(request) || !request!.Reviewed) return Fail<EvidenceActionReceipt>(ErrorCodes.Accounting.ReconciliationRejected, "Preview and explicitly confirm this exact action.");
    var r = request!;
    var outside = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
    if (!outside.Succeeded) return Fail<EvidenceActionReceipt>(outside.ErrorCode!, outside.Message!);
    var v = outside.Value!;
    var roles = r.Action == "LINK" ? PrepareRoles : ReviewRoles;
    var auth = await Auth(db, a, v, roles, ct);
    if (!auth.Succeeded) return Fail<EvidenceActionReceipt>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    var locked = await GeneralLedgerCompletenessWorkspace.LockAsync(db, a, v.ClientId, v.EngagementId, ct);
    if (!locked.Succeeded) return Fail<EvidenceActionReceipt>(locked.ErrorCode!, locked.Message!);
    // Epoch changes and competing native intents cannot pass while this actor is publishing.
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id = {a.FirmId} AND id = {a.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    var previous = await db.AccountingEvidenceActions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == r.RequestId, ct);
    if (previous is not null)
    {
      if (previous.EvidenceKind != kind || previous.EvidenceId != id || previous.RequestHash != Hash(a, kind, id, r))
        return Fail<EvidenceActionReceipt>(ErrorCodes.IdempotencyConflict, "A changed intent cannot reuse this request identity.");
      auth = await Auth(db, a, v, roles, ct);
      if (!auth.Succeeded) return Fail<EvidenceActionReceipt>(auth.ErrorCode!, auth.Message!);
      await tx.CommitAsync(ct);
      return CommandResult<EvidenceActionReceipt>.Ok(Receipt(previous));
    }
    var book = await BookAsync(db, a, v, ct);
    var mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, a, v.ClientId, v.EngagementId, v.PeriodId, book, ct);
    if (!mutable.Succeeded) return Fail<EvidenceActionReceipt>(mutable.ErrorCode!, mutable.Message!);
    await LockEvidenceAsync(db, a.FirmId, kind, id, ct);
    var proof = await PreviewAsync(db, a, kind, id, r, ct);
    if (!proof.Succeeded) return Fail<EvidenceActionReceipt>(proof.ErrorCode!, proof.Message!);
    if (!proof.Value!.CanProceed) return Fail<EvidenceActionReceipt>(ErrorCodes.GateBlocked, proof.Value.Blockers.First());
    var before = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
    if (!before.Succeeded) return Fail<EvidenceActionReceipt>(before.ErrorCode!, before.Message!);
    Guid? linkId = null;
    if (r.Action == "LINK")
    {
      var link = await AccountingAnalysisService.LinkAccountingEvidenceToProcedureAsync(db, a, new(kind, id, r.ResultId!.Value), ct);
      if (!link.Succeeded) return Fail<EvidenceActionReceipt>(link.ErrorCode!, link.Message!);
      linkId = link.Value;
    }
    else
    {
      var review = await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, a,
        new(kind, id, r.Decision, kind == "JOURNAL_RISK" ? r.Reason : null, r.Reason, r.EvidenceReference), ct);
      if (!review.Succeeded) return Fail<EvidenceActionReceipt>(review.ErrorCode!, review.Message!);
    }
    var after = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
    if (!after.Succeeded) return Fail<EvidenceActionReceipt>(after.ErrorCode!, after.Message!);
    var evidence = new AccountingEvidenceAction { Id = Guid.CreateVersion7(), FirmId = a.FirmId, ClientId = v.ClientId,
      EngagementId = v.EngagementId, EvidenceKind = kind, EvidenceId = id, ActorId = a.UserId, ActorEpoch = a.SessionEpoch,
      RequestId = r.RequestId, RequestHash = proof.Value.RequestHash, ReviewBasis = r.ReviewBasis, Action = r.Action,
      ResultId = r.ResultId, LinkId = linkId, Decision = r.Decision, Reason = r.Reason.Trim(), EvidenceReference = r.EvidenceReference.Trim(),
      BeforeJson = JsonSerializer.Serialize(before.Value), AfterJson = JsonSerializer.Serialize(after.Value), CreatedAt = DateTimeOffset.UtcNow };
    db.AccountingEvidenceActions.Add(evidence);
    await db.SaveChangesAsync(ct);
    auth = await Auth(db, a, v, roles, ct);
    if (!auth.Succeeded) return Fail<EvidenceActionReceipt>(auth.ErrorCode!, auth.Message!);
    mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, a, v.ClientId, v.EngagementId, v.PeriodId, book, ct);
    if (!mutable.Succeeded) return Fail<EvidenceActionReceipt>(mutable.ErrorCode!, mutable.Message!);
    await tx.CommitAsync(ct);
    return CommandResult<EvidenceActionReceipt>.Ok(Receipt(evidence));
  }
}
