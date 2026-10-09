using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditFieldwork")]
public sealed class AngularAuditFieldworkParityJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true",
    ["AngularUi__CanonicalRoutes"] = "true"
  };

  [Fact]
  [Trait("CaseId", "AS-PAR-009-ANGULAR-AGGREGATE-DIFFERENCES-01")]
  public async Task AggregateDifferenceConclusionRetainsHumanDecisionAndExactSourceSnapshot()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-009-ANGULAR-AGGREGATE-DIFFERENCES-01");
    var f = host.Fixture;
    const string conclusion = "Human assessment: evaluate unadjusted QAR amounts and qualitative factors; reporting impact remains subject to partner judgment.";
    const string differenceDescription = "Synthetic unadjusted cut-off difference";
    var differenceId = Guid.NewGuid();

    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId, f.EngagementId));
      await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId,
        PbcSeed.Actor(f.Staff, "Partner"), PbcSeed.Actor(f.Reviewer, "Partner"));
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = differenceId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        AccountArea = "Revenue", DifferenceType = "KNOWN", Description = differenceDescription,
        Amount = 12_000m, Currency = "QAR", MaterialityReference = "AFS-v1",
        QualitativeConcerns = "Synthetic only", Status = AuditDifferenceStatuses.Evaluated,
        CreatedByUserId = f.Staff.Id, EvaluatedByUserId = f.Reviewer.Id,
        Evaluation = "Synthetic individual evaluation", CreatedAt = DateTimeOffset.UtcNow,
        EvaluatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var path = $"/app/engagements/{f.EngagementId:D}/audit-fieldwork";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    var aggregate = page.GetByRole(AriaRole.Region,
      new() { Name = "Aggregate differences and reporting assessment", Exact = true });
    await Assertions.Expect(aggregate).ToContainTextAsync("12,000.00");
    await Assertions.Expect(aggregate).ToContainTextAsync("QAR");
    await aggregate.GetByLabel("Required: professional aggregate conclusion and reporting impact")
      .FillAsync(conclusion);
    await aggregate.GetByRole(AriaRole.Button, new() { Name = "Record aggregate conclusion", Exact = true }).ClickAsync();
    await Assertions.Expect(aggregate.GetByText(conclusion, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(aggregate.GetByText(AuditAreaAssessmentStatuses.Submitted, new() { Exact = true }))
      .ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var saved = await db.AuditAreaAssessments.AsNoTracking().SingleAsync(x =>
        x.FirmId == f.FirmId && x.ClientId == f.ClientId && x.EngagementId == f.EngagementId &&
        x.AreaCode == AuditAreaCodes.AuditDifferences &&
        x.AssessmentKind == AuditAreaAssessmentKinds.AggregateDifferences);
      Assert.Equal(conclusion, saved.Conclusion);
      Assert.Equal(AuditAreaAssessmentStatuses.Submitted, saved.Status);
      Assert.Equal("audit-differences-aggregate.v1", saved.MethodologyReference);
      var separator = saved.InputSnapshotJson.IndexOf(':');
      Assert.True(separator > 0);
      using var snapshot = JsonDocument.Parse(saved.InputSnapshotJson[(separator + 1)..]);
      Assert.Equal("audit-difference-aggregate.v1", snapshot.RootElement.GetProperty("Version").GetString());
      var sourceDifference = Assert.Single(snapshot.RootElement.GetProperty("Differences").EnumerateArray());
      Assert.Equal(differenceId, sourceDifference.GetProperty("Id").GetGuid());
      Assert.Equal(differenceDescription, sourceDifference.GetProperty("Description").GetString());
      Assert.Equal(12_000m, sourceDifference.GetProperty("Amount").GetDecimal());
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-04-ANGULAR")]
  public async Task AdoptedProgramReviewedFieldworkAndFrozenWorkpaperSurviveReload()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "PROP-E2E-04-ANGULAR");
    var f = host.Fixture;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var fieldworkPath = $"/app/engagements/{f.EngagementId:D}/audit-fieldwork";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(fieldworkPath));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Controlled audit fieldwork", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Publish and adopt 2026.1", Exact = true }).ClickAsync();
    var program = page.GetByRole(AriaRole.Region, new() { Name = "Versioned audit program", Exact = true });
    await Assertions.Expect(program).ToContainTextAsync("AUDIT-WORKING-PROCESS v2026.1");
    await Assertions.Expect(program.GetByText("165", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Showing 9 of 165 authorized procedures", new() { Exact = true })).ToBeVisibleAsync();

    var section = page.Locator("select[name='section']");
    await section.SelectOptionAsync(new SelectOptionValue { Label = "All sections" });
    await Assertions.Expect(page.GetByText("Showing 165 of 165 authorized procedures", new() { Exact = true })).ToBeVisibleAsync();
    await section.SelectOptionAsync(new SelectOptionValue { Index = 1 });
    await Assertions.Expect(page.GetByText("Showing 9 of 165 authorized procedures", new() { Exact = true })).ToBeVisibleAsync();

    await page.GotoAsync(origin + "/app/audit/library");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Library versions", Exact = true })).ToBeVisibleAsync();
    var refreshLibrary = page.GetByRole(AriaRole.Button, new() { Name = "Refresh library", Exact = true });
    await page.GetByRole(AriaRole.Link, new() { Name = "Back to portfolio" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(refreshLibrary).ToBeFocusedAsync();
    await Assertions.Expect(refreshLibrary).ToHaveCSSAsync("outline-style", "solid");

    await page.GotoAsync(origin + fieldworkPath);
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1", new() { Exact = true })).ToBeVisibleAsync();
    Guid procedureId;
    string sourceProcedureId;
    await using (var db = host.CreateDbContext())
    {
      var procedure = await db.AuditProcedures.AsNoTracking().Where(x => x.EngagementId == f.EngagementId)
        .OrderBy(x => x.SourceSectionNumber).ThenBy(x => x.SourceProcedureId).FirstAsync();
      procedureId = procedure.Id;
      sourceProcedureId = procedure.SourceProcedureId;
    }
    var procedureRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = sourceProcedureId });
    await procedureRow.GetByRole(AriaRole.Button, new() { Name = "Applicable", Exact = true }).ClickAsync();
    await Assertions.Expect(procedureRow).ToContainTextAsync("APPLICABLE");

    Guid scheduleId;
    Guid selectionId;
    Guid itemTestId;
    Guid findingId;
    Guid workpaperId;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Reviewer");
    const string workPerformed = "Agreed ROW-001 to the synthetic source schedule.";
    const string workpaperConclusion = "The selected item agrees; the separately recorded finding remains open.";
    await using (var db = host.CreateDbContext())
    {
      await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId, staff, PbcSeed.Actor(f.Reviewer, "Partner"));
      var sourceHash = Hashing.Sha256Hex("synthetic-audit-source-schedule");
      var schedule = await AuditFieldworkService.CreateScheduleAsync(db, staff, new CreateScheduleRequest(
        f.EngagementId, "SYNTHETIC_GL", "TEST-ENTITY", "E2E-SOURCE-001", null,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "DEBIT_MINUS_CREDIT", sourceHash, 125m,
        [new ScheduleRowInput("ROW-001", 1, "1000", "Synthetic cash item", 125m, "QAR",
          new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 30), null, null, "{}") ]));
      Assert.True(schedule.Succeeded, schedule.Message);
      scheduleId = schedule.Value!.ScheduleId;
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
        new ReviewScheduleRequest(scheduleId, "Complete synthetic source schedule", true))).Succeeded);

      var selection = await AuditFieldworkService.CreateSelectionAsync(db, staff, new CreateSelectionRequest(
        f.EngagementId, procedureId, scheduleId, null, "TARGETED", "Synthetic high-value selection",
        [new SelectionItemInput("ROW-001", 125m, "QAR", "Synthetic test item")]));
      Assert.True(selection.Succeeded, selection.Message);
      selectionId = selection.Value!.SelectionId;
      Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
        new ReviewSelectionRequest(selectionId, AuditSelectionStatuses.Reviewed, "Independent selection review."))).Succeeded);

      var selectedItemId = await db.AuditSelectionItems.AsNoTracking().Where(x => x.SelectionId == selectionId)
        .Select(x => x.Id).SingleAsync();
      var itemTest = await AuditFieldworkService.RecordItemTestAsync(db, staff, new RecordItemTestRequest(
        selectedItemId, "Agreed synthetic row to source evidence.", ["synthetic-evidence-001"],
        AuditItemTestResults.Pass, null, null, null));
      Assert.True(itemTest.Succeeded, itemTest.Message);
      itemTestId = itemTest.Value!.AuditItemTestId;
      Assert.True((await AuditFieldworkService.ReviewItemTestAsync(db, reviewer,
        new ReviewItemTestRequest(itemTestId, AuditItemTestReviewDecisions.Reviewed, "Independent result review."))).Succeeded);

      var finding = await AuditPlanningService.CreateFindingAsync(db, staff, new CreateFindingRequest(
        f.EngagementId, "Synthetic cut-off exception", "Synthetic test exception retained for review.",
        false, 125m, null));
      Assert.True(finding.Succeeded, finding.Message);
      findingId = finding.Value!.FindingId;
      var workpaper = await AuditPlanningService.CreateWorkpaperAsync(db, staff, new CreateWorkpaperRequest(
        f.EngagementId, "E2E-04", "Synthetic source test", "Test scoped source evidence",
        "E2E-TEMPLATE-v1", procedureId, "Inspect selected source row and resolve exception."));
      Assert.True(workpaper.Succeeded, workpaper.Message);
      workpaperId = workpaper.Value!.WorkpaperId;
      var draft = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(), workPerformed, workpaperConclusion));
      Assert.True(draft.Succeeded, draft.Message);
    }

    var workpaperPath = $"/app/audit/workpapers/{workpaperId:D}";
    await page.GotoAsync(origin + workpaperPath);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Workpaper: Synthetic source test", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByLabel("Work performed", new() { Exact = true })).ToHaveValueAsync(workPerformed);
    await Assertions.Expect(page.GetByLabel("Conclusion", new() { Exact = true })).ToHaveValueAsync(workpaperConclusion);

    await using (var verifyDraft = host.CreateDbContext())
    {
      var saved = await verifyDraft.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal(workPerformed, saved.WorkPerformed);
      Assert.Equal(workpaperConclusion, saved.Conclusion);
    }

    await page.EvaluateAsync("() => localStorage.clear()");
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByLabel("Work performed", new() { Exact = true })).ToHaveValueAsync(workPerformed);
    await Assertions.Expect(page.GetByLabel("Conclusion", new() { Exact = true })).ToHaveValueAsync(workpaperConclusion);
    await page.GetByRole(AriaRole.Button, new() { Name = "Submit for review", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The submission was recorded.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("1 frozen submissions", new() { Exact = false })).ToBeVisibleAsync();

    await page.GotoAsync(origin + $"/app/findings/{findingId:D}");
    await Assertions.Expect(page.GetByText("Synthetic cut-off exception", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Synthetic test exception retained for review.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var verify = host.CreateDbContext())
    {
      Assert.Equal(AuditScheduleStatuses.Approved,
        await verify.AuditSchedules.Where(x => x.Id == scheduleId).Select(x => x.Status).SingleAsync());
      Assert.Equal(AuditSelectionStatuses.Reviewed,
        await verify.AuditSelections.Where(x => x.Id == selectionId).Select(x => x.Status).SingleAsync());
      Assert.Single(await verify.AuditItemTestReviews.Where(x => x.AuditItemTestId == itemTestId).ToListAsync());
      Assert.Single(await verify.WorkpaperSubmissions.Where(x => x.WorkpaperId == workpaperId).ToListAsync());
      Assert.Equal(AuditApplicabilityStatuses.Applicable,
        await verify.AuditProcedures.Where(x => x.Id == procedureId).Select(x => x.ApplicabilityStatus).SingleAsync());
    }
    Assert.Empty(errors);
  }
}
