using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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

    await using var cached = new AuditSphereDbContext(pg.Options);
    var trackedIntent = await cached.PbcUploadIntents.SingleAsync(x =>
      x.Id == transfer.Staged.UploadIntentId);
    Assert.Equal(PbcUploadStates.Staged, trackedIntent.State);
    var stagedRevision = trackedIntent.Revision;
    var transferOperationId = trackedIntent.TransferOperationId ??
      throw new InvalidOperationException("The staged intent has no transfer operation.");

    Assert.True(await transfer.Worker.ProcessNextAsync());
    Assert.False(await transfer.Worker.ProcessNextAsync());

    long receivedRevision;
    Guid attemptId;
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var intent = await verify.PbcUploadIntents.AsNoTracking()
        .SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
      Assert.Equal(PbcUploadStates.Received, intent.State);
      Assert.True(intent.Revision > stagedRevision);
      receivedRevision = intent.Revision;
      Assert.NotNull(intent.ProviderRegisteredAt);
      Assert.Equal(transfer.Staged.DeclaredSha256Hex, intent.ProviderReceiptDigest);
      Assert.Equal(transferOperationId, intent.TransferOperationId);
      var request = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == transfer.RequestId);
      Assert.Equal(PbcStates.Received, request.State);
      Assert.Equal(5, request.Revision);
      var operation = await verify.DurableOperations.AsNoTracking()
        .SingleAsync(x => x.Id == transferOperationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      Assert.Equal("sim://pbc/" + transfer.Staged.UploadIntentId.ToString("D"), operation.ResultIdentity);
      Assert.Equal(transfer.Staged.DeclaredSha256Hex, operation.ResultDigest);
      Assert.True(await verify.OperationEvents.AnyAsync(x =>
        x.OperationId == operation.Id && x.Kind == "pbc.transfer.registered.v1"));
      Assert.Equal(1, await verify.OperationAttempts.CountAsync(x => x.OperationId == operation.Id));
      attemptId = await verify.OperationAttempts.AsNoTracking()
        .Where(x => x.OperationId == operation.Id).Select(x => x.Id).SingleAsync();
      Assert.True(File.Exists(Path.Combine(transfer.ProviderRoot,
        transfer.Staged.UploadIntentId.ToString("N"))));
    }

    var sink = new SimulationPbcProviderSink(transfer.ProviderRoot);
    var boundary = PbcSeed.Boundary(sink, pg);
    var replay = await PbcService.CompleteUploadAsync(cached,
      PbcSeed.Actor(transfer.Fixture.Staff, "Staff"),
      new CompletePbcUploadRequest(transfer.Staged.UploadIntentId, transfer.Staged.DeclaredSha256Hex),
      boundary.Store, boundary.Handler);
    Assert.True(replay.Succeeded, replay.ErrorCode);
    Assert.Equal(PbcUploadStates.Received, replay.Value!.State);
    Assert.Equal(receivedRevision, replay.Value.Revision);

    await using (var afterReplay = new AuditSphereDbContext(pg.Options))
    {
      var intent = await afterReplay.PbcUploadIntents.AsNoTracking()
        .SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
      Assert.Equal(PbcUploadStates.Received, intent.State);
      Assert.Equal(receivedRevision, intent.Revision);
      Assert.Equal(transferOperationId, intent.TransferOperationId);
      var attempts = await afterReplay.OperationAttempts.AsNoTracking()
        .Where(x => x.OperationId == transferOperationId).ToListAsync();
      var attempt = Assert.Single(attempts);
      Assert.Equal(attemptId, attempt.Id);
      Assert.Equal(1, await afterReplay.OperationEvents.CountAsync(x =>
        x.OperationId == transferOperationId && x.Kind == "pbc.transfer.registered.v1"));
      Assert.Equal(1, await afterReplay.DurableOperations.CountAsync(x =>
        x.OperationKind == PbcDocumentTransferHandler.Kind));
      Assert.True(File.Exists(Path.Combine(transfer.ProviderRoot,
        transfer.Staged.UploadIntentId.ToString("N"))));
    }

    var scope = new PbcTransferScope(transfer.Fixture.FirmId, transfer.Fixture.ClientId,
      transfer.Fixture.EngagementId, transfer.Staged.UploadIntentId);
    var wrongIdentity = "sim://pbc/" + Guid.NewGuid().ToString("D");
    var mismatch = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      sink.VerifyAsync(scope, wrongIdentity, CancellationToken.None));
    Assert.Equal("provider-receipt-conflict", mismatch.Code);

    PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot);
  }

  [Fact]
  public async Task CompleteUploadAndExactChunkReplay_UseOneLockOrderAndCompleteWithoutDeadlock()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, client);
    var content = "%PDF-1.7 fully chunked lock-order regression"u8.ToArray();
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, client, requestId, content);
    var (store, handler) = PbcSeed.Boundary(
      new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider")), pg);

    var completeApplicationName = "pbc-complete-" + Guid.NewGuid().ToString("N");
    var replayApplicationName = "pbc-replay-" + Guid.NewGuid().ToString("N");
    var pause = new PauseAfterUploadIntentForUpdate();
    var completeConnectionString = new NpgsqlConnectionStringBuilder(pg.ConnectionString)
    {
      ApplicationName = completeApplicationName
    }.ConnectionString;
    var replayConnectionString = new NpgsqlConnectionStringBuilder(pg.ConnectionString)
    {
      ApplicationName = replayApplicationName
    }.ConnectionString;
    var completeOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .UseNpgsql(completeConnectionString)
      .AddInterceptors(pause)
      .Options;
    var replayOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .UseNpgsql(replayConnectionString)
      .Options;
    await using var completeDb = new AuditSphereDbContext(completeOptions);
    await using var replayDb = new AuditSphereDbContext(replayOptions);
    await using var observer = new AuditSphereDbContext(pg.Options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    Task<CommandResult<PbcUploadReceipt>>? completing = null;
    Task<CommandResult<PbcUploadReceipt>>? replaying = null;
    try
    {
      completing = PbcService.CompleteUploadAsync(completeDb, staff,
        new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler, timeout.Token);
      await pause.Reached.Task.WaitAsync(timeout.Token);
      var completePid = await BackendPidAsync(observer, completeApplicationName, timeout.Token);

      replaying = PbcService.RecordChunkAsync(replayDb, client,
        new RecordPbcUploadChunkRequest(staged.UploadIntentId, 0, 0, checked((int)staged.ByteCount),
          staged.DeclaredSha256Hex, staged.Capability,
          Path.Combine(staged.StagingRoot, staged.UploadIntentId.ToString("N"), "0.part")), timeout.Token);
      await WaitUntilBlockedByAsync(observer, replayApplicationName, completePid, timeout.Token);

      pause.Release.TrySetResult(true);
      var results = await Task.WhenAll(completing!, replaying!).WaitAsync(timeout.Token);
      var completed = results[0];
      var replay = results[1];
      Assert.True(completed.Succeeded, completed.ErrorCode);
      Assert.Equal(PbcUploadStates.Staged, completed.Value!.State);
      Assert.True(replay.Succeeded, replay.ErrorCode);
      Assert.Equal(PbcUploadStates.Staged, replay.Value!.State);
      Assert.Equal(completed.Value.Revision, replay.Value.Revision);

      await using var verify = new AuditSphereDbContext(pg.Options);
      var intent = await verify.PbcUploadIntents.AsNoTracking()
        .SingleAsync(x => x.Id == staged.UploadIntentId, timeout.Token);
      Assert.Equal(PbcUploadStates.Staged, intent.State);
      Assert.Equal(staged.ByteCount, intent.ReceivedByteCount);
      Assert.Equal(staged.DeclaredSha256Hex, intent.FinalSha256Hex);

      var chunk = Assert.Single(await verify.PbcUploadChunks.AsNoTracking()
        .Where(x => x.PbcUploadIntentId == staged.UploadIntentId).ToListAsync(timeout.Token));
      Assert.Equal(0, chunk.ChunkIndex);
      Assert.Equal(0L, chunk.Offset);
      Assert.Equal(staged.ByteCount, chunk.ByteCount);
      Assert.Equal(staged.DeclaredSha256Hex, chunk.Sha256Hex);

      Assert.Equal(1, await verify.DurableOperations.AsNoTracking()
        .CountAsync(x => x.OperationKind == PbcDocumentTransferHandler.Kind, timeout.Token));
      var transferOperation = await verify.DurableOperations.AsNoTracking().SingleAsync(x =>
        x.OperationKind == PbcDocumentTransferHandler.Kind && x.TargetId == staged.UploadIntentId,
        timeout.Token);
      Assert.Equal(intent.TransferOperationId, transferOperation.Id);
      Assert.Equal(OperationState.PENDING, transferOperation.Status);

      var request = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, timeout.Token);
      Assert.Equal(PbcStates.PartiallyReceived, request.State);
      Assert.Null(request.AcceptedByUserId);
      Assert.Null(request.AcceptedAt);
      Assert.Empty(await verify.PbcAcceptanceReceipts.AsNoTracking().ToListAsync(timeout.Token));
    }
    finally
    {
      pause.Release.TrySetResult(true);
      try
      {
        if (completing is not null && replaying is not null)
          await Task.WhenAll(completing, replaying).WaitAsync(TimeSpan.FromSeconds(5));
        else if (completing is not null)
          await completing.WaitAsync(TimeSpan.FromSeconds(5));
        else if (replaying is not null)
          await replaying.WaitAsync(TimeSpan.FromSeconds(5));
      }
      catch (Exception) { }
      PbcSeed.DeleteDirectory(staged.StagingRoot);
    }
  }

  [Fact]
  public async Task ReviewerGates_AfterReceived_StayScopedAndStale()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var transfer = await CreateTransferWorkerAsync(pg, scripted: null);
    Assert.True(await transfer.Worker.ProcessNextAsync());
    var reviewer = PbcSeed.Actor(transfer.Fixture.Reviewer, "Reviewer");
    var client = PbcSeed.Actor(transfer.Fixture.Client, "ClientUser");
    var nonparticipantUser = PbcSeed.User(transfer.Fixture.FirmId, "Client");
    var nonparticipant = PbcSeed.Actor(nonparticipantUser, "ClientUser");

    await using (var seedNonparticipant = new AuditSphereDbContext(pg.Options))
    {
      seedNonparticipant.Users.Add(nonparticipantUser);
      seedNonparticipant.RoleGrants.Add(PbcSeed.Grant(transfer.Fixture.FirmId, nonparticipantUser, "ClientUser",
        transfer.Fixture.ClientId, transfer.Fixture.EngagementId));
      await seedNonparticipant.SaveChangesAsync();
    }

    await using (var beforeAcceptanceDb = new AuditSphereDbContext(pg.Options))
    {
      var beforeAcceptance = await ClientPortalWorkspaceQuery.RequestAsync(beforeAcceptanceDb, client, transfer.RequestId);
      Assert.True(beforeAcceptance.Succeeded, beforeAcceptance.ErrorCode);
      Assert.Null(beforeAcceptance.Value!.Acceptance);
    }

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
    PbcAcceptanceReceipt receipt;
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      receipt = await verify.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == transfer.RequestId);
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
      Assert.Equal(PbcStates.Accepted, accepted.State);
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

    var expectedAcceptance = new PortalPbcAcceptance(
      receipt.Id, receipt.PbcUploadIntentId, receipt.AcceptedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
      receipt.AcceptedRequestRevision.ToString(CultureInfo.InvariantCulture),
      receipt.SourceRequestRevision.ToString(CultureInfo.InvariantCulture),
      receipt.UploadIntentRevision.ToString(CultureInfo.InvariantCulture), receipt.FileName,
      receipt.ByteCount.ToString(CultureInfo.InvariantCulture), receipt.ContentSha256Hex);
    await using (var acceptedReadDb = new AuditSphereDbContext(pg.Options))
    {
      var acceptedRead = await ClientPortalWorkspaceQuery.RequestAsync(acceptedReadDb, client, transfer.RequestId);
      Assert.True(acceptedRead.Succeeded, acceptedRead.ErrorCode);
      Assert.Equal(expectedAcceptance, acceptedRead.Value!.Acceptance);

      var staffRead = await ClientPortalWorkspaceQuery.RequestAsync(acceptedReadDb,
        PbcSeed.Actor(transfer.Fixture.Staff, "Staff"), transfer.RequestId);
      Assert.False(staffRead.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, staffRead.ErrorCode);
      Assert.Null(staffRead.Value);

      var nonparticipantRead = await ClientPortalWorkspaceQuery.RequestAsync(acceptedReadDb, nonparticipant, transfer.RequestId);
      Assert.False(nonparticipantRead.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, nonparticipantRead.ErrorCode);
      Assert.Null(nonparticipantRead.Value);
    }

    await using (var closeDb = new AuditSphereDbContext(pg.Options))
    {
      var closed = await PbcService.ChangeStateAsync(closeDb, reviewer,
        new PbcStateChangeRequest(transfer.RequestId, PbcStates.Closed, receipt.AcceptedRequestRevision));
      Assert.True(closed.Succeeded, closed.ErrorCode);
    }
    await using (var closedReadDb = new AuditSphereDbContext(pg.Options))
    {
      var closedRead = await ClientPortalWorkspaceQuery.RequestAsync(closedReadDb, client, transfer.RequestId);
      Assert.True(closedRead.Succeeded, closedRead.ErrorCode);
      Assert.Equal(PbcStates.Closed, closedRead.Value!.State);
      Assert.Equal(expectedAcceptance, closedRead.Value.Acceptance);
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

  private sealed class PauseAfterUploadIntentForUpdate : DbCommandInterceptor
  {
    private int paused;

    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
      DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("pbc_upload_intents", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref paused, 1, 0) == 0)
      {
        Reached.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }

  private static async Task<int> BackendPidAsync(
    AuditSphereDbContext observer, string applicationName, CancellationToken ct) =>
    await observer.Database.SqlQuery<int>($"""
      SELECT pid AS "Value" FROM pg_stat_activity
      WHERE datname = current_database() AND application_name = {applicationName}
        AND pid <> pg_backend_pid()
      """).SingleAsync(ct);

  private static async Task WaitUntilBlockedByAsync(
    AuditSphereDbContext observer, string waitingApplicationName, int blockingPid, CancellationToken ct)
  {
    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
    bounded.CancelAfter(TimeSpan.FromSeconds(10));
    try
    {
      while (true)
      {
        var blockedByComplete = await observer.Database.SqlQuery<bool>($"""
          SELECT EXISTS (
            SELECT 1 FROM pg_stat_activity AS waiting
            WHERE waiting.datname = current_database()
              AND waiting.application_name = {waitingApplicationName}
              AND waiting.wait_event_type = 'Lock'
              AND {blockingPid} = ANY(pg_blocking_pids(waiting.pid))
          ) AS "Value"
          """).SingleAsync(bounded.Token);
        if (blockedByComplete) return;
        await Task.Delay(TimeSpan.FromMilliseconds(20), bounded.Token);
      }
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      throw new TimeoutException("The exact chunk replay did not become blocked by the paused completion transaction.");
    }
  }

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
