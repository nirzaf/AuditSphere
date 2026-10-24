using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularNativeGeneralLedgerSourceJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-NATIVE-GL-SOURCE-ACTIVE-01")]
  public async Task ExistingActiveContextHandsApprovedNativeOpeningAndPostedSourceIntoReviewedAngularGlSeal()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-NATIVE-GL-SOURCE-ACTIVE-01");
    var f = host.Fixture;
    var maker = PbcSeed.Actor(f.Admin, "Administrator");
    var reviewer = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    var periodId = Guid.CreateVersion7();
    Guid openingId, journalId;
    string openingManifestHash;
    // An explicitly synthetic, pre-existing ACTIVE context. This case does not exercise
    // public period creation/activation, cutover approval or any real client acceptance.
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer"));
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, PracticeClientId = f.ClientId, ServiceRoute = "BOOKKEEPING",
        Decision = "Accepted", Generation = 1, Rationale = "Synthetic existing native GL context, not live mandate approval",
        EvaluationTemplateVersion = "TEST-NATIVE-GL", EvaluationSnapshotDigest = new string('a', 64),
        DecidedByUserId = f.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var profile = await ClientAccountingService.CreateProfileAsync(db, maker,
        new(f.ClientId, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE-GL-ACTIVE-TEST", ClientAccountingSourceModes.NativeBookkeeping));
      Assert.True(profile.Succeeded, profile.Message);
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "SYNTHETIC-ACTIVE-2026",
        StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), Basis = "STATUTORY", Currency = "QAR",
        Status = AccountingWorkflowStates.Active, Revision = 1, CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, maker, f.ClientId, "NATIVE-GL-ACTIVE-TEST", new(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message);
      var accounts = await ClientAccountingService.AddAccountsAsync(db, maker, chart.Value,
        [new("cash", "1000", "Synthetic cash", "ASSET", "DEBIT", true),
         new("equity", "3000", "Synthetic capital", "EQUITY", "CREDIT", true)]);
      Assert.True(accounts.Succeeded, accounts.Message);
      var published = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chart.Value);
      Assert.True(published.Succeeded, published.Message);
      var opening = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, maker,
        new(f.ClientId, periodId, 1, new(2026, 1, 1), "QAR", "Synthetic recorded opening cash and capital",
          Hashing.Sha256Hex("Synthetic opening evidence fixture bytes"), [new("1000", 125m, 0m), new("3000", 0m, 125m)]));
      Assert.True(opening.Succeeded, opening.Message); openingId = opening.Value;
      var openingReview = await ClientOperationalOpeningBalanceWorkspace.GetAsync(db, reviewer, f.ClientId, periodId);
      Assert.True(openingReview.Succeeded, openingReview.Message);
      openingManifestHash = openingReview.Value!.ManifestSha256;
      Assert.False((await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, maker, f.ClientId,
        openingId, 1, openingReview.Value!.ManifestSha256)).Succeeded);
      var approved = await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, reviewer, f.ClientId,
        openingId, 1, openingReview.Value.ManifestSha256);
      Assert.True(approved.Succeeded, approved.Message);
      var journal = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, maker,
        new(f.ClientId, periodId, "NATIVE-GL-UI-10", "Synthetic recorded capital contribution", new(2026, 1, 15),
          [new("1000", "Synthetic cash", 10m, 0m), new("3000", "Synthetic capital", 0m, 10m)]));
      Assert.True(journal.Succeeded, journal.Message); journalId = journal.Value;
      var posting = await ClientOperationalLedgerWorkspace.PreviewAsync(db, maker, f.ClientId, journalId);
      Assert.True(posting.Succeeded, posting.Message);
      var submitted = await ClientOperationalLedgerWorkspace.SubmitAsync(db, maker, f.ClientId, journalId, 1,
        previewDigest: posting.Value!.Digest);
      Assert.True(submitted.Succeeded, submitted.Message);
      var review = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, f.ClientId, journalId);
      Assert.True(review.Succeeded, review.Message);
      var posted = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, f.ClientId, journalId,
        new(2, "APPROVE", "Synthetic independent posting review", review.Value!.Digest, Guid.CreateVersion7()));
      Assert.True(posted.Succeeded, posted.Message);
    }

    NativeTrialBalanceSourceView tb;
    await using (var db = host.CreateDbContext())
    {
      var request = new NativeTrialBalanceSourceRequest(Guid.CreateVersion7(), f.ClientId, f.EngagementId, periodId,
        1, new(2026, 12, 31), "SYNTHETIC-NATIVE-GL-ENTITY", true, "Synthetic current native TB for existing ACTIVE-context UI handoff");
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, maker, request);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.False(preview.Value!.ActivityOnly);
      Assert.Equal("125", preview.Value.TrialBalance.OpeningDebit);
      Assert.Equal("10", preview.Value.TrialBalance.PeriodDebit);
      Assert.Equal("135", preview.Value.TrialBalance.ClosingDebit);
      var sealedTb = await NativeTrialBalanceSourceWorkspace.SealAsync(db, maker,
        request with { Reviewed = true, ReviewBasis = preview.Value.ReviewBasis });
      Assert.True(sealedTb.Succeeded, sealedTb.Message); tb = sealedTb.Value!;
      Assert.Equal("Pending", tb.ValidationStatus);
    }
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(host.DbOptions));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(f.FirmId, "Test");
    var handler = new TrialBalanceValidationHandler();
    var worker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new TrialBalanceDiscovery(factory, store, handler, options)], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    await using (var db = host.CreateDbContext())
    {
      var validated = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == tb.DatasetId);
      Assert.Equal("Accepted", validated.ValidationStatus); Assert.True(validated.Balanced); Assert.Equal(0m, validated.ControlTotal);
      var self = await SourceAcceptanceWorkspace.GetAsync(db, maker, tb.DatasetId);
      Assert.True(self.Succeeded, self.Message); Assert.False(self.Value!.CanAccept);
      var review = await SourceAcceptanceWorkspace.GetAsync(db, reviewer, tb.DatasetId);
      Assert.True(review.Succeeded, review.Message); Assert.True(review.Value!.CanAccept, review.Value.Blocker);
      var accepted = await SourceAcceptanceWorkspace.AcceptAsync(db, reviewer, tb.DatasetId, review.Value.Revision,
        "Synthetic independent accepted native TB for exact GL source", true);
      Assert.True(accepted.Succeeded, accepted.Message);
    }

    var angular = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH") is { Length: > 0 } buildPath)
      angular["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(f.Admin, angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var capturePath = $"/ui/app/accounting/clients/{f.ClientId:D}/native-sources/{tb.DatasetId:D}/general-ledger";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(capturePath));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Native general ledger source", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Evidence reference", new() { Exact = true }).FillAsync("Synthetic exact original posting rows for existing ACTIVE context");
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I explicitly selected this whole native period ledger with no reporting book and its retained TB cutoff.", Exact = true }).CheckAsync();
    var response = await page.RunAndWaitForResponseAsync(
      () => page.GetByRole(AriaRole.Button, new() { Name = "Preview exact native GL rows", Exact = true }).ClickAsync(),
      r => r.Request.Method == "POST" && r.Url.EndsWith("/native-gl-sources/preview", StringComparison.Ordinal));
    Assert.Equal(200, response.Status);
    var rows = page.GetByRole(AriaRole.Region, new() { Name = "Native GL exact preview rows", Exact = true });
    await Assertions.Expect(rows.GetByRole(AriaRole.Row)).ToHaveCountAsync(3);
    await Assertions.Expect(rows.GetByRole(AriaRole.Cell, new() { Name = "Unknown / Unknown", Exact = true })).ToHaveCountAsync(2);
    await Assertions.Expect(rows.GetByRole(AriaRole.Rowheader)).ToContainTextAsync(["NATIVE-GL-UI-10", "NATIVE-GL-UI-10"]);
    await Assertions.Expect(rows).ToContainTextAsync(journalId.ToString("D"));
    await Assertions.Expect(page.GetByText(openingId.ToString("D"), new() { Exact = false }).First).ToBeVisibleAsync();
    var seal = page.GetByRole(AriaRole.Button, new() { Name = "Seal native generated GL source", Exact = true });
    await Assertions.Expect(seal).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the exact source rows, cutoff, opening, unavailable metadata and preview basis. Seal generated evidence only.", Exact = true }).CheckAsync();
    var sealedResponse = await page.RunAndWaitForResponseAsync(() => seal.ClickAsync(),
      r => r.Request.Method == "POST" && r.Url.EndsWith("/native-gl-sources/seal", StringComparison.Ordinal));
    Assert.Equal(200, sealedResponse.Status);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Retained native generated GL source", Exact = true })).ToBeVisibleAsync();
    Guid batchId;
    string generatedJson;
    await using (var db = host.CreateDbContext())
    {
      var evidence = await db.NativeGeneralLedgerSources.AsNoTracking().SingleAsync(x => x.TrialBalanceDatasetId == tb.DatasetId);
      batchId = evidence.ImportBatchId; generatedJson = evidence.GeneratedRowsJson;
      Assert.Equal(tb.SourceIdentityHash, evidence.NativeTrialBalanceSourceHash);
      Assert.Equal(1, evidence.TransactionCount); Assert.Equal(2, evidence.LineCount);
      var batch = await db.SourceImportBatches.AsNoTracking().SingleAsync(x => x.Id == batchId);
      Assert.Equal("SEALED", batch.Status); Assert.Equal(SourceImportOrigins.NativeLedger, batch.OriginKind);
      Assert.Equal("", batch.RawFileSha256Hex); Assert.Null(batch.BookId);
      Assert.Empty(await db.GeneralLedgerImportChunks.Where(x => x.ImportBatchId == batchId).ToListAsync());
      var originalIds = await db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.JournalId == journalId).Select(x => x.Id).ToListAsync();
      var projected = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.ImportBatchId == batchId).ToListAsync();
      Assert.All(projected, x => { Assert.Contains(Guid.Parse(x.StableLineId), originalIds); Assert.Null(x.IsManual); Assert.Null(x.IsYearEnd); });
      using var document = JsonDocument.Parse(generatedJson);
      Assert.Equal(2, document.RootElement.GetArrayLength());
      Assert.All(document.RootElement.EnumerateArray(), x =>
      {
        Assert.Equal(journalId, x.GetProperty("JournalId").GetGuid());
        Assert.Contains(x.GetProperty("LineId").GetGuid(), originalIds);
        Assert.Equal(JsonValueKind.Null, x.GetProperty("IsManual").ValueKind);
        Assert.Equal(JsonValueKind.Null, x.GetProperty("IsYearEnd").ValueKind);
      });
      Assert.False(await db.SourceAcceptanceDecisions.AnyAsync(x => x.ImportBatchId == batchId)); // A UI seal never accepts its own source.
      Assert.Equal(AccountingWorkflowStates.Active, (await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId)).Status);
    }
    var download = await page.RunAndWaitForDownloadAsync(() =>
      page.GetByRole(AriaRole.Button, new() { Name = "Download exact generated GL rows", Exact = true }).ClickAsync());
    Assert.Equal($"native-gl-{batchId:D}.json", download.SuggestedFilename);
    Assert.Equal(generatedJson, await File.ReadAllTextAsync((await download.PathAsync())!));
    var retainedLink = page.GetByRole(AriaRole.Link, new() { Name = "Open retained native GL source link", Exact = true });
    var retainedHref = await retainedLink.GetAttributeAsync("href");
    Assert.EndsWith($"/app/accounting/clients/{f.ClientId:D}/native-gl-sources/{batchId:D}", retainedHref);
    var tbHref = await page.GetByRole(AriaRole.Link, new() { Name = "Retained native trial balance", Exact = true }).GetAttributeAsync("href");
    Assert.EndsWith($"/app/accounting/clients/{f.ClientId:D}/native-sources/{tb.DatasetId:D}", tbHref);
    await retainedLink.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Retained native generated GL source", Exact = true })).ToBeVisibleAsync();

    // Exercise the existing acceptance decoder and maker denial, rather than accepting through setup code.
    var acceptanceLink = page.GetByRole(AriaRole.Link, new() { Name = "Separate independent GL acceptance", Exact = true });
    var acceptanceHref = await acceptanceLink.GetAttributeAsync("href");
    Assert.EndsWith($"/app/accounting/gl-sources/{batchId:D}/acceptance", acceptanceHref);
    await acceptanceLink.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("other than the source importer");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Accept reviewed source", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("NATIVE_LEDGER");
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, angular);
    await using var reviewerContext = await browser.NewContextAsync();
    var reviewPage = await reviewerContext.NewPageAsync();
    reviewPage.PageError += (_, error) => errors.Add("reviewer: " + error);
    await reviewPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(acceptanceHref!));
    await Assertions.Expect(reviewPage.Locator("body")).ToContainTextAsync("NATIVE_LEDGER");
    await reviewPage.GetByLabel("Acceptance evidence reference", new() { Exact = true }).FillAsync("Synthetic independent browser review of exact native GL source");
    await reviewPage.GetByLabel("I independently reviewed this exact source and its effect on the selected pointer.", new() { Exact = true }).CheckAsync();
    var acceptedResponse = await reviewPage.RunAndWaitForResponseAsync(
      () => reviewPage.GetByRole(AriaRole.Button, new() { Name = "Accept reviewed source", Exact = true }).ClickAsync(),
      r => r.Request.Method == "POST" && r.Url.EndsWith($"/gl-sources/{batchId:D}/acceptance", StringComparison.Ordinal));
    Assert.Equal(200, acceptedResponse.Status);
    await Assertions.Expect(reviewPage.GetByRole(AriaRole.Heading, new() { Name = "Retained acceptance decision", Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var decision = await db.SourceAcceptanceDecisions.AsNoTracking().SingleAsync(x => x.ImportBatchId == batchId);
      Assert.Equal("ACCEPTED", decision.Decision);
      Assert.Equal(f.Reviewer.Id, decision.AcceptedByUserId);
      Assert.NotEqual(f.Admin.Id, decision.AcceptedByUserId);
      var batch = await db.SourceImportBatches.AsNoTracking().SingleAsync(x => x.Id == batchId);
      Assert.Equal(batch.NormalizedDatasetDigest, decision.SourceIdentityHash);
    }

    // Read the modified existing Native completeness workspace/plan decoders in the browser.
    var completenessPath = acceptanceHref![..^"/acceptance".Length] + "/completeness";
    await reviewPage.GotoAsync(reviewerOrigin + completenessPath);
    await Assertions.Expect(reviewPage.GetByText("Inherited native opening", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewPage.GetByText(openingId.ToString("D"), new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewPage.GetByText(openingManifestHash, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewPage.GetByLabel("Opening trial balance", new() { Exact = true })).ToHaveCountAsync(0);
    await reviewPage.GetByLabel("Closing trial balance", new() { Exact = true }).SelectOptionAsync(tb.DatasetId.ToString("D"));
    var planResponse = await reviewPage.RunAndWaitForResponseAsync(
      () => reviewPage.GetByRole(AriaRole.Button, new() { Name = "Review completeness source pair", Exact = true }).ClickAsync(),
      r => r.Request.Method == "GET" && r.Url.Contains($"/gl-sources/{batchId:D}/completeness/plan?", StringComparison.Ordinal));
    Assert.Equal(200, planResponse.Status);
    await Assertions.Expect(reviewPage.GetByText("Inherited native opening / manifest", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewPage.GetByText("Unknown opening", new() { Exact = false })).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
      Assert.False(await db.GeneralLedgerCompletenessBridges.AnyAsync(x => x.ImportBatchId == batchId)); // A read/preview never supplies completeness approval.
    var observedCompletenessPath = new Uri(reviewPage.Url).PathAndQuery;
    Assert.Equal(completenessPath, observedCompletenessPath);
    var refresh = reviewPage.GetByRole(AriaRole.Button, new() { Name = "Refresh completeness", Exact = true });
    await Assertions.Expect(refresh).ToBeEnabledAsync();
    await refresh.FocusAsync();
    await Assertions.Expect(refresh).ToBeFocusedAsync();
    var refreshed = await reviewPage.RunAndWaitForResponseAsync(() => refresh.PressAsync("Enter"),
      r => r.Request.Method == "GET" && r.Url.EndsWith($"/gl-sources/{batchId:D}/completeness?page=1&openingPage=1", StringComparison.Ordinal));
    Assert.Equal(200, refreshed.Status);
    await Assertions.Expect(reviewPage.GetByRole(AriaRole.Heading, new() { Name = "Immutable GL source", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(refresh).ToBeEnabledAsync();
    await Assertions.Expect(refresh).ToBeFocusedAsync();
    await File.WriteAllTextAsync(Path.Combine(host.RunRoot, "native-gl-ui-observed-handoff.json"),
      JsonSerializer.Serialize(new
      {
        origin = reviewerOrigin, path = observedCompletenessPath, importBatchId = Guid.Parse(retainedHref!.Split('/').Last()),
        trialBalanceDatasetId = Guid.Parse(tbHref!.Split('/').Last()), nativeOpeningId = openingId,
        retainedHref, tbHref, acceptanceHref, boundary = "Synthetic existing ACTIVE context; independent GL acceptance recorded through reviewer UI; completeness is inspected, not approved"
      }));
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(reviewerOrigin, observedCompletenessPath, "native-gl-completeness-current");
  }
}
