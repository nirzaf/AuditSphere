using AuditSphereOps.Domain.Engagements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>Requires the Angular production build; see the Angular development guide before running.</summary>
public sealed class AngularPortfolioJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "ANGULAR-PORTFOLIO-E2E-01")]
  public async Task DirectLink_Scope_Revocation_AndBlazorRollback(bool canonical)
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-PORTFOLIO-E2E-01");
    var prefix=canonical?"":"/ui";
    var settings=new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()};
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings);
    await using (var db = host.CreateDbContext())
    {
      db.Engagements.Add(new Engagement { Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
    }
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name = "Portfolio", Exact = true})).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
    await Assertions.Expect(page.Locator("tbody td.number")).ToHaveTextAsync("1");
    await page.ReloadAsync();
    await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await page.GetByRole(AriaRole.Textbox, new() {Name = "Search client name or ID"}).FillAsync("NO MATCH");
    await page.Locator("audit-portfolio").GetByRole(AriaRole.Button, new() {Name = "Search", Exact = true}).ClickAsync();
    await Assertions.Expect(page.GetByText("No clients match this search in your current scope.")).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Textbox, new() {Name = "Search client name or ID"}).FillAsync("");
    await page.Locator("audit-portfolio").GetByRole(AriaRole.Button, new() {Name = "Search", Exact = true}).ClickAsync();
    await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
    await using (var db = host.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(u => u.Id == host.Fixture.Staff.Id);
      user.SessionEpoch++;
      await db.SaveChangesAsync();
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name = "Access unavailable"})).ToBeVisibleAsync(new() {Timeout = 15000});
    Assert.DoesNotContain("PBC TEST CLIENT", await page.Locator("body").InnerTextAsync());
    await page.GotoAsync(host.StaffUrl + "/auth/sign-in?returnUrl=%2Fapp");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name = "Portfolio", Exact = true})).ToBeVisibleAsync();
    using (var http = new HttpClient())
    {
      Assert.DoesNotContain("<app-root", await http.GetStringAsync(origin + "/health/live"));
      using var api = await http.GetAsync(origin + "/api/ui/session");
      Assert.Equal(System.Net.HttpStatusCode.Unauthorized, api.StatusCode);
      Assert.DoesNotContain("<app-root", await api.Content.ReadAsStringAsync());
      var disabled = await http.GetAsync(host.StaffUrl + "/ui/app");
      Assert.DoesNotContain("<app-root", await disabled.Content.ReadAsStringAsync());
    }
    var clientOrigin = await host.StartApiForIdentityAsync(host.Fixture.Client, settings);
    await using var restricted = await browser.NewContextAsync();
    var clientPage = await restricted.NewPageAsync();
    await clientPage.GotoAsync(clientOrigin + "/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app"));
    await Assertions.Expect(clientPage.GetByRole(AriaRole.Heading, new() {Name = "Client portal", Exact = true})).ToBeVisibleAsync();
    Assert.DoesNotContain("Portfolio", await clientPage.Locator("aside").InnerTextAsync());
    await Assertions.Expect(clientPage.Locator("audit-portfolio")).ToHaveCountAsync(0);
    await using var logoutContext = await browser.NewContextAsync();
    var logoutPage = await logoutContext.NewPageAsync();
    await logoutPage.GotoAsync(origin + "/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app"));
    await Assertions.Expect(logoutPage.GetByRole(AriaRole.Heading, new() {Name = "Portfolio", Exact = true})).ToBeVisibleAsync();
    await logoutPage.GetByRole(AriaRole.Button, new() {Name = "Sign out", Exact = true}).ClickAsync();
    await Assertions.Expect(logoutPage.GetByRole(AriaRole.Heading, new() {Name = "Access unavailable"})).ToBeVisibleAsync();
    Assert.Equal(401, (await logoutContext.APIRequest.GetAsync(origin + "/api/ui/session")).Status);
    Assert.Empty(errors);
  }
}
