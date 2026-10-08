using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularAdvancedConsolidationAuthorizationJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-ADV-CONSOLIDATION-READ-01")]
  public async Task ClientIdentityWithErroneousGroupGrantCannotReadAdvancedConsolidation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-ADV-CONSOLIDATION-READ-01");
    var f = host.Fixture;
    var groupId = Guid.NewGuid();
    var scopeId = Guid.NewGuid();
    const string privateGroupName = "SYN-PAR-002-PRIVATE-ADVANCED-GROUP";
    const string privateMethod = AdvancedConsolidationMethods.AcquisitionNci;

    await using (var db = host.CreateDbContext())
    {
      db.ClientGroups.Add(new ClientGroup
      {
        Id = groupId, FirmId = f.FirmId, Code = "SYN-PAR-002-PRIVATE",
        Name = privateGroupName, CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
      {
        Id = scopeId, FirmId = f.FirmId, GroupId = groupId, PeriodId = Guid.NewGuid(),
        Method = privateMethod, ReportingCurrency = "QAR", OpeningBasis = "OPENING-2026",
        CreatedByUserId = f.Admin.Id
      });
      db.GroupAccessGrants.AddRange(
        GroupGrant(f.FirmId, groupId, f.Client.Id, "AccountingPreparer", f.Admin.Id),
        GroupGrant(f.FirmId, groupId, f.Staff.Id, "AccountingPreparer", f.Admin.Id));
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };

    var clientOrigin = await host.StartApiForIdentityAsync(f.Client, settings);
    await using (var clientContext = await browser.NewContextAsync())
    {
      var clientPage = await clientContext.NewPageAsync();
      var errors = new List<string>();
      clientPage.PageError += (_, error) => errors.Add(error);
      var route = $"/app/consolidation/advanced/{scopeId:D}";
      await clientPage.GotoAsync(clientOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
      await Assertions.Expect(clientPage.GetByRole(AriaRole.Heading,
        new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
      Assert.EndsWith("/portal", new Uri(clientPage.Url).AbsolutePath, StringComparison.Ordinal);
      var portalText = await clientPage.Locator("main").InnerTextAsync();
      Assert.DoesNotContain(privateGroupName, portalText, StringComparison.Ordinal);
      Assert.DoesNotContain(scopeId.ToString("D"), portalText, StringComparison.OrdinalIgnoreCase);

      var apiResult = await clientPage.EvaluateAsync<string>("""
        async path => {
          const response = await fetch(path, { credentials: 'same-origin' });
          return `${response.status}|${await response.text()}`;
        }
        """, $"/api/ui/consolidation/advanced/{scopeId:D}");
      var separator = apiResult.IndexOf('|');
      Assert.InRange(int.Parse(apiResult[..separator]), 400, 499);
      var apiBody = apiResult[(separator + 1)..];
      Assert.DoesNotContain(privateGroupName, apiBody, StringComparison.Ordinal);
      Assert.DoesNotContain(scopeId.ToString("D"), apiBody, StringComparison.OrdinalIgnoreCase);
      Assert.Empty(errors);
    }

    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, settings);
    await using (var staffContext = await browser.NewContextAsync())
    {
      var staffPage = await staffContext.NewPageAsync();
      await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" +
        Uri.EscapeDataString($"/app/consolidation/advanced/{scopeId:D}"));
      await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading,
        new() { Name = privateGroupName + " · v1", Exact = true })).ToBeVisibleAsync();
      var staffText = await staffPage.Locator("main").InnerTextAsync();
      Assert.Contains(privateMethod, staffText, StringComparison.Ordinal);
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-ADV-CONSOLIDATION-STALE-01")]
  public async Task AdvancedConsolidationClearsPriorGroupWhenRouteChangesInPlace()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-ADV-CONSOLIDATION-STALE-01");
    var f = host.Fixture;
    var ownGroupId = Guid.NewGuid();
    var ownScopeId = Guid.NewGuid();
    var siblingGroupId = Guid.NewGuid();
    var siblingScopeId = Guid.NewGuid();
    const string ownGroupName = "SYN-PAR-002-ADV-GROUP-PRIVATE";
    const string siblingGroupName = "SYN-PAR-002-ADV-GROUP-UNAUTHORIZED";
    var now = DateTimeOffset.UtcNow;

    await using (var db = host.CreateDbContext())
    {
      db.ClientGroups.AddRange(
        new ClientGroup
        {
          Id = ownGroupId, FirmId = f.FirmId, Code = "SYN-PAR-002-ADV-A", Name = ownGroupName,
          CreatedByUserId = f.Admin.Id, CreatedAt = now
        },
        new ClientGroup
        {
          Id = siblingGroupId, FirmId = f.FirmId, Code = "SYN-PAR-002-ADV-B", Name = siblingGroupName,
          CreatedByUserId = f.Admin.Id, CreatedAt = now
        });
      db.ConsolidationScopeVersions.AddRange(
        Scope(f.FirmId, ownScopeId, ownGroupId, f.Admin.Id),
        Scope(f.FirmId, siblingScopeId, siblingGroupId, f.Admin.Id));
      db.GroupAccessGrants.Add(GroupGrant(f.FirmId, ownGroupId, f.Staff.Id,
        "AccountingPreparer", f.Admin.Id, now));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/app/consolidation/advanced/{ownScopeId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = ownGroupName + " · v1", Exact = true })).ToBeVisibleAsync();
    Assert.Contains(AdvancedConsolidationMethods.AcquisitionNci, await page.Locator("main").InnerTextAsync(),
      StringComparison.Ordinal);

    const string draft = "{\"sources\":[],\"reviewedJournals\":[],\"note\":\"synthetic draft\"}";
    var manifest = page.GetByLabel("Source manifest JSON *", new() { Exact = true });
    await manifest.FillAsync(draft);
    await page.WaitForFunctionAsync("() => Array.from({ length: localStorage.length }, (_, i) => localStorage.getItem(localStorage.key(i))).some(item => item?.includes('synthetic draft'))");

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__advancedScopeRouteToken = token", documentToken);
    await NavigateInPlaceAsync(page, $"/app/consolidation/advanced/{siblingScopeId:D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync(
      "The requested consolidation scope is not available in the current firm and group grant.");
    var deniedText = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(ownGroupName, deniedText, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingGroupName, deniedText, StringComparison.Ordinal);
    Assert.DoesNotContain(ownScopeId.ToString("D"), deniedText, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__advancedScopeRouteToken"));

    await NavigateInPlaceAsync(page, $"/app/consolidation/advanced/{ownScopeId:D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = ownGroupName + " · v1", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__advancedScopeRouteToken"));
    for (var widthIndex = 0; widthIndex < 6; widthIndex++)
    {
      var width = new[] { 320, 390, 760, 1024, 1440, 1920 }[widthIndex];
      await page.SetViewportSizeAsync(width, 900);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
        $"Advanced consolidation overflows at {width}px.");
      Assert.True(await page.EvaluateAsync<bool>("() => [...document.querySelectorAll('.table-scroll')].every(element => { const rect = element.getBoundingClientRect(); return rect.left >= -1 && rect.right <= innerWidth + 1 && getComputedStyle(element).overflowX === 'auto'; })"),
        $"An advanced consolidation table escaped its scroll container at {width}px.");
    }
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = ownGroupName + " · v1", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByLabel("Source manifest JSON *", new() { Exact = true })).ToHaveValueAsync(draft);
    var submit = page.GetByRole(AriaRole.Button, new() { Name = "Submit schedule", Exact = true });
    await page.GetByLabel("Source manifest JSON *", new() { Exact = true }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    await page.Keyboard.PressAsync("Tab");
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(submit).ToHaveCSSAsync("outline-style", "solid");
    Assert.Empty(errors);
  }

  private static ConsolidationScopeVersion Scope(Guid firmId, Guid scopeId, Guid groupId, Guid actorId) => new()
  {
    Id = scopeId, FirmId = firmId, GroupId = groupId, PeriodId = Guid.NewGuid(),
    Method = AdvancedConsolidationMethods.AcquisitionNci, ReportingCurrency = "QAR",
    OpeningBasis = "OPENING-2026", CreatedByUserId = actorId
  };

  private static GroupAccessGrant GroupGrant(Guid firmId, Guid groupId, Guid userId,
    string role, Guid grantedBy, DateTimeOffset? grantedAt = null) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, GroupId = groupId, UserId = userId, Role = role,
    GrantedAt = grantedAt ?? DateTimeOffset.UtcNow, GrantedByUserId = grantedBy
  };

  private static async Task NavigateInPlaceAsync(IPage page, string route)
  {
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }", route);
    await page.WaitForFunctionAsync("path => location.pathname === path", route);
  }
}
