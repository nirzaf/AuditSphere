using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// Deliberately non-live checkpoint boundary. Local append-only storage remains the only
/// development implementation until external custody and recovery evidence are approved.
/// </summary>
public sealed class GraphReleaseCheckpointStore : IReleaseCheckpointStore
{
  private static OperationBlockedException Block() => new("live-provider-not-approved");

  public Task<CheckpointReceipt> WriteAsync(string manifestDigest, byte[] manifestBytes, CancellationToken ct) =>
    Task.FromException<CheckpointReceipt>(Block());

  public Task<CheckpointReceipt?> VerifyAsync(string reference, string expectedDigest, CancellationToken ct) =>
    Task.FromException<CheckpointReceipt?>(Block());

  public Task<CheckpointReceipt?> ProbeAsync(string manifestDigest, CancellationToken ct) =>
    Task.FromException<CheckpointReceipt?>(Block());
}
