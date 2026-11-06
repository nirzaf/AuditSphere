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
