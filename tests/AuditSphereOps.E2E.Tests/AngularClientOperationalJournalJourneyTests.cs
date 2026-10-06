using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientOperationalJournalJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-CLIENT-OPERATIONAL-JOURNAL-01")]
  public async Task NativeClientJournalRequiresIndependentReviewAndPostsThroughAngular()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-OPERATIONAL-JOURNAL-01");
    var fixture = host.Fixture;
    var admin = PbcSeed.Actor(fixture.Admin, "Administrator");
    var reviewer = PbcSeed.Actor(fixture.Reviewer, "AccountingReviewer");
    Guid chartId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, fixture.Reviewer, "AccountingReviewer"));
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Synthetic approved bookkeeping service", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = fixture.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var created = await ClientAccountingService.CreateChartVersionAsync(db, admin, fixture.ClientId, "JOURNEY", new DateOnly(2026, 1, 1));
      Assert.True(created.Succeeded, created.Message);
      chartId = created.Value;
      var accounts = await ClientAccountingService.AddAccountsAsync(db, admin, chartId,
      [
        new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
        new("expense", "6000", "Office expense", "EXPENSE", "DEBIT", true)
      ]);
      Assert.True(accounts.Succeeded, accounts.Message);
    }
    await using (var db = host.CreateDbContext())
    {
      var published = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId);
      Assert.True(published.Succeeded, published.Message);
    }

    var origin = await host.StartApiForIdentityAsync(fixture.Admin, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    var workspace = page.Locator("audit-accounting");
    string clientName;
    await using (var db = host.CreateDbContext())
      clientName = await db.PracticeClients.Where(x => x.Id == fixture.ClientId).Select(x => x.LegalName).SingleAsync();
    await workspace.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    var profile = workspace.Locator("form").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Create accounting profile", Exact = true }) });
    await profile.GetByLabel("Jurisdiction", new() { Exact = true }).FillAsync("QA");
    await profile.GetByLabel("Functional currency", new() { Exact = true }).FillAsync("QAR");
    await profile.GetByLabel("Source system", new() { Exact = true }).FillAsync("AUDITSPHERE");
    await profile.GetByLabel("Source identifier", new() { Exact = true }).FillAsync("NATIVE");
    var sourceMode = profile.Locator("select[name='sourceMode']");
    await Assertions.Expect(sourceMode).ToBeEnabledAsync();
    await sourceMode.SelectOptionAsync("NATIVE_BOOKKEEPING");
    await profile.GetByRole(AriaRole.Checkbox).CheckAsync();
    await profile.GetByRole(AriaRole.Button, new() { Name = "Save profile", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client bookkeeping journals", Exact = true })).ToBeVisibleAsync();

    var period = workspace.Locator("form").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Create reporting period", Exact = true }) });
    await period.GetByLabel("Period code", new() { Exact = true }).FillAsync("2026");
    await period.GetByLabel("Start date", new() { Exact = true }).FillAsync("2026-01-01");
    await period.GetByLabel("End date", new() { Exact = true }).FillAsync("2026-12-31");
    await period.GetByLabel("Reporting basis", new() { Exact = true }).FillAsync("STATUTORY");
    await period.GetByLabel("Reporting currency", new() { Exact = true }).FillAsync("QAR");
    await period.GetByRole(AriaRole.Checkbox).CheckAsync();
    await period.GetByRole(AriaRole.Button, new() { Name = "Create period", Exact = true }).ClickAsync();
    await Assertions.Expect(workspace.GetByRole(AriaRole.Cell, new() { Name = "2026", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(workspace.GetByRole(AriaRole.Heading, new() { Name = "Client bookkeeping journals", Exact = true })).ToBeVisibleAsync();
    Guid periodId;
    await using (var db = host.CreateDbContext()) periodId = await db.ClientReportingPeriods.Where(x => x.ClientId == fixture.ClientId).Select(x => x.Id).SingleAsync();

    var journals = page.Locator("audit-client-operational-journals");
    await journals.Locator("select[name='period']").SelectOptionAsync(periodId.ToString());
    await journals.GetByLabel("Journal number", new() { Exact = true }).FillAsync("J-UI-001");
    await journals.GetByLabel("Description", new() { Exact = true }).FillAsync("Record office expense");
    await journals.GetByLabel("Accounting date", new() { Exact = true }).FillAsync("2026-01-15");
    await journals.GetByLabel("Account code").Nth(0).FillAsync("6000");
    await journals.GetByLabel("Debit").Nth(0).FillAsync("125.25");
    await journals.GetByLabel("Credit").Nth(0).FillAsync("0");
    await journals.GetByLabel("Account code").Nth(1).FillAsync("1000");
    await journals.GetByLabel("Debit").Nth(1).FillAsync("0");
    await journals.GetByLabel("Credit").Nth(1).FillAsync("125.25");
    var creatorReview = journals.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the selected client, period, date and exact balanced intent.", Exact = true });
    await Assertions.Expect(journals.GetByRole(AriaRole.Button, new() { Name = "Save journal draft", Exact = true })).ToBeDisabledAsync();
    await creatorReview.CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Save journal draft", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · DRAFT", Exact = true })).ToBeVisibleAsync();
    var submitReview = journals.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this client, journal, posting date, account selection and exact amounts.", Exact = true });
    await submitReview.CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Submit for independent review", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · SUBMITTED", Exact = true })).ToBeVisibleAsync();
    Guid journalId;
    await using (var db = host.CreateDbContext()) journalId = await db.ClientOperationalJournals.Where(x => x.ClientId == fixture.ClientId).Select(x => x.Id).SingleAsync();

    var reviewerOrigin = await host.StartApiForIdentityAsync(fixture.Reviewer, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    await page.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await workspace.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await journals.GetByLabel("Open a saved journal by ID", new() { Exact = true }).FillAsync(journalId.ToString());
    await journals.GetByRole(AriaRole.Button, new() { Name = "Open journal", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · SUBMITTED", Exact = true })).ToBeVisibleAsync();
    await journals.GetByLabel("Approval reason", new() { Exact = true }).FillAsync("Independently reviewed and balanced");
    await journals.GetByRole(AriaRole.Checkbox, new() { Name = "I independently reviewed this exact journal revision and its balanced lines.", Exact = true }).CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Approve and post", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · POSTED", Exact = true })).ToBeVisibleAsync();
    var ledgerAccounts = journals.GetByRole(AriaRole.Table, new() { Name = "Account debit, credit and net movement", Exact = true });
    await Assertions.Expect(ledgerAccounts.GetByRole(AriaRole.Cell, new() { Name = "6000 · Office expense", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(ledgerAccounts.GetByRole(AriaRole.Cell, new() { Name = "-125.250000", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Table, new() { Name = "Posted journal line detail", Exact = true }).GetByRole(AriaRole.Row)).ToHaveCountAsync(3);
    await using (var db = host.CreateDbContext())
    {
      var posted = await db.ClientOperationalJournals.SingleAsync(x => x.Id == journalId);
      Assert.Equal("POSTED", posted.Status);
      Assert.Equal(fixture.Reviewer.Id, posted.PostedByUserId);
      Assert.Single(await db.ClientOperationalJournalDecisions.Where(x => x.JournalId == journalId).ToListAsync());
    }
    Assert.Empty(errors);
  }
}
