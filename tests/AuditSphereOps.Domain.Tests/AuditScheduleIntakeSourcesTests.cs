using System.Text;
using System.Text.Json;
using System.Globalization;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditScheduleIntakeSourcesTests
{
  [Fact]
  public async Task EmptySourceCaptureSchemaCanRollBackAndForward()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using var db = new AuditSphereDbContext(pg.Options);
    var migrations = db.Database.GetMigrations().ToArray();
    var captureMigration = migrations.Single(x => x.EndsWith("_AuditScheduleSourceIntake", StringComparison.Ordinal));
    await db.GetService<IMigrator>().MigrateAsync(migrations.TakeWhile(x => x != captureMigration).Last());
    Assert.DoesNotContain(captureMigration, await db.Database.GetAppliedMigrationsAsync());
    await db.GetService<IMigrator>().MigrateAsync(captureMigration);
    Assert.Empty(await db.AuditScheduleSourceCaptures.AsNoTracking().ToListAsync());
    Assert.Empty(await db.AuditScheduleSourceReviews.AsNoTracking().ToListAsync());
    Assert.False(db.Database.HasPendingModelChanges());
  }

  [Fact]
  public async Task AcceptedPbcListingAndSelectedTbCapturePreserveAccountVarianceReceiptsAndOriginalRows()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var ct = timeout.Token;
    var f = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Reviewer");
    var client = PbcSeed.Actor(f.Client, "ClientUser");
    var accountingReviewer = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    var sourceDate = new DateOnly(2026, 12, 31);
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    Guid requestId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer", f.ClientId, f.EngagementId));
      setup.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "FY2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = sourceDate, Basis = "STATUTORY", Currency = "QAR",
        Status = AccountingWorkflowStates.Active, CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      setup.ClientReportingBooks.Add(new ClientReportingBook
      {
        Id = bookId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodId = periodId,
        Code = "STATUTORY", Basis = "STATUTORY", Currency = "QAR", InclusionRule = "ALL_ENTITIES",
        Status = AccountingWorkflowStates.Active, CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await setup.SaveChangesAsync(ct);
      var created = await PbcService.CreateRequestAsync(setup, staff, new(
        f.EngagementId, "Receivables", "TEST ENTITY", "2026-01-01", "2026-12-31", "Receivables",
        "CSV", "Complete selected listing", f.Client.Id, f.Staff.Id, f.Reviewer.Id,
        "2027-01-31", "Confidential", "Synthetic exact account listing for source resolver verification."), ct);
      Assert.True(created.Succeeded, created.Message);
      requestId = created.Value;
      var sent = await PbcService.ChangeStateAsync(setup, staff, new(requestId, PbcStates.Sent, 1), ct);
      Assert.True(sent.Succeeded, sent.Message);
      var acknowledged = await PbcService.ChangeStateAsync(setup, client,
        new(requestId, PbcStates.Acknowledged, 2), ct);
      Assert.True(acknowledged.Succeeded, acknowledged.Message);
    }

    var csv = Encoding.UTF8.GetBytes("AccountCode,Description,SignedAmount,Currency,StableRowId,TransactionDate\r\n" +
      "0010,Trade A,125,QAR,row-a,2025-12-15\r\n0020,Trade B,75,QAR,row-b,2026-11-30\r\n");
    var staged = await PbcSeed.StageUploadAsync(pg, f, client, requestId, csv, "receivables.csv");
    try
    {
      // Local simulation uses the real durable transfer/verification boundary. No LIVE provider is invoked.
      var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
      var store = new PostgresOperationStore(factory);
      var handler = new PbcDocumentTransferHandler(factory,
        new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider")));
      await using (var complete = new AuditSphereDbContext(pg.Options))
      {
        var completed = await PbcService.CompleteUploadAsync(complete, staff,
          new(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler, ct);
        Assert.True(completed.Succeeded, completed.Message);
      }
      var options = new WorkerOptions(f.FirmId, "Test", AllowSimulationAdapters: true);
      var registry = new DurableOperationRegistry([handler], options);
      var discovery = new PbcTransferDiscovery(factory, store, handler, options);
      var worker = new AuditSphereOps.Worker.Worker(new OperationDispatcher(store, registry, options),
        [discovery], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
      Assert.True(await worker.ProcessNextAsync(ct));

      Guid receiptId;
      await using (var accept = new AuditSphereDbContext(pg.Options))
      {
        var request = await accept.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, ct);
        Assert.Equal(PbcStates.Received, request.State);
        var underReview = await PbcService.ChangeStateAsync(accept, reviewer,
          new(requestId, PbcStates.UnderReview, request.Revision), ct);
        Assert.True(underReview.Succeeded, underReview.Message);
        var current = await accept.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, ct);
        var upload = await accept.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId, ct);
        var accepted = await PbcService.ChangeStateAsync(accept, reviewer,
          new(requestId, PbcStates.Accepted, current.Revision, ReviewedUploadIntentId: upload.Id,
            ReviewedUploadRevision: upload.Revision, ReviewedContentSha256Hex: staged.DeclaredSha256Hex), ct);
        Assert.True(accepted.Succeeded, accepted.Message);
        var receipt = await accept.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId, ct);
        receiptId = receipt.Id;
        Assert.Equal(f.Reviewer.Id, receipt.ReviewerUserId);
        Assert.Equal(f.Client.Id, receipt.UploaderUserId);
        Assert.Equal(staged.DeclaredSha256Hex, receipt.ContentSha256Hex);
        var operation = await accept.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == receipt.TransferOperationId, ct);
        Assert.Equal(upload.Revision - 1, operation.ExpectedRevision);
        Assert.Equal(receipt.ProviderIdentity, operation.ResultIdentity);
      }

      // Seed raw TB membership only while LOADING; acceptance is a real independent service decision.
      var datasetId = Guid.NewGuid();
      var tbBytes = Encoding.UTF8.GetBytes("AccountCode,Amount,Currency,Entity\n" +
        "0010,100,QAR,TEST ENTITY\n0020,100,QAR,TEST ENTITY\n4000,-200,QAR,TEST ENTITY\n");
      var tbHash = Hashing.Sha256Hex(tbBytes);
      Guid decisionId;
      await using (var acceptTb = new AuditSphereDbContext(pg.Options))
      {
        var dataset = new TrialBalanceDataset
        {
          Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          PeriodId = periodId, BookId = bookId, Basis = "STATUTORY", SourceKind = "Raw", Revision = 1,
          LegalEntityKey = "TEST ENTITY", Currency = "QAR", RawFileSha256Hex = tbHash,
          NormalizedDatasetDigest = tbHash, Sha256Hex = tbHash, Balanced = true, ControlTotal = 0m,
          ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
          ImportedByUserId = f.Staff.Id, ImportedAt = DateTimeOffset.UtcNow
        };
        acceptTb.TrialBalanceDatasets.Add(dataset);
        acceptTb.TrialBalanceRows.AddRange(new[] { ("0010", 100m), ("0020", 100m), ("4000", -200m) }.Select(x =>
          new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = x.Item1,
            AccountName = "Synthetic " + x.Item1, Amount = x.Item2, Currency = "QAR", Entity = "TEST ENTITY" }));
        await acceptTb.SaveChangesAsync(ct);
        dataset.ImportState = TrialBalanceImportStates.Sealed;
        dataset.ValidationStatus = "Accepted";
        await acceptTb.SaveChangesAsync(ct);
        var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(acceptTb, accountingReviewer,
          new(f.ClientId, f.EngagementId, AccountingSourceKinds.TrialBalance, datasetId, null,
            "Synthetic independent accepted TB closing source"), ct);
        Assert.True(accepted.Succeeded, accepted.Message);
        decisionId = accepted.Value;
      }

      var reader = new PbcStagedScheduleContentReader(staged.StagingRoot);
      var columns = new AuditScheduleCsvColumns("AccountCode", "Description", "SignedAmount", "Currency",
        "StableRowId", "TransactionDate");
      await using var resolve = new AuditSphereDbContext(pg.Options);
      var receiptFacts = await resolve.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.Id == receiptId, ct);
      var result = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, reader, f.ClientId, f.EngagementId,
        receiptId, decisionId, sourceDate, columns, ["0010", "0020"], ct);
      // Do not round/backfill service acceptance timestamps: this genuine path must expose digest precision mismatches.
      Assert.True(result.Succeeded, $"{result.ErrorCode}: {result.Message}; actual receipt={receiptId}, " +
        $"AcceptedAt={receiptFacts.AcceptedAt:O}, stored ticks remainder={receiptFacts.AcceptedAt.Ticks % 10}, " +
        $"retained SourceDigest={receiptFacts.SourceDigest}");
      var facts = result.Value!;
      Assert.Equal(PbcStates.Accepted, facts.CurrentPbcRequestState);
      Assert.Equal(200m, facts.SignedTotal);
      Assert.Equal(200m, facts.ClosingControl);
      Assert.Equal(0m, facts.Residual);
      Assert.True(facts.HasCompleteListingCoverage);
      Assert.False(facts.AllAccountsReconciled);
      Assert.Equal(new[] { "0010", "0020" }, facts.AccountTieOuts.Select(x => x.AccountCode));
      Assert.Equal(new[] { 25m, -25m }, facts.AccountTieOuts.Select(x => x.Difference));
      Assert.Equal(new DateOnly(2025, 12, 15), facts.Rows[0].TransactionDate);
      Assert.Equal(receiptId, facts.PbcAcceptanceReceiptId);
      Assert.Equal(decisionId, facts.AcceptedTbDecisionId);
      Assert.Equal(datasetId, facts.TrialBalanceDatasetId);
      Assert.Equal(bookId, facts.BookId);
      Assert.Equal(sourceDate, facts.SourceDate);
      Assert.Equal(staged.DeclaredSha256Hex, facts.RawFileSha256);
      var repeat = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, reader, f.ClientId, f.EngagementId,
        receiptId, decisionId, sourceDate, columns, ["0020", "0010"], ct);
      Assert.True(repeat.Succeeded, repeat.Message);
      Assert.Equal(facts.ManifestJson, repeat.Value!.ManifestJson);
      Assert.Equal(facts.ManifestSha256, repeat.Value.ManifestSha256);

      var undeclared = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, reader, f.ClientId, f.EngagementId,
        receiptId, decisionId, sourceDate, columns, ["0010"], ct);
      Assert.False(undeclared.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, undeclared.ErrorCode);
      var wrongScope = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, reader, Guid.NewGuid(), f.EngagementId,
        receiptId, decisionId, sourceDate, columns, ["0010", "0020"], ct);
      Assert.False(wrongScope.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongScope.ErrorCode);
      var wrongDate = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, reader, f.ClientId, f.EngagementId,
        receiptId, decisionId, sourceDate.AddDays(-1), columns, ["0010", "0020"], ct);
      Assert.False(wrongDate.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, wrongDate.ErrorCode);

      // Supported closure changes the current request state, not its immutable accepted source.
      var retainedReceiptJson = JsonSerializer.Serialize(receiptFacts);
      var generationBeforeClose = await resolve.ClientSafetyStates.AsNoTracking()
        .Where(x => x.FirmId == f.FirmId && x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync(ct);
      long closedRevision;
      await using (var close = new AuditSphereDbContext(pg.Options))
      {
        var acceptedRequest = await close.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, ct);
        Assert.Equal(PbcStates.Accepted, acceptedRequest.State);
        Assert.Equal(receiptFacts.AcceptedRequestRevision, acceptedRequest.Revision);
        var closed = await PbcService.ChangeStateAsync(close, reviewer,
          new(requestId, PbcStates.Closed, acceptedRequest.Revision), ct);
        Assert.True(closed.Succeeded, closed.Message);
        var closedRequest = await close.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, ct);
        Assert.Equal(PbcStates.Closed, closedRequest.State);
        closedRevision = closedRequest.Revision;
        Assert.Equal(receiptFacts.AcceptedRequestRevision + 1, closedRevision);
        Assert.Equal(receiptFacts.ReviewerUserId, closedRequest.AcceptedByUserId);
        Assert.Equal(receiptFacts.AcceptedAt, closedRequest.AcceptedAt);
        Assert.Equal(retainedReceiptJson, JsonSerializer.Serialize(
          await close.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.Id == receiptId, ct)));
      }
      Assert.Equal(generationBeforeClose, await resolve.ClientSafetyStates.AsNoTracking()
        .Where(x => x.FirmId == f.FirmId && x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync(ct));
      var afterClose = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, reader, f.ClientId, f.EngagementId,
        receiptId, decisionId, sourceDate, columns, ["0010", "0020"], ct);
      Assert.True(afterClose.Succeeded,
        $"Supported assigned-reviewer closure must preserve the accepted source. {afterClose.ErrorCode}: {afterClose.Message}");
      Assert.Equal(facts.ManifestJson, afterClose.Value!.ManifestJson);
      Assert.Equal(facts.ManifestSha256, afterClose.Value.ManifestSha256);
      Assert.Equal(facts.RawFileSha256, afterClose.Value.RawFileSha256);
      Assert.Equal(facts.PbcSourceDigest, afterClose.Value.PbcSourceDigest);
      Assert.Equal(receiptId, afterClose.Value.PbcAcceptanceReceiptId);
      Assert.Equal(closedRevision, afterClose.Value.PbcRequestRevision);
      Assert.Equal(PbcStates.Closed, afterClose.Value.CurrentPbcRequestState);
      // Current workflow state belongs outside immutable source identity; never pretend CLOSED is ACCEPTED.
      using (var manifest = JsonDocument.Parse(afterClose.Value.ManifestJson))
      {
        var sourceRequest = manifest.RootElement.GetProperty("Pbc").GetProperty("Request");
        Assert.False(sourceRequest.TryGetProperty("State", out _));
        Assert.False(sourceRequest.TryGetProperty("Revision", out _));
      }

      Assert.Empty(await resolve.AuditSchedules.AsNoTracking().ToListAsync(ct));
      Assert.Empty(await resolve.AuditScheduleRows.AsNoTracking().ToListAsync(ct));
      var captureId = Guid.NewGuid();
      var captureFields = new AuditScheduleSourceFields(receiptId, decisionId, sourceDate, columns, ["0020", "0010"]);
      AuditScheduleCaptureRequest captureRequest;
      AuditScheduleSourceReceiptView originalCapture;
      await using (var command = new AuditSphereDbContext(pg.Options))
      {
        var preview = await AuditScheduleSourceWorkspace.PreviewCaptureAsync(command, staff, reader,
          f.EngagementId, captureId, captureFields, ct);
        Assert.True(preview.Succeeded, preview.Message);
        Assert.Equal(new[] { "25.000000", "-25.000000" }, preview.Value!.Accounts.Select(x => x.Difference));
        Assert.False(preview.Value.AllAccountsReconciled);
        captureRequest = new(captureId, preview.Value.CanonicalInput, preview.Value.ManifestHash,
          long.Parse(preview.Value.InputGeneration, CultureInfo.InvariantCulture),
          long.Parse(preview.Value.PeriodRevision, CultureInfo.InvariantCulture), preview.Value.RequestHash, true);
        var notYetRecorded = await AuditScheduleSourceWorkspace.GetCaptureReceiptAsync(command, staff,
          f.EngagementId, captureId, captureRequest.ExpectedRequestHash, ct);
        Assert.True(notYetRecorded.Succeeded, notYetRecorded.Message);
        Assert.False(notYetRecorded.Value!.Found);
        Assert.Null(notYetRecorded.Value.Receipt);
        var noAssent = await AuditScheduleSourceWorkspace.CaptureAsync(command, staff, reader,
          f.EngagementId, captureRequest with { Reviewed = false }, ct);
        Assert.False(noAssent.Succeeded);
        Assert.Empty(await command.AuditSchedules.AsNoTracking().ToListAsync(ct));
        await using (var interrupted = new AuditSphereDbContext(
          new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
            .AddInterceptors(new InterruptScheduleSave()).Options))
        {
          await Assert.ThrowsAsync<OperationCanceledException>(() =>
            AuditScheduleSourceWorkspace.CaptureAsync(interrupted, staff, reader,
              f.EngagementId, captureRequest, ct));
          Assert.DoesNotContain(interrupted.ChangeTracker.Entries(), x =>
            x.Entity is AuditSchedule or AuditScheduleRow or AuditScheduleSourceCapture);
          await interrupted.SaveChangesAsync(ct);
          Assert.Empty(await interrupted.AuditSchedules.AsNoTracking().ToListAsync(ct));
        }
        await using var competing = new AuditSphereDbContext(pg.Options);
        var captures = await Task.WhenAll(
          AuditScheduleSourceWorkspace.CaptureAsync(command, staff, reader, f.EngagementId, captureRequest, ct),
          AuditScheduleSourceWorkspace.CaptureAsync(competing, staff, reader, f.EngagementId, captureRequest, ct));
        Assert.All(captures, captured => Assert.True(captured.Succeeded, captured.Message));
        Assert.Equal(captures[0].Value, captures[1].Value);
        originalCapture = captures[0].Value!;
        Assert.Equal("UNRECONCILED", originalCapture.Receipt.Status); // Net-zero is not per-account reconciliation.
        Assert.Equal("200.000000", originalCapture.Receipt.SignedTotal);
        Assert.Equal("200.000000", originalCapture.Receipt.ClosingControl);
        Assert.Equal("0.000000", originalCapture.Receipt.Residual);
        Assert.Equal(f.Staff.Id, originalCapture.Receipt.ActorUserId);
        Assert.Equal(preview.Value.ManifestHash, originalCapture.Receipt.ManifestHash);
      }
      await using (var repeatCapture = new AuditSphereDbContext(pg.Options))
      {
        var replay = await AuditScheduleSourceWorkspace.CaptureAsync(repeatCapture, staff, reader,
          f.EngagementId, captureRequest, ct);
        Assert.True(replay.Succeeded, replay.Message);
        Assert.Equal(originalCapture, replay.Value);
        Assert.Equal(1, await repeatCapture.AuditScheduleSourceCaptures.CountAsync(ct));
        Assert.Equal(1, await repeatCapture.AuditSchedules.CountAsync(ct));
        var core = await repeatCapture.AuditSchedules.AsNoTracking().SingleAsync(ct);
        Assert.Equal(decisionId, core.AcceptedSourceDecisionId);
        Assert.Equal("UNRECONCILED", core.Status);
        var legacyReview = await AuditFieldworkService.ReviewScheduleAsync(repeatCapture, reviewer,
          new(core.Id, "Legacy review cannot replace retained source review", false), ct);
        Assert.Equal(ErrorCodes.ProtectedState, legacyReview.ErrorCode);
        Assert.Equal(new[] { 125m, 75m }, await repeatCapture.AuditScheduleRows.AsNoTracking()
          .OrderBy(x => x.SourceLineNumber).Select(x => x.SignedAmount).ToArrayAsync(ct));
        // A fresh valid preview is not permission to reuse an earlier request identity for another column map.
        var remapped = captureFields with { Columns = columns with { Description = "StableRowId", StableRowId = "Description" } };
        var changed = await AuditScheduleSourceWorkspace.PreviewCaptureAsync(repeatCapture, staff, reader,
          f.EngagementId, captureId, remapped, ct);
        Assert.True(changed.Succeeded, changed.Message);
        var conflict = await AuditScheduleSourceWorkspace.CaptureAsync(repeatCapture, staff, reader, f.EngagementId,
          new(captureId, changed.Value!.CanonicalInput, changed.Value.ManifestHash,
            long.Parse(changed.Value.InputGeneration, CultureInfo.InvariantCulture),
            long.Parse(changed.Value.PeriodRevision, CultureInfo.InvariantCulture), changed.Value.RequestHash, true), ct);
        Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
        Assert.Equal(1, await repeatCapture.AuditScheduleSourceCaptures.CountAsync(ct));
        var scheduleId = originalCapture.Receipt.ScheduleId;
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => repeatCapture.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE audit_schedule_source_captures SET manifest_hash = repeat('0',64) WHERE schedule_id = {scheduleId}", ct));
        Assert.Equal("55000", immutable.SqlState);
        var immutableRows = await Assert.ThrowsAsync<PostgresException>(() => repeatCapture.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE audit_schedule_rows SET signed_amount = signed_amount + 1 WHERE schedule_id = {scheduleId}", ct));
        Assert.Equal("55000", immutableRows.SqlState);
        var forgedApproval = await Assert.ThrowsAsync<PostgresException>(() => repeatCapture.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE audit_schedules SET status = 'APPROVED', completeness_decision = 'unrecorded' WHERE id = {scheduleId}", ct));
        Assert.Equal("23514", forgedApproval.SqlState);
        Assert.Equal("UNRECONCILED", (await repeatCapture.AuditSchedules.AsNoTracking().SingleAsync(ct)).Status);
        Assert.Empty(await repeatCapture.AuditScheduleSourceReviews.AsNoTracking().ToListAsync(ct));
        var captureMigration = repeatCapture.Database.GetMigrations()
          .Single(x => x.EndsWith("_AuditScheduleSourceIntake", StringComparison.Ordinal));
        var prior = repeatCapture.Database.GetMigrations().TakeWhile(x => x != captureMigration).Last();
        var retainedDown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          repeatCapture.GetService<IMigrator>().MigrateAsync(prior, ct));
        var databaseRefusal = Assert.IsType<PostgresException>(retainedDown.InnerException);
        Assert.Equal("55000", databaseRefusal.SqlState);
        Assert.Contains(captureMigration, await repeatCapture.Database.GetAppliedMigrationsAsync(ct));
        Assert.Equal(1, await repeatCapture.AuditScheduleSourceCaptures.CountAsync(ct));
      }

      // Last: change the actual staged file, retaining all real accepted receipts and provider bytes.
      var chunk = await resolve.PbcUploadChunks.AsNoTracking().SingleAsync(x => x.PbcUploadIntentId == staged.UploadIntentId, ct);
      var altered = (byte[])csv.Clone();
      altered[^3] = (byte)'9';
      await File.WriteAllBytesAsync(chunk.StagedPath!, altered, ct);
      var tampered = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, reader, f.ClientId, f.EngagementId,
        receiptId, decisionId, sourceDate, columns, ["0010", "0020"], ct);
      Assert.False(tampered.Succeeded);
      Assert.Equal("pbc.staging-unverifiable", tampered.ErrorCode);
      await using (var recover = new AuditSphereDbContext(pg.Options))
      {
        var lookup = await AuditScheduleSourceWorkspace.GetCaptureReceiptAsync(recover, staff,
          f.EngagementId, captureId, captureRequest.ExpectedRequestHash, ct);
        Assert.True(lookup.Succeeded, lookup.Message);
        Assert.True(lookup.Value!.Found);
        Assert.Equal(originalCapture, lookup.Value.Receipt);
        var wrongIntent = await AuditScheduleSourceWorkspace.GetCaptureReceiptAsync(recover, staff,
          f.EngagementId, captureId, new string('0', 64), ct);
        Assert.Equal(ErrorCodes.IdempotencyConflict, wrongIntent.ErrorCode);
        var receiptFirst = await AuditScheduleSourceWorkspace.CaptureAsync(recover, staff, reader,
          f.EngagementId, captureRequest, ct);
        Assert.True(receiptFirst.Succeeded, receiptFirst.Message);
        Assert.Equal(originalCapture, receiptFirst.Value);
        Assert.Equal(1, await recover.AuditScheduleSourceCaptures.CountAsync(ct));
      }
      Assert.Equal(1, await resolve.AuditSchedules.CountAsync(ct));
      Assert.Equal(2, await resolve.AuditScheduleRows.CountAsync(ct));
      Assert.DoesNotContain(resolve.ChangeTracker.Entries(), x =>
        x.Entity is AuditSchedule or AuditScheduleRow);
    }
    finally { Directory.Delete(staged.StagingRoot, true); }
  }
  private sealed class InterruptScheduleSave : SaveChangesInterceptor
  {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      if (eventData.Context!.ChangeTracker.Entries<AuditSchedule>().Any(x => x.State == EntityState.Added))
        throw new OperationCanceledException("Synthetic interruption after schedule rows were staged.");
      return ValueTask.FromResult(result);
    }
  }
}
