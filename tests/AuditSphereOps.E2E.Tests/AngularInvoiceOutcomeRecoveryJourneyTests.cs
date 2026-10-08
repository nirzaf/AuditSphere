using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularInvoiceOutcomeRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-INVOICE-OUTCOME-01")]
  public async Task AcceptedApprovePostAndSendWithLostResponsesRequireRefreshBeforeContinuing()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-INVOICE-OUTCOME-01");
    var f = host.Fixture;
    var manager = PbcSeed.Actor(f.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(f.Reviewer, "FinanceReviewer");
    Guid invoiceId;
    const string invoiceNumber = "SYN-ANG-INVOICE-OUTCOME-001";
    const string lostResponseMessage = "The last command response was lost. Refresh the invoice and billing history to confirm the saved status or record before preparing another action.";
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
          [new InvoiceLineRequest("Synthetic annual assurance service", 1m, 1200m)]));
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
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    var managerOrigin = await host.StartApiForIdentityAsync(f.Admin, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var pageErrors = new List<string>();
    var invoiceRoute = $"/app/practice/invoices/{invoiceId:D}";

    await using var reviewerContext = await browser.NewContextAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    reviewerPage.PageError += (_, error) => pageErrors.Add($"reviewer: {error}");
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString(invoiceRoute));
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = invoiceNumber, Exact = true })).ToBeVisibleAsync();
    var approveCalls = 0;
    await reviewerPage.RouteAsync($"**/api/ui/finance/invoices/{invoiceId:D}/approve", async interception =>
    {
      if (interception.Request.Method != "POST")
      {
        await interception.ContinueAsync();
        return;
      }
      Interlocked.Increment(ref approveCalls);
      var accepted = await interception.FetchAsync();
      Assert.Equal(200, accepted.Status);
      await interception.AbortAsync();
    });
    await reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Approve invoice", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Verify the saved billing state", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByText(lostResponseMessage, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Approve invoice", Exact = true })).ToBeDisabledAsync();
    Assert.Equal(1, approveCalls);
    await reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Refresh persisted billing state", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.Locator("section[aria-labelledby='invoice-heading'] [data-status='APPROVED']"))
      .ToBeVisibleAsync();
    await reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Clear unresolved billing draft", Exact = true }).ClickAsync();
    Assert.Equal(1, approveCalls);

    await using var managerContext = await browser.NewContextAsync();
    var managerPage = await managerContext.NewPageAsync();
    managerPage.PageError += (_, error) => pageErrors.Add($"manager: {error}");
    await managerPage.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString(invoiceRoute));
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Post invoice (freeze & emit ledger event)", Exact = true })).ToBeVisibleAsync();
    var postCalls = 0;
    await managerPage.RouteAsync($"**/api/ui/finance/invoices/{invoiceId:D}/post", async interception =>
    {
      if (interception.Request.Method != "POST")
      {
        await interception.ContinueAsync();
        return;
      }
      Interlocked.Increment(ref postCalls);
      var accepted = await interception.FetchAsync();
      Assert.Equal(200, accepted.Status);
      await interception.AbortAsync();
    });
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Post invoice (freeze & emit ledger event)", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Verify the saved billing state", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByText(lostResponseMessage, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Post invoice (freeze & emit ledger event)", Exact = true })).ToBeDisabledAsync();
    Assert.Equal(1, postCalls);
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Refresh persisted billing state", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.Locator("section[aria-labelledby='invoice-heading'] [data-status='POSTED']"))
      .ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Mark sent to client", Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Clear unresolved billing draft", Exact = true }).ClickAsync();
    Assert.Equal(1, postCalls);

    var sendCalls = 0;
    await managerPage.RouteAsync($"**/api/ui/finance/invoices/{invoiceId:D}/send", async interception =>
    {
      if (interception.Request.Method != "POST")
      {
        await interception.ContinueAsync();
        return;
      }
      Interlocked.Increment(ref sendCalls);
      var accepted = await interception.FetchAsync();
      Assert.Equal(200, accepted.Status);
      await interception.AbortAsync();
    });
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Mark sent to client", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Verify the saved billing state", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Mark sent to client", Exact = true })).ToBeDisabledAsync();
    Assert.Equal(1, sendCalls);
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Refresh persisted billing state", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.Locator("section[aria-labelledby='invoice-heading'] [data-status='SENT']"))
      .ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Mark sent to client", Exact = true })).ToHaveCountAsync(0);
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Clear unresolved billing draft", Exact = true }).ClickAsync();
    Assert.Equal(1, sendCalls);

    await using (var db = host.CreateDbContext())
    {
      var sent = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoiceId);
      Assert.Equal(BillingStates.InvoiceSent, sent.Status);
      Assert.Equal(f.Reviewer.Id, sent.ApprovedByUserId);
      Assert.NotNull(sent.ApprovedAt);
      Assert.NotNull(sent.PostedAt);
      Assert.NotNull(sent.SentAt);
    }
    Assert.Empty(pageErrors);
  }
}
