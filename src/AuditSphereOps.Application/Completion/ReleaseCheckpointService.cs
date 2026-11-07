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
    if (!string.Equals(candidate.ManifestDigest, request.ManifestDigest, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.ManifestMismatch, "The release candidate manifest does not match.");
    if (candidate.Status != ReleaseStates.Ready)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The candidate is not ready for issuance.");

    var existing = await db.ReleaseCheckpoints.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ReleaseCandidateId == candidate.Id &&
      x.CandidateRevision == candidate.Revision && x.ManifestDigest == request.ManifestDigest, ct);
    if (existing is not null && !string.Equals(existing.AuthorizedReleaseKey, releaseKey, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
        "The checkpoint is already bound to a different authorized release key.");

    // Write outside application database, re-read and compare
    var receipt = await store.WriteAsync(request.ManifestDigest, request.ManifestBytes, ct);
    var verified = await store.VerifyAsync(receipt.Reference, request.ManifestDigest, ct);
    if (verified is null || !string.Equals(verified.ContentSha256Hex, request.ManifestDigest, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "External checkpoint verification failed.");

    if (existing is not null)
    {
      existing.StoredReference = receipt.Reference;
      existing.ReadBackDigest = verified.ContentSha256Hex;
      existing.VerifiedStatus = "VERIFIED";
      existing.VerifiedAt = DateTimeOffset.UtcNow;
      existing.Verifier = actor.UserId.ToString("D");
      await db.SaveChangesAsync(ct);
      return CommandResult<Guid>.Ok(existing.Id);
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
      ManifestDigest = request.ManifestDigest,
      StoredReference = receipt.Reference,
      ReadBackDigest = verified.ContentSha256Hex,
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
