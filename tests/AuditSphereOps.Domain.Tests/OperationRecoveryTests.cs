using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
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
      var cancellation = await ReviewedCommandAsync(db, harness.Fixture,
        new(OperationCommandActions.Cancel, OperationId: operationId,
          Disposition: "Operator stopped the queued transfer before worker claim."));
      var cancelled = await OperationCommandWorkspace.ExecuteAsync(db, admin, cancellation);
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
      var retry = await ReviewedCommandAsync(verify, admin,
        new(OperationCommandActions.Retry, OperationId: operationId), expectedCanProceed: false);
      var refused = await OperationCommandWorkspace.ExecuteAsync(verify, admin, retry);
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
      var denied = await OperationCommandWorkspace.PreviewAsync(db, staff,
        new(Guid.NewGuid(), new(OperationCommandActions.Retry, OperationId: operationId)));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var retry = await ReviewedCommandAsync(db, harness.Fixture,
        new(OperationCommandActions.Retry, OperationId: operationId));
      var rearmed = await OperationCommandWorkspace.ExecuteAsync(db, admin, retry);
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
      var retry = await ReviewedCommandAsync(db, admin,
        new(OperationCommandActions.Retry, OperationId: operationId), expectedCanProceed: false);
      var refused = await OperationCommandWorkspace.ExecuteAsync(db, admin, retry);
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.Operations.State, refused.ErrorCode);
      var listed = await OperationRecoveryService.ListAsync(db, admin);
      Assert.True(listed.Succeeded);
      var projection = listed.Value!.Single(x => x.Id == operationId);
      Assert.Equal("COMPLETED", projection.Status);
      Assert.True(projection.HasResultEvidence);

      var discovered = await OperationRecoveryService.DiscoverAsync(db, admin,
        new(OperationId: operationId));
      Assert.True(discovered.Succeeded, discovered.Message);
      Assert.Equal(operationId, discovered.Value!.OperationId);
      Assert.Equal(operationId, Assert.Single(discovered.Value.Operations).Id);

      var otherFirm = admin with { FirmId = Guid.NewGuid() };
      var concealed = await OperationRecoveryService.DiscoverAsync(db, otherFirm,
        new(OperationId: operationId));
      Assert.False(concealed.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, concealed.ErrorCode);
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
      var retry = await ReviewedCommandAsync(db, harness.Fixture,
        new(OperationCommandActions.Retry, OperationId: operationId));
      var rearmed = await OperationCommandWorkspace.ExecuteAsync(db, admin, retry);
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
      var retry = await ReviewedCommandAsync(db, admin,
        new(OperationCommandActions.Retry, OperationId: operationId), expectedCanProceed: false);
      var blocked = await OperationCommandWorkspace.ExecuteAsync(db, admin, retry);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.Operations.State, blocked.ErrorCode);

      // Staff cannot lift quarantine
      var staffDenied = await OperationCommandWorkspace.PreviewAsync(db, staff,
        new(Guid.NewGuid(), new(OperationCommandActions.LiftQuarantine, RecoverySessionId: Guid.NewGuid())));
      Assert.False(staffDenied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, staffDenied.ErrorCode);

      // Admin lifts quarantine
      var lift = await ReviewedCommandAsync(db, harness.Fixture,
        new(OperationCommandActions.LiftQuarantine));
      var lifted = await OperationCommandWorkspace.ExecuteAsync(db, admin, lift);
      Assert.True(lifted.Succeeded, lifted.ErrorCode);

      var safety = await db.FirmSafetyStates.AsNoTracking().SingleAsync(x => x.Id == harness.Fixture.FirmId);
      Assert.Equal("LOCAL_ONLY", safety.OperatingMode);
      Assert.Equal(2, safety.DeploymentEpoch);

      // Now retry succeeds
      var retryAfterLift = await ReviewedCommandAsync(db, harness.Fixture,
        new(OperationCommandActions.Retry, OperationId: operationId));
      var rearmed = await OperationCommandWorkspace.ExecuteAsync(db, admin, retryAfterLift);
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

      var approval = await ReviewedCommandAsync(db, harness.Fixture,
        new(OperationCommandActions.ApproveRecoveryRestart, RecoverySessionId: started.Value!,
          Findings: "Counts and digests reconciled; no replay permitted."));
      var approved = await OperationCommandWorkspace.ExecuteAsync(db, admin, approval);
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
    var denied = await OperationCommandWorkspace.PreviewAsync(db, admin,
      new(Guid.NewGuid(), new(OperationCommandActions.Retry, OperationId: otherOperationId)));
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

  private static async Task<OperationCommandRequest> ReviewedCommandAsync(
    AuditSphereDbContext db, ActorContext actor, OperationCommandInput input, bool expectedCanProceed = true)
  {
    var request = new OperationCommandRequest(Guid.NewGuid(), input);
    var preview = await OperationCommandWorkspace.PreviewAsync(db, actor, request);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.Equal(expectedCanProceed, preview.Value!.CanProceed);
    return request with
    {
      Input = preview.Value.CanonicalInput,
      ReviewBasis = preview.Value.ReviewBasis,
      ExpectedRequestHash = preview.Value.RequestHash,
      Reviewed = true
    };
  }

  private static Task<OperationCommandRequest> ReviewedCommandAsync(
    AuditSphereDbContext db, PbcSeed.Fixture fixture, OperationCommandInput input) =>
    OperatorCommandReceiptSeed.ReviewAsync(db, new(fixture, Guid.Empty, input));
}
