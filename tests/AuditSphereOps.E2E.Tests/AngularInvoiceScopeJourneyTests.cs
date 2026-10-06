using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularInvoiceScopeJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-INV-01")]
  public async Task FinanceManagerScopedToAnotherClientCannotViewInvoiceOrFallbackBalance()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-INV-01");
    var fixture = host.Fixture;
    var owner = PbcSeed.User(fixture.FirmId, "Staff");
    var viewer = PbcSeed.User(fixture.FirmId, "Staff");
    var reviewer = PbcSeed.User(fixture.FirmId, "Staff");
    var viewerClientId = Guid.NewGuid();
    const string invoiceNumber = "SYN-PAR-002-INV-001";
    const string privateLine = "Synthetic restricted invoice line";
    const string viewerInvoiceNumber = "SYN-PAR-002-INV-002";
    const string viewerPrivateLine = "Synthetic other-client invoice line";
    Guid invoiceId;
    Guid viewerInvoiceId;

    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = viewerClientId,
        FirmId = fixture.FirmId,
        LegalName = "Synthetic unrelated billing client",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = viewerClientId, FirmId = fixture.FirmId });
      db.Users.AddRange(owner, viewer, reviewer);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(fixture.FirmId, owner, "FinanceManager", fixture.ClientId),
        PbcSeed.Grant(fixture.FirmId, viewer, "FinanceManager", viewerClientId),
        PbcSeed.Grant(fixture.FirmId, reviewer, "FinanceReviewer", fixture.ClientId));
      await db.SaveChangesAsync();

      var ownerActor = PbcSeed.Actor(owner, "FinanceManager");
      var ownerAccount = await BillingService.CreateBillingAccountAsync(db, ownerActor,
        new CreateBillingAccountRequest(fixture.ClientId, "QAR"));
      Assert.True(ownerAccount.Succeeded, ownerAccount.Message);
      var invoice = await BillingService.CreateInvoiceDraftAsync(db, ownerActor,
        new CreateInvoiceDraftRequest(ownerAccount.Value, invoiceNumber, [new InvoiceLineRequest(privateLine, 1m, 247m)]));
      Assert.True(invoice.Succeeded, invoice.Message);
      invoiceId = invoice.Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, ownerActor, invoiceId)).Succeeded);

      var viewerActor = PbcSeed.Actor(viewer, "FinanceManager");
      var viewerAccount = await BillingService.CreateBillingAccountAsync(db, viewerActor,
        new CreateBillingAccountRequest(viewerClientId, "QAR"));
      Assert.True(viewerAccount.Succeeded, viewerAccount.Message);
      var viewerInvoice = await BillingService.CreateInvoiceDraftAsync(db, viewerActor,
        new CreateInvoiceDraftRequest(viewerAccount.Value, viewerInvoiceNumber,
          [new InvoiceLineRequest(viewerPrivateLine, 1m, 991m)]));
      Assert.True(viewerInvoice.Succeeded, viewerInvoice.Message);
      viewerInvoiceId = viewerInvoice.Value;
    }

    var ownerOrigin = await host.StartApiForIdentityAsync(owner);
    var viewerOrigin = await host.StartApiForIdentityAsync(viewer);
    var reviewerOrigin = await host.StartApiForIdentityAsync(reviewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var ownerContext = await browser.NewContextAsync();
    var ownerPage = await ownerContext.NewPageAsync();
    var ownerErrors = new List<string>();
    ownerPage.PageError += (_, error) => ownerErrors.Add($"page-error: {error}");
    await ownerPage.GotoAsync(SignInUrl(ownerOrigin, $"/app/practice/invoices/{invoiceId:D}"));
    await Assertions.Expect(ownerPage.GetByRole(AriaRole.Heading, new() { Name = invoiceNumber, Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(ownerPage.GetByText(privateLine, new() { Exact = true })).ToBeVisibleAsync();
    Assert.Contains("247.00", await ownerPage.Locator("body").InnerTextAsync());

    var documentToken = Guid.NewGuid().ToString("N");
    await ownerPage.EvaluateAsync("token => window.__invoiceRouteTestToken = token", documentToken);
    await PushHistoryRouteAsync(ownerPage, $"/app/practice/invoices/{viewerInvoiceId:D}");
    await Assertions.Expect(ownerPage.GetByText(
      "The requested invoice was not found in the current firm scope.", new() { Exact = true })).ToBeVisibleAsync();
    var deniedRouteBody = await ownerPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(invoiceNumber, deniedRouteBody);
    Assert.DoesNotContain(privateLine, deniedRouteBody);
    Assert.DoesNotContain("247.00", deniedRouteBody);
    Assert.DoesNotContain(viewerInvoiceNumber, deniedRouteBody);
    Assert.DoesNotContain(viewerPrivateLine, deniedRouteBody);
    Assert.DoesNotContain("991.00", deniedRouteBody);
    Assert.Equal(documentToken, await ownerPage.EvaluateAsync<string>("window.__invoiceRouteTestToken"));

    await using var reviewerContext = await browser.NewContextAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    var reviewerErrors = new List<string>();
    reviewerPage.PageError += (_, error) => reviewerErrors.Add($"page-error: {error}");
    await reviewerPage.GotoAsync(SignInUrl(reviewerOrigin, $"/app/practice/invoices/{invoiceId:D}"));
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = invoiceNumber, Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Approve invoice", Exact = true })).ToBeVisibleAsync();
    await reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Approve invoice", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("Invoice approved.", new() { Exact = true })).ToBeVisibleAsync();

    var crossClientApproval = await reviewerPage.EvaluateAsync<int>("""
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
      """.Replace("__INVOICE_ID__", viewerInvoiceId.ToString("D"), StringComparison.Ordinal));
    Assert.Equal(403, crossClientApproval);
    await PushHistoryRouteAsync(reviewerPage, $"/app/practice/invoices/{viewerInvoiceId:D}");
    await Assertions.Expect(reviewerPage.GetByText(
      "The requested invoice was not found in the current firm scope.", new() { Exact = true })).ToBeVisibleAsync();
    var reviewerDeniedBody = await reviewerPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(invoiceNumber, reviewerDeniedBody);
    Assert.DoesNotContain(privateLine, reviewerDeniedBody);
    Assert.DoesNotContain(viewerInvoiceNumber, reviewerDeniedBody);
    Assert.DoesNotContain(viewerPrivateLine, reviewerDeniedBody);
    Assert.DoesNotContain("247.00", reviewerDeniedBody);
    Assert.DoesNotContain("991.00", reviewerDeniedBody);
    await using (var db = host.CreateDbContext())
    {
      var approved = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoiceId);
      Assert.Equal(BillingStates.InvoiceApproved, approved.Status);
      Assert.Equal(reviewer.Id, approved.ApprovedByUserId);
      var sibling = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == viewerInvoiceId);
      Assert.Equal(BillingStates.InvoiceDraft, sibling.Status);
      Assert.Null(sibling.ApprovedByUserId);
    }

    await PushHistoryRouteAsync(ownerPage, $"/app/practice/invoices/{invoiceId:D}");
    await Assertions.Expect(ownerPage.GetByRole(AriaRole.Heading, new() { Name = invoiceNumber, Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(ownerPage.GetByText(privateLine, new() { Exact = true })).ToBeVisibleAsync();
    Assert.Contains("247.00", await ownerPage.Locator("body").InnerTextAsync());

    await using var viewerContext = await browser.NewContextAsync();
    var viewerPage = await viewerContext.NewPageAsync();
    var viewerErrors = new List<string>();
    viewerPage.PageError += (_, error) => viewerErrors.Add($"page-error: {error}");
    await viewerPage.GotoAsync(SignInUrl(viewerOrigin, $"/app/practice/invoices/{viewerInvoiceId:D}"));
    await Assertions.Expect(viewerPage.GetByRole(AriaRole.Heading, new() { Name = viewerInvoiceNumber, Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(viewerPage.GetByText(viewerPrivateLine, new() { Exact = true })).ToBeVisibleAsync();
    Assert.Contains("991.00", await viewerPage.Locator("body").InnerTextAsync());

    await viewerPage.GotoAsync(viewerOrigin + $"/app/practice/invoices/{invoiceId:D}");
    await Assertions.Expect(viewerPage.GetByText(
      "The requested invoice was not found in the current firm scope.", new() { Exact = true })).ToBeVisibleAsync();
    var deniedBody = await viewerPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(invoiceNumber, deniedBody);
    Assert.DoesNotContain(privateLine, deniedBody);
    Assert.DoesNotContain("247.00", deniedBody);
    Assert.DoesNotContain(viewerInvoiceNumber, deniedBody);
    Assert.DoesNotContain(viewerPrivateLine, deniedBody);
    Assert.Empty(ownerErrors);
    Assert.Empty(viewerErrors);
    Assert.Empty(reviewerErrors);
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";

  private static Task PushHistoryRouteAsync(IPage page, string path) =>
    page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }", path);
}
