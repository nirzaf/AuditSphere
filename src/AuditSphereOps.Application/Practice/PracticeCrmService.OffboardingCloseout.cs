using System.Globalization;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record ClientOffboardingCloseoutRequest(
  Guid ClientId,
  string DispositionNotes,
  IReadOnlyList<string>? AcknowledgedActionItems = null,
  long? ExpectedRevision = null,
  bool ArchiveClientOnCloseout = false,
  string? ExpectedReadinessDigest = null,
  string? RequestKey = null);

public sealed record ClientOffboardingCloseoutResult(
  Guid RecordId,
  Guid ClientId,
  string Status,
  string ReadinessOverallState,
  string ReadinessDigest,
  string DispositionNotes,
  IReadOnlyList<string> AcknowledgedActionItems,
  Guid DecidedByUserId,
  DateTimeOffset DecidedAt,
  bool IsArchived,
  string ArchiveRevision,
  string? RequestKey = null);

public sealed record ClientOffboardingCloseoutRecoveryRequest(
  Guid ClientId,
  string RequestKey,
  long ExpectedRevision,
  string ExpectedReadinessDigest,
  string DispositionNotes,
  IReadOnlyList<string>? AcknowledgedActionItems = null);

public sealed record ClientOffboardingCloseoutRecoveryResult(
  string Outcome,
  Guid ClientId,
  Guid ActorUserId,
  string RequestKey,
  string ReadinessDigest,
  string ExpectedArchiveRevision,
  string CurrentArchiveRevision,
  ClientOffboardingCloseoutResult? Receipt);

public static class ClientOffboardingCloseoutRecoveryOutcomes
{
  public const string CommittedReceipt = "COMMITTED_RECEIPT";
  public const string SupersededWithoutCommit = "SUPERSEDED_WITHOUT_COMMIT";
}

public static partial class PracticeCrmService
{
  private static readonly JsonSerializerOptions OffboardingJsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
  };

  public static async Task<CommandResult<ClientOffboardingCloseoutResult>> RecordOffboardingCloseoutAsync(
    IAuditSphereDbContext db, ActorContext actor, ClientOffboardingCloseoutRequest request, CancellationToken ct = default,
    IAuditSphereDbContextFactory? snapshotFactory = null)
  {
    if (request.ClientId == Guid.Empty || request.ExpectedRevision is null or < 0)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail("crm.invalid", "A client and non-negative expected archive revision are required.");
    if (string.IsNullOrWhiteSpace(request.DispositionNotes) || request.DispositionNotes.Trim().Length > 2000)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail("crm.invalid", "Review notes are required and must not exceed 2,000 characters.");
    if (request.ArchiveClientOnCloseout)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GateBlocked,
        "Offboarding review does not archive a client. Use the separate reviewed archive action when authorized.");
    if (!TryNormalizeRequestKey(request.RequestKey, out var normalizedKey))
      return CommandResult<ClientOffboardingCloseoutResult>.Fail("crm.invalid", "A canonical request identity is required.");
    if (!TryNormalizeDigest(request.ExpectedReadinessDigest, out var expectedDigest))
      return CommandResult<ClientOffboardingCloseoutResult>.Fail("crm.invalid", "The reviewed readiness SHA-256 is required.");

    var notes = request.DispositionNotes.Trim();
    var gate = new AuthorizationRequest(actor.FirmId, ClientId: request.ClientId, RequiredRoles: CommercialRoles, InternalOnly: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, gate, ct);
    if (!auth.Succeeded)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(auth.ErrorCode!, auth.Message!);

    // Resolve a retry from its original immutable receipt before recomputing readiness.
    // A later change to the underlying checklist must not turn a timed-out exact retry
    // into a different operation or a second record.
    var existing = await db.ClientOffboardingRecords.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.RequestKey == normalizedKey, ct);
    if (existing is not null)
    {
      var replay = await ResolveOffboardingReplayAsync(db, actor, gate, request.ClientId, existing.Id, normalizedKey,
        expectedDigest, request.ExpectedRevision.Value, notes, request.AcknowledgedActionItems, ct);
      if (replay is not null) return replay;
    }

    // Acquire the publication fence at ReadCommitted so post-wait archive revisions are visible.
    // The API supplies a factory for a separate RepeatableRead readiness snapshot created only
    // after this fence is held; direct callers retain the original single-context snapshot path.
    await using var tx = await db.Database.BeginTransactionAsync(
      snapshotFactory is null ? IsolationLevel.RepeatableRead : IsolationLevel.ReadCommitted, ct);
    try
    {
      if (await LockFirmAsync(db, actor.FirmId, ct) is null)
        return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");

      // Recheck after the firm fence. The user lock serializes session-epoch changes;
      // authorization and the client lock below also remain inside this snapshot.
      await db.Users.FromSqlInterpolated(
        $"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
      auth = await AuthorizationDecision.AuthorizeAsync(db, actor, gate, ct);
      if (!auth.Succeeded)
        return CommandResult<ClientOffboardingCloseoutResult>.Fail(auth.ErrorCode!, auth.Message!);

      var client = await db.PracticeClients.FromSqlInterpolated($"""
        SELECT * FROM practice_clients WHERE firm_id = {actor.FirmId} AND id = {request.ClientId} FOR UPDATE
        """).AsNoTracking().SingleOrDefaultAsync(ct);
      if (client is null)
        return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.ScopeDenied, "Client unavailable.");
    }
    catch (Exception ex) when (IsSerializationFailure(ex))
    {
      await tx.RollbackAsync(ct);
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GenerationStale,
        "Authorization or client archive changed while offboarding readiness was being recorded; refresh and review again.");
    }

    // Recheck under the same firm -> client lock order as the separate archive route.
    existing = await db.ClientOffboardingRecords.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.RequestKey == normalizedKey, ct);
    if (existing is not null)
    {
      var replay = ResolveOffboardingReplay(existing, actor, normalizedKey, expectedDigest,
        request.ExpectedRevision.Value, notes,
        (request.AcknowledgedActionItems ?? Array.Empty<string>()).Order(StringComparer.Ordinal).ToArray());
      if (replay is not null)
      {
        await tx.CommitAsync(ct);
        return replay;
      }
    }

    var currentArchiveRevision = await db.ClientLifecycleEvents.AsNoTracking()
      .Where(e => e.FirmId == actor.FirmId && e.ClientId == request.ClientId)
      .Select(e => (long?)e.Revision).MaxAsync(ct) ?? 0;
    if (currentArchiveRevision != request.ExpectedRevision.Value)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GenerationStale,
        "Client archive state changed while readiness was being recorded; refresh and review again.");

    CommandResult<ClientOffboardingReadiness> readinessResult;
    if (snapshotFactory is null)
    {
      readinessResult = await GetClientOffboardingReadinessCoreAsync(db, actor, request.ClientId, true, ct);
    }
    else
    {
      await using var snapshotDb = await snapshotFactory.CreateAsync(ct);
      await using var snapshotTx = await snapshotDb.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
      readinessResult = await GetClientOffboardingReadinessCoreAsync(snapshotDb, actor, request.ClientId, true, ct);
      await snapshotTx.CommitAsync(ct);
    }
    if (!readinessResult.Succeeded || readinessResult.Value is null)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(readinessResult.ErrorCode!, readinessResult.Message!);
    var readiness = readinessResult.Value;
    if (!string.Equals(readiness.ReadinessDigest, expectedDigest, StringComparison.Ordinal))
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GenerationStale,
        "The offboarding readiness state changed since it was reviewed; review current readiness before recording the blocked review.");
    if (!long.TryParse(readiness.ArchiveRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var readinessRevision) ||
        readinessRevision != request.ExpectedRevision.Value || readinessRevision != currentArchiveRevision)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GenerationStale,
        "Client archive state changed; refresh the readiness review before recording it.");

    var acknowledged = request.AcknowledgedActionItems ?? Array.Empty<string>();
    var actionRequiredKeys = readiness.Sections.SelectMany(s => s.Items)
      .Where(i => i.State == ClientOffboardingStates.ActionRequired)
      .Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
    if (acknowledged.Any(string.IsNullOrWhiteSpace) ||
        acknowledged.Distinct(StringComparer.Ordinal).Count() != acknowledged.Count ||
        acknowledged.Any(k => !actionRequiredKeys.Contains(k)))
      return CommandResult<ClientOffboardingCloseoutResult>.Fail("crm.invalid",
        "Acknowledged items must be distinct action-required items from the reviewed readiness snapshot.");
    var normalizedAcknowledged = acknowledged.Order(StringComparer.Ordinal).ToArray();
    var acknowledgedJson = JsonSerializer.Serialize(normalizedAcknowledged, OffboardingJsonOptions);
    var snapshotJson = JsonSerializer.Serialize(readiness, OffboardingJsonOptions);
    var instant = DateTimeOffset.UtcNow;
    var decidedAt = new DateTimeOffset(instant.Ticks - instant.Ticks % 10, TimeSpan.Zero);

    var record = new ClientOffboardingRecord
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      ClientId = request.ClientId,
      Status = ClientOffboardingRecordStates.BlockedDecision,
      ReadinessOverallState = readiness.OverallState,
      ReadinessSnapshotJson = snapshotJson,
      ReadinessDigest = readiness.ReadinessDigest,
      DispositionNotes = notes,
      AcknowledgedActionItemsJson = acknowledgedJson,
      DecidedByUserId = actor.UserId,
      DecidedAt = decidedAt,
      RequestKey = normalizedKey
    };

    db.ClientOffboardingRecords.Add(record);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return CommandResult<ClientOffboardingCloseoutResult>.Ok(MapOffboardingRecord(record, readiness, normalizedAcknowledged));
  }

  public static async Task<CommandResult<ClientOffboardingCloseoutRecoveryResult>> ResolveOffboardingCloseoutAsync(
    IAuditSphereDbContext db, ActorContext actor, ClientOffboardingCloseoutRecoveryRequest request, CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || request.ExpectedRevision < 0)
      return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail("crm.invalid", "A client and non-negative expected archive revision are required.");
    if (string.IsNullOrWhiteSpace(request.DispositionNotes) || request.DispositionNotes.Trim().Length > 2000)
      return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail("crm.invalid", "Original review notes are required and must not exceed 2,000 characters.");
    if (!TryNormalizeRequestKey(request.RequestKey, out var normalizedKey) || normalizedKey != request.RequestKey)
      return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail("crm.invalid", "The original canonical request identity is required.");
    if (!TryNormalizeDigest(request.ExpectedReadinessDigest, out var expectedDigest) || expectedDigest != request.ExpectedReadinessDigest)
      return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail("crm.invalid", "The original reviewed readiness SHA-256 is required.");

    var notes = request.DispositionNotes.Trim();
    var acknowledged = request.AcknowledgedActionItems ?? Array.Empty<string>();
    if (acknowledged.Any(string.IsNullOrWhiteSpace) ||
        acknowledged.Distinct(StringComparer.Ordinal).Count() != acknowledged.Count)
      return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail("crm.invalid", "Original acknowledged items must be distinct and non-empty.");
    var normalizedAcknowledged = acknowledged.Order(StringComparer.Ordinal).ToArray();
    var gate = new AuthorizationRequest(actor.FirmId, ClientId: request.ClientId, RequiredRoles: CommercialRoles, InternalOnly: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, gate, ct);
    if (!auth.Succeeded)
      return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail(auth.ErrorCode!, auth.Message!);

    // Use the same firm -> user -> client serialization as publication. ReadCommitted is
    // essential here: a request that waited for the fence must inspect committed receipt
    // and lifecycle rows using a snapshot taken after the wait.
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
    try
    {
      if (await LockFirmAsync(db, actor.FirmId, ct) is null)
        return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");

      await db.Users.FromSqlInterpolated(
        $"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
      auth = await AuthorizationDecision.AuthorizeAsync(db, actor, gate, ct);
      if (!auth.Succeeded)
        return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail(auth.ErrorCode!, auth.Message!);

      var client = await db.PracticeClients.FromSqlInterpolated($"""
        SELECT * FROM practice_clients WHERE firm_id = {actor.FirmId} AND id = {request.ClientId} FOR UPDATE
        """).AsNoTracking().SingleOrDefaultAsync(ct);
      if (client is null)
        return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail(ErrorCodes.ScopeDenied, "Client unavailable.");

      // A committed receipt always wins over revision drift. Append-only receipt storage
      // makes absence meaningful only after the original publication fence is acquired.
      var existing = await db.ClientOffboardingRecords.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.RequestKey == normalizedKey, ct);
      if (existing is not null)
      {
        var replay = ResolveOffboardingReplay(existing, actor, normalizedKey, expectedDigest,
          request.ExpectedRevision, notes, normalizedAcknowledged);
        if (replay is null || !replay.Succeeded || replay.Value is null)
          return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail(
            replay?.ErrorCode ?? ErrorCodes.GateBlocked, replay?.Message ?? "The retained offboarding review receipt is unavailable.");
        var currentRevisionText = await db.ClientLifecycleEvents.AsNoTracking()
          .Where(e => e.FirmId == actor.FirmId && e.ClientId == request.ClientId)
          .Select(e => (long?)e.Revision).MaxAsync(ct) is long latestRevision
            ? latestRevision.ToString(CultureInfo.InvariantCulture)
            : "0";
        await tx.CommitAsync(ct);
        return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Ok(new(
          ClientOffboardingCloseoutRecoveryOutcomes.CommittedReceipt, request.ClientId, actor.UserId, normalizedKey,
          expectedDigest, request.ExpectedRevision.ToString(CultureInfo.InvariantCulture), currentRevisionText, replay.Value));
      }

      var currentArchiveRevision = await db.ClientLifecycleEvents.AsNoTracking()
        .Where(e => e.FirmId == actor.FirmId && e.ClientId == request.ClientId)
        .Select(e => (long?)e.Revision).MaxAsync(ct) ?? 0;
      if (currentArchiveRevision <= request.ExpectedRevision)
        return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail(ErrorCodes.GenerationStale,
          "No committed review receipt or terminal non-commit proof is available yet. Keep the original request and retry recovery later.");

      await tx.CommitAsync(ct);
      return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Ok(new(
        ClientOffboardingCloseoutRecoveryOutcomes.SupersededWithoutCommit, request.ClientId, actor.UserId, normalizedKey,
        expectedDigest, request.ExpectedRevision.ToString(CultureInfo.InvariantCulture),
        currentArchiveRevision.ToString(CultureInfo.InvariantCulture), null));
    }
    catch (Exception ex) when (IsSerializationFailure(ex))
    {
      await tx.RollbackAsync(ct);
      return CommandResult<ClientOffboardingCloseoutRecoveryResult>.Fail(ErrorCodes.GenerationStale,
        "Authorization or archive state changed while the original review outcome was being resolved. Keep the request and refresh.");
    }
  }

  private static async Task<CommandResult<ClientOffboardingCloseoutResult>?> ResolveOffboardingReplayAsync(
    IAuditSphereDbContext db, ActorContext actor, AuthorizationRequest gate, Guid clientId, Guid recordId,
    string requestKey, string expectedDigest, long expectedRevision, string notes,
    IReadOnlyList<string>? acknowledged, CancellationToken ct)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, gate, ct);
    if (!auth.Succeeded)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(auth.ErrorCode!, auth.Message!);
    var client = await db.PracticeClients.FromSqlInterpolated($"""
      SELECT * FROM practice_clients WHERE firm_id = {actor.FirmId} AND id = {clientId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (client is null)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.ScopeDenied, "Client unavailable.");
    var existing = await db.ClientOffboardingRecords.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == recordId && x.FirmId == actor.FirmId && x.ClientId == clientId && x.RequestKey == requestKey, ct);
    if (existing is null)
    {
      await tx.CommitAsync(ct);
      return null;
    }
    var normalizedAcknowledged = (acknowledged ?? Array.Empty<string>()).Order(StringComparer.Ordinal).ToArray();
    var result = ResolveOffboardingReplay(existing, actor, requestKey, expectedDigest, expectedRevision, notes, normalizedAcknowledged);
    await tx.CommitAsync(ct);
    return result;
  }

  private static CommandResult<ClientOffboardingCloseoutResult>? ResolveOffboardingReplay(
    ClientOffboardingRecord record, ActorContext actor, string requestKey, string expectedDigest,
    long expectedRevision, string notes, IReadOnlyList<string> acknowledged)
  {
    ClientOffboardingReadiness? snapshot;
    List<string>? storedAcknowledged;
    try
    {
      snapshot = JsonSerializer.Deserialize<ClientOffboardingReadiness>(record.ReadinessSnapshotJson, OffboardingJsonOptions);
      storedAcknowledged = JsonSerializer.Deserialize<List<string>>(record.AcknowledgedActionItemsJson, OffboardingJsonOptions);
    }
    catch (JsonException)
    {
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GateBlocked, "The retained offboarding review receipt is unreadable.");
    }

    if (snapshot is null || storedAcknowledged is null || record.Status != ClientOffboardingRecordStates.BlockedDecision ||
        record.DecidedByUserId != actor.UserId || record.RequestKey != requestKey || record.DispositionNotes != notes ||
        record.ReadinessDigest != expectedDigest || snapshot.ReadinessDigest != record.ReadinessDigest ||
        !long.TryParse(snapshot.ArchiveRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ||
        revision != expectedRevision || !storedAcknowledged.SequenceEqual(acknowledged, StringComparer.Ordinal))
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.IdempotencyConflict,
        "This request identity was already used for a different offboarding review.");

    return CommandResult<ClientOffboardingCloseoutResult>.Ok(MapOffboardingRecord(record, snapshot, storedAcknowledged));
  }

  private static ClientOffboardingCloseoutResult MapOffboardingRecord(
    ClientOffboardingRecord record, ClientOffboardingReadiness snapshot, IReadOnlyList<string> acknowledged) => new(
      record.Id, record.ClientId, record.Status, record.ReadinessOverallState, record.ReadinessDigest,
      record.DispositionNotes, acknowledged, record.DecidedByUserId, record.DecidedAt, snapshot.IsArchived,
      snapshot.ArchiveRevision, record.RequestKey);

  private static bool TryNormalizeRequestKey(string? value, out string normalized)
  {
    normalized = string.Empty;
    if (!Guid.TryParseExact(value?.Trim(), "D", out var parsed) || parsed == Guid.Empty) return false;
    var candidate = parsed.ToString("D", CultureInfo.InvariantCulture);
    if (candidate[14] is < '1' or > '8' || candidate[19] is not ('8' or '9' or 'a' or 'b')) return false;
    normalized = candidate;
    return true;
  }

  private static bool TryNormalizeDigest(string? value, out string normalized)
  {
    normalized = string.Empty;
    var candidate = value?.Trim();
    if (candidate is null || candidate.Length != 64 || candidate.Any(c => !Uri.IsHexDigit(c))) return false;
    normalized = candidate.ToLowerInvariant();
    return true;
  }

  private static bool IsSerializationFailure(Exception ex) => ex.GetBaseException() is DbException cause &&
    cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001";
}
