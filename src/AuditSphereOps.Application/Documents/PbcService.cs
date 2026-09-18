using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace AuditSphereOps.Application.Documents;

public sealed record CreatePbcRequestRequest(
  Guid EngagementId,
  string Objective,
  string EntityScope,
  string PeriodStart,
  string PeriodEnd,
  string Area,
  string RequestedFormat,
  string ControlTotals,
  Guid ClientOwnerUserId,
  Guid FirmOwnerUserId,
  Guid ReviewerUserId,
  string DueDate,
  string Confidentiality,
  string AcceptanceCriteria);

public sealed record PbcStateChangeRequest(
  Guid PbcRequestId,
  string State,
  long ExpectedRevision,
  string? ClarificationReason = null);

public sealed record StartPbcUploadRequest(
  Guid PbcRequestId,
  string FileName,
  string ContentType,
  long DeclaredByteCount,
  string DeclaredSha256Hex);

public sealed record RecordPbcUploadChunkRequest(
  Guid UploadIntentId,
  int ChunkIndex,
  long Offset,
  int ByteCount,
  string Sha256Hex,
  string Capability,
  string? StagedPath = null);

public sealed record CompletePbcUploadRequest(Guid UploadIntentId, string FinalSha256Hex);

public sealed record PbcUploadReceipt(Guid UploadIntentId, Guid PbcRequestId, string State,
  long ReceivedByteCount, long Revision, string? Capability = null);

/// <summary>
/// Local PBC and upload boundary. It records bounded transfer intent/chunk receipts
/// and never marks evidence received until a trusted completion boundary verifies it.
/// </summary>
public static class PbcService
{
  public const long MaxUploadBytes = 250L * 1024 * 1024;
  public const int MaxChunkBytes = 8 * 1024 * 1024;
  private static readonly string[] StaffRoles =
    ["Administrator", "Partner", "Manager", "Reviewer", "Staff", "AccountingPreparer", "AccountingReviewer"];

  public static async Task<CommandResult<Guid>> CreateRequestAsync(
    IAuditSphereDbContext db, ActorContext actor, CreatePbcRequestRequest input,
    CancellationToken ct = default)
  {
    var validation = ValidateRequest(input);
    if (validation is not null)
      return CommandResult<Guid>.Fail("pbc.invalid", validation);

    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == input.EngagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizeStaffAsync(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var users = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      (x.Id == input.ClientOwnerUserId || x.Id == input.FirmOwnerUserId || x.Id == input.ReviewerUserId))
      .ToListAsync(ct);
    if (users.Count != 3 || users.Any(x => x.UserKind.Equals("Client", StringComparison.OrdinalIgnoreCase) &&
        x.Id != input.ClientOwnerUserId) || users.SingleOrDefault(x => x.Id == input.ClientOwnerUserId)?.UserKind != "Client")
      return CommandResult<Guid>.Fail("pbc.users.invalid", "PBC owners and reviewer must be active firm users with one client upload owner.");
    if (users.Any(x => x.Disabled))
      return CommandResult<Guid>.Fail("pbc.users.disabled", "PBC cannot assign a disabled user.");

    var now = DateTimeOffset.UtcNow;
    var request = new PbcRequest
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      ClientId = engagement.PracticeClientId,
      EngagementId = engagement.Id,
      Objective = input.Objective.Trim(),
      EntityScope = input.EntityScope.Trim(),
      PeriodStart = input.PeriodStart,
      PeriodEnd = input.PeriodEnd,
      Area = input.Area.Trim(),
      RequestedFormat = input.RequestedFormat.Trim(),
      ControlTotals = input.ControlTotals.Trim(),
      ClientOwnerUserId = input.ClientOwnerUserId,
      FirmOwnerUserId = input.FirmOwnerUserId,
      ReviewerUserId = input.ReviewerUserId,
      DueDate = input.DueDate,
      Confidentiality = input.Confidentiality.Trim(),
      AcceptanceCriteria = input.AcceptanceCriteria.Trim(),
      CreatedByUserId = actor.UserId,
      CreatedAt = now,
      UpdatedAt = now
    };
    db.PbcRequests.Add(request);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail("pbc.conflict", "The PBC request could not be created in the current scope.");
    }
    return CommandResult<Guid>.Ok(request.Id);
  }

  public static async Task<CommandResult> ChangeStateAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcStateChangeRequest input,
    CancellationToken ct = default)
  {
    if (input.PbcRequestId == Guid.Empty || input.ExpectedRevision < 1)
      return CommandResult.Fail("pbc.invalid", "A request and current revision are required.");

    var current = await db.PbcRequests.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == input.PbcRequestId && x.FirmId == actor.FirmId, ct);
    if (current is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var clientAction = input.State is PbcStates.Acknowledged or PbcStates.Resubmitted;
    var auth = clientAction
      ? await AuthorizeClientAsync(db, actor, current, ct)
      : await AuthorizeStaffAsync(db, actor, current.ClientId, current.EngagementId, ct);
    if (!auth.Succeeded)
      return auth;
    if (input.State == PbcStates.Accepted && actor.UserId != current.ReviewerUserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Only the assigned reviewer can accept this request.");
    if (input.State == PbcStates.ClarificationRequired && string.IsNullOrWhiteSpace(input.ClarificationReason))
      return CommandResult.Fail("pbc.clarification-required", "A specific clarification reason is required.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var request = await db.PbcRequests.FromSqlInterpolated($"""
      SELECT * FROM pbc_requests WHERE firm_id = {actor.FirmId} AND id = {input.PbcRequestId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (request is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (request.Revision != input.ExpectedRevision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The PBC request changed; reload before changing its state.");
    if (!AllowedTransition(request.State, input.State))
      return CommandResult.Fail("pbc.transition", $"Cannot change a {request.State} request to {input.State}.");

    request.State = input.State;
    request.Revision++;
    request.UpdatedAt = DateTimeOffset.UtcNow;
    request.ClarificationReason = input.State == PbcStates.ClarificationRequired
      ? input.ClarificationReason!.Trim() : request.ClarificationReason;
    if (input.State == PbcStates.Accepted)
    {
      request.AcceptedByUserId = actor.UserId;
      request.AcceptedAt = request.UpdatedAt;
    }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<PbcUploadReceipt>> StartUploadAsync(
    IAuditSphereDbContext db, ActorContext actor, StartPbcUploadRequest input,
    CancellationToken ct = default)
  {
    var validation = ValidateUpload(input);
    if (validation is not null)
      return CommandResult<PbcUploadReceipt>.Fail("pbc.upload.invalid", validation);

    var request = await db.PbcRequests.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == input.PbcRequestId && x.FirmId == actor.FirmId, ct);
    if (request is null)
      return CommandResult<PbcUploadReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeUploadActorAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<PbcUploadReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.State is not (PbcStates.Sent or PbcStates.Acknowledged or PbcStates.Resubmitted or PbcStates.PartiallyReceived))
      return CommandResult<PbcUploadReceipt>.Fail("pbc.upload-state", "The request is not accepting an upload.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var lockedRequest = await db.PbcRequests.FromSqlInterpolated($"""
      SELECT * FROM pbc_requests WHERE firm_id = {actor.FirmId} AND id = {input.PbcRequestId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (lockedRequest is null)
      return CommandResult<PbcUploadReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lockedRequest.State is not (PbcStates.Sent or PbcStates.Acknowledged or PbcStates.Resubmitted or PbcStates.PartiallyReceived))
      return CommandResult<PbcUploadReceipt>.Fail("pbc.upload-state", "The request is not accepting an upload.");

    var now = DateTimeOffset.UtcNow;
    var capability = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    var intent = new PbcUploadIntent
    {
      Id = Guid.CreateVersion7(),
      FirmId = lockedRequest.FirmId,
      ClientId = lockedRequest.ClientId,
      EngagementId = lockedRequest.EngagementId,
      PbcRequestId = lockedRequest.Id,
      UploaderUserId = actor.UserId,
      FileName = input.FileName.Trim(),
      ContentType = input.ContentType.Trim(),
      DeclaredByteCount = input.DeclaredByteCount,
      DeclaredSha256Hex = input.DeclaredSha256Hex.ToLowerInvariant(),
      CapabilityHash = Hashing.Sha256Hex(capability),
      ExpiresAt = now.AddHours(24),
      CreatedAt = now
    };
    lockedRequest.State = PbcStates.PartiallyReceived;
    lockedRequest.Revision++;
    lockedRequest.UpdatedAt = now;
    db.PbcUploadIntents.Add(intent);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<PbcUploadReceipt>.Ok(new PbcUploadReceipt(
      intent.Id, intent.PbcRequestId, intent.State, intent.ReceivedByteCount, intent.Revision, capability));
  }

  public static async Task<CommandResult<PbcUploadReceipt>> RecordChunkAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordPbcUploadChunkRequest input,
    CancellationToken ct = default)
  {
    var validation = ValidateChunk(input);
    if (validation is not null)
      return CommandResult<PbcUploadReceipt>.Fail("pbc.chunk.invalid", validation);

    var intent = await db.PbcUploadIntents.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == input.UploadIntentId && x.FirmId == actor.FirmId, ct);
    if (intent is null)
      return CommandResult<PbcUploadReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!IsCapability(input.Capability) || Hashing.Sha256Hex(input.Capability) != intent.CapabilityHash)
      return CommandResult<PbcUploadReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var request = await db.PbcRequests.AsNoTracking().SingleAsync(x =>
      x.Id == intent.PbcRequestId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeUploadActorAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<PbcUploadReceipt>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    intent = await db.PbcUploadIntents.FromSqlInterpolated($"""
      SELECT * FROM pbc_upload_intents WHERE firm_id = {actor.FirmId} AND id = {input.UploadIntentId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (intent is null)
      return CommandResult<PbcUploadReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var existing = await db.PbcUploadChunks.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.PbcUploadIntentId == intent.Id && x.ChunkIndex == input.ChunkIndex, ct);
    var hash = input.Sha256Hex.ToLowerInvariant();
    if (existing is not null)
    {
      if (existing.Offset != input.Offset || existing.ByteCount != input.ByteCount || existing.Sha256Hex != hash)
        return CommandResult<PbcUploadReceipt>.Fail("pbc.chunk.conflict", "A replayed chunk index has different content.");
      await tx.CommitAsync(ct);
      return CommandResult<PbcUploadReceipt>.Ok(new PbcUploadReceipt(intent.Id, intent.PbcRequestId,
        intent.State, intent.ReceivedByteCount, intent.Revision));
    }
    if (intent.ExpiresAt <= DateTimeOffset.UtcNow)
    {
      intent.State = PbcUploadStates.Expired;
      intent.FailureReason = "Upload capability expired.";
      intent.Revision++;
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return CommandResult<PbcUploadReceipt>.Fail("pbc.upload-expired", "The upload capability expired.");
    }
    var expectedIndex = await db.PbcUploadChunks.CountAsync(x =>
      x.FirmId == actor.FirmId && x.PbcUploadIntentId == intent.Id, ct);
    if (input.ChunkIndex != expectedIndex || input.Offset != intent.ReceivedByteCount)
      return CommandResult<PbcUploadReceipt>.Fail("pbc.chunk-offset", "Chunks must be sequential and contiguous.");
    if (input.ByteCount > intent.DeclaredByteCount - intent.ReceivedByteCount)
      return CommandResult<PbcUploadReceipt>.Fail("pbc.quota", "The chunk exceeds the declared upload size.");

    db.PbcUploadChunks.Add(new PbcUploadChunk
    {
      Id = Guid.CreateVersion7(), FirmId = intent.FirmId, ClientId = intent.ClientId,
      EngagementId = intent.EngagementId, PbcUploadIntentId = intent.Id,
      ChunkIndex = input.ChunkIndex, Offset = input.Offset, ByteCount = input.ByteCount,
      Sha256Hex = hash, StagedPath = input.StagedPath, ReceivedAt = DateTimeOffset.UtcNow
    });
    intent.ReceivedByteCount += input.ByteCount;
    intent.State = PbcUploadStates.Chunking;
    intent.Revision++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<PbcUploadReceipt>.Ok(new PbcUploadReceipt(intent.Id, intent.PbcRequestId,
      intent.State, intent.ReceivedByteCount, intent.Revision));
  }

  public static async Task<CommandResult<PbcUploadReceipt>> CompleteUploadAsync(
    IAuditSphereDbContext db, ActorContext actor, CompletePbcUploadRequest input,
    CancellationToken ct = default)
  {
    if (input.UploadIntentId == Guid.Empty || !IsSha256(input.FinalSha256Hex))
      return CommandResult<PbcUploadReceipt>.Fail("pbc.upload.invalid", "A final content hash is required.");

    var intent = await db.PbcUploadIntents.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == input.UploadIntentId && x.FirmId == actor.FirmId, ct);
    if (intent is null)
      return CommandResult<PbcUploadReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeStaffAsync(db, actor, intent.ClientId, intent.EngagementId, ct);
    if (!auth.Succeeded)
      return CommandResult<PbcUploadReceipt>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    intent = await db.PbcUploadIntents.FromSqlInterpolated($"""
      SELECT * FROM pbc_upload_intents WHERE firm_id = {actor.FirmId} AND id = {input.UploadIntentId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (intent is null)
      return CommandResult<PbcUploadReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (intent.State == PbcUploadStates.Received)
    {
      return string.Equals(intent.FinalSha256Hex, input.FinalSha256Hex, StringComparison.OrdinalIgnoreCase)
        ? CommandResult<PbcUploadReceipt>.Ok(new PbcUploadReceipt(intent.Id, intent.PbcRequestId,
          intent.State, intent.ReceivedByteCount, intent.Revision))
        : CommandResult<PbcUploadReceipt>.Fail("pbc.upload.conflict", "The upload was already completed with a different hash.");
    }
    if (intent.State != PbcUploadStates.Chunking || intent.ReceivedByteCount != intent.DeclaredByteCount)
      return CommandResult<PbcUploadReceipt>.Fail("pbc.upload-incomplete", "The trusted transfer boundary has not received every declared byte.");
    if (!string.Equals(intent.DeclaredSha256Hex, input.FinalSha256Hex, StringComparison.OrdinalIgnoreCase))
      return CommandResult<PbcUploadReceipt>.Fail("pbc.hash-mismatch", "The completed content hash does not match the declared content.");

    var request = await db.PbcRequests.FromSqlInterpolated($"""
      SELECT * FROM pbc_requests WHERE firm_id = {actor.FirmId} AND id = {intent.PbcRequestId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (request is null)
      return CommandResult<PbcUploadReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    intent.State = PbcUploadStates.Received;
    intent.CompletedAt = DateTimeOffset.UtcNow;
    intent.FinalSha256Hex = input.FinalSha256Hex.ToLowerInvariant();
    intent.Revision++;
    request.State = PbcStates.Received;
    request.Revision++;
    request.UpdatedAt = intent.CompletedAt.Value;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<PbcUploadReceipt>.Ok(new PbcUploadReceipt(intent.Id, intent.PbcRequestId,
      intent.State, intent.ReceivedByteCount, intent.Revision));
  }

  private static async Task<CommandResult> AuthorizeStaffAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, StaffRoles, InternalOnly: true), ct);

  private static async Task<CommandResult> AuthorizeClientAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcRequest request, CancellationToken ct)
  {
    if (actor.UserId != request.ClientOwnerUserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId,
        ["ClientUser"]), ct);
  }

  private static async Task<CommandResult> AuthorizeUploadActorAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcRequest request, CancellationToken ct) =>
    actor.UserId == request.ClientOwnerUserId && actor.Roles.Contains("ClientUser", StringComparer.OrdinalIgnoreCase)
      ? await AuthorizeClientAsync(db, actor, request, ct)
      : await AuthorizeStaffAsync(db, actor, request.ClientId, request.EngagementId, ct);

  private static bool AllowedTransition(string current, string next) =>
    (current, next) switch
    {
      (PbcStates.Draft, PbcStates.Sent) => true,
      (PbcStates.Sent, PbcStates.Acknowledged) => true,
      (PbcStates.Received, PbcStates.UnderReview) => true,
      (PbcStates.Resubmitted, PbcStates.UnderReview) => true,
      (PbcStates.UnderReview, PbcStates.Accepted) => true,
      (PbcStates.UnderReview, PbcStates.ClarificationRequired) => true,
      (PbcStates.ClarificationRequired, PbcStates.Resubmitted) => true,
      (PbcStates.Accepted, PbcStates.Closed) => true,
      _ => false
    };

  private static string? ValidateRequest(CreatePbcRequestRequest input)
  {
    if (input.EngagementId == Guid.Empty || input.ClientOwnerUserId == Guid.Empty ||
        input.FirmOwnerUserId == Guid.Empty || input.ReviewerUserId == Guid.Empty)
      return "The engagement and assigned users are required.";
    if (string.IsNullOrWhiteSpace(input.Objective) || input.Objective.Length > 2000 ||
        string.IsNullOrWhiteSpace(input.EntityScope) || input.EntityScope.Length > 500 ||
        string.IsNullOrWhiteSpace(input.Area) || input.Area.Length > 200 ||
        string.IsNullOrWhiteSpace(input.RequestedFormat) || input.RequestedFormat.Length > 200 ||
        input.ControlTotals.Length > 2000 || string.IsNullOrWhiteSpace(input.Confidentiality) ||
        input.Confidentiality.Length > 100 || string.IsNullOrWhiteSpace(input.AcceptanceCriteria) ||
        input.AcceptanceCriteria.Length > 4000)
      return "PBC fields are missing or exceed their bounded lengths.";
    if (!ValidDate(input.PeriodStart) || !ValidDate(input.PeriodEnd) || !ValidDate(input.DueDate) ||
        string.CompareOrdinal(input.PeriodStart, input.PeriodEnd) > 0)
      return "PBC period and due date must be ISO dates with an ordered period.";
    return null;
  }

  private static string? ValidateUpload(StartPbcUploadRequest input)
  {
    if (input.PbcRequestId == Guid.Empty || string.IsNullOrWhiteSpace(input.FileName) ||
        input.FileName.Trim() != input.FileName || input.FileName.Length > 255 ||
        input.FileName.Contains('/') || input.FileName.Contains('\\') || input.FileName.Contains("..") ||
        string.IsNullOrWhiteSpace(input.ContentType) || input.ContentType.Length > 200 ||
        input.DeclaredByteCount <= 0 || input.DeclaredByteCount > MaxUploadBytes ||
        !IsSha256(input.DeclaredSha256Hex))
      return "Upload metadata is invalid or exceeds the bounded transfer policy.";
    return null;
  }

  private static string? ValidateChunk(RecordPbcUploadChunkRequest input) =>
    input.UploadIntentId == Guid.Empty || input.ChunkIndex < 0 || input.Offset < 0 ||
    input.ByteCount is <= 0 or > MaxChunkBytes || !IsSha256(input.Sha256Hex)
      ? "Chunk metadata is invalid or exceeds the bounded transfer policy." : null;

  private static bool IsSha256(string? value) => value is { Length: 64 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

  private static bool IsCapability(string? value) => IsSha256(value);

  private static bool ValidDate(string value) =>
    DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
      System.Globalization.DateTimeStyles.None, out _);
}
