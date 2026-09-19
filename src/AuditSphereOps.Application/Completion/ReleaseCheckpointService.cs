using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
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
  public static async Task<CommandResult<Guid>> RecordCheckpointDirectAsync(
    IAuditSphereDbContext db,
    IReleaseCheckpointStore store,
    ActorContext actor,
    RecordReleaseCheckpointRequest request,
    CancellationToken ct = default)
  {
    if (request.CandidateId == Guid.Empty || request.ExpectedCandidateRevision < 1 ||
        string.IsNullOrWhiteSpace(request.AuthorizedReleaseKey) ||
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

    // Write outside application database, re-read and compare
    var receipt = await store.WriteAsync(request.ManifestDigest, request.ManifestBytes, ct);
    var verified = await store.VerifyAsync(receipt.Reference, request.ManifestDigest, ct);
    if (verified is null || !string.Equals(verified.ContentSha256Hex, request.ManifestDigest, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "External checkpoint verification failed.");

    var existing = await db.ReleaseCheckpoints.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ReleaseCandidateId == candidate.Id &&
      x.CandidateRevision == candidate.Revision && x.ManifestDigest == request.ManifestDigest, ct);

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
      AuthorizedReleaseKey = request.AuthorizedReleaseKey.Trim(),
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
