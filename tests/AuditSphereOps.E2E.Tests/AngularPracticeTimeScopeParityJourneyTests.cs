using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularPracticeTimeScopeParityJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-TIME-ENG-01")]
  public async Task EngagementScopedTimeQueueHidesSiblingTasksEntriesAndClientPeriods()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-TIME-ENG-01");
    var f = host.Fixture;
    var engagementManager = PbcSeed.User(f.FirmId, "Staff");
    var siblingEngagementId = Guid.NewGuid();
    var assignedTaskId = Guid.NewGuid();
    var siblingTaskId = Guid.NewGuid();
    const string periodCode = "SYN-PAR-002-TIME-CLIENT-PERIOD";
    const string assignedTaskTitle = "SYN-PAR-002-ASSIGNED-ENGAGEMENT-TASK";
    const string siblingTaskTitle = "SYN-PAR-002-SIBLING-ENGAGEMENT-TASK";
    const string assignedNarrative = "SYN-PAR-002-ASSIGNED-TIME-NARRATIVE";
    const string siblingNarrative = "SYN-PAR-002-SIBLING-TIME-NARRATIVE";

    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(engagementManager);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, engagementManager, "Manager",
        clientId: f.ClientId, engagementId: f.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        PeriodCode = periodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.WorkTasks.AddRange(
        new WorkTask
        {
          Id = assignedTaskId, FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = f.EngagementId, Title = assignedTaskTitle, CreatedAt = DateTimeOffset.UtcNow
        },
        new WorkTask
        {
          Id = siblingTaskId, FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = siblingEngagementId, Title = siblingTaskTitle, CreatedAt = DateTimeOffset.UtcNow
        });
      db.TimeEntries.AddRange(
        new TimeEntry
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = f.EngagementId, TaskId = assignedTaskId, UserId = f.Staff.Id,
          WorkDate = new DateOnly(2026, 9, 10), StartMinute = 540, DurationMinutes = 60,
          Role = "Staff", Activity = "Fieldwork", Narrative = assignedNarrative,
          BillableClassification = PracticeTimeStates.NonBillable,
          Status = PracticeTimeStates.TimeSubmitted, SubmittedAt = DateTimeOffset.UtcNow,
          NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
        },
        new TimeEntry
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = siblingEngagementId, TaskId = siblingTaskId, UserId = f.Staff.Id,
          WorkDate = new DateOnly(2026, 9, 11), StartMinute = 540, DurationMinutes = 60,
          Role = "Staff", Activity = "Review", Narrative = siblingNarrative,
          BillableClassification = PracticeTimeStates.NonBillable,
          Status = PracticeTimeStates.TimeSubmitted, SubmittedAt = DateTimeOffset.UtcNow,
          NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(engagementManager,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Ftime");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Practice time & task records", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(assignedTaskTitle, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(assignedNarrative, new() { Exact = true })).ToBeVisibleAsync();
    var body = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(siblingTaskTitle, body, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingNarrative, body, StringComparison.Ordinal);
    Assert.DoesNotContain(periodCode, body, StringComparison.Ordinal);
    Assert.Empty(errors);
  }
}
