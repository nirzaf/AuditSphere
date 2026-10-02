using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularTenantConnectionJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-TENANT-CONNECTION")]
  public async Task ConsentAndExactDirectoryBinding_UseStandaloneApi_AndRemainSeparateFromLocalAccess()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-TENANT-CONNECTION");
    var seeded = await TenantAdministrationJourneyTests.SeedAsync(host, verified: false);
    var settings = TenantAdministrationJourneyTests.Simulation(seeded);
    settings["AngularUi__Enabled"] = "true";
    var origin = await host.StartApiForIdentityAsync(seeded.Admin, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fmicrosoft365%2Ftenant-connection");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Microsoft tenant connection", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Connect Microsoft 365 tenant", Exact = true })).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the configured tenant", Exact = false }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Connect Microsoft 365 tenant", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Verified consenting identity", Exact = true })).ToBeVisibleAsync();
    Assert.Contains("/ui/app/administration/microsoft365/tenant-connection", page.Url);
    await using (var db = host.CreateDbContext())
    {
      var attempt = await db.TenantConsentAttempts.SingleAsync(x => x.State == TenantConsentAttemptStates.ConsentVerified);
      Assert.Equal(seeded.TenantId, attempt.ConsentingTenantId); Assert.Equal(seeded.Admin.Subject, attempt.ConsentingObjectId);
      Assert.Equal(64, attempt.NonceHash!.Length);
    }
    await page.Locator("audit-tenant-connection").GetByRole(AriaRole.Link, new() { Name = "Users & Access", Exact = true }).ClickAsync();
    var directory = page.Locator("audit-microsoft-directory");
    await directory.GetByLabel("Display name or user principal name prefix", new() { Exact = true }).FillAsync("Directory Journey");
    await directory.GetByRole(AriaRole.Button, new() { Name = "Search Microsoft directory", Exact = true }).ClickAsync();
    await directory.GetByRole(AriaRole.Button, new() { Name = "Review Directory Journey Member", Exact = true }).ClickAsync();
    await directory.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this exact Microsoft identity", Exact = false }).CheckAsync();
    await directory.GetByRole(AriaRole.Button, new() { Name = "Verify and bind selected identity", Exact = true }).ClickAsync();
    await Assertions.Expect(directory.GetByText("Identity verified and bound locally. No new AuditSphere role has been granted.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(x => x.TenantId == seeded.TenantId && x.Subject == seeded.MemberObjectId);
      Assert.False(await db.RoleGrants.AnyAsync(x => x.UserId == user.Id));
    }
    Assert.Empty(errors);
  }
}
