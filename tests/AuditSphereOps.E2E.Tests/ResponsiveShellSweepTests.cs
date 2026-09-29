using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// UX-003/UX-030 whole-application shell sweep for an authorized firm-wide staff identity: every parameterless staff
/// route and the seeded client, engagement, audit and accounting detail routes render without page-level horizontal
/// overflow at the 320-1920px matrix, show at most one exact-route active navigation entry, and mark the owning
/// section of detail routes. Navigation highlighting is presentation only; each page still authorizes its own data.
/// </summary>
public sealed class ResponsiveShellSweepTests(ITestOutputHelper output)
{
  private static readonly int[] Widths = [320, 390, 760, 1024, 1440, 1920];

  private static readonly string[] ParameterlessRoutes =
  [
    "/app", "/app/practice/leads", "/app/practice/commercial-settings", "/app/practice/time", "/app/finance", "/app/operations", "/app/administration",
    "/app/administration/project-progress", "/app/administration/microsoft365/tenant-connection", "/app/accounting",
    "/app/accounting/evidence", "/app/accounting/mappings", "/app/accounting/journals", "/app/accounting/differences",
    "/app/accounting/reviews", "/app/accounting/rollforward", "/app/accounting/restatements",
    "/app/accounting/remeasurement", "/app/consolidation", "/app/audit/library"
  ];

  [Fact]
  [Trait("CaseId", "AS-UI-RESPONSIVE-SWEEP-01")]
  public async Task StaffRoutesReflowAcrossTheViewportMatrixAndHighlightOneSection()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-UI-RESPONSIVE-SWEEP-01");
    var user = PbcSeed.User(host.Fixture.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(user);
      foreach (var role in new[] { "Administrator", "Partner", "Manager", "Staff", "AccountingPreparer", "AccountingReviewer" })
        db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, user, role));
      await db.SaveChangesAsync();
    }
    // A second client with long, marker-named records exercises wrapping on the detail routes.
    var seeded = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, "SWEEP");
    await using (var db = host.CreateDbContext())
    {
      var client = await db.PracticeClients.FindAsync(seeded.Fixture.ClientId);
      client!.LegalName = "Responsive Sweep Holdings International Limited With An Intentionally Long Registered Name";
      await db.SaveChangesAsync();
    }
    try
    {
      var engagement = seeded.Fixture.EngagementId;
      var detailRoutes = new (string Route, string? Section)[]
      {
        ($"/app/clients/{seeded.Fixture.ClientId:D}", "Portfolio"),
        ($"/app/engagements/{engagement:D}", "Portfolio"),
        ($"/app/engagements/{engagement:D}/pbc", "Portfolio"),
        ($"/app/engagements/{engagement:D}/audit-plan", "Audit program library"),
        ($"/app/engagements/{engagement:D}/audit-fieldwork", "Audit program library"),
        ($"/app/engagements/{engagement:D}/completion", "Audit program library"),
        ($"/app/accounting/periods/{seeded.PeriodId:D}", "Accounting workspace"),
        ($"/app/accounting/packages/{seeded.PackageId:D}", "Package reviews"),
        ($"/app/accounting/mappings/{seeded.MappingId:D}", null),
        ($"/app/accounting/journals/{seeded.JournalId:D}", null),
      };

      var origin = await host.StartWebForIdentityAsync(user);
      using var playwright = await Playwright.CreateAsync();
      await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
      var page = await context.NewPageAsync();
      var diagnostics = new List<string>();
      page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
      await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/app")}");
      await page.Locator("h1").First.WaitForAsync(new() { Timeout = 15000 });

      var failures = new List<string>();
      foreach (var (route, section) in ParameterlessRoutes.Select(x => (x, (string?)null)).Concat(detailRoutes))
      {
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync(origin + route);
        await page.Locator("h1").First.WaitForAsync(new() { Timeout = 15000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var active = await page.Locator(".audit-sidebar .mud-nav-link.active").CountAsync();
        if (active > 1) failures.Add($"{route}: {active} exact-active navigation entries");
        if (section is not null)
        {
          var marked = await page.Locator(".audit-sidebar .audit-nav-section-current").AllInnerTextsAsync();
          if (marked.Count != 1 || !marked[0].Contains(section, StringComparison.Ordinal))
            failures.Add($"{route}: expected owning section '{section}', saw [{string.Join(", ", marked)}]");
        }

        foreach (var width in Widths)
        {
          await page.SetViewportSizeAsync(width, 900);
          await page.WaitForFunctionAsync(width < 960
            ? "() => getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px'"
            : "() => document.querySelector('.audit-sidebar')?.getBoundingClientRect().left >= -1");
          var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
          if (overflow > 1)
          {
            var culprits = await page.EvaluateAsync<string[]>(
              "() => [...document.querySelectorAll('main *')].filter(x => x.getBoundingClientRect().right > innerWidth + 1 && !x.closest('.table-wrap, .mud-table-container, pre, .audit-artifact-text')).slice(0, 6).map(x => `${x.tagName.toLowerCase()}.${String(x.className).slice(0, 60)}`)");
            failures.Add($"{route} @{width}px overflows by {overflow}px: {string.Join(", ", culprits)}");
          }
        }
      }

      foreach (var failure in failures) output.WriteLine(failure);
      Assert.Empty(failures);
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
      output.WriteLine($"{ParameterlessRoutes.Length} parameterless and {detailRoutes.Length} detail routes reflowed at {string.Join("/", Widths)}px with one navigation section each");
    }
    finally
    {
      PbcSeed.DeleteDirectory(seeded.StagingRoot);
    }
  }

  [Fact]
  [Trait("CaseId", "AS-UI-RECONNECT-STATE-01")]
  public async Task LostCircuitShowsReconnectStateWithoutClaimingPendingActionsCompleted()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-UI-RECONNECT-STATE-01");
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    // Test-only network fault: record page WebSockets and, while blocked, point new ones at a closed port.
    await context.AddInitScriptAsync("""
      (() => {
        const Native = window.WebSocket;
        window.__auditSockets = [];
        window.__auditBlockSockets = false;
        window.WebSocket = class extends Native {
          constructor(url, protocols) {
            super(window.__auditBlockSockets ? "ws://127.0.0.1:9/blocked" : url, protocols);
            window.__auditSockets.push(this);
          }
        };
      })();
      """);
    var page = await context.NewPageAsync();
    await page.GotoAsync($"{host.StaffUrl}/auth/sign-in?returnUrl={Uri.EscapeDataString("/app")}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio" }).First.WaitForAsync(new() { Timeout = 15000 });
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    var modal = page.Locator("#components-reconnect-modal");
    await Assertions.Expect(modal).ToBeHiddenAsync();

    // An unrequested close of the live circuit socket is what a dropped network connection looks like to Blazor.
    await page.EvaluateAsync("() => { window.__auditBlockSockets = true; window.__auditSockets.filter(x => x.readyState === 1).forEach(x => x.close()); }");
    await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 15000 });
    await Assertions.Expect(modal).ToContainTextAsync("is not confirmed until its result is shown after reconnecting");

    await page.EvaluateAsync("() => { window.__auditBlockSockets = false; }");
    // Blazor either resumes the circuit or reloads the page; in both cases the panel clears.
    await Assertions.Expect(modal).ToBeHiddenAsync(new() { Timeout = 60000 });
    await page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio" }).First.WaitForAsync();
  }
}
