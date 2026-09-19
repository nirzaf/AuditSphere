using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

public sealed class ProviderBoundaryTests
{
  [Fact]
  public async Task GraphBoundariesFailClosedBeforeAnyExternalEffect()
  {
    var pbc = new GraphPbcProviderSink();
    var pbcError = await Assert.ThrowsAsync<OperationBlockedException>(() => pbc.UploadAsync(
      new PbcTransferPlan(Guid.NewGuid(), Guid.NewGuid(), 0, new string('a', 64), []),
      CancellationToken.None));
    Assert.Equal("live-provider-not-approved", pbcError.Code);

    var checkpoints = new GraphReleaseCheckpointStore();
    var checkpointError = await Assert.ThrowsAsync<OperationBlockedException>(() => checkpoints.ProbeAsync(
      new string('a', 64), CancellationToken.None));
    Assert.Equal("live-provider-not-approved", checkpointError.Code);
  }
}
