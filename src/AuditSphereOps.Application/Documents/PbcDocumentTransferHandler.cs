using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>Verified provider registration evidence for a staged PBC transfer.</summary>
public sealed record PbcProviderReceipt(string Identity, string ContentSha256Hex, long ByteCount);

/// <summary>
/// Trusted provider boundary for staged PBC bytes (spec section 43.5 item 6). Implementations
/// stream staged bytes into the selected provider target and return verified registration
/// evidence. No implementation may report success without a verified digest.
/// </summary>
public interface IPbcProviderSink
{
  Task<PbcProviderReceipt> UploadAsync(PbcTransferPlan plan, CancellationToken ct);

  /// <summary>Re-reads a previously reported registration; null when no effect is observable.</summary>
  Task<PbcProviderReceipt?> VerifyAsync(string identity, CancellationToken ct);

  /// <summary>Probes the intent target registration after an unknown outcome; returns verified
  /// registration evidence, or null when no effect is observable.</summary>
  Task<PbcProviderReceipt?> ProbeAsync(Guid uploadIntentId, CancellationToken ct);
}

/// <summary>The verified staged-byte plan a worker transfers: ordered chunk paths with digests.</summary>
public sealed record PbcTransferPlan(
  Guid FirmId, Guid UploadIntentId, long DeclaredByteCount, string FinalSha256Hex,
  IReadOnlyList<PbcTransferChunk> Chunks);

public sealed record PbcTransferChunk(int ChunkIndex, long Offset, int ByteCount, string Sha256Hex, string StagedPath);

/// <summary>
/// Durable worker/provider handoff for staged PBC uploads. SIMULATED-only: no live provider
/// adapter is approved, so execution requires the explicit Test/simulation composition.
/// RECEIVED is published locally only after verified provider registration, and staged bytes
/// are fully verified before any provider effect may begin.
/// </summary>
public sealed class PbcDocumentTransferHandler(
  IAuditSphereDbContextFactory factory,
  IPbcProviderSink sink) : IOperationHandler
{
  public const string Kind = "TransferPbcDocument.v1";
  public OperationDefinition Definition { get; } = new(Kind, OperationMode.SIMULATED, OperationAuthority.SIMULATION);

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      var payload = ReadPayload(request.PayloadJson, request.TargetId, request.ExpectedRevision);
      if (request.ClientId is null || request.EngagementId is null)
        throw new OperationBlockedException("invalid-transfer-request");
      return JsonSerializer.Serialize(new
      {
        uploadIntentId = payload.UploadIntentId.ToString("D"),
        intentRevision = payload.IntentRevision,
        finalSha256Hex = payload.FinalSha256Hex,
        declaredByteCount = payload.DeclaredByteCount
      });
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("invalid-transfer-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    var intents = await db.PbcUploadIntents.FromSqlInterpolated($"""
      SELECT * FROM pbc_upload_intents WHERE firm_id = {op.FirmId} AND id = {op.TargetId}
        AND client_id = {op.ClientId} AND engagement_id = {op.EngagementId} FOR UPDATE
      """).ToListAsync(ct);
    if (intents.Count != 1 || intents[0].Revision != op.ExpectedRevision ||
        intents[0].State != PbcUploadStates.Staged || intents[0].TransferOperationId != op.Id ||
        !await db.PbcRequests.AnyAsync(r => r.FirmId == op.FirmId && r.Id == intents[0].PbcRequestId &&
          r.ClientId == op.ClientId && r.EngagementId == op.EngagementId, ct))
      throw new OperationBlockedException("transfer-scope-or-revision-conflict", authorization: true);
  }

  public async Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct)
  {
    var payload = ReadPayload(op.PayloadJson, op.TargetId, op.ExpectedRevision);
    var plan = await LoadPlanAsync(op, payload, ct);
    // Verify every staged byte before contacting the provider: the local effect then either
    // happened completely or not at all, with no partial provider start.
    await VerifyStagedBytesAsync(plan, ct);
    var receipt = await sink.UploadAsync(plan, ct);
    if (string.IsNullOrWhiteSpace(receipt.Identity) || !IsSha256(receipt.ContentSha256Hex) ||
        !string.Equals(receipt.ContentSha256Hex, payload.FinalSha256Hex, StringComparison.OrdinalIgnoreCase) ||
        receipt.ByteCount != payload.DeclaredByteCount)
      throw new OperationBlockedException("provider-receipt-unverifiable");
    return new(receipt.Identity.Trim(), receipt.ContentSha256Hex);
  }

  public async Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct)
  {
    var payload = ReadPayload(op.PayloadJson, op.TargetId, op.ExpectedRevision);
    // Unknown outcome: probe the intent target instead of trusting the operation row; a known
    // identity is re-read exactly as reported.
    var receipt = string.IsNullOrWhiteSpace(op.ResultIdentity)
      ? await sink.ProbeAsync(payload.UploadIntentId, ct)
      : await sink.VerifyAsync(op.ResultIdentity, ct);
    if (receipt is null)
      throw new SafeRetryException(TimeSpan.FromSeconds(5));
    if (!IsSha256(receipt.ContentSha256Hex) ||
        !string.Equals(receipt.ContentSha256Hex, payload.FinalSha256Hex, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("provider-receipt-conflict");
    return new(receipt.Identity, receipt.ContentSha256Hex);
  }

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op,
    OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    if (verifiedRemoteResult is null || string.IsNullOrWhiteSpace(verifiedRemoteResult.Identity) ||
        !IsSha256(verifiedRemoteResult.Digest))
      throw new OperationBlockedException("provider-receipt-unverifiable");
    var payload = ReadPayload(op.PayloadJson, op.TargetId, op.ExpectedRevision);
    if (!string.Equals(verifiedRemoteResult.Digest, payload.FinalSha256Hex, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("provider-receipt-conflict");

    var intent = await db.PbcUploadIntents.SingleAsync(x =>
      x.FirmId == op.FirmId && x.Id == op.TargetId, ct);
    if (intent.Revision != op.ExpectedRevision || intent.TransferOperationId != op.Id)
      throw new OperationBlockedException("transfer-scope-or-revision-conflict", authorization: true);
    if (intent.State == PbcUploadStates.Received)
    {
      if (!string.Equals(intent.ProviderReceiptDigest, verifiedRemoteResult.Digest, StringComparison.OrdinalIgnoreCase))
        throw new OperationBlockedException("provider-receipt-conflict");
      return new(verifiedRemoteResult.Identity, verifiedRemoteResult.Digest);
    }

    var request = await db.PbcRequests.SingleAsync(x =>
      x.FirmId == op.FirmId && x.Id == intent.PbcRequestId &&
      x.ClientId == op.ClientId && x.EngagementId == op.EngagementId, ct);
    if (request.State != PbcStates.PartiallyReceived)
      throw new OperationBlockedException("pbc-request-state-conflict", authorization: true);

    var now = DateTimeOffset.UtcNow;
    intent.State = PbcUploadStates.Received;
    intent.ProviderRegisteredAt = now;
    intent.ProviderReceiptDigest = verifiedRemoteResult.Digest;
    intent.Revision++;
    request.State = PbcStates.Received;
    request.Revision++;
    request.UpdatedAt = now;
    db.OperationEvents.Add(new OperationEvent
    {
      Id = Guid.CreateVersion7(), OperationId = op.Id, Token = op.AttemptToken,
      Kind = "pbc.transfer.registered.v1", Executor = op.LeaseOwner!, OccurredAt = now
    });
    // The durable operation result records the verified provider registration identity and
    // content digest; the registration event evidence remains append-only.
    return new(verifiedRemoteResult.Identity, verifiedRemoteResult.Digest);
  }

  private static string RegistrationEvidence(PbcUploadIntent intent) => JsonSerializer.Serialize(new
  {
    intent.Id, intent.Revision, intent.State, intent.FinalSha256Hex,
    intent.ProviderReceiptDigest, registeredAt = intent.ProviderRegisteredAt
  });

  private async Task<PbcTransferPlan> LoadPlanAsync(
    DurableOperation op, PbcTransferPayload payload, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    var chunks = await db.PbcUploadChunks.AsNoTracking()
      .Where(x => x.FirmId == op.FirmId && x.PbcUploadIntentId == op.TargetId)
      .OrderBy(x => x.ChunkIndex).ToListAsync(ct);
    if (chunks.Count == 0 || chunks.Sum(x => (long)x.ByteCount) != payload.DeclaredByteCount ||
        chunks[0].Offset != 0)
      throw new OperationBlockedException("staged-evidence-incomplete");
    for (var i = 0; i < chunks.Count; i++)
    {
      if (chunks[i].ChunkIndex != i ||
          (i > 0 && chunks[i].Offset != chunks[i - 1].Offset + chunks[i - 1].ByteCount) ||
          string.IsNullOrWhiteSpace(chunks[i].StagedPath))
        throw new OperationBlockedException("staged-evidence-incomplete");
    }
    return new(op.FirmId, op.TargetId, payload.DeclaredByteCount, payload.FinalSha256Hex,
      chunks.Select(x => new PbcTransferChunk(x.ChunkIndex, x.Offset, x.ByteCount, x.Sha256Hex, x.StagedPath!)).ToArray());
  }

  private static async Task VerifyStagedBytesAsync(PbcTransferPlan plan, CancellationToken ct)
  {
    using var combined = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    var buffer = new byte[64 * 1024];
    long total = 0;
    foreach (var chunk in plan.Chunks)
    {
      if (!File.Exists(chunk.StagedPath))
        throw new OperationBlockedException("pbc-staged-bytes-missing");
      using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      await using var stream = new FileStream(chunk.StagedPath, FileMode.Open, FileAccess.Read,
        FileShare.Read, buffer.Length, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
      long count = 0;
      int read;
      while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
      {
        count += read;
        if (count > chunk.ByteCount)
          throw new OperationBlockedException("pbc-staged-bytes-conflict");
        digest.AppendData(buffer, 0, read);
        combined.AppendData(buffer, 0, read);
      }
      if (count != chunk.ByteCount ||
          !string.Equals(Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(),
            chunk.Sha256Hex, StringComparison.OrdinalIgnoreCase))
        throw new OperationBlockedException("pbc-staged-bytes-conflict");
      total += count;
    }
    if (total != plan.DeclaredByteCount ||
        !string.Equals(Convert.ToHexString(combined.GetHashAndReset()).ToLowerInvariant(),
          plan.FinalSha256Hex, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("pbc-staged-bytes-conflict");
  }

  private static PbcTransferPayload ReadPayload(string json, Guid expectedIntentId, long expectedRevision)
  {
    try
    {
      using var document = JsonDocument.Parse(json);
      var root = document.RootElement;
      if (!root.TryGetProperty("uploadIntentId", out var idElement) ||
          !root.TryGetProperty("intentRevision", out var revisionElement) ||
          !root.TryGetProperty("finalSha256Hex", out var hashElement) ||
          !root.TryGetProperty("declaredByteCount", out var byteElement) ||
          !idElement.TryGetGuid(out var id) || id != expectedIntentId ||
          !revisionElement.TryGetInt64(out var revision) || revision != expectedRevision ||
          hashElement.GetString() is not { Length: 64 } hash || !IsSha256(hash) ||
          !byteElement.TryGetInt64(out var bytes) || bytes <= 0)
        throw new OperationBlockedException("invalid-transfer-request");
      return new(id, revision, hash.ToLowerInvariant(), bytes);
    }
    catch (OperationBlockedException)
    {
      throw;
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("invalid-transfer-request");
    }
  }

  private static bool IsSha256(string? value) => value is { Length: 64 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

  private sealed record PbcTransferPayload(
    Guid UploadIntentId, long IntentRevision, string FinalSha256Hex, long DeclaredByteCount);
}

/// <summary>
/// Development/test-only simulated provider boundary. It persists transferred bytes under a
/// server-generated loopback path and verifies stored content on reconciliation. It is not a
/// live Microsoft adapter and records no tenant effect.
/// </summary>
public sealed class SimulationPbcProviderSink : IPbcProviderSink
{
  public string ProviderRoot { get; }

  public SimulationPbcProviderSink(string providerRoot)
  {
    ProviderRoot = providerRoot;
  }

  public async Task<PbcProviderReceipt> UploadAsync(PbcTransferPlan plan, CancellationToken ct)
  {
    Directory.CreateDirectory(ProviderRoot);
    var path = Path.Combine(ProviderRoot, plan.UploadIntentId.ToString("N"));
    await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
      bufferSize: 64 * 1024, options: FileOptions.Asynchronous | FileOptions.SequentialScan))
    {
      using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      var buffer = new byte[64 * 1024];
      long total = 0;
      foreach (var chunk in plan.Chunks)
      {
        await using var input = new FileStream(chunk.StagedPath, FileMode.Open, FileAccess.Read,
          FileShare.Read, buffer.Length, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
          await output.WriteAsync(buffer.AsMemory(0, read), ct);
          digest.AppendData(buffer, 0, read);
          total += read;
        }
      }
      await output.FlushAsync(ct);
      if (total != plan.DeclaredByteCount ||
          !string.Equals(Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(),
            plan.FinalSha256Hex, StringComparison.OrdinalIgnoreCase))
        throw new IOException("Simulated provider transfer failed the byte-count or digest verification.");
    }
    return new("sim://pbc/" + plan.UploadIntentId.ToString("D"),
      plan.FinalSha256Hex.ToLowerInvariant(), plan.DeclaredByteCount);
  }

  public Task<PbcProviderReceipt?> ProbeAsync(Guid uploadIntentId, CancellationToken ct) =>
    VerifyAsync("sim://pbc/" + uploadIntentId.ToString("D"), ct);

  public async Task<PbcProviderReceipt?> VerifyAsync(string identity, CancellationToken ct)
  {
    const string prefix = "sim://pbc/";
    if (!identity.StartsWith(prefix, StringComparison.Ordinal) ||
        !Guid.TryParseExact(identity[prefix.Length..], "D", out var intentId))
      throw new OperationBlockedException("provider-receipt-conflict");
    var path = Path.Combine(ProviderRoot, intentId.ToString("N"));
    if (!File.Exists(path)) return null;
    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    var buffer = new byte[64 * 1024];
    long total = 0;
    await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
      buffer.Length, options: FileOptions.Asynchronous | FileOptions.SequentialScan))
    {
      int read;
      while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
      {
        digest.AppendData(buffer, 0, read);
        total += read;
      }
    }
    return new(identity, Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(), total);
  }
}
