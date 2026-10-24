using System.Text;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditScheduleIntakeSourcesTests
{
  [Fact]
  public async Task AcceptedPbcBytesAndExactAcceptedTbRetainOffsettingAccountDifferencesWithoutPublishingSchedule()
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

      // Last: change the actual staged file, retaining all real accepted receipts and provider bytes.
      var chunk = await resolve.PbcUploadChunks.AsNoTracking().SingleAsync(x => x.PbcUploadIntentId == staged.UploadIntentId, ct);
      var altered = (byte[])csv.Clone();
      altered[^3] = (byte)'9';
      await File.WriteAllBytesAsync(chunk.StagedPath!, altered, ct);
      var tampered = await AuditScheduleIntakeSources.ResolveAsync(resolve, staff, reader, f.ClientId, f.EngagementId,
        receiptId, decisionId, sourceDate, columns, ["0010", "0020"], ct);
      Assert.False(tampered.Succeeded);
      Assert.Equal("pbc.staging-unverifiable", tampered.ErrorCode);
      // Capture/review persistence is a later integration slice; verify the actual schedule boundary here.
      Assert.Empty(await resolve.AuditSchedules.AsNoTracking().ToListAsync(ct));
      Assert.Empty(await resolve.AuditScheduleRows.AsNoTracking().ToListAsync(ct));
      Assert.DoesNotContain(resolve.ChangeTracker.Entries(), x =>
        x.Entity is AuditSchedule or AuditScheduleRow);
    }
    finally { Directory.Delete(staged.StagingRoot, true); }
  }
}
