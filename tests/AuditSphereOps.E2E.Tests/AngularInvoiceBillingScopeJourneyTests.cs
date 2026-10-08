using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularInvoiceBillingScopeJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-INV-BILLING-SCOPE-01")]
  public async Task ClientScopedManagerCanUseOwnBillingWorkspaceButCannotMutateSiblingBillingRecords()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-INV-BILLING-SCOPE-01");
    var f = host.Fixture;
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var reviewer = PbcSeed.User(f.FirmId, "Staff");
    var siblingManager = PbcSeed.User(f.FirmId, "Staff");
    var siblingReviewer = PbcSeed.User(f.FirmId, "Staff");
    var siblingClientId = Guid.NewGuid();
    Guid ownAccountId, ownInvoiceId, siblingAccountId, siblingInvoiceId, siblingReceiptId;
    const string ownInvoiceNumber = "SYN-PAR-002-SCOPED-INVOICE-A";

    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = siblingClientId,
        FirmId = f.FirmId,
        LegalName = "Synthetic sibling billing client",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClientId, FirmId = f.FirmId });
      db.Users.AddRange(manager, reviewer, siblingManager, siblingReviewer);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, manager, "FinanceManager", f.ClientId),
        PbcSeed.Grant(f.FirmId, reviewer, "FinanceReviewer", f.ClientId),
        PbcSeed.Grant(f.FirmId, siblingManager, "FinanceManager", siblingClientId),
        PbcSeed.Grant(f.FirmId, siblingReviewer, "FinanceReviewer", siblingClientId));
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var managerActor = PbcSeed.Actor(manager, "FinanceManager");
      var reviewerActor = PbcSeed.Actor(reviewer, "FinanceReviewer");
      ownAccountId = (await BillingService.CreateBillingAccountAsync(db, managerActor,
        new CreateBillingAccountRequest(f.ClientId, "QAR"))).Value;
      ownInvoiceId = (await BillingService.CreateInvoiceDraftAsync(db, managerActor,
        new CreateInvoiceDraftRequest(ownAccountId, ownInvoiceNumber,
          [new InvoiceLineRequest("Synthetic scoped audit service", 1m, 100m)]))).Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, managerActor, ownInvoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewerActor, ownInvoiceId)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, managerActor, ownInvoiceId)).Succeeded);

      var siblingManagerActor = PbcSeed.Actor(siblingManager, "FinanceManager");
      var siblingReviewerActor = PbcSeed.Actor(siblingReviewer, "FinanceReviewer");
      siblingAccountId = (await BillingService.CreateBillingAccountAsync(db, siblingManagerActor,
        new CreateBillingAccountRequest(siblingClientId, "QAR"))).Value;
      siblingInvoiceId = (await BillingService.CreateInvoiceDraftAsync(db, siblingManagerActor,
        new CreateInvoiceDraftRequest(siblingAccountId, "SYN-PAR-002-SCOPED-INVOICE-B",
          [new InvoiceLineRequest("Synthetic sibling confidential service", 1m, 900m)]))).Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, siblingManagerActor, siblingInvoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, siblingReviewerActor, siblingInvoiceId)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, siblingManagerActor, siblingInvoiceId)).Succeeded);
      var siblingReceipt = await BillingService.RecordReceiptAsync(db, siblingManagerActor,
        new RecordReceiptRequest(siblingAccountId, 50m, "SYN-SIBLING-RECEIPT"));
      Assert.True(siblingReceipt.Succeeded, siblingReceipt.Message);
      siblingReceiptId = siblingReceipt.Value;
    }

    var origin = await host.StartApiForIdentityAsync(manager, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/app/practice/invoices/{ownInvoiceId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = ownInvoiceNumber, Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Record a payment", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Issue a credit note", Exact = true })).ToBeVisibleAsync();
    var initialBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("SYN-PAR-002-SCOPED-INVOICE-B", initialBody);
    Assert.DoesNotContain("Synthetic sibling confidential service", initialBody);
    Assert.DoesNotContain("900.00", initialBody);

    await page.GetByLabel("Payment amount (QAR)", new() { Exact = true }).FillAsync("40");
    await page.GetByLabel("Bank or cheque transaction reference", new() { Exact = true })
      .FillAsync("SYN-SCOPED-RECEIPT-A");
    await page.GetByLabel("I reviewed the payment amount and transaction reference.", new() { Exact = true })
      .CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Record receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText(
      "Receipt recorded. Refresh or allocate it to a posted invoice when ready.", new() { Exact = true })).ToBeVisibleAsync();
    Guid ownReceiptId;
    await using (var db = host.CreateDbContext())
      ownReceiptId = await db.Receipts.AsNoTracking()
        .Where(x => x.FirmId == f.FirmId && x.Reference == "SYN-SCOPED-RECEIPT-A")
        .Select(x => x.Id).SingleAsync();

    var deniedStatusesJson = await page.EvaluateAsync<string>("""
      async ids => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const headers = { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token };
        const post = async (url, body) => (await fetch(url, {
          method: 'POST', headers, body: JSON.stringify(body)
        })).status;
        return JSON.stringify([
          await post(`/api/ui/finance/billing-accounts/${ids.siblingAccountId}/receipts`,
            { amount: '1', reference: 'SYN-UNAUTHORIZED-RECEIPT', reviewed: true }),
          await post(`/api/ui/finance/receipts/${ids.siblingReceiptId}/allocations`,
            { invoiceId: ids.siblingInvoiceId, amount: '1', reviewed: true }),
          await post(`/api/ui/finance/receipts/${ids.ownReceiptId}/allocations`,
            { invoiceId: ids.siblingInvoiceId, amount: '1', reviewed: true }),
          await post(`/api/ui/finance/invoices/${ids.siblingInvoiceId}/credit-notes`,
            { noteNumber: 'SYN-UNAUTHORIZED-CREDIT', amount: '1', reason: 'Must not persist', reviewed: true })
        ]);
      }
      """, new
    {
      siblingAccountId = siblingAccountId.ToString("D"),
      siblingReceiptId = siblingReceiptId.ToString("D"),
      siblingInvoiceId = siblingInvoiceId.ToString("D"),
      ownReceiptId = ownReceiptId.ToString("D")
    });
    using (var statuses = System.Text.Json.JsonDocument.Parse(deniedStatusesJson))
      Assert.Equal(new[] { 403, 403, 403, 403 }, statuses.RootElement.EnumerateArray()
        .Select(x => x.GetInt32()).ToArray());

    await page.GetByLabel("Receipt and available balance", new() { Exact = true })
      .SelectOptionAsync(ownReceiptId.ToString("D"));
    await page.GetByLabel("Amount to allocate (QAR)", new() { Exact = true }).FillAsync("25");
    await page.GetByLabel("I reviewed this receipt, invoice and allocation amount.", new() { Exact = true })
      .CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Allocate receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText(
      "Receipt allocation recorded against this invoice.", new() { Exact = true })).ToBeVisibleAsync();

    await page.GetByLabel("Credit note number", new() { Exact = true }).FillAsync("SYN-SCOPED-CREDIT-A");
    await page.GetByLabel("Credit amount (QAR)", new() { Exact = true }).FillAsync("10");
    await page.GetByLabel("Reason", new() { Exact = true }).FillAsync("Synthetic reviewed client adjustment");
    await page.GetByLabel("I reviewed the credit note number, amount and reason.", new() { Exact = true })
      .CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Issue credit note", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText(
      "Credit note issued. The invoice balance has been refreshed.", new() { Exact = true })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(1, await db.Receipts.CountAsync(x => x.FirmId == f.FirmId && x.BillingAccountId == ownAccountId));
      Assert.Equal(1, await db.Receipts.CountAsync(x => x.FirmId == f.FirmId && x.BillingAccountId == siblingAccountId));
      var ownAllocation = await db.ReceiptAllocations.AsNoTracking()
        .SingleAsync(x => x.FirmId == f.FirmId && x.ReceiptId == ownReceiptId);
      Assert.Equal(ownInvoiceId, ownAllocation.InvoiceId);
      Assert.Equal(25m, ownAllocation.Amount);
      Assert.Empty(await db.ReceiptAllocations.AsNoTracking()
        .Where(x => x.FirmId == f.FirmId && x.InvoiceId == siblingInvoiceId).ToListAsync());
      var ownCredit = await db.CreditNotes.AsNoTracking()
        .SingleAsync(x => x.FirmId == f.FirmId && x.InvoiceId == ownInvoiceId);
      Assert.Equal("SYN-SCOPED-CREDIT-A", ownCredit.NoteNumber);
      Assert.Empty(await db.CreditNotes.AsNoTracking()
        .Where(x => x.FirmId == f.FirmId && x.InvoiceId == siblingInvoiceId).ToListAsync());
      Assert.Equal(BillingStates.InvoicePosted,
        (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == siblingInvoiceId)).Status);
    }
    Assert.Empty(errors);
  }
}
