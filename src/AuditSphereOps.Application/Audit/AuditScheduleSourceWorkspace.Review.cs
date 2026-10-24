using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditScheduleSourceWorkspace
{
  private sealed record ReviewIntent(string Version, Guid FirmId, Guid ClientId, Guid EngagementId,
    Guid ActorUserId, long ActorEpoch, Guid RequestId, Guid ScheduleId, string ExpectedCaptureHash,
    long ExpectedInputGeneration, string ExpectedReviewBasis, AuditScheduleSourceReviewFields Fields);
  private sealed record ReviewFacts(AuditScheduleSourceCapture Capture, AuditSchedule Schedule,
    long Generation, string Basis, bool SourceCurrent, bool CanApprove, string? SourceFailureCode, Guid? ReviewId);

  public static async Task<CommandResult<AuditScheduleSourceReviewPreview>> PreviewReviewAsync(
    IClientAccountingDbContext db, ActorContext actor, IAuditScheduleSourceContentReader reader,
    Guid engagementId, Guid scheduleId, Guid requestId, AuditScheduleSourceReviewFields fields,
    CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || scheduleId == Guid.Empty || !ValidReview(fields))
      return Invalid<AuditScheduleSourceReviewPreview>();
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<AuditScheduleSourceReviewPreview>.Fail(ErrorCodes.ProtectedState,
        "Source review preview requires its own transaction.");
    var auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReviewPreview>(auth);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await AuditFieldworkService.LockConfirmationScopeAsync(db, actor, engagementId, ct);
    auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReviewPreview>(auth);
    var current = await LoadReviewFactsAsync(db, actor, reader, auth.Value, engagementId, scheduleId, ct);
    if (!current.Succeeded) return Failure<ReviewFacts, AuditScheduleSourceReviewPreview>(current);
    var facts = current.Value!;
    var canonical = CanonicalReview(fields);
    var json = ReviewJson(actor, auth.Value, engagementId, requestId, scheduleId,
      facts.Capture.ManifestHash, facts.Generation, facts.Basis, canonical);
    if (!Bounded(json)) return Invalid<AuditScheduleSourceReviewPreview>();
    auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReviewPreview>(auth);
    await tx.CommitAsync(ct);
    return CommandResult<AuditScheduleSourceReviewPreview>.Ok(new(requestId, scheduleId,
      facts.Capture.ManifestHash, Exact(facts.Generation), facts.Basis, Hashing.Sha256Hex(json), canonical,
      facts.SourceCurrent, facts.CanApprove, facts.SourceFailureCode, facts.Schedule.Status, facts.ReviewId));
  }

  public static async Task<CommandResult<AuditScheduleSourceReceiptView>> ReviewAsync(
    IClientAccountingDbContext db, ActorContext actor, IAuditScheduleSourceContentReader reader,
    Guid engagementId, AuditScheduleSourceReviewRequest request, CancellationToken ct = default)
  {
    if (request is null || request.RequestId == Guid.Empty || request.ScheduleId == Guid.Empty ||
        !ValidReview(request.Fields) || !Hash(request.ExpectedCaptureHash) || !Hash(request.ExpectedReviewBasis) ||
        !Hash(request.ExpectedRequestHash) || request.ExpectedInputGeneration < 1 || !request.Reviewed)
      return Invalid<AuditScheduleSourceReceiptView>();
    if (db is not DbContext context || context.ChangeTracker.HasChanges() ||
        context.ChangeTracker.Entries<AuditSchedule>().Any() || db.Database.CurrentTransaction is not null)
      return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.ProtectedState,
        "Review requires a fresh command context and its own transaction.");
    var auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReceiptView>(auth);
    var clientId = auth.Value;
    var fields = CanonicalReview(request.Fields);
    var json = ReviewJson(actor, clientId, engagementId, request.RequestId, request.ScheduleId,
      request.ExpectedCaptureHash, request.ExpectedInputGeneration, request.ExpectedReviewBasis, fields);
    if (!Bounded(json) || Hashing.Sha256Hex(json) != request.ExpectedRequestHash)
      return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.ManifestMismatch,
        "Review the exact independent decision preview before submitting it.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var completed = false;
    try
    {
      await AuditFieldworkService.LockConfirmationScopeAsync(db, actor, engagementId, ct);
      auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
      if (!auth.Succeeded || auth.Value != clientId)
        return CommandResult<AuditScheduleSourceReceiptView>.Fail(auth.ErrorCode ?? ErrorCodes.ScopeDenied,
          auth.Message ?? "Access denied.");
      // The original request outcome wins over later review/source state.
      var existing = await db.AuditScheduleSourceReviews.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.ActorEpoch == actor.SessionEpoch &&
        x.RequestId == request.RequestId, ct);
      if (existing is not null)
      {
        if (existing.ClientId != clientId || existing.EngagementId != engagementId ||
            existing.ScheduleId != request.ScheduleId || existing.RequestHash != request.ExpectedRequestHash ||
            existing.RequestJson != json) return Conflict<AuditScheduleSourceReceiptView>();
        var retained = ReadReviewReceipt(existing);
        if (!retained.Succeeded) return retained;
        auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
        if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReceiptView>(auth);
        await tx.CommitAsync(ct);
        completed = true;
        return retained;
      }
      var current = await LoadReviewFactsAsync(db, actor, reader, clientId, engagementId, request.ScheduleId, ct);
      if (!current.Succeeded) return Failure<ReviewFacts, AuditScheduleSourceReceiptView>(current);
      var facts = current.Value!;
      if (facts.ReviewId is not null)
        return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.ProtectedState,
          "This immutable capture already has an independent decision. Prepare a new capture for rework.");
      if (facts.Capture.ManifestHash != request.ExpectedCaptureHash || facts.Generation != request.ExpectedInputGeneration ||
          facts.Basis != request.ExpectedReviewBasis) return Stale<AuditScheduleSourceReceiptView>();
      if (fields.Action == "APPROVE" && !facts.CanApprove)
        return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.GateBlocked,
          "Approval requires the current exact source and complete per-account reconciliation.");
      var decision = await AuditFieldworkService.ReviewScheduleInTransactionAsync(db, actor,
        new(request.ScheduleId, fields.Reason, fields.Action == "APPROVE"), ct);
      if (!decision.Succeeded) return Failure<ScheduleValue, AuditScheduleSourceReceiptView>(decision);
      var schedule = db.AuditSchedules.Local.Single(x => x.Id == request.ScheduleId && x.FirmId == actor.FirmId);
      schedule.Status = fields.Action == "APPROVE" ? AuditScheduleStatuses.Approved : AuditScheduleStatuses.Unreconciled;
      var ticks = DateTimeOffset.UtcNow.UtcTicks;
      var now = new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
      var reviewId = Guid.CreateVersion7();
      var receipt = new AuditScheduleSourceReceipt(ReceiptVersion, fields.Action, schedule.Id, reviewId,
        actor.FirmId, clientId, engagementId, actor.UserId, Exact(actor.SessionEpoch), request.RequestId,
        request.ExpectedRequestHash, facts.Capture.ManifestHash, Exact(facts.Generation), Exact(schedule.RowCount),
        Money(schedule.SignedControlTotal), Money(schedule.GlControlTotal), Money(schedule.Residual), schedule.Status, now);
      var receiptJson = JsonSerializer.Serialize(receipt);
      if (!Bounded(receiptJson)) return Invalid<AuditScheduleSourceReceiptView>();
      var review = new AuditScheduleSourceReview
      {
        Id = reviewId, FirmId = actor.FirmId, ClientId = clientId, EngagementId = engagementId,
        ScheduleId = schedule.Id, RequestId = request.RequestId, ActorUserId = actor.UserId,
        ActorEpoch = actor.SessionEpoch, InputGeneration = facts.Generation, Action = fields.Action,
        Reason = fields.Reason, EvidenceReference = fields.EvidenceReference, CaptureHash = facts.Capture.ManifestHash,
        RequestJson = json, RequestHash = request.ExpectedRequestHash, ReceiptJson = receiptJson,
        ReceiptHash = Hashing.Sha256Hex(receiptJson), CreatedAt = now
      };
      db.AuditScheduleSourceReviews.Add(review);
      await db.SaveChangesAsync(ct);
      if (fields.Action == "APPROVE")
      {
        var final = await LoadReviewFactsAsync(db, actor, reader, clientId, engagementId, schedule.Id, ct);
        if (!final.Succeeded) return Failure<ReviewFacts, AuditScheduleSourceReceiptView>(final);
        if (!final.Value!.SourceCurrent) return Stale<AuditScheduleSourceReceiptView>();
      }
      auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
      if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReceiptView>(auth);
      await tx.CommitAsync(ct);
      completed = true;
      return CommandResult<AuditScheduleSourceReceiptView>.Ok(new(receipt, review.ReceiptHash));
    }
    finally
    {
      if (!completed)
        foreach (var entry in context.ChangeTracker.Entries().Where(x => x.Entity switch
        {
          AuditSchedule schedule => schedule.Id == request.ScheduleId && schedule.FirmId == actor.FirmId,
          AuditScheduleSourceReview review => review.RequestId == request.RequestId && review.ActorUserId == actor.UserId &&
            review.ActorEpoch == actor.SessionEpoch && review.FirmId == actor.FirmId,
          _ => false
        }).ToArray()) entry.State = EntityState.Detached;
    }
  }

  private static async Task<CommandResult<ReviewFacts>> LoadReviewFactsAsync(IClientAccountingDbContext db,
    ActorContext actor, IAuditScheduleSourceContentReader reader, Guid clientId, Guid engagementId,
    Guid scheduleId, CancellationToken ct)
  {
    var capture = await db.AuditScheduleSourceCaptures.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && x.ScheduleId == scheduleId, ct);
    if (capture is null) return CommandResult<ReviewFacts>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (capture.ActorUserId == actor.UserId)
      return CommandResult<ReviewFacts>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same capture.");
    var original = ReadCaptureReceipt(capture);
    if (!original.Succeeded) return Failure<AuditScheduleSourceReceiptView, ReviewFacts>(original);
    CaptureIntent? intent;
    try { intent = JsonSerializer.Deserialize<CaptureIntent>(capture.RequestJson); }
    catch (JsonException) { return Invalid<ReviewFacts>(); }
    if (intent is null || !ValidFields(intent.Fields) || Hashing.Sha256Hex(capture.RequestJson) != capture.RequestHash)
      return CommandResult<ReviewFacts>.Fail(ErrorCodes.ProtectedState, "The retained source request is inconsistent.");
    var schedule = await db.AuditSchedules.AsNoTracking().SingleAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Id == scheduleId, ct);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if (generation is null or < 1) return CommandResult<ReviewFacts>.Fail(ErrorCodes.GateBlocked, "Client generation is unavailable.");
    var reviewId = await db.AuditScheduleSourceReviews.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ScheduleId == scheduleId)
      .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
    CommandResult locked;
    CommandResult<AuditScheduleIntakeSourceFacts>? source = null;
    try
    {
      locked = await LockSourceAsync(db, actor, clientId, engagementId, intent.Fields, ct);
      if (locked.Succeeded)
        source = await ResolveAsync(db, actor, reader, clientId, engagementId, intent.Fields, ct);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      // A read/open race must block approval, but may not prevent an independent refusal.
      // Cancellation and authorization failures retain their normal semantics.
      ct.ThrowIfCancellationRequested();
      locked = CommandResult.Fail(ErrorCodes.GateBlocked, "The retained source bytes are currently unavailable.");
    }
    var current = source?.Succeeded == true && source.Value!.ManifestSha256 == capture.ManifestHash &&
      source.Value.InputGeneration == capture.InputGeneration && source.Value.PeriodRevision == capture.PeriodRevision &&
      generation.Value == capture.InputGeneration;
    var failure = locked.Succeeded ? source?.ErrorCode : locked.ErrorCode;
    var canApprove = current && source!.Value!.AllAccountsReconciled && reviewId is null;
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      Version = "audit-schedule-review-basis.v1", capture.ManifestHash, InputGeneration = generation.Value,
      schedule.Status, schedule.CompletenessDecision, ReviewId = reviewId, SourceCurrent = current,
      SourceManifestHash = source?.Succeeded == true ? source.Value!.ManifestSha256 : null,
      SourceFailureCode = failure
    }));
    return CommandResult<ReviewFacts>.Ok(new(capture, schedule, generation.Value, basis, current, canApprove, failure, reviewId));
  }

  private static string ReviewJson(ActorContext actor, Guid clientId, Guid engagementId, Guid requestId,
    Guid scheduleId, string captureHash, long generation, string basis, AuditScheduleSourceReviewFields fields) =>
    JsonSerializer.Serialize(new ReviewIntent(CommandVersion, actor.FirmId, clientId, engagementId, actor.UserId,
      actor.SessionEpoch, requestId, scheduleId, captureHash, generation, basis, fields));
  private static bool ValidReview(AuditScheduleSourceReviewFields fields) => fields is not null &&
    fields.Action is "APPROVE" or "RETURN" && !string.IsNullOrWhiteSpace(fields.Reason) && fields.Reason.Length <= 2000 &&
    fields.EvidenceReference is { Length: <= 2000 };
  private static AuditScheduleSourceReviewFields CanonicalReview(AuditScheduleSourceReviewFields fields) =>
    fields with { Reason = fields.Reason.Trim(), EvidenceReference = fields.EvidenceReference.Trim() };
  private static CommandResult<AuditScheduleSourceReceiptView> ReadReviewReceipt(AuditScheduleSourceReview review)
  {
    try
    {
      var value = JsonSerializer.Deserialize<AuditScheduleSourceReceipt>(review.ReceiptJson);
      if (value is null || Hashing.Sha256Hex(review.ReceiptJson) != review.ReceiptHash ||
          value.Version != ReceiptVersion || value.Action != review.Action || value.ReviewId != review.Id ||
          value.ScheduleId != review.ScheduleId || value.FirmId != review.FirmId || value.ClientId != review.ClientId ||
          value.EngagementId != review.EngagementId || value.ActorUserId != review.ActorUserId ||
          value.ActorEpoch != Exact(review.ActorEpoch) || value.RequestId != review.RequestId ||
          value.RequestHash != review.RequestHash || value.ManifestHash != review.CaptureHash || value.CreatedAt != review.CreatedAt)
        return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.ProtectedState, "The retained review receipt is inconsistent.");
      return CommandResult<AuditScheduleSourceReceiptView>.Ok(new(value, review.ReceiptHash));
    }
    catch (JsonException)
    {
      return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.ProtectedState, "The retained review receipt is unreadable.");
    }
  }
}
