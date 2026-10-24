using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuditSphereOps.Api.Tests;

public sealed class ScheduleSourceIntakeApiTests
{
  [Fact]
  public async Task SourceCatalogAndIndependentQueueRoutesEnforceTheirStaffScopes()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-SCHEDULE-SOURCE-CATALOG");
    var f = await PbcSeed.SeedAsync(pg);
    var root = $"/api/ui/engagements/{f.EngagementId}/fieldwork/schedule-intake";

    using var staffFactory = Factory(pg.ConnectionString, f.Staff);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync(root + "/options")).StatusCode);
    await SignIn(staff);

    using var optionsResponse = await staff.GetAsync(root + "/options?pbcOffset=0&trialBalanceOffset=0&limit=25");
    var options = await ReadValueAsync(optionsResponse);
    Assert.Empty(options.GetProperty("pbcSources").EnumerateArray());
    Assert.Equal(0, options.GetProperty("nextPbcOffset").GetInt32());
    Assert.False(options.GetProperty("hasMorePbcSources").GetBoolean());
    Assert.Empty(options.GetProperty("trialBalances").EnumerateArray());
    Assert.Equal(0, options.GetProperty("nextTrialBalanceOffset").GetInt32());
    Assert.False(options.GetProperty("hasMoreTrialBalances").GetBoolean());
    Assert.Equal(HttpStatusCode.Forbidden,
      (await staff.GetAsync(root + "/review-candidates?offset=0&limit=25")).StatusCode);

    using var reviewerFactory = Factory(pg.ConnectionString, f.Reviewer);
    using var reviewer = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(reviewer);
    using var candidatesResponse = await reviewer.GetAsync(root + "/review-candidates?offset=0&limit=25");
    var candidates = await ReadValueAsync(candidatesResponse);
    Assert.Equal(0, candidates.GetProperty("offset").GetInt32());
    Assert.Equal(25, candidates.GetProperty("limit").GetInt32());
    Assert.False(candidates.GetProperty("hasMore").GetBoolean());
    Assert.Empty(candidates.GetProperty("items").EnumerateArray());

    using var clientFactory = Factory(pg.ConnectionString, f.Client);
    using var client = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(root + "/options")).StatusCode);
  }

  [Fact]
  public async Task IndependentReviewerCanReadExactCapturedSourceRowsOverHttp()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-SCHEDULE-SOURCE-REVIEW");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var ct = timeout.Token;
    var f = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Reviewer");
    var client = PbcSeed.Actor(f.Client, "ClientUser");
    var accountingReviewer = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    var sourceDate = new DateOnly(2026, 12, 31);
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
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
    }

    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, f, staff, client);
    var csv = Encoding.UTF8.GetBytes("AccountCode,Description,SignedAmount,Currency,StableRowId,TransactionDate\r\n" +
      "0010,Trade A,125,QAR,row-a,2025-12-15\r\n0020,Trade B,75,QAR,row-b,2026-11-30\r\n");
    var staged = await PbcSeed.StageUploadAsync(pg, f, client, requestId, csv, "receivables.csv");
    try
    {
      var operationFactory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
      var store = new PostgresOperationStore(operationFactory);
      var transfer = new PbcDocumentTransferHandler(operationFactory,
        new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider")));
      await using (var complete = new AuditSphereDbContext(pg.Options))
      {
        var result = await PbcService.CompleteUploadAsync(complete, staff,
          new(staged.UploadIntentId, staged.DeclaredSha256Hex), store, transfer, ct);
        Assert.True(result.Succeeded, result.Message);
      }
      var workerOptions = new WorkerOptions(f.FirmId, "Test", AllowSimulationAdapters: true);
      var registry = new DurableOperationRegistry([transfer], workerOptions);
      var discovery = new PbcTransferDiscovery(operationFactory, store, transfer, workerOptions);
      var worker = new AuditSphereOps.Worker.Worker(new OperationDispatcher(store, registry, workerOptions),
        [discovery], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
      Assert.True(await worker.ProcessNextAsync(ct));

      Guid receiptId;
      await using (var acceptPbc = new AuditSphereDbContext(pg.Options))
      {
        var request = await acceptPbc.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, ct);
        var underReview = await PbcService.ChangeStateAsync(acceptPbc, reviewer,
          new(requestId, PbcStates.UnderReview, request.Revision), ct);
        Assert.True(underReview.Succeeded, underReview.Message);
        var current = await acceptPbc.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, ct);
        var upload = await acceptPbc.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId, ct);
        var accepted = await PbcService.ChangeStateAsync(acceptPbc, reviewer,
          new(requestId, PbcStates.Accepted, current.Revision, ReviewedUploadIntentId: upload.Id,
            ReviewedUploadRevision: upload.Revision, ReviewedContentSha256Hex: staged.DeclaredSha256Hex), ct);
        Assert.True(accepted.Succeeded, accepted.Message);
        receiptId = await acceptPbc.PbcAcceptanceReceipts.AsNoTracking()
          .Where(x => x.PbcRequestId == requestId).Select(x => x.Id).SingleAsync(ct);
      }

      var datasetId = Guid.NewGuid();
      var tbBytes = Encoding.UTF8.GetBytes("AccountCode,Amount,Currency,Entity\n" +
        "0010,125,QAR,TEST ENTITY\n0020,75,QAR,TEST ENTITY\n4000,-200,QAR,TEST ENTITY\n");
      var tbHash = Hashing.Sha256Hex(tbBytes);
      Guid decisionId;
      await using (var acceptTb = new AuditSphereDbContext(pg.Options))
      {
        acceptTb.TrialBalanceDatasets.Add(new TrialBalanceDataset
        {
          Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          PeriodId = periodId, BookId = bookId, Basis = "STATUTORY", SourceKind = "Raw", Revision = 1,
          LegalEntityKey = "TEST ENTITY", Currency = "QAR", RawFileSha256Hex = tbHash,
          NormalizedDatasetDigest = tbHash, Sha256Hex = tbHash, Balanced = true, ControlTotal = 0m,
          ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
          ImportedByUserId = f.Staff.Id, ImportedAt = DateTimeOffset.UtcNow
        });
        acceptTb.TrialBalanceRows.AddRange(new[] { ("0010", 125m), ("0020", 75m), ("4000", -200m) }.Select(x =>
          new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = x.Item1,
            AccountName = "Synthetic " + x.Item1, Amount = x.Item2, Currency = "QAR", Entity = "TEST ENTITY" }));
        await acceptTb.SaveChangesAsync(ct);
        var dataset = await acceptTb.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId, ct);
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
      await using (var captureDb = new AuditSphereDbContext(pg.Options))
      {
        var request = Guid.NewGuid();
        var fields = new AuditScheduleSourceFields(receiptId, decisionId, sourceDate, columns, ["0010", "0020"]);
        var preview = await AuditScheduleSourceWorkspace.PreviewCaptureAsync(captureDb, staff, reader,
          f.EngagementId, request, fields, ct);
        Assert.True(preview.Succeeded, preview.Message);
        Assert.Equal("200.000000", preview.Value!.SignedTotal);
        Assert.Equal("200.000000", preview.Value.ClosingControl);
        Assert.Equal("0.000000", preview.Value.Residual);
        Assert.All(preview.Value.Accounts, x => Assert.Equal("0.000000", x.Difference));
        var command = new AuditScheduleCaptureRequest(request, preview.Value!.CanonicalInput,
          preview.Value.ManifestHash, long.Parse(preview.Value.InputGeneration, CultureInfo.InvariantCulture),
          long.Parse(preview.Value.PeriodRevision, CultureInfo.InvariantCulture), preview.Value.RequestHash, true);
        var captured = await AuditScheduleSourceWorkspace.CaptureAsync(captureDb, staff, reader,
          f.EngagementId, command, ct);
        Assert.True(captured.Succeeded, captured.Message);

        using var reviewerFactory = Factory(pg.ConnectionString, f.Reviewer);
        using var reviewerClient = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
        await SignIn(reviewerClient);
        var root = $"/api/ui/engagements/{f.EngagementId}/fieldwork/schedule-intake/schedules/" +
          $"{captured.Value!.Receipt.ScheduleId}/review/source-rows";
        using var response = await reviewerClient.GetAsync(root +
          $"?captureHash={captured.Value.Receipt.ManifestHash}&offset=0&limit=1", ct);
        var page = await ReadValueAsync(response);
        Assert.Equal(captured.Value.Receipt.ManifestHash, page.GetProperty("captureHash").GetString());
        Assert.Equal(0, page.GetProperty("offset").GetInt32());
        Assert.Equal(1, page.GetProperty("limit").GetInt32());
        Assert.Equal(2, page.GetProperty("totalRows").GetInt32());
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(new[] { "AccountCode", "Description", "SignedAmount", "Currency", "StableRowId", "TransactionDate" },
          page.GetProperty("headers").EnumerateArray().Select(x => x.GetString()));
        var row = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("row-a", row.GetProperty("stableRowId").GetString());
        Assert.Equal(2, row.GetProperty("sourceLineNumber").GetInt32());
        Assert.Equal(new[] { "0010", "Trade A", "125", "QAR", "row-a", "2025-12-15" },
          row.GetProperty("cells").EnumerateArray().Select(x => x.GetString()));
      }
    }
    finally
    {
      PbcSeed.DeleteDirectory(staged.StagingRoot);
    }
  }

  private static StandaloneApiApplicationFactory Factory(string connection, AppUser user) => new(
    new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = connection,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = user.Subject,
      ["DevelopmentIdentity:TenantId"] = user.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });

  private static async Task SignIn(HttpClient client)
  {
    using var signIn = await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
  }

  private static async Task<JsonElement> ReadValueAsync(HttpResponseMessage response)
  {
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.TryGetProperty("value", out var value)
      ? value.Clone()
      : document.RootElement.Clone();
  }
}
