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
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "CLIENT-SITE-STATUS");
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
    var origin = await host.StartWebForIdentityAsync(f.Admin);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await (await browser.NewContextAsync()).NewPageAsync();
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fadministration");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Setup progress" }).WaitForAsync();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    var sharePoint = page.GetByRole(AriaRole.Tab, new() { Name = "SharePoint", Exact = true });
    for (var attempt = 0; attempt < 6 && !await sharePoint.IsVisibleAsync(); attempt++)
    {
      await page.GetByRole(AriaRole.Tab, new() { Name = "Microsoft 365", Exact = true }).ClickAsync();
      await page.WaitForTimeoutAsync(350);
    }
    await sharePoint.ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Client SharePoint sites", Exact = true }).WaitForAsync();
    await Assertions.Expect(page.GetByText("Assigned staff receive Full Control", new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Open client site" })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "SharePoint documents and client sites" }).GetByText("PARTIAL", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var admin = await db.Users.SingleAsync(x => x.Id == f.Admin.Id); admin.SessionEpoch++;
      await db.SaveChangesAsync();
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh site status" }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Open client site" })).ToHaveCountAsync(0);
  }
}
