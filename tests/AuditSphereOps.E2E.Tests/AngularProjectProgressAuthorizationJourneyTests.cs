using System.Text.Json;
using AuditSphereOps.Domain.Tests;
using Microsoft.AspNetCore.Http;
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
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
          new() { Name = "Project task progress", Exact = true })).ToBeVisibleAsync();
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
}
