using System.Diagnostics;
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
  public async Task MaximumChunk_TransfersOnceAndReportsMeasurement()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var content = new byte[PbcService.MaxChunkBytes];
    for (var index = 0; index < content.Length; index++)
      content[index] = (byte)(index % 251);
    var started = Stopwatch.GetTimestamp();
    var transfer = await CreateTransferWorkerAsync(pg, scripted: null, content: content);
    var stagedElapsed = Stopwatch.GetElapsedTime(started);
    try
    {
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        Assert.Equal(1, await db.PbcUploadChunks.AsNoTracking()
          .CountAsync(x => x.PbcUploadIntentId == transfer.Staged.UploadIntentId));
      }
      started = Stopwatch.GetTimestamp();
      Assert.True(await transfer.Worker.ProcessNextAsync());
      var workerElapsed = Stopwatch.GetElapsedTime(started);
      Assert.False(await transfer.Worker.ProcessNextAsync());
      await using var verify = new AuditSphereDbContext(pg.Options);
      var intent = await verify.PbcUploadIntents.AsNoTracking()
        .SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
      Assert.Equal(PbcUploadStates.Received, intent.State);
      Assert.Equal(transfer.Staged.DeclaredSha256Hex, intent.ProviderReceiptDigest);
      Assert.Equal(content.Length, new FileInfo(Path.Combine(transfer.ProviderRoot,
        transfer.Staged.UploadIntentId.ToString("N"))).Length);
      Assert.Equal(1, await verify.OperationAttempts.AsNoTracking()
        .CountAsync(x => x.OperationId == intent.TransferOperationId));
      Console.WriteLine($"PBC_CAPACITY chunk_bytes={content.Length} stage_ms={stagedElapsed.TotalMilliseconds:F1} " +
        $"worker_ms={workerElapsed.TotalMilliseconds:F1} received=true attempts=1");
    }
    finally { PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot); }
  }

  [Theory]
  [InlineData("", 12, null)]
  [InlineData("provider://other", 12, "provider://expected")]
  [InlineData("provider://expected", 11, "provider://expected")]
  public async Task Reconciliation_RejectsIncompleteOrChangedProviderReceipt(
    string identity, long byteCount, string? knownIdentity)
  {
    var digest = new string('a', 64);
    var intentId = Guid.NewGuid();
    var receipt = new PbcProviderReceipt(identity, digest, byteCount);
    var handler = new PbcDocumentTransferHandler(null!, new ReceiptOnlySink(receipt));
    var operation = new DurableOperation
    {
      FirmId = Guid.NewGuid(), ClientId = Guid.NewGuid(), EngagementId = Guid.NewGuid(),
      TargetId = intentId, ExpectedRevision = 1, ResultIdentity = knownIdentity,
      PayloadJson = $$"""{"uploadIntentId":"{{intentId:D}}","intentRevision":1,"finalSha256Hex":"{{digest}}","declaredByteCount":12}"""
    };

    var error = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      handler.ReconcileAsync(operation, CancellationToken.None));
    Assert.Equal("provider-receipt-conflict", error.Code);
  }

  [Fact]
  public async Task ProviderHandoff_UsesTheTrustedClientAndEngagementScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scripted = new ScriptedSink();
    var transfer = await CreateTransferWorkerAsync(pg, scripted);
    scripted.OnUpload(async plan =>
    {
      Assert.Equal(transfer.Fixture.FirmId, plan.FirmId);
      Assert.Equal(transfer.Fixture.ClientId, plan.ClientId);
      Assert.Equal(transfer.Fixture.EngagementId, plan.EngagementId);
      Assert.Equal(transfer.Staged.UploadIntentId, plan.UploadIntentId);
      return await scripted.Inner!.UploadAsync(plan, CancellationToken.None);
    });

    Assert.True(await transfer.Worker.ProcessNextAsync());
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(PbcUploadStates.Received, (await verify.PbcUploadIntents.AsNoTracking()
      .SingleAsync(x => x.Id == transfer.Staged.UploadIntentId)).State);
    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

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

    var sink = new SimulationPbcProviderSink(transfer.ProviderRoot);
    var scope = new PbcTransferScope(transfer.Fixture.FirmId, transfer.Fixture.ClientId,
      transfer.Fixture.EngagementId, transfer.Staged.UploadIntentId);
    var wrongIdentity = "sim://pbc/" + Guid.NewGuid().ToString("D");
    var mismatch = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      sink.VerifyAsync(scope, wrongIdentity, CancellationToken.None));
    Assert.Equal("provider-receipt-conflict", mismatch.Code);

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
      var source = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
      var underReview = await PbcService.ChangeStateAsync(db, reviewer,
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.UnderReview, 5));
      Assert.True(underReview.Succeeded, underReview.ErrorCode);
      var unreviewed = await PbcService.ChangeStateAsync(db, reviewer,
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Accepted, 6));
      Assert.Equal("pbc.acceptance-review-required", unreviewed.ErrorCode);
      var unassignedReviewer = await PbcService.ChangeStateAsync(db,
        PbcSeed.Actor(transfer.Fixture.Staff, "Staff"),
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Accepted, 6,
          ReviewedUploadIntentId: transfer.Staged.UploadIntentId, ReviewedUploadRevision: source.Revision,
          ReviewedContentSha256Hex: transfer.Staged.DeclaredSha256Hex));
      Assert.Equal(ErrorCodes.ScopeDenied, unassignedReviewer.ErrorCode);
      var wrongSource = await PbcService.ChangeStateAsync(db, reviewer,
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Accepted, 6,
          ReviewedUploadIntentId: Guid.NewGuid(), ReviewedUploadRevision: source.Revision,
          ReviewedContentSha256Hex: transfer.Staged.DeclaredSha256Hex));
      Assert.Equal("pbc.acceptance-source-changed", wrongSource.ErrorCode);
      var accepted = await PbcService.ChangeStateAsync(db, reviewer,
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Accepted, 6,
          ReviewedUploadIntentId: transfer.Staged.UploadIntentId, ReviewedUploadRevision: source.Revision,
          ReviewedContentSha256Hex: transfer.Staged.DeclaredSha256Hex));
      Assert.True(accepted.Succeeded, accepted.ErrorCode);
      Assert.False((await PbcService.ChangeStateAsync(db, client,
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Accepted, 7))).Succeeded);
      Assert.False((await PbcService.ChangeStateAsync(db,
        PbcSeed.Actor(transfer.Fixture.Staff, "Staff"),
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Closed, 1))).Succeeded);
    }
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var receipt = await verify.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == transfer.RequestId);
      var operation = await verify.DurableOperations.AsNoTracking().SingleAsync(x =>
        x.OperationKind == PbcDocumentTransferHandler.Kind && x.TargetId == transfer.Staged.UploadIntentId);
      var sourceUpload = await verify.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == receipt.PbcUploadIntentId);
      Assert.Equal(transfer.Staged.UploadIntentId, receipt.PbcUploadIntentId);
      Assert.Equal(sourceUpload.RequestRevision, receipt.SourceRequestRevision);
      Assert.Equal(sourceUpload.Revision, receipt.UploadIntentRevision);
      Assert.Equal(transfer.Staged.DeclaredSha256Hex, receipt.ContentSha256Hex);
      Assert.Equal(transfer.Staged.DeclaredSha256Hex, receipt.ProviderReceiptDigest);
      Assert.Equal(operation.Id, receipt.TransferOperationId);
      Assert.Equal(operation.ResultIdentity, receipt.ProviderIdentity);
      Assert.Equal(transfer.Fixture.Reviewer.Id, receipt.ReviewerUserId);
      Assert.Matches("^[0-9a-f]{64}$", receipt.SourceDigest);
      Assert.Equal(1, await verify.PbcAcceptanceReceipts.CountAsync(x => x.PbcRequestId == transfer.RequestId));
      var accepted = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == transfer.RequestId);
      Assert.Equal(receipt.AcceptedAt, accepted.AcceptedAt);
      Assert.Equal(receipt.ReviewerUserId, accepted.AcceptedByUserId);
      var receiptMutation = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE pbc_acceptance_receipts SET file_name = 'rewritten.pdf' WHERE id = {receipt.Id}"));
      Assert.Equal("55000", receiptMutation.SqlState);
      var sourceMutation = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE pbc_upload_intents SET file_name = 'rewritten.pdf' WHERE id = {receipt.PbcUploadIntentId}"));
      Assert.Equal("55000", sourceMutation.SqlState);
      var requestMutation = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE pbc_requests SET accepted_by_user_id = {transfer.Fixture.Staff.Id} WHERE id = {transfer.RequestId}"));
      Assert.Equal("55000", requestMutation.SqlState);
      var requestContentMutation = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE pbc_requests SET objective = 'rewritten after acceptance' WHERE id = {transfer.RequestId}"));
      Assert.Equal("55000", requestContentMutation.SqlState);
    }
    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

  [Fact]
  public async Task AcceptanceWithoutCompletedProviderVersionFailsClosed()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(
        fixture.EngagementId, "Bank statements", "TEST ENTITY", "2026-01-01", "2026-12-31",
        "Cash", "PDF", "12 months", fixture.Client.Id, fixture.Staff.Id, fixture.Reviewer.Id,
        "2027-01-31", "Confidential", "Complete period with readable account identity."));
      Assert.True(created.Succeeded, created.ErrorCode);
      requestId = created.Value;
      var request = await db.PbcRequests.SingleAsync(x => x.Id == requestId);
      request.State = PbcStates.UnderReview;
      request.Revision++;
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await PbcService.ChangeStateAsync(db, PbcSeed.Actor(fixture.Reviewer, "Reviewer"),
        new PbcStateChangeRequest(requestId, PbcStates.Accepted, 2,
          ReviewedUploadIntentId: Guid.NewGuid(), ReviewedUploadRevision: 1,
          ReviewedContentSha256Hex: new string('a', 64)));
      Assert.Equal("pbc.acceptance-evidence", result.ErrorCode);
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await verify.PbcAcceptanceReceipts.AsNoTracking().ToListAsync());
    Assert.Equal(PbcStates.UnderReview,
      await verify.PbcRequests.AsNoTracking().Where(x => x.Id == requestId).Select(x => x.State).SingleAsync());
  }

  [Fact]
  public async Task CreateRequestRequiresQualifiedIndependentReviewer()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var unqualified = PbcSeed.User(fixture.FirmId, "Staff");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(unqualified);
    db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, unqualified, "Staff",
      clientId: fixture.ClientId, engagementId: fixture.EngagementId));
    await db.SaveChangesAsync();

    var admin = PbcSeed.Actor(fixture.Admin, "Administrator");
    var unqualifiedResult = await PbcService.CreateRequestAsync(db, admin, new CreatePbcRequestRequest(
      fixture.EngagementId, "Bank statements", "TEST ENTITY", "2026-01-01", "2026-12-31",
      "Cash", "PDF", "12 months", fixture.Client.Id, fixture.Staff.Id, unqualified.Id,
      "2027-01-31", "Confidential", "Complete period with readable account identity."));
    Assert.Equal("pbc.reviewer-required", unqualifiedResult.ErrorCode);

    var selfReviewResult = await PbcService.CreateRequestAsync(db, admin, new CreatePbcRequestRequest(
      fixture.EngagementId, "Bank statements", "TEST ENTITY", "2026-01-01", "2026-12-31",
      "Cash", "PDF", "12 months", fixture.Client.Id, fixture.Reviewer.Id, fixture.Reviewer.Id,
      "2027-01-31", "Confidential", "Complete period with readable account identity."));
    Assert.Equal("pbc.reviewer-separation", selfReviewResult.ErrorCode);
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
    PgTestSchema pg, ScriptedSink? scripted, bool wrap = true, byte[]? content = null)
  {
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, client);
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId,
      content ?? "%PDF-1.7 staged transfer evidence"u8.ToArray());
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

    public Task<PbcProviderReceipt?> VerifyAsync(PbcTransferScope scope, string identity, CancellationToken ct) =>
      Inner!.VerifyAsync(scope, identity, ct);

    public Task<PbcProviderReceipt?> ProbeAsync(PbcTransferScope scope, CancellationToken ct) =>
      Inner!.ProbeAsync(scope, ct);
  }

  private sealed class ReceiptOnlySink(PbcProviderReceipt receipt) : IPbcProviderSink
  {
    public Task<PbcProviderReceipt> UploadAsync(PbcTransferPlan plan, CancellationToken ct) =>
      throw new NotSupportedException();

    public Task<PbcProviderReceipt?> VerifyAsync(PbcTransferScope scope, string identity, CancellationToken ct) =>
      Task.FromResult<PbcProviderReceipt?>(receipt);

    public Task<PbcProviderReceipt?> ProbeAsync(PbcTransferScope scope, CancellationToken ct) =>
      Task.FromResult<PbcProviderReceipt?>(receipt);
  }
}
