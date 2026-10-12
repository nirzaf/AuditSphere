using System.Text.Json;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAnalyticsPagingJourneyTests
{
  [Fact]
  public async Task EconomicsPagesUseTheRealApiAndKeepPeriodTaskDenominators()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-ANALYTICS-PAGING");
    var f = host.Fixture;
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    await using (var db = host.CreateDbContext())
    {
      for (var index = 0; index < 51; index++)
      {
        var clientId = Guid.NewGuid(); var engagementId = Guid.NewGuid(); var taskId = Guid.NewGuid();
        db.PracticeClients.Add(new PracticeClient { Id = clientId, FirmId = f.FirmId,
          LegalName = $"SYNTHETIC PAGING CLIENT {index:D2}", CreatedAt = DateTimeOffset.UtcNow });
        db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = f.FirmId });
        db.Engagements.Add(new Engagement { Id = engagementId, FirmId = f.FirmId, PracticeClientId = clientId,
          ServiceRoute = "AccountingOnly", ServiceProfileId = "synthetic-paging",
          PeriodStart = $"{today.Year}-01-01", PeriodEnd = $"{today.Year}-12-31", CreatedAt = DateTimeOffset.UtcNow });
        db.WorkTasks.Add(new WorkTask { Id = taskId, FirmId = f.FirmId, ClientId = clientId,
          EngagementId = engagementId, AssigneeUserId = f.Staff.Id, Title = "Synthetic analytics task",
          DueDate = today, Status = PracticeTimeStates.TaskOpen, CreatedAt = DateTimeOffset.UtcNow });
        db.TimeEntries.Add(new TimeEntry { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = clientId,
          EngagementId = engagementId, TaskId = taskId, UserId = f.Staff.Id, WorkDate = today,
          StartMinute = index * 10, DurationMinutes = 10, Role = "Staff", Activity = "SYNTHETIC",
          BillableClassification = PracticeTimeStates.NonBillable, Narrative = "Synthetic report fixture only",
          Currency = "QAR", Status = PracticeTimeStates.TimeApproved, SubmittedAt = DateTimeOffset.UtcNow,
          ApprovedByUserId = f.Admin.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
      }
      await db.SaveChangesAsync();
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
      { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    const string path = "/app/practice/analytics";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    var economics = page.GetByRole(AriaRole.Region, new() { Name = "Engagement economics", Exact = true });
    await Assertions.Expect(economics.Locator("tbody tr")).ToHaveCountAsync(50);
    var summary = page.Locator("p[aria-label='Task completion and milestone performance']");
    await Assertions.Expect(summary).ToContainTextAsync("0 / 51 applicable tasks completed");
    await economics.GetByRole(AriaRole.Button, new() { Name = "Next engagements", Exact = true }).ClickAsync();
    await Assertions.Expect(economics.Locator("tbody tr")).ToHaveCountAsync(1);
    await Assertions.Expect(summary).ToContainTextAsync("0 / 51 applicable tasks completed");
    await economics.GetByRole(AriaRole.Button, new() { Name = "First engagements", Exact = true }).ClickAsync();
    await Assertions.Expect(economics.Locator("tbody tr")).ToHaveCountAsync(50);
    Assert.Empty(errors);

    // Optional bounded handoff reuses this owned synthetic API/database for built-in-browser inspection.
    // Basenames only; never overwrite an existing artifact or touch a persistent application database.
    var readyName = Environment.GetEnvironmentVariable("AUDITSPHERE_BROWSER_FIXTURE_READY_FILE");
    var releaseName = Environment.GetEnvironmentVariable("AUDITSPHERE_BROWSER_FIXTURE_RELEASE_FILE");
    if (!string.IsNullOrEmpty(readyName) && !string.IsNullOrEmpty(releaseName))
    {
      if (Path.GetFileName(readyName) != readyName || Path.GetFileName(releaseName) != releaseName)
        throw new InvalidOperationException("Fixture handoff names must be temporary-file basenames.");
      var ready = Path.Combine(Path.GetTempPath(), readyName);
      var release = Path.Combine(Path.GetTempPath(), releaseName);
      if (File.Exists(ready) || File.Exists(release)) throw new InvalidOperationException("Fixture handoff already exists.");
      var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
      try
      {
        await File.WriteAllTextAsync(ready, JsonSerializer.Serialize(new { origin, path, environment = "Owned synthetic test fixture", expiresAtUtc = deadline }));
        while (!File.Exists(release) && DateTimeOffset.UtcNow < deadline) await Task.Delay(250);
      }
      finally { File.Delete(ready); }
    }
  }
}
