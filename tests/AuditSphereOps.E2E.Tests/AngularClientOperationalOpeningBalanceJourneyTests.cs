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
    Assert.Equal(200, create.Status);
    var createBody = await create.JsonAsync() ?? throw new InvalidOperationException("Opening creation response was empty.");
    var openingId = createBody.GetProperty("id").GetGuid();
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
    await Assertions.Expect(journals.GetByRole(AriaRole.Cell, new() { Name = "1000 · Opening cash", Exact = true })).ToBeVisibleAsync();
    var trialBalance = journals.GetByRole(AriaRole.Table, new() { Name = "Official native Trial Balance · 2026-01-01 to 2026-12-31", Exact = true });
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
    Assert.Empty(errors);
  }
}
