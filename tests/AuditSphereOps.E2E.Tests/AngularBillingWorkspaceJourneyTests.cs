using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularBillingWorkspaceJourneyTests
{
  [Fact]
  public async Task FinanceManagerRecordsAllocatesAndRecoversCreditNoteInNativeInvoiceWorkspace()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-BILLING-WORKSPACE-E2E");
    var f = host.Fixture;
    var manager = PbcSeed.Actor(f.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(f.Reviewer, "FinanceReviewer");
    Guid accountId, invoiceId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();
      accountId = (await BillingService.CreateBillingAccountAsync(db, manager,
        new CreateBillingAccountRequest(f.ClientId, "QAR"))).Value;
      invoiceId = (await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-ANG-E2E-INV-001", [new InvoiceLineRequest("Synthetic audit fee", 1m, 100m)]))).Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewer, invoiceId)).Succeeded);
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile,
        Approved = true, ApprovedByUserId = f.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, invoiceId)).Succeeded);
      var sameInstant = DateTimeOffset.UtcNow.AddDays(-20);
      db.Receipts.AddRange(Enumerable.Range(1, 101).Select(i => new Receipt
      {
        Id = HistoryId(i), FirmId = f.FirmId, BillingAccountId = accountId,
        Amount = 1m, Currency = "QAR", Reference = $"SYN-PAGE-R-{i:D3}",
        RecordedByUserId = f.Admin.Id, ReceivedAt = sameInstant
      }));
      db.CreditNotes.AddRange(Enumerable.Range(1, 101).Select(i => new CreditNote
      {
        Id = HistoryId(i), FirmId = f.FirmId, BillingAccountId = accountId, InvoiceId = invoiceId,
        NoteNumber = $"SYN-PAGE-C-{i:D3}", Currency = "QAR", Amount = 0.01m,
        Reason = "Synthetic history page", CreatedByUserId = f.Admin.Id, CreatedAt = sameInstant
      }));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    var route = $"/app/practice/invoices/{invoiceId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "SYN-ANG-E2E-INV-001" })).ToBeVisibleAsync();

    var invalidCursorResponses = await page.EvaluateAsync<string>("""
      async () => {
        const base = '/api/ui/finance/invoices/__INVOICE_ID__';
        const queries = [
          '?receiptBefore=2026-10-01T00%3A00%3A00Z',
          '?receiptBefore=2026-10-01T00%3A00%3A00Z&receiptBeforeId=00000000-0000-0000-0000-000000000000',
          '?creditBefore=2026-10-01T00%3A00%3A00Z'
        ];
        const responses = [];
        for (const query of queries) {
          const response = await fetch(base + query);
          responses.push({ status: response.status, body: await response.text() });
        }
        return JSON.stringify(responses);
      }
      """.Replace("__INVOICE_ID__", invoiceId.ToString("D"), StringComparison.Ordinal));
    using (var responses = System.Text.Json.JsonDocument.Parse(invalidCursorResponses))
    {
      var invalid = responses.RootElement.EnumerateArray().ToArray();
      Assert.Equal(3, invalid.Length);
      Assert.All(invalid, response => Assert.Equal(400, response.GetProperty("status").GetInt32()));
      Assert.All(invalid, response => Assert.Equal(invalid[0].GetProperty("body").GetString(), response.GetProperty("body").GetString()));
      Assert.DoesNotContain("SYN-ANG-E2E-INV-001", invalid[0].GetProperty("body").GetString(), StringComparison.Ordinal);
    }

    await page.GetByLabel("Payment amount (QAR)", new() { Exact = true }).FillAsync("40");
    await page.GetByLabel("Bank or cheque transaction reference", new() { Exact = true }).FillAsync("SYN-ANG-BANK-001");
    await page.GetByLabel("I reviewed the payment amount and transaction reference.", new() { Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Record receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Receipt recorded. Refresh or allocate it to a posted invoice when ready.", new() { Exact = true })).ToBeVisibleAsync();
    Guid receiptId;
    await using (var db = host.CreateDbContext())
      receiptId = await db.Receipts.AsNoTracking().Where(x => x.Reference == "SYN-ANG-BANK-001").Select(x => x.Id).SingleAsync();

    await page.GetByLabel("Receipt and available balance", new() { Exact = true }).SelectOptionAsync(receiptId.ToString("D"));
    await page.GetByLabel("Amount to allocate (QAR)", new() { Exact = true }).FillAsync("25");
    await page.GetByLabel("I reviewed this receipt, invoice and allocation amount.", new() { Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Allocate receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Receipt allocation recorded against this invoice.", new() { Exact = true })).ToBeVisibleAsync();

    await page.GetByLabel("Credit note number", new() { Exact = true }).FillAsync("SYN-ANG-CN-001");
    await page.GetByLabel("Credit amount (QAR)", new() { Exact = true }).FillAsync("10");
    await page.GetByLabel("Reason", new() { Exact = true }).FillAsync("Synthetic reviewed adjustment");
    await page.GetByLabel("I reviewed the credit note number, amount and reason.", new() { Exact = true }).CheckAsync();
    var creditCalls = 0;
    await page.RouteAsync("**/api/ui/finance/invoices/*/credit-notes", async interception =>
    {
      Interlocked.Increment(ref creditCalls);
      var response = await interception.FetchAsync();
      Assert.Equal(200, response.Status);
      await interception.AbortAsync();
    });
    await page.GetByRole(AriaRole.Button, new() { Name = "Issue credit note", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Verify the saved billing state", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh persisted billing state", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("SYN-ANG-CN-001", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Clear unresolved billing draft", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(1, creditCalls);
    await page.GetByRole(AriaRole.Button, new() { Name = "Clear unresolved billing draft", Exact = true }).ClickAsync();
    var interceptedReceiptPage = false;
    await page.RouteAsync("**/api/ui/finance/invoices/**", async interception =>
    {
      if (!interceptedReceiptPage && interception.Request.Method == "GET" &&
          interception.Request.Url.Contains("receiptBefore=", StringComparison.Ordinal))
      {
        interceptedReceiptPage = true;
        await interception.FulfillAsync(new()
        {
          Status = 503,
          ContentType = "application/json",
          Body = "{\"code\":\"synthetic.unavailable\",\"message\":\"Synthetic temporary failure\"}"
        });
        return;
      }
      await interception.ContinueAsync();
    });
    await page.GetByRole(AriaRole.Button, new() { Name = "Load older receipts", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert).GetByText(
      "Older receipts could not be loaded. Try again shortly.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("SYN-PAGE-R-101", new() { Exact = true })).ToBeVisibleAsync();
    Assert.True(interceptedReceiptPage);
    await page.GetByRole(AriaRole.Button, new() { Name = "Load older receipts", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("SYN-PAGE-R-001", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Load older credit notes", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("SYN-PAGE-C-001", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(1, await db.CreditNotes.CountAsync(x => x.InvoiceId == invoiceId && x.NoteNumber == "SYN-ANG-CN-001"));
      Assert.Equal(25m, await db.ReceiptAllocations.Where(x => x.InvoiceId == invoiceId).SumAsync(x => x.Amount));
    }

    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    Assert.Empty(errors);
  }

  private static Guid HistoryId(int number) => Guid.Parse($"00000000-0000-7000-8000-{number:000000000000}");
}
