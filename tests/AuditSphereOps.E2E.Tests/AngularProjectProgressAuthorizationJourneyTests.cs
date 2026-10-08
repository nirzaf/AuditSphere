using System.Text.Json;
using AuditSphereOps.Api.Services;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularProjectProgressAuthorizationJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-PROJECT-PROGRESS-ADMIN-BOUNDARY-01")]
  public async Task OnlyFirmWideAdministratorCanReadThePublishedTracker()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-PROJECT-PROGRESS-ADMIN-BOUNDARY-01");
    var firmId = host.Fixture.FirmId;
    var identities = new List<(string Role, AuditSphereOps.Domain.Security.AppUser User, bool Allowed)>();
    foreach (var role in new[] { "Administrator", "Partner", "Manager", "Staff", "AccountingPreparer", "AccountingReviewer" })
      identities.Add((role, PbcSeed.User(firmId, "Staff"), role == "Administrator"));
    identities.Add(("ClientUser", PbcSeed.User(firmId, "Client"), false));

    await using (var db = host.CreateDbContext())
    {
      foreach (var (role, user, _) in identities)
      {
        db.Users.Add(user);
        db.RoleGrants.Add(PbcSeed.Grant(firmId, user, role,
          role == "ClientUser" ? host.Fixture.ClientId : null));
      }
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var pageErrors = new List<string>();

    foreach (var (role, user, allowed) in identities)
    {
      var origin = await host.StartApiForIdentityAsync(user, settings);
      await using var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      page.PageError += (_, error) => pageErrors.Add(error);
      await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/app/administration/project-progress")}");
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

      var responseJson = await page.EvaluateAsync<string>("""
        async () => {
          const response = await fetch('/api/ui/administration/project-progress', { credentials: 'same-origin' });
          return JSON.stringify({ status: response.status, body: await response.text() });
        }
        """);
      using var response = JsonDocument.Parse(responseJson);
      var status = response.RootElement.GetProperty("status").GetInt32();
      var body = response.RootElement.GetProperty("body").GetString() ?? "";

      if (allowed)
      {
        Assert.Equal("Administrator", role);
        Assert.Equal(StatusCodes.Status200OK, status);
        using var payload = JsonDocument.Parse(body);
        Assert.True(payload.RootElement.GetProperty("snapshot").GetProperty("tasks").GetArrayLength() > 0);
        Assert.True(DateTimeOffset.TryParse(payload.RootElement.GetProperty("snapshot").GetProperty("publishedAtUtc").GetString(), out var publishedAt));
        Assert.Equal(TimeSpan.Zero, publishedAt.Offset);
        Assert.Equal(ProjectProgressReader.FreshnessWindowDays,
          payload.RootElement.GetProperty("snapshot").GetProperty("freshnessWindowDays").GetInt32());
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
          new() { Name = "Project task progress", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Task-card totals", Exact = true }))
          .ToContainTextAsync($"stale after {ProjectProgressReader.FreshnessWindowDays} days.");
        await Assertions.Expect(page.Locator("audit-task-bar").First).ToBeVisibleAsync();
      }
      else
      {
        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.DoesNotContain("snapshot", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tasks", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("T001", body, StringComparison.Ordinal);
        if (role != "ClientUser")
        {
          await Assertions.Expect(page.GetByRole(AriaRole.Heading,
            new() { Name = "Project task progress", Exact = true })).ToBeVisibleAsync();
          await Assertions.Expect(page.GetByRole(AriaRole.Alert))
            .ToContainTextAsync("Only an authorized firm administrator can view the implementation tracker.");
        }
        else
        {
          await Assertions.Expect(page.GetByRole(AriaRole.Heading,
            new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
        }
        Assert.Equal(0, await page.Locator("audit-task-bar").CountAsync());
      }
    }

    Assert.Empty(pageErrors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-PROJECT-PROGRESS-EXPIRED-ADMIN-01")]
  public async Task ExpiredAdministratorIsRevokedOnceBeforeAnySnapshotIsReturned()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-PROJECT-PROGRESS-EXPIRED-ADMIN-01");
    var user = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var grant = PbcSeed.AdminGrant(user.FirmId, user);
    var now = DateTimeOffset.UtcNow;
    grant.GrantedAt = now.AddMinutes(-2);
    grant.ExpiresAt = now.AddMinutes(-1);
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(user);
      db.RoleGrants.Add(grant);
      await db.SaveChangesAsync();
    }
    var origin = await host.StartApiForIdentityAsync(user, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fadministration%2Fproject-progress");
    foreach (var response in await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => ReadTrackerAsync(page))))
    {
      Assert.Equal(StatusCodes.Status401Unauthorized, response.Status);
      AssertNoSnapshot(response.Body);
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("audit-task-bar")).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
    {
      Assert.NotNull((await db.RoleGrants.AsNoTracking().SingleAsync(x => x.Id == grant.Id)).RevokedAt);
      Assert.Equal(user.SessionEpoch + 1, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).SessionEpoch);
      var evidence = Assert.Single(await db.RoleGrantChangeEvidences.AsNoTracking()
        .Where(x => x.RoleGrantId == grant.Id).ToListAsync());
      Assert.Equal("EXPIRY", evidence.Source);
      Assert.Equal("REVOKED", evidence.Action);
      Assert.Equal(Guid.Empty, evidence.ActorUserId);
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-PROJECT-PROGRESS-REVOKED-ADMIN-01")]
  public async Task AdministratorRevocationClearsOpenTrackerAndFreshSignInCannotRestoreIt()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-PROJECT-PROGRESS-REVOKED-ADMIN-01");
    var user = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var grant = PbcSeed.AdminGrant(user.FirmId, user);
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(user);
      db.RoleGrants.AddRange(grant, PbcSeed.Grant(user.FirmId, user, "Staff"));
      await db.SaveChangesAsync();
    }
    var origin = await host.StartApiForIdentityAsync(user, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fadministration%2Fproject-progress");
    await Assertions.Expect(page.Locator("audit-task-bar").First).ToBeVisibleAsync();
    Assert.Equal(StatusCodes.Status200OK, (await ReadTrackerAsync(page)).Status);
    await using (var db = host.CreateDbContext())
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"),
        new RevokeRoleGrantRequest(grant.Id, Reason: "Synthetic tracker revocation check"));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    var stale = await ReadTrackerAsync(page);
    Assert.Equal(StatusCodes.Status401Unauthorized, stale.Status);
    AssertNoSnapshot(stale.Body);
    var unavailable = page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true });
    if (!await unavailable.IsVisibleAsync())
    {
      try { await page.GetByRole(AriaRole.Button, new() { Name = "Refresh task progress", Exact = true }).ClickAsync(new() { Timeout = 5000 }); }
      catch (PlaywrightException) { Assert.True(await unavailable.IsVisibleAsync()); }
    }
    await Assertions.Expect(unavailable).ToBeVisibleAsync(new() { Timeout = 15000 });
    await Assertions.Expect(page.Locator("audit-task-bar")).ToHaveCountAsync(0);
    Assert.DoesNotContain("T001", await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fadministration%2Fproject-progress");
    var fresh = await ReadTrackerAsync(page);
    Assert.Equal(StatusCodes.Status403Forbidden, fresh.Status);
    AssertNoSnapshot(fresh.Body);
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Only an authorized firm administrator can view the implementation tracker.");
    await Assertions.Expect(page.Locator("audit-task-bar")).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(user.SessionEpoch + 1, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).SessionEpoch);
      var evidence = Assert.Single(await db.RoleGrantChangeEvidences.AsNoTracking()
        .Where(x => x.RoleGrantId == grant.Id).ToListAsync());
      Assert.Equal("ADMIN_ACTION", evidence.Source);
      Assert.Equal(host.Fixture.Admin.Id, evidence.ActorUserId);
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-PROJECT-PROGRESS-FOREIGN-GRANT-01")]
  public async Task AdministratorGrantInAnotherFirmCannotAuthorizeThePersistedIdentity()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-PROJECT-PROGRESS-FOREIGN-GRANT-01");
    var user = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var foreignGrant = PbcSeed.AdminGrant(Guid.NewGuid(), user);
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(user);
      // Deliberately seed a misbound grant: the persisted user's firm, not the role
      // name or a foreign grant for the same user ID, must determine authority.
      db.RoleGrants.AddRange(PbcSeed.Grant(user.FirmId, user, "Staff"), foreignGrant);
      await db.SaveChangesAsync();
    }
    var origin = await host.StartApiForIdentityAsync(user, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fadministration%2Fproject-progress");
    var denied = await ReadTrackerAsync(page);
    Assert.Equal(StatusCodes.Status403Forbidden, denied.Status);
    AssertNoSnapshot(denied.Body);
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Only an authorized firm administrator can view the implementation tracker.");
    await Assertions.Expect(page.Locator("audit-task-bar")).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
    {
      var persistedUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
      Assert.Equal(host.Fixture.FirmId, persistedUser.FirmId);
      Assert.Equal(user.SessionEpoch, persistedUser.SessionEpoch);
      Assert.Null((await db.RoleGrants.AsNoTracking().SingleAsync(x => x.Id == foreignGrant.Id)).RevokedAt);
      Assert.False(await db.RoleGrantChangeEvidences.AnyAsync(x => x.TargetUserId == user.Id));
    }
    Assert.Empty(errors);
  }

  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true"
  };

  private static async Task<(int Status, string Body)> ReadTrackerAsync(IPage page)
  {
    var json = await page.EvaluateAsync<string>("""
      async () => {
        const response = await fetch('/api/ui/administration/project-progress', { credentials: 'same-origin' });
        return JSON.stringify({ status: response.status, body: await response.text() });
      }
      """);
    using var result = JsonDocument.Parse(json);
    return (result.RootElement.GetProperty("status").GetInt32(), result.RootElement.GetProperty("body").GetString() ?? "");
  }

  private static void AssertNoSnapshot(string body)
  {
    Assert.DoesNotContain("snapshot", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("tasks", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("T001", body, StringComparison.Ordinal);
  }
}
