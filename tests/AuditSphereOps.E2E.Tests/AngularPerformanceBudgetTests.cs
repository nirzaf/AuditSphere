using System.Diagnostics;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// US-043 local production-like budgets for the native shell: cold first paint of the signed-in portfolio,
/// a warm full load of a lazy route, a client-side router navigation, and rendering a large lead dataset.
/// Ceilings are deliberately loose local budgets that catch order-of-magnitude regressions; exact observed
/// measurements are recorded only in docs/execution/status.json, never asserted to two significant figures.
/// </summary>
public sealed class AngularPerformanceBudgetTests
{
  private readonly Xunit.Abstractions.ITestOutputHelper output;
  public AngularPerformanceBudgetTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

  private static readonly TimeSpan ColdBudget = TimeSpan.FromSeconds(12);
  private static readonly TimeSpan WarmLoadBudget = TimeSpan.FromSeconds(6);
  private static readonly TimeSpan RouterBudget = TimeSpan.FromSeconds(4);
  private static readonly TimeSpan LargeDatasetBudget = TimeSpan.FromSeconds(5);

  [Fact]
  [Trait("CaseId", "ANGULAR-PERF-E2E")]
  public async Task NativeShell_MeetsLocalColdWarmAndLargeDatasetBudgets()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-PERF-E2E", startLegacyBlazorHosts: false);
    var f = host.Fixture;
    const int leadCount = 60;
    await using (var db = host.CreateDbContext())
    {
      for (var i = 0; i < leadCount; i++)
      {
        var created = await PracticeCrmService.CreateLeadAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
          new($"PERF LEAD {i:D3}", "PERFORMANCE", $"Contact {i:D3}", $"contact{i:D3}@example.test"));
        Assert.True(created.Succeeded, created.Message);
      }
    }

    var origin = await host.StartApiForIdentityAsync(f.Admin,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);

    var cold = Stopwatch.StartNew();
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/app"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    cold.Stop();

    var warm = Stopwatch.StartNew();
    await page.GotoAsync(origin + "/ui/app/practice/leads");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Practice leads", Exact = true })).ToBeVisibleAsync();
    warm.Stop();

    var large = Stopwatch.StartNew();
    await Assertions.Expect(page.GetByText($"PERF LEAD {leadCount - 1:D3}", new() { Exact = true }))
      .ToBeVisibleAsync(new() { Timeout = (float)LargeDatasetBudget.TotalMilliseconds * 2 });
    large.Stop();

    var router = Stopwatch.StartNew();
    await page.GetByRole(AriaRole.Link, new() { Name = "Portfolio", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    router.Stop();

    Assert.True(cold.Elapsed <= ColdBudget, $"Cold first paint {cold.ElapsedMilliseconds}ms exceeded the {ColdBudget.TotalMilliseconds}ms local budget.");
    Assert.True(warm.Elapsed <= WarmLoadBudget, $"Warm lazy-route load {warm.ElapsedMilliseconds}ms exceeded the {WarmLoadBudget.TotalMilliseconds}ms local budget.");
    Assert.True(router.Elapsed <= RouterBudget, $"Router navigation {router.ElapsedMilliseconds}ms exceeded the {RouterBudget.TotalMilliseconds}ms local budget.");
    Assert.True(large.Elapsed <= LargeDatasetBudget, $"Large-dataset render ({leadCount} leads) {large.ElapsedMilliseconds}ms exceeded the {LargeDatasetBudget.TotalMilliseconds}ms local budget.");
    output.WriteLine($"PERF cold={cold.ElapsedMilliseconds}ms warm={warm.ElapsedMilliseconds}ms router={router.ElapsedMilliseconds}ms largeDataset({leadCount} leads)={large.ElapsedMilliseconds}ms");
    Assert.Empty(errors);
  }
}
