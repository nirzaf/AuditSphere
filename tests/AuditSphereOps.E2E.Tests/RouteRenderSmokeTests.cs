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
    }

    // The client portal renders for the client identity through its own started web origin.
    var clientPage = await context.NewPageAsync();
    await clientPage.GotoAsync(SignInUrl(host.ClientUrl, "/portal"));
    await clientPage.GetByRole(AriaRole.Heading, new() { Name = "Client portal" }).First.WaitForAsync(new() { Timeout = 15000 });

    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";
}
