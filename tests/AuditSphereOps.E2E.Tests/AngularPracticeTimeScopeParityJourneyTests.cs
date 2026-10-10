using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularPracticeTimeScopeParityJourneyTests
{
  [Fact]
  [Trait("CaseId", "STE-GAP-009-RATE-FSLI-BROWSER")]
  public async Task SeparateAdministratorApprovesSteRateAndMappedTaskPinsItToBillableTime()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "STE-GAP-009-RATE-FSLI-BROWSER");
    var f = host.Fixture;
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.NewGuid();
    var mappingId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      var safety = await db.ClientSafetyStates.AsNoTracking().SingleAsync(x => x.Id == f.ClientId);
      db.Users.Add(manager);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, manager, "Manager"));
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        SourceKind = "Raw", Currency = "QAR", Balanced = true, ControlTotal = 0m,
        ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed,
        LegalEntityKey = "SYNTHETIC-ENTITY", RawFileSha256Hex = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex("synthetic-raw-tb"),
        NormalizedDatasetDigest = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex("synthetic-normalized-tb"),
        ImportedAt = now, ImportedByUserId = f.Staff.Id
      });
      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        DatasetId = datasetId, Version = 1, Generation = safety.InputGeneration,
        TaxonomyVersion = "synthetic-fsli-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = AccountingPackageStates.MappingApproved, CreatedByUserId = f.Staff.Id,
        ApprovedByUserId = f.Reviewer.Id, ApprovedAt = now, CreatedAt = now
      });
      db.MappingAllocations.Add(new MappingAllocation
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        MappingVersionId = mappingId, SourceAccountCode = "1000", DestinationCode = "CASH",
        StatementSection = "ASSETS", Fraction = 1m, Rationale = "Synthetic approved FSLI mapping", CreatedAt = now
      });
      await db.SaveChangesAsync();
    }

    var managerOrigin = await host.StartApiForIdentityAsync(manager,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var managerContext = await browser.NewContextAsync();
    var managerPage = await managerContext.NewPageAsync();
    var errors = new List<string>();
    managerPage.PageError += (_, error) => errors.Add(error);
    await managerPage.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Frate-cards");
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Charge-out rates", Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Create missing STE baseline drafts", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.Locator("audit-command-message"))
      .ToContainTextAsync("STE baseline drafts were created. Each draft needs approval by a different authorized approver.");
    var draftRow = managerPage.Locator("section[aria-labelledby='slots-heading'] tbody tr")
      .Filter(new() { HasText = "Audit Manager" });
    await Assertions.Expect(draftRow).ToContainTextAsync("draft");

    var adminOrigin = await host.StartApiForIdentityAsync(f.Admin,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    await using var adminContext = await browser.NewContextAsync();
    var adminPage = await adminContext.NewPageAsync();
    adminPage.PageError += (_, error) => errors.Add(error);
    await adminPage.GotoAsync(adminOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Frate-cards");
    await Assertions.Expect(adminPage.GetByRole(AriaRole.Heading,
      new() { Name = "Charge-out rates", Exact = true })).ToBeVisibleAsync();
    var approvalRow = adminPage.Locator("section[aria-labelledby='slots-heading'] tbody tr")
      .Filter(new() { HasText = "Audit Manager" });
    await approvalRow.GetByRole(AriaRole.Button, new() { Name = "Approve draft", Exact = true }).ClickAsync();
    await Assertions.Expect(approvalRow).ToContainTextAsync("750.00");
    await Assertions.Expect(approvalRow.GetByRole(AriaRole.Button,
      new() { Name = "Approve draft", Exact = true })).ToHaveCountAsync(0);

    await managerPage.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Ftime");
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Practice time & task records", Exact = true })).ToBeVisibleAsync();
    const string taskTitle = "SYN-FSLI-MAPPED-TIME-TASK";
    await managerPage.GetByLabel("Task title", new() { Exact = true }).FillAsync(taskTitle);
    await managerPage.GetByLabel("Engagement (optional)").SelectOptionAsync(f.EngagementId.ToString());
    var fsli = managerPage.GetByLabel("FSLI mapping (optional)");
    await Assertions.Expect(fsli.Locator("option[value='CASH']")).ToBeAttachedAsync();
    await fsli.SelectOptionAsync("CASH");
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Create task", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText("Work task created.", new() { Exact = true })).ToBeVisibleAsync();

    Guid taskId;
    await using (var db = host.CreateDbContext())
    {
      var task = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Title == taskTitle);
      taskId = task.Id;
      Assert.Equal(mappingId, task.MappingVersionId);
      Assert.Equal("CASH", task.FsliCode);
      var rate = await db.RateCardVersions.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId &&
        x.Role == "Audit Manager" && x.Activity == "General" && x.Status == PracticeTimeStates.RateApproved);
      Assert.Equal(750m, rate.RatePerHour);
      Assert.Equal(f.Admin.Id, rate.ApprovedByUserId);
      Assert.NotEqual(manager.Id, rate.ApprovedByUserId);
    }

    await managerPage.Locator("select[name='task']").SelectOptionAsync(taskId.ToString());
    await managerPage.GetByLabel("Date", new() { Exact = true }).FillAsync("2026-10-09");
    await managerPage.GetByLabel("Duration (minutes)", new() { Exact = true }).FillAsync("60");
    await managerPage.GetByLabel("Role", new() { Exact = true }).FillAsync("Audit Manager");
    await managerPage.GetByLabel("Activity", new() { Exact = true }).FillAsync("General");
    await managerPage.GetByLabel("Billable to client (optional)", new() { Exact = true }).CheckAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Save time draft", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Table,
      new() { Name = "My recorded time entries", Exact = true })).ToContainTextAsync("CASH");
    await using (var db = host.CreateDbContext())
    {
      var entry = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.TaskId == taskId);
      Assert.Equal(mappingId, entry.MappingVersionId);
      Assert.Equal("CASH", entry.FsliCode);
      Assert.Equal(PracticeTimeStates.Billable, entry.BillableClassification);
      Assert.Equal(750m, entry.RatePerHour);
      Assert.NotNull(entry.RateCardVersionId);
      Assert.Equal(f.Admin.Id, await db.RateCardVersions.Where(x => x.Id == entry.RateCardVersionId)
        .Select(x => x.ApprovedByUserId).SingleAsync());
    }
    Assert.Empty(errors);
  }

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
      var mappingId = Guid.NewGuid();
      var datasetId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;
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
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        SourceKind = "Raw", Currency = "QAR", Balanced = true, ControlTotal = 0m,
        ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Loading,
        NormalizedDatasetDigest = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(datasetId.ToString()),
        ImportedAt = now, ImportedByUserId = f.Staff.Id
      });
      await db.SaveChangesAsync();
      await db.TrialBalanceDatasets.Where(x => x.Id == datasetId)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        DatasetId = datasetId, TaxonomyVersion = "time-e2e-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = AccountingPackageStates.MappingApproved, CreatedByUserId = f.Staff.Id,
        ApprovedByUserId = f.Reviewer.Id, ApprovedAt = now, CreatedAt = now
      });
      db.MappingAllocations.Add(new MappingAllocation
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        MappingVersionId = mappingId, SourceAccountCode = "1000", DestinationCode = "CASH",
        StatementSection = "ASSETS", Fraction = 1m, Rationale = "Synthetic approved mapping", CreatedAt = now
      });
      db.WorkTasks.AddRange(
        new WorkTask
        {
          Id = assignedTaskId, FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = f.EngagementId, Title = assignedTaskTitle, MappingVersionId = mappingId,
          FsliCode = "CASH", CreatedAt = DateTimeOffset.UtcNow
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
          MappingVersionId = mappingId, FsliCode = "CASH",
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
