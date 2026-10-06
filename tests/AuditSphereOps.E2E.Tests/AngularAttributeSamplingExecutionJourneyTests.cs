using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditFieldwork")]
public sealed class AngularAttributeSamplingExecutionJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true",
    ["AngularUi__CanonicalRoutes"] = "true"
  };

  [Fact]
  [Trait("CaseId", "AS-COMP-17-ANGULAR-SAMPLING-EXECUTION")]
  public async Task ExactAttributeSelectionIsIndependentlyReviewedExecutedAndReviewedAgain()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-COMP-17-ANGULAR-SAMPLING-EXECUTION");
    var f = host.Fixture;
    var now = DateTimeOffset.UtcNow;
    var procedureId = Guid.NewGuid();
    var scheduleId = Guid.NewGuid();

    await using (var db = host.CreateDbContext())
    {
      db.AuditProgramVersions.Add(new AuditProgramVersion
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ProgramCode = "SYNTHETIC-AUDIT",
        Version = "AS-COMP-17", SourceHash = Digest("AS-COMP-17 synthetic program"),
        Status = AuditProgramStatuses.Published, CreatedByUserId = f.Reviewer.Id,
        ApprovedByUserId = f.Reviewer.Id, CreatedAt = now, ApprovedAt = now
      });
      var programVersionId = db.AuditProgramVersions.Local.Single().Id;
      var adoptedProgramId = Guid.NewGuid();
      db.EngagementAuditPrograms.Add(new EngagementAuditProgram
      {
        Id = adoptedProgramId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ProgramVersionId = programVersionId, Status = EngagementAuditProgramStatuses.Adopted,
        AdoptedByUserId = f.Reviewer.Id, AdoptedAt = now
      });
      db.AuditProcedures.Add(new AuditProcedure
      {
        Id = procedureId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        EngagementProgramId = adoptedProgramId, SourceProcedureId = "AS-COMP-17-01",
        SourceSectionNumber = 17, SourceSectionTitle = "Completion sampling",
        SourceWording = "Inspect the selected schedule rows and record evidence.",
        Title = "Inspect attribute-stratified schedule rows", ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
        Status = AuditProcedureStatuses.Planned, CreatedAt = now
      });
      await db.SaveChangesAsync();

      var rows = new[]
      {
        new ScheduleRowInput("SAMP-001", 1, "4000", "Synthetic revenue item 1", 100m, "QAR",
          new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10), null, null, "{}"),
        new ScheduleRowInput("SAMP-002", 2, "4000", "Synthetic revenue item 2", 200m, "QAR",
          new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 20), null, null, "{}"),
        new ScheduleRowInput("SAMP-003", 3, "4010", "Synthetic revenue item 3", 300m, "QAR",
          new DateOnly(2026, 2, 10), new DateOnly(2026, 2, 10), null, null, "{}"),
        new ScheduleRowInput("SAMP-004", 4, "4010", "Synthetic revenue item 4", 400m, "QAR",
          new DateOnly(2026, 2, 20), new DateOnly(2026, 2, 20), null, null, "{}")
      };
      var created = await AuditFieldworkService.CreateScheduleAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
        new CreateScheduleRequest(f.EngagementId, "SYNTHETIC_SALES", "TEST-ENTITY", "AS-COMP-17-SOURCE",
          now, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "DEBIT_POSITIVE",
          Digest("AS-COMP-17 synthetic approved schedule"), 1000m, rows));
      Assert.True(created.Succeeded, created.Message);
      scheduleId = created.Value!.ScheduleId;
      var approved = await AuditFieldworkService.ReviewScheduleAsync(db, PbcSeed.Actor(f.Reviewer, "Reviewer"),
        new ReviewScheduleRequest(scheduleId, "Synthetic source completeness independently checked.", true));
      Assert.True(approved.Succeeded, approved.Message);
    }

    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var errors = new List<string>();
    var staffPage = await browser.NewPageAsync();
    staffPage.PageError += (_, error) => errors.Add($"staff: {error}");
    var route = $"/app/engagements/{f.EngagementId:D}/audit-fieldwork";
    await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading,
      new() { Name = "Controlled audit fieldwork", Exact = true })).ToBeVisibleAsync();
    await staffPage.GetByRole(AriaRole.Tab, new() { Name = "Sampling", Exact = true }).ClickAsync();
    await staffPage.Locator("select[name='sp']").SelectOptionAsync(procedureId.ToString());
    await staffPage.Locator("select[name='ss']").SelectOptionAsync(scheduleId.ToString());
    await staffPage.Locator("select[name='sm']").SelectOptionAsync(AuditSamplingMethods.AttributeStrata);
    await staffPage.GetByLabel("Sample size", new() { Exact = true }).FillAsync("2");
    await staffPage.GetByLabel("Seed", new() { Exact = true }).FillAsync("17017");
    await staffPage.GetByLabel("Account code", new() { Exact = true }).CheckAsync();
    await staffPage.Locator("input[name='sr']").FillAsync("Select and inspect one row from each account stratum.");
    await staffPage.GetByRole(AriaRole.Button, new() { Name = "Preview selection", Exact = true }).ClickAsync();
    var preview = staffPage.GetByRole(AriaRole.Region, new() { Name = "Exact selection preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync("2 of 4 rows selected");
    await Assertions.Expect(preview).ToContainTextAsync("4000");
    await Assertions.Expect(preview).ToContainTextAsync("4010");
    await Assertions.Expect(preview.GetByRole(AriaRole.Table,
      new() { Name = "Exact selected sampling rows", Exact = true }).GetByRole(AriaRole.Row)).ToHaveCountAsync(3);
    await preview.GetByRole(AriaRole.Button, new() { Name = "Record this exact sample", Exact = true }).ClickAsync();
    var selectionPanel = staffPage.GetByRole(AriaRole.Table, new() { Name = "Sampling calculation log", Exact = true });
    await Assertions.Expect(selectionPanel).ToContainTextAsync("SUBMITTED");
    var sampleSet = staffPage.Locator("section[aria-labelledby='sample-execution-heading']");
    await Assertions.Expect(sampleSet).ToContainTextAsync("Execution is locked until an independent reviewer approves this exact sample.");
    await Assertions.Expect(sampleSet.GetByRole(AriaRole.Button,
      new() { Name = "Record item test", Exact = true })).ToHaveCountAsync(0);

    Guid selectionId;
    Guid selectedItemId;
    await using (var db = host.CreateDbContext())
    {
      var run = await db.AuditSamplingRuns.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId && x.ScheduleId == scheduleId);
      selectionId = run.SelectionId;
      selectedItemId = await db.AuditSelectionItems.AsNoTracking().Where(x => x.SelectionId == selectionId)
        .OrderBy(x => x.StableRowId).Select(x => x.Id).FirstAsync();
      Assert.Equal(AuditSamplingMethods.AttributeStrata, run.Method);
      Assert.Equal("SCHEDULE_SOURCE_LINE_THEN_STABLE_ROW_ID", run.OrderingPolicy);
      Assert.Equal(2, await db.AuditSelectionItems.CountAsync(x => x.SelectionId == selectionId));
      var reviewerWorkspace = await AuditFieldworkWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Reviewer, "Reviewer"), f.EngagementId);
      Assert.True(reviewerWorkspace.Succeeded, reviewerWorkspace.Message);
      Assert.True(reviewerWorkspace.Value!.CanReviewSelections);
      Assert.True(Assert.Single(reviewerWorkspace.Value.SamplingRuns).CanReviewSelection);
    }

    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => errors.Add($"reviewer: {error}");
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await reviewerPage.GetByRole(AriaRole.Tab, new() { Name = "Sampling", Exact = true }).ClickAsync();
    var workspaceJson = await reviewerPage.EvaluateAsync<string>(
      $"async () => await (await fetch('/api/ui/engagements/{f.EngagementId:D}/fieldwork')).text()");
    using var workspaceDocument = System.Text.Json.JsonDocument.Parse(workspaceJson);
    var projectedRun = Assert.Single(workspaceDocument.RootElement.GetProperty("samplingRuns").EnumerateArray());
    Assert.Equal(selectionId, projectedRun.GetProperty("selectionId").GetGuid());
    Assert.True(projectedRun.GetProperty("canReviewSelection").GetBoolean(), workspaceJson);
    var reviewerBody = await reviewerPage.Locator("body").InnerTextAsync();
    Assert.True(reviewerBody.Contains("SUBMITTED", StringComparison.Ordinal), reviewerBody);
    var reviewInput = reviewerPage.GetByLabel("Review note", new() { Exact = true });
    Assert.Equal(1, await reviewInput.CountAsync());
    await reviewInput
      .FillAsync("The exact identities, strata and allocation were independently reviewed.");
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Approve for execution", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.Locator("section[aria-labelledby='sample-execution-heading']"))
      .ToContainTextAsync("REVIEWED");

    await staffPage.ReloadAsync();
    await staffPage.GetByRole(AriaRole.Tab, new() { Name = "Sampling", Exact = true }).ClickAsync();
    var staffWorkspaceJson = await staffPage.EvaluateAsync<string>(
      $"async () => await (await fetch('/api/ui/engagements/{f.EngagementId:D}/fieldwork')).text()");
    using var staffWorkspaceDocument = System.Text.Json.JsonDocument.Parse(staffWorkspaceJson);
    Assert.True(staffWorkspaceDocument.RootElement.GetProperty("canManageFieldwork").GetBoolean(), staffWorkspaceJson);
    await staffPage.GetByRole(AriaRole.Button, new() { Name = "Open sample set", Exact = true }).ClickAsync();
    var staffSampleJson = await staffPage.EvaluateAsync<string>(
      $"async () => await (await fetch('/api/ui/selections/{selectionId:D}/sample-set?page=1&pageSize=100')).text()");
    using var staffSampleDocument = System.Text.Json.JsonDocument.Parse(staffSampleJson);
    Assert.Equal("REVIEWED", staffSampleDocument.RootElement.GetProperty("status").GetString());
    Assert.Equal(2, staffSampleDocument.RootElement.GetProperty("items").GetArrayLength());
    await Assertions.Expect(staffPage.Locator("[data-sample-item]")).ToHaveCountAsync(2);
    var firstItem = staffPage.Locator("[data-sample-item]").First;
    var firstItemHtml = await firstItem.EvaluateAsync<string>("element => element.outerHTML");
    Assert.Contains("Record item test", firstItemHtml);
    await firstItem.Locator($"select[name='result-{selectedItemId:D}']").SelectOptionAsync("EXCEPTION");
    await firstItem.Locator($"textarea[name='work-{selectedItemId:D}']")
      .FillAsync("Agreed this selected transaction to the synthetic invoice and approval record.");
    await firstItem.Locator($"textarea[name='evidence-{selectedItemId:D}']")
      .FillAsync("invoice:AS-COMP-17-001\napproval:AS-COMP-17-001");
    await firstItem.Locator($"input[name='amount-{selectedItemId:D}']").FillAsync("25.00");
    await firstItem.Locator($"textarea[name='contradiction-{selectedItemId:D}']")
      .FillAsync("Invoice date falls after the recorded transaction date.");
    await firstItem.Locator($"textarea[name='followup-{selectedItemId:D}']")
      .FillAsync("Resolve the date discrepancy with the engagement reviewer.");
    await firstItem.GetByRole(AriaRole.Button, new() { Name = "Record item test", Exact = true }).ClickAsync();
    await Assertions.Expect(firstItem).ToContainTextAsync("EXCEPTION");
    await Assertions.Expect(firstItem).ToContainTextAsync("25.00");
    await Assertions.Expect(firstItem).ToContainTextAsync("Resolve the date discrepancy");

    await reviewerPage.ReloadAsync();
    await reviewerPage.GetByRole(AriaRole.Tab, new() { Name = "Sampling", Exact = true }).ClickAsync();
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Open sample set", Exact = true }).ClickAsync();
    var reviewerItem = reviewerPage.Locator("[data-sample-item]").First;
    await Assertions.Expect(reviewerItem.GetByRole(AriaRole.Button,
      new() { Name = "Review item test", Exact = true })).ToBeVisibleAsync();
    await reviewerItem.GetByLabel("Reviewer note", new() { Exact = true })
      .FillAsync("Exception evidence and required follow-up were independently reviewed.");
    await reviewerItem.GetByRole(AriaRole.Button, new() { Name = "Review item test", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerItem).ToContainTextAsync("independently reviewed");

    await using (var db = host.CreateDbContext())
    {
      var selection = await db.AuditSelections.AsNoTracking().SingleAsync(x => x.Id == selectionId);
      Assert.Equal(AuditSelectionStatuses.Reviewed, selection.Status);
      var test = await db.AuditItemTests.AsNoTracking().SingleAsync(x => x.SelectionItemId == selectedItemId);
      Assert.Equal(AuditItemTestResults.Exception, test.Result);
      Assert.Equal(25m, test.ExceptionAmount);
      Assert.Contains("Resolve the date discrepancy", test.FollowUp ?? string.Empty);
      Assert.Equal(f.Staff.Id, test.TestedByUserId);
      var testReview = await db.AuditItemTestReviews.AsNoTracking().SingleAsync(x => x.AuditItemTestId == test.Id);
      Assert.Equal(f.Reviewer.Id, testReview.ReviewerUserId);
      Assert.Equal(AuditItemTestReviewDecisions.Reviewed, testReview.Decision);
      Assert.Equal(1, await db.AuditSamplingRuns.CountAsync(x => x.SelectionId == selectionId));
      Assert.Equal(2, await db.AuditSelectionItems.CountAsync(x => x.SelectionId == selectionId));
    }
    Assert.Empty(errors);
  }

  private static string Digest(string input) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
