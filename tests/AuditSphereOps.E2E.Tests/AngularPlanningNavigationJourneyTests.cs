using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularPlanningNavigationJourneyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlanningNavigationKeepsSavesRestoresAndDiscardsWithoutBusinessWrites(bool canonical)
    {
        await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-PLANNING-NAVIGATION-E2E");
        var fixture = host.Fixture;
        await using (var db = host.CreateDbContext()) await BudgetPreparationReviewSeed.PopulateAsync(db, fixture);
        var origin = await host.StartApiForIdentityAsync(fixture.Staff, new Dictionary<string, string>
        {
            ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = canonical.ToString()
        });
        var prefix = canonical ? "" : "/ui";
        var path = prefix + "/app/engagements/" + fixture.EngagementId;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var businessWrites = 0;
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.Contains("/api/ui/engagements/", StringComparison.Ordinal))
                Interlocked.Increment(ref businessWrites);
        };
        await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
        var planning = page.GetByRole(AriaRole.Region, new() { Name = "Engagement planning", Exact = true });
        await Assertions.Expect(planning).ToBeVisibleAsync();
        await planning.GetByLabel("Currency", new() { Exact = true }).FillAsync("USD");
        await page.GetByRole(AriaRole.Link, new() { Name = "PBC requests", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToContainTextAsync("staffing choices and approval are never saved");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync();
        await Assertions.Expect(planning.GetByLabel("Currency", new() { Exact = true })).ToHaveValueAsync("USD");
        await page.GetByRole(AriaRole.Link, new() { Name = "PBC requests", Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save budget draft and continue", Exact = true }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(origin + path + "/pbc");
        var planningPath = $"/api/ui/engagements/{fixture.EngagementId}/planning";
        var (planningStatus, planningBody) = await PlaywrightResponseCapture.RunAsync(page,
            request => request.Method == "GET" && new Uri(request.Url).AbsolutePath.Equals(planningPath, StringComparison.OrdinalIgnoreCase),
            async () =>
            {
                await page.GotoAsync(origin + path);
                await Assertions.Expect(planning).ToBeVisibleAsync();
            });
        Assert.True(planningStatus is >= 200 and < 300,
            $"Planning data did not reload before restoring the saved budget fields (HTTP {planningStatus}): {planningBody}");
        var restore = planning.GetByRole(AriaRole.Button, new() { Name = "Restore saved budget fields", Exact = true });
        await Assertions.Expect(restore).ToBeEnabledAsync();
        await restore.ClickAsync();
        await Assertions.Expect(planning.GetByLabel("Currency", new() { Exact = true })).ToHaveValueAsync("USD");
        await planning.GetByLabel("Forecast minutes", new() { Exact = true }).FillAsync("123");
        await page.SetViewportSizeAsync(390, 844);
        await page.GetByRole(AriaRole.Link, new() { Name = "PBC requests", Exact = true }).ClickAsync();
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Discard edits and continue", Exact = true })).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Discard edits and continue", Exact = true }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(origin + path + "/pbc");
        Assert.Equal(0, businessWrites);
        Assert.Empty(errors);
    }
}
