using System.Text.Json;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// T016 / WU-028: a staff user adds a manual workflow task from the engagement page. A lost response is recovered by
/// retrying the exact saved request, the server keeps one task, and creating work never advances the lifecycle.
/// </summary>
[Trait("CompletionUnit", "WU-028")]
public sealed class AngularWorkflowTaskCreationJourneyTests
{
  [Fact]
  public async Task ManualTaskCreationRecoversALostResponseWithoutADuplicateOrLifecycleChange()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-WORKFLOW-TASK-CREATE");
    var f = host.Fixture;
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/engagements/{f.EngagementId:D}"));

    var form = page.GetByRole(AriaRole.Region, new() { Name = "Add manual workflow task", Exact = true });
    await Assertions.Expect(form).ToBeVisibleAsync(new() { Timeout = 20000 });
    await Assertions.Expect(form).ToContainTextAsync("does not approve audit work or advance the engagement lifecycle");
    var stagesBefore = await page.Locator("audit-engagement-workflow").Locator("ol, [role='list']").First.InnerTextAsync();

    const string title = "SYNTHETIC MANUAL FOLLOW-UP TASK";
    await form.GetByLabel("Task title", new() { Exact = true }).FillAsync(title);
    await form.GetByLabel("Order in phase", new() { Exact = true }).FillAsync("7");

    // The first POST is persisted, but its reply is lost.
    var keys = new List<string>();
    const string pattern = "**/api/ui/engagements/*/workflow/tasks";
    await page.RouteAsync(pattern, async route =>
    {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
      keys.Add(body.RootElement.GetProperty("idempotencyKey").GetString()!);
      var persisted = await route.FetchAsync();
      if (keys.Count == 1)
      {
        Assert.True(persisted.Ok, $"Synthetic persisted create returned {persisted.Status}");
        await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Synthetic lost reply\"}" });
        return;
      }
      await route.FulfillAsync(new() { Response = persisted });
    });
    await form.GetByRole(AriaRole.Button, new() { Name = "Create workflow task", Exact = true }).ClickAsync();
    await Assertions.Expect(form).ToContainTextAsync("outcome is unconfirmed");
    await form.GetByRole(AriaRole.Button, new() { Name = "Retry exact request", Exact = true }).ClickAsync();
    await Assertions.Expect(form.GetByRole(AriaRole.Button, new() { Name = "Retry exact request", Exact = true })).ToHaveCountAsync(0);
    await page.UnrouteAsync(pattern);

    Assert.Equal(2, keys.Count);
    Assert.Equal(keys[0], keys[1]);
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByText(title, new() { Exact = true })).ToHaveCountAsync(1, new() { Timeout = 20000 });
    var stagesAfter = await page.Locator("audit-engagement-workflow").Locator("ol, [role='list']").First.InnerTextAsync();
    Assert.Equal(stagesBefore, stagesAfter);
    Assert.Empty(errors);
  }
}
