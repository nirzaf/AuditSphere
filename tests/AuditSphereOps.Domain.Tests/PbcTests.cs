using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PbcTests
{
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
}
