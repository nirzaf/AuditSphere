using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

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
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Record Partner decision", Exact = true })).ToHaveCountAsync(0);
    Assert.Contains("decisionId=" + decisionId, page.Url, StringComparison.OrdinalIgnoreCase);
    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await page.GetByRole(AriaRole.Link, new() { Name = "Open current evaluation", Exact = true }).ClickAsync();
    var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Record Partner decision", Exact = true });
    await Assertions.Expect(confirm).ToBeVisibleAsync();
    await page.GetByLabel("Service route", new() { Exact = true }).FillAsync("AccountingOnly");
    await page.GetByLabel("Decision", new() { Exact = true }).SelectOptionAsync("Declined");
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Synthetic current review");
    var assent = page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this evaluation and confirm my Partner decision.", Exact = true });
    await assent.CheckAsync(); await Assertions.Expect(confirm).ToBeEnabledAsync();
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Changed synthetic review");
    await Assertions.Expect(confirm).ToBeDisabledAsync(); await Assertions.Expect(assent).Not.ToBeCheckedAsync();
    await page.GotoAsync(origin + prefix + "/app/clients/" + f.ClientId + "/assessment?decisionId=" + Guid.NewGuid());
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Acceptance unavailable");
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Client assessment profile", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Record Partner decision", Exact = true })).ToHaveCountAsync(0);
    Assert.Empty(errors);
  }
}
