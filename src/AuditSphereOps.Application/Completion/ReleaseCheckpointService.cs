using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

public sealed record RecordReleaseCheckpointRequest(
  Guid CandidateId,
  long ExpectedCandidateRevision,
  string AuthorizedReleaseKey,
  string ManifestDigest,
  byte[] ManifestBytes);

public static class ReleaseCheckpointService
{
  /// <summary>Enqueues the exact checkpoint intent for the isolated release-checkpoint worker. The direct writer is retained for
  /// controlled local verification; this producer is the supported deployment path.</summary>
  public static async Task<CommandResult<Guid>> EnqueueCheckpointAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordReleaseCheckpointRequest request,
    ReleaseCheckpointRuntimeOptions runtime, IOperationStore operationStore, ReleaseCheckpointHandler handler,
    CancellationToken ct = default)
  {
    if (!ReleaseCheckpointKey.TryNormalize(request.AuthorizedReleaseKey, out var releaseKey))
      return CommandResult<Guid>.Fail("checkpoint.invalid", "Invalid checkpoint request parameters.");
    if (request.CandidateId == Guid.Empty || request.ExpectedCandidateRevision < 1 ||
        string.IsNullOrWhiteSpace(request.ManifestDigest) || request.ManifestDigest.Length != 64 ||
        request.ManifestBytes is null || request.ManifestBytes.Length == 0)
      return CommandResult<Guid>.Fail("checkpoint.invalid", "Invalid checkpoint request parameters.");
    var digest = request.ManifestDigest.ToLowerInvariant();
    if (!string.Equals(Hashing.Sha256Hex(request.ManifestBytes), digest, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.ManifestMismatch, "Manifest bytes do not match declared digest.");
    var candidate = await db.ReleaseCandidates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.CandidateId && x.FirmId == actor.FirmId, ct);
    if (candidate is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, candidate.ClientId, candidate.EngagementId,
        ["Partner", "Administrator"], InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!runtime.Enabled)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Release checkpoint execution is not enabled for this environment.");
    if (candidate.Revision != request.ExpectedCandidateRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The release candidate changed; reload it.");
    if (candidate.Status != ReleaseStates.Ready)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The candidate is not ready for checkpointing.");
    if (!string.Equals(candidate.ManifestDigest, digest, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.ManifestMismatch, "The release candidate manifest does not match.");
    if (candidate.TargetKind == ReleaseTargetKinds.FinancialPackage &&
        (candidate.CompositionId is null || candidate.CompositionManifestDigest is null))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "The candidate lacks an exact statement-composition pin; prepare it again.");

    var idempotencyKey = $"checkpoint:{candidate.Id:D}:{candidate.Revision}:{digest}";
    var payload = JsonSerializer.Serialize(new
    {
      candidateId = candidate.Id,
      candidateRevision = candidate.Revision,
      manifestDigest = digest,
      authorizedReleaseKey = releaseKey,
      manifestBytesBase64 = Convert.ToBase64String(request.ManifestBytes)
    });
    try
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var enqueued = await operationStore.EnqueueAsync(db, new OperationRequest(
        actor.FirmId, candidate.ClientId, candidate.EngagementId, ReleaseCheckpointHandler.Kind,
        candidate.Id, candidate.Revision, idempotencyKey, payload, actor.UserId), handler, ct);
      if (!enqueued.Succeeded)
        return CommandResult<Guid>.Fail(enqueued.ErrorCode!, enqueued.Message!);
      await tx.CommitAsync(ct);
      return enqueued;
    }
    catch (OperationBlockedException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The checkpoint operation request was refused.");
    }
  }

  public static async Task<CommandResult<Guid>> RecordCheckpointDirectAsync(
    IAuditSphereDbContext db,
    IReleaseCheckpointStore store,
    ActorContext actor,
    RecordReleaseCheckpointRequest request,
    CancellationToken ct = default)
  {
    if (!ReleaseCheckpointKey.TryNormalize(request.AuthorizedReleaseKey, out var releaseKey))
      return CommandResult<Guid>.Fail("checkpoint.invalid", "Invalid checkpoint request parameters.");
    if (request.CandidateId == Guid.Empty || request.ExpectedCandidateRevision < 1 ||
        string.IsNullOrWhiteSpace(request.ManifestDigest) || request.ManifestDigest.Length != 64 ||
        request.ManifestBytes is null || request.ManifestBytes.Length == 0)
    {
      return CommandResult<Guid>.Fail("checkpoint.invalid", "Invalid checkpoint request parameters.");
    }

    var computedDigest = Hashing.Sha256Hex(request.ManifestBytes);
    if (!string.Equals(computedDigest, request.ManifestDigest, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.ManifestMismatch, "Manifest bytes do not match declared digest.");

    var candidate = await db.ReleaseCandidates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.CandidateId && x.FirmId == actor.FirmId, ct);
    if (candidate is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, candidate.ClientId, candidate.EngagementId,
        ["Partner", "Administrator"], InternalOnly: true, RequireProfessionalWork: true), ct);

    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    if (candidate.Revision != request.ExpectedCandidateRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The release candidate changed; reload it.");
    var manifestDigest = request.ManifestDigest.ToLowerInvariant();
    if (!string.Equals(candidate.ManifestDigest, manifestDigest, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.ManifestMismatch, "The release candidate manifest does not match.");
    if (candidate.Status != ReleaseStates.Ready)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The candidate is not ready for issuance.");

    var matchingRevisionRows = await db.ReleaseCheckpoints.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ReleaseCandidateId == candidate.Id &&
      x.CandidateRevision == candidate.Revision).ToListAsync(ct);
    if (matchingRevisionRows.Count > 1)
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
        "The checkpoint evidence for this candidate revision is inconsistent.");

    var existing = matchingRevisionRows.SingleOrDefault();
    if (existing is not null)
    {
      if (!ReleaseCheckpointReuse.HasMatchingIdentity(existing, actor.FirmId, candidate.ClientId,
            candidate.EngagementId, candidate.Id, candidate.Revision, releaseKey, manifestDigest) ||
          !ReleaseCheckpointReuse.HasVerifiedMetadata(existing, manifestDigest))
      {
        return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
          "The checkpoint evidence conflicts with this candidate revision.");
      }

      if (!await ReleaseCheckpointReuse.VerifyStoredEvidenceAsync(store, existing, manifestDigest,
            request.ManifestBytes.LongLength, ct))
      {
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          "Existing checkpoint evidence could not be verified.");
      }

      return CommandResult<Guid>.Ok(existing.Id);
    }

    // Write outside application database, re-read and compare
    var receipt = await store.WriteAsync(manifestDigest, request.ManifestBytes, ct);
    var verified = await store.VerifyAsync(receipt.Reference, manifestDigest, ct);
    if (!ReleaseCheckpointReuse.IsVerifiedReceipt(receipt, receipt.Reference, manifestDigest,
          request.ManifestBytes.LongLength) ||
        !ReleaseCheckpointReuse.IsVerifiedReceipt(verified, receipt.Reference, manifestDigest,
          request.ManifestBytes.LongLength))
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "External checkpoint verification failed.");
    }

    var checkpoint = new ReleaseCheckpoint
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      ClientId = candidate.ClientId,
      EngagementId = candidate.EngagementId,
      ReleaseCandidateId = candidate.Id,
      CandidateRevision = candidate.Revision,
      AuthorizedReleaseKey = releaseKey,
      ManifestDigest = manifestDigest,
      StoredReference = verified!.Reference,
      ReadBackDigest = manifestDigest,
      VerifiedStatus = "VERIFIED",
      VerifiedAt = DateTimeOffset.UtcNow,
      Verifier = actor.UserId.ToString("D"),
      CreatedAt = DateTimeOffset.UtcNow
    };

    db.ReleaseCheckpoints.Add(checkpoint);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(checkpoint.Id);
  }
}

internal static class ReleaseCheckpointReuse
{
  public static bool HasMatchingIdentity(
    ReleaseCheckpoint checkpoint,
    Guid firmId,
    Guid clientId,
    Guid engagementId,
    Guid candidateId,
    long candidateRevision,
    string authorizedReleaseKey,
    string manifestDigest) =>
    checkpoint.FirmId == firmId &&
    checkpoint.ClientId == clientId &&
    checkpoint.EngagementId == engagementId &&
    checkpoint.ReleaseCandidateId == candidateId &&
    checkpoint.CandidateRevision == candidateRevision &&
    string.Equals(checkpoint.AuthorizedReleaseKey, authorizedReleaseKey, StringComparison.Ordinal) &&
    string.Equals(checkpoint.ManifestDigest, manifestDigest, StringComparison.Ordinal);

  public static bool HasVerifiedMetadata(ReleaseCheckpoint checkpoint, string manifestDigest) =>
    string.Equals(checkpoint.VerifiedStatus, "VERIFIED", StringComparison.Ordinal) &&
    !string.IsNullOrWhiteSpace(checkpoint.StoredReference) &&
    string.Equals(checkpoint.ReadBackDigest, manifestDigest, StringComparison.Ordinal) &&
    checkpoint.VerifiedAt is { } verifiedAt && verifiedAt != default &&
    !string.IsNullOrWhiteSpace(checkpoint.Verifier) &&
    checkpoint.CreatedAt != default;

  public static bool IsVerifiedReceipt(
    CheckpointReceipt? receipt,
    string expectedReference,
    string expectedDigest,
    long expectedByteCount) =>
    receipt is not null &&
    !string.IsNullOrWhiteSpace(receipt.Reference) &&
    string.Equals(receipt.Reference, expectedReference, StringComparison.Ordinal) &&
    string.Equals(receipt.ContentSha256Hex, expectedDigest, StringComparison.OrdinalIgnoreCase) &&
    receipt.ByteCount == expectedByteCount;

  public static async Task<bool> VerifyStoredEvidenceAsync(
    IReleaseCheckpointStore store,
    ReleaseCheckpoint checkpoint,
    string manifestDigest,
    long expectedByteCount,
    CancellationToken ct)
  {
    if (!HasVerifiedMetadata(checkpoint, manifestDigest))
      return false;

    var verified = await store.VerifyAsync(checkpoint.StoredReference, manifestDigest, ct);
    return IsVerifiedReceipt(verified, checkpoint.StoredReference, manifestDigest, expectedByteCount);
  }
}
