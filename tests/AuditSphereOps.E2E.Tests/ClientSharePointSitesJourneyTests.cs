using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>Administration status uses persisted site/member evidence; no live Microsoft effects in normal CI.</summary>
[Trait("Category", "Microsoft365Onboarding")]
public sealed class ClientSharePointSitesJourneyTests
{
  [Fact]
  public async Task SeparateSiteAndMembershipHealth_FullControlWarning_AndStaleSessionDenial()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "CLIENT-SITE-STATUS");
    var f = host.Fixture;
    var tenant = Guid.NewGuid().ToString("D");
    var connectionId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision { Id = connectionId, FirmId = f.FirmId,
        TenantId = tenant, LoginClientIdReference = "slot:login", RuntimeCredentialReference = "slot:selected-site",
        State = Microsoft365RevisionStates.Active, ConsentState = "VERIFIED", CreatedAt = DateTimeOffset.UtcNow });
      db.ClientSharePointSites.Add(new ClientSharePointSite { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        ConnectionRevisionId = connectionId, RequestedByUserId = f.Admin.Id, TenantId = tenant,
        RequestedUrl = "https://example.sharepoint.com/sites/client-" + f.ClientId.ToString("N"), Title = "Synthetic client site",
        OwnershipMarker = $"AuditSphere:{f.FirmId:D}:{f.ClientId:D}", State = "READY", SiteId = "site-test", DriveId = "drive-test", RootItemId = "root-test",
        StaffGroupId = "42", MembershipState = "PARTIAL", Reason = "Synthetic owner-approved test policy", CreatedAt = DateTimeOffset.UtcNow,
        VerifiedAt = DateTimeOffset.UtcNow, LastMembershipSyncAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await (await browser.NewContextAsync()).NewPageAsync();
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fmicrosoft365%2Ftenant-connection");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Microsoft tenant connection", Exact = true }).WaitForAsync();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    var panel = page.Locator("audit-workspace-administration");
    await Assertions.Expect(panel.GetByRole(AriaRole.Heading, new() { Name = "Dedicated client SharePoint sites", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(panel).ToContainTextAsync("Full Control over the entire client site");
    var sites = panel.GetByRole(AriaRole.Table, new() { Name = "Client-site and membership verification", Exact = true });
    await Assertions.Expect(sites.GetByText("partial", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(sites.GetByRole(AriaRole.Link, new() { Name = "Open verified site", Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var admin = await db.Users.SingleAsync(x => x.Id == f.Admin.Id); admin.SessionEpoch++;
      await db.SaveChangesAsync();
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true }))
      .ToBeVisibleAsync(new() { Timeout = 15000 });
    await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Open verified site", Exact = true })).ToHaveCountAsync(0);
  }
}
