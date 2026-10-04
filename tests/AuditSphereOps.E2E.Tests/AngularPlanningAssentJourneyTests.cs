using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularPlanningAssentJourneyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedStaffingSelectionClearsNativeAssentWithoutDispatch(bool canonical)
    {
        await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-PLANNING-ASSENT-E2E");
        var fixture = host.Fixture;
        await using (var db = host.CreateDbContext()) await BudgetPreparationReviewSeed.PopulateAsync(db, fixture);
        var origin = await host.StartApiForIdentityAsync(fixture.Staff, new Dictionary<string, string>
        {
            ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = canonical.ToString()
        });
        var path = (canonical ? "" : "/ui") + "/app/engagements/" + fixture.EngagementId;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var writes = 0;
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.Contains("/api/ui/engagements/", StringComparison.Ordinal))
                Interlocked.Increment(ref writes);
        };
        await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
        await page.GetByText("Team and budget", new() { Exact = true }).ScrollIntoViewIfNeededAsync();
        var planning = page.GetByRole(AriaRole.Region, new() { Name = "Engagement planning", Exact = true });
        var person = planning.GetByLabel("Person", new() { Exact = true });
        await Assertions.Expect(person).ToBeVisibleAsync();
        var candidates = await person.Locator("option").EvaluateAllAsync<string[]>("options => options.map(option => option.value).filter(Boolean)");
        Assert.True(candidates.Length >= 2);
        var assent = planning.GetByLabel("I reviewed the engagement role and client-site access.", new() { Exact = true });
        var add = planning.GetByRole(AriaRole.Button, new() { Name = "Review team assignment", Exact = true });
        await person.SelectOptionAsync(candidates[0]);
        await assent.CheckAsync();
        await Assertions.Expect(add).ToBeEnabledAsync();
        await planning.GetByLabel("Level", new() { Exact = true }).SelectOptionAsync("SENIOR_AUDITOR");
        await Assertions.Expect(assent).Not.ToBeCheckedAsync();
        await Assertions.Expect(add).ToBeDisabledAsync();
        await assent.CheckAsync();
        await Assertions.Expect(add).ToBeEnabledAsync();
        await person.SelectOptionAsync(candidates[1]);
        await Assertions.Expect(assent).Not.ToBeCheckedAsync();
        await Assertions.Expect(add).ToBeDisabledAsync();
        await page.SetViewportSizeAsync(390, 844);
        await Assertions.Expect(assent).ToBeVisibleAsync();
        Assert.Equal(0, writes);
        Assert.Empty(errors);
    }
}
