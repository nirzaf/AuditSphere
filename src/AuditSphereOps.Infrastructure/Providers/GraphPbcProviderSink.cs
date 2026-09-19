using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// Deliberately non-live provider boundary. A real Graph adapter requires an approved
/// binding, credential source and tenant acceptance evidence before it may be implemented.
/// </summary>
public sealed class GraphPbcProviderSink : IPbcProviderSink
{
  private static OperationBlockedException Block() => new("live-provider-not-approved");

  public Task<PbcProviderReceipt> UploadAsync(PbcTransferPlan plan, CancellationToken ct) =>
    Task.FromException<PbcProviderReceipt>(Block());

  public Task<PbcProviderReceipt?> VerifyAsync(string identity, CancellationToken ct) =>
    Task.FromException<PbcProviderReceipt?>(Block());

  public Task<PbcProviderReceipt?> ProbeAsync(Guid uploadIntentId, CancellationToken ct) =>
    Task.FromException<PbcProviderReceipt?>(Block());
}
