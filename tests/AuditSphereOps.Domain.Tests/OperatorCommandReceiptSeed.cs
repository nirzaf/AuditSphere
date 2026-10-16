using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Disposable synthetic operator decisions; no live provider acceptance.</summary>
internal static class OperatorCommandReceiptSeed
{
  internal sealed record World(PbcSeed.Fixture Fixture, Guid OperationId, OperationCommandInput Input)
  {
    internal AuditSphereOps.Application.Abstractions.ActorContext Actor => PbcSeed.Actor(Fixture.Admin, "Administrator");
  }

  internal static async Task<World> SeedAsync(ITestPostgresDatabase pg, string action, long deploymentEpoch = 1,
    long policyGeneration = 1, long expectedRevision = 1, long attemptToken = 0)
  {
    var f = await PbcSeed.SeedAsync(pg);
    var id = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.FirmSafetyStates.Where(x => x.Id == f.FirmId).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.DeploymentEpoch, deploymentEpoch).SetProperty(x => x.PolicyGeneration, policyGeneration));
    db.DurableOperations.Add(new()
    {
      Id = id, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      OperationKind = "SYNTHETIC-OPERATOR-RECEIPT", TargetId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
      IdempotencyKey = id.ToString("D"), ExpectedRevision = expectedRevision, AttemptToken = attemptToken,
      RequestDigest = new string('a', 64), RequestBytes = [1],
      Status = action == OperationCommandActions.Retry ? OperationState.RESULT_UNCERTAIN : OperationState.PENDING,
      NextAttemptAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    OperationCommandInput input;
    if (action == OperationCommandActions.ApproveRecoveryRestart)
    {
      var session = await OperationRecoveryService.BeginRecoverySessionAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new("Synthetic restore", 1, "Exact synthetic scope", "Pending synthetic findings"));
      Assert.True(session.Succeeded, session.Message);
      input = new(action, RecoverySessionId: session.Value, Findings: "Synthetic reconciled findings\r\nتفاصيل المراجعة");
    }
    else if (action == OperationCommandActions.LiftQuarantine)
    {
      await db.FirmSafetyStates.Where(x => x.Id == f.FirmId).ExecuteUpdateAsync(s => s.SetProperty(x => x.OperatingMode, "RECOVERY_QUARANTINE"));
      input = new(action);
    }
    else input = new(action, OperationId: id, Disposition: action == OperationCommandActions.Cancel ? "Synthetic exact cancellation\r\nسبب الإلغاء" : null);
    return new(f, id, input);
  }

  internal static async Task<OperationCommandRequest> ReviewAsync(AuditSphereDbContext db, World world)
  {
    var request = new OperationCommandRequest(Guid.NewGuid(), world.Input);
    var preview = await OperationCommandWorkspace.PreviewAsync(db, world.Actor, request);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.True(preview.Value!.CanProceed, string.Join(';', preview.Value.Blockers));
    return request with
    {
      Input = preview.Value.CanonicalInput, ReviewBasis = preview.Value.ReviewBasis,
      ExpectedRequestHash = preview.Value.RequestHash, Reviewed = true
    };
  }
}
