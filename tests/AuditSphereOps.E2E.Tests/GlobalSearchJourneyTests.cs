using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// UX-029 staff search in the real shell: scoped hits only, coverage stated, results clear on navigation and
/// Escape, the "/" shortcut never steals a character typed into a form field, and the narrow toggle reflows.
/// </summary>
[Trait("Category", "AuthorizationAndScope")]
public sealed class GlobalSearchJourneyTests
{
  private const string Marker = "ZQXFINDME";

  [Fact]
  [Trait("CaseId", "AS-UI-SEARCH-01")]
  public async Task StaffSearchFindsOnlyOpenableRecordsAndRespectsKeyboardAndContext()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-UI-SEARCH-01");
    var user = PbcSeed.User(host.Fixture.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(user);
      db.RoleGrants.AddRange(PbcSeed.Grant(host.Fixture.FirmId, user, "Staff", host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, user, "Partner", host.Fixture.ClientId));
      await db.SaveChangesAsync();
    }
    var sibling = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, Marker);
    try
    {
      var origin = await host.StartWebForIdentityAsync(user);
      using var playwright = await Playwright.CreateAsync();
      await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
      var page = await context.NewPageAsync();
      var diagnostics = new List<string>();
      page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
      page.Console += (_, message) => diagnostics.Add($"console-{message.Type}: {message.Text}");
      await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/app")}");
      await page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio" }).First.WaitForAsync(new() { Timeout = 15000 });
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

      var search = page.Locator("#global-search-input");
      var results = page.Locator("#global-search-results");

      // "/" outside an editable control focuses search.
      await page.Locator("h1").First.ClickAsync();
      await page.Keyboard.PressAsync("/");
      await Assertions.Expect(search).ToBeFocusedAsync();
      await Assertions.Expect(search).ToHaveValueAsync(string.Empty);

      await search.PressSequentiallyAsync("pbc test");
      await Assertions.Expect(results.GetByRole(AriaRole.Link, new() { Name = "PBC TEST CLIENT" }).First).ToBeVisibleAsync(new() { Timeout = 15000 });
      await Assertions.Expect(results).ToContainTextAsync("Client documents, evidence and emails are not searched.");
      Assert.DoesNotContain(Marker, await results.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

      // A sibling client's exact name yields nothing, not even a count or snippet.
      await search.FillAsync(string.Empty);
      await search.PressSequentiallyAsync(Marker);
      await Assertions.Expect(results).ToContainTextAsync("No records you can open match", new() { Timeout = 15000 });
      Assert.Equal(0, await results.GetByRole(AriaRole.Link).CountAsync());

      // Escape closes; following a hit navigates and clears the panel.
      await search.PressAsync("Escape");
      await Assertions.Expect(results).ToHaveCountAsync(0);
      await search.PressSequentiallyAsync("pbc test");
      await results.GetByRole(AriaRole.Link, new() { Name = "PBC TEST CLIENT" }).First.ClickAsync();
      await page.GetByRole(AriaRole.Heading, new() { Name = "Client profile" }).WaitForAsync();
      await Assertions.Expect(results).ToHaveCountAsync(0);
      await Assertions.Expect(search).ToHaveValueAsync(string.Empty);

      // "/" typed inside a form field stays in the field.
      await page.GotoAsync($"{origin}/app/engagements/{host.Fixture.EngagementId:D}/pbc");
      var field = page.GetByLabel("Files required");
      await field.ClickAsync();
      await page.Keyboard.TypeAsync("a/b");
      await Assertions.Expect(field).ToHaveValueAsync("a/b");
      await Assertions.Expect(search).Not.ToBeFocusedAsync();

      // Narrow screens use a labelled toggle and do not overflow.
      await page.SetViewportSizeAsync(390, 844);
      await page.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')");
      await Assertions.Expect(search).ToBeHiddenAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Open search" }).ClickAsync();
      await Assertions.Expect(search).ToBeVisibleAsync();
      await search.PressSequentiallyAsync("pbc test");
      await Assertions.Expect(results.GetByRole(AriaRole.Link, new() { Name = "PBC TEST CLIENT" }).First).ToBeVisibleAsync(new() { Timeout = 15000 });
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        "Search panel overflows the 390px viewport.");
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
      // Rapid typing must never terminate the circuit (regression: a disposed token source crashed it).
      Assert.DoesNotContain(diagnostics, x => x.Contains("unhandled exception on the current circuit", StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }
  }
}
