using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
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
      db.Users.AddRange(owner, viewer);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(fixture.FirmId, owner, "FinanceManager", fixture.ClientId),
        PbcSeed.Grant(fixture.FirmId, viewer, "FinanceManager", viewerClientId));
      await db.SaveChangesAsync();

      var ownerActor = PbcSeed.Actor(owner, "FinanceManager");
      var ownerAccount = await BillingService.CreateBillingAccountAsync(db, ownerActor,
        new CreateBillingAccountRequest(fixture.ClientId, "QAR"));
      Assert.True(ownerAccount.Succeeded, ownerAccount.Message);
      var invoice = await BillingService.CreateInvoiceDraftAsync(db, ownerActor,
        new CreateInvoiceDraftRequest(ownerAccount.Value, invoiceNumber, [new InvoiceLineRequest(privateLine, 1m, 247m)]));
      Assert.True(invoice.Succeeded, invoice.Message);
      invoiceId = invoice.Value;

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
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";

  private static Task PushHistoryRouteAsync(IPage page, string path) =>
    page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }", path);
}
