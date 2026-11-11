using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAdministrationJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-ADMINISTRATION-E2E")]
  public async Task ReviewedRoleScopeAndRevocation_PersistEvidence_AndInvalidateOpenSessions()
  {
    AppUser? preparedAdministrator = null, preparedStaff = null;
    var clientId = Guid.Empty;
    await using var host = await OwnedHost.StartSupportedAsync("ANGULAR-ADMINISTRATION-E2E", async database =>
    {
      var scope = await SupportedAuditCompletionWorldBuilder.CreateClientBeforeAcceptanceAsync(database, "Partner");
      clientId = scope.ClientId;
      await using var db = new AuditSphereDbContext(database.Options);
      preparedAdministrator = await (from user in db.Users.AsNoTracking()
        join grant in db.RoleGrants.AsNoTracking() on user.Id equals grant.UserId
        where user.FirmId == scope.FirmId && grant.Role == "Administrator" && grant.RevokedAt == null
        select user).SingleAsync();
      var administratorActor = new ActorContext(preparedAdministrator.Id, scope.FirmId,
        preparedAdministrator.SessionEpoch, ["Administrator"]);
      // Access administration needs a genuine scope, not a fabricated active engagement or portal.
      var state = await EngagementCreationWorkspace.StateAsync(db, scope.Actor, clientId);
      Assert.True(state.Succeeded, state.Message);
      var request = new EngagementCreationRequest(Guid.NewGuid(), state.Value!.ReviewBasis,
        new("FinancialStatementAudit", "Synthetic access-administration scope", "2026-01-01", "2026-12-31"));
      var preview = await EngagementCreationWorkspace.PreviewAsync(db, scope.Actor, clientId, request);
      Assert.True(preview.Succeeded, preview.Message);
      var engagement = await EngagementCreationWorkspace.ExecuteAsync(db, scope.Actor, clientId,
        request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash });
      Assert.True(engagement.Succeeded, engagement.Message);
      var ensured = await RoleAdministrationService.EnsureUserAsync(db, administratorActor,
        new(preparedAdministrator.TenantId, Guid.NewGuid().ToString("D"),
          $"access-staff-{Guid.NewGuid():N}@example.test", "Synthetic access staff", "Staff", "APPROVED_ROSTER"));
      Assert.True(ensured.Succeeded, ensured.Message);
      var granted = await RoleAdministrationService.ApplyRoleGrantAsync(db, administratorActor,
        new(ensured.Value, "Staff", "ENGAGEMENT", clientId, engagement.Value!.EngagementId));
      Assert.True(granted.Succeeded, granted.Message);
      preparedStaff = await db.Users.AsNoTracking().SingleAsync(x => x.Id == ensured.Value && x.FirmId == scope.FirmId);
      return scope.FirmId;
    });
    var administrator = preparedAdministrator ?? throw new InvalidOperationException("Supported setup did not return its administrator.");
    var staff = preparedStaff ?? throw new InvalidOperationException("Supported setup did not return its staff identity.");
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var adminOrigin = await host.StartApiForIdentityAsync(administrator, settings);
    var staffOrigin = await host.StartApiForIdentityAsync(staff, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var adminContext = await browser.NewContextAsync(); await using var staffContext = await browser.NewContextAsync();
    var page = await adminContext.NewPageAsync(); var staffPage = await staffContext.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    await using var adminEvidence = new SyntheticBrowserEvidence(page, host, "administration-admin", administrator.Id);
    await using var staffEvidence = new SyntheticBrowserEvidence(staffPage, host, "administration-staff", staff.Id);
    await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fapp");
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    await page.GotoAsync(adminOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Setup progress", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "Users & Access", Exact = true }).Last.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Users & Access", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Search local users", new() { Exact = true }).FillAsync(staff.Email);
    await page.GetByRole(AriaRole.Button, new() { Name = "Search local access", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Assign role and scope", Exact = true })).ToHaveCountAsync(1);
    await page.GetByRole(AriaRole.Button, new() { Name = "Assign role and scope", Exact = true }).ClickAsync();
    var dialog = page.GetByRole(AriaRole.Dialog);
    await dialog.GetByLabel("Role", new() { Exact = true }).SelectOptionAsync("Manager");
    await dialog.GetByLabel("Client", new() { Exact = true }).SelectOptionAsync(clientId.ToString());
    await dialog.GetByLabel("Reason", new() { Exact = true }).FillAsync("Reviewed client management assignment");
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Review proposed access", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Professional independence impact", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true })).ToBeDisabledAsync();
    await dialog.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposed access", Exact = false }).CheckAsync();
    await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true })).ToBeEnabledAsync();
    await dialog.GetByLabel("Reason", new() { Exact = true }).FillAsync("Reviewed assignment after validating current access");
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Professional independence impact", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposed access", Exact = false })).ToHaveCountAsync(0);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true })).ToHaveCountAsync(0);
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Review proposed access", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Professional independence impact", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true })).ToBeDisabledAsync();
    await dialog.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposed access", Exact = false }).CheckAsync();
    await dialog.GetByLabel("Expires at (local time, optional)", new() { Exact = true }).FillAsync("2027-01-01T12:00");
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Professional independence impact", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposed access", Exact = false })).ToHaveCountAsync(0);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true })).ToHaveCountAsync(0);
    await dialog.GetByLabel("Expires at (local time, optional)", new() { Exact = true }).FillAsync("");
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Review proposed access", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Professional independence impact", Exact = true })).ToBeVisibleAsync();
    await dialog.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposed access", Exact = false }).CheckAsync();
    await dialog.GetByLabel("Role", new() { Exact = true }).SelectOptionAsync("Senior");
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Professional independence impact", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposed access", Exact = false })).ToHaveCountAsync(0);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Assign reviewed access", Exact = true })).ToHaveCountAsync(0);
    await dialog.GetByLabel("Role", new() { Exact = true }).SelectOptionAsync("Manager");
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
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == staff.Id && x.Role == "Manager" && x.RevokedAt == null);
      grantId = grant.Id; Assert.Equal(clientId, grant.ClientId); Assert.Null(grant.EngagementId);
      Assert.True(await db.RoleGrantChangeEvidences.AnyAsync(x => x.RoleGrantId == grantId && x.Reason == "Reviewed assignment after validating current access"));
    }
    await page.GetByLabel("Search local users", new() { Exact = true }).FillAsync(staff.Email);
    await page.GetByRole(AriaRole.Button, new() { Name = "Search local access", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Revoke Manager for " + staff.DisplayName, Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Dialog).GetByText(staff.DisplayName, new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToContainTextAsync($"{staff.DisplayName} · {staff.Email}");
    await Assertions.Expect(page.GetByRole(AriaRole.Dialog).GetByText($"Manager · CLIENT · {clientId}", new() { Exact = true })).ToBeVisibleAsync();
    var revoke = dialog.GetByRole(AriaRole.Button, new() { Name = "Revoke reviewed access", Exact = true });
    await Assertions.Expect(revoke).ToBeDisabledAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
    await Assertions.Expect(dialog).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
      Assert.Null((await db.RoleGrants.SingleAsync(x => x.Id == grantId)).RevokedAt);

    await page.GetByRole(AriaRole.Button, new() { Name = "Revoke Manager for " + staff.DisplayName, Exact = true }).ClickAsync();
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
