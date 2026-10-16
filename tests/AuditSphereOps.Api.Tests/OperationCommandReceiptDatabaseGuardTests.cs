using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class OperationCommandReceiptDatabaseGuardTests
{
  [Fact]
  public async Task SubstitutedRequestHashOrReviewBasisCannotPublish()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-OPERATOR-RECEIPT-INTEGRITY");
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.LiftQuarantine);
    await using var db = new AuditSphereDbContext(pg.Options);
    await AssertMigrationAppliedAsync(db);

    var request = await OperatorCommandReceiptSeed.ReviewAsync(db, world);
    var previewResult = await OperationCommandWorkspace.PreviewAsync(db, world.Actor, request);
    Assert.True(previewResult.Succeeded, previewResult.Message);
    var preview = previewResult.Value!;

    var wrongHash = Substitute(request.ExpectedRequestHash!);
    await using (var tx = await db.Database.BeginTransactionAsync())
    {
      var eventId = Guid.NewGuid();
      await SetPublicationAsync(db, world.Actor, request, request.Input, eventId, null, wrongHash);
      var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE firm_safety_states
        SET operating_mode = 'LOCAL_ONLY', deployment_epoch = deployment_epoch + 1
        WHERE id = {world.Actor.FirmId}
        """));
      Assert.Equal("55000", error.SqlState);
      Assert.Contains("Operator request hash must bind", error.MessageText);
      await tx.RollbackAsync();
    }
    await AssertNoPublicationAsync(db, world);

    await using (var tx = await db.Database.BeginTransactionAsync())
    {
      var eventId = Guid.NewGuid();
      await SetPublicationAsync(db, world.Actor, request, request.Input, eventId, null, request.ExpectedRequestHash!);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE firm_safety_states
        SET operating_mode = 'LOCAL_ONLY', deployment_epoch = deployment_epoch + 1
        WHERE id = {world.Actor.FirmId}
        """);

      var substitutedPreview = preview with { ReviewBasis = Substitute(preview.ReviewBasis) };
      var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO operator_command_receipts
          (id, firm_id, actor_id, actor_epoch, request_id, request_hash, action, event_id, input_json, preview_json, created_at)
        VALUES
          ({Guid.NewGuid()}, {world.Actor.FirmId}, {world.Actor.UserId}, {world.Actor.SessionEpoch},
           {request.RequestId}, {request.ExpectedRequestHash}, {request.Input.Action}, {eventId},
           {JsonSerializer.Serialize(request.Input)}, {JsonSerializer.Serialize(substitutedPreview)}, statement_timestamp())
        """));
      Assert.Equal("23514", error.SqlState);
      Assert.Contains("Receipt must bind", error.MessageText);
      await tx.RollbackAsync();
    }
    await AssertNoPublicationAsync(db, world);
  }

  [Theory]
  [InlineData("null")]
  [InlineData("blank")]
  [InlineData("oversized")]
  public async Task InvalidCancelDispositionIsRejectedByThePersistedTransitionGuard(string kind)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-OPERATOR-CANCEL-GUARD");
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.Cancel);
    await using var db = new AuditSphereDbContext(pg.Options);
    await AssertMigrationAppliedAsync(db);

    string? disposition = kind switch
    {
      "null" => null,
      "blank" => "   ",
      "oversized" => new string('x', 2001),
      _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    var request = new OperationCommandRequest(Guid.NewGuid(), world.Input with { Disposition = disposition });

    await using (var tx = await db.Database.BeginTransactionAsync())
    {
      await SetPublicationAsync(db, world.Actor, request, request.Input, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64));
      var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE durable_operations
        SET status = 'CANCELLED_WITH_DISPOSITION', error_code = 'operator-cancelled',
            cancellation_disposition = {disposition}, next_attempt_at = statement_timestamp()
        WHERE firm_id = {world.Actor.FirmId} AND id = {world.OperationId}
        """));
      var expectedSqlState = kind switch { "blank" or "null" => "23514", "oversized" => "22001", _ => "55000" };
      Assert.Equal(expectedSqlState, error.SqlState);
      await tx.RollbackAsync();
    }

    Assert.Equal(OperationState.PENDING,
      await db.DurableOperations.AsNoTracking().Where(x => x.Id == world.OperationId).Select(x => x.Status).SingleAsync());
    Assert.Empty(await db.OperatorCommandEvents.AsNoTracking().ToListAsync());
    Assert.Empty(await db.OperatorCommandReceipts.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task PersistedEventAndReceiptRejectUpdateDeleteAndTruncate()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-OPERATOR-RECEIPT-IMMUTABLE");
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.LiftQuarantine);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await AssertMigrationAppliedAsync(db);
      var request = await OperatorCommandReceiptSeed.ReviewAsync(db, world);
      var published = await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request);
      Assert.True(published.Succeeded, published.Message);
    }

    await using var probe = new AuditSphereDbContext(pg.Options);
    var receipt = await probe.OperatorCommandReceipts.AsNoTracking().SingleAsync();
    var commandEvent = await probe.OperatorCommandEvents.AsNoTracking().SingleAsync();
    const string emptyObjectJson = "{}";

    await AssertRefusedAsync(() => probe.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE operator_command_events SET request_hash = {Substitute(commandEvent.RequestHash)} WHERE id = {commandEvent.Id}
      """), "Operator events are immutable.");
    await AssertRefusedAsync(() => probe.Database.ExecuteSqlInterpolatedAsync($"""
      DELETE FROM operator_command_events WHERE id = {commandEvent.Id}
      """), "Operator events are immutable.");
    await AssertRefusedAsync(() => probe.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE operator_command_receipts SET preview_json = {emptyObjectJson} WHERE id = {receipt.Id}
      """), "Operator receipts are immutable.");
    await AssertRefusedAsync(() => probe.Database.ExecuteSqlInterpolatedAsync($"""
      DELETE FROM operator_command_receipts WHERE id = {receipt.Id}
      """), "Operator receipts are immutable.");

    await AssertRefusedAsync(() => probe.Database.ExecuteSqlRawAsync("TRUNCATE TABLE operator_command_receipts"), "Operator receipts are immutable.");
    await AssertRefusedAnyAsync(() => probe.Database.ExecuteSqlRawAsync("TRUNCATE TABLE operator_command_events CASCADE"),
      "Operator events are immutable.", "Operator receipts are immutable.");

    Assert.Single(await probe.OperatorCommandEvents.AsNoTracking().ToListAsync());
    Assert.Single(await probe.OperatorCommandReceipts.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task SourceTransitionCannotCommitWithoutItsAtomicReceipt()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-OPERATOR-RECEIPT-ATOMIC");
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.LiftQuarantine);
    await using var db = new AuditSphereDbContext(pg.Options);
    await AssertMigrationAppliedAsync(db);

    var request = await OperatorCommandReceiptSeed.ReviewAsync(db, world);
    var eventId = Guid.NewGuid();
    await using (var tx = await db.Database.BeginTransactionAsync())
    {
      await SetPublicationAsync(db, world.Actor, request, request.Input, eventId, null, request.ExpectedRequestHash!);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE firm_safety_states
        SET operating_mode = 'LOCAL_ONLY', deployment_epoch = deployment_epoch + 1
        WHERE id = {world.Actor.FirmId}
        """);

      Assert.Single(await db.OperatorCommandEvents.AsNoTracking().Where(x => x.Id == eventId).ToListAsync());
      Assert.Empty(await db.OperatorCommandReceipts.AsNoTracking().ToListAsync());
      var error = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
      Assert.Equal("23514", error.SqlState);
      Assert.Contains("Operator transition and original acknowledgement must commit atomically", error.MessageText);
    }

    await AssertNoPublicationAsync(db, world);
  }

  [Fact]
  public async Task OperatorOnlySourceTransitionsRequirePublicationMetadata()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-OPERATOR-METADATA-GUARD");
    var retryWorld = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.Retry);
    var cancelWorld = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.Cancel);
    var restartWorld = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.ApproveRecoveryRestart);
    var liftWorld = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.LiftQuarantine);
    await using var db = new AuditSphereDbContext(pg.Options);
    await AssertMigrationAppliedAsync(db);

    await AssertPublicationMetadataRequiredAsync(db, () => db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE durable_operations
      SET status = 'RETRY_WAIT'
      WHERE firm_id = {retryWorld.Actor.FirmId} AND id = {retryWorld.OperationId}
      """), "Operator retry and cancellation require preview-bound receipt evidence.");

    await AssertPublicationMetadataRequiredAsync(db, () => db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE durable_operations
      SET status = 'CANCELLED_WITH_DISPOSITION', error_code = 'operator-cancelled', cancellation_disposition = 'Synthetic cancellation'
      WHERE firm_id = {cancelWorld.Actor.FirmId} AND id = {cancelWorld.OperationId}
      """), "Operator retry and cancellation require preview-bound receipt evidence.");

    var recoverySessionId = restartWorld.Input.RecoverySessionId!.Value;
    await AssertPublicationMetadataRequiredAsync(db, () => db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE recovery_sessions
      SET approved_restart_at = statement_timestamp(), approved_by_user_id = {restartWorld.Actor.UserId}
      WHERE firm_id = {restartWorld.Actor.FirmId} AND id = {recoverySessionId}
      """), "Recovery restart approval requires preview-bound receipt evidence.");

    await AssertPublicationMetadataRequiredAsync(db, () => db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE firm_safety_states
      SET operating_mode = 'LOCAL_ONLY', deployment_epoch = deployment_epoch + 1
      WHERE id = {liftWorld.Actor.FirmId}
      """), "Quarantine lift requires preview-bound receipt evidence.");

    Assert.Equal(OperationState.RESULT_UNCERTAIN,
      await db.DurableOperations.AsNoTracking().Where(x => x.Id == retryWorld.OperationId).Select(x => x.Status).SingleAsync());
    var retainedSession = await db.RecoverySessions.AsNoTracking().SingleAsync(x => x.Id == recoverySessionId);
    Assert.Null(retainedSession.ApprovedRestartAt);
    Assert.Null(retainedSession.ApprovedByUserId);
    var retainedSafetyState = await db.FirmSafetyStates.AsNoTracking().SingleAsync(x => x.Id == liftWorld.Actor.FirmId);
    Assert.Equal("RECOVERY_QUARANTINE", retainedSafetyState.OperatingMode);
    Assert.Equal(1L, retainedSafetyState.DeploymentEpoch);
    Assert.Empty(await db.OperatorCommandEvents.AsNoTracking().ToListAsync());
    Assert.Empty(await db.OperatorCommandReceipts.AsNoTracking().ToListAsync());
  }

  private static async Task AssertMigrationAppliedAsync(AuditSphereDbContext db)
  {
    var migration = Assert.Single(db.Database.GetMigrations(), x => x.EndsWith("_OperatorCommandReceipts", StringComparison.Ordinal));
    Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
  }

  private static async Task AssertPublicationMetadataRequiredAsync(AuditSphereDbContext db, Func<Task<int>> attempt,
    string expectedMessage)
  {
    await using var tx = await db.Database.BeginTransactionAsync();
    var error = await Assert.ThrowsAsync<PostgresException>(attempt);
    Assert.Equal("55000", error.SqlState);
    Assert.Equal(expectedMessage, error.MessageText);
    await tx.RollbackAsync();
  }

  private static async Task AssertNoPublicationAsync(AuditSphereDbContext db, OperatorCommandReceiptSeed.World world)
  {
    Assert.Equal("RECOVERY_QUARANTINE", await db.FirmSafetyStates.AsNoTracking().Where(x => x.Id == world.Actor.FirmId).Select(x => x.OperatingMode).SingleAsync());
    Assert.Empty(await db.OperatorCommandEvents.AsNoTracking().ToListAsync());
    Assert.Empty(await db.OperatorCommandReceipts.AsNoTracking().ToListAsync());
  }

  private static Task<int> SetPublicationAsync(AuditSphereDbContext db, ActorContext actor, OperationCommandRequest request,
    OperationCommandInput input, Guid eventId, Guid? sourceEventId, string hash)
  {
    var eventText = eventId.ToString("D");
    var sourceText = sourceEventId?.ToString("D") ?? "";
    var requestText = request.RequestId.ToString("D");
    var actorText = actor.UserId.ToString("D");
    var epochText = actor.SessionEpoch.ToString(CultureInfo.InvariantCulture);
    var operationText = input.OperationId?.ToString("D") ?? "";
    var sessionText = input.RecoverySessionId?.ToString("D") ?? "";
    var inputJson = JsonSerializer.Serialize(input);
    return db.Database.ExecuteSqlInterpolatedAsync($"""
      SELECT set_config('audit.operator_event_id', {eventText}, true),
             set_config('audit.operator_source_id', {sourceText}, true),
             set_config('audit.operator_request_id', {requestText}, true),
             set_config('audit.operator_actor_id', {actorText}, true),
             set_config('audit.operator_actor_epoch', {epochText}, true),
             set_config('audit.operator_operation_id', {operationText}, true),
             set_config('audit.operator_session_id', {sessionText}, true),
             set_config('audit.operator_hash', {hash}, true),
             set_config('audit.operator_action', {input.Action}, true),
             set_config('audit.operator_input', {inputJson}, true)
      """);
  }

  private static string Substitute(string value)
    => (value[0] == '0' ? '1' : '0') + value[1..];

  private static async Task AssertRefusedAsync(Func<Task<int>> attempt, string expectedMessage)
  {
    var error = await Assert.ThrowsAsync<PostgresException>(attempt);
    Assert.Equal("55000", error.SqlState);
    Assert.Equal(expectedMessage, error.MessageText);
  }

  private static async Task AssertRefusedAnyAsync(Func<Task<int>> attempt, params string[] expectedMessages)
  {
    var error = await Assert.ThrowsAsync<PostgresException>(attempt);
    Assert.Equal("55000", error.SqlState);
    Assert.Contains(error.MessageText, expectedMessages);
  }
}
