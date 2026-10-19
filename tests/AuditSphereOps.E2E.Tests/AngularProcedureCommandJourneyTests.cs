using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditFieldwork")]
public sealed class AngularProcedureCommandJourneyTests
{
  private const string ProcedureAssentLabel = "I reviewed this exact procedure, work, evidence and basis.";

  private static async Task TabToAsync(IPage page, ILocator target)
  {
    var focusableCount = await page.Locator(
      "a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex='-1'])")
      .CountAsync();
    for (var attempt = 0; attempt <= focusableCount; attempt++)
    {
      if (await target.EvaluateAsync<bool>("element => element === document.activeElement")) return;
      await page.Keyboard.PressAsync("Tab");
    }

    if (!await target.EvaluateAsync<bool>("element => element === document.activeElement"))
      Assert.Fail("The control could not be reached through the page's normal Tab order. Target: " +
        await target.EvaluateAsync<string>("element => element.outerHTML") + " Active: " +
        await page.EvaluateAsync<string>("() => document.activeElement?.outerHTML ?? ''"));
  }

  private static ILocator QueueTaskCards(IPage page) => page.Locator("article.task-card");

  private static async Task OpenQueueViewAsync(IPage page, string origin, string view)
  {
    await page.GotoAsync(origin + "/app/work");
    await page.GetByRole(AriaRole.Navigation, new() { Name = "Work queue views", Exact = true })
      .GetByRole(AriaRole.Button, new() { Name = view, Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Navigation, new() { Name = "Work queue display", Exact = true })
      .GetByRole(AriaRole.Button, new() { Name = "Board", Exact = true }).ClickAsync();
  }

  [Fact]
  [Trait("CaseId", "AS-AWP-PROCEDURE-COMMAND-RECOVERY-01")]
  public async Task CashAndBankProcedureRecoversExactSubmissionAndIndependentReviewerCanReturnIt()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-AWP-PROCEDURE-COMMAND-RECOVERY-01");
    var f = host.Fixture;
    var preparer = PbcSeed.Actor(f.Staff, "Partner");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Partner");
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Partner"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      await db.SaveChangesAsync();

      var publication = await AuditProgramTestHelpers.PublishAsync(db, preparer,
        new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(publication.Succeeded, publication.Message);
      await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId, preparer, reviewer);
    }

    var angular = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true",
      ["WorkflowExperience__Enabled"] = "true"
    };
    var origin = await host.StartApiForIdentityAsync(f.Staff, angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var preparerContext = await browser.NewContextAsync();
    var preparerPage = await preparerContext.NewPageAsync();
    var pageErrors = new List<string>();
    preparerPage.PageError += (_, error) => pageErrors.Add(error);
    var path = $"/app/engagements/{f.EngagementId:D}/audit-fieldwork";
    await preparerPage.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Button,
      new() { Name = "Adopt exact published version 2026.1", Exact = true })).ToBeVisibleAsync();
    await preparerPage.GetByRole(AriaRole.Button,
      new() { Name = "Adopt exact published version 2026.1", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText("AUDIT-WORKING-PROCESS v2026.1", new() { Exact = true })).ToBeVisibleAsync();

    var section = preparerPage.Locator("select[name='section']");
    var cashBankSection = section.Locator("option").Filter(new() { HasText = "Cash & Bank" });
    await Assertions.Expect(cashBankSection).ToHaveCountAsync(1);
    await section.SelectOptionAsync(new SelectOptionValue { Label = await cashBankSection.InnerTextAsync() });
    var procedureRow = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "AWP-02-01" });
    await Assertions.Expect(procedureRow).ToContainTextAsync("Cash & Bank");
    await procedureRow.GetByRole(AriaRole.Button, new() { Name = "Applicable", Exact = true }).ClickAsync();
    await Assertions.Expect(procedureRow).ToContainTextAsync(AuditApplicabilityStatuses.Applicable);
    await procedureRow.GetByRole(AriaRole.Button, new() { Name = "Open procedure", Exact = true }).ClickAsync();

    var execution = preparerPage.GetByRole(AriaRole.Region, new() { Name = "Procedure execution and review", Exact = true });
    await Assertions.Expect(execution).ToContainTextAsync("AWP-02-01");
    await execution.GetByRole(AriaRole.Textbox, new() { Name = "Work performed", Exact = true })
      .FillAsync("Agreed the synthetic year-end bank reconciliation to the listed bank accounts.");
    await execution.GetByRole(AriaRole.Textbox, new() { Name = "Conclusion", Exact = true })
      .FillAsync("The synthetic reconciliation agrees to the supporting statement.");
    await execution.GetByRole(AriaRole.Textbox, new() { Name = "Manual reference", Exact = true })
      .FillAsync("BANK-REC-SYNTHETIC-01");
    await execution.GetByRole(AriaRole.Textbox, new() { Name = "What was observed", Exact = true })
      .FillAsync("The closing balance and reconciling items agree to the synthetic schedule.");
    var prepareSubmission = execution.GetByRole(AriaRole.Button,
      new() { Name = "Preview exact submission", Exact = true });
    await TabToAsync(preparerPage, prepareSubmission);
    await Assertions.Expect(prepareSubmission).ToBeFocusedAsync();
    await preparerPage.Keyboard.PressAsync("Enter");
    var preview = execution.GetByRole(AriaRole.Region, new() { Name = "Exact command preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync("Submit this exact work and evidence snapshot.");
    var submissionAssent = preview.GetByRole(AriaRole.Checkbox,
      new() { Name = ProcedureAssentLabel, Exact = true });
    var recordSubmission = preview.GetByRole(AriaRole.Button,
      new() { Name = "Record exact outcome", Exact = true });
    await Assertions.Expect(submissionAssent).Not.ToBeCheckedAsync();
    await Assertions.Expect(recordSubmission).ToBeDisabledAsync();

    Guid requestId = Guid.Empty;
    string? requestHash = null;
    var actionUrl = $"**/api/ui/procedures/*/commands/actions";
    await preparerPage.RouteAsync(actionUrl, async route =>
    {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      using var body = JsonDocument.Parse(route.Request.PostData!);
      requestId = body.RootElement.GetProperty("requestId").GetGuid();
      requestHash = body.RootElement.GetProperty("expectedRequestHash").GetString();
      var committed = await route.FetchAsync();
      Assert.Equal(200, committed.Status);
      await route.AbortAsync("failed");
    });
    await TabToAsync(preparerPage, submissionAssent);
    await Assertions.Expect(submissionAssent).ToBeFocusedAsync();
    await preparerPage.Keyboard.PressAsync("Space");
    await Assertions.Expect(submissionAssent).ToBeCheckedAsync();
    await Assertions.Expect(recordSubmission).ToBeEnabledAsync();
    await TabToAsync(preparerPage, recordSubmission);
    await Assertions.Expect(recordSubmission).ToBeFocusedAsync();
    await preparerPage.Keyboard.PressAsync("Enter");
    var recover = execution.GetByRole(AriaRole.Button, new() { Name = "Check original outcome", Exact = true });
    await Assertions.Expect(recover).ToBeVisibleAsync();
    await Assertions.Expect(execution.GetByRole(AriaRole.Alert).Filter(new() { HasText = "The outcome is unknown." }))
      .ToContainTextAsync("do not resend a new identity.");
    Assert.NotEqual(Guid.Empty, requestId);
    Assert.Matches("^[0-9a-f]{64}$", requestHash!);
    await recover.ClickAsync();
    await Assertions.Expect(execution.Locator("p[role='status']").Filter(new() { HasText = "Recorded SUBMIT_RESULT" }))
      .ToBeVisibleAsync();
    await preparerPage.UnrouteAsync(actionUrl);

    Guid procedureId;
    await using (var db = host.CreateDbContext())
    {
      procedureId = await db.AuditProcedures.AsNoTracking().Where(x => x.EngagementId == f.EngagementId &&
          x.SourceProcedureId == "AWP-02-01").Select(x => x.Id).SingleAsync();
    }

    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, angular);
    await using var reviewerContext = await browser.NewContextAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    reviewerPage.PageError += (_, error) => pageErrors.Add(error);
    var fieldworkPath = $"/api/ui/engagements/{f.EngagementId:D}/fieldwork";
    var (reviewerFieldworkStatus, reviewerFieldworkBody) = await PlaywrightResponseCapture.RunAsync(reviewerPage,
      request => request.Method == "GET" && new Uri(request.Url).AbsolutePath.Equals(fieldworkPath, StringComparison.OrdinalIgnoreCase),
      async () => await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path)));
    await using var reviewerProof = host.CreateDbContext();
    var storedProcedureStatus = await reviewerProof.AuditProcedures.AsNoTracking().Where(x =>
      x.EngagementId == f.EngagementId && x.SourceProcedureId == "AWP-02-01").Select(x => x.Status).SingleAsync();
    Assert.Equal(200, reviewerFieldworkStatus);
    using (var reviewerFieldworkJson = JsonDocument.Parse(reviewerFieldworkBody))
    {
      var workspace = reviewerFieldworkJson.RootElement;
      var target = workspace.GetProperty("procedures").EnumerateArray()
        .FirstOrDefault(item => item.GetProperty("sourceProcedureId").GetString() == "AWP-02-01");
      Assert.True(target.ValueKind == JsonValueKind.Object,
        $"Reviewer fieldwork did not project AWP-02-01. canManageFieldwork={workspace.GetProperty("canManageFieldwork").GetBoolean()}, " +
        $"canViewReviewNotes={workspace.GetProperty("canViewReviewNotes").GetBoolean()}, " +
        $"procedureCount={workspace.GetProperty("procedures").GetArrayLength()}, " +
        $"storedProcedureStatus={storedProcedureStatus}.");
    }
    var reviewerSection = reviewerPage.Locator("select[name='section']");
    var reviewerCashBankSection = reviewerSection.Locator("option").Filter(new() { HasText = "Cash & Bank" });
    await Assertions.Expect(reviewerCashBankSection).ToHaveCountAsync(1);
    await reviewerSection.SelectOptionAsync(new SelectOptionValue { Label = await reviewerCashBankSection.InnerTextAsync() });
    var reviewerRow = reviewerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "AWP-02-01" });
    var commandStatePath = $"/api/ui/procedures/{procedureId:D}/commands/state";
    var (commandStateStatus, commandStateBody) = await PlaywrightResponseCapture.RunAsync(reviewerPage,
      request => request.Method == "GET" && new Uri(request.Url).AbsolutePath.Equals(commandStatePath, StringComparison.OrdinalIgnoreCase),
      async () => await reviewerRow.GetByRole(AriaRole.Button, new() { Name = "Open procedure", Exact = true }).ClickAsync());
    Assert.Equal(200, commandStateStatus);
    using (var commandStateJson = JsonDocument.Parse(commandStateBody))
    {
      var basis = commandStateJson.RootElement.GetProperty("basis");
      Assert.True(basis.GetProperty("canReview").GetBoolean(),
        $"Independent reviewer cannot review the just-submitted current result. Status={basis.GetProperty("latestResultStatus").GetString()}, " +
        $"resultRevision={basis.GetProperty("latestResultRevision").GetInt64()}, blockers={string.Join(" | ", basis.GetProperty("blockers").EnumerateArray().Select(x => x.GetString()))}.");
    }
    var review = reviewerPage.GetByRole(AriaRole.Region, new() { Name = "Procedure execution and review", Exact = true });
    await Assertions.Expect(review).ToContainTextAsync("Frozen submitted result");
    await Assertions.Expect(review.GetByText(
      "Agreed the synthetic year-end bank reconciliation to the listed bank accounts."))
      .ToBeVisibleAsync();
    foreach (var width in new[] { 320, 390 })
    {
      await reviewerPage.SetViewportSizeAsync(width, 900);
      var documentWidth = await reviewerPage.EvaluateAsync<int>(
        "() => Math.max(document.documentElement.scrollWidth, document.body.scrollWidth)");
      Assert.True(documentWidth <= width + 1,
        $"The populated procedure review document overflows at {width}px (scroll width {documentWidth}px).");
      await Assertions.Expect(review.GetByText(
        "Agreed the synthetic year-end bank reconciliation to the listed bank accounts."))
        .ToBeVisibleAsync();
    }

    var decisionControl = review.GetByRole(AriaRole.Combobox, new() { Name = "Decision", Exact = true });
    Assert.True(await decisionControl.CountAsync() == 1,
      $"The server state allowed independent review, but the UI did not render its decision control. " +
      $"Command state: {commandStateBody}; panel: {await review.InnerTextAsync()}; page errors: {string.Join(" | ", pageErrors)}");
    await decisionControl.SelectOptionAsync("CHANGES_REQUIRED");
    var reviewComment = review.GetByRole(AriaRole.Textbox,
      new() { Name = "Review comment (required when returning)", Exact = true });
    const string initialReviewCommentText = "Return the reconciliation conclusion with an explicit statement-level tie-out.";
    const string recordedReviewCommentText =
      "Return the reconciliation conclusion with an explicit statement-level tie-out and closing-balance cross-check.";
    var prepareReview = review.GetByRole(AriaRole.Button, new() { Name = "Preview exact review", Exact = true });
    await Assertions.Expect(reviewComment).ToBeVisibleAsync();
    await Assertions.Expect(prepareReview).ToBeDisabledAsync();
    await reviewComment.FillAsync(initialReviewCommentText);
    await TabToAsync(reviewerPage, prepareReview);
    await Assertions.Expect(prepareReview).ToBeFocusedAsync();
    await reviewerPage.Keyboard.PressAsync("Enter");
    var reviewPreview = review.GetByRole(AriaRole.Region, new() { Name = "Exact command preview", Exact = true });
    var reviewAssent = reviewPreview.GetByRole(AriaRole.Checkbox,
      new() { Name = ProcedureAssentLabel, Exact = true });
    var recordReview = reviewPreview.GetByRole(AriaRole.Button,
      new() { Name = "Record exact outcome", Exact = true });
    await Assertions.Expect(reviewPreview).ToContainTextAsync("Record this exact independent review.");
    await Assertions.Expect(reviewAssent).Not.ToBeCheckedAsync();
    await Assertions.Expect(recordReview).ToBeDisabledAsync();

    // Editing the named comment is the existing cancel-and-edit path: it invalidates the preview and assent.
    await TabToAsync(reviewerPage, reviewAssent);
    await reviewerPage.Keyboard.PressAsync("Space");
    await Assertions.Expect(reviewAssent).ToBeCheckedAsync();
    await reviewerPage.Keyboard.PressAsync("Shift+Tab");
    await Assertions.Expect(prepareReview).ToBeFocusedAsync();
    await reviewerPage.Keyboard.PressAsync("Shift+Tab");
    await Assertions.Expect(reviewComment).ToBeFocusedAsync();
    await reviewComment.FillAsync(recordedReviewCommentText);
    await Assertions.Expect(reviewPreview).ToHaveCountAsync(0);
    await TabToAsync(reviewerPage, prepareReview);
    await reviewerPage.Keyboard.PressAsync("Enter");
    await Assertions.Expect(reviewPreview).ToContainTextAsync("Record this exact independent review.");
    await Assertions.Expect(reviewAssent).Not.ToBeCheckedAsync();
    await Assertions.Expect(recordReview).ToBeDisabledAsync();
    await TabToAsync(reviewerPage, reviewAssent);
    await reviewerPage.Keyboard.PressAsync("Space");
    await Assertions.Expect(reviewAssent).ToBeCheckedAsync();
    await Assertions.Expect(recordReview).ToBeEnabledAsync();
    await TabToAsync(reviewerPage, recordReview);
    await Assertions.Expect(recordReview).ToBeFocusedAsync();
    await reviewerPage.Keyboard.PressAsync("Enter");
    await Assertions.Expect(review.Locator("p[role='status']").Filter(new() { HasText = "Recorded REVIEW_RESULT" }))
      .ToBeVisibleAsync();

    await preparerPage.GotoAsync(origin + path);
    var preparerSectionAfterReturn = preparerPage.Locator("select[name='section']");
    var cashBankAfterReturn = preparerSectionAfterReturn.Locator("option").Filter(new() { HasText = "Cash & Bank" });
    await Assertions.Expect(cashBankAfterReturn).ToHaveCountAsync(1);
    await preparerSectionAfterReturn.SelectOptionAsync(new SelectOptionValue { Label = await cashBankAfterReturn.InnerTextAsync() });
    var preparerRowAfterReturn = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "AWP-02-01" });
    await preparerRowAfterReturn.GetByRole(AriaRole.Button, new() { Name = "Open procedure", Exact = true }).ClickAsync();
    var returned = preparerPage.GetByRole(AriaRole.Region, new() { Name = "Procedure execution and review", Exact = true });
    await Assertions.Expect(returned.GetByLabel("Work performed", new() { Exact = true })).ToBeVisibleAsync();
    await returned.GetByLabel("Work performed", new() { Exact = true })
      .FillAsync("Reperformed the synthetic bank reconciliation tie-out after independent review.");
    await returned.GetByLabel("Conclusion", new() { Exact = true })
      .FillAsync("The reconciliation is now explicitly tied to the statement balance.");
    await returned.GetByLabel("Manual reference", new() { Exact = true }).FillAsync("BANK-REC-SYNTHETIC-02");
    await returned.GetByLabel("What was observed", new() { Exact = true })
      .FillAsync("The revised schedule and closing statement balance agree.");
    await returned.GetByRole(AriaRole.Button, new() { Name = "Preview exact submission", Exact = true }).ClickAsync();
    var resubmission = returned.GetByRole(AriaRole.Region, new() { Name = "Exact command preview", Exact = true });
    await Assertions.Expect(resubmission.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    await Assertions.Expect(resubmission.GetByRole(AriaRole.Checkbox,
      new() { Name = ProcedureAssentLabel, Exact = true })).Not.ToBeCheckedAsync();
    await Assertions.Expect(resubmission.GetByRole(AriaRole.Button,
      new() { Name = "Record exact outcome", Exact = true })).ToBeDisabledAsync();

    Guid firstReturnedResultId;
    Guid firstReworkTaskId;
    await using (var proof = host.CreateDbContext())
    {
      var result = await proof.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.AuditProcedureId == procedureId);
      Assert.Equal(AuditProcedureResultStatuses.ChangesRequired, result.Status);
      firstReturnedResultId = result.Id;
      Assert.Equal(f.Staff.Id, result.PreparedByUserId);
      using var submitted = JsonDocument.Parse(result.StructuredResultJson);
      var evidence = submitted.RootElement.GetProperty(AuditProgramService.EvidenceSnapshotProperty)
        .GetProperty("selections").EnumerateArray().Single();
      Assert.Equal("BANK-REC-SYNTHETIC-01", evidence.GetProperty("manualReference").GetString());
      Assert.Equal("The closing balance and reconciling items agree to the synthetic schedule.",
        evidence.GetProperty("observation").GetString());
      var reviewRecord = await proof.AuditProcedureReviews.AsNoTracking().SingleAsync(x => x.AuditProcedureResultId == result.Id);
      Assert.Equal(AuditProcedureReviewDecisions.ChangesRequired, reviewRecord.Decision);
      Assert.Equal(f.Reviewer.Id, reviewRecord.ReviewerUserId);
      Assert.Equal(requestId, await proof.AuditProcedureCommandReceipts.AsNoTracking()
        .Where(x => x.AuditProcedureId == procedureId && x.Action == ProcedureCommandActions.SubmitResult)
        .Select(x => x.RequestId).SingleAsync());
      Assert.Equal(requestHash, await proof.AuditProcedureCommandReceipts.AsNoTracking()
        .Where(x => x.AuditProcedureId == procedureId && x.Action == ProcedureCommandActions.SubmitResult)
        .Select(x => x.RequestHash).SingleAsync());
      Assert.Equal(2, await proof.AuditProcedureCommandReceipts.CountAsync(x => x.AuditProcedureId == procedureId));
      var binding = await proof.WorkflowTaskBindings.AsNoTracking().SingleAsync(x =>
        x.FirmId == f.FirmId && x.EngagementId == f.EngagementId && x.ReviewProcedureResultId == result.Id);
      firstReworkTaskId = binding.WorkTaskId;
    }

    // Finish the first correction through the same keyboard-accessible command preview.
    var correctionAssent = resubmission.GetByRole(AriaRole.Checkbox,
      new() { Name = ProcedureAssentLabel, Exact = true });
    var recordCorrection = resubmission.GetByRole(AriaRole.Button,
      new() { Name = "Record exact outcome", Exact = true });
    await TabToAsync(preparerPage, correctionAssent);
    await preparerPage.Keyboard.PressAsync("Space");
    await Assertions.Expect(correctionAssent).ToBeCheckedAsync();
    await Assertions.Expect(recordCorrection).ToBeEnabledAsync();
    await TabToAsync(preparerPage, recordCorrection);
    await preparerPage.Keyboard.PressAsync("Enter");
    await Assertions.Expect(returned.Locator("p[role='status']")
      .Filter(new() { HasText = "Recorded SUBMIT_RESULT" })).ToBeVisibleAsync();

    Guid firstResubmissionId;
    await using (var db = host.CreateDbContext())
    {
      var result = await db.AuditProcedureResults.AsNoTracking().Where(x => x.AuditProcedureId == procedureId)
        .OrderByDescending(x => x.Revision).FirstAsync();
      Assert.Equal(2, result.Revision);
      Assert.Equal(AuditProcedureResultStatuses.Submitted, result.Status);
      firstResubmissionId = result.Id;
    }

    var firstTaskSource = $"REVIEW-REWORK:{procedureId:N}:{firstReturnedResultId:N}";
    Assert.NotEqual(Guid.Empty, firstReworkTaskId);
    await OpenQueueViewAsync(reviewerPage, reviewerOrigin, "Under review");
    var taskCards = QueueTaskCards(reviewerPage);
    await Assertions.Expect(taskCards).ToHaveCountAsync(1);
    await Assertions.Expect(taskCards).ToContainTextAsync("Address review changes for AWP-02-01");
    await Assertions.Expect(taskCards).ToContainTextAsync(firstTaskSource);
    await Assertions.Expect(taskCards).ToContainTextAsync(recordedReviewCommentText);
    var reviewInbox = reviewerPage.GetByRole(AriaRole.Region,
      new() { Name = "Submitted procedure reviews", Exact = true });
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Submitted work awaiting review", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewInbox).ToContainTextAsync(
      "Current procedure submissions awaiting independent review");
    await Assertions.Expect(reviewerPage.GetByText(
      "These submissions are not represented in Under review. Earlier operational tasks may still exist.",
      new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(reviewInbox).ToContainTextAsync(
      "No additional current submissions await review in your authorized scope.");

    // Repeated review with rework-task creation disabled must reuse the historical task
    // while showing the current comment; the source result and prior review remain immutable.
    const string repeatedReviewComment = "Resolve the remaining statement-level tie-out discrepancy.";
    await using (var db = host.CreateDbContext())
    {
      var reviewAgain = await AuditProgramService.ReviewResultAsync(db, reviewer,
        new ReviewProcedureResultRequest(firstResubmissionId,
          AuditProcedureReviewDecisions.ChangesRequired, repeatedReviewComment), createWorkflowReworkTask: false);
      Assert.True(reviewAgain.Succeeded, reviewAgain.Message);
    }
    await OpenQueueViewAsync(reviewerPage, reviewerOrigin, "Changes required");
    await Assertions.Expect(taskCards).ToHaveCountAsync(1);
    await Assertions.Expect(taskCards).ToContainTextAsync(firstTaskSource);
    await Assertions.Expect(taskCards).ToContainTextAsync(repeatedReviewComment);

    await reviewerPage.GetByRole(AriaRole.Navigation, new() { Name = "Work queue display", Exact = true })
      .GetByRole(AriaRole.Button, new() { Name = "List", Exact = true }).ClickAsync();
    await reviewerPage.SetViewportSizeAsync(390, 844);
    Assert.True(await reviewerPage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
      "The populated correction queue must not widen the narrow document.");
    var taskScroll = reviewerPage.GetByRole(AriaRole.Region, new() { Name = "Operational task list", Exact = true });
    await Assertions.Expect(taskScroll).ToContainTextAsync(repeatedReviewComment);
    await TabToAsync(reviewerPage, taskScroll);
    await Assertions.Expect(taskScroll).ToBeFocusedAsync();
    if (await taskScroll.EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth"))
    {
      await taskScroll.PressAsync("ArrowRight");
      var scrolled = false;
      for (var attempt = 0; attempt < 40; attempt++)
      {
        scrolled = await taskScroll.EvaluateAsync<bool>("element => element.scrollLeft > 0");
        if (scrolled) break;
        await Task.Delay(25);
      }
      Assert.True(scrolled, "The correction table must scroll through native keyboard input.");
    }
    await OwnedBrowserFixtureHandoff.WaitAsync(reviewerOrigin, "/app/work", "repeated-correction-review");
    await reviewerPage.SetViewportSizeAsync(1280, 900);

    // With no task linked to this later rejected revision, the current submission remains
    // visible in the reviewer inbox and is not duplicated into the Under review task view.
    Guid unrepresentedResubmissionId;
    await using (var db = host.CreateDbContext())
    {
      var resubmitted = await AuditProgramService.SubmitResultAsync(db, preparer,
        new SubmitProcedureResultRequest(procedureId, 1, "Both source discrepancies reconciled.",
          "{\"result\":\"PASS\"}", ["source:second-corrected-sample"],
          "Second correction is ready for independent review."));
      Assert.True(resubmitted.Succeeded, resubmitted.Message);
      unrepresentedResubmissionId = resubmitted.Value!.AuditProcedureResultId;
      var current = await db.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.Id == unrepresentedResubmissionId);
      Assert.Equal(3, current.Revision);
      Assert.Equal(AuditProcedureResultStatuses.Submitted, current.Status);
    }
    await OpenQueueViewAsync(reviewerPage, reviewerOrigin, "Under review");
    await Assertions.Expect(taskCards).ToHaveCountAsync(0);
    await Assertions.Expect(reviewerPage.GetByText(
      "1 current submission(s) awaiting review outside the task queue.", new() { Exact = true })).ToBeVisibleAsync();
    var unrepresentedRow = reviewInbox.GetByRole(AriaRole.Row).Filter(new() { HasText = "AWP-02-01" });
    await Assertions.Expect(unrepresentedRow).ToHaveCountAsync(1);
    await Assertions.Expect(unrepresentedRow).ToContainTextAsync("Revision 3");
    await Assertions.Expect(unrepresentedRow.GetByRole(AriaRole.Link,
      new() { Name = "Open fieldwork", Exact = true })).ToHaveAttributeAsync("href",
      $"/app/engagements/{f.EngagementId:D}/audit-fieldwork");

    // A newer linked rework task supersedes the old queue projection without duplicating it.
    const string newestReviewComment = "Confirm the final source cross-reference.";
    await using (var db = host.CreateDbContext())
    {
      var reviewAgain = await AuditProgramService.ReviewResultAsync(db, reviewer,
        new ReviewProcedureResultRequest(unrepresentedResubmissionId,
          AuditProcedureReviewDecisions.ChangesRequired, newestReviewComment), createWorkflowReworkTask: true);
      Assert.True(reviewAgain.Succeeded, reviewAgain.Message);
    }
    var newestTaskSource = $"REVIEW-REWORK:{procedureId:N}:{unrepresentedResubmissionId:N}";
    await OpenQueueViewAsync(reviewerPage, reviewerOrigin, "Changes required");
    await Assertions.Expect(taskCards).ToHaveCountAsync(1);
    await Assertions.Expect(taskCards).ToContainTextAsync(newestTaskSource);
    await Assertions.Expect(taskCards).ToContainTextAsync(newestReviewComment);
    Assert.DoesNotContain(firstTaskSource, await taskCards.InnerTextAsync(), StringComparison.Ordinal);

    Guid finalResubmissionId;
    await using (var db = host.CreateDbContext())
    {
      var finalResubmission = await AuditProgramService.SubmitResultAsync(db, preparer,
        new SubmitProcedureResultRequest(procedureId, 1, "Final source cross-reference confirmed.",
          "{\"result\":\"PASS\"}", ["source:final-cross-reference"],
          "Final correction is ready for independent review."));
      Assert.True(finalResubmission.Succeeded, finalResubmission.Message);
      finalResubmissionId = finalResubmission.Value!.AuditProcedureResultId;
    }
    await OpenQueueViewAsync(reviewerPage, reviewerOrigin, "Under review");
    await Assertions.Expect(taskCards).ToHaveCountAsync(1);
    await Assertions.Expect(taskCards).ToContainTextAsync(newestTaskSource);
    await Assertions.Expect(taskCards).ToContainTextAsync(newestReviewComment);
    await Assertions.Expect(reviewInbox).ToContainTextAsync(
      "No additional current submissions await review in your authorized scope.");
    Assert.DoesNotContain(firstTaskSource, await taskCards.InnerTextAsync(), StringComparison.Ordinal);

    await using (var proof = host.CreateDbContext())
    {
      var bindings = await proof.WorkflowTaskBindings.AsNoTracking().Where(x =>
        x.FirmId == f.FirmId && x.EngagementId == f.EngagementId).ToListAsync();
      Assert.Equal(2, bindings.Count);
      var originalBinding = Assert.Single(bindings, x => x.WorkTaskId == firstReworkTaskId);
      Assert.Equal((Guid?)firstReturnedResultId, originalBinding.ReviewProcedureResultId);
      Assert.Contains(bindings, x => x.ReviewProcedureResultId == unrepresentedResubmissionId);

      var latest = await proof.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.Id == finalResubmissionId);
      Assert.Equal(4, latest.Revision);
      Assert.Equal(AuditProcedureResultStatuses.Submitted, latest.Status);
      var procedureResultIds = await proof.AuditProcedureResults.AsNoTracking()
        .Where(x => x.FirmId == f.FirmId && x.EngagementId == f.EngagementId && x.AuditProcedureId == procedureId)
        .Select(x => x.Id).ToArrayAsync();
      var reviewedDecisions = await proof.AuditProcedureReviews.AsNoTracking().CountAsync(x =>
        procedureResultIds.Contains(x.AuditProcedureResultId) && x.Decision == AuditProcedureReviewDecisions.Reviewed);
      Assert.Equal(0, reviewedDecisions);
    }
    Assert.Empty(pageErrors);
  }
}
