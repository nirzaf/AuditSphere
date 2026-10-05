using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.Json;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularAccountingTaskOwnerScopeJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-ACCT-TASK-OWNER-01")]
  public async Task AccountingWorkspaceShowsOnlyOwnersInAssignedEngagementAndClearsAfterRevocation()
  {
    const string caseId = "AS-PAR-002-ANG-ACCT-TASK-OWNER-01";
    const string assignedOwnerName = "SYN-PAR-002-ASSIGNED-TASK-OWNER";
    const string siblingOwnerName = "SYN-PAR-002-SIBLING-TASK-OWNER";
    const string assignedTaskTitle = "SYN-PAR-002-PRIVATE-ASSIGNED-TASK-TITLE";
    const string siblingTaskTitle = "SYN-PAR-002-PRIVATE-SIBLING-TASK-TITLE";
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: caseId);
    var f = host.Fixture;
    var scopedUser = PbcSeed.User(f.FirmId, "Staff");
    var assignedOwner = PbcSeed.User(f.FirmId, "Staff");
    assignedOwner.DisplayName = assignedOwnerName;
    var siblingOwner = PbcSeed.User(f.FirmId, "Staff");
    siblingOwner.DisplayName = siblingOwnerName;
    var siblingEngagementId = Guid.NewGuid();
    var periodId = Guid.NewGuid();

    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(scopedUser, assignedOwner, siblingOwner);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, scopedUser, "AccountingPreparer", f.ClientId, f.EngagementId));
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "SYN-PAR-002-TASK-OWNER-PERIOD",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Basis = "STATUTORY",
        Currency = "QAR", CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.WorkTasks.AddRange(
        new WorkTask
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          ReportingPeriodId = periodId, Title = assignedTaskTitle, AssigneeUserId = assignedOwner.Id,
          DueDate = new DateOnly(2026, 9, 30), CreatedAt = DateTimeOffset.UtcNow
        },
        new WorkTask
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = siblingEngagementId,
          ReportingPeriodId = periodId, Title = siblingTaskTitle, AssigneeUserId = siblingOwner.Id,
          DueDate = new DateOnly(2026, 9, 1), CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(scopedUser,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Task owners in your assigned engagements", Exact = true })).ToBeVisibleAsync();
    var visible = await page.Locator("main").InnerTextAsync();
    Assert.Contains(assignedOwnerName, visible, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingOwnerName, visible, StringComparison.Ordinal);
    Assert.DoesNotContain(assignedTaskTitle, visible, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingTaskTitle, visible, StringComparison.Ordinal);

    await using (var summaryResponse = await context.APIRequest.GetAsync(origin + "/api/ui/accounting/task-owners"))
    {
      Assert.Equal(200, summaryResponse.Status);
      using var summary = JsonDocument.Parse(await summaryResponse.TextAsync());
      var json = summary.RootElement.GetRawText();
      Assert.Contains(assignedOwnerName, json, StringComparison.Ordinal);
      Assert.DoesNotContain(siblingOwnerName, json, StringComparison.Ordinal);
      Assert.DoesNotContain(assignedOwner.Id.ToString("D"), json, StringComparison.Ordinal);
      Assert.DoesNotContain(assignedTaskTitle, json, StringComparison.Ordinal);
    }

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == f.FirmId && x.UserId == scopedUser.Id &&
        x.Role == "AccountingPreparer" && x.EngagementId == f.EngagementId);
      grant.RevokedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync();
    }
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Portal unavailable. A current client assignment is required.");
    visible = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(assignedOwnerName, visible, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingOwnerName, visible, StringComparison.Ordinal);
    await using var revokedSummary = await context.APIRequest.GetAsync(origin + "/api/ui/accounting/task-owners");
    Assert.Equal(403, revokedSummary.Status);
    Assert.Empty(errors);
  }
}
