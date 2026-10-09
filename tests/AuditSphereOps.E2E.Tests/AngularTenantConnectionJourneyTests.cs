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
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-TENANT-CONNECTION");
    var seeded = await TenantAdministrationSeed.SeedAsync(host, verified: false);
    var settings = TenantAdministrationSeed.Simulation(seeded);
    settings["AngularUi__Enabled"] = "true";
    var origin = await host.StartApiForIdentityAsync(seeded.Admin, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fmicrosoft365%2Ftenant-connection");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Microsoft tenant connection", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Configured tenant", Exact = true })).ToBeVisibleAsync();
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 844);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
        $"Tenant connection overflows the {width}px viewport.");
    }
    await page.SetViewportSizeAsync(390, 844);
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Connect Microsoft 365 tenant", Exact = true })).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the configured tenant", Exact = false }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Connect Microsoft 365 tenant", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Verified consenting identity", Exact = true })).ToBeVisibleAsync();
    Assert.Contains("/app/administration/microsoft365/tenant-connection", page.Url);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 844);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
        $"Verified tenant connection overflows the {width}px viewport.");
    }
    await page.SetViewportSizeAsync(390, 844);
    await using (var db = host.CreateDbContext())
    {
      var attempt = await db.TenantConsentAttempts.SingleAsync(x => x.State == TenantConsentAttemptStates.ConsentVerified);
      Assert.Equal(seeded.TenantId, attempt.ConsentingTenantId); Assert.Equal(seeded.Admin.Subject, attempt.ConsentingObjectId);
      Assert.Equal(64, attempt.NonceHash!.Length);
      Assert.Equal(4, await db.TenantCapabilityVerifications.CountAsync(x => x.State == CapabilityVerificationStates.Verified &&
        x.Capability != Microsoft365Capabilities.SelectedSite));
    }
    await page.Locator("audit-tenant-connection").GetByRole(AriaRole.Link, new() { Name = "Administration overview", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Administration", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Setup progress", Exact = true })).ToBeVisibleAsync();
    var overview = await page.Locator("body").InnerTextAsync();
    Assert.Contains("Tenant connected", overview);
    Assert.Contains("Consent verified", overview);
    Assert.Contains("Directory access", overview);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 844);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
        $"Administration overview overflows the {width}px viewport.");
    }
    await page.SetViewportSizeAsync(1440, 900);
    await page.GetByRole(AriaRole.Navigation, new() { Name = "Administration sections", Exact = true })
      .GetByRole(AriaRole.Link, new() { Name = "Users & Access", Exact = true }).ClickAsync();
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
    await directory.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed role and scope", Exact = true }).ClickAsync();
    var roleDialog = page.GetByRole(AriaRole.Dialog);
    await roleDialog.GetByLabel("Role", new() { Exact = true }).SelectOptionAsync("Staff");
    await roleDialog.GetByLabel("Scope type", new() { Exact = true }).SelectOptionAsync("CLIENT");
    await roleDialog.GetByLabel("Client", new() { Exact = true }).SelectOptionAsync(host.Fixture.ClientId.ToString());
    await roleDialog.GetByLabel("Reason", new() { Exact = true }).FillAsync("Joined the reviewed client team");
    await roleDialog.GetByRole(AriaRole.Button, new() { Name = "Review proposed access", Exact = true }).ClickAsync();
    await Assertions.Expect(roleDialog.GetByRole(AriaRole.Heading, new() { Name = "Professional independence impact", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(roleDialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true })).ToBeDisabledAsync();
    await roleDialog.GetByLabel("I reviewed the proposed access, scope change, expiry and independence impact.", new() { Exact = true }).CheckAsync();
    await roleDialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true }).ClickAsync();
    await Assertions.Expect(roleDialog).ToHaveCountAsync(0);
    Guid boundUserId;
    Guid grantId;
    await using (var db = host.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(x => x.TenantId == seeded.TenantId && x.Subject == seeded.MemberObjectId);
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == user.Id && x.RevokedAt == null);
      Assert.Equal(("Staff", (Guid?)host.Fixture.ClientId), (grant.Role, grant.ClientId));
      Assert.Equal("Joined the reviewed client team", grant.Reason);
      boundUserId = user.Id;
      grantId = grant.Id;
    }
    await page.ReloadAsync();
    var access = page.Locator("audit-user-access");
    await access.GetByLabel("Search local users", new() { Exact = true }).FillAsync("journey.member@example.test");
    await access.GetByRole(AriaRole.Button, new() { Name = "Search local access", Exact = true }).ClickAsync();
    await access.GetByRole(AriaRole.Button, new() { Name = "Revoke Staff for Directory Journey Member", Exact = true }).ClickAsync();
    var revokeDialog = page.GetByRole(AriaRole.Dialog);
    await revokeDialog.GetByLabel("Reason", new() { Exact = true }).FillAsync("Directory assignment ended");
    await revokeDialog.GetByLabel("I reviewed this grant and confirm revocation.", new() { Exact = true }).CheckAsync();
    await revokeDialog.GetByRole(AriaRole.Button, new() { Name = "Revoke reviewed access", Exact = true }).ClickAsync();
    await Assertions.Expect(revokeDialog).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
    {
      Assert.NotNull((await db.RoleGrants.SingleAsync(x => x.Id == grantId && x.UserId == boundUserId)).RevokedAt);
      Assert.Contains(await db.RoleGrantChangeEvidences.ToListAsync(),
        x => x.RoleGrantId == grantId && x.Reason == "Directory assignment ended");
    }
    Assert.Empty(errors);
  }
}
