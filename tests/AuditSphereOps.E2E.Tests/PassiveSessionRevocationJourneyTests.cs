using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class PassiveSessionRevocationJourneyTests
{
  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task OpenShellClearsContentWithoutUserAction(bool client, bool disable)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: $"PASSIVE-REVOCATION-{(client ? 1 : 0)}-{(disable ? 1 : 0)}");
    var identity = client ? host.Fixture.Client : host.Fixture.Admin;
    var origin = client ? host.ClientUrl : await host.StartWebForIdentityAsync(identity);
    var route = client ? "/portal" : "/app/administration";
    var heading = client ? "Client portal" : "Firm administration & security";
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await (await browser.NewContextAsync()).NewPageAsync();
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).WaitForAsync();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    await page.EvaluateAsync("""
      () => {
        localStorage.setItem('auditsphere:draft:v1:synthetic-revoked', 'synthetic protected draft');
        sessionStorage.setItem('auditsphere:draft:v1:synthetic-revoked', 'synthetic protected draft');
        localStorage.setItem('unrelated-preference', 'preserve');
      }
      """);
    await using (var db = host.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(x => x.Id == identity.Id);
      if (disable) user.Disabled = true;
      else user.SessionEpoch++;
      await db.SaveChangesAsync();
    }
    // Deliberately no refresh, click, navigation or server command after revocation.
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = heading, Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.Locator(".audit-shell")).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
    await page.WaitForFunctionAsync("""
      () => localStorage.getItem('auditsphere:draft:v1:synthetic-revoked') === null &&
            sessionStorage.getItem('auditsphere:draft:v1:synthetic-revoked') === null
      """);
    Assert.Equal("preserve", await page.EvaluateAsync<string>("localStorage.getItem('unrelated-preference')"));
  }
}
