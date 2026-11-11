using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record OpeningBalanceFields(Guid? ProcedureId, Guid? PriorPeriodId, string PriorReference,
  DateOnly AsOfDate, string Currency, string OpeningSignedTotal, string AgreedSignedTotal,
  bool? AccountingPoliciesConsistent, string Rationale, IReadOnlyList<string> EvidenceReferences);
public sealed record OpeningBalanceCommandRequest(Guid RequestId, string ExpectedRevision, string ReviewBasis,
  OpeningBalanceFields? Fields = null, Guid? ExpectedVerificationId = null, string? Comment = null,
  string? ExpectedRequestHash = null, bool Reviewed = false);
public sealed record OpeningBalanceSnapshot(Guid Id, Guid EngagementId, Guid? ProcedureId, Guid? PriorPeriodId,
  string PriorReference, DateOnly AsOfDate, string Currency, string OpeningSignedTotal, string AgreedSignedTotal,
  string DifferenceAmount, bool AccountingPoliciesConsistent, string Conclusion, string Rationale,
  IReadOnlyList<string> EvidenceReferences, string Revision, Guid RecordedByUserId, DateTimeOffset RecordedAt,
  Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt, string? ReviewedBasisRevision, string? ReviewComment,
  bool ComparisonResolved, bool CurrentIndependentReview);
public sealed record OpeningBalanceCommandState(Guid EngagementId, Guid ClientId, DateOnly AsOfDate,
  string ExpectedRevision, string ReviewBasis, OpeningBalanceSnapshot? Record, bool CanPrepare, bool CanReview,
  IReadOnlyList<string> Blockers);
public sealed record OpeningBalanceCommandPreview(Guid RequestId, string Action, string RequestHash,
  string ReviewBasis, string ExpectedRevision, bool CanProceed, IReadOnlyList<string> Blockers,
  OpeningBalanceSnapshot? CurrentRecord, string Summary);
public sealed record OpeningBalanceCommandReceiptView(Guid Id, Guid EngagementId, Guid VerificationId,
  Guid ActorId, string ActorEpoch, Guid RequestId, string RequestHash, string Action, DateTimeOffset CreatedAt,
  OpeningBalanceCommandPreview Preview, OpeningBalanceSnapshot Result);
public sealed record OpeningBalanceCommandLookup(bool Found, OpeningBalanceCommandReceiptView? Receipt);
internal sealed class OpeningBalanceReceiptContext(string action, OpeningBalanceCommandRequest request,
  OpeningBalanceCommandPreview preview, string inputJson)
{
  internal string Action { get; } = action;
  internal OpeningBalanceCommandRequest Request { get; } = request;
  internal OpeningBalanceCommandPreview Preview { get; set; } = preview;
  internal string InputJson { get; } = inputJson;
}

/// <summary>Product connection to the existing opening owner. Receipts commit inside that owner's transaction.</summary>
public static class AuditOpeningBalanceWorkspace
{
  private static readonly string[] PlanningRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReadRoles = PlanningRoles.Concat(ReviewRoles).Distinct().ToArray();
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string Long(long n) => n.ToString(CultureInfo.InvariantCulture);
  private static string Money(decimal n) => n.ToString("0.######", CultureInfo.InvariantCulture);
  private static bool Hash(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool Revision(string? s, out long n) => long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 0 && n < long.MaxValue && s == Long(n);
  private static bool Amount(string? s, out decimal n) => decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
    CultureInfo.InvariantCulture, out n) && n is >= -9_999_999_999_999.999999m and <= 9_999_999_999_999.999999m && decimal.Round(n, 6) == n && s is { Length: <= 22 };

  internal static OpeningBalanceSnapshot Snapshot(OpeningBalanceVerification r) => new(r.Id, r.EngagementId,
    r.ProcedureId, r.PriorPeriodId, r.PriorReference, r.AsOfDate, r.Currency, Money(r.OpeningSignedTotal), Money(r.AgreedSignedTotal),
    Money(r.DifferenceAmount), r.AccountingPoliciesConsistent, r.Conclusion, r.Rationale,
    JsonSerializer.Deserialize<string[]>(r.EvidenceReferencesJson) ?? [], Long(r.Revision), r.RecordedByUserId,
    r.RecordedAt, r.ReviewedByUserId, r.ReviewedAt, r.ReviewedBasisRevision is { } b ? Long(b) : null, r.ReviewComment,
    r.DifferenceAmount == 0 && r.Conclusion is OpeningBalanceVerificationConclusions.Agreed or OpeningBalanceVerificationConclusions.DifferencesResolved,
    OpeningBalanceReviewCurrentness.IsCurrent(r));

  private sealed record Basis(Guid ClientId, long Generation, long InputGeneration, bool Blocked, bool Held, bool Frozen, OpeningBalanceVerification? Row);
  private static async Task<CommandResult<Basis>> ReadBasisAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, DateOnly date, CancellationToken ct)
  {
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (e is null) return Fail<Basis>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, e.PracticeClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<Basis>(auth.ErrorCode!, auth.Message!);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == e.PracticeClientId).Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct) ?? 0;
    var held = await db.EngagementHolds.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && !x.Released, ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.State == "FROZEN", ct);
    var row = await db.OpeningBalanceVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == e.PracticeClientId && x.EngagementId == engagementId && x.AsOfDate == date, ct);
    return CommandResult<Basis>.Ok(new(e.PracticeClientId, e.Generation, generation, e.ProfessionalWorkBlocked, held, frozen, row));
  }
  private static string BasisHash(ActorContext a, Guid id, DateOnly date, Basis b) => Hashing.Sha256Hex(JsonSerializer.Serialize(new {
    schema = "opening-balance-basis-v1", a.FirmId, a.UserId, a.SessionEpoch, engagementId = id, date,
    b.ClientId, b.Generation, b.InputGeneration, b.Blocked, b.Held, b.Frozen, record = b.Row is null ? null : Snapshot(b.Row) }, Json));

  public static async Task<CommandResult<OpeningBalanceCommandState>> StateAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, DateOnly asOfDate, CancellationToken ct = default)
  {
    var first = await ReadBasisAsync(db, actor, engagementId, asOfDate, ct);
    if (!first.Succeeded) return Fail<OpeningBalanceCommandState>(first.ErrorCode!, first.Message!);
    var b = first.Value!;
    var prepare = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, b.ClientId, engagementId, PlanningRoles, InternalOnly: true), ct);
    var review = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, b.ClientId, engagementId, ReviewRoles, InternalOnly: true), ct);
    var writable = !b.Blocked && !b.Held && !b.Frozen;
    var canPrepare = writable && prepare.Succeeded && (b.Row is null || b.Row.RecordedByUserId == actor.UserId);
    var canReview = writable && review.Succeeded && b.Row is { } row && row.RecordedByUserId != actor.UserId && row.DifferenceAmount == 0 &&
      row.Conclusion is OpeningBalanceVerificationConclusions.Agreed or OpeningBalanceVerificationConclusions.DifferencesResolved && !OpeningBalanceReviewCurrentness.IsCurrent(row);
    var blockers = new List<string>();
    if (!writable) blockers.Add("Professional work is blocked, held or frozen on this engagement.");
    if (b.Row is { } current && current.DifferenceAmount != 0) blockers.Add("Resolve the opening-balance difference before independent review.");
    var hash = BasisHash(actor, engagementId, asOfDate, b);
    var second = await ReadBasisAsync(db, actor, engagementId, asOfDate, ct);
    if (!second.Succeeded) return Fail<OpeningBalanceCommandState>(second.ErrorCode!, second.Message!);
    if (hash != BasisHash(actor, engagementId, asOfDate, second.Value!)) return Fail<OpeningBalanceCommandState>(ErrorCodes.StaleRevision, "The opening-balance basis changed. Refresh the exact date.");
    return CommandResult<OpeningBalanceCommandState>.Ok(new(engagementId, b.ClientId, asOfDate, Long(b.Row?.Revision ?? 0), hash,
      b.Row is null ? null : Snapshot(b.Row), canPrepare, canReview, blockers));
  }

  public static async Task<CommandResult<OpeningBalanceSnapshot>> DetailAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid engagementId, Guid verificationId, CancellationToken ct = default)
  {
    var detail = await AuditPlanningCompletionService.GetOpeningBalanceVerificationAsync(db, actor, engagementId, verificationId, ct);
    if (!detail.Succeeded) return Fail<OpeningBalanceSnapshot>(detail.ErrorCode!, detail.Message!);
    var state = await StateAsync(db, actor, engagementId, detail.Value!.AsOfDate, ct);
    if (!state.Succeeded) return Fail<OpeningBalanceSnapshot>(state.ErrorCode!, state.Message!);
    return state.Value!.Record is { } row && row.Id == verificationId
      ? CommandResult<OpeningBalanceSnapshot>.Ok(row) : Fail<OpeningBalanceSnapshot>(ErrorCodes.ScopeDenied, "Access denied.");
  }

  private static OpeningBalanceCommandRequest? Normalize(OpeningBalanceCommandRequest r, string action)
  {
    if (r.RequestId == Guid.Empty || !Revision(r.ExpectedRevision, out var revision) || !Hash(r.ReviewBasis) ||
      (revision == 0) != (r.ExpectedVerificationId is null) || r.ExpectedVerificationId == Guid.Empty || r.Comment?.Length > 2000) return null;
    if (action == "REVIEW") return r.Fields is null && revision > 0 ? r with { Comment = r.Comment?.Trim() } : null;
    if (r.Comment is not null || r.Fields is not { } f || !Amount(f.OpeningSignedTotal, out var opening) || !Amount(f.AgreedSignedTotal, out var agreed) ||
      f.AccountingPoliciesConsistent is null || string.IsNullOrWhiteSpace(f.PriorReference) || f.PriorReference.Trim().Length > 200 ||
      string.IsNullOrWhiteSpace(f.Rationale) || f.Rationale.Trim().Length > 2000 || f.Currency?.Trim().Length != 3 ||
      f.Currency.Trim().ToUpperInvariant().Any(c => c is < 'A' or > 'Z') || f.EvidenceReferences is null) return null;
    var evidence = f.EvidenceReferences.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
    if (evidence.Length == 0 || JsonSerializer.Serialize(evidence).Length > 20000) return null;
    return r with { Fields = f with { PriorReference = f.PriorReference.Trim(), Currency = f.Currency.Trim().ToUpperInvariant(),
      OpeningSignedTotal = Money(opening), AgreedSignedTotal = Money(agreed), Rationale = f.Rationale.Trim(), EvidenceReferences = evidence } };
  }
  private static string Input(OpeningBalanceCommandRequest r) => JsonSerializer.Serialize(new { r.ExpectedRevision, r.ReviewBasis, r.ExpectedVerificationId, r.Fields, r.Comment }, Json);
  private static string RequestHash(ActorContext a, Guid engagementId, string action, OpeningBalanceCommandRequest r) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { schema = "opening-balance-command-v1", a.FirmId, a.UserId, a.SessionEpoch, engagementId, action, r.RequestId, input = Input(r) }, Json));

  public static Task<CommandResult<OpeningBalanceCommandPreview>> PreviewPrepareAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, OpeningBalanceCommandRequest request, CancellationToken ct = default) => PreviewAsync(db, actor, engagementId, "PREPARE", request, ct);
  public static Task<CommandResult<OpeningBalanceCommandPreview>> PreviewReviewAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid verificationId, OpeningBalanceCommandRequest request, CancellationToken ct = default) =>
    request.ExpectedVerificationId != verificationId ? Task.FromResult(Fail<OpeningBalanceCommandPreview>(ErrorCodes.ScopeDenied, "The exact review target is required.")) : PreviewAsync(db, actor, engagementId, "REVIEW", request, ct);
  private static async Task<CommandResult<OpeningBalanceCommandPreview>> PreviewAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, string action, OpeningBalanceCommandRequest request, CancellationToken ct)
  {
    var r = Normalize(request, action);
    if (r is null) return Fail<OpeningBalanceCommandPreview>(ErrorCodes.Accounting.MappingInvalid, "Use bounded evidence, exact figures and the current opening-balance identity and revision.");
    DateOnly date;
    if (action == "PREPARE") date = r.Fields!.AsOfDate;
    else {
      var row = await db.OpeningBalanceVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Id == r.ExpectedVerificationId, ct);
      if (row is null) return Fail<OpeningBalanceCommandPreview>(ErrorCodes.ScopeDenied, "Access denied.");
      date = row.AsOfDate;
    }
    var state = await StateAsync(db, actor, engagementId, date, ct);
    if (!state.Succeeded) return Fail<OpeningBalanceCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (s.ExpectedRevision != r.ExpectedRevision || s.ReviewBasis != r.ReviewBasis || s.Record?.Id != r.ExpectedVerificationId)
      return Fail<OpeningBalanceCommandPreview>(ErrorCodes.StaleRevision, "Refresh the exact opening-balance preparation before submitting.");
    if (r.Fields is { } f && (f.ProcedureId is { } procedure && !await db.AuditProcedures.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == s.ClientId && x.EngagementId == engagementId && x.Id == procedure, ct) ||
      f.PriorPeriodId is { } period && !await db.ClientReportingPeriods.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == s.ClientId && x.Id == period, ct)))
      return Fail<OpeningBalanceCommandPreview>(ErrorCodes.ScopeDenied, "Linked procedure or prior period is outside this verification's scope.");
    var can = action == "PREPARE" ? s.CanPrepare : s.CanReview;
    string[] blockers = can ? [] : s.Blockers.Count > 0 ? s.Blockers.ToArray() : [action == "REVIEW" ? "A different authorized reviewer and eligible unreviewed preparation are required." : "Current preparation authority is required; only the recording auditor can revise this record."];
    return CommandResult<OpeningBalanceCommandPreview>.Ok(new(r.RequestId, action, RequestHash(actor, engagementId, action, r), r.ReviewBasis, r.ExpectedRevision,
      can, blockers, s.Record, action == "PREPARE" ? "Record this comparison; this does not independently approve it." : "Independently review this exact current opening-balance preparation."));
  }

  public static Task<CommandResult<OpeningBalanceCommandReceiptView>> ExecutePrepareAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, OpeningBalanceCommandRequest request, CancellationToken ct = default) => ExecuteAsync(db, actor, engagementId, "PREPARE", request, ct);
  public static Task<CommandResult<OpeningBalanceCommandReceiptView>> ExecuteReviewAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid verificationId, OpeningBalanceCommandRequest request, CancellationToken ct = default) =>
    request.ExpectedVerificationId != verificationId ? Task.FromResult(Fail<OpeningBalanceCommandReceiptView>(ErrorCodes.ScopeDenied, "The exact review target is required.")) : ExecuteAsync(db, actor, engagementId, "REVIEW", request, ct);
  private static async Task<CommandResult<OpeningBalanceCommandReceiptView>> ExecuteAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, string action, OpeningBalanceCommandRequest request, CancellationToken ct)
  {
    // Reject unauthorized scope before interpreting actor-bound intent hashes.
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<OpeningBalanceCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
    var authorized = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!authorized.Succeeded) return Fail<OpeningBalanceCommandReceiptView>(authorized.ErrorCode!, authorized.Message!);
    var r = Normalize(request, action);
    if (r is null || !r.Reviewed || !Hash(r.ExpectedRequestHash) || RequestHash(actor, engagementId, action, r) != r.ExpectedRequestHash)
      return Fail<OpeningBalanceCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Preview and confirm this exact command; changed intent cannot reuse its reference.");
    // Preview is re-evaluated under the owner's locks. A retained request is resolved there first.
    var placeholder = new OpeningBalanceCommandPreview(r.RequestId, action, r.ExpectedRequestHash!, r.ReviewBasis, r.ExpectedRevision, false, [], null, "");
    var context = new OpeningBalanceReceiptContext(action, r, placeholder, Input(r));
    CommandResult result;
    if (action == "PREPARE") {
      var f = r.Fields!;
      var recorded = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationWithReceiptAsync(db, actor,
        new(engagementId, f.ProcedureId, f.PriorPeriodId, f.PriorReference, f.AsOfDate, f.Currency,
          decimal.Parse(f.OpeningSignedTotal, CultureInfo.InvariantCulture), decimal.Parse(f.AgreedSignedTotal, CultureInfo.InvariantCulture),
          f.AccountingPoliciesConsistent!.Value, f.Rationale, f.EvidenceReferences, long.Parse(r.ExpectedRevision, CultureInfo.InvariantCulture), r.ExpectedVerificationId), context, ct);
      result = recorded.Succeeded ? CommandResult.Ok() : CommandResult.Fail(recorded.ErrorCode!, recorded.Message!);
    } else result = await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationWithReceiptAsync(db, actor,
      new(engagementId, r.ExpectedVerificationId!.Value, long.Parse(r.ExpectedRevision, CultureInfo.InvariantCulture), r.Comment), context, ct);
    if (!result.Succeeded) return Fail<OpeningBalanceCommandReceiptView>(result.ErrorCode!, result.Message!);
    var lookup = await LookupAsync(db, actor, engagementId, r.RequestId, r.ExpectedRequestHash, ct);
    return lookup.Succeeded && lookup.Value!.Receipt is { } receipt ? CommandResult<OpeningBalanceCommandReceiptView>.Ok(receipt) :
      Fail<OpeningBalanceCommandReceiptView>(lookup.ErrorCode ?? ErrorCodes.IdempotencyConflict, lookup.Message ?? "Outcome not confirmed. Recover the original request before any replacement.");
  }

  internal static async Task LockRequestAsync(IClientAccountingDbContext db, ActorContext actor, Guid requestId, bool write, CancellationToken ct)
  {
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "opening-balance-command-v1", actor.FirmId, actor.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (write) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }
  internal static async Task<CommandResult<OpeningBalanceCommandLookup>> ResolveAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, OpeningBalanceReceiptContext context, CancellationToken ct)
  {
    await LockRequestAsync(db, actor, context.Request.RequestId, true, ct);
    var row = await db.OpeningBalanceCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == context.Request.RequestId, ct);
    if (row is not null) {
      if (row.ActorEpoch != actor.SessionEpoch) return Fail<OpeningBalanceCommandLookup>(ErrorCodes.GenerationStale, "The request belongs to an earlier session lifetime.");
      if (row.EngagementId != engagementId || row.RequestHash != context.Request.ExpectedRequestHash || row.Action != context.Action || row.InputJson != context.InputJson)
        return Fail<OpeningBalanceCommandLookup>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse the retained request.");
      return CommandResult<OpeningBalanceCommandLookup>.Ok(new(true, View(row)));
    }
    var preview = await PreviewAsync(db, actor, engagementId, context.Action, context.Request, ct);
    if (!preview.Succeeded) return Fail<OpeningBalanceCommandLookup>(preview.ErrorCode!, preview.Message!);
    if (preview.Value!.RequestHash != context.Request.ExpectedRequestHash) return Fail<OpeningBalanceCommandLookup>(ErrorCodes.IdempotencyConflict, "The submitted command differs from its preview.");
    if (!preview.Value.CanProceed) return Fail<OpeningBalanceCommandLookup>(ErrorCodes.GateBlocked, preview.Value.Blockers.First());
    context.Preview = preview.Value;
    return CommandResult<OpeningBalanceCommandLookup>.Ok(new(false, null));
  }
  internal static async Task RetainAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid verificationId, OpeningBalanceReceiptContext context, CancellationToken ct)
  {
    var row = await db.OpeningBalanceVerifications.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Id == verificationId, ct);
    // Result is retained at commit, not reconstructed from a future mutable preparation.
    var preview = context.Preview;
    db.OpeningBalanceCommandReceipts.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = row.ClientId,
      EngagementId = engagementId, VerificationId = verificationId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
      RequestId = context.Request.RequestId, RequestHash = context.Request.ExpectedRequestHash!, ReviewBasis = context.Request.ReviewBasis,
      Action = context.Action, InputJson = context.InputJson, PreviewJson = JsonSerializer.Serialize(preview, Json),
      ResultJson = JsonSerializer.Serialize(Snapshot(row), Json), CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(ct);
  }
  private static OpeningBalanceCommandReceiptView View(OpeningBalanceCommandReceipt r) => new(r.Id, r.EngagementId, r.VerificationId, r.ActorId, Long(r.ActorEpoch), r.RequestId,
    r.RequestHash, r.Action, r.CreatedAt, JsonSerializer.Deserialize<OpeningBalanceCommandPreview>(r.PreviewJson, Json)!, JsonSerializer.Deserialize<OpeningBalanceSnapshot>(r.ResultJson, Json)!);
  public static async Task<CommandResult<OpeningBalanceCommandLookup>> LookupAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<OpeningBalanceCommandLookup>(ErrorCodes.Accounting.MappingInvalid, "Use the retained exact request reference.");
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (e is null) return Fail<OpeningBalanceCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, e.PracticeClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<OpeningBalanceCommandLookup>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockRequestAsync(db, actor, requestId, false, ct);
    var row = await db.OpeningBalanceCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is not null && row.ActorEpoch != actor.SessionEpoch) return Fail<OpeningBalanceCommandLookup>(ErrorCodes.GenerationStale, "The request belongs to an earlier session lifetime.");
    if (row is not null && (row.EngagementId != engagementId || row.ClientId != e.PracticeClientId || row.RequestHash != requestHash))
      return Fail<OpeningBalanceCommandLookup>(ErrorCodes.IdempotencyConflict, "The retained reference belongs to another intent.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, e.PracticeClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<OpeningBalanceCommandLookup>(auth.ErrorCode!, auth.Message!);
    await tx.CommitAsync(ct);
    return CommandResult<OpeningBalanceCommandLookup>.Ok(new(row is not null, row is null ? null : View(row)));
  }
}
