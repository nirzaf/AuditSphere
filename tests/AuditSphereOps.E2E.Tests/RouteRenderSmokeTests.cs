using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Domain.Security;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// Route-render smoke: every parameterless inventoried route renders its page heading
/// through the MudBlazor shell with no unhandled page error. Detail routes carrying
/// route parameters need seeded entities and stay covered by their dedicated journeys;
/// /setup/microsoft365 is covered by the M365 setup journey (bootstrap-claim state).
/// </summary>
public sealed class RouteRenderSmokeTests
{
  private static readonly IReadOnlyList<(string Route, string Heading)> StaffRoutes =
  [
    ("/", "Controlled work, visible evidence."),
    ("/app", "Portfolio"),
    ("/app/practice/leads", "Practice leads"),
    ("/app/practice/time", "Practice time & task records"),
    ("/app/finance", "Firm ledger & financial operations"),
    ("/app/operations", "Operations"),
    ("/app/administration", "Firm administration & security"),
    ("/app/administration/project-progress", "Project task progress"),
    ("/app/administration/microsoft365/tenant-connection", "Microsoft 365 tenant connection"),
    ("/app/accounting", "Client accounting workspace"),
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
  [Trait("CaseId", "AS-UI-ROUTE-RENDER-SMOKE-01")]
  public async Task EveryParameterlessRouteRendersItsHeadingWithoutPageErrors()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-UI-ROUTE-RENDER-SMOKE-01");
    var smokeUser = PbcSeed.User(host.Fixture.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(smokeUser);
      // One firm-wide grant per internal role family the smoke routes check; this is a
      // read-only render pass and every command still enforces its own authorization.
      foreach (var role in new[] { "Administrator", "Partner", "Manager", "Staff", "AccountingPreparer", "AccountingReviewer" })
        db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, smokeUser, role));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(smokeUser);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");

    foreach (var (route, heading) in StaffRoutes)
    {
      await page.GotoAsync(SignInUrl(origin, route));
      await page.GetByRole(AriaRole.Heading, new() { Name = heading }).First.WaitForAsync(new() { Timeout = 15000 });
      if (route is "/app/accounting/mappings" or "/app/accounting/journals" or
          "/app/accounting/differences" or "/app/accounting/reviews")
      {
        var queueSlug = route.Split('/').Last();
        foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
        {
          await page.SetViewportSizeAsync(width, 900);
          if (width < 960)
            await page.WaitForFunctionAsync("() => getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px'");
          else if (width == 1440)
            await page.WaitForFunctionAsync("() => document.querySelector('.audit-sidebar')?.getBoundingClientRect().left >= -1");
          Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
            $"Accounting {queueSlug} queue overflows the {width}px viewport.");
          if ((width == 390 || width == 1440) && Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } queueCaptureDir)
          {
            Directory.CreateDirectory(queueCaptureDir);
            await page.ScreenshotAsync(new() { Path = Path.Combine(queueCaptureDir, $"accounting-{queueSlug}-{width}.png"), FullPage = true });
          }
        }
      }
      if (route == "/app/accounting/mappings")
      {
        var queueLink = page.Locator("nav[aria-label='Accounting record queues'] a[href='/app/accounting/journals']");
        await queueLink.FocusAsync();
        Assert.Equal("solid", await queueLink.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
      }
      if (route == "/app/administration/project-progress")
      {
        await page.GetByRole(AriaRole.Heading, new() { Name = "Module 20 — Accounting setup" }).WaitForAsync();
        await Assertions.Expect(page.Locator(".audit-module-progress-grid [role='progressbar']")).ToHaveCountAsync(7);
        await Assertions.Expect(page.GetByRole(AriaRole.Progressbar, new() { Name = "Overall task-card progress" }))
          .ToHaveAttributeAsync("aria-valuetext", new System.Text.RegularExpressions.Regex(@"\d+ completed, \d+ active or in review, \d+ pending, \d+ blocked out of \d+ task cards"));
        await page.GetByRole(AriaRole.Heading, new() { Name = "Other application modules" }).WaitForAsync();
        await Assertions.Expect(page.Locator(".audit-task-progress-untracked")).ToHaveCountAsync(6);
        Assert.Equal(0, await page.Locator(".audit-task-progress-untracked[value]").CountAsync());
        await page.SetViewportSizeAsync(320, 900);
        await page.Locator(".audit-module-progress-grid").First.WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Module 20 — Accounting setup" }).WaitForAsync();
        await page.WaitForFunctionAsync("() => getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px'");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
        if (Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } progressCaptureDir)
        {
          Directory.CreateDirectory(progressCaptureDir);
          await page.ScreenshotAsync(new() { Path = Path.Combine(progressCaptureDir, "project-progress-320.png"), FullPage = true });
        }
        await page.SetViewportSizeAsync(1280, 900);
        await page.Locator(".audit-module-progress-grid").First.WaitForAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.audit-sidebar')?.getBoundingClientRect().left >= -1");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
        if (Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } desktopProgressCaptureDir)
          await page.ScreenshotAsync(new() { Path = Path.Combine(desktopProgressCaptureDir, "project-progress-1280.png"), FullPage = true });
      }
      if (route == "/")
        Assert.Equal(0, await page.GetByRole(AriaRole.Navigation, new() { Name = "Primary navigation" }).CountAsync());
      else if (route == "/app")
        Assert.Equal(1, await page.GetByRole(AriaRole.Navigation, new() { Name = "Primary navigation" }).CountAsync());
    }

    await page.GotoAsync(SignInUrl(origin, "/app"));
    await page.GetByRole(AriaRole.Link, new() { Name = "Client portal" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Client portal" }).First.WaitForAsync();
    Assert.Equal(0, await page.GetByRole(AriaRole.Navigation, new() { Name = "Primary navigation" }).CountAsync());

    var scopedStaffPage = await context.NewPageAsync();
    await scopedStaffPage.GotoAsync(SignInUrl(host.StaffUrl, "/app/administration/project-progress"));
    await scopedStaffPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    Assert.Equal(0, await scopedStaffPage.Locator(".audit-module-progress-grid").CountAsync());

    await page.GotoAsync(SignInUrl(origin, "/app"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio" }).First.WaitForAsync();
    await page.WaitForFunctionAsync("() => getComputedStyle(document.documentElement).getPropertyValue('--audit-blue').trim() === '#2b6cb0'");
    Assert.Equal("#2b6cb0", await page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).getPropertyValue('--audit-blue').trim()"));
    Assert.Equal("rgb(255, 255, 255)", await page.Locator(".audit-sidebar .mud-nav-link.active").First
      .EvaluateAsync<string>("element => getComputedStyle(element).color"));
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } initialCaptureDir)
    {
      Directory.CreateDirectory(initialCaptureDir);
      await page.ScreenshotAsync(new() { Path = Path.Combine(initialCaptureDir, "portfolio-initial.png"), FullPage = true });
    }
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      if (width < 960)
        await page.WaitForFunctionAsync("() => getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px'");
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Portfolio overflows the {width}px viewport.");
      if ((width == 390 || width == 1440) && Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } captureDir)
      {
        Directory.CreateDirectory(captureDir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"portfolio-{width}.png"), FullPage = true });
      }
    }

    // The client portal renders for the client identity through its own started web origin.
    var clientPage = await context.NewPageAsync();
    await clientPage.GotoAsync(SignInUrl(host.ClientUrl, "/portal"));
    await clientPage.GetByRole(AriaRole.Heading, new() { Name = "Client portal" }).First.WaitForAsync(new() { Timeout = 15000 });
    Assert.Equal(0, await clientPage.GetByRole(AriaRole.Navigation, new() { Name = "Primary navigation" }).CountAsync());
    Assert.Equal(0, await clientPage.GetByRole(AriaRole.Link, new() { Name = "Firm administration" }).CountAsync());
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await clientPage.SetViewportSizeAsync(width, 900);
      Assert.True(await clientPage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Client portal overflows the {width}px viewport.");
      if ((width == 390 || width == 1440) && Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } captureDir)
      {
        Directory.CreateDirectory(captureDir);
        await clientPage.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"client-portal-{width}.png"), FullPage = true });
      }
    }

    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";
}
