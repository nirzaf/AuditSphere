using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class ReleaseTests
{
  [Fact]
  public async Task ReleaseGate_RejectsMissingCheckpointAndWrongManifest_ThenIsIdempotent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("release-test-manifest-payload-a");
    var digest = Hashing.Sha256Hex(manifestBytes);
    var checkpointDir = Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N"));
    var runtime = ReleaseCheckpointRuntimeOptions.Resolve(checkpointDir, "Acceptance", enabled: true);
    var store = new LocalAppendOnlyCheckpointStore(runtime.RootDirectory);
    var failAfterCheckpointWrite = new LoseFirstCheckpointReadBackStore(store);
    var operationStore = new PostgresOperationStore(
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)));
    var handler = new ReleaseCheckpointHandler(failAfterCheckpointWrite);
    Guid candidateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approval = await ApprovalService.CreateAsync(db, fixture.Reviewer,
        new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(approval.Succeeded);

      var candidate = await ReleaseService.CreateCandidateAsync(db, fixture.Partner,
        new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(candidate.Succeeded);
      candidateId = candidate.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // 1. Blocked when external checkpoint is absent
      var blocked = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-001"));
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      Assert.Empty(await db.Releases.ToListAsync());

      // 2. Mismatched manifest rejected
      var wrongManifest = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, new string('b', 64), "release-002"));
      Assert.False(wrongManifest.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, wrongManifest.ErrorCode);
      Assert.Empty(await db.Releases.ToListAsync());

      // The supported API path remains off unless its isolated checkpoint store is explicitly configured.
      var disabledRuntime = ReleaseCheckpointRuntimeOptions.Resolve(null, "Development", enabled: false);
      var disabled = await ReleaseCheckpointService.EnqueueCheckpointAsync(db, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "release-001", digest, manifestBytes), disabledRuntime,
        operationStore, handler);
      Assert.False(disabled.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, disabled.ErrorCode);
      Assert.Empty(await db.DurableOperations.Where(x => x.OperationKind == ReleaseCheckpointHandler.Kind).ToListAsync());

      // 4. Enqueue the exact checkpoint intent for the isolated live worker group.
      var enqueued = await ReleaseCheckpointService.EnqueueCheckpointAsync(db, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "release-001", digest, manifestBytes), runtime,
        operationStore, handler);
      Assert.True(enqueued.Succeeded, enqueued.Message);
      var checkpointOperation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == enqueued.Value);
      Assert.Equal("RecordReleaseCheckpoint.v1", checkpointOperation.OperationKind);
      Assert.Equal(OperationState.PENDING, checkpointOperation.Status);
      Assert.Equal(ReleaseCheckpointHandler.WorkerGroup, checkpointOperation.ExecutionGroup);
      Assert.Equal(candidateId, checkpointOperation.TargetId);
      var repeatedEnqueue = await ReleaseCheckpointService.EnqueueCheckpointAsync(db, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, " release-001 ", digest, manifestBytes), runtime,
        operationStore, handler);
      Assert.True(repeatedEnqueue.Succeeded, repeatedEnqueue.Message);
      Assert.Equal(enqueued.Value, repeatedEnqueue.Value);
      var wrongKeyEnqueue = await ReleaseCheckpointService.EnqueueCheckpointAsync(db, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "release-002", digest, manifestBytes), runtime,
        operationStore, handler);
      Assert.False(wrongKeyEnqueue.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, wrongKeyEnqueue.ErrorCode);
      Assert.DoesNotContain("release-001", wrongKeyEnqueue.Message, StringComparison.Ordinal);
      Assert.DoesNotContain("release-002", wrongKeyEnqueue.Message, StringComparison.Ordinal);
      var checkpointOperations = await db.DurableOperations.AsNoTracking()
        .Where(x => x.OperationKind == "RecordReleaseCheckpoint.v1" && x.TargetId == candidateId).ToListAsync();
      Assert.Single(checkpointOperations);
      Assert.Equal(OperationState.PENDING, checkpointOperations[0].Status);
      using (var checkpointPayload = System.Text.Json.JsonDocument.Parse(checkpointOperations[0].PayloadJson))
        Assert.Equal("release-001", checkpointPayload.RootElement.GetProperty("authorizedReleaseKey").GetString());

      var candidateScope = await db.ReleaseCandidates.AsNoTracking().SingleAsync(x => x.Id == candidateId);
      var wrongGroupOptions = new WorkerOptions(fixture.FirmId, "Acceptance", ExternalEffectsEnabled: true, Group: "general");
      var wrongGroupDefinition = new OperationDefinition(ReleaseCheckpointHandler.Kind, OperationMode.LIVE,
        OperationAuthority.LIVE_PROVIDER, SchemaVersion: 1, Group: "general");
      var wrongGroupClaim = await operationStore.ClaimAsync(wrongGroupOptions, [wrongGroupDefinition],
        "release-checkpoint-wrong-group-test", reconciliation: false, CancellationToken.None);
      Assert.Null(wrongGroupClaim);

      var workerOptions = new WorkerOptions(fixture.FirmId, "Acceptance", ExternalEffectsEnabled: true,
        Group: ReleaseCheckpointHandler.WorkerGroup);
      var dispatcher = new OperationDispatcher(operationStore, new DurableOperationRegistry([handler], workerOptions), workerOptions);
      Assert.True(await dispatcher.ProcessNextAsync());
      db.ChangeTracker.Clear();
      var uncertainOperation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == enqueued.Value);
      Assert.Equal(OperationState.RESULT_UNCERTAIN, uncertainOperation.Status);
      Assert.Equal(OperationMode.LIVE, uncertainOperation.ExecutionMode);
      Assert.Equal(OperationAuthority.LIVE_PROVIDER, uncertainOperation.AuthorityMode);
      Assert.Equal(ReleaseCheckpointHandler.WorkerGroup, uncertainOperation.ExecutionGroup);
      Assert.Equal(digest, (await store.ProbeAsync(digest))?.ContentSha256Hex);

      // The store write succeeded but its first acknowledgement was lost; reconciliation probes the same content identity.
      await Task.Delay(TimeSpan.FromSeconds(5.2)); // Respect the dispatcher's fail-closed uncertainty backoff.
      Assert.True(await dispatcher.ProcessNextAsync());
      db.ChangeTracker.Clear();
      var reconciledOperation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == enqueued.Value);
      Assert.Equal(OperationState.COMPLETED, reconciledOperation.Status);
      Assert.Equal(uncertainOperation.Id, reconciledOperation.Id);
      var checkpointBeforeWorkerRefresh = await db.ReleaseCheckpoints.AsNoTracking()
        .SingleAsync(x => x.ReleaseCandidateId == candidateId);
      Assert.Equal(digest, (await store.VerifyAsync(checkpointBeforeWorkerRefresh.StoredReference, digest))?.ContentSha256Hex);
      Assert.Equal(checkpointBeforeWorkerRefresh.Id, (await db.ReleaseCheckpoints.AsNoTracking()
        .SingleAsync(x => x.ReleaseCandidateId == candidateId)).Id);

      var wrongKeyWorkerOperation = new DurableOperation
      {
        FirmId = fixture.FirmId,
        ClientId = candidateScope.ClientId,
        EngagementId = candidateScope.EngagementId,
        OperationKind = ReleaseCheckpointHandler.Kind,
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
          candidateId,
          candidateRevision = 1,
          manifestDigest = digest,
          authorizedReleaseKey = "release-002",
          manifestBytesBase64 = Convert.ToBase64String(manifestBytes)
        }),
        TargetId = candidateId,
        ExpectedRevision = 1
      };
      var workerKeyConflict = await Assert.ThrowsAsync<OperationBlockedException>(() =>
        handler.PublishAsync(db, wrongKeyWorkerOperation,
          new OperationResult("synthetic://checkpoint/wrong-key", digest), CancellationToken.None));
      Assert.Equal("checkpoint-release-key-conflict", workerKeyConflict.Code);
      db.ChangeTracker.Clear();
      var checkpointAfterWorkerRefresh = await db.ReleaseCheckpoints.AsNoTracking()
        .SingleAsync(x => x.ReleaseCandidateId == candidateId);
      Assert.Equal(checkpointBeforeWorkerRefresh.StoredReference, checkpointAfterWorkerRefresh.StoredReference);
      Assert.Equal(checkpointBeforeWorkerRefresh.ReadBackDigest, checkpointAfterWorkerRefresh.ReadBackDigest);
      Assert.Equal(checkpointBeforeWorkerRefresh.VerifiedAt, checkpointAfterWorkerRefresh.VerifiedAt);
      Assert.Equal(checkpointBeforeWorkerRefresh.Verifier, checkpointAfterWorkerRefresh.Verifier);

      // A different key cannot reuse the K1 checkpoint or refresh it.
      var wrongKeyIssue = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-002"));
      Assert.False(wrongKeyIssue.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, wrongKeyIssue.ErrorCode);
      Assert.DoesNotContain("release-001", wrongKeyIssue.Message, StringComparison.Ordinal);
      Assert.DoesNotContain("release-002", wrongKeyIssue.Message, StringComparison.Ordinal);
      var wrongKeyCheckpoint = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, store, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "release-002", digest, manifestBytes));
      Assert.False(wrongKeyCheckpoint.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, wrongKeyCheckpoint.ErrorCode);
      Assert.DoesNotContain("release-001", wrongKeyCheckpoint.Message, StringComparison.Ordinal);
      Assert.DoesNotContain("release-002", wrongKeyCheckpoint.Message, StringComparison.Ordinal);
      var checkpointAfterWrongDirectRetry = await db.ReleaseCheckpoints.AsNoTracking()
        .SingleAsync(x => x.ReleaseCandidateId == candidateId);
      Assert.Equal(checkpointBeforeWorkerRefresh.StoredReference, checkpointAfterWrongDirectRetry.StoredReference);
      Assert.Equal(checkpointBeforeWorkerRefresh.ReadBackDigest, checkpointAfterWrongDirectRetry.ReadBackDigest);
      Assert.Equal(checkpointBeforeWorkerRefresh.VerifiedAt, checkpointAfterWrongDirectRetry.VerifiedAt);
      Assert.Equal(checkpointBeforeWorkerRefresh.Verifier, checkpointAfterWrongDirectRetry.Verifier);
      Assert.Empty(await db.Releases.ToListAsync());
      Assert.Equal(ReleaseStates.Ready, (await db.ReleaseCandidates.SingleAsync(x => x.Id == candidateId)).Status);
      Assert.Equal("release-001", (await db.ReleaseCheckpoints.SingleAsync()).AuthorizedReleaseKey);

      // 4. Issue release succeeds
      var issued = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-001"));
      Assert.True(issued.Succeeded);

      // 5. Idempotent re-issuance returns same ID
      var repeated = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-001"));
      Assert.True(repeated.Succeeded);
      Assert.Equal(issued.Value, repeated.Value);
      Assert.Single(await db.Releases.ToListAsync());
      var deliveryIntent = Assert.Single(await db.DurableOperations
        .Where(x => x.OperationKind == "ReleaseDelivery.v1").ToListAsync());
      Assert.Equal("release:release-001", deliveryIntent.IdempotencyKey);
      Assert.Equal(OperationState.PROVIDER_BLOCKED, deliveryIntent.Status);
      Assert.Equal("general", deliveryIntent.ExecutionGroup);
      Assert.Equal("release.delivery.awaiting.authorization", deliveryIntent.ErrorCode);
      Assert.Equal(ReleaseCheckpointHandler.WorkerGroup, handler.Definition.Group);
      db.ChangeTracker.Clear();
      Assert.Equal(ReleaseStates.Issued, (await db.ReleaseCandidates.AsNoTracking().SingleAsync(x => x.Id == candidateId)).Status);

      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE releases SET manifest_digest = {new string('c', 64)} WHERE firm_id = {fixture.FirmId} AND id = {issued.Value}
        """));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM releases WHERE firm_id = {fixture.FirmId} AND id = {issued.Value}
        """));
    }
  }

  [Fact]
  public async Task ReleaseCheckpointKey_RejectsInvalidKeysBeforeQueueStoreOrIssueEffects()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("release-checkpoint-key-validation");
    var digest = Hashing.Sha256Hex(manifestBytes);
    var candidateId = await CreateCandidateForCheckpointKeyAsync(pg, fixture, digest);
    var candidate = await LoadCandidateAsync(pg, candidateId);
    var checkpointDir = Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N"));
    var checkpointStore = new CountingCheckpointStore(new LocalAppendOnlyCheckpointStore(checkpointDir));
    var handler = new ReleaseCheckpointHandler(checkpointStore);
    var innerOperationStore = new PostgresOperationStore(
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)));
    var operationStore = new CountingOperationStore(innerOperationStore);
    var runtime = ReleaseCheckpointRuntimeOptions.Resolve(checkpointDir, "Acceptance", enabled: true);
    var invalidKeys = new string?[] { " \t\r\n ", "release\nkey", new string('k', 201), null };

    OperationRequest HandlerRequest(string? key) => new(
      fixture.FirmId, candidate.ClientId, candidate.EngagementId, ReleaseCheckpointHandler.Kind,
      candidateId, 1, $"checkpoint:{candidateId:D}:1:{digest}",
      System.Text.Json.JsonSerializer.Serialize(new
      {
        candidateId,
        candidateRevision = 1,
        manifestDigest = digest,
        authorizedReleaseKey = key,
        manifestBytesBase64 = Convert.ToBase64String(manifestBytes)
      }), fixture.Partner.UserId);

    try
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      foreach (var key in invalidKeys)
      {
        var issue = await ReleaseService.IssueAsync(db, fixture.Partner,
          new IssueReleaseRequest(candidateId, 1, digest, key!));
        Assert.False(issue.Succeeded);
        Assert.Equal("release.invalid", issue.ErrorCode);
        if (!string.IsNullOrEmpty(key))
          Assert.DoesNotContain(key, issue.Message, StringComparison.Ordinal);

        var direct = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, checkpointStore, fixture.Partner,
          new RecordReleaseCheckpointRequest(candidateId, 1, key!, digest, manifestBytes));
        Assert.False(direct.Succeeded);
        Assert.Equal("checkpoint.invalid", direct.ErrorCode);
        if (!string.IsNullOrEmpty(key))
          Assert.DoesNotContain(key, direct.Message, StringComparison.Ordinal);

        var queued = await ReleaseCheckpointService.EnqueueCheckpointAsync(db, fixture.Partner,
          new RecordReleaseCheckpointRequest(candidateId, 1, key!, digest, manifestBytes), runtime,
          operationStore, handler);
        Assert.False(queued.Succeeded);
        Assert.Equal("checkpoint.invalid", queued.ErrorCode);
        if (!string.IsNullOrEmpty(key))
          Assert.DoesNotContain(key, queued.Message, StringComparison.Ordinal);

        var blocked = Assert.Throws<OperationBlockedException>(() => handler.NormalizePayload(HandlerRequest(key)));
        Assert.Equal("invalid-checkpoint-request", blocked.Code);

        Assert.Equal(0, operationStore.EnqueueCalls);
        Assert.Equal(0, checkpointStore.TotalCalls);
        Assert.Empty(await db.DurableOperations.AsNoTracking()
          .Where(x => x.OperationKind == ReleaseCheckpointHandler.Kind && x.TargetId == candidateId).ToListAsync());
        Assert.Empty(await db.ReleaseCheckpoints.AsNoTracking().Where(x => x.ReleaseCandidateId == candidateId).ToListAsync());
        Assert.Empty(await db.Releases.AsNoTracking().Where(x => x.ReleaseCandidateId == candidateId).ToListAsync());
      }
    }
    finally
    {
      if (Directory.Exists(checkpointDir))
        Directory.Delete(checkpointDir, recursive: true);
    }
  }

  [Fact]
  public async Task ReleaseCheckpointKey_Normalizes200CharactersAcrossEnqueueDirectAndIssueReplay()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("release-checkpoint-key-boundary");
    var digest = Hashing.Sha256Hex(manifestBytes);
    var candidateId = await CreateCandidateForCheckpointKeyAsync(pg, fixture, digest);
    var candidate = await LoadCandidateAsync(pg, candidateId);
    var key = new string('k', 200);
    var paddedKey = $" \t{key}\r\n";
    var otherValidKey = new string('x', 200);
    var checkpointDir = Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N"));
    var checkpointStore = new CountingCheckpointStore(new LocalAppendOnlyCheckpointStore(checkpointDir));
    var handler = new ReleaseCheckpointHandler(checkpointStore);
    var innerOperationStore = new PostgresOperationStore(
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)));
    var operationStore = new CountingOperationStore(innerOperationStore);
    var runtime = ReleaseCheckpointRuntimeOptions.Resolve(checkpointDir, "Acceptance", enabled: true);
    var idempotencyKey = $"checkpoint:{candidateId:D}:1:{digest}";
    var rawPayload = System.Text.Json.JsonSerializer.Serialize(new
    {
      candidateId,
      candidateRevision = 1,
      manifestDigest = digest,
      authorizedReleaseKey = paddedKey,
      manifestBytesBase64 = Convert.ToBase64String(manifestBytes)
    });
    var rawOperation = new OperationRequest(fixture.FirmId, candidate.ClientId, candidate.EngagementId,
      ReleaseCheckpointHandler.Kind, candidateId, 1, idempotencyKey, rawPayload, fixture.Partner.UserId);
    var normalizedPayload = handler.NormalizePayload(rawOperation);
    using (var normalizedDocument = System.Text.Json.JsonDocument.Parse(normalizedPayload))
      Assert.Equal(key, normalizedDocument.RootElement.GetProperty("authorizedReleaseKey").GetString());

    try
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var enqueued = await ReleaseCheckpointService.EnqueueCheckpointAsync(db, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, paddedKey, digest, manifestBytes), runtime,
        operationStore, handler);
      Assert.True(enqueued.Succeeded, enqueued.Message);
      Assert.Equal(1, operationStore.EnqueueCalls);
      var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == enqueued.Value);
      using (var operationPayload = System.Text.Json.JsonDocument.Parse(operation.PayloadJson))
        Assert.Equal(key, operationPayload.RootElement.GetProperty("authorizedReleaseKey").GetString());

      var differentKey = await ReleaseCheckpointService.EnqueueCheckpointAsync(db, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, otherValidKey, digest, manifestBytes), runtime,
        operationStore, handler);
      Assert.False(differentKey.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, differentKey.ErrorCode);
      Assert.DoesNotContain(key, differentKey.Message, StringComparison.Ordinal);
      Assert.DoesNotContain(otherValidKey, differentKey.Message, StringComparison.Ordinal);
      Assert.Equal(2, operationStore.EnqueueCalls);
      Assert.Single(await db.DurableOperations.AsNoTracking()
        .Where(x => x.OperationKind == ReleaseCheckpointHandler.Kind && x.TargetId == candidateId).ToListAsync());

      var recorded = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, checkpointStore, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, paddedKey, digest, manifestBytes));
      Assert.True(recorded.Succeeded, recorded.Message);
      Assert.Equal(1, checkpointStore.WriteCalls);
      Assert.Equal(1, checkpointStore.VerifyCalls);
      Assert.Equal(0, checkpointStore.ProbeCalls + checkpointStore.ReadCalls);
      Assert.Equal(key, (await db.ReleaseCheckpoints.AsNoTracking()
        .SingleAsync(x => x.ReleaseCandidateId == candidateId)).AuthorizedReleaseKey);

      var directDifferentKey = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, checkpointStore,
        fixture.Partner, new RecordReleaseCheckpointRequest(candidateId, 1, otherValidKey, digest, manifestBytes));
      Assert.False(directDifferentKey.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, directDifferentKey.ErrorCode);
      Assert.Equal(1, checkpointStore.WriteCalls);
      Assert.Equal(1, checkpointStore.VerifyCalls);

      var issued = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, paddedKey));
      Assert.True(issued.Succeeded, issued.Message);
      var deliveryIntent = await db.DurableOperations.AsNoTracking()
        .SingleAsync(x => x.OperationKind == "ReleaseDelivery.v1" && x.TargetId == issued.Value);
      Assert.Equal($"release:{issued.Value:D}", deliveryIntent.IdempotencyKey);
      var workspace = await EngagementCompletionWorkspaceQuery.GetAsync(db, fixture.Partner,
        candidate.EngagementId, DateTimeOffset.UtcNow);
      Assert.True(workspace.Succeeded, workspace.Message);
      Assert.Equal("AWAITING_AUTHORIZATION_OR_CONFIGURATION", workspace.Value!.Release!.DeliveryDisposition);
      var replayed = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, key));
      Assert.True(replayed.Succeeded, replayed.Message);
      Assert.Equal(issued.Value, replayed.Value);
      Assert.Single(await db.Releases.AsNoTracking().Where(x => x.ReleaseCandidateId == candidateId).ToListAsync());
      Assert.Equal(1, checkpointStore.WriteCalls);
      Assert.Equal(1, checkpointStore.VerifyCalls);
    }
    finally
    {
      if (Directory.Exists(checkpointDir))
        Directory.Delete(checkpointDir, recursive: true);
    }
  }

  private sealed class LoseFirstCheckpointReadBackStore(IReleaseCheckpointStore inner) : IReleaseCheckpointStore
  {
    private int failNextVerify = 1;

    public Task<CheckpointReceipt> WriteAsync(string manifestDigest, byte[] manifestBytes,
      CancellationToken ct = default) => inner.WriteAsync(manifestDigest, manifestBytes, ct);

    public Task<CheckpointReceipt?> VerifyAsync(string reference, string expectedDigest,
      CancellationToken ct = default)
    {
      if (Interlocked.Exchange(ref failNextVerify, 0) == 1)
        throw new IOException("Synthetic acknowledgement loss after checkpoint write.");
      return inner.VerifyAsync(reference, expectedDigest, ct);
    }

    public Task<CheckpointReceipt?> ProbeAsync(string manifestDigest, CancellationToken ct = default) =>
      inner.ProbeAsync(manifestDigest, ct);

    public Task<CheckpointContent?> ReadVerifiedAsync(string reference, string expectedDigest,
      CancellationToken ct = default) => inner.ReadVerifiedAsync(reference, expectedDigest, ct);
  }

  private sealed class CountingCheckpointStore(IReleaseCheckpointStore inner) : IReleaseCheckpointStore
  {
    public int WriteCalls { get; private set; }
    public int VerifyCalls { get; private set; }
    public int ProbeCalls { get; private set; }
    public int ReadCalls { get; private set; }
    public int TotalCalls => WriteCalls + VerifyCalls + ProbeCalls + ReadCalls;

    public async Task<CheckpointReceipt> WriteAsync(string manifestDigest, byte[] manifestBytes,
      CancellationToken ct = default)
    {
      WriteCalls++;
      return await inner.WriteAsync(manifestDigest, manifestBytes, ct);
    }

    public async Task<CheckpointReceipt?> VerifyAsync(string reference, string expectedDigest,
      CancellationToken ct = default)
    {
      VerifyCalls++;
      return await inner.VerifyAsync(reference, expectedDigest, ct);
    }

    public async Task<CheckpointReceipt?> ProbeAsync(string manifestDigest, CancellationToken ct = default)
    {
      ProbeCalls++;
      return await inner.ProbeAsync(manifestDigest, ct);
    }

    public async Task<CheckpointContent?> ReadVerifiedAsync(string reference, string expectedDigest,
      CancellationToken ct = default)
    {
      ReadCalls++;
      return await inner.ReadVerifiedAsync(reference, expectedDigest, ct);
    }
  }

  private sealed class CountingOperationStore(IOperationStore inner) : IOperationStore
  {
    public int EnqueueCalls { get; private set; }

    public Task<CommandResult<Guid>> EnqueueAsync(IAuditSphereDbContext db, OperationRequest request,
      IOperationHandler handler, CancellationToken ct)
    {
      EnqueueCalls++;
      return inner.EnqueueAsync(db, request, handler, ct);
    }

    public Task<DurableOperation?> ClaimAsync(WorkerOptions options, IReadOnlyList<OperationDefinition> definitions,
      string owner, bool reconciliation, CancellationToken ct) =>
      inner.ClaimAsync(options, definitions, owner, reconciliation, ct);

    public Task<bool> ValidateAsync(DurableOperation operation, WorkerOptions options,
      IOperationHandler handler, CancellationToken ct) => inner.ValidateAsync(operation, options, handler, ct);

    public Task<bool> RenewAsync(DurableOperation operation, WorkerOptions options, CancellationToken ct) =>
      inner.RenewAsync(operation, options, ct);

    public Task<bool> TransitionAsync(DurableOperation operation, WorkerOptions options, OperationState next,
      string? error, TimeSpan? delay, CancellationToken ct) =>
      inner.TransitionAsync(operation, options, next, error, delay, ct);

    public Task<bool> CompleteAsync(DurableOperation operation, WorkerOptions options,
      IOperationHandler handler, OperationResult? remote, CancellationToken ct) =>
      inner.CompleteAsync(operation, options, handler, remote, ct);

    public Task<int> ReapAsync(WorkerOptions options, CancellationToken ct) => inner.ReapAsync(options, ct);
  }

  private static async Task<Guid> CreateCandidateForCheckpointKeyAsync(
    PgTestSchema pg, Fixture fixture, string manifestDigest)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var approval = await ApprovalService.CreateAsync(db, fixture.Reviewer,
      new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, manifestDigest));
    Assert.True(approval.Succeeded, approval.Message);
    var candidate = await ReleaseService.CreateCandidateAsync(db, fixture.Partner,
      new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", fixture.WorkpaperId, 1, 1, 1, manifestDigest));
    Assert.True(candidate.Succeeded, candidate.Message);
    return candidate.Value;
  }

  private static async Task<ReleaseCandidate> LoadCandidateAsync(PgTestSchema pg, Guid candidateId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await db.ReleaseCandidates.AsNoTracking().SingleAsync(x => x.Id == candidateId);
  }

  [Fact]
  public async Task ReleaseGate_BlocksChangedInputsAndCandidateRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("release-test-manifest-payload-d");
    var digest = Hashing.Sha256Hex(manifestBytes);
    var checkpointDir = Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N"));
    var store = new LocalAppendOnlyCheckpointStore(checkpointDir);
    Guid candidateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approval = await ApprovalService.CreateAsync(db, fixture.Reviewer,
        new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(approval.Succeeded);
      var candidate = await ReleaseService.CreateCandidateAsync(db, fixture.Partner,
        new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(candidate.Succeeded);
      candidateId = candidate.Value;

      var cpResult = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, store, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "release-generation", digest, manifestBytes));
      Assert.True(cpResult.Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.ClientSafetyStates.Where(x => x.FirmId == fixture.FirmId && x.Id == fixture.ClientId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
      var staleGeneration = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-generation"));
      Assert.False(staleGeneration.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleGeneration.ErrorCode);
      Assert.Empty(await db.Releases.ToListAsync());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staleCandidate = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 2, digest, "release-revision"));
      Assert.False(staleCandidate.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleCandidate.ErrorCode);
    }
  }


  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var scope = await pg.SeedScopeAsync();
    var reviewer = NewUser(scope.FirmId, "reviewer");
    var partner = NewUser(scope.FirmId, "partner");
    var workpaperId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.AddRange(reviewer, partner);
    db.RoleGrants.AddRange(
      NewGrant(scope, reviewer, "Reviewer"), NewGrant(scope, partner, "Partner"));
    db.Workpapers.Add(new Workpaper
    {
      Id = workpaperId, FirmId = scope.FirmId, ClientId = scope.ClientId,
      EngagementId = scope.EngagementId, ActorId = partner.Id, Index = "R-01",
      Title = "Release workpaper", Objective = "Support the released conclusion",
      TemplateVersion = "RELEASE-2026-v1", Procedure = "Agree the frozen package to evidence",
      Status = "WORKING", CreatedAt = DateTimeOffset.UtcNow
    });
    await db.Engagements.Where(x => x.FirmId == scope.FirmId && x.Id == scope.EngagementId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProfessionalWorkBlocked, false));
    await db.SaveChangesAsync();
    return new Fixture(scope.FirmId, scope.ClientId, workpaperId,
      Actor(reviewer, "Reviewer"), Actor(partner, "Partner"));
  }

  private static AppUser NewUser(Guid firmId, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = role + "-" + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-" + Guid.NewGuid().ToString("N"), Email = role + "@example.test",
    DisplayName = role, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant NewGrant((Guid FirmId, Guid ClientId, Guid EngagementId) scope,
    AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = user.Id, Role = role,
    ClientId = scope.ClientId, EngagementId = scope.EngagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid WorkpaperId,
    ActorContext Reviewer, ActorContext Partner);
}
