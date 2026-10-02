using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularGeneralLedgerAcceptanceJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-GL-ACCEPTANCE")]
  public async Task InspectionIndependentAcceptance_UnknownSaveReloadAndRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-GL-ACCEPTANCE");
    var first = await Seed(host); var second = await Seed(host);
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var pw = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(pw);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/ui/app/engagements/{host.Fixture.EngagementId}/general-ledger"));
    await page.GetByRole(AriaRole.Button, new() { Name = $"Inspect ledger {first:D}", Exact = true }).First.ClickAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "Review general ledger source acceptance", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("GL import batch");
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Account-exact completeness evidence");
    Assert.DoesNotContain("Worker validation", await page.Locator("body").InnerTextAsync());
    // Navigate directly to a known source so catalogue ordering cannot choose the reviewed identity.
    await page.GotoAsync(origin + Route(first));
    var evidence = page.GetByLabel("Acceptance evidence reference", new() { Exact = true });
    var assent = page.GetByLabel("I independently reviewed this exact source and its effect on the selected pointer.", new() { Exact = true });
    var accept = page.GetByRole(AriaRole.Button, new() { Name = "Accept reviewed source", Exact = true });
    await evidence.FillAsync("Independent browser GL evidence"); await assent.CheckAsync(); await accept.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Retained acceptance decision", Exact = true })).ToBeVisibleAsync();
    var writes = 0; await page.RouteAsync($"**/gl-sources/{second}/acceptance", async r =>
    {
      if (r.Request.Method != "POST") { await r.ContinueAsync(); return; }
      writes++; var response = await r.FetchAsync(); Assert.Equal(200, response.Status); await r.AbortAsync("failed");
    });
    await page.GotoAsync(origin + Route(second)); await Assertions.Expect(page.Locator("body")).ToContainTextAsync(first.ToString("D"));
    await evidence.FillAsync("Replacement GL evidence"); await assent.CheckAsync(); await accept.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Acceptance outcome needs review", Exact = true })).ToBeVisibleAsync();
    await page.ReloadAsync(); await page.GetByRole(AriaRole.Button, new() { Name = "Read persisted acceptance", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "I reviewed the persisted acceptance", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("This is the currently selected source.", new() { Exact = true })).ToBeVisibleAsync(); Assert.Equal(1, writes);
    await page.SetViewportSizeAsync(390, 844); Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(2, await db.SourceAcceptanceDecisions.CountAsync()); Assert.Empty(await db.GeneralLedgerCompletenessBridges.ToListAsync());
      Assert.Equal(3, await db.ClientSafetyStates.Where(x => x.Id == host.Fixture.ClientId).Select(x => x.InputGeneration).SingleAsync());
      await db.Users.Where(x => x.Id == host.Fixture.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain("Replacement GL evidence", await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-GL-ACCEPTANCE-GATES")]
  public async Task ImporterCannotAccept_StaleReviewRefused_AndNavigationProtectsIntent()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-GL-ACCEPTANCE-GATES");
    var id = await Seed(host); var self = await Seed(host, self: true);
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var pw = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(pw); var page = await browser.NewPageAsync();
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(Route(self)));
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("other than the source importer");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Accept reviewed source", Exact = true })).ToBeDisabledAsync();
    await page.GotoAsync(origin + Route(id)); var evidence = page.GetByLabel("Acceptance evidence reference", new() { Exact = true });
    await evidence.FillAsync("Unsubmitted GL evidence"); await page.GetByRole(AriaRole.Link, new() { Name = "Back to general ledger inspection", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync(); await page.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync();
    await page.GetByLabel("I independently reviewed this exact source and its effect on the selected pointer.", new() { Exact = true }).CheckAsync();
    await using (var db = host.CreateDbContext()) await db.ClientSafetyStates.Where(x => x.Id == host.Fixture.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    await page.GetByRole(AriaRole.Button, new() { Name = "Accept reviewed source", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("changed");
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh source acceptance", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByLabel("I independently reviewed this exact source and its effect on the selected pointer.", new() { Exact = true })).Not.ToBeCheckedAsync();
    await using var proof = host.CreateDbContext(); Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());
  }

  private static string Route(Guid id) => $"/ui/app/accounting/gl-sources/{id}/acceptance";
  private static async Task<Guid> Seed(OwnedBlazorHost host, bool self = false)
  {
    await using var db = host.CreateDbContext();
    return await GeneralLedgerWorkspaceSeed.SeedAsync(db, self ? host.Fixture with { Staff = host.Fixture.Admin } : host.Fixture, journals: 1);
  }
}
