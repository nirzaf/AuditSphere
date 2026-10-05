using System.Text.RegularExpressions;
using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularRouteAndShellMigrationSweepTests
{
  private static readonly string[] FirmWideRoles =
    ["Administrator", "Partner", "Manager", "Staff", "AccountingPreparer", "AccountingReviewer"];

  private static readonly (string Route, string Heading)[] ParameterlessRoutes =
  [
    ("/", "Portfolio"),
    ("/app", "Portfolio"),
    ("/app/practice/leads", "Practice leads"),
    ("/app/practice/commercial-settings", "Commercial settings"),
    ("/app/practice/resources", "Resource planning"),
    ("/app/practice/analytics", "Practice analytics"),
    ("/app/finance/books", "Firm books"),
    ("/app/library", "Technical library"),
    ("/app/practice/time", "Practice time & task records"),
    ("/app/finance", "Firm ledger & financial operations"),
    ("/app/operations", "Operations"),
    ("/app/administration", "Administration"),
    ("/app/administration/project-progress", "Project task progress"),
    ("/app/administration/microsoft365/tenant-connection", "Microsoft tenant connection"),
    ("/app/accounting", "Accounting workspace"),
    ("/app/accounting/evidence", "Accounting evidence queue"),
    ("/app/accounting/mappings", "COA and accounting mappings"),
    ("/app/accounting/journals", "Adjustment journals"),
    ("/app/accounting/differences", "Audit differences"),
    ("/app/accounting/reviews", "Financial package reviews"),
    ("/app/accounting/rollforward", "Period roll-forward"),
    ("/app/accounting/restatements", "Period restatements"),
    ("/app/accounting/remeasurement", "Currency remeasurement workpapers"),
    ("/app/consolidation", "Group consolidation"),
    ("/app/audit/library", "Audit program library"),
    ("/auth/access-not-assigned", "Access not assigned"),
  ];

  [Fact]
  [Trait("CaseId", "ANGULAR-MIGRATION-ROUTE-RENDER")]
  public async Task ParameterlessRoutesRenderAndPreserveRoleSpecificShells()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-MIGRATION-ROUTE-RENDER");
    var user = await AddFirmWideRouteUserAsync(host);
    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var origin = await host.StartApiForIdentityAsync(user, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var errors = new List<string>();
    staffPage.PageError += (_, error) => errors.Add(error);

    await staffPage.GotoAsync(SignInUrl(origin, "/app"));
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Navigation, new() { Name = "Workspace navigation", Exact = true })).ToHaveCountAsync(1);

    foreach (var (route, heading) in ParameterlessRoutes)
    {
      await staffPage.GotoAsync(origin + route);
      var expectedHeading = staffPage.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true });
      await expectedHeading.WaitForAsync(new() { Timeout = 15000 });
      Assert.Equal(1, await staffPage.Locator("main h1").CountAsync());
      Assert.True(await staffPage.Locator("main h1").IsVisibleAsync(), $"{route} did not show its page heading.");
      var currentLinks = staffPage.Locator("audit-workspace-navigation nav a[aria-current='page']");
      Assert.True(await currentLinks.CountAsync() <= 1, $"{route} marked more than one workspace destination current.");
    }

    // Progress is an implementation tracker, not a readiness percentage; its filter changes
    // the visible subset while the published total remains stable.
    await staffPage.GotoAsync(origin + "/app/administration/project-progress");
    var filters = staffPage.GetByRole(AriaRole.Group, new() { Name = "Filter task cards by status", Exact = true });
    await Assertions.Expect(filters).ToBeVisibleAsync();
    var taskSummary = staffPage.Locator("section[aria-label='Task-card filters'] p");
    var beforeFilter = await taskSummary.InnerTextAsync();
    var beforeTotal = Regex.Match(beforeFilter, @"of (\d+) distinct task cards").Groups[1].Value;
    Assert.False(string.IsNullOrWhiteSpace(beforeTotal), beforeFilter);
    await filters.GetByRole(AriaRole.Button, new() { Name = "Completed", Exact = true }).ClickAsync();
    await Assertions.Expect(filters.GetByRole(AriaRole.Button, new() { Name = "Completed", Exact = true }))
      .ToHaveAttributeAsync("aria-pressed", "true");
    var filteredText = await taskSummary.InnerTextAsync();
    Assert.Contains($"of {beforeTotal} distinct task cards", filteredText);
    await filters.GetByRole(AriaRole.Button, new() { Name = "All", Exact = true }).ClickAsync();

    // A staff identity without firm-wide Administrator authority sees no tracker data.
    var staffOrigin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings);
    await using var restrictedContext = await browser.NewContextAsync();
    var restrictedPage = await restrictedContext.NewPageAsync();
    restrictedPage.PageError += (_, error) => errors.Add(error);
    await restrictedPage.GotoAsync(SignInUrl(staffOrigin, "/app/administration/project-progress"));
    await Assertions.Expect(restrictedPage.GetByRole(AriaRole.Heading, new() { Name = "Project task progress", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(restrictedPage.GetByRole(AriaRole.Alert)).ToContainTextAsync("Only an authorized firm administrator can view the implementation tracker.");
    Assert.Equal(0, await restrictedPage.Locator("audit-task-bar").CountAsync());

    // Client navigation stays separate from the staff workspace.
    var clientOrigin = await host.StartApiForIdentityAsync(host.Fixture.Client, settings);
    await using var clientContext = await browser.NewContextAsync();
    var clientPage = await clientContext.NewPageAsync();
    clientPage.PageError += (_, error) => errors.Add(error);
    await clientPage.GotoAsync(SignInUrl(clientOrigin, "/portal"));
    await Assertions.Expect(clientPage.GetByRole(AriaRole.Heading, new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
    var clientNavigation = clientPage.GetByRole(AriaRole.Navigation, new() { Name = "Workspace navigation", Exact = true });
    await Assertions.Expect(clientNavigation.GetByRole(AriaRole.Link)).ToHaveCountAsync(1);
    await Assertions.Expect(clientNavigation.GetByRole(AriaRole.Link, new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain("Administration", await clientNavigation.InnerTextAsync());
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-MIGRATION-RESPONSIVE-SWEEP")]
  public async Task StaffAndDetailRoutesReflowAcrossTheSixViewportMatrix()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-MIGRATION-RESPONSIVE-SWEEP");
    var sibling = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, "ANGULAR-SWEEP");
    await using (var db = host.CreateDbContext())
    {
      var client = await db.PracticeClients.FindAsync(sibling.Fixture.ClientId);
      client!.LegalName = "Responsive Sweep Holdings International Limited With An Intentionally Long Registered Name";
      await db.SaveChangesAsync();
    }

    try
    {
      var user = await AddFirmWideRouteUserAsync(host);
      var settings = new Dictionary<string, string>
      {
        ["AngularUi__Enabled"] = "true",
        ["AngularUi__CanonicalRoutes"] = "true"
      };
      var origin = await host.StartApiForIdentityAsync(user, settings);
      var engagement = sibling.Fixture.EngagementId;
      var routes = ParameterlessRoutes
        .Where(x => x.Route is not "/" and not "/auth/access-not-assigned")
        .Concat(new (string Route, string Heading)[]
        {
          ($"/app/clients/{sibling.Fixture.ClientId:D}", "Client profile"),
          ($"/app/engagements/{engagement:D}", "Engagement details"),
          ($"/app/engagements/{engagement:D}/pbc", "Prepared-by-client requests"),
          ($"/app/engagements/{engagement:D}/audit-plan", "Audit plan & strategy"),
          ($"/app/engagements/{engagement:D}/audit-fieldwork", "Controlled audit fieldwork"),
          ($"/app/engagements/{engagement:D}/completion", "Engagement completion checklist"),
          ($"/app/accounting/periods/{sibling.PeriodId:D}", "Accounting period"),
          ($"/app/accounting/packages/{sibling.PackageId:D}", "Financial statement package"),
          ($"/app/accounting/mappings/{sibling.MappingId:D}", "Accounting mapping"),
          ($"/app/accounting/journals/{sibling.JournalId:D}", "Adjustment journal")
        })
        .ToArray();

      using var playwright = await Playwright.CreateAsync();
      await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
      var page = await context.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);
      await page.GotoAsync(SignInUrl(origin, "/app"));
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();

      var widths = new[] { 320, 390, 760, 1024, 1440, 1920 };
      foreach (var (route, heading) in routes)
      {
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync(origin + route);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }))
          .ToBeVisibleAsync(new() { Timeout = 15000 });
        Assert.True(await page.Locator("audit-workspace-navigation nav a[aria-current='page']").CountAsync() <= 1,
          $"{route} marked more than one workspace destination current.");
        foreach (var width in widths)
        {
          await page.SetViewportSizeAsync(width, 900);
          var overflow = await page.EvaluateAsync<string>("""
            () => {
            if (document.documentElement.scrollWidth <= window.innerWidth + 1) return '';
            return [...document.querySelectorAll('body *')]
              .filter((element) => {
                const rect = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return rect.width > 0 && rect.right > window.innerWidth + 1 &&
                  style.position !== 'fixed' && !element.closest('.table-scroll');
              })
              .slice(0, 12)
              .map((element) => {
                const rect = element.getBoundingClientRect();
                const ancestry = [];
                for (let parent = element; parent && ancestry.length < 5; parent = parent.parentElement) {
                  ancestry.push(`${parent.tagName.toLowerCase()}${parent.id ? `#${parent.id}` : ''}.${typeof parent.className === 'string' ? parent.className.trim().replaceAll(' ', '.') : ''}`);
                }
                return `${element.tagName.toLowerCase()}${element.id ? `#${element.id}` : ''}` +
                  `${typeof element.className === 'string' && element.className ? `.${element.className.trim().replaceAll(' ', '.')}` : ''}` +
                  ` text=${(element.textContent ?? '').trim().slice(0, 80)} left=${Math.round(rect.left)} right=${Math.round(rect.right)}` +
                  ` scrollWidth=${element.scrollWidth} ancestry=${ancestry.join('>')}`;
              }).join('; ');
            }
            """);
          Assert.True(string.IsNullOrEmpty(overflow), $"{route} overflows the {width}px viewport: {overflow}");
        }
      }

      Assert.Empty(errors);
    }
    finally
    {
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }
  }

  private static async Task<AuditSphereOps.Domain.Security.AppUser> AddFirmWideRouteUserAsync(OwnedHost host)
  {
    var user = PbcSeed.User(host.Fixture.FirmId, "Staff");
    await using var db = host.CreateDbContext();
    db.Users.Add(user);
    foreach (var role in FirmWideRoles)
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, user, role));
    await db.SaveChangesAsync();
    return user;
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";
}
