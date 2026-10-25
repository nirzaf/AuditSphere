using System.Text;
using System.Text.Json;
using System.Globalization;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
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

  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task AcceptedPbcListingAndSelectedTbCapturePreserveAccountVarianceReceiptsAndOriginalRows(bool balanced, bool remoteOnly)
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
        PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId, f.EngagementId));
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
      IPbcProviderSink sink = new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider"));
      if (remoteOnly)
      {
        // A provider-verified selected-site receipt registers the exact SharePoint version at publication.
        const string driveId = "b!schedule-remote-drive";
        await using var bind = new AuditSphereDbContext(pg.Options);
        bind.RepositoryBindings.Add(new RepositoryBinding
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          TenantId = "tenant", SiteId = "site", DriveId = driveId, RootFolderId = "root", Classification = "working",
          DesiredAccess = "read-write", ObservedAccess = "read-write", CapabilityProfile = "selected-site",
          CreatedAt = DateTimeOffset.UtcNow
        });
        await bind.SaveChangesAsync(ct);
        sink = new SelectedSiteReceiptSink(new PbcProviderReceipt(
          PbcSelectedSiteIdentity.Format(driveId, "listing-item", "1.0"), staged.DeclaredSha256Hex, csv.Length));
      }
      var handler = new PbcDocumentTransferHandler(factory, sink);
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
        (balanced
          ? "0010,125,QAR,TEST ENTITY\n0020,75,QAR,TEST ENTITY\n4000,-200,QAR,TEST ENTITY\n"
          : "0010,100,QAR,TEST ENTITY\n0020,100,QAR,TEST ENTITY\n4000,-200,QAR,TEST ENTITY\n"));
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
        acceptTb.TrialBalanceRows.AddRange(new[] { ("0010", balanced ? 125m : 100m), ("0020", balanced ? 75m : 100m), ("4000", -200m) }.Select(x =>
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

      var stagedReader = new PbcStagedScheduleContentReader(staged.StagingRoot);
      IAuditScheduleSourceContentReader reader = stagedReader;
      var columns = new AuditScheduleCsvColumns("AccountCode", "Description", "SignedAmount", "Currency",
        "StableRowId", "TransactionDate");
      await using var resolve = new AuditSphereDbContext(pg.Options);
      var receiptFacts = await resolve.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.Id == receiptId, ct);
      string? localManifest = null;
      MemoryRemoteReader? remote = null;
      if (remoteOnly)
      {
        var local = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, stagedReader, f.ClientId, f.EngagementId,
          receiptId, decisionId, sourceDate, columns, ["0010", "0020"], ct);
        Assert.True(local.Succeeded, local.Message);
        localManifest = local.Value!.ManifestJson;
        var registration = await resolve.PbcDocumentRegistrations.AsNoTracking()
          .SingleAsync(x => x.PbcUploadIntentId == staged.UploadIntentId, ct);
        var activated = await PbcService.ActivateRemoteReadAsync(resolve, PbcSeed.Actor(f.Admin, "Administrator"),
          new PbcReadDispositionRequest(staged.UploadIntentId, registration.Id, 0, "Synthetic remote-only schedule source"), ct);
        Assert.True(activated.Succeeded, activated.Message);
        // Remote-only from here: every retained staged chunk is removed before any further read.
        foreach (var path in await resolve.PbcUploadChunks.AsNoTracking()
          .Where(x => x.PbcUploadIntentId == staged.UploadIntentId).Select(x => x.StagedPath!).ToListAsync(ct))
          File.Delete(path);
        remote = new MemoryRemoteReader(csv);
        reader = new PbcScheduleSourceContentReader(stagedReader, remote);
      }
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
      Assert.Equal(balanced, facts.AllAccountsReconciled);
      Assert.Equal(new[] { "0010", "0020" }, facts.AccountTieOuts.Select(x => x.AccountCode));
      Assert.Equal(balanced ? new[] { 0m, 0m } : new[] { 25m, -25m }, facts.AccountTieOuts.Select(x => x.Difference));
      Assert.Equal(new DateOnly(2025, 12, 15), facts.Rows[0].TransactionDate);
      Assert.Equal(receiptId, facts.PbcAcceptanceReceiptId);
      Assert.Equal(decisionId, facts.AcceptedTbDecisionId);
      Assert.Equal(datasetId, facts.TrialBalanceDatasetId);
      Assert.Equal(bookId, facts.BookId);
      Assert.Equal(sourceDate, facts.SourceDate);
      Assert.Equal(staged.DeclaredSha256Hex, facts.RawFileSha256);
      if (localManifest is not null)
      {
        Assert.Equal(localManifest, facts.ManifestJson);
        Assert.True(remote!.Reads > 0);
      }
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
        Assert.Equal(balanced ? new[] { "0.000000", "0.000000" } : new[] { "25.000000", "-25.000000" }, preview.Value!.Accounts.Select(x => x.Difference));
        Assert.Equal(balanced, preview.Value.AllAccountsReconciled);
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
        Assert.Equal(balanced ? "RECONCILED" : "UNRECONCILED", originalCapture.Receipt.Status); // Net-zero is not per-account reconciliation.
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
        Assert.Equal(balanced ? "RECONCILED" : "UNRECONCILED", core.Status);
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
        Assert.Equal(balanced ? "RECONCILED" : "UNRECONCILED", (await repeatCapture.AuditSchedules.AsNoTracking().SingleAsync(ct)).Status);
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
        // Later migrations reverted before the refusal are reapplied so the rest of the flow runs on the current schema.
        await repeatCapture.GetService<IMigrator>().MigrateAsync(cancellationToken: ct);
        Assert.Empty(await repeatCapture.Database.GetPendingMigrationsAsync(ct));
      }

      // Independent review uses fresh command contexts; capture previews never confer approval.
      var reviewFields = new AuditScheduleSourceReviewFields("APPROVE", "Exact selected accounts checked", "Synthetic retained listing");
      var reviewRequestId = Guid.NewGuid();
      var scheduleToReview = originalCapture.Receipt.ScheduleId;
      async Task<AuditScheduleSourceReviewRequest> PreviewDecision(AuditScheduleSourceReviewFields fields, Guid id)
      {
        await using var previewDb = new AuditSphereDbContext(pg.Options);
        var preview = await AuditScheduleSourceWorkspace.PreviewReviewAsync(previewDb, reviewer, reader,
          f.EngagementId, scheduleToReview, id, fields, ct);
        Assert.True(preview.Succeeded, preview.Message);
        return new(id, scheduleToReview, preview.Value!.CaptureHash,
          long.Parse(preview.Value.InputGeneration, CultureInfo.InvariantCulture), preview.Value.ReviewBasis,
          preview.Value.CanonicalInput, preview.Value.RequestHash, true);
      }
      async Task<AuditScheduleSourceReceiptView?> SubmitDecision(AuditScheduleSourceReviewRequest request,
        bool succeeds, string? code = null)
      {
        await using var commandDb = new AuditSphereDbContext(pg.Options);
        var outcome = await AuditScheduleSourceWorkspace.ReviewAsync(commandDb, reviewer, reader,
          f.EngagementId, request, ct);
        Assert.Equal(succeeds, outcome.Succeeded);
        if (succeeds) Assert.NotNull(outcome.Value);
        else if (code is not null) Assert.Equal(code, outcome.ErrorCode);
        return outcome.Value;
      }
      await using (var self = new AuditSphereDbContext(pg.Options))
      {
        var denied = await AuditScheduleSourceWorkspace.PreviewReviewAsync(self, staff, reader,
          f.EngagementId, scheduleToReview, Guid.NewGuid(), reviewFields, ct);
        Assert.False(denied.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
        var rows = await AuditScheduleSourceWorkspace.GetReviewSourceRowsAsync(self, staff,
          f.EngagementId, scheduleToReview, originalCapture.Receipt.ManifestHash, ct: ct);
        Assert.False(rows.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, rows.ErrorCode);
      }
      await using (var currentReview = new AuditSphereDbContext(pg.Options))
      {
        var preview = await AuditScheduleSourceWorkspace.PreviewReviewAsync(currentReview, reviewer, reader,
          f.EngagementId, scheduleToReview, reviewRequestId, reviewFields, ct);
        Assert.True(preview.Succeeded, preview.Message);
        Assert.True(preview.Value!.SourceCurrent);
        Assert.Equal(balanced, preview.Value.CanApprove);
        Assert.Equal("receivables.csv", preview.Value.Source.FileName);
        Assert.Equal(staged.DeclaredSha256Hex, preview.Value.Source.RawFileSha256);
        Assert.Equal(2, preview.Value.Source.RowCount);
        Assert.Equal("FY2026", preview.Value.Source.PeriodCode);
        Assert.Equal("TB_CLOSING_SIGNED_NET", preview.Value.Source.ControlBasis);
        Assert.Equal(new[] { "0010", "0020" }, preview.Value.Source.SelectedControlAccountCodes);
        Assert.Equal(balanced, preview.Value.Source.AllAccountsReconciled);
        Assert.Equal(balanced ? new[] { "0.000000", "0.000000" } : new[] { "25.000000", "-25.000000" },
          preview.Value.Source.Accounts.Select(x => x.Difference));

        var firstPage = await AuditScheduleSourceWorkspace.GetReviewSourceRowsAsync(currentReview, reviewer,
          f.EngagementId, scheduleToReview, preview.Value.CaptureHash, offset: 0, limit: 1, ct: ct);
        Assert.True(firstPage.Succeeded, firstPage.Message);
        Assert.Equal(preview.Value.CaptureHash, firstPage.Value!.CaptureHash);
        Assert.Equal(new[] { "AccountCode", "Description", "SignedAmount", "Currency", "StableRowId", "TransactionDate" },
          firstPage.Value.Headers);
        Assert.Equal(2, firstPage.Value.Items[0].SourceLineNumber);
        Assert.Equal(new[] { "0010", "Trade A", "125", "QAR", "row-a", "2025-12-15" }, firstPage.Value.Items[0].Cells);
        Assert.True(firstPage.Value.HasMore);
        var nextPage = await AuditScheduleSourceWorkspace.GetReviewSourceRowsAsync(currentReview, reviewer,
          f.EngagementId, scheduleToReview, preview.Value.CaptureHash, offset: 1, limit: 1, ct: ct);
        Assert.True(nextPage.Succeeded, nextPage.Message);
        Assert.Equal(new[] { "0020", "Trade B", "75", "QAR", "row-b", "2026-11-30" }, nextPage.Value!.Items[0].Cells);
        Assert.False(nextPage.Value.HasMore);
        var staleRows = await AuditScheduleSourceWorkspace.GetReviewSourceRowsAsync(currentReview, reviewer,
          f.EngagementId, scheduleToReview, new string('0', 64), offset: 0, limit: 1, ct: ct);
        Assert.Equal(ErrorCodes.GenerationStale, staleRows.ErrorCode);
      }
      await using (var ioPreview = new AuditSphereDbContext(pg.Options))
      {
        var unavailable = await AuditScheduleSourceWorkspace.PreviewReviewAsync(ioPreview, reviewer,
          new UnavailableSourceReader(), f.EngagementId, scheduleToReview, Guid.NewGuid(), reviewFields, ct);
        Assert.True(unavailable.Succeeded, unavailable.Message);
        Assert.False(unavailable.Value!.SourceCurrent);
        Assert.False(unavailable.Value.CanApprove);
        Assert.Equal(ErrorCodes.GateBlocked, unavailable.Value.SourceFailureCode);
      }
      var approval = await PreviewDecision(reviewFields, reviewRequestId);
      await SubmitDecision(approval with { Reviewed = false }, false);
      AuditScheduleSourceReviewRequest finalReviewRequest;
      AuditScheduleSourceReceiptView? originalReview = null;
      if (balanced)
      {
        finalReviewRequest = approval;
        originalReview = await SubmitDecision(approval, true);
      }
      else
      {
        await SubmitDecision(approval, false, ErrorCodes.GateBlocked);
        // This current-source RETURN basis must become stale when the accepted bytes disappear.
        finalReviewRequest = await PreviewDecision(new("RETURN", "Per-account differences require rework", "Synthetic retained listing"),
          Guid.NewGuid());
      }

      var currentProcedureId = Guid.NewGuid();
      var pendingProcedureId = Guid.NewGuid();
      var unusedProcedureId = Guid.NewGuid();
      Guid currentSelectionId = Guid.Empty;
      Guid pendingSelectionId = Guid.Empty;
      Guid reviewedItemTestId = Guid.Empty;
      Guid pendingItemTestId = Guid.Empty;
      Guid pendingSelectionItemId = Guid.Empty;
      var samplingRequest = new RunSamplingRequest(f.EngagementId, currentProcedureId, scheduleToReview,
        "RANDOM", null, null, 1, 73, "Synthetic source-bound sample of the accepted listing.");
      CreateSelectionRequest SelectionFor(Guid procedureId) => new(f.EngagementId, procedureId,
        scheduleToReview, null, "ALL", "Synthetic complete accepted listing selection.",
        [new("row-a", 125m, "QAR", "Complete listing"), new("row-b", 75m, "QAR", "Complete listing")]);
      if (balanced)
      {
        await using var consumer = new AuditSphereDbContext(pg.Options);
        consumer.AuditProcedures.AddRange(new[] { currentProcedureId, pendingProcedureId, unusedProcedureId }.Select(id => new AuditProcedure
        {
          Id = id, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          SourceProcedureId = id == currentProcedureId ? "SYNTHETIC-CURRENT" :
            id == pendingProcedureId ? "SYNTHETIC-PENDING" : "SYNTHETIC-UNUSED",
          Title = "Synthetic listing source consumption",
          ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Planned,
          CreatedAt = DateTimeOffset.UtcNow
        }));
        await consumer.SaveChangesAsync(ct);
        await PlanningBasisSeed.EstablishAsync(consumer, f.FirmId, f.ClientId, f.EngagementId, staff, reviewer,
          [("0010", "Trade receivables", 125m, "TRADE_RECEIVABLES", "ASSETS"),
           ("0020", "Other receivables", 75m, "OTHER_RECEIVABLES", "ASSETS"),
           ("4000", "Revenue", -200m, "REVENUE", "INCOME")]);
        var preview = await AuditFieldworkService.PreviewSamplingAsync(consumer, staff, samplingRequest, ct);
        Assert.True(preview.Succeeded, preview.Message);
        Assert.Equal(1, preview.Value!.Outcome.SelectedCount);
        var selected = await AuditFieldworkService.CreateSelectionAsync(consumer, staff, SelectionFor(currentProcedureId), ct);
        Assert.True(selected.Succeeded, selected.Message);
        Assert.Equal(200m, selected.Value!.SelectedSignedTotal);
        currentSelectionId = selected.Value.SelectionId;
        var pending = await AuditFieldworkService.CreateSelectionAsync(consumer, staff, SelectionFor(pendingProcedureId), ct);
        Assert.True(pending.Succeeded, pending.Message);
        pendingSelectionId = pending.Value!.SelectionId;

        var selectionReview = await AuditFieldworkService.ReviewSelectionAsync(consumer, reviewer,
          new ReviewSelectionRequest(currentSelectionId, AuditSelectionStatuses.Reviewed, null), ct);
        Assert.True(selectionReview.Succeeded, selectionReview.Message);
        var selectedItems = await consumer.AuditSelectionItems.AsNoTracking()
          .Where(x => x.SelectionId == currentSelectionId).OrderBy(x => x.StableRowId).ToArrayAsync(ct);
        Assert.Equal(2, selectedItems.Length);
        pendingSelectionItemId = selectedItems[1].Id;
        var reviewedTest = await AuditFieldworkService.RecordItemTestAsync(consumer, staff,
          new RecordItemTestRequest(selectedItems[0].Id, "Agreed the selected receivable row to its retained source.",
            ["synthetic:accepted-receivables:row-a"], AuditItemTestResults.Pass, null, null, null), ct);
        Assert.True(reviewedTest.Succeeded, reviewedTest.Message);
        reviewedItemTestId = reviewedTest.Value!.AuditItemTestId;
        var testReview = await AuditFieldworkService.ReviewItemTestAsync(consumer, reviewer,
          new ReviewItemTestRequest(reviewedItemTestId, AuditItemTestReviewDecisions.Reviewed,
            "Agreed to retained evidence."), ct);
        Assert.True(testReview.Succeeded, testReview.Message);
        var pendingTest = await AuditFieldworkService.RecordItemTestAsync(consumer, staff,
          new RecordItemTestRequest(pendingSelectionItemId, "Recorded the open test before source expiry.",
            [], AuditItemTestResults.Pending, null, null, null), ct);
        Assert.True(pendingTest.Succeeded, pendingTest.Message);
        pendingItemTestId = pendingTest.Value!.AuditItemTestId;
      }
      async Task AssertConsumersRefuseStaleSource()
      {
        await using var consumer = new AuditSphereDbContext(pg.Options);
        var preview = await AuditFieldworkService.PreviewSamplingAsync(consumer, staff, samplingRequest, ct);
        Assert.False(preview.Succeeded);
        // Use a genuinely unused applicable procedure: duplicate selection must not mask source refusal.
        var selected = await AuditFieldworkService.CreateSelectionAsync(consumer, staff, SelectionFor(unusedProcedureId), ct);
        Assert.False(selected.Succeeded);
        Assert.NotEqual(ErrorCodes.IdempotencyConflict, selected.ErrorCode);
        Assert.Equal(2, await consumer.AuditSelections.CountAsync(ct));
        Assert.Equal(4, await consumer.AuditSelectionItems.CountAsync(ct));
        Assert.False(await consumer.AuditSelections.AnyAsync(x => x.ProcedureId == unusedProcedureId, ct));
        var pendingReview = await AuditFieldworkService.ReviewSelectionAsync(consumer, reviewer,
          new ReviewSelectionRequest(pendingSelectionId, AuditSelectionStatuses.Reviewed, null), ct);
        Assert.False(pendingReview.Succeeded);
        var pending = await consumer.AuditSelections.AsNoTracking().SingleAsync(x => x.Id == pendingSelectionId, ct);
        Assert.Equal(AuditSelectionStatuses.Submitted, pending.Status);
        var testCount = await consumer.AuditItemTests.CountAsync(ct);
        var reviewCount = await consumer.AuditItemTestReviews.CountAsync(ct);
        var recordAfterExpiry = await AuditFieldworkService.RecordItemTestAsync(consumer, staff,
          new RecordItemTestRequest(pendingSelectionItemId, "Must not record against an expired source.",
            [], AuditItemTestResults.Pending, null, null, null), ct);
        Assert.False(recordAfterExpiry.Succeeded);
        Assert.Equal(testCount, await consumer.AuditItemTests.CountAsync(ct));
        var reviewAfterExpiry = await AuditFieldworkService.ReviewItemTestAsync(consumer, reviewer,
          new ReviewItemTestRequest(pendingItemTestId, AuditItemTestReviewDecisions.Reviewed,
            "Must not review against an expired source."), ct);
        Assert.False(reviewAfterExpiry.Succeeded);
        Assert.Equal(reviewCount, await consumer.AuditItemTestReviews.CountAsync(ct));
        var readiness = await AuditFieldworkService.EvaluateCompletionAsync(consumer, reviewer, f.EngagementId, ct);
        Assert.True(readiness.Succeeded, readiness.Message);
        Assert.Contains("schedule-source:stale-or-unavailable", readiness.Value!.Blockers);
        Assert.Equal(AuditSelectionStatuses.Reviewed,
          (await consumer.AuditSelections.AsNoTracking().SingleAsync(x => x.Id == currentSelectionId, ct)).Status);
        Assert.True(await consumer.AuditItemTestReviews.AsNoTracking().AnyAsync(x =>
          x.AuditItemTestId == reviewedItemTestId && x.Decision == AuditItemTestReviewDecisions.Reviewed, ct));
        var original = await consumer.AuditSelections.AsNoTracking().SingleAsync(x => x.Id == currentSelectionId, ct);
        Assert.Equal(currentProcedureId, original.ProcedureId);
        Assert.Equal(originalCapture.Receipt.InputGeneration, original.InputGeneration.ToString(CultureInfo.InvariantCulture));
      }

      if (remoteOnly)
      {
        // Accepted evidence stays current without local chunks; a SharePoint read that no longer matches the
        // registered version fails closed without rewriting the approved review.
        await using var current = new AuditSphereDbContext(pg.Options);
        var readiness = await AuditFieldworkService.EvaluateCompletionAsync(current, reviewer, f.EngagementId, ct);
        Assert.True(readiness.Succeeded, readiness.Message);
        Assert.DoesNotContain("schedule-source:stale-or-unavailable", readiness.Value!.Blockers);
        Assert.Equal("APPROVED", originalReview!.Receipt.Status);
        remote!.Tampered = true;
        var unverified = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, reader, f.ClientId, f.EngagementId,
          receiptId, decisionId, sourceDate, columns, ["0010", "0020"], ct);
        Assert.Equal("pbc.source-integrity", unverified.ErrorCode);
        Assert.Equal("APPROVED", (await current.AuditSchedules.AsNoTracking().SingleAsync(ct)).Status);
        return;
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
      if (balanced) await AssertConsumersRefuseStaleSource();
      if (!balanced)
      {
        await SubmitDecision(finalReviewRequest, false, ErrorCodes.GenerationStale);
        finalReviewRequest = await PreviewDecision(finalReviewRequest.Fields, finalReviewRequest.RequestId);
        await using (var unavailable = new AuditSphereDbContext(pg.Options))
        {
          var preview = await AuditScheduleSourceWorkspace.PreviewReviewAsync(unavailable, reviewer, reader,
            f.EngagementId, scheduleToReview, Guid.NewGuid(), reviewFields, ct);
          Assert.True(preview.Succeeded, preview.Message);
          Assert.False(preview.Value!.SourceCurrent);
          Assert.False(preview.Value.CanApprove);
          Assert.Equal("pbc.staging-unverifiable", preview.Value.SourceFailureCode);
        }
        originalReview = await SubmitDecision(finalReviewRequest, true);
      }
      Assert.NotNull(originalReview);
      Assert.Equal(balanced ? "APPROVE" : "RETURN", originalReview.Receipt.Action);
      Assert.Equal(balanced ? "APPROVED" : "UNRECONCILED", originalReview.Receipt.Status);
      Assert.Equal(f.Reviewer.Id, originalReview.Receipt.ActorUserId);
      Assert.Equal(originalCapture.Receipt.ManifestHash, originalReview.Receipt.ManifestHash);
      Assert.Equal(originalReview, await SubmitDecision(finalReviewRequest, true));
      var wrongReviewIntent = await PreviewDecision(finalReviewRequest.Fields with { Reason = "Different decision intent" },
        finalReviewRequest.RequestId);
      await SubmitDecision(wrongReviewIntent, false, ErrorCodes.IdempotencyConflict);
      var replacementReview = await PreviewDecision(finalReviewRequest.Fields with { Reason = "Cannot overwrite retained decision" },
        Guid.NewGuid());
      await SubmitDecision(replacementReview, false, ErrorCodes.ProtectedState);
      await using (var retained = new AuditSphereDbContext(pg.Options))
      {
        var reviewRow = await retained.AuditScheduleSourceReviews.AsNoTracking().SingleAsync(ct);
        Assert.Equal(finalReviewRequest.Fields.Reason, reviewRow.Reason);
        Assert.Equal(finalReviewRequest.Fields.EvidenceReference, reviewRow.EvidenceReference);
        Assert.Equal(f.Reviewer.Id, reviewRow.ActorUserId);
        Assert.Equal(originalReview.ReceiptHash, reviewRow.ReceiptHash);
        Assert.Equal(JsonSerializer.Serialize(originalReview.Receipt), reviewRow.ReceiptJson);
        Assert.Equal(originalReview.Receipt.Status, (await retained.AuditSchedules.AsNoTracking().SingleAsync(ct)).Status);
        var immutableReview = await Assert.ThrowsAsync<PostgresException>(() => retained.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE audit_schedule_source_reviews SET reason = 'overwrite' WHERE id = {reviewRow.Id}", ct));
        Assert.Equal("55000", immutableReview.SqlState);
        var recovered = await AuditScheduleSourceWorkspace.GetReviewReceiptAsync(retained, reviewer,
          f.EngagementId, finalReviewRequest.RequestId, finalReviewRequest.ExpectedRequestHash, ct);
        Assert.True(recovered.Succeeded, recovered.Message);
        Assert.True(recovered.Value!.Found);
        Assert.Equal(originalReview, recovered.Value.Receipt);
      }
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
      if (balanced)
      {
        // Controlled SQL diagnostic only: restore genuine bytes, then advance the real generation fence.
        // This isolates stale generation from missing-byte failure; it is not a supported business acceptance.
        await File.WriteAllBytesAsync(chunk.StagedPath!, csv, ct);
        await using var advance = new AuditSphereDbContext(pg.Options);
        Assert.Equal(1, await advance.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE client_safety_states SET input_generation = input_generation + 1 WHERE firm_id = {f.FirmId} AND id = {f.ClientId}", ct));
        await AssertConsumersRefuseStaleSource();
        Assert.Equal("APPROVED", (await advance.AuditSchedules.AsNoTracking().SingleAsync(ct)).Status);
      }
      Assert.Equal(1, await resolve.AuditSchedules.CountAsync(ct));
      Assert.Equal(2, await resolve.AuditScheduleRows.CountAsync(ct));
      Assert.DoesNotContain(resolve.ChangeTracker.Entries(), x =>
        x.Entity is AuditSchedule or AuditScheduleRow);
    }
    finally { Directory.Delete(staged.StagingRoot, true); }
  }
  private sealed class SelectedSiteReceiptSink(PbcProviderReceipt receipt) : IPbcProviderSink
  {
    public Task<PbcProviderReceipt> UploadAsync(PbcTransferPlan plan, CancellationToken ct) => Task.FromResult(receipt);
    public Task<PbcProviderReceipt?> VerifyAsync(PbcTransferScope scope, string identity, CancellationToken ct) =>
      Task.FromResult<PbcProviderReceipt?>(identity == receipt.Identity ? receipt : null);
    public Task<PbcProviderReceipt?> ProbeAsync(PbcTransferScope scope, CancellationToken ct) =>
      Task.FromResult<PbcProviderReceipt?>(null);
  }

  /// <summary>Releases bytes only when they match the registered hash and size, like the selected-site reader.</summary>
  private sealed class MemoryRemoteReader(byte[] content) : IPbcRemoteContentReader
  {
    public int Reads { get; private set; }
    public bool Tampered { get; set; }

    public Task<CommandResult<PbcVerifiedContent>> ReadVerifiedAsync(PbcRemoteSource source, CancellationToken ct)
    {
      Reads++;
      var bytes = (byte[])content.Clone();
      if (Tampered) bytes[^3] = (byte)'9';
      return Task.FromResult(bytes.Length != source.ByteCount || Hashing.Sha256Hex(bytes) != source.Sha256Hex
        ? CommandResult<PbcVerifiedContent>.Fail("pbc.source-integrity", "The SharePoint version does not match its registered hash and size.")
        : CommandResult<PbcVerifiedContent>.Ok(new PbcVerifiedContent(new MemoryStream(bytes, writable: false), bytes.Length)));
    }
  }

  private sealed class UnavailableSourceReader : IAuditScheduleSourceContentReader
  {
    public Task<CommandResult<byte[]>> ReadVerifiedAsync(PbcDownload download, string expectedSha256,
      long expectedByteCount, CancellationToken ct) => throw new IOException("Synthetic staged-file open failure.");
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
