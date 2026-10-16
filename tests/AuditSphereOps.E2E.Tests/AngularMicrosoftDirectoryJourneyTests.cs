using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularMicrosoftDirectoryJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "ANGULAR-MICROSOFT-DIRECTORY")]
  public async Task ExactBinding_AcknowledgementOrDroppedResponse_DoesNotGrantRoles(bool dropResponse)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-MICROSOFT-DIRECTORY");
    var seeded = await TenantAdministrationSeed.SeedAsync(host, verified: true);
    var settings = TenantAdministrationSeed.Simulation(seeded); settings["AngularUi__Enabled"] = "true";
    var origin = await host.StartApiForIdentityAsync(seeded.Admin, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    var writes = 0;
    await page.RouteAsync("**/api/ui/administration/directory/bind", async route => {
      Interlocked.Increment(ref writes);
      var response = await route.FetchAsync(); Assert.Equal(200, response.Status);
      if (dropResponse) await route.AbortAsync(); else await route.FulfillAsync(new() { Response = response });
    });
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fusers");
    var directory = page.Locator("audit-microsoft-directory");
    await Assertions.Expect(directory.GetByRole(AriaRole.Button, new() { Name = "Browse active identities", Exact = true })).ToBeEnabledAsync();
    if (!dropResponse) await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/app/administration/users", "microsoft-directory");
    await directory.GetByLabel("Display name or user principal name prefix", new() { Exact = true }).FillAsync("Directory Journey Member");
    await directory.GetByRole(AriaRole.Button, new() { Name = "Search Microsoft directory", Exact = true }).ClickAsync();
    await directory.GetByRole(AriaRole.Button, new() { Name = "Review Directory Journey Member", Exact = true }).ClickAsync();
    var execute = directory.GetByRole(AriaRole.Button, new() { Name = "Verify and bind selected identity", Exact = true });
    await Assertions.Expect(execute).ToBeDisabledAsync();
    await directory.GetByLabel("I reviewed this exact Microsoft identity and account type.", new() { Exact = true }).CheckAsync();
    await execute.ClickAsync();
    if (dropResponse)
    {
      await Assertions.Expect(directory.GetByRole(AriaRole.Heading, new() { Name = "Binding outcome is not confirmed", Exact = true })).ToBeVisibleAsync();
      await page.ReloadAsync();
      await Assertions.Expect(directory.GetByRole(AriaRole.Button, new() { Name = "Inspect current local binding", Exact = true })).ToBeEnabledAsync();
      await directory.GetByRole(AriaRole.Button, new() { Name = "Inspect current local binding", Exact = true }).ClickAsync();
      await Assertions.Expect(directory.GetByText("Current local binding: Present", new() { Exact = false })).ToBeVisibleAsync();
      await Assertions.Expect(directory.GetByText("The binding remains blocked", new() { Exact = false })).ToBeVisibleAsync();
      await Assertions.Expect(directory.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed role and scope", Exact = true })).ToHaveCountAsync(0);
    }
    else
    {
      await Assertions.Expect(directory.GetByText("Identity verified and bound locally. No new AuditSphere role has been granted.", new() { Exact = true })).ToBeVisibleAsync();
      // The local roster refresh is automatic; there is deliberately no browser reload here.
      await Assertions.Expect(page.Locator("audit-user-access").GetByRole(AriaRole.Heading, new() { Name = "Directory Journey Member", Exact = true })).ToBeVisibleAsync();
    }
    Assert.Equal(1, writes);
    await using var db = host.CreateDbContext();
    var user = await db.Users.SingleAsync(x => x.TenantId == seeded.TenantId && x.Subject == seeded.MemberObjectId);
    Assert.False(await db.RoleGrants.AnyAsync(x => x.UserId == user.Id)); Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-MICROSOFT-DIRECTORY")]
  public async Task ProtectedPaging_RetriesExactCursor_AndDenialClearsSelection()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-MICROSOFT-DIRECTORY-PAGING");
    var seeded = await TenantAdministrationSeed.SeedAsync(host, verified: true);
    var settings = TenantAdministrationSeed.Simulation(seeded); settings["AngularUi__Enabled"] = "true";
    for (var i = 2; i < 29; i++)
    {
      settings[$"TenantAdministration__Simulation__Users__{i}__ObjectId"] = Guid.NewGuid().ToString("D");
      settings[$"TenantAdministration__Simulation__Users__{i}__DisplayName"] = $"Paging Member {i:D2}";
      settings[$"TenantAdministration__Simulation__Users__{i}__UserPrincipalName"] = $"paging{i:D2}@example.test";
    }
    var origin = await host.StartApiForIdentityAsync(seeded.Admin, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); string? failedBody = null; var deny = false; var failNext = true; var writes = 0;
    await page.RouteAsync("**/api/ui/administration/directory/bind", async route => { Interlocked.Increment(ref writes); await route.ContinueAsync(); });
    await page.RouteAsync("**/api/ui/administration/directory/search", async route => {
      var body = route.Request.PostData!; using var json = JsonDocument.Parse(body);
      if (deny) { await route.FulfillAsync(new() { Status = 403, ContentType = "application/json", Body = "{\"message\":\"Denied\"}" }); return; }
      if (json.RootElement.GetProperty("pageToken").ValueKind == JsonValueKind.String)
      {
        if (failNext) { failNext = false; failedBody = body; await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Unavailable\"}" }); return; }
        Assert.Equal(failedBody, body);
      }
      await route.ContinueAsync();
    });
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fusers");
    var directory = page.Locator("audit-microsoft-directory");
    await directory.GetByLabel("Display name or user principal name prefix", new() { Exact = true }).FillAsync("Paging Member");
    await directory.GetByRole(AriaRole.Button, new() { Name = "Search Microsoft directory", Exact = true }).ClickAsync();
    await Assertions.Expect(directory.Locator("tbody tr")).ToHaveCountAsync(25);
    await directory.GetByRole(AriaRole.Button, new() { Name = "Next 25 identities", Exact = true }).ClickAsync();
    await Assertions.Expect(directory.GetByRole(AriaRole.Button, new() { Name = "Retry directory page", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(directory.Locator("tbody tr")).ToHaveCountAsync(25);
    await directory.GetByRole(AriaRole.Button, new() { Name = "Retry directory page", Exact = true }).ClickAsync();
    await Assertions.Expect(directory.Locator("tbody tr")).ToHaveCountAsync(2);
    await directory.GetByRole(AriaRole.Button, new() { Name = "Review Paging Member", Exact = false }).First.ClickAsync();
    await directory.GetByLabel("I reviewed this exact Microsoft identity and account type.", new() { Exact = true }).CheckAsync();
    deny = true;
    await directory.GetByRole(AriaRole.Button, new() { Name = "Search Microsoft directory", Exact = true }).ClickAsync();
    await Assertions.Expect(directory.Locator("tbody tr")).ToHaveCountAsync(0);
    await Assertions.Expect(directory.GetByRole(AriaRole.Button, new() { Name = "Verify and bind selected identity", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(directory.GetByRole(AriaRole.Button, new() { Name = "Retry directory page", Exact = true })).ToHaveCountAsync(0);
    Assert.NotNull(failedBody); Assert.Equal(0, writes);
  }
}
