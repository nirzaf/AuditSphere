using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>A worker-side discovery that enqueues pending durable work for its slice.</summary>
public interface IPendingOperationDiscovery
{
  Task<int> EnqueuePendingAsync(CancellationToken ct);
}

/// <summary>
/// Safety net that enqueues a durable transfer for STAGED PBC upload intents whose
/// completion boundary recorded the staged bytes but whose durable operation row is not
/// active. Staged bytes without a queued or running transfer are re-queued exactly once per
/// intent revision through the deterministic idempotency key.
/// </summary>
public sealed class PbcTransferDiscovery(
  IAuditSphereDbContextFactory factory,
  IOperationStore store,
  PbcDocumentTransferHandler handler,
  WorkerOptions options) : IPendingOperationDiscovery
{
  public const int BatchSize = 25;

  public async Task<int> EnqueuePendingAsync(CancellationToken ct)
  {
    await using var read = await factory.CreateAsync(ct);
    var staged = await read.PbcUploadIntents.AsNoTracking()
      .Where(i => i.FirmId == options.FirmId && i.State == PbcUploadStates.Staged &&
        !read.DurableOperations.Any(o => o.FirmId == i.FirmId && o.TargetId == i.Id &&
          o.ExpectedRevision == i.Revision && o.OperationKind == PbcDocumentTransferHandler.Kind &&
          o.Status != OperationState.COMPLETED && o.Status != OperationState.DEAD_LETTER &&
          o.Status != OperationState.AUTHORIZATION_BLOCKED && o.Status != OperationState.PROVIDER_BLOCKED &&
          o.Status != OperationState.CANCELLED_WITH_DISPOSITION))
      .OrderBy(i => i.CompletedAt).ThenBy(i => i.Id).Take(BatchSize).ToListAsync(ct);
    var count = 0;
    foreach (var intent in staged)
    {
      if (intent.TransferOperationId is not null)
        continue; // A bound transfer operation already owns this intent.
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var payload = JsonSerializer.Serialize(new
      {
        uploadIntentId = intent.Id.ToString("D"),
        intentRevision = intent.Revision,
        finalSha256Hex = intent.DeclaredSha256Hex.ToLowerInvariant(),
        declaredByteCount = intent.DeclaredByteCount
      });
      var result = await store.EnqueueAsync(db, new OperationRequest(
        intent.FirmId, intent.ClientId, intent.EngagementId, PbcDocumentTransferHandler.Kind,
        intent.Id, intent.Revision, "pbc-transfer:" + intent.Id.ToString("D") + ":" +
          intent.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        payload, intent.UploaderUserId), handler, ct);
      if (!result.Succeeded) continue;
      var bound = await db.PbcUploadIntents.SingleAsync(x =>
        x.FirmId == intent.FirmId && x.Id == intent.Id, ct);
      if (bound.State != PbcUploadStates.Staged || bound.Revision != intent.Revision)
        continue; // Lost the race; the completion boundary owns the binding.
      bound.TransferOperationId = result.Value;
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      count++;
    }
    return count;
  }
}
