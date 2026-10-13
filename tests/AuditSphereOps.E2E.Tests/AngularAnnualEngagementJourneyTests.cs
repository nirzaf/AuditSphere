using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAnnualEngagementJourneyTests
{
  [Fact]
  public async Task PriorPeriodReferenceCreatesOneReviewedBlockedAnnualShellWithoutCopyingApprovals()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-ANNUAL-ENGAGEMENT"); var f = host.Fixture;
    Guid sourceId;
    await using (var db = host.CreateDbContext())
    {
      await EngagementCreationReviewSeed.PopulateAsync(db, f);
      var source = await EngagementLifecycleService.CreateDraftAsync(db, PbcSeed.Actor(f.Staff, "Manager"),
        new(f.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "Synthetic annual reference"));
      Assert.True(source.Succeeded, source.Message); sourceId = source.Value;
    }
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
      { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    var path = "/app/clients/" + f.ClientId + "/engagements/new?annualSourceEngagementId=" + sourceId;
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/clients/" + f.ClientId));
    await page.GetByRole(AriaRole.Row).Filter(new() { HasText = sourceId.ToString() })
      .GetByRole(AriaRole.Link, new() { Name = "Prepare next annual period", Exact = true }).ClickAsync();
    var annual = page.GetByRole(AriaRole.Region, new() { Name = "Annual engagement preparation", Exact = true });
    await Assertions.Expect(annual).ToContainTextAsync("Partner continuance");
    await Assertions.Expect(annual).ToContainTextAsync("2027-01-01 to 2027-12-31");
    await annual.GetByRole(AriaRole.Button, new() { Name = "Use annual suggestion", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByLabel("Period start", new() { Exact = true })).ToHaveValueAsync("2027-01-01");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review engagement", Exact = true }).ClickAsync();
    var review = page.GetByRole(AriaRole.Region, new() { Name = "Reviewed engagement intent", Exact = true });
    await Assertions.Expect(review).ToContainTextAsync(sourceId.ToString());
    var confirm = review.GetByRole(AriaRole.Button, new() { Name = "Confirm blocked creation", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path);
    await page.GetByLabel("I reviewed this exact client, service, profile and period.", new() { Exact = true }).CheckAsync();
    await confirm.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Engagement creation receipt", Exact = true })).ToContainTextAsync(sourceId.ToString());
    await using (var db = host.CreateDbContext())
    {
      var receipt = await db.EngagementCreations.AsNoTracking().SingleAsync();
      var created = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == receipt.EngagementId);
      Assert.Equal(("Draft", true, "2027-01-01", "2027-12-31"),
        (created.Status, created.ProfessionalWorkBlocked, created.PeriodStart, created.PeriodEnd));
      Assert.False(await db.AcceptanceDecisions.AnyAsync(x => x.EngagementId == created.Id));
      Assert.Empty(await db.EngagementActivations.ToListAsync());
      Assert.Equal("2026-12-31", (await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == sourceId)).PeriodEnd);
    }
    Assert.Empty(errors);
  }
}
