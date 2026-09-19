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

  public OperationDefinition Definition { get; } =
    new(Kind, OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION, SchemaVersion: 1, Group: "release");

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      var doc = JsonDocument.Parse(request.PayloadJson);
      var root = doc.RootElement;
      var candidateId = root.GetProperty("candidateId").GetGuid();
      var candidateRevision = root.GetProperty("candidateRevision").GetInt64();
      var manifestDigest = root.GetProperty("manifestDigest").GetString() ?? string.Empty;
      var releaseKey = root.GetProperty("authorizedReleaseKey").GetString() ?? string.Empty;
      var bytesBase64 = root.GetProperty("manifestBytesBase64").GetString() ?? string.Empty;

      if (candidateId != request.TargetId || candidateRevision != request.ExpectedRevision ||
          string.IsNullOrWhiteSpace(manifestDigest) || manifestDigest.Length != 64 ||
          string.IsNullOrWhiteSpace(releaseKey) || string.IsNullOrWhiteSpace(bytesBase64))
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
        authorizedReleaseKey = releaseKey.Trim(),
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

    if (verified is null || !string.Equals(verified.ContentSha256Hex, payload.ManifestDigest, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("checkpoint-write-unverifiable");

    return new OperationResult(receipt.Reference, receipt.ContentSha256Hex);
  }

  public async Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct)
  {
    var payload = ReadPayload(op.PayloadJson);
    var receipt = string.IsNullOrWhiteSpace(op.ResultIdentity)
      ? await store.ProbeAsync(payload.ManifestDigest, ct)
      : await store.VerifyAsync(op.ResultIdentity, payload.ManifestDigest, ct);

    if (receipt is null)
      throw new SafeRetryException(TimeSpan.FromSeconds(5));

    if (!string.Equals(receipt.ContentSha256Hex, payload.ManifestDigest, StringComparison.OrdinalIgnoreCase))
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

    if (candidate is null || candidate.Revision != op.ExpectedRevision)
      throw new OperationBlockedException("candidate-scope-or-revision-conflict", authorization: true);

    var existing = await db.ReleaseCheckpoints.SingleOrDefaultAsync(x =>
      x.FirmId == op.FirmId && x.ReleaseCandidateId == candidate.Id &&
      x.CandidateRevision == candidate.Revision && x.ManifestDigest == payload.ManifestDigest, ct);

    if (existing is null)
    {
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
        ReadBackDigest = verifiedRemoteResult.Digest,
        VerifiedStatus = "VERIFIED",
        VerifiedAt = DateTimeOffset.UtcNow,
        Verifier = "RecordReleaseCheckpoint.v1",
        CreatedAt = DateTimeOffset.UtcNow
      };
      db.ReleaseCheckpoints.Add(checkpoint);
    }
    else
    {
      existing.StoredReference = verifiedRemoteResult.Identity;
      existing.ReadBackDigest = verifiedRemoteResult.Digest;
      existing.VerifiedStatus = "VERIFIED";
      existing.VerifiedAt = DateTimeOffset.UtcNow;
      existing.Verifier = "RecordReleaseCheckpoint.v1";
    }

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
