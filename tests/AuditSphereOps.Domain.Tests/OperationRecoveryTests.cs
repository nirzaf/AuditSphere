using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class OperationRecoveryTests
{
  [Fact]
  public async Task QueuedOperation_CanBeCancelledWithoutPublishing()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var harness = await PbcSeed.CreateTransferHarnessAsync(pg);
    var admin = PbcSeed.Actor(harness.Fixture.Admin, "Administrator");
    var operationId = await SingleOperationIdAsync(pg);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var cancelled = await OperationRecoveryService.CancelAsync(db, admin,
        new OperationCancellationRequest(operationId, "Operator stopped the queued transfer before worker claim."));
      Assert.True(cancelled.Succeeded, cancelled.ErrorCode);
      var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.CANCELLED_WITH_DISPOSITION, operation.Status);
      Assert.Equal("operator-cancelled", operation.ErrorCode);
      Assert.Equal("Operator stopped the queued transfer before worker claim.", operation.CancellationDisposition);
      Assert.True(await db.OperationEvents.AsNoTracking().AnyAsync(x =>
        x.OperationId == operationId && x.Kind == "operation.cancelled.v1"));
    }

    Assert.False(await harness.Worker.ProcessNextAsync());
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var intent = await verify.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == harness.Staged.UploadIntentId);
      Assert.Equal(PbcUploadStates.Staged, intent.State);
      var refused = await OperationRecoveryService.RetryAsync(verify, admin,
        new OperationRecoveryRequest(operationId));
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.Operations.State, refused.ErrorCode);
    }
    PbcSeed.DeleteDirectory(harness.Staged.StagingRoot);
  }

  [Fact]
  public async Task BlockedTransfer_RecoveredByAdministrator_CompletesOnRetry()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scripted = new PbcSeed.ScriptedSink();
    var harness = await PbcSeed.CreateTransferHarnessAsync(pg, scripted);
    scripted.OnUpload(_ => throw new OperationBlockedException("simulated-provider-rejection"));

    Assert.True(await harness.Worker.ProcessNextAsync());
    var admin = PbcSeed.Actor(harness.Fixture.Admin, "Administrator");
    var staff = PbcSeed.Actor(harness.Fixture.Staff, "Staff");
    var operationId = await SingleOperationIdAsync(pg);
    await using (var mid = new AuditSphereDbContext(pg.Options))
      Assert.Equal(OperationState.PROVIDER_BLOCKED,
        (await mid.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == operationId)).Status);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var denied = await OperationRecoveryService.RetryAsync(db, staff,
        new OperationRecoveryRequest(operationId));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rearmed = await OperationRecoveryService.RetryAsync(db, admin,
        new OperationRecoveryRequest(operationId));
      Assert.True(rearmed.Succeeded, rearmed.ErrorCode);
      var op = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.RETRY_WAIT, op.Status);
      Assert.Equal(0, op.AttemptCount);
      Assert.True(await db.OperationEvents.AsNoTracking().AnyAsync(e =>
        e.OperationId == operationId && e.Kind == "operation.recovery-retry.v1"));
    }

    await using (var mid = new AuditSphereDbContext(pg.Options))
    {
      await mid.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE durable_operations SET next_attempt_at = statement_timestamp() WHERE id = {operationId}");
    }

    scripted.OnUpload(plan => scripted.Inner!.UploadAsync(plan, CancellationToken.None));
    Assert.True(await harness.Worker.ProcessNextAsync());
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var intent = await verify.PbcUploadIntents.AsNoTracking()
        .SingleAsync(x => x.Id == harness.Staged.UploadIntentId);
      Assert.Equal(PbcUploadStates.Received, intent.State);
      var request = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == harness.RequestId);
      Assert.Equal(PbcStates.Received, request.State);
    }
    PbcSeed.DeleteDirectory(harness.Staged.StagingRoot);
  }

  [Fact]
  public async Task CompletedOperation_IsNeverReArmed_AndListIsRedacted()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var harness = await PbcSeed.CreateTransferHarnessAsync(pg);
    Assert.True(await harness.Worker.ProcessNextAsync());
    var admin = PbcSeed.Actor(harness.Fixture.Admin, "Administrator");
    var operationId = await SingleOperationIdAsync(pg);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var refused = await OperationRecoveryService.RetryAsync(db, admin,
        new OperationRecoveryRequest(operationId));
      Assert.False(refused.Succeeded);
      Assert.Equal("operations.state", refused.ErrorCode);
      var listed = await OperationRecoveryService.ListAsync(db, admin);
      Assert.True(listed.Succeeded);
      var projection = listed.Value!.Single(x => x.Id == operationId);
      Assert.Equal("COMPLETED", projection.Status);
      Assert.True(projection.HasResultEvidence);
    }
    PbcSeed.DeleteDirectory(harness.Staged.StagingRoot);
  }

  [Fact]
  public async Task DeadLetterOperation_RetriedWithFreshAttemptBudget()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var harness = await PbcSeed.CreateTransferHarnessAsync(pg);
    var admin = PbcSeed.Actor(harness.Fixture.Admin, "Administrator");
    var operationId = await SingleOperationIdAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Test-only forcing: the dead-letter state normally arrives after durable retry exhaustion.
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE durable_operations SET status = 'DEAD_LETTER' WHERE id = {operationId}");
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rearmed = await OperationRecoveryService.RetryAsync(db, admin,
        new OperationRecoveryRequest(operationId));
      Assert.True(rearmed.Succeeded, rearmed.ErrorCode);
      var op = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.RETRY_WAIT, op.Status);
      Assert.Equal(0, op.AttemptCount);
      Assert.Null(op.ErrorCode);
    }
    PbcSeed.DeleteDirectory(harness.Staged.StagingRoot);
  }

  [Fact]
  public async Task QuarantinedFirm_BlocksRetry_UntilQuarantineLiftedByAdministrator()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var harness = await PbcSeed.CreateTransferHarnessAsync(pg);
    var admin = PbcSeed.Actor(harness.Fixture.Admin, "Administrator");
    var staff = PbcSeed.Actor(harness.Fixture.Staff, "Staff");
    var operationId = await SingleOperationIdAsync(pg);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE firm_safety_states SET operating_mode = 'RECOVERY_QUARANTINE' WHERE id = {harness.Fixture.FirmId}");
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE durable_operations SET status = 'PROVIDER_BLOCKED' WHERE id = {operationId}");
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var mode = await OperationRecoveryService.GetFirmOperatingModeAsync(db, admin);
      Assert.True(mode.Succeeded);
      Assert.Equal("RECOVERY_QUARANTINE", mode.Value);

      // Retry blocked under quarantine
      var blocked = await OperationRecoveryService.RetryAsync(db, admin, new OperationRecoveryRequest(operationId));
      Assert.False(blocked.Succeeded);
      Assert.Equal("operations.quarantined", blocked.ErrorCode);

      // Staff cannot lift quarantine
      var staffDenied = await OperationRecoveryService.LiftQuarantineAsync(db, staff);
      Assert.False(staffDenied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, staffDenied.ErrorCode);

      // Admin lifts quarantine
      var lifted = await OperationRecoveryService.LiftQuarantineAsync(db, admin);
      Assert.True(lifted.Succeeded, lifted.ErrorCode);

      var safety = await db.FirmSafetyStates.AsNoTracking().SingleAsync(x => x.Id == harness.Fixture.FirmId);
      Assert.Equal("LOCAL_ONLY", safety.OperatingMode);
      Assert.Equal(2, safety.DeploymentEpoch);

      // Now retry succeeds
      var rearmed = await OperationRecoveryService.RetryAsync(db, admin, new OperationRecoveryRequest(operationId));
      Assert.True(rearmed.Succeeded, rearmed.ErrorCode);
    }
    PbcSeed.DeleteDirectory(harness.Staged.StagingRoot);
  }

  [Fact]
  public async Task RecoverySession_RequiresPersistedApprovalBeforeRestart()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var harness = await PbcSeed.CreateTransferHarnessAsync(pg);
    var admin = PbcSeed.Actor(harness.Fixture.Admin, "Administrator");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var started = await OperationRecoveryService.BeginRecoverySessionAsync(db, admin,
        new RecoverySessionRequest("restore-20260919", 7, "release-checkpoints-and-pending-effects",
          "Checkpoint comparison required before restart."));
      Assert.True(started.Succeeded, started.ErrorCode);
      Assert.Equal("RECOVERY_QUARANTINE", (await db.FirmSafetyStates.AsNoTracking().SingleAsync()).OperatingMode);
      Assert.Equal(7, (await db.FirmSafetyStates.AsNoTracking().SingleAsync()).RecoveryEpoch);

      var approved = await OperationRecoveryService.ApproveRecoveryRestartAsync(db, admin,
        new RecoveryRestartRequest(started.Value!, "Counts and digests reconciled; no replay permitted."));
      Assert.True(approved.Succeeded, approved.ErrorCode);
      var safety = await db.FirmSafetyStates.AsNoTracking().SingleAsync();
      Assert.Equal("LOCAL_ONLY", safety.OperatingMode);
      Assert.Equal(2, safety.DeploymentEpoch);
      Assert.True(await db.RecoverySessions.AsNoTracking().AnyAsync(x =>
        x.Id == started.Value && x.ApprovedByUserId == harness.Fixture.Admin.Id));
    }
    PbcSeed.DeleteDirectory(harness.Staged.StagingRoot);
  }

  [Fact]
  public async Task CrossFirmOperation_IsNondisclosingDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var harness = await PbcSeed.CreateTransferHarnessAsync(pg);
    var other = await PbcSeed.CreateTransferHarnessAsync(pg);
    var admin = PbcSeed.Actor(harness.Fixture.Admin, "Administrator");
    Guid otherOperationId;
    await using (var otherDb = new AuditSphereDbContext(pg.Options))
    {
      var otherOp = await otherDb.DurableOperations.AsNoTracking()
        .SingleAsync(x => x.FirmId == other.Fixture.FirmId);
      otherOperationId = otherOp.Id;
    }
    await using var db = new AuditSphereDbContext(pg.Options);
    var denied = await OperationRecoveryService.RetryAsync(db, admin,
      new OperationRecoveryRequest(otherOperationId));
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    PbcSeed.DeleteDirectory(harness.Staged.StagingRoot);
    PbcSeed.DeleteDirectory(other.Staged.StagingRoot);
  }

  [Fact]
  public async Task ClientScopedAdministrator_CannotListFirmRecoveryOperations()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var harness = await PbcSeed.CreateTransferHarnessAsync(pg);
    var scopedAdmin = PbcSeed.User(harness.Fixture.FirmId, "Staff");

    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.Users.Add(scopedAdmin);
      seed.RoleGrants.Add(PbcSeed.Grant(harness.Fixture.FirmId, scopedAdmin, "Administrator",
        harness.Fixture.ClientId, harness.Fixture.EngagementId));
      await seed.SaveChangesAsync();
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await OperationRecoveryService.ListAsync(db,
      PbcSeed.Actor(scopedAdmin, "Administrator"));

    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    PbcSeed.DeleteDirectory(harness.Staged.StagingRoot);
  }

  private static async Task<Guid> SingleOperationIdAsync(PgTestSchema pg)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var operations = await db.DurableOperations.AsNoTracking()
      .Where(x => x.OperationKind == PbcDocumentTransferHandler.Kind).ToListAsync();
    Assert.Single(operations);
    return operations[0].Id;
  }
}
