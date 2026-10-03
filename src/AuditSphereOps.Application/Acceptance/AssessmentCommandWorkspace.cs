using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record AssessmentCommandFields(string Kind, string Generation, string? Revision = null,
  string? QuestionCode = null, string? Answer = null, string? Evidence = null, string? Area = null,
  string? Specialist = null, Guid? ReviewId = null, string? Status = null, string? ExpectedStatus = null,
  string? Conditions = null, string? ServiceRoute = null, string? Decision = null, string? Rationale = null);
public sealed record AssessmentCommandRequest(Guid RequestId, AssessmentCommandFields Fields,
  string? ReviewBasis = null, string? RequestHash = null, bool Reviewed = false);
public sealed record AssessmentCommandBefore(string Generation, string Path, string? Decision, bool Ready,
  string? Answer, string? Evidence, string? Revision, string? ReviewStatus, string? ReviewConditions);
public sealed record AssessmentCommandPreview(Guid ClientId, Guid RequestId, string RequestHash, string ReviewBasis,
  AssessmentCommandFields Fields, AssessmentCommandBefore Before, string Effect);
public sealed record AssessmentReceiptView(Guid Id, Guid ClientId, Guid ActorId, Guid RequestId, string RequestHash,
  string ReviewBasis, string Kind, string Generation, string ResultGeneration, Guid ResourceId,
  AssessmentCommandPreview Preview, DateTimeOffset CreatedAt);
public sealed record AssessmentReceiptLookup(bool Found, AssessmentReceiptView? Receipt);

/// <summary>One reviewed local command and its immutable receipt publish together. Unknown replies use lookup, not POST retry.</summary>
public static class AssessmentCommandWorkspace
{
  private static readonly string[] ProfessionalRoles = ["Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"];
  private static string[] Roles(string kind) => kind switch {
    "DECISION" => ["Partner"], "RECORD_REVIEW" or "CONTINUANCE" => ["Partner", "Manager"], _ => ProfessionalRoles };
  private static AuthorizationRequest Authority(ActorContext a, Guid id, string kind) =>
    new(a.FirmId, ClientId: id, RequiredRoles: Roles(kind), InternalOnly: true);
  private static Task<CommandResult> Authorize(IAuditSphereDbContext db, ActorContext a, Guid id, string kind, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, a, Authority(a, id, kind), ct);
  private static CommandResult<T> Unavailable<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Assessment command unavailable.");
  private static CommandResult<T> Invalid<T>() => CommandResult<T>.Fail("request.invalid", "Review the bounded fields for this assessment action.");
  private static string Exact(long n) => n.ToString(CultureInfo.InvariantCulture);
  private static bool Counter(string? s, long min, out long value) =>
    long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && Exact(value) == s;
  private static bool HashValid(string? s) => s is { Length:64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
  private static bool Text(string? s, int max) => s is null || s.Length <= max && !s.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
  private static AssessmentCommandFields? Canonical(AssessmentCommandFields? f)
  {
    if (f is null || !Counter(f.Generation, 1, out _) ||
      !Text(f.Answer, 2000) || !Text(f.Evidence, 500) || !Text(f.Area, 100) || !Text(f.Specialist, 200) ||
      !Text(f.Conditions, 2000) || !Text(f.ServiceRoute, 100) || !Text(f.Rationale, 2000) || !Text(f.QuestionCode, 50)) return null;
    var n = f with { Kind = f.Kind?.Trim().ToUpperInvariant() ?? "", QuestionCode = Trim(f.QuestionCode)?.ToUpperInvariant(),
      Answer = Trim(f.Answer), Evidence = Trim(f.Evidence), Area = Trim(f.Area), Specialist = Trim(f.Specialist),
      Status = Trim(f.Status)?.ToUpperInvariant(), ExpectedStatus = Trim(f.ExpectedStatus)?.ToUpperInvariant(),
      Conditions = Trim(f.Conditions), ServiceRoute = Trim(f.ServiceRoute), Decision = Trim(f.Decision), Rationale = Trim(f.Rationale) };
    // Reject irrelevant fields rather than silently accepting a different action than the caller reviewed.
    AssessmentCommandFields? shaped = n.Kind switch {
      "ANSWER" when n.QuestionCode is not null && n.Answer is not null && Counter(n.Revision, 0, out _) =>
        new(n.Kind, n.Generation, n.Revision, n.QuestionCode, n.Answer, n.Evidence),
      "REQUEST_REVIEW" when n.Area is {Length: >=2} && n.Specialist is {Length: >=2} =>
        new(n.Kind, n.Generation, Area:n.Area, Specialist:n.Specialist),
      "RECORD_REVIEW" when n.ReviewId is Guid review && review != Guid.Empty &&
        n.Status is "CLEARED" or "HOLD" or "CONDITIONS" && n.ExpectedStatus is "PENDING" or "HOLD" or "CONDITIONS" &&
        (n.Status != "CLEARED" || n.Evidence is not null) && (n.Status != "CONDITIONS" || n.Conditions is not null) =>
        new(n.Kind, n.Generation, Evidence:n.Evidence, ReviewId:n.ReviewId, Status:n.Status, ExpectedStatus:n.ExpectedStatus, Conditions:n.Conditions),
      "DECISION" when n.ServiceRoute is not null && n.Rationale is not null &&
        n.Decision is "Accepted" or "AcceptedWithConditions" or "Declined" or "Deferred" &&
        (n.Decision != "AcceptedWithConditions" || n.Conditions is not null) && (n.Decision != "Accepted" || n.Conditions is null) =>
        new(n.Kind, n.Generation, Conditions:n.Conditions, ServiceRoute:n.ServiceRoute, Decision:n.Decision, Rationale:n.Rationale),
      "CONTINUANCE" => new(n.Kind, n.Generation), _ => null };
    return shaped == n ? shaped : null;
  }
  private static string RequestHash(ActorContext a, Guid id, Guid requestId, AssessmentCommandFields f) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, id, requestId, Fields = f }));
  private static AssessmentReceiptView Receipt(AssessmentCommandReceipt r) =>
    new(r.Id, r.ClientId, r.ActorId, r.RequestId, r.RequestHash, r.ReviewBasis, r.Kind, Exact(r.Generation),
      Exact(r.ResultGeneration), r.ResourceId, JsonSerializer.Deserialize<AssessmentCommandPreview>(r.PreviewJson)!, r.CreatedAt);

  // Caller holds firm -> client -> actor locks and must roll back its whole scope on any failure.
  private static async Task<bool> LockAsync(IAuditSphereDbContext db, ActorContext a, Guid id, string kind, CancellationToken ct)
  {
    if (!await AcceptanceCommandAuthority.LockFirmAsync(db, a, ct)) return false;
    if (await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null) return false;
    // Serialize this actor's request namespace even when two requests name different client rows.
    if (await db.Users.FromSqlInterpolated(
      $"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null) return false;
    return (await AcceptanceCommandAuthority.CheckAsync(db, a, Authority(a, id, kind), ct)).Succeeded;
  }

  private static async Task<CommandResult<AssessmentCommandPreview>> BuildAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, Guid requestId, AssessmentCommandFields f, CancellationToken ct)
  {
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.Id == id).Select(x => x.InputGeneration).SingleAsync(ct);
    if (Exact(generation) != f.Generation) return CommandResult<AssessmentCommandPreview>.Fail(ErrorCodes.GenerationStale, "Reload the current evaluation before review.");
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (client is null) return Unavailable<AssessmentCommandPreview>();
    var checklist = await AcceptanceChecklistService.LoadAsync(db, a.FirmId, id, null, generation, ct);
    if (f.Kind != "CONTINUANCE" && checklist.CurrentDecision is not null)
      return CommandResult<AssessmentCommandPreview>.Fail(ErrorCodes.ProtectedState, "This evaluation has a professional decision and is immutable.");
    var question = f.Kind == "ANSWER" ? checklist.Items.SingleOrDefault(x => x.Question.Code.Equals(f.QuestionCode, StringComparison.OrdinalIgnoreCase)) : null;
    SpecialistClearance? review = null;
    if (f.Kind == "ANSWER") {
      if (question is null) return Unavailable<AssessmentCommandPreview>();
      if (Exact(question.Answer?.Revision ?? 0) != f.Revision)
        return CommandResult<AssessmentCommandPreview>.Fail(ErrorCodes.StaleRevision, "The answer changed. Reload before review.");
      if (question.Question.RequiresEvidence && f.Evidence is null ||
        question.Question.AnswerType == "BOOLEAN" && AcceptanceRules.NormalizeBoolean(f.Answer!) is null)
        return Invalid<AssessmentCommandPreview>();
    }
    if (f.Kind == "RECORD_REVIEW") {
      review = await db.SpecialistClearances.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.PracticeClientId == id &&
        x.EngagementId == null && x.Id == f.ReviewId, ct);
      if (review is null) return Unavailable<AssessmentCommandPreview>();
      if (review.Status != f.ExpectedStatus) return CommandResult<AssessmentCommandPreview>.Fail(ErrorCodes.StaleRevision, "The specialist review changed. Reload before review.");
    }
    if (f.Kind == "REQUEST_REVIEW")
      review = await db.SpecialistClearances.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.PracticeClientId == id &&
        x.EngagementId == null && x.Area == f.Area && x.Status != "CLEARED").OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
    if (f.Kind == "DECISION" && f.Decision is "Accepted" or "AcceptedWithConditions" && !checklist.Ready)
      return CommandResult<AssessmentCommandPreview>.Fail(ErrorCodes.GateBlocked, "Resolve the checklist blockers before acceptance.");
    if (f.Kind == "CONTINUANCE" && (generation == long.MaxValue || !await db.AcceptanceDecisions.AsNoTracking().AnyAsync(x =>
        x.FirmId == a.FirmId && x.PracticeClientId == id && x.Generation == generation && (x.Decision == "Accepted" || x.Decision == "AcceptedWithConditions"), ct)))
      return CommandResult<AssessmentCommandPreview>.Fail(ErrorCodes.GateBlocked, "A current accepted decision is required to begin another evaluation.");
    // Include every effective answer, question definition and clearance: same status with changed evidence is still a new basis.
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, id, Fields=f,
      client.LegalName, client.Status, checklist.Generation, checklist.Path, checklist.CurrentDecision, checklist.PriorDecision,
      Items=checklist.Items.OrderBy(x => x.Question.Code, StringComparer.Ordinal),
      Clearances=checklist.Clearances.OrderBy(x => x.Id), TargetReview=review, checklist.Blockers }));
    var before = new AssessmentCommandBefore(Exact(generation), checklist.Path, checklist.CurrentDecision?.Decision, checklist.Ready,
      question?.Answer?.Answer, question?.Answer?.EvidenceReference, question is null ? null : Exact(question.Answer?.Revision ?? 0), review?.Status, review?.Conditions);
    var effect = f.Kind switch {
      "ANSWER" => "Appends an answer revision to this client evaluation. It makes no professional acceptance decision.",
      "REQUEST_REVIEW" => "Requests specialist review or returns the existing open review. It grants no clearance or acceptance.",
      "RECORD_REVIEW" => "Records this specialist result and evidence. A cleared review is final; it does not accept the client.",
      "DECISION" => "Records the Partner's exact professional decision. Unconditional acceptance may record repository intent; no engagement is activated.",
      _ => "Starts the next evaluation generation and pending decision. Prior decisions remain evidence, not current approval." };
    return CommandResult<AssessmentCommandPreview>.Ok(new(id, requestId, RequestHash(a, id, requestId, f), basis, f, before, effect));
  }

  public static async Task<CommandResult<AssessmentCommandPreview>> PreviewAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, AssessmentCommandRequest? r, CancellationToken ct = default)
  {
    var f = Canonical(r?.Fields);
    if (r is null || r.RequestId == Guid.Empty || f is null) return Invalid<AssessmentCommandPreview>();
    if (!(await Authorize(db, a, id, f.Kind, ct)).Succeeded) return Unavailable<AssessmentCommandPreview>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await LockAsync(db, a, id, f.Kind, ct)) return Unavailable<AssessmentCommandPreview>();
    var result = await BuildAsync(db, a, id, r.RequestId, f, ct);
    if (!(await Authorize(db, a, id, f.Kind, ct)).Succeeded) return Unavailable<AssessmentCommandPreview>();
    await tx.CommitAsync(ct);
    return result;
  }

  public static async Task<CommandResult<AssessmentReceiptView>> ExecuteAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, AssessmentCommandRequest? r, CancellationToken ct = default)
  {
    var f = Canonical(r?.Fields);
    if (r is null || r.RequestId == Guid.Empty || f is null || !r.Reviewed || !HashValid(r.RequestHash) || !HashValid(r.ReviewBasis) ||
      r.RequestHash != RequestHash(a, id, r.RequestId, f)) return Invalid<AssessmentReceiptView>();
    if (!(await Authorize(db, a, id, f.Kind, ct)).Succeeded) return Unavailable<AssessmentReceiptView>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await LockAsync(db, a, id, f.Kind, ct)) return Unavailable<AssessmentReceiptView>();
    var prior = await db.AssessmentCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == r.RequestId, ct);
    if (prior is not null) {
      if (prior.ClientId != id || prior.RequestHash != r.RequestHash || prior.ReviewBasis != r.ReviewBasis)
        return CommandResult<AssessmentReceiptView>.Fail(ErrorCodes.IdempotencyConflict, "This request belongs to another reviewed assessment action.");
      await tx.CommitAsync(ct);
      return CommandResult<AssessmentReceiptView>.Ok(Receipt(prior));
    }
    var preview = await BuildAsync(db, a, id, r.RequestId, f, ct);
    if (!preview.Succeeded) return CommandResult<AssessmentReceiptView>.Fail(preview.ErrorCode!, preview.Message!);
    if (preview.Value!.ReviewBasis != r.ReviewBasis)
      return CommandResult<AssessmentReceiptView>.Fail(ErrorCodes.StaleRevision, "Assessment context changed. Refresh and review again.");
    Counter(f.Generation, 1, out var generation);
    var result = await DispatchAsync(db, a, id, f, generation, ct);
    if (!result.Succeeded) return CommandResult<AssessmentReceiptView>.Fail(result.ErrorCode!, result.Message!);
    var receipt = new AssessmentCommandReceipt { Id=Guid.CreateVersion7(), FirmId=a.FirmId, ClientId=id, ActorId=a.UserId,
      ActorEpoch=a.SessionEpoch, RequestId=r.RequestId, Kind=f.Kind, RequestHash=r.RequestHash!, ReviewBasis=r.ReviewBasis!,
      PreviewJson=JsonSerializer.Serialize(preview.Value), Generation=generation, ResultGeneration=result.Value!.Generation,
      ResourceId=result.Value.Id, CreatedAt=DateTimeOffset.UtcNow };
    db.AssessmentCommandReceipts.Add(receipt);
    await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, a, id, f.Kind, ct)).Succeeded) return Unavailable<AssessmentReceiptView>();
    await tx.CommitAsync(ct);
    return CommandResult<AssessmentReceiptView>.Ok(Receipt(receipt));
  }

  private sealed record Result(Guid Id, long Generation);
  private static async Task<CommandResult<Result>> DispatchAsync(IAuditSphereDbContext db, ActorContext a, Guid id,
    AssessmentCommandFields f, long generation, CancellationToken ct)
  {
    switch (f.Kind) {
      case "ANSWER": {
        Counter(f.Revision, 0, out var revision);
        var r = await AcceptanceChecklistService.RecordAnswerAsync(db, a, id, f.QuestionCode!, f.Answer!, f.Evidence, ct, generation, revision);
        if (!r.Succeeded) return CommandResult<Result>.Fail(r.ErrorCode!, r.Message!);
        var bank = AcceptanceRules.BankFor((await AcceptanceChecklistService.LoadAsync(db, a.FirmId, id, null, generation, ct)).Path);
        var answer = await db.EvaluationResponses.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.PracticeClientId == id &&
          x.Bank == bank && x.QuestionId == f.QuestionCode && x.Generation == generation).OrderByDescending(x => x.Revision).FirstAsync(ct);
        return CommandResult<Result>.Ok(new(answer.Id, generation));
      }
      case "REQUEST_REVIEW": {
        var r = await AcceptanceChecklistService.RequestClearanceAsync(db, a, id, f.Area!, f.Specialist!, ct, generation);
        return r.Succeeded ? CommandResult<Result>.Ok(new(r.Value, generation)) : CommandResult<Result>.Fail(r.ErrorCode!, r.Message!);
      }
      case "RECORD_REVIEW": {
        var r = await AcceptanceWorkspaceQuery.RecordReviewAsync(db, a, id, f.ReviewId!.Value, f.Status!, f.Evidence, f.Conditions, generation, f.ExpectedStatus!, ct);
        return r.Succeeded ? CommandResult<Result>.Ok(new(f.ReviewId.Value, generation)) : CommandResult<Result>.Fail(r.ErrorCode!, r.Message!);
      }
      case "DECISION": {
        var r = await AcceptanceDecisionService.RecordAsync(db, a, new(id, null, f.ServiceRoute!, f.Decision!, f.Rationale!, f.Conditions, generation), ct);
        return r.Succeeded ? CommandResult<Result>.Ok(new(r.Value, generation)) : CommandResult<Result>.Fail(r.ErrorCode!, r.Message!);
      }
      default: {
        var r = await AcceptanceChecklistService.StartContinuanceAsync(db, a, id, ct, generation);
        if (!r.Succeeded) return CommandResult<Result>.Fail(r.ErrorCode!, r.Message!);
        var pending = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.PracticeClientId == id && x.Generation == r.Value && x.Decision == "Pending")
          .Select(x => x.Id).SingleAsync(ct);
        return CommandResult<Result>.Ok(new(pending, r.Value));
      }
    }
  }

  public static async Task<CommandResult<AssessmentReceiptLookup>> LookupAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !HashValid(requestHash) || !(await Authorize(db, a, id, "ANSWER", ct)).Succeeded)
      return Unavailable<AssessmentReceiptLookup>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Serialize with execution before returning an absent receipt. No result is inferred from an HTTP timeout.
    if (!await LockAsync(db, a, id, "ANSWER", ct)) return Unavailable<AssessmentReceiptLookup>();
    var row = await db.AssessmentCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == requestId, ct);
    if (row is not null && (row.ClientId != id || row.RequestHash != requestHash))
      return CommandResult<AssessmentReceiptLookup>.Fail(ErrorCodes.IdempotencyConflict, "This reference belongs to another assessment action.");
    if (row is not null && !(await Authorize(db, a, id, row.Kind, ct)).Succeeded) return Unavailable<AssessmentReceiptLookup>();
    await tx.CommitAsync(ct);
    return CommandResult<AssessmentReceiptLookup>.Ok(new(row is not null, row is null ? null : Receipt(row)));
  }
}
