using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class InvoiceScopeJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-INV-01")]
  public async Task FinanceManagerScopedToAnotherClientCannotViewInvoiceOrFallbackBalance()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-INV-01");
    Guid invoiceId;
    Guid unrelatedInvoiceId;
    const string invoiceNumber = "SYN-PAR-002-INV-001";
    const string privateLine = "Synthetic restricted invoice line";
    const string unrelatedInvoiceNumber = "SYN-PAR-002-INV-002";
    const string unrelatedPrivateLine = "Synthetic other-client invoice line";
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var invoiceClientManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();

    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated billing client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = unrelatedClientId, FirmId = host.Fixture.FirmId });
      db.Users.AddRange(viewer, invoiceClientManager);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, viewer, "FinanceManager", unrelatedClientId),
        PbcSeed.Grant(host.Fixture.FirmId, invoiceClientManager, "FinanceManager", host.Fixture.ClientId));
      await db.SaveChangesAsync();

      var account = await BillingService.CreateBillingAccountAsync(db,
        PbcSeed.Actor(invoiceClientManager, "FinanceManager"),
        new CreateBillingAccountRequest(host.Fixture.ClientId, "QAR"));
      Assert.True(account.Succeeded, account.Message);
      var invoice = await BillingService.CreateInvoiceDraftAsync(db,
        PbcSeed.Actor(invoiceClientManager, "FinanceManager"),
        new CreateInvoiceDraftRequest(account.Value, invoiceNumber, [new InvoiceLineRequest(privateLine, 1m, 247m)]));
      Assert.True(invoice.Succeeded, invoice.Message);
      invoiceId = invoice.Value;

      var unrelatedAccount = await BillingService.CreateBillingAccountAsync(db,
        PbcSeed.Actor(viewer, "FinanceManager"), new CreateBillingAccountRequest(unrelatedClientId, "QAR"));
      Assert.True(unrelatedAccount.Succeeded, unrelatedAccount.Message);
      var unrelatedInvoice = await BillingService.CreateInvoiceDraftAsync(db,
        PbcSeed.Actor(viewer, "FinanceManager"), new CreateInvoiceDraftRequest(unrelatedAccount.Value,
          unrelatedInvoiceNumber, [new InvoiceLineRequest(unrelatedPrivateLine, 1m, 991m)]));
      Assert.True(unrelatedInvoice.Succeeded, unrelatedInvoice.Message);
      unrelatedInvoiceId = unrelatedInvoice.Value;
    }

    var managerUrl = await host.StartWebForIdentityAsync(invoiceClientManager);
    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var managerContext = await browser.NewContextAsync();
    var managerPage = await managerContext.NewPageAsync();
    var managerDiagnostics = new List<string>();
    var managerConnected = WaitForCircuitConnectionAsync(managerPage, managerDiagnostics);
    await managerPage.GotoAsync(SignInUrl(managerUrl, $"/app/practice/invoices/{invoiceId:D}"));
    await managerPage.GetByText(invoiceNumber, new() { Exact = true }).WaitForAsync();
    await managerConnected;
    var managerBody = await managerPage.Locator("body").InnerTextAsync();
    Assert.Contains(privateLine, managerBody);
    Assert.Contains("247.00", managerBody);
    Assert.DoesNotContain(managerDiagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    var documentToken = Guid.NewGuid().ToString("N");
    await managerPage.EvaluateAsync("token => window.__invoiceRouteTestToken = token", documentToken);
    await managerPage.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/practice/invoices/{unrelatedInvoiceId:D}");
    await managerPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var deniedRouteBody = await managerPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(invoiceNumber, deniedRouteBody);
    Assert.DoesNotContain(privateLine, deniedRouteBody);
    Assert.DoesNotContain("247.00", deniedRouteBody);
    Assert.DoesNotContain(unrelatedInvoiceNumber, deniedRouteBody);
    Assert.DoesNotContain(unrelatedPrivateLine, deniedRouteBody);
    Assert.Equal(documentToken, await managerPage.EvaluateAsync<string>("window.__invoiceRouteTestToken"));

    await managerPage.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/practice/invoices/{invoiceId:D}");
    await managerPage.GetByText(invoiceNumber, new() { Exact = true }).WaitForAsync();
    Assert.Contains(privateLine, await managerPage.Locator("body").InnerTextAsync());

    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);

    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/practice/invoices/{invoiceId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(invoiceNumber, body);
    Assert.DoesNotContain(privateLine, body);
    Assert.DoesNotContain("247.00", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";

  private static Task WaitForCircuitConnectionAsync(IPage page, List<string> diagnostics)
  {
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    page.Console += (_, message) =>
    {
      diagnostics.Add($"console/{message.Type}: {message.Text}");
      if (message.Text.Contains("WebSocket connected to ws://", StringComparison.Ordinal))
        connected.TrySetResult();
    };
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    return connected.Task.WaitAsync(TimeSpan.FromSeconds(15));
  }
}
