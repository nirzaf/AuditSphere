using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.Json;

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
    await journals.GetByLabel("Debit").Nth(0).FillAsync("10000000000000");
    await journals.GetByLabel("Credit").Nth(1).FillAsync("10000000000000");
    await creatorReview.CheckAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Button, new() { Name = "Save journal draft", Exact = true })).ToBeDisabledAsync();
    await journals.GetByLabel("Debit").Nth(0).FillAsync("125.25");
    await journals.GetByLabel("Credit").Nth(1).FillAsync("125.25");
    await Assertions.Expect(creatorReview).Not.ToBeCheckedAsync();
    await creatorReview.CheckAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Button, new() { Name = "Save journal draft", Exact = true })).ToBeEnabledAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Save journal draft", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · DRAFT", Exact = true })).ToBeVisibleAsync();
    var submitReview = journals.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this client, journal, posting date, account selection and exact amounts.", Exact = true });
    await Assertions.Expect(journals.GetByRole(AriaRole.Button, new() { Name = "Submit for independent review", Exact = true })).ToBeDisabledAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Preview accounting effect", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Status)).ToContainTextAsync("Server-validated preview");
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
    await journals.GetByLabel("Review reason", new() { Exact = true }).FillAsync("Clarify expense evidence");
    await journals.GetByRole(AriaRole.Checkbox, new() { Name = "I independently reviewed this exact journal revision and its balanced lines.", Exact = true }).CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Return for rework", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · RETURNED", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Region, new() { Name = "Journal review history", Exact = true })).ToContainTextAsync("Clarify expense evidence");
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await workspace.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await journals.GetByLabel("Open a saved journal by ID", new() { Exact = true }).FillAsync(journalId.ToString());
    await journals.GetByRole(AriaRole.Button, new() { Name = "Open journal", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · RETURNED", Exact = true })).ToBeVisibleAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Edit returned journal", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByLabel("Journal number", new() { Exact = true })).ToBeDisabledAsync();
    await journals.GetByLabel("Description", new() { Exact = true }).FillAsync("Corrected office expense");
    await journals.GetByLabel("Debit").Nth(0).FillAsync("150.25");
    await journals.GetByLabel("Credit").Nth(1).FillAsync("150.25");
    await creatorReview.CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Save rework draft", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · DRAFT", Exact = true })).ToBeVisibleAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Preview accounting effect", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Status)).ToContainTextAsync("revision 4");
    await submitReview.CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Submit for independent review", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · SUBMITTED", Exact = true })).ToBeVisibleAsync();
    await page.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await workspace.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await journals.GetByLabel("Open a saved journal by ID", new() { Exact = true }).FillAsync(journalId.ToString());
    await journals.GetByRole(AriaRole.Button, new() { Name = "Open journal", Exact = true }).ClickAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Preview accounting effect", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Status)).ToContainTextAsync("revision 5");
    await journals.GetByLabel("Review reason", new() { Exact = true }).FillAsync("Independently reviewed and balanced");
    await journals.GetByRole(AriaRole.Checkbox, new() { Name = "I independently reviewed this exact journal revision and its balanced lines.", Exact = true }).CheckAsync();
    // Commit the real request, then simulate losing its response. Recovery must use the original receipt, not repost.
    var postingKeys = new List<string>();
    await page.RouteAsync("**/operational-journals/*/post", async route => {
      using var body = JsonDocument.Parse(route.Request.PostData!);
      postingKeys.Add(body.RootElement.GetProperty("commandId").GetString()!);
      if (postingKeys.Count == 1) {
        await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"code\":\"synthetic.request-not-delivered\"}" });
        return;
      }
      var response = await route.FetchAsync();
      Assert.Equal(200, response.Status);
      await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"code\":\"synthetic.response-lost\"}" });
    });
    await journals.GetByRole(AriaRole.Button, new() { Name = "Approve and post", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByText("The journal action outcome could not be confirmed. Inspect the persisted journal before retrying.", new() { Exact = true })).ToBeVisibleAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Recover posting receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Button, new() { Name = "Retry original posting", Exact = true })).ToBeVisibleAsync();
    await journals.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the original posting request and want to retry that same command.", Exact = true }).CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Retry original posting", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByText("The journal action outcome could not be confirmed. Inspect the persisted journal before retrying.", new() { Exact = true })).ToBeVisibleAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Recover posting receipt", Exact = true }).ClickAsync();
    Assert.Equal(2, postingKeys.Count);
    Assert.Equal(postingKeys[0], postingKeys[1]);
    await Assertions.Expect(journals.GetByRole(AriaRole.Region, new() { Name = "Posting command recovery", Exact = true })).ToContainTextAsync("Confirmed posting");
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · POSTED", Exact = true })).ToBeVisibleAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "View submitted versions", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByText("Submitted revision 2", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(journals.GetByText("Submitted revision 5", new() { Exact = true })).ToBeVisibleAsync();
    await journals.GetByText("Submitted revision 2", new() { Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Table, new() { Name = "Preserved submitted journal lines", Exact = true }).First
      .GetByRole(AriaRole.Cell, new() { Name = "125.250000", Exact = true })).ToHaveCountAsync(2);
    var ledgerAccounts = journals.GetByRole(AriaRole.Table, new() { Name = "Account debit, credit and net movement", Exact = true });
    await Assertions.Expect(ledgerAccounts.GetByRole(AriaRole.Cell, new() { Name = "6000 · Office expense", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(ledgerAccounts.GetByRole(AriaRole.Cell, new() { Name = "-150.250000", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Table, new() { Name = "Posted journal line detail", Exact = true }).GetByRole(AriaRole.Row)).ToHaveCountAsync(3);
    var tb = journals.GetByRole(AriaRole.Table, new() { Name = "Official native Trial Balance", Exact = false });
    await Assertions.Expect(tb.GetByRole(AriaRole.Row)).ToHaveCountAsync(4);
    await Assertions.Expect(tb.GetByRole(AriaRole.Cell, new() { Name = "150.25", Exact = true })).ToHaveCountAsync(8);
    await journals.GetByLabel("Ledger from date", new() { Exact = true }).FillAsync("2026-02-01");
    await journals.GetByRole(AriaRole.Button, new() { Name = "Refresh posted ledger", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByText("No posted native journal lines in this period.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(tb.GetByRole(AriaRole.Cell, new() { Name = "150.25", Exact = true })).ToHaveCountAsync(8);
    await journals.GetByLabel("Ledger from date", new() { Exact = true }).FillAsync("");
    await journals.GetByRole(AriaRole.Button, new() { Name = "Refresh posted ledger", Exact = true }).ClickAsync();
    await using (var db = host.CreateDbContext())
    {
      var posted = await db.ClientOperationalJournals.SingleAsync(x => x.Id == journalId);
      Assert.Equal("POSTED", posted.Status);
      Assert.Single(await db.ClientOperationalPostingReceipts.Where(x => x.JournalId == journalId).ToListAsync());
      Assert.Equal(fixture.Reviewer.Id, posted.PostedByUserId);
      var decisions = await db.ClientOperationalJournalDecisions.Where(x => x.JournalId == journalId).OrderBy(x => x.JournalRevision).ToListAsync();
      Assert.Equal(2, decisions.Count);
      Assert.Equal("RETURN", decisions[0].Decision);
      Assert.Equal("Clarify expense evidence", decisions[0].Reason);
      Assert.Equal("APPROVE", decisions[1].Decision);
    }
    await page.UnrouteAsync("**/operational-journals/*/post");
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await workspace.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await journals.GetByLabel("Open a saved journal by ID", new() { Exact = true }).FillAsync(journalId.ToString());
    await journals.GetByRole(AriaRole.Button, new() { Name = "Open journal", Exact = true }).ClickAsync();
    await journals.GetByText("Prepare a full reversal", new() { Exact = true }).ClickAsync();
    await journals.GetByLabel("Reversal reporting period", new() { Exact = true }).SelectOptionAsync(periodId.ToString());
    await journals.GetByLabel("Reversal journal number", new() { Exact = true }).FillAsync("J-UI-REV-001");
    await journals.GetByLabel("Reversal accounting date", new() { Exact = true }).FillAsync("2026-01-16");
    await journals.GetByLabel("Correction reason", new() { Exact = true }).FillAsync("Reverse duplicate office expense");
    await journals.GetByLabel("Correction evidence reference", new() { Exact = true }).FillAsync("SYN-REVERSAL-EVIDENCE-001");
    await journals.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the original, correction date, full reversed amounts, reason and evidence reference.", Exact = true }).CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Save reversal draft", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-REV-001 · DRAFT", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Region, new() { Name = "Reversal lineage", Exact = true })).ToContainTextAsync("SYN-REVERSAL-EVIDENCE-001");
    Guid reversalId;
    await using (var db = host.CreateDbContext()) reversalId = await db.ClientOperationalJournalReversals.Where(x => x.OriginalJournalId == journalId).Select(x => x.ReversalJournalId).SingleAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Preview accounting effect", Exact = true }).ClickAsync();
    await submitReview.CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Submit for independent review", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-REV-001 · SUBMITTED", Exact = true })).ToBeVisibleAsync();
    await page.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await workspace.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await journals.GetByLabel("Open a saved journal by ID", new() { Exact = true }).FillAsync(reversalId.ToString());
    await journals.GetByRole(AriaRole.Button, new() { Name = "Open journal", Exact = true }).ClickAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Preview accounting effect", Exact = true }).ClickAsync();
    await journals.GetByLabel("Review reason", new() { Exact = true }).FillAsync("Independently checked full reversal and evidence");
    await journals.GetByRole(AriaRole.Checkbox, new() { Name = "I independently reviewed this exact journal revision and its balanced lines.", Exact = true }).CheckAsync();
    await journals.GetByRole(AriaRole.Button, new() { Name = "Approve and post", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-REV-001 · POSTED", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(ledgerAccounts.GetByRole(AriaRole.Cell, new() { Name = "0.000000", Exact = true })).ToHaveCountAsync(2);
    await Assertions.Expect(journals.GetByRole(AriaRole.Table, new() { Name = "Posted journal line detail", Exact = true }).GetByRole(AriaRole.Row)).ToHaveCountAsync(5);
    await journals.GetByRole(AriaRole.Button, new() { Name = "Open original journal", Exact = true }).ClickAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Heading, new() { Name = "J-UI-001 · POSTED", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(journals.GetByRole(AriaRole.Region, new() { Name = "Linked correction", Exact = true })).ToContainTextAsync("POSTED");
    await Assertions.Expect(journals.GetByText("Prepare a full reversal", new() { Exact = true })).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext()) {
      Assert.Equal("POSTED", (await db.ClientOperationalJournals.SingleAsync(x => x.Id == journalId)).Status);
      Assert.Equal(2, await db.ClientOperationalPostingReceipts.CountAsync(x => x.ClientId == fixture.ClientId));
      Assert.Single(await db.ClientOperationalJournalReversals.Where(x => x.OriginalJournalId == journalId).ToListAsync());
    }
    Assert.Empty(errors);
  }
}
