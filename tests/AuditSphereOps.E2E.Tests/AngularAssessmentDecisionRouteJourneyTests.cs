using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAssessmentDecisionRouteJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task LegacyDecisionDeepLinkEnforcesPartnerGrantAndRecoversLostDecisionOnce(bool canonical)
  {
    await using var host = await OwnedBlazorHost.StartAsync(
      startWorker: false, caseId: "ANGULAR-ASSESSMENT-DECISION-LINK", startLegacyBlazorHosts: false);
    var f = host.Fixture;
    await using (var db = host.CreateDbContext())
      await AssessmentParitySeed.PopulateAsync(db, f);

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = canonical.ToString()
    };
    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, settings);
    var origin = await host.StartApiForIdentityAsync(f.Admin, settings);
    var prefix = canonical ? string.Empty : "/ui";
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    // A current client-scoped Staff grant can open the assessment, but cannot acquire
    // the Partner-only decision action through the legacy direct URL.
    await using (var staffContext = await browser.NewContextAsync())
    {
      var staffPage = await staffContext.NewPageAsync();
      await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
        prefix + "/app/assessments/" + f.ClientId + "/decision"));
      await Assertions.Expect(staffPage.GetByRole(AriaRole.Region,
        new() { Name = "Client assessment profile", Exact = true })).ToContainTextAsync("PBC TEST CLIENT");
      await Assertions.Expect(staffPage.GetByRole(AriaRole.Button,
        new() { Name = "Review Partner decision", Exact = true })).ToHaveCountAsync(0);
    }

    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
      prefix + "/app/assessments/" + f.ClientId + "/decision"));
    await Assertions.Expect(page.GetByRole(AriaRole.Region,
      new() { Name = "Client assessment profile", Exact = true })).ToContainTextAsync("PBC TEST CLIENT");
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Review Partner decision", Exact = true })).ToBeVisibleAsync();
    Assert.Contains("/app/clients/" + f.ClientId + "/assessment", page.Url, StringComparison.Ordinal);

    await page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true }).ClickAsync();
    await page.GetByLabel("Service route", new() { Exact = true }).FillAsync("AccountingOnly");
    await page.GetByLabel("Decision", new() { Exact = true }).SelectOptionAsync("Declined");
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Deep-link reviewed decision recovery");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true }).ClickAsync();

    var review = page.GetByRole(AriaRole.Region, new() { Name = "Exact assessment action review", Exact = true });
    await Assertions.Expect(review).ToContainTextAsync("Deep-link reviewed decision recovery");
    var assent = page.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this exact assessment action and its effects.", Exact = true });
    var confirm = page.GetByRole(AriaRole.Button,
      new() { Name = "Confirm reviewed assessment action", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();

    var dispatched = 0;
    var commandRoute = "**/api/ui/clients/" + f.ClientId + "/assessment/commands";
    await page.RouteAsync(commandRoute, async route =>
    {
      var response = await route.FetchAsync();
      Assert.Equal(200, response.Status);
      dispatched++;
      await route.AbortAsync("failed");
    });
    await assent.CheckAsync();
    await confirm.ClickAsync();
    var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Assessment request recovery", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await page.ReloadAsync();
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Verify assessment receipt", Exact = true }).ClickAsync();
    var receipt = page.GetByRole(AriaRole.Region, new() { Name = "Retained assessment receipt", Exact = true });
    await Assertions.Expect(receipt).ToContainTextAsync("Deep-link reviewed decision recovery");
    await page.GetByRole(AriaRole.Button, new() { Name = "Acknowledge assessment receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,
      new() { Name = "Recorded professional decision", Exact = true }))
      .ToContainTextAsync("Deep-link reviewed decision recovery");
    await page.UnrouteAsync(commandRoute);

    await using (var proof = host.CreateDbContext())
    {
      Assert.Single(await proof.AssessmentCommandReceipts.Where(x => x.ClientId == f.ClientId && x.Kind == "DECISION").ToListAsync());
      Assert.Single(await proof.AcceptanceDecisions.Where(x => x.PracticeClientId == f.ClientId &&
        x.Rationale == "Deep-link reviewed decision recovery").ToListAsync());
    }
    Assert.Equal(1, dispatched);
    Assert.Empty(errors);
  }
}
