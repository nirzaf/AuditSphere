using System.Data.Common;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task GlImport_HoldsCurrentAuthorityThroughSaveAgainstRoleRevocation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, scope, preparer);
    var administrator = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, Subject = "authority-admin-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "authority-admin@example.test", DisplayName = "Authority Administrator"
    };
    Guid preparerGrantId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      preparerGrantId = await setup.RoleGrants.Where(x => x.FirmId == scope.FirmId && x.UserId == scope.Preparer.Id &&
          x.Role == "AccountingPreparer" && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync(timeout.Token);
      setup.Users.Add(administrator);
      setup.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = administrator.Id, Role = "Administrator",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = administrator.Id
      });
      await setup.SaveChangesAsync(timeout.Token);
    }

    var request = new GeneralLedgerImportRequest(scope.ClientA, scope.EngagementA, periodId, bookId,
      "csv-v1", "gl-v1", new string('a', 64), "CLIENT-A", "QAR", "authority-race-receipt",
      [new("J-AUTH", "INV-AUTH", new DateOnly(2026, 6, 30), null, "user-a", "LEDGER-A", null, false, false,
        [new("J-AUTH-L1", "1000", 25m, 0m, "QAR", 25m, 25m),
         new("J-AUTH-L2", "4000", 0m, 25m, "QAR", -25m, -25m)])]);
    var duplicateCheckReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var resumeImport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var importConnectionReached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var revocationLockAttempted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var importOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(new PauseBeforeSourceIdentityCheck(duplicateCheckReached, resumeImport, importConnectionReached)).Options;
    var revokeOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(new ObserveFirmRevocationLock(revocationLockAttempted)).Options;
    await using var importDb = new AuditSphereDbContext(importOptions);
    var import = ClientAccountingService.ImportGeneralLedgerAsync(importDb, preparer, request, timeout.Token);
    Task<CommandResult>? revoke = null;

    try
    {
      await duplicateCheckReached.Task.WaitAsync(timeout.Token);
      var importPid = await importConnectionReached.Task.WaitAsync(timeout.Token);
      revoke = Task.Run(async () =>
      {
        await using var revokeDb = new AuditSphereDbContext(revokeOptions);
        return await RoleAdministrationService.RevokeRoleGrantAsync(revokeDb,
          Actor(administrator, "Administrator"), new(preparerGrantId, Reason: "Focused GL authority race"), timeout.Token);
      }, timeout.Token);
      var revokePid = await revocationLockAttempted.Task.WaitAsync(timeout.Token);
      var revocationWaitedForImport = await WaitForRevocationBlockerOrCompletionAsync(
        revoke, revokePid, importPid, pg.ConnectionString, timeout.Token);

      resumeImport.TrySetResult();
      var imported = await import.WaitAsync(timeout.Token);
      var revoked = await revoke.WaitAsync(timeout.Token);

      Assert.True(imported.Succeeded, imported.Message);
      Assert.True(revoked.Succeeded, revoked.Message);
      await using var verify = new AuditSphereDbContext(pg.Options);
      Assert.NotNull((await verify.RoleGrants.SingleAsync(x => x.Id == preparerGrantId, timeout.Token)).RevokedAt);
      Assert.Single(await verify.SourceImportBatches.Where(x => x.ReceiptReference == request.ReceiptReference).ToListAsync(timeout.Token));
      Assert.Single(await verify.GeneralLedgerTransactions.Where(x => x.StableJournalId == "J-AUTH").ToListAsync(timeout.Token));
      Assert.True(revocationWaitedForImport,
        $"RoleAdministration completed before GL resumed despite its earlier authorization (import succeeded={imported.Succeeded}; revocation succeeded={revoked.Succeeded}).");
    }
    finally
    {
      resumeImport.TrySetResult();
      if (revoke is not null)
      {
        try { await revoke.WaitAsync(timeout.Token); }
        catch (OperationCanceledException) { }
      }
      try { await import.WaitAsync(timeout.Token); }
      catch (OperationCanceledException) { }
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GlImport_DeniesCrossClientDuplicateAndSealedChunkReplayBeforeDuplicateChecks()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, scope, preparer);
    var clientBPreparer = User(scope.FirmId, "client-b-preparer");
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.Add(clientBPreparer);
      setup.RoleGrants.Add(Grant(scope.FirmId, clientBPreparer, "AccountingPreparer", scope.ClientB, scope.EngagementB));
      await setup.SaveChangesAsync();
    }
    var clientBActor = Actor(clientBPreparer, "AccountingPreparer");
    var singleTransactions = new[]
    {
      new GeneralLedgerTransactionInput("J-SCOPE", "INV-SCOPE", new DateOnly(2026, 6, 30), null, "user-a", "LEDGER-A", null, false, false,
        [new("J-SCOPE-L1", "1000", 15m, 0m, "QAR", 15m, 15m),
         new("J-SCOPE-L2", "4000", 0m, 15m, "QAR", -15m, -15m)])
    };
    var singleRequest = new GeneralLedgerImportRequest(scope.ClientA, scope.EngagementA, periodId, bookId,
      "csv-v1", "gl-v1", new string('b', 64), "CLIENT-A", "QAR", "scope-duplicate-receipt", singleTransactions);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var imported = await ClientAccountingService.ImportGeneralLedgerAsync(db, preparer, singleRequest);
      Assert.True(imported.Succeeded, imported.Message);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var crossClientDuplicate = await ClientAccountingService.ImportGeneralLedgerAsync(db, clientBActor, singleRequest);
      Assert.False(crossClientDuplicate.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, crossClientDuplicate.ErrorCode);
    }

    var chunk = new[]
    {
      new GeneralLedgerTransactionInput("J-STREAM-SCOPE", "INV-STREAM-SCOPE", new DateOnly(2026, 6, 30), null,
        "user-a", "LEDGER-A", null, false, false,
        [new("J-STREAM-SCOPE-L1", "1000", 20m, 0m, "QAR", 20m, 20m),
         new("J-STREAM-SCOPE-L2", "4000", 0m, 20m, "QAR", -20m, -20m)])
    };
    var digest = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", chunk);
    Guid batchId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var started = await ClientAccountingService.BeginGeneralLedgerImportAsync(db, preparer,
        new GeneralLedgerImportStartRequest(scope.ClientA, scope.EngagementA, periodId, bookId,
          "stream-v1", "gl-v1", new string('c', 64), "CLIENT-A", "QAR", "scope-stream-receipt", 1, 1, 2));
      Assert.True(started.Succeeded, started.Message);
      batchId = started.Value;
      var sealedBatch = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, digest, chunk, Finalize: true));
      Assert.True(sealedBatch.Succeeded, sealedBatch.Message);
      Assert.Equal("SEALED", sealedBatch.Value!.Status);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var crossClientReplay = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, clientBActor,
        new GeneralLedgerImportChunkRequest(batchId, 0, digest, chunk, Finalize: true));
      Assert.False(crossClientReplay.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, crossClientReplay.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GlReplayAndSourceAcceptance_UseClientBeforeBatchLockWithoutDeadlock()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, scope, preparer);
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var period = await setup.ClientReportingPeriods.SingleAsync(x => x.Id == periodId);
      period.Status = AccountingWorkflowStates.Active;
      await setup.SaveChangesAsync();
    }
    var transactions = new[]
    {
      new GeneralLedgerTransactionInput("J-LOCK-ORDER", "INV-LOCK-ORDER", new DateOnly(2026, 6, 30), null,
        "user-a", "LEDGER-A", null, false, false,
        [new("J-LOCK-ORDER-L1", "1000", 30m, 0m, "QAR", 30m, 30m),
         new("J-LOCK-ORDER-L2", "4000", 0m, 30m, "QAR", -30m, -30m)])
    };
    var digest = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", transactions);
    Guid batchId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var started = await ClientAccountingService.BeginGeneralLedgerImportAsync(setup, preparer,
        new GeneralLedgerImportStartRequest(scope.ClientA, scope.EngagementA, periodId, bookId,
          "lock-order-v1", "gl-v1", new string('d', 64), "CLIENT-A", "QAR", "lock-order-receipt", 1, 1, 2));
      Assert.True(started.Succeeded, started.Message);
      batchId = started.Value;
      var sealedBatch = await ClientAccountingService.AppendGeneralLedgerChunkAsync(setup, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, digest, transactions, Finalize: true));
      Assert.True(sealedBatch.Succeeded, sealedBatch.Message);
      Assert.Equal("SEALED", sealedBatch.Value!.Status);
    }

    var acceptanceAtBatchLock = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var resumeAcceptance = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var appendAtClientLock = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var acceptanceOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(new PauseBeforeSourceAcceptanceBatchLock(acceptanceAtBatchLock, resumeAcceptance)).Options;
    var appendOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(new ObserveGlAppendClientLock(appendAtClientLock)).Options;
    Task<CommandResult<Guid>>? acceptance = null;
    Task<CommandResult<GeneralLedgerImportBatchSummary>>? replay = null;

    try
    {
      acceptance = Task.Run(async () =>
      {
        await using var db = new AuditSphereDbContext(acceptanceOptions);
        return await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
          new AcceptSourceRevisionRequest(scope.ClientA, scope.EngagementA, AccountingSourceKinds.GeneralLedger,
            null, batchId, "lock-order-source-acceptance"), timeout.Token);
      }, timeout.Token);
      var acceptancePid = await acceptanceAtBatchLock.Task.WaitAsync(timeout.Token);

      replay = Task.Run(async () =>
      {
        await using var db = new AuditSphereDbContext(appendOptions);
        return await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
          new GeneralLedgerImportChunkRequest(batchId, 0, digest, transactions, Finalize: true), timeout.Token);
      }, timeout.Token);
      var replayPid = await appendAtClientLock.Task.WaitAsync(timeout.Token);
      var replayWaitedOnAcceptanceClientLock = await WaitForPostgresBlockerOrCompletionAsync(
        replay, replayPid, acceptancePid, pg.ConnectionString, timeout.Token);
      Assert.True(replayWaitedOnAcceptanceClientLock,
        "The replay did not wait on source acceptance's PostgreSQL client-row lock before acceptance was released.");

      resumeAcceptance.TrySetResult();
      var accepted = await acceptance.WaitAsync(timeout.Token);
      var replayed = await replay.WaitAsync(timeout.Token);
      Assert.True(accepted.Succeeded, accepted.Message);
      Assert.True(replayed.Succeeded, replayed.Message);
      Assert.Equal("SEALED", replayed.Value!.Status);

      await using var verify = new AuditSphereDbContext(pg.Options);
      var selected = await AccountingSourceAcceptanceService.GetSelectedSourceAsync(
        verify, reviewer, scope.ClientA, scope.EngagementA, AccountingSourceKinds.GeneralLedger, timeout.Token);
      Assert.True(selected.Succeeded, selected.Message);
      Assert.Equal(batchId, selected.Value!.ImportBatchId);
      Assert.Single(await verify.SourceAcceptanceDecisions.Where(x => x.ImportBatchId == batchId).ToListAsync(timeout.Token));
    }
    finally
    {
      resumeAcceptance.TrySetResult();
      await ObserveTaskCompletionAsync(acceptance);
      await ObserveTaskCompletionAsync(replay);
    }
  }

  private sealed class PauseBeforeSourceIdentityCheck(
    TaskCompletionSource reached, TaskCompletionSource resume, TaskCompletionSource<int> connectionReached) : DbCommandInterceptor
  {
    private int paused;

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("source_import_batches", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref paused, 1, 0) == 0)
      {
        if (eventData.Connection is NpgsqlConnection connection)
          connectionReached.TrySetResult(connection.ProcessID);
        reached.TrySetResult();
        await resume.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }

  private sealed class ObserveFirmRevocationLock(TaskCompletionSource<int> reached) : DbCommandInterceptor
  {
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) &&
          eventData.Connection is NpgsqlConnection connection)
        reached.TrySetResult(connection.ProcessID);
      return ValueTask.FromResult(result);
    }
  }

  private sealed class PauseBeforeSourceAcceptanceBatchLock(
    TaskCompletionSource<int> reached, TaskCompletionSource resume) : DbCommandInterceptor
  {
    private int paused;

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("source_import_batches", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("FOR SHARE", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref paused, 1, 0) == 0 &&
          eventData.Connection is NpgsqlConnection connection)
      {
        reached.TrySetResult(connection.ProcessID);
        await resume.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }

  private sealed class ObserveGlAppendClientLock(TaskCompletionSource<int> reached) : DbCommandInterceptor
  {
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("client_safety_states", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("FOR SHARE", StringComparison.OrdinalIgnoreCase) &&
          eventData.Connection is NpgsqlConnection connection)
        reached.TrySetResult(connection.ProcessID);
      return ValueTask.FromResult(result);
    }
  }

  private static async Task<bool> WaitForRevocationBlockerOrCompletionAsync(
    Task revocation, int processId, int expectedBlockerProcessId, string connectionString, CancellationToken ct)
  {
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(ct);
    await using var command = new NpgsqlCommand(
      "SELECT @blocker_pid = ANY(pg_blocking_pids(@pid)) FROM pg_stat_activity WHERE pid = @pid", connection);
    command.Parameters.AddWithValue("pid", processId);
    command.Parameters.AddWithValue("blocker_pid", expectedBlockerProcessId);
    while (true)
    {
      if (revocation.IsCompleted) return false;
      if (await command.ExecuteScalarAsync(ct) is true) return true;
      await Task.Delay(20, ct);
    }
  }

  private static async Task<bool> WaitForPostgresBlockerOrCompletionAsync(
    Task operation, int processId, int expectedBlockerProcessId, string connectionString, CancellationToken ct)
  {
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(ct);
    await using var command = new NpgsqlCommand(
      "SELECT @blocker_pid = ANY(pg_blocking_pids(@pid)) FROM pg_stat_activity WHERE pid = @pid", connection);
    command.Parameters.AddWithValue("pid", processId);
    command.Parameters.AddWithValue("blocker_pid", expectedBlockerProcessId);
    while (true)
    {
      if (operation.IsCompleted) return false;
      if (await command.ExecuteScalarAsync(ct) is true) return true;
      await Task.Delay(20, ct);
    }
  }

  private static async Task ObserveTaskCompletionAsync(Task? operation)
  {
    if (operation is null) return;
    try { await operation.WaitAsync(TimeSpan.FromSeconds(5)); }
    catch (Exception) { }
  }

  [Fact]
  [Trait("ReportingScope", "Database")]
  public async Task ReportingScopeResolver_RequiresExactFirmClientEngagementPeriodAndBook()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var (periodA, bookA) = await CreateGlFixtureAsync(pg, scope, preparer);

    Guid periodB;
    Guid bookB;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(scope.ClientB, "QA", "QAR", 1, 1, "LEDGER-B", "B-1"))).Succeeded);
      periodB = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientB, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      var createdBook = await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientB, periodB, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"));
      Assert.True(createdBook.Succeeded, createdBook.Message);
      bookB = await db.ClientReportingBooks.Where(x => x.ClientId == scope.ClientB && x.PeriodId == periodB)
        .Select(x => x.Id).SingleAsync();
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var exact = await ReportingScopeResolver.ResolveAsync(verify, scope.FirmId, scope.ClientA, scope.EngagementA,
      periodA, bookA);
    Assert.True(exact.Succeeded, exact.Message);
    Assert.Equal(scope.FirmId, exact.Value!.FirmId);
    Assert.Equal(scope.ClientA, exact.Value.ClientId);
    Assert.Equal(scope.EngagementA, exact.Value.EngagementId);
    Assert.Equal(periodA, exact.Value!.Period.Id);
    Assert.Equal(bookA, exact.Value.Book!.Id);

    var otherFirm = await ReportingScopeResolver.ResolveAsync(verify, Guid.NewGuid(), scope.ClientA,
      scope.EngagementA, periodA, bookA);
    Assert.False(otherFirm.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, otherFirm.ErrorCode);

    var siblingEngagement = await ReportingScopeResolver.ResolveAsync(verify, scope.FirmId, scope.ClientA,
      scope.EngagementB, periodA, bookA);
    Assert.False(siblingEngagement.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, siblingEngagement.ErrorCode);

    var siblingPeriod = await ReportingScopeResolver.ResolveAsync(verify, scope.FirmId, scope.ClientA,
      scope.EngagementA, periodB, bookB);
    Assert.False(siblingPeriod.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, siblingPeriod.ErrorCode);

    var siblingBook = await ReportingScopeResolver.ResolveAsync(verify, scope.FirmId, scope.ClientA,
      scope.EngagementA, periodA, bookB);
    Assert.False(siblingBook.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, siblingBook.ErrorCode);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GlImport_RejectsUndefinedClientDimensionValue()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var fixture = await CreateGlFixtureAsync(pg, scope, preparer);
    var request = new GeneralLedgerImportRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, fixture.BookId,
      "csv-v1", "gl-v1", new string('9', 64), "CLIENT-A", "QAR", "dimension-test",
      [new("J-DIM", "INV-DIM", new DateOnly(2026, 6, 30), null, "user-a", "LEDGER-A", null, false, false,
        [new("J-DIM-L1", "1000", 10m, 0m, "QAR", 10m, 10m, Department: "FINANCE"),
         new("J-DIM-L2", "4000", 0m, 10m, "QAR", -10m, -10m)])]);

    await using var db = new AuditSphereDbContext(pg.Options);
    var blocked = await ClientAccountingService.ImportGeneralLedgerAsync(db, preparer, request);
    Assert.False(blocked.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.ImportRejected, blocked.ErrorCode);
    Assert.Contains("dimension", blocked.Message!, StringComparison.OrdinalIgnoreCase);

    Assert.True((await ClientAccountingService.AddDimensionDefinitionsAsync(db, preparer, scope.ClientA,
      [new("DEPARTMENT", "FINANCE", "Finance")])).Succeeded);
    var imported = await ClientAccountingService.ImportGeneralLedgerAsync(db, preparer, request);
    Assert.True(imported.Succeeded, imported.Message);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task StreamingGlImport_IsIdempotentAndSealsOnlyCompleteBatch()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, scope, preparer);
    var chunkOne = new[]
    {
      new GeneralLedgerTransactionInput("J-1", "INV-1", new DateOnly(2026, 6, 30), null, "user-a", "LEDGER-A", null, false, false,
        [new("J-1-L1", "1000", 100m, 0m, "QAR", 100m, 100m), new("J-1-L2", "4000", 0m, 100m, "QAR", -100m, -100m)],
        new DateOnly(2026, 6, 29))
    };
    var chunkTwo = new[]
    {
      new GeneralLedgerTransactionInput("J-2", "INV-2", new DateOnly(2026, 7, 1), null, "user-a", "LEDGER-A", null, false, false,
        [new("J-2-L1", "1000", 25m, 0m, "USD", 20m, 25m), new("J-2-L2", "4000", 0m, 25m, "QAR", -25m, -25m)])
    };
    var digestOne = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", chunkOne);
    var digestTwo = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", chunkTwo);
    var noServiceDateChunk = chunkOne.Select(x => x with { ServiceDate = null }).ToArray();
    Assert.NotEqual(digestOne, ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", noServiceDateChunk));
    var sourceCurrencyVariant = chunkOne.Select(x => x with
    {
      Lines = x.Lines.Select((line, index) => index == 0
        ? line with { OriginalCurrency = "USD", OriginalAmount = 27.027m }
        : line).ToArray()
    }).ToArray();
    Assert.NotEqual(digestOne, ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", sourceCurrencyVariant));
    var normalizedCaseVariant = chunkOne.Select(x => x with
    {
      Lines = x.Lines.Select((line, index) => index == 0
        ? line with { OriginalCurrency = " qar ", StableLineId = " J-1-L1 ", AccountCode = " 1000 " }
        : line).ToArray()
    }).ToArray();
    Assert.Equal(digestOne, ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", normalizedCaseVariant));
    Guid batchId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var started = await ClientAccountingService.BeginGeneralLedgerImportAsync(db, preparer,
        new GeneralLedgerImportStartRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "stream-v1", "gl-v1",
          new string('9', 64), "CLIENT-A", "QAR", "stream-receipt", 2, 2, 4));
      Assert.True(started.Succeeded, started.Message);
      batchId = started.Value;

      var invalidChunk = chunkOne.Select(x => x with
      {
        Lines = x.Lines.Select((line, index) => index == 0 ? line with { FunctionalAmount = 99m } : line).ToArray()
      }).ToArray();
      var invalidDigest = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", invalidChunk);
      var invalid = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, invalidDigest, invalidChunk, Finalize: false));
      Assert.False(invalid.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ImportRejected, invalid.ErrorCode);

      var first = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, digestOne, chunkOne, Finalize: false));
      Assert.True(first.Succeeded, first.Message);
      Assert.Equal("LOADING", first.Value!.Status);
      Assert.Equal(1, first.Value.AcceptedChunkCount);

      var retry = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, digestOne, chunkOne, Finalize: false));
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(1, retry.Value!.AcceptedChunkCount);

      var premature = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, digestOne, chunkOne, Finalize: true));
      Assert.False(premature.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, premature.ErrorCode);

      var final = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 1, digestTwo, chunkTwo, Finalize: true));
      Assert.True(final.Succeeded, final.Message);
      Assert.Equal("SEALED", final.Value!.Status);
      Assert.Equal(2, final.Value.AcceptedChunkCount);
      Assert.Equal(2, final.Value.AcceptedTransactionCount);
      Assert.Equal(4, final.Value.AcceptedLineCount);
      Assert.NotNull(final.Value.NormalizedDatasetDigest);
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal("SEALED", await verify.SourceImportBatches.Where(x => x.Id == batchId).Select(x => x.Status).SingleAsync());
    Assert.Equal(2, await verify.GeneralLedgerImportChunks.CountAsync(x => x.ImportBatchId == batchId));
    Assert.Equal(2, await verify.GeneralLedgerTransactions.CountAsync(x => x.ImportBatchId == batchId));
    Assert.Equal(4, await verify.GeneralLedgerLines.CountAsync(x => x.ImportBatchId == batchId));
    var foreignLine = await verify.GeneralLedgerLines.SingleAsync(x => x.ImportBatchId == batchId && x.StableLineId == "J-2-L1");
    Assert.Equal("USD", foreignLine.OriginalCurrency);
    Assert.Equal(20m, foreignLine.OriginalAmount);
    Assert.Equal(25m, foreignLine.FunctionalAmount);
    Assert.Equal(new DateOnly(2026, 6, 29), await verify.GeneralLedgerTransactions.Where(x => x.ImportBatchId == batchId && x.StableJournalId == "J-1").Select(x => x.ServiceDate).SingleAsync());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GlCompletenessBridge_IsAccountExactAndPagedWithinScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var digest = Hashing.Sha256Hex("completeness-fixture");
    Guid priorPeriodId, periodId, priorBookId, bookId, datasetId, openingDatasetId, batchId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      priorPeriodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, priorPeriodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded);
      priorBookId = await db.ClientReportingBooks.Where(x => x.ClientId == scope.ClientA && x.PeriodId == priorPeriodId).Select(x => x.Id).SingleAsync();
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR", priorPeriodId))).Value;
      Assert.True((await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded);
      bookId = await db.ClientReportingBooks.Where(x => x.ClientId == scope.ClientA && x.PeriodId == periodId).Select(x => x.Id).SingleAsync();
      datasetId = Guid.CreateVersion7();
      openingDatasetId = Guid.CreateVersion7();
      var openingDigest = Hashing.Sha256Hex("completeness-opening-fixture");
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = openingDatasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = priorPeriodId, BookId = priorBookId, Basis = "STATUTORY",
        SourceKind = "Raw", LegalEntityKey = "CLIENT-A", Currency = "QAR", RawFileSha256Hex = openingDigest,
        NormalizedDatasetDigest = openingDigest, Sha256Hex = openingDigest, Balanced = true, ValidationStatus = "Pending",
        ImportState = TrialBalanceImportStates.Loading, ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = scope.Preparer.Id
      });
      db.TrialBalanceRows.AddRange(
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = openingDatasetId, AccountCode = "1000", AccountName = "Cash", Amount = 0m, Currency = "QAR", Entity = "CLIENT-A" },
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = openingDatasetId, AccountCode = "4000", AccountName = "Revenue", Amount = 0m, Currency = "QAR", Entity = "CLIENT-A" });
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY",
        SourceKind = "Raw", LegalEntityKey = "CLIENT-A", Currency = "QAR", RawFileSha256Hex = digest,
        NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true, ValidationStatus = "Pending",
        ImportState = TrialBalanceImportStates.Loading, ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = scope.Preparer.Id
      });
      db.TrialBalanceRows.AddRange(
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "CLIENT-A" },
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "4000", AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "CLIENT-A" });
      batchId = Guid.CreateVersion7();
      db.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = batchId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA, PeriodId = periodId,
        BookId = bookId, SourceKind = "GL", ProfileVersion = "gl-v1", ParserVersion = "parser-v1", RawFileSha256Hex = digest,
        NormalizedDatasetDigest = digest, LegalEntityKey = "CLIENT-A", Currency = "QAR", RowCount = 1,
        Status = "SEALED", ReceiptReference = "gl-receipt", CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      var transactionId = Guid.CreateVersion7();
      db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
      {
        Id = transactionId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        ImportBatchId = batchId, StableJournalId = "J-1", DocumentNumber = "DOC-1", PostingDate = new DateOnly(2026, 6, 30),
        SourceUser = "user-a", SourceSystem = "LEDGER-A", Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
      });
      db.GeneralLedgerLines.AddRange(
        new GeneralLedgerLine { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA, ImportBatchId = batchId, TransactionId = transactionId, StableLineId = "J-1-L1", AccountCode = "1000", Debit = 100m, OriginalCurrency = "QAR", OriginalAmount = 100m, FunctionalAmount = 100m, CreatedAt = DateTimeOffset.UtcNow },
        new GeneralLedgerLine { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA, ImportBatchId = batchId, TransactionId = transactionId, StableLineId = "J-1-L2", AccountCode = "4000", Credit = 100m, OriginalCurrency = "QAR", OriginalAmount = -100m, FunctionalAmount = -100m, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      var openingDataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == openingDatasetId);
      openingDataset.ValidationStatus = "Accepted";
      openingDataset.ImportState = TrialBalanceImportStates.Sealed;
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      dataset.ValidationStatus = "Accepted";
      dataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();
    }

    var factory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var operationStore = new PostgresOperationStore(factory);
    var operationHandler = new GeneralLedgerCompletenessHandler();
    var workerOptions = new WorkerOptions(scope.FirmId, "Test");
    Guid operationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await AccountingAnalysisService.EnqueueGeneralLedgerCompletenessBridgeAsync(db, preparer,
        new GeneralLedgerCompletenessRequest(scope.ClientA, scope.EngagementA, periodId, bookId, datasetId, batchId, "tb-gl-completeness", openingDatasetId),
        operationStore, operationHandler);
      Assert.True(queued.Succeeded, queued.Message);
      operationId = queued.Value;
    }
    var worker = new WorkerHost(
      new OperationDispatcher(operationStore, new DurableOperationRegistry([operationHandler], workerOptions), workerOptions),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var retry = await AccountingAnalysisService.EnqueueGeneralLedgerCompletenessBridgeAsync(db, preparer,
        new GeneralLedgerCompletenessRequest(scope.ClientA, scope.EngagementA, periodId, bookId, datasetId, batchId, "tb-gl-completeness", openingDatasetId),
        operationStore, operationHandler);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(operationId, retry.Value);
      var bridge = await db.GeneralLedgerCompletenessBridges.SingleAsync();
      Assert.Equal("RECONCILED", bridge.Status);
      Assert.Equal(2, bridge.MatchedAccountCount);
      Assert.Equal(0m, bridge.AbsoluteResidual);
      Assert.Equal(openingDatasetId, bridge.OpeningTrialBalanceDatasetId);
      Assert.Equal(0m, bridge.OpeningMovementResidual);
      Assert.Equal(0, bridge.OpeningMovementMismatchedAccountCount);
      Assert.Equal(0, bridge.JournalExceptionCount);
      Assert.False(bridge.IncompleteExtract);
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      Assert.Equal(bridge.Id.ToString("D"), operation.ResultIdentity);
      Assert.True(await db.OperationEvents.AnyAsync(x => x.OperationId == operationId && x.Kind == "gl.completeness-built.v1"));

      var firstPage = await AccountingAnalysisService.GetGeneralLedgerPageAsync(db, preparer, batchId, 1, 1);
      Assert.True(firstPage.Succeeded, firstPage.Message);
      Assert.Single(firstPage.Value!.Rows);
      Assert.True(firstPage.Value.HasNextPage);
      var secondPage = await AccountingAnalysisService.GetGeneralLedgerPageAsync(db, preparer, batchId, 2, 1);
      Assert.True(secondPage.Succeeded, secondPage.Message);
      Assert.Single(secondPage.Value!.Rows);
      Assert.False(secondPage.Value.HasNextPage);

      Assert.True((await AccountingAnalysisService.ReviewGeneralLedgerCompletenessAsync(db, reviewer, bridge.Id, approve: true)).Succeeded);
      Assert.Equal(AccountingWorkflowStates.Approved, await db.GeneralLedgerCompletenessBridges.Where(x => x.Id == bridge.Id).Select(x => x.Status).SingleAsync());

      var sourceRows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == datasetId)
        .OrderBy(x => x.Id).ToArrayAsync();
      Assert.Equal(2, sourceRows.Length);
      Assert.Contains(sourceRows, x => x.Amount != 0m);
      var sourceLines = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.ImportBatchId == batchId)
        .OrderBy(x => x.Id).ToArrayAsync();
      Assert.Equal(2, sourceLines.Length);

      await AssertPracticeTimePreservesClientBooksAsync(pg, scope, periodId);
    }
  }

  private static async Task AssertPracticeTimePreservesClientBooksAsync(PgTestSchema pg, Scope scope, Guid periodId)
  {
    async Task<string> CaptureClientBooksAsync()
    {
      await using var snapshotDb = new AuditSphereDbContext(pg.Options);
      var datasets = await snapshotDb.TrialBalanceDatasets.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var datasetIds = datasets.Select(x => x.Id).ToArray();
      var trialBalanceRows = await snapshotDb.TrialBalanceRows.AsNoTracking().Where(x => datasetIds.Contains(x.DatasetId))
        .OrderBy(x => x.DatasetId).ThenBy(x => x.Id).ToArrayAsync();
      var trialBalanceBatches = await snapshotDb.TrialBalanceImportBatches.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var sourceBatches = await snapshotDb.SourceImportBatches.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var generalLedgerChunks = await snapshotDb.GeneralLedgerImportChunks.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var transactions = await snapshotDb.GeneralLedgerTransactions.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var generalLedgerLines = await snapshotDb.GeneralLedgerLines.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var journals = await snapshotDb.ClientOperationalJournals.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var journalLines = await snapshotDb.ClientOperationalJournalLines.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var decisions = await snapshotDb.ClientOperationalJournalDecisions.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var journalSnapshots = await snapshotDb.ClientOperationalJournalSnapshots.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var postingReceipts = await snapshotDb.ClientOperationalPostingReceipts.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var reversals = await snapshotDb.ClientOperationalJournalReversals.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();
      var openingBalances = await snapshotDb.ClientOperationalOpeningBalances.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.Id).ToArrayAsync();

      return System.Text.Json.JsonSerializer.Serialize(new
      {
        datasets, trialBalanceRows, trialBalanceBatches, sourceBatches, generalLedgerChunks, transactions, generalLedgerLines,
        journals, journalLines, decisions, journalSnapshots, postingReceipts, reversals, openingBalances
      });
    }

    var staffActor = Actor(scope.Preparer, "Staff");
    var reviewerActor = Actor(scope.Reviewer, "Partner");
    var rateManager = User(scope.FirmId, "TimeIsolationRateManager");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(rateManager);
      db.RoleGrants.Add(Grant(scope.FirmId, rateManager, "Manager"));
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "Staff", scope.ClientA, scope.EngagementA));
      await db.SaveChangesAsync();

      var rate = await PracticeTimeService.ReviseRateCardAsync(db, Actor(rateManager, "Manager"),
        new RateCardDraftRequest("Staff", "Client book isolation", "QAR", 100m));
      Assert.True(rate.Succeeded, rate.Message);
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, reviewerActor, rate.Value)).Succeeded);
    }

    var before = await CaptureClientBooksAsync();
    Guid originalEntryId;
    Guid correctionEntryId;
    Guid rateCardVersionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      rateCardVersionId = await db.RateCardVersions.AsNoTracking()
        .Where(x => x.FirmId == scope.FirmId && x.Role == "Staff" && x.Activity == "Client book isolation" && x.Currency == "QAR")
        .Select(x => x.Id).SingleAsync();
      var task = await PracticeTimeService.CreateTaskAsync(db, staffActor,
        new CreateTaskRequest("Record client book isolation time", scope.ClientA, scope.EngagementA,
          AssigneeUserId: staffActor.UserId, ReportingPeriodId: periodId));
      Assert.True(task.Succeeded, task.Message);
      var draft = await PracticeTimeService.SaveTimeDraftAsync(db, staffActor,
        new SaveTimeDraftRequest(task.Value, new DateOnly(2026, 6, 30), 9 * 60, 60,
          "Staff", "Client book isolation", Currency: "QAR"));
      Assert.True(draft.Succeeded, draft.Message);
      originalEntryId = draft.Value;
      var original = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == originalEntryId);
      Assert.Equal(PracticeTimeStates.Billable, original.BillableClassification);
      Assert.Equal(rateCardVersionId, original.RateCardVersionId);
      Assert.Equal(100m, original.RatePerHour);
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, staffActor, originalEntryId)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, reviewerActor, originalEntryId)).Succeeded);

      var correction = await PracticeTimeService.CorrectTimeAsync(db, staffActor,
        new CorrectTimeRequest(originalEntryId, new DateOnly(2026, 6, 30), 9 * 60, 75,
          "Client book isolation", "Corrected synthetic time", Reason: "Corrected duration"));
      Assert.True(correction.Succeeded, correction.Message);
      correctionEntryId = correction.Value;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, staffActor, correctionEntryId)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, reviewerActor, correctionEntryId)).Succeeded);

      var originalAfterCorrection = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == originalEntryId);
      var corrected = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == correctionEntryId);
      Assert.Equal(PracticeTimeStates.TimeSuperseded, originalAfterCorrection.Status);
      Assert.Equal(60, originalAfterCorrection.DurationMinutes);
      Assert.Equal(rateCardVersionId, originalAfterCorrection.RateCardVersionId);
      Assert.Equal(100m, originalAfterCorrection.RatePerHour);
      Assert.Equal(PracticeTimeStates.TimeApproved, corrected.Status);
      Assert.Equal(originalEntryId, corrected.SupersedesId);
      Assert.Equal(75, corrected.DurationMinutes);
      Assert.Equal(rateCardVersionId, corrected.RateCardVersionId);
      Assert.Equal(100m, corrected.RatePerHour);
      Assert.Equal(PracticeTimeStates.Billable, corrected.BillableClassification);
    }

    var after = await CaptureClientBooksAsync();
    Assert.Equal(before, after);
  }
}
