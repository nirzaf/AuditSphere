using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// A Partner publishes a technical library entry prepared by a Manager and finds it through search, opens practice
/// analytics, and finance records, reviews and posts a rent expense before calculating the firm trial balance.
/// </summary>
[Trait("Category", "FirmOperations")]
public sealed class FirmOperationsJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-STE-FIRM-OPS-01")]
  public async Task LibraryPublishAnalyticsAndExpenseToTrialBalance()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-STE-FIRM-OPS-01");
    var f = host.Fixture;
    var partner = PbcSeed.User(f.FirmId, "Staff"); partner.DisplayName = "Pat Partner";
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var finance = PbcSeed.User(f.FirmId, "Staff"); finance.DisplayName = "Fin Manager";
    var reviewer = PbcSeed.User(f.FirmId, "Staff"); reviewer.DisplayName = "Fin Reviewer";
    Guid entryId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(partner, manager, finance, reviewer);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, partner, "Partner"), PbcSeed.Grant(f.FirmId, manager, "Manager"),
        PbcSeed.Grant(f.FirmId, finance, "FinanceManager"), PbcSeed.Grant(f.FirmId, reviewer, "FinanceReviewer"));
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile { Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile,
        Approved = true, ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      entryId = (await TechnicalLibraryService.CreateAsync(db, new ActorContext(manager.Id, f.FirmId, manager.SessionEpoch, ["Manager"]), "ISA-505", "External confirmations",
        "ISA", "ALL_STAFF", "The auditor maintains control over external confirmation requests, including selecting the confirming parties.", "ISA 505", new DateOnly(2010, 12, 15))).Value;
      var financeActor = new ActorContext(finance.Id, f.FirmId, finance.SessionEpoch, ["FinanceManager"]);
      await LedgerService.CreateFirmAccountAsync(db, financeActor, new CreateFirmAccountRequest("1000", "Bank", LedgerStates.AccountAsset, LedgerStates.Debit));
      await LedgerService.CreateFirmAccountAsync(db, financeActor, new CreateFirmAccountRequest("6100", "Office rent", LedgerStates.AccountExpense, LedgerStates.Debit));
      Assert.True((await LedgerService.CreateFirmPeriodAsync(db, financeActor, new CreateFirmPeriodRequest(DateTime.Today.ToString("yyyy-MM")))).Succeeded);
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var diagnostics = new List<string>();
    async Task<IPage> SignInAsync(Domain.Security.AppUser user, string path, string heading)
    {
      var origin = await host.StartApiForIdentityAsync(user);
      var page = await (await browser.NewContextAsync()).NewPageAsync();
      page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
      await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(path)}");
      await page.GetByRole(AriaRole.Heading, new() { Name = heading }).WaitForAsync(new() { Timeout = 20000 });
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
      await page.WaitForTimeoutAsync(500);
      return page;
    }

    // Library: the Partner publishes the Manager's draft, then finds it by search.
    var partnerPage = await SignInAsync(partner, $"/app/library/{entryId:D}", "ISA-505 — External confirmations");
    await partnerPage.GetByRole(AriaRole.Button, new() { Name = "Publish v1 (second approver)" }).ClickAsync();
    await Assertions.Expect(partnerPage.Locator(".command-result").Last).ToContainTextAsync("Version published");
    await partnerPage.GetByLabel("Search", new() { Exact = true }).FillAsync("confirming parties");
    await partnerPage.GetByRole(AriaRole.Button, new() { Name = "Search library" }).ClickAsync();
    await Assertions.Expect(partnerPage.Locator("[aria-label='Library results']")).ToContainTextAsync("ISA-505 — External confirmations");
    await Assertions.Expect(partnerPage.Locator("[aria-label='Library results']")).ToContainTextAsync("v1");

    await partnerPage.GotoAsync(partnerPage.Url.Split("/app/")[0] + "/app/practice/analytics");
    await partnerPage.GetByRole(AriaRole.Heading, new() { Name = "Definitions" }).WaitForAsync(new() { Timeout = 20000 });
    await Assertions.Expect(partnerPage.GetByText("charge-out rates are not costs", new() { Exact = false })).ToBeVisibleAsync();

    // Finance records and submits a rent expense with its source document.
    var financePage = await SignInAsync(finance, "/app/finance/books", "Operating expenses");
    await financePage.GetByLabel("Payee", new() { Exact = true }).FillAsync("West Bay Towers");
    await financePage.GetByLabel("Description", new() { Exact = true }).FillAsync("Office rent");
    await financePage.GetByLabel("Amount", new() { Exact = true }).FillAsync("6500");
    await financePage.Locator("select[name='expenseAccount']").SelectOptionAsync(new SelectOptionValue { Label = "6100 Office rent" });
    await financePage.Locator("select[name='paymentAccount']").SelectOptionAsync(new SelectOptionValue { Label = "1000 Bank" });
    await financePage.GetByLabel("Source document", new() { Exact = true }).SetInputFilesAsync(new FilePayload { Name = "rent-invoice.pdf", MimeType = "application/pdf", Buffer = "%PDF rent invoice"u8.ToArray() });
    await financePage.WaitForTimeoutAsync(500);
    await financePage.GetByRole(AriaRole.Button, new() { Name = "Record expense" }).ClickAsync();
    await Assertions.Expect(financePage.Locator(".command-result").Last).ToContainTextAsync("Expense recorded as a draft.");
    await financePage.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
    await Assertions.Expect(financePage.Locator(".command-result").Last).ToContainTextAsync("Submitted");

    var reviewerPage = await SignInAsync(reviewer, "/app/finance/books", "Operating expenses");
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
    await Assertions.Expect(reviewerPage.Locator(".command-result").Last).ToContainTextAsync("Approved.");

    await financePage.ReloadAsync();
    await financePage.GetByRole(AriaRole.Heading, new() { Name = "Operating expenses" }).WaitForAsync();
    await financePage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
    await financePage.WaitForTimeoutAsync(500);
    await financePage.GetByRole(AriaRole.Button, new() { Name = "Post to ledger" }).ClickAsync();
    await Assertions.Expect(financePage.Locator(".command-result").Last).ToContainTextAsync("Posted to the firm ledger.");
    await financePage.GetByRole(AriaRole.Button, new() { Name = "Calculate" }).ClickAsync();
    await Assertions.Expect(financePage.GetByRole(AriaRole.Region, new() { Name = "Firm trial balance", Exact = true }))
      .ToContainTextAsync("6100 Office rent");
    await Assertions.Expect(financePage.Locator("[aria-label='Firm financial summary']")).ToContainTextAsync("Balanced.");
    await Assertions.Expect(financePage.Locator("[aria-label='Firm financial summary']")).ToContainTextAsync("reconciles");
    await using (var db = host.CreateDbContext())
      Assert.Equal(FirmExpenseStates.Posted, (await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId)).Status);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }
}
