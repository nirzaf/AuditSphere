using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularInvoiceWorkflowJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-INVOICE-WORKFLOW-01")]
  public async Task InvoiceApprovalPostingAndSendingRespectDistinctRoleCapabilities()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-INVOICE-WORKFLOW-01");
    var f = host.Fixture;
    var manager = PbcSeed.Actor(f.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(f.Reviewer, "FinanceReviewer");
    Guid invoiceId;
    const string invoiceNumber = "SYN-ANG-INVOICE-WORKFLOW-001";
    const string lineDescription = "Synthetic annual audit services";

    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();

      var account = await BillingService.CreateBillingAccountAsync(db, manager,
        new CreateBillingAccountRequest(f.ClientId, "QAR"));
      Assert.True(account.Succeeded, account.Message);
      var invoice = await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(account.Value, invoiceNumber,
          [new InvoiceLineRequest(lineDescription, 1m, 1200m)]));
      Assert.True(invoice.Succeeded, invoice.Message);
      invoiceId = invoice.Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, invoiceId)).Succeeded);
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = f.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var managerOrigin = await host.StartApiForIdentityAsync(f.Admin, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var errors = new List<string>();

    var managerPage = await browser.NewPageAsync();
    managerPage.PageError += (_, error) => errors.Add($"manager: {error}");
    await managerPage.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/app/practice/invoices/{invoiceId:D}"));
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = invoiceNumber, Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByText(lineDescription, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Approve invoice", Exact = true })).ToHaveCountAsync(0);

    var unauthorizedApproval = await managerPage.EvaluateAsync<int>("""
      async () => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const response = await fetch('/api/ui/finance/invoices/__INVOICE_ID__/approve', {
          method: 'POST', headers: { 'X-XSRF-TOKEN': token }
        });
        return response.status;
      }
      """.Replace("__INVOICE_ID__", invoiceId.ToString("D"), StringComparison.Ordinal));
    Assert.Equal(403, unauthorizedApproval);
    await using (var db = host.CreateDbContext())
      Assert.Equal(BillingStates.InvoiceReviewRequired,
        await db.Invoices.Where(x => x.Id == invoiceId).Select(x => x.Status).SingleAsync());

    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => errors.Add($"reviewer: {error}");
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/app/practice/invoices/{invoiceId:D}"));
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = invoiceNumber, Exact = true })).ToBeVisibleAsync();
    await reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Approve invoice", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("Invoice approved.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Post invoice (freeze & emit ledger event)", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(reviewerPage.GetByText(
      "Posting requires an authorized FinanceManager and an approved firm finance profile in the invoice currency.",
      new() { Exact = true })).ToBeVisibleAsync();
    var unauthorizedPost = await reviewerPage.EvaluateAsync<int>("""
      async () => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const response = await fetch('/api/ui/finance/invoices/__INVOICE_ID__/post', {
          method: 'POST', headers: { 'X-XSRF-TOKEN': token }
        });
        return response.status;
      }
      """.Replace("__INVOICE_ID__", invoiceId.ToString("D"), StringComparison.Ordinal));
    Assert.Equal(403, unauthorizedPost);
    await using (var db = host.CreateDbContext())
    {
      var approved = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoiceId);
      Assert.Equal(BillingStates.InvoiceApproved, approved.Status);
      Assert.Equal(f.Reviewer.Id, approved.ApprovedByUserId);
    }

    await managerPage.ReloadAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Post invoice (freeze & emit ledger event)", Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Post invoice (freeze & emit ledger event)", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText("Invoice posted and frozen.", new() { Exact = true })).ToBeVisibleAsync();
    var repeatedPostResponses = await managerPage.EvaluateAsync<string>("""
      async () => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const send = async () => {
          const response = await fetch('/api/ui/finance/invoices/__INVOICE_ID__/post', {
            method: 'POST', headers: { 'X-XSRF-TOKEN': token }
          });
          return { status: response.status, body: await response.text() };
        };
        const first = await send();
        const retry = await send();
        return JSON.stringify({ first, retry });
      }
      """.Replace("__INVOICE_ID__", invoiceId.ToString("D"), StringComparison.Ordinal));
    using (var responses = System.Text.Json.JsonDocument.Parse(repeatedPostResponses))
    {
      var root = responses.RootElement;
      Assert.Equal(200, root.GetProperty("first").GetProperty("status").GetInt32());
      Assert.Equal(200, root.GetProperty("retry").GetProperty("status").GetInt32());
      Assert.Equal(root.GetProperty("first").GetProperty("body").GetString(),
        root.GetProperty("retry").GetProperty("body").GetString());
    }
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Mark sent to client", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText("Invoice marked as sent.", new() { Exact = true })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var sent = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoiceId);
      Assert.Equal(BillingStates.InvoiceSent, sent.Status);
      Assert.Equal(f.Reviewer.Id, sent.ApprovedByUserId);
      Assert.NotNull(sent.PostedAt);
      Assert.NotNull(sent.SentAt);
    }
    await managerPage.SetViewportSizeAsync(390, 844);
    Assert.True(await managerPage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    Assert.Empty(errors);
  }
}
