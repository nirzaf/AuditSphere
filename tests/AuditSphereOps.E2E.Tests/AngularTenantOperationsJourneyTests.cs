using System.Text.Json;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>Native Angular dialogs against the standalone API and separately gated simulated Microsoft providers.</summary>
public sealed class AngularTenantOperationsJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-TENANT-PROVISIONING-E2E")]
  public async Task ReviewedProvisioning_OneTimePassword_GuestScope_AndUnknownRecovery()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-TENANT-PROVISIONING-E2E", startLegacyBlazorHosts: false);
    var seeded = await TenantAdministrationJourneyTests.SeedAsync(host, verified: true);
    var settings = TenantAdministrationJourneyTests.Simulation(seeded); settings["AngularUi__Enabled"] = "true";
    var origin = await host.StartApiForIdentityAsync(seeded.Admin, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fusers");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Users & Access", Exact = true })).ToBeVisibleAsync();

    async Task<ILocator> Review(string email, bool invite)
    {
      await page.GetByRole(AriaRole.Button, new() { Name = invite ? "Invite new guest" : "Create Microsoft 365 user", Exact = true }).ClickAsync();
      var dialog = page.GetByRole(AriaRole.Dialog);
      if (!invite) await dialog.GetByLabel("Display name", new() { Exact = true }).FillAsync("Angular workforce joiner");
      await dialog.GetByLabel(invite ? "Approved guest email" : "User principal name", new() { Exact = true }).FillAsync(email);
      await dialog.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
      if (!invite) await dialog.GetByLabel("Mail nickname", new() { Exact = true }).FillAsync("angularjoiner");
      await dialog.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
      await dialog.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
      if (invite)
      {
        Assert.Equal(0, await dialog.Locator("#new-directory-scope option[value=FIRM_WIDE]").CountAsync());
        await dialog.GetByLabel("Scope type", new() { Exact = true }).SelectOptionAsync("ENGAGEMENT");
      }
      await dialog.GetByLabel("Client", new() { Exact = true }).SelectOptionAsync(host.Fixture.ClientId.ToString());
      if (invite) await dialog.GetByLabel("Engagement", new() { Exact = true }).SelectOptionAsync(host.Fixture.EngagementId.ToString());
      await dialog.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
      await dialog.GetByLabel("Reason", new() { Exact = true }).FillAsync("Reviewed Angular client team assignment");
      var create = dialog.GetByRole(AriaRole.Button, new() { Name = invite ? "Send reviewed Microsoft invitation" : "Create reviewed Microsoft user", Exact = true });
      await Assertions.Expect(create).ToBeDisabledAsync();
      await dialog.GetByLabel("I reviewed the identity, tenant, local role, scope and reason.", new() { Exact = true }).CheckAsync();
      if (!invite) await dialog.GetByLabel("I accept the one-time initial password flow and will handle it through the approved secure process.", new() { Exact = true }).CheckAsync();
      await create.ClickAsync();
      await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Microsoft operation result", Exact = true })).ToBeVisibleAsync();
      return dialog;
    }

    var dialog = await Review("angular.joiner@example.test", invite: false);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "One-time initial password", Exact = true })).ToBeVisibleAsync();
    var secret = dialog.Locator("#temporary-directory-password");
    Assert.Equal("password", await secret.GetAttributeAsync("type"));
    var password = await secret.InputValueAsync();
    Assert.True(password.Length >= 16);
    await dialog.GetByRole(AriaRole.Button, new() { Name = "I have handled the password; clear it", Exact = true }).ClickAsync();
    await Assertions.Expect(secret).ToHaveCountAsync(0);
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
    await using (var db = host.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(x => x.Email == "angular.joiner@example.test");
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == user.Id);
      Assert.Equal("Staff", grant.Role); Assert.Equal(host.Fixture.ClientId, grant.ClientId); Assert.Null(grant.EngagementId);
      Assert.DoesNotContain(password, JsonSerializer.Serialize(new object[] { await db.Microsoft365ExternalOperations.ToListAsync(), await db.Microsoft365AdministrationEvents.ToListAsync(), await db.Users.ToListAsync() }));
    }
    dialog = await Review("unknown.angular@example.test", invite: false);
    await Assertions.Expect(dialog).ToContainTextAsync("Microsoft did not confirm the result");
    Assert.Equal(0, await dialog.Locator("#temporary-directory-password").CountAsync());
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
    var history = page.Locator("audit-user-access details").Filter(new() { HasText = "unknown.angular@example.test" });
    await history.Locator("summary").ClickAsync();
    await history.GetByRole(AriaRole.Button, new() { Name = "Review operation recovery", Exact = true }).ClickAsync();
    dialog = page.GetByRole(AriaRole.Dialog);
    var recover = dialog.GetByRole(AriaRole.Button, new() { Name = "Reconcile reviewed operation", Exact = true });
    await Assertions.Expect(recover).ToBeDisabledAsync();
    await dialog.GetByLabel("I reviewed this persisted intent and want to reconcile or complete its local binding.", new() { Exact = true }).CheckAsync();
    await recover.ClickAsync();
    await Assertions.Expect(dialog).ToContainTextAsync("Reconciled by immutable Microsoft identity");
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
    await using (var db = host.CreateDbContext())
    {
      var op = await db.Microsoft365ExternalOperations.SingleAsync(x => x.TargetDescriptor == "unknown.angular@example.test");
      Assert.Equal(ExternalOperationStates.Bound, op.State); Assert.Equal(1, op.AttemptCount);
      Assert.Single(await db.Users.Where(x => x.Email == "unknown.angular@example.test").ToListAsync());
    }
    dialog = await Review("angular.finance@client.test", invite: true);
    await Assertions.Expect(dialog).ToContainTextAsync("Guest invited and bound");
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
    await using (var db = host.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(x => x.Email == "angular.finance@client.test");
      Assert.Equal("Client", user.UserKind);
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == user.Id);
      Assert.Equal("ClientUser", grant.Role); Assert.Equal(host.Fixture.ClientId, grant.ClientId); Assert.Equal(host.Fixture.EngagementId, grant.EngagementId);
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-TENANT-GROUPS-E2E")]
  public async Task GroupAllowlist_ReviewedAddRemove_AndRetirement_PreserveLocalAuthority()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-TENANT-GROUPS-E2E", startLegacyBlazorHosts: false);
    var seeded = await TenantAdministrationJourneyTests.SeedAsync(host, verified: true);
    var settings = TenantAdministrationJourneyTests.Simulation(seeded); settings["AngularUi__Enabled"] = "true";
    var origin = await host.StartApiForIdentityAsync(seeded.Admin, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fusers");
    await page.GetByRole(AriaRole.Button, new() { Name = "Manage approved Microsoft groups", Exact = true }).ClickAsync();
    var dialog = page.GetByRole(AriaRole.Dialog);
    Assert.Contains("system-ui", await dialog.EvaluateAsync<string>("element => getComputedStyle(element).fontFamily"));
    await dialog.GetByText("Approve a managed group", new() { Exact = true }).ClickAsync();
    await dialog.GetByLabel("Group object ID", new() { Exact = true }).FillAsync(seeded.GroupObjectId);
    await dialog.GetByLabel("Purpose", new() { Exact = true }).FillAsync("Reviewed client collaboration");
    await dialog.GetByLabel("Approval reason", new() { Exact = true }).FillAsync("Approved Angular team group");
    await dialog.GetByLabel("I reviewed this exact group, collaboration access, purpose and reason.", new() { Exact = true }).CheckAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Approve reviewed managed group", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog).ToContainTextAsync("Group allowlist approval recorded.");
    Guid group;
    await using (var db = host.CreateDbContext()) group = (await db.ManagedDirectoryGroups.SingleAsync()).Id;
    await dialog.GetByLabel("Managed group", new() { Exact = true }).SelectOptionAsync(group.ToString());
    await dialog.GetByLabel("Local Microsoft-bound user", new() { Exact = true }).SelectOptionAsync(seeded.Admin.Id.ToString());
    async Task Change(string action, string observed)
    {
      await dialog.GetByLabel("Membership action", new() { Exact = true }).SelectOptionAsync(action);
      await dialog.GetByLabel("Change reason", new() { Exact = true }).FillAsync("Reviewed Angular collaboration membership");
      await dialog.GetByRole(AriaRole.Button, new() { Name = "Review Microsoft membership", Exact = true }).ClickAsync();
      await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Review membership change", Exact = true })).ToBeVisibleAsync();
      Assert.Contains(observed, await dialog.InnerTextAsync());
      var save = dialog.GetByRole(AriaRole.Button, new() { Name = "Apply reviewed membership change", Exact = true });
      await Assertions.Expect(save).ToBeDisabledAsync();
      await dialog.GetByLabel("I reviewed the exact group, user, existing membership and requested collaboration access.", new() { Exact = true }).CheckAsync();
      await save.ClickAsync();
      await Assertions.Expect(dialog).ToContainTextAsync(action == "ADD" ? "Microsoft added the member." : "Microsoft removed the member.");
    }
    await Change("ADD", "Not a member");
    await dialog.GetByRole(AriaRole.Button, new() { Name = "View members", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog.Locator("ul")).ToContainTextAsync(seeded.Admin.DisplayName);
    await Change("REMOVE", "Member");
    await dialog.GetByRole(AriaRole.Button, new() { Name = "View members", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog).ToContainTextAsync("No members in this page.");
    await dialog.GetByLabel("I reviewed the selected group and reason and want to retire its allowlist entry.", new() { Exact = true }).CheckAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Retire reviewed allowlist entry", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog).ToContainTextAsync("Allowlist entry retired; Microsoft memberships were preserved.");
    await using (var db = host.CreateDbContext())
    {
      Assert.NotNull((await db.ManagedDirectoryGroups.SingleAsync()).RetiredAt);
      Assert.Equal(2, await db.Microsoft365ExternalOperations.CountAsync(x => x.ManagedGroupId == group));
      Assert.Single(await db.RoleGrants.Where(x => x.UserId == seeded.Admin.Id).ToListAsync());
    }
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
    Assert.Empty(errors);
  }
}
