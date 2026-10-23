using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>Exact persisted receipt for an idempotent client reply. It carries no reply text.</summary>
public sealed record PbcReplyReceipt(Guid RequestId, Guid PbcRequestId, Guid CommunicationId, string BodySha256);

public static partial class PbcService
{
  /// <summary>Creates or replays one client reply identified by its client-generated operation key.</summary>
  public static async Task<CommandResult<PbcReplyReceipt>> ReplyAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid requestId, Guid clientRequestId, string body,
    CancellationToken ct = default)
  {
    body = body?.Trim() ?? string.Empty;
    if (requestId == Guid.Empty || clientRequestId == Guid.Empty || body.Length is 0 or > 4000)
      return CommandResult<PbcReplyReceipt>.Fail("pbc.message.invalid", "A reply and non-empty operation key are required.");

    var hash = HashReply(body);
    var current = await db.PbcRequests.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == requestId && x.FirmId == actor.FirmId, ct);
    if (current is null) return CommandResult<PbcReplyReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, current, ct);
    if (!auth.Succeeded) return CommandResult<PbcReplyReceipt>.Fail(auth.ErrorCode!, auth.Message!);

    // A receipt remains recoverable after the request closes, but the current participant authorization above
    // is always required before the operation key is inspected.
    var prior = await FindReplyOperationAsync(db, actor, clientRequestId, ct);
    if (prior is not null)
    {
      var refreshed = await ReloadAndAuthorizeCurrentParticipantAsync(db, actor, current, ct);
      if (!refreshed.Succeeded)
        return CommandResult<PbcReplyReceipt>.Fail(refreshed.ErrorCode!, refreshed.Message!);
      return ReplayOrConflict(prior, requestId, clientRequestId, hash);
    }
    if (current.State is PbcStates.Draft or PbcStates.Accepted or PbcStates.Closed)
      return CommandResult<PbcReplyReceipt>.Fail("pbc.message-state", "This request is not open for replies.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Keep the established firm → client → engagement → request lock order.
    _ = await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    _ = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {current.ClientId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleAsync(ct);
    _ = await db.Engagements.FromSqlInterpolated(
      $"SELECT * FROM engagements WHERE id = {current.EngagementId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleAsync(ct);
    var request = await db.PbcRequests.FromSqlInterpolated($"""
      SELECT * FROM pbc_requests WHERE firm_id = {actor.FirmId} AND id = {requestId} FOR UPDATE
      """).SingleAsync(ct);
    await db.PbcRequests.Attach(request).ReloadAsync(ct);
    if (request.ClientId != current.ClientId || request.EngagementId != current.EngagementId)
      return CommandResult<PbcReplyReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizeClientAsync(db, actor, request, ct);
    if (!auth.Succeeded) return CommandResult<PbcReplyReceipt>.Fail(auth.ErrorCode!, auth.Message!);

    // The firm lock serializes reply operation keys across targets; this second lookup closes the
    // race between the initial receipt read and the locked write path.
    prior = await FindReplyOperationAsync(db, actor, clientRequestId, ct);
    if (prior is not null)
    {
      auth = await AuthorizeClientAsync(db, actor, request, ct);
      if (!auth.Succeeded) return CommandResult<PbcReplyReceipt>.Fail(auth.ErrorCode!, auth.Message!);
      return ReplayOrConflict(prior, requestId, clientRequestId, hash);
    }

    var window = await ClientPortalService.RequireUploadWindowAsync(db, actor, request.EngagementId, ct);
    if (!window.Succeeded) return CommandResult<PbcReplyReceipt>.Fail(window.ErrorCode!, window.Message!);
    if (request.State is PbcStates.Draft or PbcStates.Accepted or PbcStates.Closed)
      return CommandResult<PbcReplyReceipt>.Fail("pbc.message-state", "This request is not open for replies.");

    var now = DateTimeOffset.UtcNow;
    var communication = Message(request, actor.UserId, PbcCommunicationKinds.ClientMessage, body, now);
    communication.ClientRequestId = clientRequestId;
    communication.ClientRequestSha256 = hash;
    db.PbcCommunications.Add(communication);
    if (request.State == PbcStates.ClarificationRequired)
    {
      request.State = PbcStates.Resubmitted;
      request.Revision++;
      request.UpdatedAt = now;
    }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<PbcReplyReceipt>.Ok(ToReplyReceipt(communication));
  }

  /// <summary>Returns the exact reply receipt, or null after current participant authorization proves absence.</summary>
  public static async Task<CommandResult<PbcReplyReceipt?>> GetReplyReceiptAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid requestId, Guid clientRequestId,
    CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || clientRequestId == Guid.Empty)
      return CommandResult<PbcReplyReceipt?>.Fail("request.invalid", "A request and operation key are required.");
    var current = await db.PbcRequests.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == requestId && x.FirmId == actor.FirmId, ct);
    if (current is null) return CommandResult<PbcReplyReceipt?>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, current, ct);
    if (!auth.Succeeded) return CommandResult<PbcReplyReceipt?>.Fail(auth.ErrorCode!, auth.Message!);

    var prior = await FindReplyOperationAsync(db, actor, clientRequestId, ct);
    var refreshed = await ReloadAndAuthorizeCurrentParticipantAsync(db, actor, current, ct);
    if (!refreshed.Succeeded)
      return CommandResult<PbcReplyReceipt?>.Fail(refreshed.ErrorCode!, refreshed.Message!);
    if (prior is null) return CommandResult<PbcReplyReceipt?>.Ok(null);
    if (prior.PbcRequestId != requestId || prior.Kind != PbcCommunicationKinds.ClientMessage ||
        prior.ClientRequestSha256 is null)
      return CommandResult<PbcReplyReceipt?>.Fail(ErrorCodes.IdempotencyConflict,
        "This operation key belongs to a different reply target.");
    return CommandResult<PbcReplyReceipt?>.Ok(ToReplyReceipt(prior));
  }

  // Direct Application callers predating the API operation key remain source-compatible. The HTTP contract
  // uses the overload above and requires a non-empty client-generated key for replay guarantees.
  public static async Task<CommandResult> ReplyAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid requestId, string body,
    CancellationToken ct = default)
  {
    var result = await ReplyAsync(db, actor, requestId, Guid.NewGuid(), body, ct);
    return result.Succeeded ? CommandResult.Ok() : CommandResult.Fail(result.ErrorCode!, result.Message!);
  }

  private static Task<PbcCommunication?> FindReplyOperationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientRequestId, CancellationToken ct) =>
    db.PbcCommunications.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.AuthorUserId == actor.UserId && x.ClientRequestId == clientRequestId, ct);

  private static async Task<CommandResult> ReloadAndAuthorizeCurrentParticipantAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcRequest expectedScope, CancellationToken ct)
  {
    var current = await db.PbcRequests.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == expectedScope.Id && x.FirmId == actor.FirmId, ct);
    if (current is null || current.Id != expectedScope.Id || current.FirmId != expectedScope.FirmId ||
        current.ClientId != expectedScope.ClientId || current.EngagementId != expectedScope.EngagementId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    return await AuthorizeClientAsync(db, actor, current, ct);
  }

  private static CommandResult<PbcReplyReceipt> ReplayOrConflict(
    PbcCommunication prior, Guid requestId, Guid clientRequestId, string hash) =>
    prior.PbcRequestId == requestId && prior.Kind == PbcCommunicationKinds.ClientMessage &&
    prior.ClientRequestId == clientRequestId && prior.ClientRequestSha256 == hash
      ? CommandResult<PbcReplyReceipt>.Ok(ToReplyReceipt(prior))
      : CommandResult<PbcReplyReceipt>.Fail(ErrorCodes.IdempotencyConflict,
        "This operation key was already used for different reply content or target.");

  private static PbcReplyReceipt ToReplyReceipt(PbcCommunication communication) =>
    new(communication.ClientRequestId!.Value, communication.PbcRequestId, communication.Id,
      communication.ClientRequestSha256!);

  private static string HashReply(string canonicalBody) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalBody))).ToLowerInvariant();
}
