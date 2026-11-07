using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

public sealed record ReleaseCheckpointPayload(
  Guid CandidateId,
  long CandidateRevision,
  string ManifestDigest,
  string AuthorizedReleaseKey,
  string ManifestBytesBase64);

/// <summary>
/// Durable operation handler for "RecordReleaseCheckpoint.v1".
/// Fenced, leased, idempotent and reconcilable write of release evidence outside the application database.
/// </summary>
public sealed class ReleaseCheckpointHandler(
  IReleaseCheckpointStore store) : IOperationHandler
{

  public const string Kind = "RecordReleaseCheckpoint.v1";
  public const string WorkerGroup = "release-checkpoint";
  public static OperationDefinition Descriptor { get; } =
    new(Kind, OperationMode.LIVE, OperationAuthority.LIVE_PROVIDER, SchemaVersion: 1, Group: WorkerGroup);

  public OperationDefinition Definition => Descriptor;

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      var doc = JsonDocument.Parse(request.PayloadJson);
      var root = doc.RootElement;
      var candidateId = root.GetProperty("candidateId").GetGuid();
      var candidateRevision = root.GetProperty("candidateRevision").GetInt64();
      var manifestDigest = root.GetProperty("manifestDigest").GetString() ?? string.Empty;
      var rawReleaseKey = root.GetProperty("authorizedReleaseKey").GetString();
      var bytesBase64 = root.GetProperty("manifestBytesBase64").GetString() ?? string.Empty;

      if (candidateId != request.TargetId || candidateRevision != request.ExpectedRevision ||
          string.IsNullOrWhiteSpace(manifestDigest) || manifestDigest.Length != 64 ||
          !ReleaseCheckpointKey.TryNormalize(rawReleaseKey, out var releaseKey) ||
          string.IsNullOrWhiteSpace(bytesBase64))
      {
        throw new OperationBlockedException("invalid-checkpoint-request");
      }

      var bytes = Convert.FromBase64String(bytesBase64);
      var computed = Hashing.Sha256Hex(bytes);
      if (!string.Equals(computed, manifestDigest, StringComparison.OrdinalIgnoreCase))
        throw new OperationBlockedException("checkpoint-digest-mismatch");

      return JsonSerializer.Serialize(new
      {
        candidateId = candidateId.ToString("D"),
        candidateRevision,
        manifestDigest = manifestDigest.ToLowerInvariant(),
        authorizedReleaseKey = releaseKey,
        manifestBytesBase64 = bytesBase64
      });
    }
    catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
    {
      throw new OperationBlockedException("invalid-checkpoint-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    var candidates = await db.ReleaseCandidates.FromSqlInterpolated($"""
      SELECT * FROM release_candidates WHERE firm_id = {op.FirmId} AND id = {op.TargetId}
        AND client_id = {op.ClientId} AND engagement_id = {op.EngagementId} FOR UPDATE
      """).ToListAsync(ct);

    if (candidates.Count != 1 || candidates[0].Revision != op.ExpectedRevision ||
        candidates[0].Status != ReleaseStates.Ready)
    {
      throw new OperationBlockedException("candidate-scope-or-revision-conflict", authorization: true);
    }
  }

  public async Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct)
  {
    var payload = ReadPayload(op.PayloadJson);
    var bytes = Convert.FromBase64String(payload.ManifestBytesBase64);
    var receipt = await store.WriteAsync(payload.ManifestDigest, bytes, ct);
    var verified = await store.VerifyAsync(receipt.Reference, payload.ManifestDigest, ct);

    if (!ReleaseCheckpointReuse.IsVerifiedReceipt(receipt, receipt.Reference, payload.ManifestDigest, bytes.LongLength) ||
        !ReleaseCheckpointReuse.IsVerifiedReceipt(verified, receipt.Reference, payload.ManifestDigest, bytes.LongLength))
      throw new OperationBlockedException("checkpoint-write-unverifiable");

    return new OperationResult(verified!.Reference, verified.ContentSha256Hex);
  }

  public async Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct)
  {
    var payload = ReadPayload(op.PayloadJson);
    var bytes = Convert.FromBase64String(payload.ManifestBytesBase64);
    var receipt = string.IsNullOrWhiteSpace(op.ResultIdentity)
      ? await store.ProbeAsync(payload.ManifestDigest, ct)
      : await store.VerifyAsync(op.ResultIdentity, payload.ManifestDigest, ct);

    if (receipt is null)
      throw new SafeRetryException(TimeSpan.FromSeconds(5));

    var expectedReference = string.IsNullOrWhiteSpace(op.ResultIdentity) ? receipt.Reference : op.ResultIdentity;
    if (!ReleaseCheckpointReuse.IsVerifiedReceipt(receipt, expectedReference, payload.ManifestDigest, bytes.LongLength))
      throw new OperationBlockedException("checkpoint-receipt-conflict");

    return new OperationResult(receipt.Reference, receipt.ContentSha256Hex);
  }

  public async Task<OperationResult> PublishAsync(
    IAuditSphereDbContext db,
    DurableOperation op,
    OperationResult? verifiedRemoteResult,
    CancellationToken ct)
  {
    if (verifiedRemoteResult is null || string.IsNullOrWhiteSpace(verifiedRemoteResult.Identity) ||
        string.IsNullOrWhiteSpace(verifiedRemoteResult.Digest))
    {
      throw new OperationBlockedException("checkpoint-result-unverifiable");
    }

    var payload = ReadPayload(op.PayloadJson);
    if (!string.Equals(verifiedRemoteResult.Digest, payload.ManifestDigest, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("checkpoint-receipt-conflict");

    var candidate = await db.ReleaseCandidates.SingleOrDefaultAsync(x =>
      x.FirmId == op.FirmId && x.Id == op.TargetId, ct);

    if (candidate is null || payload.CandidateId != op.TargetId ||
        payload.CandidateRevision != op.ExpectedRevision || candidate.Revision != op.ExpectedRevision ||
        candidate.ClientId != op.ClientId || candidate.EngagementId != op.EngagementId ||
        candidate.Status != ReleaseStates.Ready ||
        !string.Equals(candidate.ManifestDigest, payload.ManifestDigest, StringComparison.Ordinal))
      throw new OperationBlockedException("candidate-scope-or-revision-conflict", authorization: true);

    var matchingRevisionRows = await db.ReleaseCheckpoints.AsNoTracking().Where(x =>
      x.FirmId == op.FirmId && x.ReleaseCandidateId == candidate.Id &&
      x.CandidateRevision == candidate.Revision).ToListAsync(ct);
    if (matchingRevisionRows.Count > 1)
      throw new OperationBlockedException("checkpoint-evidence-conflict");

    var existing = matchingRevisionRows.SingleOrDefault();

    if (existing is not null &&
        !string.Equals(existing.AuthorizedReleaseKey, payload.AuthorizedReleaseKey, StringComparison.Ordinal))
      throw new OperationBlockedException("checkpoint-release-key-conflict");

    if (existing is not null)
    {
      if (!ReleaseCheckpointReuse.HasMatchingIdentity(existing, op.FirmId, candidate.ClientId,
            candidate.EngagementId, candidate.Id, candidate.Revision,
            payload.AuthorizedReleaseKey, payload.ManifestDigest) ||
          !ReleaseCheckpointReuse.HasVerifiedMetadata(existing, payload.ManifestDigest))
      {
        throw new OperationBlockedException("checkpoint-evidence-conflict");
      }

      var manifestBytes = Convert.FromBase64String(payload.ManifestBytesBase64);
      if (!await ReleaseCheckpointReuse.VerifyStoredEvidenceAsync(store, existing, payload.ManifestDigest,
            manifestBytes.LongLength, ct))
      {
        throw new OperationBlockedException("checkpoint-existing-evidence-unverifiable");
      }

      return new OperationResult(existing.StoredReference, existing.ReadBackDigest);
    }

    var checkpoint = new ReleaseCheckpoint
    {
      Id = Guid.CreateVersion7(),
      FirmId = op.FirmId,
      ClientId = candidate.ClientId,
      EngagementId = candidate.EngagementId,
      ReleaseCandidateId = candidate.Id,
      CandidateRevision = candidate.Revision,
      AuthorizedReleaseKey = payload.AuthorizedReleaseKey,
      ManifestDigest = payload.ManifestDigest,
      StoredReference = verifiedRemoteResult.Identity,
      ReadBackDigest = payload.ManifestDigest,
      VerifiedStatus = "VERIFIED",
      VerifiedAt = DateTimeOffset.UtcNow,
      Verifier = "RecordReleaseCheckpoint.v1",
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.ReleaseCheckpoints.Add(checkpoint);

    await db.SaveChangesAsync(ct);
    return verifiedRemoteResult;
  }

  private static ReleaseCheckpointPayload ReadPayload(string json)
  {
    var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    return new ReleaseCheckpointPayload(
      root.GetProperty("candidateId").GetGuid(),
      root.GetProperty("candidateRevision").GetInt64(),
      root.GetProperty("manifestDigest").GetString()!,
      root.GetProperty("authorizedReleaseKey").GetString()!,
      root.GetProperty("manifestBytesBase64").GetString()!);
  }
}
