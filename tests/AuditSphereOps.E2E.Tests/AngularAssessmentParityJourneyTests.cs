using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAssessmentParityJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ExactHistoryProfileProgressAndCurrentDecisionAssentRemainScoped(bool canonical)
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-ASSESSMENT-PARITY");
    var f = host.Fixture;
    Guid decisionId;
    await using (var db = host.CreateDbContext()) decisionId = await AssessmentParitySeed.PopulateAsync(db, f);
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string> {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = canonical.ToString() });
    var prefix = canonical ? "" : "/ui";
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(prefix + "/app/assessments/" + decisionId));
    var recorded = page.GetByRole(AriaRole.Region, new() { Name = "Recorded professional decision", Exact = true });
    await Assertions.Expect(recorded).ToContainTextAsync("Exact synthetic historical decision");
    await Assertions.Expect(recorded).ToContainTextAsync("Evaluation 1");
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Client assessment profile", Exact = true })).ToContainTextAsync("PBC TEST CLIENT");
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Evaluation progress", Exact = true })).ToContainTextAsync("0 of 62 questions answered");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true })).ToHaveCountAsync(0);
    Assert.Contains("decisionId=" + decisionId, page.Url, StringComparison.OrdinalIgnoreCase);
    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await page.GetByRole(AriaRole.Link, new() { Name = "Open current evaluation", Exact = true }).ClickAsync();
    var answer = page.GetByRole(AriaRole.Region, new() { Name = "CE-001 assessment question", Exact = true });
    await answer.GetByLabel("Answer for CE-001", new() { Exact = true }).SelectOptionAsync("Yes");
    await answer.GetByLabel("Evidence reference (required)", new() { Exact = true }).FillAsync("SYNTHETIC-ASSESSMENT-EVIDENCE");
    await answer.GetByRole(AriaRole.Button, new() { Name = "Review answer for CE-001", Exact = true }).ClickAsync();
    var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed assessment action", Exact = true });
    var assent = page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this exact assessment action and its effects.", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    var dispatched = 0;
    var commandRoute = "**/api/ui/clients/" + f.ClientId + "/assessment/commands";
    await page.RouteAsync(commandRoute, async route => {
      var response = await route.FetchAsync();
      Assert.Equal(200, response.Status);
      dispatched++;
      await route.AbortAsync("failed");
    });
    await assent.CheckAsync();
    await confirm.ClickAsync();
    var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Assessment request recovery", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await Assertions.Expect(answer).ToHaveCountAsync(0);
    await page.ReloadAsync();
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Verify assessment receipt", Exact = true }).ClickAsync();
    var receipt = page.GetByRole(AriaRole.Region, new() { Name = "Retained assessment receipt", Exact = true });
    await Assertions.Expect(receipt).ToContainTextAsync("SYNTHETIC-ASSESSMENT-EVIDENCE");
    await page.GetByRole(AriaRole.Button, new() { Name = "Acknowledge assessment receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(answer).ToContainTextAsync("Recorded: Yes");
    Assert.Equal(1, dispatched);
    await page.UnrouteAsync(commandRoute);
    await using (var db = host.CreateDbContext()) {
      Assert.Single(await db.AssessmentCommandReceipts.Where(x => x.ClientId == f.ClientId).ToListAsync());
      Assert.Single(await db.EvaluationResponses.Where(x => x.PracticeClientId == f.ClientId && x.Generation == 2).ToListAsync());
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Service route", new() { Exact = true }).FillAsync("AccountingOnly");
    await page.GetByLabel("Decision", new() { Exact = true }).SelectOptionAsync("Declined");
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Synthetic current review");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true }).ClickAsync();
    await assent.CheckAsync(); await Assertions.Expect(confirm).ToBeEnabledAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Cancel review and edit", Exact = true }).ClickAsync();
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Changed synthetic review");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true }).ClickAsync();
    await Assertions.Expect(confirm).ToBeDisabledAsync(); await Assertions.Expect(assent).Not.ToBeCheckedAsync();
    await assent.CheckAsync(); await confirm.ClickAsync();
    await Assertions.Expect(receipt).ToContainTextAsync("Changed synthetic review");
    await page.GetByRole(AriaRole.Button, new() { Name = "Acknowledge assessment receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Recorded professional decision", Exact = true })).ToContainTextAsync("Changed synthetic review");
    await page.GotoAsync(origin + prefix + "/app/clients/" + f.ClientId + "/assessment?decisionId=" + Guid.NewGuid());
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Acceptance unavailable");
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Client assessment profile", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true })).ToHaveCountAsync(0);
    Assert.Empty(errors);
  }
}
