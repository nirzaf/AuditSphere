using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.Json;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// UX-029 staff search in the real shell: scoped hits only, coverage stated, results clear on navigation and
/// Escape, the "/" shortcut never steals a character typed into a form field, and the Angular shell reflows.
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
    const string libraryTerm = "ZQXTECHLIBRARY";
    const string libraryCode = "ZQX-ALL-STAFF";
    const string libraryTitle = "ZQXTECHLIBRARY approved guidance";
    Guid libraryDocumentId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(user);
      db.RoleGrants.AddRange(PbcSeed.Grant(host.Fixture.FirmId, user, "Staff", host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, user, "Partner", host.Fixture.ClientId));
      await db.SaveChangesAsync();

      var curator = PbcSeed.User(host.Fixture.FirmId, "Staff");
      var publisher = PbcSeed.User(host.Fixture.FirmId, "Staff");
      db.Users.AddRange(curator, publisher);
      db.RoleGrants.AddRange(PbcSeed.Grant(host.Fixture.FirmId, curator, "Manager"),
        PbcSeed.Grant(host.Fixture.FirmId, publisher, "Partner"));
      await db.SaveChangesAsync();

      var created = await TechnicalLibraryService.CreateAsync(db, PbcSeed.Actor(curator, "Manager"),
        libraryCode, libraryTitle, TechnicalLibraryCategories.Isa, TechnicalLibraryAudiences.AllStaff,
        $"{libraryTerm} published staff guidance.", "Approved test source", new DateOnly(2026, 1, 1));
      Assert.True(created.Succeeded, created.Message);
      libraryDocumentId = created.Value;
      var draftVersionId = await db.TechnicalLibraryVersions.Where(x => x.DocumentId == libraryDocumentId)
        .Select(x => x.Id).SingleAsync();
      var published = await TechnicalLibraryService.PublishAsync(db, PbcSeed.Actor(publisher, "Partner"), draftVersionId);
      Assert.True(published.Succeeded, published.Message);
    }
    var sibling = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, Marker);
    try
    {
      var origin = await host.StartApiForIdentityAsync(user);
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

      var search = page.GetByRole(AriaRole.Combobox, new() { Name = "Search your workspace" });
      var results = page.GetByRole(AriaRole.Region, new() { Name = "Global search" });

      // "/" outside an editable control focuses search.
      await page.Locator("h1").First.ClickAsync();
      await page.Keyboard.PressAsync("/");
      await Assertions.Expect(search).ToBeFocusedAsync();
      await Assertions.Expect(search).ToHaveValueAsync(string.Empty);

      await search.FillAsync("pbc test");
      await Assertions.Expect(results.GetByRole(AriaRole.Link, new() { Name = "PBC TEST CLIENT" }).First).ToBeVisibleAsync(new() { Timeout = 15000 });
      await Assertions.Expect(results).ToContainTextAsync("Documents and emails are not searched.");
      Assert.DoesNotContain(Marker, await results.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

      // A published library result is rendered as a real Angular route the scoped Staff user can open.
      await search.FillAsync(libraryTerm);
      var libraryLink = results.GetByRole(AriaRole.Link,
        new() { Name = $"{libraryCode} — {libraryTitle}", Exact = true });
      await Assertions.Expect(libraryLink).ToBeVisibleAsync(new() { Timeout = 15000 });
      await libraryLink.ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading,
        new() { Name = $"{libraryCode} — {libraryTitle}", Exact = true })).ToBeVisibleAsync();
      Assert.Equal($"/app/library/{libraryDocumentId:D}", new Uri(page.Url).AbsolutePath);
      await Assertions.Expect(search).ToHaveValueAsync(string.Empty);
      await Assertions.Expect(results.GetByRole(AriaRole.Status)).ToHaveCountAsync(0);

      // Supported page hits also resolve to a current Angular-owned destination.
      await search.FillAsync("practice time");
      var pageLink = results.GetByRole(AriaRole.Link,
        new() { Name = "Practice time & tasks", Exact = true });
      await Assertions.Expect(pageLink).ToBeVisibleAsync(new() { Timeout = 15000 });
      await pageLink.ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading,
        new() { Name = "Practice time & task records", Exact = true })).ToBeVisibleAsync();
      Assert.Equal("/app/practice/time", new Uri(page.Url).AbsolutePath);
      await Assertions.Expect(search).ToHaveValueAsync(string.Empty);
      await Assertions.Expect(results.GetByRole(AriaRole.Status)).ToHaveCountAsync(0);

      // A sibling client's exact name yields nothing, not even a count or snippet.
      await search.FillAsync(Marker);
      await Assertions.Expect(results.GetByRole(AriaRole.Status)).ToContainTextAsync("0 results", new() { Timeout = 15000 });
      Assert.Equal(0, await results.GetByRole(AriaRole.Link).CountAsync());

      // Escape closes; following a hit navigates and clears the panel.
      await search.PressAsync("Escape");
      await Assertions.Expect(results.GetByRole(AriaRole.Status)).ToHaveCountAsync(0);
      await search.FillAsync("pbc test");
      await results.GetByRole(AriaRole.Link, new() { Name = "PBC TEST CLIENT" }).First.ClickAsync();
      await page.GetByRole(AriaRole.Heading, new() { Name = "Client profile" }).WaitForAsync();
      await Assertions.Expect(results.GetByRole(AriaRole.Status)).ToHaveCountAsync(0);
      await Assertions.Expect(search).ToHaveValueAsync(string.Empty);

      // "/" typed inside a form field stays in the field.
      await page.GotoAsync($"{origin}/app/engagements/{host.Fixture.EngagementId:D}/pbc");
      var field = page.GetByLabel("Files required");
      await field.ClickAsync();
      await page.Keyboard.TypeAsync("a/b");
      await Assertions.Expect(field).ToHaveValueAsync("a/b");
      await Assertions.Expect(search).Not.ToBeFocusedAsync();

      // Narrow screens keep search available, expose navigation, and do not overflow.
      await page.SetViewportSizeAsync(390, 844);
      await Assertions.Expect(search).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Open navigation" })).ToBeVisibleAsync();
      await search.FillAsync("pbc test");
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

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-GLOBAL-SEARCH-API-01")]
  public async Task SearchApiEnforcesFirmGrantAndIdentityBoundariesWithoutLeakingCounts()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-GLOBAL-SEARCH-API-01");
    var f = host.Fixture;
    var staff = PbcSeed.User(f.FirmId, "Staff");
    var foreignFirmId = Guid.NewGuid();
    var foreignClientId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(staff);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, staff, "Staff", f.ClientId),
        PbcSeed.Grant(f.FirmId, staff, "Partner", f.ClientId));
      db.FirmSafetyStates.Add(new FirmSafetyState { Id = foreignFirmId });
      db.PracticeClients.Add(new PracticeClient
      {
        Id = foreignClientId, FirmId = foreignFirmId, LegalName = "ZQXFOREIGNSEARCH PRIVATE CLIENT",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = foreignClientId, FirmId = foreignFirmId });
      await db.SaveChangesAsync();
    }

    var sibling = await SiblingClientSeed.SeedAsync(host.Database, f.FirmId, Marker);
    try
    {
      var origin = await host.StartApiForIdentityAsync(staff);
      using var playwright = await Playwright.CreateAsync();
      await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      await using (var context = await browser.NewContextAsync())
      {
        var page = await context.NewPageAsync();
        await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app"));
        await page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio" })
          .First.WaitForAsync(new() { Timeout = 15000 });

        await using var own = await context.APIRequest.GetAsync(origin + "/api/ui/search?term=pbc%20test");
        Assert.Equal(200, own.Status);
        using (var body = JsonDocument.Parse(await own.TextAsync()))
        {
          var hits = body.RootElement.GetProperty("hits").EnumerateArray().ToArray();
          Assert.Contains(hits, hit => hit.GetProperty("href").GetString() == $"/app/clients/{f.ClientId:D}");
        }

        await using (var shortTerm = await context.APIRequest.GetAsync(origin + "/api/ui/search?term=x"))
        {
          Assert.Equal(200, shortTerm.Status);
          using var body = JsonDocument.Parse(await shortTerm.TextAsync());
          Assert.Empty(body.RootElement.GetProperty("hits").EnumerateArray());
          Assert.False(body.RootElement.GetProperty("truncated").GetBoolean());
        }
        await using (var tooLong = await context.APIRequest.GetAsync(origin + "/api/ui/search?term=" +
          Uri.EscapeDataString(new string('x', 101))))
        {
          Assert.Equal(400, tooLong.Status);
          using var body = JsonDocument.Parse(await tooLong.TextAsync());
          Assert.Equal("request.invalid", body.RootElement.GetProperty("code").GetString());
        }

        await AssertNoHitsAsync(context, origin, Marker);
        await AssertNoHitsAsync(context, origin, "ZQXFOREIGNSEARCH");

        await using (var db = host.CreateDbContext())
          await db.RoleGrants.Where(g => g.UserId == staff.Id && g.RevokedAt == null)
            .ExecuteUpdateAsync(g => g.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        await AssertNoHitsAsync(context, origin, "pbc test");

        await using (var db = host.CreateDbContext())
          await db.Users.Where(u => u.Id == staff.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
        await using var stale = await context.APIRequest.GetAsync(origin + "/api/ui/search?term=pbc%20test");
        Assert.Equal(401, stale.Status);
      }

      var clientOrigin = await host.StartApiForIdentityAsync(f.Client);
      await using (var clientContext = await browser.NewContextAsync())
      {
        var clientPage = await clientContext.NewPageAsync();
        await clientPage.GotoAsync(clientOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/portal"));
        await clientPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await using var denied = await clientContext.APIRequest.GetAsync(clientOrigin + "/api/ui/search?term=pbc%20test");
        Assert.Equal(403, denied.Status);
        Assert.DoesNotContain("PBC TEST CLIENT", await denied.TextAsync(), StringComparison.Ordinal);
      }
    }
    finally
    {
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }
  }

  private static async Task AssertNoHitsAsync(IBrowserContext context, string origin, string term)
  {
    await using var response = await context.APIRequest.GetAsync(
      origin + "/api/ui/search?term=" + Uri.EscapeDataString(term));
    Assert.Equal(200, response.Status);
    using var body = JsonDocument.Parse(await response.TextAsync());
    Assert.Empty(body.RootElement.GetProperty("hits").EnumerateArray());
    Assert.False(body.RootElement.GetProperty("truncated").GetBoolean());
  }
}
