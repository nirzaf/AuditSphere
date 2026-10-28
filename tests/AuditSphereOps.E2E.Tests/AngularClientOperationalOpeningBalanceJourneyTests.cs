using Microsoft.Extensions.Logging.Abstractions;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Application.Operations;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientOperationalOpeningBalanceJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-CLIENT-OPERATIONAL-OPENING-01")]
  public async Task ClientOpeningManifestRequiresMakerReviewerAndFlowsIntoTrialBalance()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-OPERATIONAL-OPENING-01");
    var f = host.Fixture;
    var maker = PbcSeed.Actor(f.Admin, "Administrator");
    var reviewer = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    Guid periodId;
    string clientName;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer"));
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Synthetic native bookkeeping cutover", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = f.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, maker,
        new(f.ClientId, "QA", "QAR", 1, 1, "AUDITSPHERE", "OPENING-UI", ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
      var period = await ClientAccountingService.CreatePeriodAsync(db, maker,
        new(f.ClientId, "FY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"));
      Assert.True(period.Succeeded, period.Message); periodId = period.Value;
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, maker, f.ClientId, "OPENING-UI", new DateOnly(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message);
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, maker, chart.Value,
      [
        new("cash", "1000", "Opening cash", "ASSET", "DEBIT", true),
        new("ar", "1100", "Opening receivables", "ASSET", "DEBIT", true),
        new("equity", "3000", "Opening equity", "EQUITY", "CREDIT", true)
      ])).Succeeded);
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chart.Value)).Succeeded);
      var ar = await db.ClientAccounts.SingleAsync(x => x.ChartVersionId == chart.Value && x.AccountCode == "1100");
      var role = await ClientAccountRoleWorkspace.ProposeAsync(db, maker, f.ClientId,
        new(chart.Value, ar.Id, "AR", new(2026, 1, 1), null, "Reviewed opening receivable control"));
      Assert.True(role.Succeeded, role.Message);
      Assert.True((await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, f.ClientId, role.Value, "APPROVE", "Independent control review")).Succeeded);
      var party = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, maker, f.ClientId,
        new("Opening customer", "Opening customer", "CUSTOMER", "QA address", "QA", "", "", "", "", "TEST-SYSTEM", "OPEN-001"));
      Assert.True(party.Succeeded, party.Message);
      clientName = await db.PracticeClients.Where(x => x.Id == f.ClientId).Select(x => x.LegalName).SingleAsync();
    }

    var angular = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH") is { Length: > 0 } buildPath)
      angular["AngularUi__BuildPath"] = buildPath;
    var makerOrigin = await host.StartApiForIdentityAsync(f.Admin, angular);
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var makerContext = await browser.NewContextAsync();
    await using var reviewerContext = await browser.NewContextAsync();
    var makerPage = await makerContext.NewPageAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    var errors = new List<string>();
    makerPage.PageError += (_, error) => errors.Add("maker: " + error);
    reviewerPage.PageError += (_, error) => errors.Add("reviewer: " + error);

    async Task<ILocator> OpenWorkspaceAsync(IPage page, string origin)
    {
      await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Accounting workspace", Exact = true })).ToBeVisibleAsync();
      var accounting = page.Locator("audit-accounting");
      await accounting.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
      return accounting.Locator("audit-client-operational-opening-balances");
    }

    var makerPanel = await OpenWorkspaceAsync(makerPage, makerOrigin);
    await makerPanel.GetByLabel("Opening period", new() { Exact = true }).SelectOptionAsync(periodId.ToString());
    await makerPanel.GetByLabel("Source evidence reference", new() { Exact = true }).FillAsync("CUTOVER-TB-2025");
    await makerPanel.GetByLabel("Source evidence SHA-256", new() { Exact = true }).FillAsync(new string('b', 64));
    await makerPanel.GetByLabel("Opening account code row 1", new() { Exact = true }).FillAsync("1000");
    await makerPanel.GetByLabel("Opening debit row 1", new() { Exact = true }).FillAsync("125.00");
    await makerPanel.GetByLabel("Opening credit row 1", new() { Exact = true }).FillAsync("0");
    await makerPanel.GetByLabel("Opening account code row 2", new() { Exact = true }).FillAsync("1100");
    await makerPanel.GetByLabel("Opening debit row 2", new() { Exact = true }).FillAsync("125.00");
    await makerPanel.GetByLabel("Opening credit row 2", new() { Exact = true }).FillAsync("0");
    await makerPanel.GetByRole(AriaRole.Button, new() { Name = "Add account line", Exact = true }).ClickAsync();
    await makerPanel.GetByLabel("Opening account code row 3", new() { Exact = true }).FillAsync("3000");
    await makerPanel.GetByLabel("Opening debit row 3", new() { Exact = true }).FillAsync("0");
    await makerPanel.GetByLabel("Opening credit row 3", new() { Exact = true }).FillAsync("250.00");
    await makerPanel.GetByRole(AriaRole.Button, new() { Name = "Add opening invoice", Exact = true }).ClickAsync();
    var partySelect = makerPanel.Locator("fieldset select[required]");
    var openingPartyOption = partySelect.Locator("option", new() { HasText = "Opening customer" });
    await Assertions.Expect(openingPartyOption).ToHaveCountAsync(1);
    var openingPartyId = await partySelect.EvaluateAsync<string>("el => Array.from(el.options).find(option => option.textContent?.trim() === 'Opening customer')?.value ?? ''");
    Assert.False(string.IsNullOrEmpty(openingPartyId), "The effective client party must appear in the opening schedule selector.");
    await partySelect.SelectOptionAsync(openingPartyId!);
    await makerPanel.GetByLabel("Invoice reference", new() { Exact = true }).FillAsync("CUST-OPEN-1");
    await makerPanel.GetByLabel("Due date", new() { Exact = true }).FillAsync("2026-02-10");
    await makerPanel.GetByLabel("Approved control account code", new() { Exact = true }).FillAsync("1100");
    await makerPanel.GetByLabel("Open amount", new() { Exact = true }).FillAsync("125.00");
    await makerPanel.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the client, cutover date, source reference, account mapping, and exact opening balances.", Exact = true }).CheckAsync();
    var create = await makerPage.RunAndWaitForResponseAsync(
      async () => await makerPanel.GetByRole(AriaRole.Button, new() { Name = "Save opening snapshot for independent review", Exact = true }).ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/operational-opening-balances", StringComparison.Ordinal));
    Guid openingId;
    await using (var db = host.CreateDbContext())
      openingId = await db.ClientOperationalOpeningBalances.Where(x => x.ClientId == f.ClientId && x.PeriodId == periodId).Select(x => x.Id).SingleAsync();
    await Assertions.Expect(makerPanel.GetByText("Awaiting independent review. The preparer cannot approve this snapshot.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(makerPanel.GetByText("CUST-OPEN-1", new() { Exact = true })).ToBeVisibleAsync();
    Assert.Equal(0, await makerPanel.GetByRole(AriaRole.Button, new() { Name = "Approve opening snapshot", Exact = true }).CountAsync());

    var reviewerPanel = await OpenWorkspaceAsync(reviewerPage, reviewerOrigin);
    await reviewerPanel.GetByLabel("Opening period", new() { Exact = true }).SelectOptionAsync(periodId.ToString());
    await Assertions.Expect(reviewerPanel.GetByText("Awaiting independent review. The preparer cannot approve this snapshot.", new() { Exact = true })).ToBeVisibleAsync();
    await reviewerPanel.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this exact opening manifest, evidence identity, period revision, and account lines.", Exact = true }).CheckAsync();
    var approve = await reviewerPage.RunAndWaitForResponseAsync(
      async () => await reviewerPanel.GetByRole(AriaRole.Button, new() { Name = "Approve opening snapshot", Exact = true }).ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith($"/operational-opening-balances/{openingId}/approve", StringComparison.Ordinal));
    Assert.True(approve.Status is 200 or 204, $"Unexpected opening approval result: {approve.Status}");
    await Assertions.Expect(reviewerPanel.GetByText("Independently approved", new() { Exact = false })).ToBeVisibleAsync();

    var journals = reviewerPage.Locator("audit-client-operational-journals");
    await journals.GetByLabel("Ledger reporting period", new() { Exact = true }).SelectOptionAsync(periodId.ToString());
    await journals.GetByRole(AriaRole.Button, new() { Name = "Refresh posted ledger", Exact = true }).ClickAsync();
    var trialBalance = journals.GetByRole(AriaRole.Table, new() { Name = "Official native Trial Balance · 2026-01-01 to 2026-12-31", Exact = true });
    await Assertions.Expect(trialBalance.GetByRole(AriaRole.Rowheader, new() { Name = "1000 · Opening cash", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(trialBalance.Locator("tfoot td").Nth(0)).ToHaveTextAsync("250");
    await Assertions.Expect(trialBalance.Locator("tfoot td").Nth(1)).ToHaveTextAsync("250");
    await Assertions.Expect(trialBalance.Locator("tfoot td").Nth(2)).ToHaveTextAsync("0");
    await Assertions.Expect(trialBalance.Locator("tfoot td").Nth(3)).ToHaveTextAsync("0");

    await using (var db = host.CreateDbContext())
    {
      var saved = await db.ClientOperationalOpeningBalances.SingleAsync(x => x.ClientId == f.ClientId && x.PeriodId == periodId);
      Assert.Equal(f.Reviewer.Id, saved.ApprovedByUserId);
      var reconciliation = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, reviewer, f.ClientId, periodId, new(2026, 12, 31));
      Assert.True(reconciliation.Succeeded, reconciliation.Message);
      Assert.True(reconciliation.Value!.Reconciled);
      Assert.Equal("REVIEWED_OPENING_DETAIL_INCLUDED", reconciliation.Value.OpeningDetailStatus);
      Assert.Equal("125.000000", reconciliation.Value.Accounts.Single(x => x.AccountCode == "1100").OpenItemBalance);
    }
    // Continue the same independently approved opening into generated source evidence.
    // This is not a shortcut to technical validation, source acceptance or period close.
    await makerPage.GotoAsync(makerOrigin + $"/ui/app/accounting/clients/{f.ClientId:D}/native-sources/new?periodId={periodId:D}");
    await Assertions.Expect(makerPage.GetByRole(AriaRole.Heading, new() { Name = "Native trial balance source", Exact = true })).ToBeVisibleAsync();
    await makerPage.GetByLabel("Engagement ID", new() { Exact = true }).FillAsync(f.EngagementId.ToString("D"));
    await makerPage.GetByLabel("As-of date", new() { Exact = true }).FillAsync("2026-12-31");
    await makerPage.GetByLabel("Legal entity key", new() { Exact = true }).FillAsync("SYNTHETIC-OPENING-ENTITY");
    await makerPage.GetByLabel("Source evidence reference", new() { Exact = true }).FillAsync("Synthetic independently reviewed opening, native source capture");
    await makerPage.GetByRole(AriaRole.Checkbox, new() { Name = "I explicitly selected the whole native period ledger with no reporting book.", Exact = true }).CheckAsync();
    var previewSource = await makerPage.RunAndWaitForResponseAsync(
      () => makerPage.GetByRole(AriaRole.Button, new() { Name = "Preview exact native source", Exact = true }).ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/native-tb-sources/preview", StringComparison.Ordinal));
    Assert.Equal(200, previewSource.Status);
    var generated = makerPage.GetByRole(AriaRole.Region, new() { Name = "Native source exact trial balance", Exact = true });
    await Assertions.Expect(generated.GetByRole(AriaRole.Rowheader, new() { Name = "1100 · Opening receivables", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(generated.Locator("tfoot td").Nth(0)).ToHaveTextAsync("250");
    await Assertions.Expect(generated.Locator("tfoot td").Nth(1)).ToHaveTextAsync("250");
    var seal = makerPage.GetByRole(AriaRole.Button, new() { Name = "Seal native generated source", Exact = true });
    await Assertions.Expect(seal).ToBeDisabledAsync();
    await makerPage.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this exact period revision, as-of date, entity, evidence, coverage and preview basis. Seal generated evidence only.", Exact = true }).CheckAsync();
    var sealedResponse = await makerPage.RunAndWaitForResponseAsync(() => seal.ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/native-tb-sources/seal", StringComparison.Ordinal));
    Assert.Equal(200, sealedResponse.Status);
    Guid nativeDatasetId;
    string generatedJson;
    await using (var db = host.CreateDbContext())
    {
      var source = await db.NativeTrialBalanceSources.SingleAsync(x => x.ClientId == f.ClientId && x.EngagementId == f.EngagementId);
      nativeDatasetId = source.DatasetId;
      generatedJson = source.GeneratedRowsJson;
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == source.DatasetId);
      Assert.Equal("Native", dataset.SourceKind);
      Assert.Equal("Pending", dataset.ValidationStatus);
      Assert.Equal("", dataset.RawFileSha256Hex);
      Assert.False(await db.SourceAcceptanceDecisions.AnyAsync(x => x.TrialBalanceDatasetId == source.DatasetId));
    }
    await Assertions.Expect(makerPage.GetByRole(AriaRole.Link, new() { Name = "Open retained source link", Exact = true })).ToBeVisibleAsync();
    var download = await makerPage.RunAndWaitForDownloadAsync(() =>
      makerPage.GetByRole(AriaRole.Button, new() { Name = "Download generated rows", Exact = true }).ClickAsync());
    Assert.Equal($"auditsphere-native-tb-{nativeDatasetId:D}.json", download.SuggestedFilename);
    Assert.Equal(generatedJson, await File.ReadAllTextAsync((await download.PathAsync())!));
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(makerOrigin,
      $"/ui/app/accounting/clients/{f.ClientId:D}/native-sources/{nativeDatasetId:D}", "native-source-current");
  }
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "ANGULAR-NATIVE-SUCCESSOR-OPENING-01")]
  public async Task NativeSuccessorOpeningUsesExactReviewedClosedSourceAndRecoversOriginalUiRequest(bool withOriginalItem)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-NATIVE-SUCCESSOR-OPENING-01");
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
         new("equity", "3000", "Synthetic capital", "EQUITY", "CREDIT", true),
         new("ar", "1100", "Synthetic receivables", "ASSET", "DEBIT", true),
         new("ap", "2100", "Synthetic payables", "LIABILITY", "CREDIT", true)]);
      Assert.True(accounts.Succeeded, accounts.Message);
      var published = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chart.Value);
      Assert.True(published.Succeeded, published.Message);
      foreach (var (role, code) in new[] { ("AR", "1100"), ("AP", "2100") })
      {
        var account = await db.ClientAccounts.SingleAsync(x => x.ChartVersionId == chart.Value && x.AccountCode == code);
        var proposed = await ClientAccountRoleWorkspace.ProposeAsync(db, maker, f.ClientId,
          new(chart.Value, account.Id, role, new(2026, 1, 1), null, "Synthetic zero-control successor fixture"));
        Assert.True(proposed.Succeeded, proposed.Message);
        var decision = await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, f.ClientId, proposed.Value,
          "APPROVE", "Synthetic independent control review");
        Assert.True(decision.Succeeded, decision.Message);
      }
      Guid? originalPartyId = null;
      if (withOriginalItem)
      {
        var party = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, maker, f.ClientId,
          new("Synthetic original customer", "Synthetic original customer", "CUSTOMER", "QA address", "QA", "", "", "", "", "TEST", "CARRY-UI-001"));
        Assert.True(party.Succeeded, party.Message); originalPartyId = party.Value;
      }
      var opening = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, maker,
        new(f.ClientId, periodId, 1, new(2026, 1, 1), "QAR", "Synthetic recorded opening cash and capital",
          Hashing.Sha256Hex("Synthetic opening evidence fixture bytes"), withOriginalItem
            ? [new("1000", 100m, 0m), new("1100", 25m, 0m), new("3000", 0m, 125m)]
            : [new("1000", 125m, 0m), new("3000", 0m, 125m)],
          originalPartyId is { } partyId ? [new("AR", partyId, "ORIGINAL-CARRY-UI-001", new(2026, 2, 10), "1100", 25m)] : null));
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

    var targetId = Guid.CreateVersion7();
    await using (var db = host.CreateDbContext())
    {
      var close = await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Synthetic accepted source closed by official command");
      Assert.True(close.Succeeded, close.Message);
      // Existing ACTIVE target is fixture context, not a claim of public activation/cutover approval.
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = targetId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "SYNTHETIC-SUCCESSOR-2027",
        StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31), Basis = "STATUTORY", Currency = "QAR",
        Status = AccountingWorkflowStates.Active, PriorPeriodId = periodId, Revision = 1,
        CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    var angular = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH") is { Length: > 0 } buildPath)
      angular["AngularUi__BuildPath"] = buildPath;
    var makerOrigin = await host.StartApiForIdentityAsync(f.Admin, angular);
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var makerContext = await browser.NewContextAsync();
    await using var reviewerContext = await browser.NewContextAsync();
    var page = await makerContext.NewPageAsync(); var reviewPage = await reviewerContext.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add("maker: " + error);
    reviewPage.PageError += (_, error) => errors.Add("reviewer: " + error);
    var path = $"/ui/app/accounting/clients/{f.ClientId:D}/operational-opening-balances";
    var delayedReviewerProof = false;
    async Task OpenAsync(IPage browserPage, string origin)
    {
      await browserPage.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
      await Assertions.Expect(browserPage.GetByRole(AriaRole.Heading, new() { Name = "Native successor opening", Exact = true })).ToBeVisibleAsync();
      // Retain the exact response body before the target-period navigation can release it.
      var catalogue = await PlaywrightResponseCapture.RunAsync(browserPage,
        request => request.Method == "GET" && request.Url.Contains($"/operational-opening-successors/catalog/{targetId:D}?", StringComparison.Ordinal),
        async () =>
        {
          if (browserPage == reviewPage && withOriginalItem)
            await browserPage.RouteAsync(new System.Text.RegularExpressions.Regex("/operational-opening-successors/[0-9a-f-]{36}$"), async route =>
            {
              if (delayedReviewerProof) { await route.ContinueAsync(); return; }
              delayedReviewerProof = true;
              var response = await route.FetchAsync();
              Assert.Equal(200, response.Status);
              await Task.Delay(TimeSpan.FromSeconds(8)); // A valid proof read may finish after the catalogue renders.
              await route.FulfillAsync(new() { Response = response });
            });
          await browserPage.GetByLabel("Opening period", new() { Exact = true }).SelectOptionAsync(targetId.ToString());
        });
      Assert.True(catalogue.Status == 200, $"Catalogue HTTP {catalogue.Status}: {catalogue.Body}");
      await browserPage.GetByRole(AriaRole.Button, new() { Name = "Native predecessor opening", Exact = true }).ClickAsync();
      await Assertions.Expect(browserPage.GetByRole(AriaRole.Heading, new() { Name = "Exact native successor opening", Exact = true })).ToBeVisibleAsync();
      if (browserPage == page)
      {
        try { await Assertions.Expect(browserPage.GetByLabel("Accepted native trial balance source", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 5000 }); }
        catch (Exception ex) { throw new InvalidOperationException($"Catalogue: {catalogue.Body} UI: {await browserPage.Locator("body").InnerTextAsync()}", ex); }
      }
    }
    await OpenAsync(page, makerOrigin);
    await page.GetByLabel("Accepted native trial balance source", new() { Exact = false }).SelectOptionAsync(tb.DatasetId.ToString());
    await page.GetByLabel("Successor evidence reference", new() { Exact = true }).FillAsync("Synthetic exact accepted source successor");
    var previewButton = page.GetByRole(AriaRole.Button, new() { Name = "Preview exact successor opening", Exact = true });
    await previewButton.FocusAsync();
    var previewResponse = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "POST" && request.Url.EndsWith("/operational-opening-successors/preview", StringComparison.Ordinal),
      () => page.Keyboard.PressAsync("Enter"));
    Assert.Equal(200, previewResponse.Status);
    using var previewJson = JsonDocument.Parse(previewResponse.Body);
    var previewPayload = previewJson.RootElement.GetProperty("value");
    var manifestHash = previewPayload.GetProperty("prospectiveOpeningManifestHash").GetString()!;
    var reviewBasis = previewPayload.GetProperty("reviewBasis").GetString()!;
    Assert.Equal(tb.SourceIdentityHash, previewPayload.GetProperty("sourceIdentityHash").GetString());
    Assert.Equal("1", previewPayload.GetProperty("periodRevision").GetString());
    Assert.Equal("2", previewPayload.GetProperty("priorPeriodRevision").GetString());
    Assert.Equal(2, previewPayload.GetProperty("controls").GetArrayLength());
    var table = page.GetByRole(AriaRole.Table, new() { Name = "Exact successor opening preview", Exact = true });
    await Assertions.Expect(table.GetByRole(AriaRole.Rowheader, new() { Name = "1000 · Synthetic cash", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(table.GetByText("135", new() { Exact = true })).ToHaveCountAsync(withOriginalItem ? 1 : 2);
    if (withOriginalItem)
    {
      var population = previewPayload.GetProperty("carriedPopulation");
      Assert.Equal(1, population.GetProperty("count").GetInt32());
      var item = Assert.Single(population.GetProperty("items").EnumerateArray());
      Assert.Equal(openingId, item.GetProperty("originalDocumentId").GetGuid());
      Assert.Equal("25.000000", item.GetProperty("originalAmount").GetString());
      await Assertions.Expect(page.GetByRole(AriaRole.Table, new() { Name = "Original-item carry preview", Exact = true })
        .GetByText("ORIGINAL-CARRY-UI-001", new() { Exact = true })).ToBeVisibleAsync();
      var sourceDetails = page.GetByRole(AriaRole.Region, new() { Name = "Original-item carry preview table", Exact = true });
      Assert.True(await sourceDetails.EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth"));
      await sourceDetails.FocusAsync();
      await Assertions.Expect(sourceDetails).ToBeFocusedAsync();
      await sourceDetails.PressAsync("ArrowRight");
      await page.WaitForFunctionAsync("() => document.querySelector('[aria-label=\"Original-item carry preview table\"]').scrollLeft > 0");
    }
    await Assertions.Expect(page.Locator("code").Filter(new() { HasText = tb.SourceIdentityHash }).First).ToBeVisibleAsync();
    var create = page.GetByRole(AriaRole.Button, new() { Name = "Create successor opening for independent review", Exact = true });
    await Assertions.Expect(create).ToBeDisabledAsync();
    await OwnedBrowserFixtureHandoff.WaitAsync(makerOrigin, path, withOriginalItem ? "native-successor-carry-preview" : "native-successor-preview");
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the exact source, target and predecessor revisions, original items, all opening rows, control identities and prospective manifest.", Exact = true }).CheckAsync();
    string? originalRequest = null;
    var committedResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var createPattern = "**/operational-opening-successors/create";
    await page.RouteAsync(createPattern, async route =>
    {
      originalRequest = route.Request.PostData;
      try
      {
        var response = await route.FetchAsync();
        var body = await response.TextAsync();
        if (response.Status != 200) throw new InvalidOperationException($"Actual create HTTP {response.Status}: {body}");
        await route.AbortAsync();
        committedResponse.TrySetResult(body);
      }
      catch (Exception error) { committedResponse.TrySetException(error); throw; } // Actual server commit, deliberately lost response; retain the original client checkpoint.
    });
    await create.ClickAsync();
    await committedResponse.Task.WaitAsync(TimeSpan.FromSeconds(35));
    await Assertions.Expect(page.GetByText("Original creation request retained.", new() { Exact = false })).ToBeVisibleAsync();
    await page.UnrouteAsync(createPattern);
    Assert.NotNull(originalRequest);
    using var original = JsonDocument.Parse(originalRequest!);
    Assert.Equal(reviewBasis, original.RootElement.GetProperty("reviewBasis").GetString());
    var recover = page.GetByRole(AriaRole.Button, new() { Name = "Recover original successor creation", Exact = true });
    await Assertions.Expect(recover).ToBeDisabledAsync();
    var recoveryValidation = await page.RunAndWaitForResponseAsync(
      () => page.GetByRole(AriaRole.Button, new() { Name = "Validate original successor recovery preview", Exact = true }).ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/operational-opening-successors/preview", StringComparison.Ordinal));
    Assert.Equal(200, recoveryValidation.Status);
    await Assertions.Expect(recover).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the retained original intent and want to recover that same successor creation request.", Exact = true }).CheckAsync();
    var replay = await page.RunAndWaitForResponseAsync(() => recover.ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/operational-opening-successors/create", StringComparison.Ordinal));
    Assert.Equal(200, replay.Status); Assert.Equal(originalRequest, replay.Request.PostData);
    await Assertions.Expect(page.GetByText("Matching retained successor evidence recovered. No new intent was created.", new() { Exact = true })).ToBeVisibleAsync();
    Guid successorId; string proofHash;
    await using (var db = host.CreateDbContext())
    {
      var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.ClientId == f.ClientId && x.PeriodId == targetId);
      successorId = opening.Id; Assert.Equal(manifestHash, opening.ManifestSha256); Assert.Null(opening.ApprovedByUserId);
      var proof = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleAsync(x => x.OpeningId == successorId);
      proofHash = proof.ProofHash; Assert.Equal(tb.SourceIdentityHash, proof.SourceIdentityHash);
      Assert.Equal(tb.DatasetId, proof.SourceDatasetId); Assert.Equal(original.RootElement.GetProperty("requestId").GetGuid(), proof.RequestId);
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Approve exact successor proof and opening", Exact = true })).ToHaveCountAsync(0);
    var retainedProof = await reviewPage.RunAndWaitForResponseAsync(() => OpenAsync(reviewPage, reviewerOrigin),
      response => response.Request.Method == "GET" && response.Url.EndsWith($"/operational-opening-successors/{successorId:D}", StringComparison.Ordinal));
    Assert.Equal(200, retainedProof.Status);
    Assert.Null(await retainedProof.FinishedAsync());
    var approve = reviewPage.GetByRole(AriaRole.Button, new() { Name = "Approve exact successor proof and opening", Exact = true });
    await Assertions.Expect(approve).ToBeDisabledAsync();
    await reviewPage.GetByRole(AriaRole.Checkbox, new() { Name = "I independently reviewed this exact successor proof and opening manifest, original source, carried original items and account population.", Exact = true }).CheckAsync();
    var approvedResponse = await reviewPage.RunAndWaitForResponseAsync(() => approve.ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith($"/operational-opening-successors/{successorId:D}/approve", StringComparison.Ordinal));
    Assert.True(approvedResponse.Status is 200 or 204);
    using var approval = JsonDocument.Parse(approvedResponse.Request.PostData!);
    Assert.Equal(proofHash, approval.RootElement.GetProperty("expectedSuccessorProofHash").GetString());
    Assert.Equal(manifestHash, approval.RootElement.GetProperty("expectedOpeningManifestHash").GetString());
    await Assertions.Expect(reviewPage.GetByText("Independently approved", new() { Exact = false })).ToBeVisibleAsync();
    await reviewPage.ReloadAsync();
    await reviewPage.GetByLabel("Opening period", new() { Exact = true }).SelectOptionAsync(targetId.ToString());
    await Assertions.Expect(reviewPage.Locator("code").Filter(new() { HasText = proofHash }).First).ToBeVisibleAsync();
    if (withOriginalItem)
      await Assertions.Expect(reviewPage.GetByRole(AriaRole.Table, new() { Name = "Retained original-item carry", Exact = true })
        .GetByText("ORIGINAL-CARRY-UI-001", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var saved = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == successorId);
      Assert.Equal(f.Reviewer.Id, saved.ApprovedByUserId); Assert.Equal(manifestHash, saved.ManifestSha256);
      var proof = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleAsync(x => x.OpeningId == successorId);
      Assert.Equal(proofHash, proof.ProofHash); Assert.Equal(tb.SourceIdentityHash, proof.SourceIdentityHash);
      Assert.Equal(1, await db.ClientOperationalOpeningSuccessorProofs.CountAsync(x => x.OpeningId == successorId));
      Assert.Equal(AccountingWorkflowStates.Active, (await db.ClientReportingPeriods.SingleAsync(x => x.Id == targetId)).Status);
    }
    Assert.Empty(errors);
    if (withOriginalItem)
      await OwnedBrowserFixtureHandoff.WaitAsync(reviewerOrigin, path, "native-successor-carry-retained");
  }
}
