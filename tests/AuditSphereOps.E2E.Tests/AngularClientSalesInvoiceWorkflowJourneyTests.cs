using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientSalesInvoiceWorkflowJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-CLIENT-SALES-INVOICE-WORKFLOW-01")]
  public async Task SalesInvoiceFlowsFromUntaxedPreparationToIndependentClientLedgerPosting()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-SALES-INVOICE-WORKFLOW-01");
    var f = host.Fixture;
    var maker = PbcSeed.Actor(f.Admin, "Administrator");
    var reviewer = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    Guid customerId, periodId, receivableRoleId, invoiceId;
    string clientName;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer"));
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Synthetic invoice workflow service", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = f.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, maker,
        new(f.ClientId, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE", ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
      var period = await ClientAccountingService.CreatePeriodAsync(db, maker,
        new(f.ClientId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"));
      Assert.True(period.Succeeded, period.Message); periodId = period.Value;
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, maker, f.ClientId, "JOURNEY", new DateOnly(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message);
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, maker, chart.Value,
      [
        new("ar", "1100", "Trade receivables", "ASSET", "DEBIT", true),
        new("revenue", "4000", "Service revenue", "INCOME", "CREDIT", true)
      ])).Succeeded);
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chart.Value)).Succeeded);
      var ar = await db.ClientAccounts.SingleAsync(x => x.ChartVersionId == chart.Value && x.AccountCode == "1100");
      var role = await ClientAccountRoleWorkspace.ProposeAsync(db, maker, f.ClientId,
        new(chart.Value, ar.Id, "AR", new DateOnly(2026, 1, 1), null, "Invoice receivables"));
      Assert.True(role.Succeeded, role.Message); receivableRoleId = role.Value;
      Assert.True((await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, f.ClientId, role.Value, "APPROVE", "Independent control account review")).Succeeded);
      var customer = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, maker, f.ClientId,
        new("Synthetic customer", "Synthetic customer", "CUSTOMER", "QA address", "QA", "", "", "", "", "", ""));
      Assert.True(customer.Succeeded, customer.Message); customerId = customer.Value;
      var saved = await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, maker, f.ClientId,
        new(Guid.CreateVersion7(), null, 0, "INV-UI-001", "SOURCE-INV-001", period.Value, customer.Value,
          new(2026, 1, 10), new(2026, 1, 10), new(2026, 1, 10), new(2026, 2, 10), "QAR",
          new("QAR", 2, "AWAY_FROM_ZERO", "REJECT", 0, ""),
          [new("Client consulting", "4000", 1, 125m, 0m, "NONE", [])], "Accepted client service evidence"));
      Assert.True(saved.Succeeded, saved.Message); invoiceId = saved.Value!.InvoiceId;
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
    var apiResponses = new List<string>();
    makerPage.PageError += (_, error) => errors.Add(error);
    reviewerPage.PageError += (_, error) => errors.Add(error);
    makerPage.Response += (_, response) => { if (response.Url.Contains("sales-invoice", StringComparison.Ordinal)) apiResponses.Add($"maker {response.Status} {response.Url}"); };
    reviewerPage.Response += (_, response) => { if (response.Url.Contains("sales-invoice", StringComparison.Ordinal)) apiResponses.Add($"reviewer {response.Status} {response.Url}"); };

    async Task<IReadOnlyList<ILocator>> OpenDraftAsync(IPage page, string origin, string expectedState)
    {
      await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
      await page.Locator("audit-accounting").GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
      var drafts = page.Locator("audit-sales-invoice-drafts");
      await drafts.GetByLabel("Open client sales draft by ID", new() { Exact = true }).FillAsync(invoiceId.ToString());
      await drafts.GetByRole(AriaRole.Button, new() { Name = "Open client sales draft", Exact = true }).ClickAsync();
      await Assertions.Expect(drafts.GetByRole(AriaRole.Heading, new() { Name = "INV-UI-001 · Preparation revision 1", Exact = true })).ToBeVisibleAsync();
      var expectedLifecycle = $"Accounting state: {expectedState} · Posted: No · Issued: No · Delivery: NOT_REQUESTED";
      try { await Assertions.Expect(drafts.GetByText(expectedLifecycle, new() { Exact = true })).ToBeVisibleAsync(); }
      catch (PlaywrightException e)
      {
        var componentText = await drafts.Locator("audit-sales-invoice-workflow").InnerTextAsync();
        throw new InvalidOperationException($"Invoice lifecycle did not load. Component text: {componentText}\nAPI responses: {string.Join("; ", apiResponses)}", e);
      }
      return [drafts];
    }

    var makerDrafts = (await OpenDraftAsync(makerPage, makerOrigin, "DRAFT"))[0];
    var workflow = makerDrafts.Locator("audit-sales-invoice-workflow");
    await workflow.GetByLabel("Approved receivables role ID", new() { Exact = true }).FillAsync(receivableRoleId.ToString());
    await workflow.GetByLabel("Source basis or explanation", new() { Exact = true }).FillAsync("Accepted client service evidence");
    await workflow.GetByRole(AriaRole.Button, new() { Name = "Preview invoice submission", Exact = true }).ClickAsync();
    await Assertions.Expect(workflow.GetByText("Previewed preparation revision 1 · 125.00 QAR", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(workflow.GetByText("The untaxed preparation path does not require the optional tax module.", new() { Exact = false })).ToBeVisibleAsync();
    await workflow.GetByRole(AriaRole.Checkbox, new() { Name = "I checked this client, exact preparation revision, receivables role, no-tax reason, source basis and proposed client-ledger lines.", Exact = true }).CheckAsync();
    var submitResponse = await makerPage.RunAndWaitForResponseAsync(
      async () => await workflow.GetByRole(AriaRole.Button, new() { Name = "Submit invoice for independent review", Exact = true }).ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/sales-invoices/" + invoiceId + "/submit", StringComparison.Ordinal));
    Assert.Equal(200, submitResponse.Status);
    await Assertions.Expect(workflow.GetByText("Accounting state: SUBMITTED · Posted: No · Issued: No · Delivery: NOT_REQUESTED", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(workflow.GetByRole(AriaRole.Button, new() { Name = "Submit invoice for independent review", Exact = true })).ToHaveCountAsync(0);

    var reviewerDrafts = (await OpenDraftAsync(reviewerPage, reviewerOrigin, "SUBMITTED"))[0];
    var reviewerWorkflow = reviewerDrafts.Locator("audit-sales-invoice-workflow");
    await reviewerWorkflow.GetByRole(AriaRole.Button, new() { Name = "Preview submitted invoice review", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerWorkflow.GetByText("Independent accounting decision", new() { Exact = true })).ToBeVisibleAsync();
    await reviewerWorkflow.GetByLabel("Invoice review decision", new() { Exact = true }).SelectOptionAsync("APPROVE");
    await reviewerWorkflow.GetByLabel("Invoice review reason", new() { Exact = true }).FillAsync("Reviewed customer, source and balanced client-ledger lines");
    await reviewerWorkflow.GetByRole(AriaRole.Checkbox, new() { Name = "I independently reviewed this submitted revision, source basis, no-tax reason and exact client-ledger effect. Approval commits the accounting posting and receivable open item.", Exact = true }).CheckAsync();
    var reviewResponse = await reviewerPage.RunAndWaitForResponseAsync(
      async () => await reviewerWorkflow.GetByRole(AriaRole.Button, new() { Name = "Record independent invoice decision", Exact = true }).ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/sales-invoice-reviews", StringComparison.Ordinal));
    Assert.Equal(200, reviewResponse.Status);
    await Assertions.Expect(reviewerWorkflow.GetByText("Accounting state: POSTED · Posted: Yes · Issued: No · Delivery: NOT_REQUESTED", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerWorkflow.GetByText("Original receivable open amount: 125.000000 · Due 2026-02-10.", new() { Exact = false })).ToBeVisibleAsync();

    await workflow.GetByRole(AriaRole.Button, new() { Name = "Refresh invoice lifecycle", Exact = true }).ClickAsync();
    var credits = workflow.Locator("audit-sales-credit-note-workflow");
    await Assertions.Expect(credits.GetByRole(AriaRole.Heading, new() { Name = "Sales credit notes", Exact = true })).ToBeVisibleAsync();
    await credits.GetByLabel("Credit note reference", new() { Exact = true }).FillAsync("CN-UI-001");
    await credits.GetByLabel("Posting date", new() { Exact = true }).FillAsync("2026-01-20");
    await credits.GetByLabel("Commercial reason", new() { Exact = true }).FillAsync("Partial service cancellation");
    await credits.GetByLabel("Source evidence or explanation", new() { Exact = true }).FillAsync("Client-approved cancellation evidence");
    await credits.GetByLabel("Line 1 · Client consulting · 4000 · Original 125.00 QAR", new() { Exact = true }).FillAsync("25");
    var creditPreviewResponse = await makerPage.RunAndWaitForResponseAsync(
      async () => await credits.GetByRole(AriaRole.Button, new() { Name = "Preview positive credit", Exact = true }).ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/sales-invoices/" + invoiceId + "/credit-notes/preview", StringComparison.Ordinal));
    Assert.Equal(200, creditPreviewResponse.Status);
    await Assertions.Expect(credits.GetByText("Exact credit preview: 25.000000 QAR · Original 125.000000 · Previously credited 0.000000 · Remaining limit 125.000000", new() { Exact = true })).ToBeVisibleAsync();
    await credits.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this positive credit and its effect on the original client invoice.", Exact = true }).CheckAsync();
    var creditSubmitResponse = await makerPage.RunAndWaitForResponseAsync(
      async () => await credits.GetByRole(AriaRole.Button, new() { Name = "Submit credit note for independent review", Exact = true }).ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/sales-invoices/" + invoiceId + "/credit-notes", StringComparison.Ordinal));
    Assert.Equal(200, creditSubmitResponse.Status);
    var reviewerCredits = reviewerWorkflow.Locator("audit-sales-credit-note-workflow");
    await reviewerCredits.GetByRole(AriaRole.Button, new() { Name = "Refresh credit-note history", Exact = true }).ClickAsync();
    var submittedCredit = reviewerCredits.Locator("article").Filter(new() { HasText = "CN-UI-001" });
    await submittedCredit.GetByRole(AriaRole.Button, new() { Name = "Review credit note", Exact = true }).ClickAsync();
    await Assertions.Expect(submittedCredit.GetByText("Ready for review", new() { Exact = false })).ToBeVisibleAsync();
    await submittedCredit.GetByLabel("Independent review reason", new() { Exact = true }).FillAsync("Independently checked the partial credit and source evidence");
    var creditReviewResponse = await reviewerPage.RunAndWaitForResponseAsync(
      async () => await submittedCredit.GetByRole(AriaRole.Button, new() { Name = "Approve and post credit", Exact = true }).ClickAsync(),
      response => response.Request.Method == "POST" && response.Url.EndsWith("/sales-credit-note-reviews", StringComparison.Ordinal));
    Assert.Equal(200, creditReviewResponse.Status);
    await Assertions.Expect(reviewerCredits.GetByText("Posted as an unapplied customer credit. Apply it through the client settlement workflow when available.", new() { Exact = true })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      Assert.Single(await db.ClientSalesInvoiceSubmissions.Where(x => x.ClientId == f.ClientId).ToListAsync());
      Assert.Single(await db.ClientSalesInvoiceOpenItems.Where(x => x.ClientId == f.ClientId).ToListAsync());
      Assert.Single(await db.ClientSalesCreditNoteOpenItems.Where(x => x.ClientId == f.ClientId).ToListAsync());
      Assert.Equal(4, (await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, f.ClientId, periodId)).Value!.TotalEntries);
      Assert.Empty(await db.FirmJournals.ToListAsync());
    }
    Assert.Empty(errors);
  }
}
