using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// A new client identity in the real portal: uploads stay closed until the first sign-in is completed, then the
/// client's primary contact delegates the request to a colleague and revokes it.
/// </summary>
[Trait("Category", "ClientDocuments")]
public sealed class ClientPortalOnboardingJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PORTAL-ONBOARD-01")]
  public async Task FirstSignInOpensUploads_AndThePrimaryContactDelegatesAndRevokes()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PORTAL-ONBOARD-01");
    var f = host.Fixture;
    var primary = PbcSeed.User(f.FirmId, "Client");
    primary.DisplayName = "Primary Contact";
    var colleague = PbcSeed.User(f.FirmId, "Client");
    colleague.DisplayName = "Finance Colleague";
    Guid requestId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(primary, colleague);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, primary, "ClientUser", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, colleague, "ClientUser", f.ClientId, f.EngagementId));
      db.ClientContacts.Add(new ClientContact { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, FullName = primary.DisplayName, Email = primary.Email, Role = "Finance director", Primary = true });
      await db.SaveChangesAsync();
      var staff = PbcSeed.Actor(f.Staff, "Staff");
      requestId = (await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(f.EngagementId, "Fixed asset register",
        "TEST ENTITY", "2026-01-01", "2026-12-31", "Fixed assets", "XLSX", "Register total", primary.Id, f.Staff.Id, f.Reviewer.Id,
        "2027-01-31", "Confidential", "Register reconciles to the ledger."))).Value;
    }
    await using (var db = host.CreateDbContext())
      Assert.True((await PbcService.ChangeStateAsync(db, PbcSeed.Actor(f.Staff, "Staff"), new PbcStateChangeRequest(requestId, PbcStates.Sent, 1))).Succeeded);

    var origin = await host.StartWebForIdentityAsync(primary);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await (await browser.NewContextAsync()).NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    async Task SettleAsync()
    {
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
      await page.WaitForTimeoutAsync(500);
    }

    // The request page withholds uploads until the first sign-in is complete.
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/portal/requests/{requestId:D}")}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "PBC request" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    await Assertions.Expect(page.GetByText("Uploads open after your first portal sign-in is complete.")).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Upload file" })).ToBeDisabledAsync();

    await page.GotoAsync($"{origin}/portal");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Complete your first sign-in" }).WaitForAsync();
    await SettleAsync();
    await page.GetByLabel("I will keep my sign-in private", new() { Exact = false }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Complete first sign-in" }).ClickAsync();
    await Assertions.Expect(page.GetByText("First sign-in complete. Uploads are open for your requests.")).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
      Assert.Equal(ClientIdentityPaths.Unobserved, (await db.ClientPortalFirstSignIns.AsNoTracking().SingleAsync(x => x.UserId == primary.Id)).IdentityPath);

    // Delegation by the primary contact, then revocation.
    await page.GotoAsync($"{origin}/portal/requests/{requestId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Delegate this request" }).WaitForAsync();
    await SettleAsync();
    await Assertions.Expect(page.GetByText("Uploads open after your first portal sign-in is complete.")).ToHaveCountAsync(0);
    await page.Locator("#delegate-user").SelectOptionAsync(new SelectOptionValue { Label = "Finance Colleague" });
    await page.GetByRole(AriaRole.Button, new() { Name = "Delegate", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Delegation recorded.")).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator(".delegation-list")).ToContainTextAsync("Finance Colleague");
    await using (var db = host.CreateDbContext())
      Assert.True(await db.PbcRequestDelegations.AnyAsync(x => x.PbcRequestId == requestId && x.DelegateUserId == colleague.Id && x.RevokedAt == null));
    await page.GetByRole(AriaRole.Button, new() { Name = "Revoke delegation for Finance Colleague" }).ClickAsync();
    await Assertions.Expect(page.GetByText("Delegation revoked.")).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
      Assert.False(await db.PbcRequestDelegations.AnyAsync(x => x.PbcRequestId == requestId && x.RevokedAt == null));

    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }
}
