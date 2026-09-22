using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PbcTests
{
  [Fact]
  public async Task MailWorker_DeliversQueuedNotification_ExactlyOnce()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(
        fixture.EngagementId, "Upload the signed bank confirmations.", "TEST ENTITY", "2026-01-01", "2026-12-31",
        "Cash", "PDF", string.Empty, fixture.Client.Id, fixture.Staff.Id, fixture.Reviewer.Id,
        "2027-01-31", "Confidential", "Signed and readable."));
      Assert.True(created.Succeeded, created.ErrorCode);
      var sent = await PbcService.SendRequestAsync(db, staff, created.Value, 1, "https://audit.example.test/");
      Assert.True(sent.Succeeded, sent.ErrorCode);
    }

    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var sender = new RecordingMailSender();
    var handler = new PbcMailDeliveryHandler(factory, sender);
    var options = new WorkerOptions(fixture.FirmId, "Acceptance", ExternalEffectsEnabled: true, Group: "mail");
    var worker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new PbcMailDiscovery(factory, store, handler, options)],
      NullLogger<AuditSphereOps.Worker.Worker>.Instance);

    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());
    Assert.Single(sender.Plans);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var mail = await verify.PbcCommunications.SingleAsync(x => x.Kind == PbcCommunicationKinds.Email);
    Assert.Equal(PbcDeliveryStates.Sent, mail.DeliveryState);
    Assert.NotNull(mail.DeliveredAt);
    var operation = await verify.DurableOperations.SingleAsync(x => x.TargetId == mail.Id);
    Assert.Equal(OperationState.COMPLETED, operation.Status);
    Assert.Equal(OperationMode.LIVE, operation.ExecutionMode);
    Assert.Equal(OperationAuthority.LIVE_PROVIDER, operation.AuthorityMode);
  }

  [Fact]
  public async Task RequestThread_QueuesEmail_StoresReplies_AndScopesDownload()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(
        fixture.EngagementId, "Upload the signed bank confirmations.", "TEST ENTITY", "2026-01-01", "2026-12-31",
        "Cash", "PDF", string.Empty, fixture.Client.Id, fixture.Staff.Id, fixture.Reviewer.Id,
        "2027-01-31", "Confidential", "Signed and readable."));
      Assert.True(created.Succeeded, created.ErrorCode);
      requestId = created.Value;
      var sent = await PbcService.SendRequestAsync(db, staff, requestId, 1, "https://audit.example.test/app/");
      Assert.True(sent.Succeeded, sent.ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var more = await PbcService.RequestMoreFilesAsync(db, staff,
        new PbcMessageRequest(requestId, "Also upload the January reconciliation.", "https://audit.example.test/"));
      Assert.True(more.Succeeded, more.ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var reply = await PbcService.ReplyAsync(db, client, requestId, "Both files are attached.");
      Assert.True(reply.Succeeded, reply.ErrorCode);
      var thread = await db.PbcCommunications.AsNoTracking().Where(x => x.PbcRequestId == requestId)
        .OrderBy(x => x.CreatedAt).ToListAsync();
      Assert.Equal(5, thread.Count);
      Assert.Equal(2, thread.Count(x => x.Kind == PbcCommunicationKinds.Email && x.DeliveryState == PbcDeliveryStates.Queued));
      Assert.All(thread.Where(x => x.Kind == PbcCommunicationKinds.Email), x =>
        Assert.Contains($"/portal/requests/{requestId:D}", x.Body));
      Assert.Contains(thread, x => x.Kind == PbcCommunicationKinds.ClientMessage && x.Body == "Both files are attached.");
    }

    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId, "%PDF-1.7 requested file"u8.ToArray());
    var (store, handler) = PbcSeed.Boundary(new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider")), pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var completed = await PbcService.CompleteUploadAsync(db, staff,
        new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
      Assert.True(completed.Succeeded, completed.ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var allowed = await PbcService.PrepareDownloadAsync(db, staff, staged.UploadIntentId);
      Assert.True(allowed.Succeeded, allowed.ErrorCode);
      Assert.Equal(staged.ByteCount, allowed.Value!.ByteCount);
      var denied = await PbcService.PrepareDownloadAsync(db, client, staged.UploadIntentId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
    PbcSeed.DeleteDirectory(staged.StagingRoot);
  }

  [Fact]
  public async Task TrustedCompletion_StagesVerifiedBytes_QueuesTransferWithoutReceiving()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, client);
    var content = "%PDF-1.7 audit evidence bytes"u8.ToArray();
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId, content);
    var sink = new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider"));
    var (store, handler) = PbcSeed.Boundary(sink, pg);

    Guid operationId;
    long stagedRevision;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var completed = await PbcService.CompleteUploadAsync(db, staff,
        new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
      Assert.True(completed.Succeeded, completed.ErrorCode);
      Assert.Equal(PbcUploadStates.Staged, completed.Value!.State);
      var intent = await db.PbcUploadIntents.AsNoTracking()
        .SingleAsync(x => x.Id == staged.UploadIntentId);
      operationId = intent.TransferOperationId
        ?? throw new InvalidOperationException("no transfer operation bound");
      stagedRevision = intent.Revision;
      var request = await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
      Assert.Equal(PbcStates.PartiallyReceived, request.State);
    }

    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var operation = await verify.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == operationId);
      Assert.Equal(PbcDocumentTransferHandler.Kind, operation.OperationKind);
      Assert.Equal(OperationMode.SIMULATED, operation.ExecutionMode);
      Assert.Equal(OperationAuthority.SIMULATION, operation.AuthorityMode);
      Assert.Equal(OperationState.PENDING, operation.Status);
      Assert.Equal(stagedRevision, operation.ExpectedRevision);
      Assert.Contains(staged.UploadIntentId.ToString("D"), operation.PayloadJson);
      Assert.Contains(staged.DeclaredSha256Hex, operation.PayloadJson);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var replay = await PbcService.CompleteUploadAsync(db, staff,
        new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
      Assert.True(replay.Succeeded);
      Assert.Equal(PbcUploadStates.Staged, replay.Value!.State);
      Assert.Equal(stagedRevision, replay.Value!.Revision);
      Assert.Equal(1, await db.DurableOperations.CountAsync(x =>
        x.OperationKind == PbcDocumentTransferHandler.Kind && x.TargetId == staged.UploadIntentId));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var extra = await PbcService.RecordChunkAsync(db, client, new RecordPbcUploadChunkRequest(
        staged.UploadIntentId, 9, 900, 2, new string('f', 64), staged.Capability));
      Assert.False(extra.Succeeded);
      Assert.Equal("pbc.chunk-state", extra.ErrorCode);
    }

    PbcSeed.DeleteDirectory(staged.StagingRoot);
  }

  [Fact]
  public async Task ClientUploader_CannotComplete_StaffBoundaryRequired()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, client);
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId,
      "%PDF-1.7 client upload"u8.ToArray());
    var sink = new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider"));
    var (store, handler) = PbcSeed.Boundary(sink, pg);

    await using var db = new AuditSphereDbContext(pg.Options);
    var denied = await PbcService.CompleteUploadAsync(db, client,
      new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId);
    Assert.Equal(PbcUploadStates.Chunking, intent.State);
    PbcSeed.DeleteDirectory(staged.StagingRoot);
  }

  [Fact]
  public async Task TamperedStagedBytes_AreRefused_WithoutTransferOperation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, client);
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId,
      "%PDF-1.7 tamper target"u8.ToArray());
    var sink = new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider"));
    var (store, handler) = PbcSeed.Boundary(sink, pg);
    var file = Directory.EnumerateFiles(
      Path.Combine(staged.StagingRoot, staged.UploadIntentId.ToString("N"))).Single();
    await File.WriteAllBytesAsync(file, "corrupted after receipt"u8.ToArray());

    await using var db = new AuditSphereDbContext(pg.Options);
    var completed = await PbcService.CompleteUploadAsync(db, staff,
      new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
    Assert.False(completed.Succeeded);
    Assert.Equal("pbc.staging-unverifiable", completed.ErrorCode);
    var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId);
    Assert.Equal(PbcUploadStates.Chunking, intent.State);
    Assert.Null(intent.TransferOperationId);
    PbcSeed.DeleteDirectory(staged.StagingRoot);
  }

  [Fact]
  public async Task MissingStagedChunk_IsRefused_WithoutTransferOperation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, client);
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId,
      "%PDF-1.7 missing bytes"u8.ToArray());
    var sink = new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider"));
    var (store, handler) = PbcSeed.Boundary(sink, pg);
    Directory.Delete(Path.Combine(staged.StagingRoot, staged.UploadIntentId.ToString("N")), true);

    await using var db = new AuditSphereDbContext(pg.Options);
    var completed = await PbcService.CompleteUploadAsync(db, staff,
      new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
    Assert.False(completed.Succeeded);
    Assert.Equal("pbc.staging-unverifiable", completed.ErrorCode);
    PbcSeed.DeleteDirectory(staged.StagingRoot);
  }

  [Fact]
  public async Task ExecutableContent_IsRejected_IndependentOfDeclaredType()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, client);
    var executable = "MZ"u8.ToArray().Concat("payload"u8.ToArray()).ToArray();
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId, executable,
      fileName: "report.pdf", contentType: "application/pdf");
    var sink = new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider"));
    var (store, handler) = PbcSeed.Boundary(sink, pg);

    await using var db = new AuditSphereDbContext(pg.Options);
    var completed = await PbcService.CompleteUploadAsync(db, staff,
      new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
    Assert.False(completed.Succeeded);
    Assert.Equal("pbc.content-rejected", completed.ErrorCode);
    var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId);
    Assert.Equal(PbcUploadStates.Chunking, intent.State);
    PbcSeed.DeleteDirectory(staged.StagingRoot);
  }

  [Fact]
  public async Task ChunkReceipts_RemainImmutable_AfterTrustedCompletion()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, client);
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId,
      "%PDF-1.7 immutability"u8.ToArray());
    var sink = new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider"));
    var (store, handler) = PbcSeed.Boundary(sink, pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var completed = await PbcService.CompleteUploadAsync(db, staff,
        new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
      Assert.True(completed.Succeeded, completed.ErrorCode);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var chunkId = await verify.PbcUploadChunks.Select(x => x.Id).FirstAsync();
    await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE pbc_upload_chunks SET byte_count = 7 WHERE id = {chunkId}"));
    PbcSeed.DeleteDirectory(staged.StagingRoot);
  }

  private sealed class RecordingMailSender : IPbcMailSender
  {
    public List<PbcMailPlan> Plans { get; } = [];
    public Task SendAsync(PbcMailPlan plan, CancellationToken ct)
    {
      Plans.Add(plan);
      return Task.CompletedTask;
    }
  }
}
