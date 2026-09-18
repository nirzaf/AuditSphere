using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PbcTransferTests
{
  [Fact]
  public async Task StagedTransfer_CompletesThroughWorker_AndMarksReceivedOnlyThen()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var transfer = await CreateTransferWorkerAsync(pg, scripted: null);

    await using (var before = new AuditSphereDbContext(pg.Options))
    {
      var request = await before.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == transfer.RequestId);
      Assert.Equal(PbcStates.PartiallyReceived, request.State);
      var intent = await before.PbcUploadIntents.AsNoTracking()
        .SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
      Assert.Equal(PbcUploadStates.Staged, intent.State);
    }

    Assert.True(await transfer.Worker.ProcessNextAsync());
    Assert.False(await transfer.Worker.ProcessNextAsync());

    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var intent = await verify.PbcUploadIntents.AsNoTracking()
        .SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
      Assert.Equal(PbcUploadStates.Received, intent.State);
      Assert.NotNull(intent.ProviderRegisteredAt);
      Assert.Equal(transfer.Staged.DeclaredSha256Hex, intent.ProviderReceiptDigest);
      var request = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == transfer.RequestId);
      Assert.Equal(PbcStates.Received, request.State);
      Assert.Equal(5, request.Revision);
      var operation = await verify.DurableOperations.AsNoTracking()
        .SingleAsync(x => x.OperationKind == PbcDocumentTransferHandler.Kind);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      Assert.Equal("sim://pbc/" + transfer.Staged.UploadIntentId.ToString("D"), operation.ResultIdentity);
      Assert.Equal(transfer.Staged.DeclaredSha256Hex, operation.ResultDigest);
      Assert.True(await verify.OperationEvents.AnyAsync(x =>
        x.OperationId == operation.Id && x.Kind == "pbc.transfer.registered.v1"));
      Assert.Equal(1, await verify.OperationAttempts.CountAsync(x => x.OperationId == operation.Id));
      Assert.True(File.Exists(Path.Combine(transfer.ProviderRoot,
        transfer.Staged.UploadIntentId.ToString("N"))));
    }

    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

  [Fact]
  public async Task ReviewerGates_AfterReceived_StayScopedAndStale()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var transfer = await CreateTransferWorkerAsync(pg, scripted: null);
    Assert.True(await transfer.Worker.ProcessNextAsync());
    var reviewer = PbcSeed.Actor(transfer.Fixture.Reviewer, "Reviewer");
    var client = PbcSeed.Actor(transfer.Fixture.Client, "ClientUser");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var underReview = await PbcService.ChangeStateAsync(db, reviewer,
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.UnderReview, 5));
      Assert.True(underReview.Succeeded, underReview.ErrorCode);
      var accepted = await PbcService.ChangeStateAsync(db, reviewer,
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Accepted, 6));
      Assert.True(accepted.Succeeded, accepted.ErrorCode);
      Assert.False((await PbcService.ChangeStateAsync(db, client,
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Accepted, 7))).Succeeded);
      Assert.False((await PbcService.ChangeStateAsync(db,
        PbcSeed.Actor(transfer.Fixture.Staff, "Staff"),
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Closed, 1))).Succeeded);
    }
    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

  [Fact]
  public async Task InterruptedProviderEffect_IsUncertain_ThenReconcilesToReceived()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scripted = new ScriptedSink();
    var transfer = await CreateTransferWorkerAsync(pg, scripted);
    scripted.OnUpload(async plan =>
    {
      await scripted.Inner!.UploadAsync(plan, CancellationToken.None);
      throw new IOException("simulated worker interruption after the provider effect");
    });

    Assert.True(await transfer.Worker.ProcessNextAsync());
    await using (var mid = new AuditSphereDbContext(pg.Options))
    {
      var operation = await mid.DurableOperations.AsNoTracking()
        .SingleAsync(x => x.OperationKind == PbcDocumentTransferHandler.Kind);
      Assert.Equal(OperationState.RESULT_UNCERTAIN, operation.Status);
      var request = await mid.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == transfer.RequestId);
      Assert.Equal(PbcStates.PartiallyReceived, request.State);
      await mid.Database.ExecuteSqlRawAsync(
        "UPDATE durable_operations SET next_attempt_at = statement_timestamp()");
    }

    Assert.True(await transfer.Worker.ProcessNextAsync()); // Reconciliation verifies stored bytes.
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var intent = await verify.PbcUploadIntents.AsNoTracking()
        .SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
      Assert.Equal(PbcUploadStates.Received, intent.State);
      Assert.Equal(transfer.Staged.DeclaredSha256Hex, intent.ProviderReceiptDigest);
    }
    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

  [Fact]
  public async Task UnknownOutcomeBeforeEffect_BlocksAfterReconciliationConfirmsNoEffect()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scripted = new ScriptedSink();
    var transfer = await CreateTransferWorkerAsync(pg, scripted);
    scripted.OnUpload(_ => throw new IOException("interrupted before any provider effect"));

    Assert.True(await transfer.Worker.ProcessNextAsync());
    await using (var mid = new AuditSphereDbContext(pg.Options))
    {
      await mid.Database.ExecuteSqlRawAsync(
        "UPDATE durable_operations SET next_attempt_at = statement_timestamp()");
    }
    Assert.True(await transfer.Worker.ProcessNextAsync()); // Reconciliation: no observable effect.
    await using var verify = new AuditSphereDbContext(pg.Options);
    var operation = await verify.DurableOperations.AsNoTracking()
      .SingleAsync(x => x.OperationKind == PbcDocumentTransferHandler.Kind);
    Assert.Equal(OperationState.PROVIDER_BLOCKED, operation.Status);
    var request = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == transfer.RequestId);
    Assert.Equal(PbcStates.PartiallyReceived, request.State);
    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

  [Fact]
  public async Task DeterministicProviderRejection_BlocksExplicitly_WithoutReceiving()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scripted = new ScriptedSink();
    var transfer = await CreateTransferWorkerAsync(pg, scripted);
    scripted.OnUpload(_ => throw new OperationBlockedException("simulated-provider-rejection"));

    Assert.True(await transfer.Worker.ProcessNextAsync());
    await using var verify = new AuditSphereDbContext(pg.Options);
    var operation = await verify.DurableOperations.AsNoTracking()
      .SingleAsync(x => x.OperationKind == PbcDocumentTransferHandler.Kind);
    Assert.Equal(OperationState.PROVIDER_BLOCKED, operation.Status);
    Assert.Equal("provider-or-integrity-blocked", operation.ErrorCode);
    var intent = await verify.PbcUploadIntents.AsNoTracking()
      .SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
    Assert.Equal(PbcUploadStates.Staged, intent.State);
    var request = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == transfer.RequestId);
    Assert.Equal(PbcStates.PartiallyReceived, request.State);
    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

  [Fact]
  public async Task SafeTransientFailure_Retries_ThenCompletes()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scripted = new ScriptedSink();
    var transfer = await CreateTransferWorkerAsync(pg, scripted);
    scripted.OnUpload(_ => throw new SafeRetryException(TimeSpan.FromSeconds(5)));

    Assert.True(await transfer.Worker.ProcessNextAsync());
    await using (var mid = new AuditSphereDbContext(pg.Options))
    {
      var operation = await mid.DurableOperations.AsNoTracking()
        .SingleAsync(x => x.OperationKind == PbcDocumentTransferHandler.Kind);
      Assert.Equal(OperationState.RETRY_WAIT, operation.Status);
      // Test-only nudge: production honors the durable retry delay; collapse it here.
      await mid.Database.ExecuteSqlRawAsync(
        "UPDATE durable_operations SET next_attempt_at = statement_timestamp()");
    }

    Assert.True(await transfer.Worker.ProcessNextAsync());
    Assert.False(await transfer.Worker.ProcessNextAsync());
    await using var verify = new AuditSphereDbContext(pg.Options);
    var intent = await verify.PbcUploadIntents.AsNoTracking()
      .SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
    Assert.Equal(PbcUploadStates.Received, intent.State);
    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

  [Fact]
  public async Task NewUpload_IsRefused_AfterProviderReception()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var transfer = await CreateTransferWorkerAsync(pg, scripted: null);
    Assert.True(await transfer.Worker.ProcessNextAsync());
    var client = PbcSeed.Actor(transfer.Fixture.Client, "ClientUser");
    await using var db = new AuditSphereDbContext(pg.Options);
    var refused = await PbcService.StartUploadAsync(db, client, new StartPbcUploadRequest(
      transfer.RequestId, "second.csv", "text/csv", 4, new string('a', 64)));
    Assert.False(refused.Succeeded);
    Assert.Equal("pbc.upload-state", refused.ErrorCode);
    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

  private sealed record TransferContext(
    PbcSeed.Fixture Fixture, Guid RequestId, PbcSeed.StagedUpload Staged,
    string ProviderRoot, WorkerHost Worker);

  private async Task<TransferContext> CreateTransferWorkerAsync(
    PgTestSchema pg, ScriptedSink? scripted, bool wrap = true)
  {
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, client);
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId,
      "%PDF-1.7 staged transfer evidence"u8.ToArray());
    var providerRoot = Path.Combine(staged.StagingRoot, "provider");
    var inner = new SimulationPbcProviderSink(providerRoot);
    if (scripted is not null) scripted.Bind(inner);
    IPbcProviderSink sink = scripted is not null && wrap ? scripted : inner;

    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var handler = new PbcDocumentTransferHandler(factory, sink);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var completed = await PbcService.CompleteUploadAsync(db, staff,
        new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
      Assert.True(completed.Succeeded, completed.ErrorCode);
    }

    var options = new WorkerOptions(fixture.FirmId, "Test", AllowSimulationAdapters: true);
    var registry = new DurableOperationRegistry([handler], options);
    var discovery = new PbcTransferDiscovery(factory, store, handler, options);
    var worker = new WorkerHost(new OperationDispatcher(store, registry, options),
      [discovery], NullLogger<WorkerHost>.Instance);
    return new TransferContext(fixture, requestId, staged, providerRoot, worker);
  }

  private sealed class ScriptedSink : IPbcProviderSink
  {
    private readonly List<Func<PbcTransferPlan, Task<PbcProviderReceipt>>> uploadScript = [];

    public IPbcProviderSink? Inner { get; private set; }

    public void Bind(IPbcProviderSink inner) => Inner = inner;

    public void OnUpload(Func<PbcTransferPlan, Task<PbcProviderReceipt>> behavior) =>
      uploadScript.Add(behavior);

    public async Task<PbcProviderReceipt> UploadAsync(PbcTransferPlan plan, CancellationToken ct)
    {
      var behavior = uploadScript.Count > 0
        ? uploadScript[0]
        : plan2 => Inner!.UploadAsync(plan2, ct);
      if (uploadScript.Count > 0) uploadScript.RemoveAt(0);
      return await behavior(plan);
    }

    public Task<PbcProviderReceipt?> VerifyAsync(string identity, CancellationToken ct) =>
      Inner!.VerifyAsync(identity, ct);

    public Task<PbcProviderReceipt?> ProbeAsync(Guid uploadIntentId, CancellationToken ct) =>
      Inner!.ProbeAsync(uploadIntentId, ct);
  }
}
