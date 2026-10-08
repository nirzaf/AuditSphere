using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAdministrationJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-ADMINISTRATION-E2E")]
  public async Task ReviewedRoleScopeAndRevocation_PersistEvidence_AndInvalidateOpenSessions()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-ADMINISTRATION-E2E");
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var adminOrigin = await host.StartApiForIdentityAsync(host.Fixture.Admin, settings);
    var staffOrigin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var adminContext = await browser.NewContextAsync(); await using var staffContext = await browser.NewContextAsync();
    var page = await adminContext.NewPageAsync(); var staffPage = await staffContext.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fapp");
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    await page.GotoAsync(adminOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Setup progress", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "Users & Access", Exact = true }).Last.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Users & Access", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Search local users", new() { Exact = true }).FillAsync(host.Fixture.Staff.Email);
    await page.GetByRole(AriaRole.Button, new() { Name = "Search local access", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Assign role and scope", Exact = true })).ToHaveCountAsync(1);
    await page.GetByRole(AriaRole.Button, new() { Name = "Assign role and scope", Exact = true }).ClickAsync();
    var dialog = page.GetByRole(AriaRole.Dialog);
    await dialog.GetByLabel("Role", new() { Exact = true }).SelectOptionAsync("Manager");
    await dialog.GetByLabel("Client", new() { Exact = true }).SelectOptionAsync(host.Fixture.ClientId.ToString());
    await dialog.GetByLabel("Reason", new() { Exact = true }).FillAsync("Reviewed client management assignment");
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Review proposed access", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Professional independence impact", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true })).ToBeDisabledAsync();
    await dialog.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposed access", Exact = false }).CheckAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog).ToHaveCountAsync(0);
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    Guid grantId;
    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == host.Fixture.Staff.Id && x.Role == "Manager" && x.RevokedAt == null);
      grantId = grant.Id; Assert.Equal(host.Fixture.ClientId, grant.ClientId); Assert.Null(grant.EngagementId);
      Assert.True(await db.RoleGrantChangeEvidences.AnyAsync(x => x.RoleGrantId == grantId && x.Reason == "Reviewed client management assignment"));
    }
    await page.GetByLabel("Search local users", new() { Exact = true }).FillAsync(host.Fixture.Staff.Email);
    await page.GetByRole(AriaRole.Button, new() { Name = "Search local access", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Revoke Manager for " + host.Fixture.Staff.DisplayName, Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Dialog).GetByText(host.Fixture.Staff.DisplayName, new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToContainTextAsync($"{host.Fixture.Staff.DisplayName} · {host.Fixture.Staff.Email}");
    await Assertions.Expect(page.GetByRole(AriaRole.Dialog).GetByText($"Manager · CLIENT · {host.Fixture.ClientId}", new() { Exact = true })).ToBeVisibleAsync();
    var revoke = dialog.GetByRole(AriaRole.Button, new() { Name = "Revoke reviewed access", Exact = true });
    await Assertions.Expect(revoke).ToBeDisabledAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
      Assert.Null((await db.RoleGrants.SingleAsync(x => x.Id == grantId)).RevokedAt);

    await page.GetByRole(AriaRole.Button, new() { Name = "Revoke Manager for " + host.Fixture.Staff.DisplayName, Exact = true }).ClickAsync();
    dialog = page.GetByRole(AriaRole.Dialog);
    revoke = dialog.GetByRole(AriaRole.Button, new() { Name = "Revoke reviewed access", Exact = true });
    await dialog.GetByLabel("Reason", new() { Exact = true }).FillAsync("Client management assignment ended");
    await Assertions.Expect(revoke).ToBeDisabledAsync();
    await dialog.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this grant and confirm revocation.", Exact = true }).CheckAsync();
    await Assertions.Expect(revoke).ToBeEnabledAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Revoke reviewed access", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
      Assert.NotNull((await db.RoleGrants.SingleAsync(x => x.Id == grantId)).RevokedAt);
    await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration");
    await Assertions.Expect(staffPage.GetByText("Current firm-wide Administrator access is required.", new() { Exact = true }).First).ToBeVisibleAsync();
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading, new() { Name = "Setup progress", Exact = true })).ToHaveCountAsync(0);
    Assert.Empty(errors);
  }
}
