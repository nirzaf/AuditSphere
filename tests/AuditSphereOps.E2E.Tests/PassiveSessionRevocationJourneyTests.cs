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
    var origin = client ? host.ClientUrl : await host.StartApiForIdentityAsync(identity,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    var route = client ? "/portal" : "/app/administration";
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await (await browser.NewContextAsync()).NewPageAsync();
    var protectedHeading = client
      ? page.GetByRole(AriaRole.Heading, new() { Name = "Client portal", Exact = true })
      : page.Locator("main h1");
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await protectedHeading.WaitForAsync();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    var draftKey = $"auditsphere-tab-draft-v1:{identity.FirmId:D}:{identity.Id:D}:{identity.SessionEpoch}:synthetic-revoked";
    await page.EvaluateAsync("key => { sessionStorage.setItem(key, 'synthetic protected draft'); localStorage.setItem('unrelated-preference', 'preserve'); }", draftKey);
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
    if (client)
      await Assertions.Expect(protectedHeading).ToHaveCountAsync(0);
    else
      await Assertions.Expect(protectedHeading).ToHaveTextAsync("Access unavailable");
    await Assertions.Expect(page.Locator(".workspace")).ToHaveCountAsync(1);
    await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
    await page.WaitForFunctionAsync($"() => sessionStorage.getItem('{draftKey}') === null");
    Assert.Equal("preserve", await page.EvaluateAsync<string>("localStorage.getItem('unrelated-preference')"));
  }
}
