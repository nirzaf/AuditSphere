using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AuditProgramLibraryScopeJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-AUDIT-LIBRARY-01")]
  public async Task RevokedGrant_ClearsOpenLibraryOnSearchAndRefresh()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-AUDIT-LIBRARY-01");
    Guid grantId;
    await using (var db = host.CreateDbContext())
    {
      var grant = PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner");
      grantId = grant.Id;
      db.RoleGrants.Add(grant);
      await db.SaveChangesAsync();
      var published = await AuditProgramService.PublishAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Partner"),
        new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(published.Succeeded, published.Message);
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.Console += (_, message) =>
    {
      if (message.Type == "error") diagnostics.Add($"console/{message.Type}: {message.Text}");
    };
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    await page.GotoAsync($"{host.StaffUrl}/auth/sign-in?returnUrl=%2Fapp%2Faudit%2Flibrary");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Audit program library" }).WaitForAsync();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await page.GetByRole(AriaRole.Button, new() { Name = "Browse" }).First.ClickAsync();
    await page.GetByText("AWP-01-01").WaitForAsync();
    var openedBody = await page.Locator("body").InnerTextAsync();
    Assert.True(openedBody.Contains("AWP-01-01", StringComparison.Ordinal),
      openedBody + "\n" + string.Join("\n", diagnostics));

    await using (var db = host.CreateDbContext())
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"),
        new RevokeRoleGrantRequest(grantId, Reason: "Library access review completed"));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    var invalidatedSession = page.WaitForResponseAsync(response =>
      response.Url.EndsWith("/api/ui/session", StringComparison.Ordinal) && response.Status == 401,
      new() { Timeout = 15000 });
    await page.EvaluateAsync("() => window.dispatchEvent(new FocusEvent('focus'))");
    await invalidatedSession;
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true }).WaitForAsync();
    Assert.Equal(0, await page.GetByText("AWP-01-01").CountAsync());

    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true }).WaitForAsync();
    Assert.Equal(0, await page.GetByText("AWP-01-01").CountAsync());
  }
}
