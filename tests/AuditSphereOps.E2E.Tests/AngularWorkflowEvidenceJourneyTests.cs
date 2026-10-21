using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularWorkflowEvidenceJourneyTests
{
  [Fact]
  public async Task PublishedTemplatePinsRequiredAndOptionalTasksAcrossQueueLayouts()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-TEMPLATE-LINEAGE");
    var f = host.Fixture;
    const string templateName = "SYNTHETIC PINNED TEMPLATE";
    const string requiredTitle = "SYN REQUIRED TASK";
    const string optionalTitle = "SYN OPTIONAL TASK";
    const string manualTitle = "SYN MANUAL CONTROL";
    var manualKey = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      (await db.Engagements.SingleAsync(x => x.Id == f.EngagementId)).ServiceRoute = "FinancialStatementAudit";
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      await db.SaveChangesAsync();
      var manual = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        f.EngagementId, new(manualTitle, WorkflowTaskPhases.AuditPlanning, 1,
          AssigneeUserId: f.Admin.Id, IdempotencyKey: manualKey));
      Assert.True(manual.Succeeded, manual.Message);
    }
    var settings = new Dictionary<string, string> {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true"
    };
    var origin = await host.StartApiForIdentityAsync(f.Admin, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    const string catalog = "/app/practice/workflow-templates";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(catalog));
    await page.GetByRole(AriaRole.Button, new() { Name = "New template", Exact = true }).ClickAsync();
    await page.GetByLabel("Template name", new() { Exact = true }).FillAsync(templateName);
    await page.GetByLabel("Service route", new() { Exact = false }).FillAsync("FinancialStatementAudit");
    var first = page.GetByRole(AriaRole.Group, new() { Name = "Step 1", Exact = true });
    await first.GetByLabel("Stable key", new() { Exact = true }).FillAsync("required");
    await first.GetByLabel("Task title", new() { Exact = true }).FillAsync(requiredTitle);
    await Assertions.Expect(first.GetByLabel("Required task", new() { Exact = true })).ToBeCheckedAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Add step", Exact = true }).ClickAsync();
    var second = page.GetByRole(AriaRole.Group, new() { Name = "Step 2", Exact = true });
    await second.GetByLabel("Stable key", new() { Exact = true }).FillAsync("optional");
    await second.GetByLabel("Task title", new() { Exact = true }).FillAsync(optionalTitle);
    await second.GetByLabel("Required task", new() { Exact = true }).UncheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save draft", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The template draft was created.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review draft", Exact = true })).ToHaveCountAsync(0);

    // Separate browser context prevents the reviewer's cookie from replacing the author's cookie.
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => errors.Add(error);
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(catalog));
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = templateName, Exact = false }).ClickAsync();
    await reviewerPage.GetByLabel("Decision rationale", new() { Exact = true }).FillAsync("Independently reviewed these synthetic operational task designations.");
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Review draft", Exact = true }).ClickAsync();
    var publish = reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Publish reviewed revision", Exact = true });
    await Assertions.Expect(publish).ToBeVisibleAsync();
    await reviewerPage.GetByLabel("Decision rationale", new() { Exact = true }).FillAsync("Publish the exact independently reviewed synthetic revision.");
    await publish.ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("The reviewed template revision was published.", new() { Exact = true })).ToBeVisibleAsync();
    Guid revisionId;
    await using (var db = host.CreateDbContext())
    {
      var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Name == templateName);
      revisionId = revision.Id;
      Assert.Equal(WorkflowTemplateStates.Published, revision.Status);
      Assert.Equal(f.Admin.Id, revision.LastEditedByUserId);
      Assert.Equal(f.Reviewer.Id, revision.ReviewedByUserId);
    }
    await page.GotoAsync(origin + $"/app/engagements/{f.EngagementId}?templateRevisionId={revisionId}#engagement-task-plan");
    await Assertions.Expect(page.GetByRole(AriaRole.Radio, new() { Name = templateName })).ToBeCheckedAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview task plan", Exact = true }).ClickAsync();
    await page.GetByLabel("I reviewed this exact plan, staffing, dates, and template revision.", new() { Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Create operational tasks", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Plan receipt confirmed", Exact = true })).ToBeVisibleAsync();
    Dictionary<string, Guid> stepIds;
    Guid requiredTaskId;
    await using (var db = host.CreateDbContext())
    {
      var steps = await db.WorkflowTemplateSteps.Where(x => x.TemplateRevisionId == revisionId).ToListAsync();
      stepIds = steps.ToDictionary(x => x.StableKey, x => x.Id);
      var bindings = await db.WorkflowTaskBindings.Where(x => x.TemplateRevisionId == revisionId).ToListAsync();
      Assert.Equal(2, bindings.Count);
      foreach (var step in steps)
      {
        var binding = Assert.Single(bindings, x => x.TemplateStepId == step.Id);
        Assert.Equal($"TEMPLATE:{revisionId:N}:{step.StableKey}", binding.SourceKey);
      }
      var requiredBinding = Assert.Single(bindings, x => x.TemplateStepId == stepIds["required"]);
      requiredTaskId = requiredBinding.WorkTaskId;
      Assert.Null(requiredBinding.AssigneeUserIdSnapshot);
      var tasks = await db.WorkTasks.Where(x => x.EngagementId == f.EngagementId).ToListAsync();
      Assert.Equal(3, tasks.Count);
      Assert.All(tasks, task => { Assert.NotEqual(PracticeTimeStates.TaskCompleted, task.Status); Assert.Null(task.CompletedAt); });
      Assert.Single(await db.WorkflowTaskPlanReceipts.ToListAsync());
      Assert.Empty(await db.MaterialityApprovals.ToListAsync());
      Assert.Empty(await db.AuditProcedureReviews.ToListAsync());
    }

    // Assign one generated, initially unassigned task through its existing engagement control.
    await page.GotoAsync(origin + $"/app/engagements/{f.EngagementId}");
    var requiredRow = page.Locator("ul.checklist > li").Filter(new() { HasText = requiredTitle });
    await Assertions.Expect(requiredRow).ToHaveCountAsync(1);
    var requiredPhase = requiredRow.Locator("xpath=ancestor::details[1]");
    if (await requiredPhase.GetAttributeAsync("open") is null)
      await requiredPhase.Locator("summary").ClickAsync();
    await requiredRow.GetByRole(AriaRole.Button, new() { Name = "Assign task", Exact = true }).ClickAsync();
    var assignment = page.GetByRole(AriaRole.Region, new() { Name = "Assign workflow task", Exact = true });
    await Assertions.Expect(assignment).ToBeVisibleAsync();
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, $"/app/engagements/{f.EngagementId}", "workflow-task-assignment");
    await assignment.GetByRole(AriaRole.Combobox, new() { Name = "New owner", Exact = true }).SelectOptionAsync(f.Staff.Id.ToString("D"));
    await assignment.GetByLabel(
      "I reviewed this exact task, engagement, current owner, and new owner.", new() { Exact = true }).CheckAsync();
    var assignmentResponses = await PlaywrightResponseCapture.RunMultipleAsync(page,
      [
        request => request.Method == "POST" && request.Url.Contains(
          $"/api/ui/engagements/{f.EngagementId:D}/workflow/tasks/{requiredTaskId:D}/assign", StringComparison.Ordinal),
        request => request.Method == "GET" && Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) &&
          uri.AbsolutePath == $"/api/ui/engagements/{f.EngagementId:D}/workflow/tasks"
      ],
      () => assignment.GetByRole(AriaRole.Button, new() { Name = "Confirm assignment", Exact = true }).ClickAsync());
    Assert.InRange(assignmentResponses[0].Status, 200, 299);
    Assert.InRange(assignmentResponses[1].Status, 200, 299);
    using (var assignedWorkspace = JsonDocument.Parse(assignmentResponses[1].Body))
    {
      Assert.Equal(f.EngagementId, assignedWorkspace.RootElement.GetProperty("engagementId").GetGuid());
      var assignedTask = Assert.Single(assignedWorkspace.RootElement.GetProperty("tasks").EnumerateArray(),
        row => row.GetProperty("id").GetGuid() == requiredTaskId);
      Assert.Equal(f.Staff.Id, assignedTask.GetProperty("assigneeUserId").GetGuid());
    }
    await Assertions.Expect(requiredRow).ToContainTextAsync("Owner: Staff");

    // A separate authenticated browser context verifies the exact task appears in its owner's My Tasks queue.
    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, settings);
    var staffPage = await browser.NewPageAsync();
    staffPage.PageError += (_, error) => errors.Add(error);
    var (staffQueueStatus, staffQueueBody) = await PlaywrightResponseCapture.RunAsync(staffPage,
      request => request.Method == "GET" && request.Url.Contains("/api/ui/work/tasks?", StringComparison.Ordinal) &&
        request.Url.Contains("view=MY_TASKS", StringComparison.Ordinal),
      () => staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fwork"));
    Assert.InRange(staffQueueStatus, 200, 299);
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading, new() { Name = "My work", Exact = true })).ToBeVisibleAsync();
    using (var staffQueue = JsonDocument.Parse(staffQueueBody))
    {
      var staffTask = Assert.Single(staffQueue.RootElement.GetProperty("tasks").EnumerateArray(),
        row => row.GetProperty("taskId").GetGuid() == requiredTaskId);
      Assert.Equal(f.Staff.Id, staffTask.GetProperty("assigneeUserId").GetGuid());
    }
    await Assertions.Expect(staffPage.Locator("tbody tr").Filter(new() { HasText = requiredTitle })).ToHaveCountAsync(1);

    await page.GotoAsync(origin + "/app/work");
    await page.GetByRole(AriaRole.Navigation, new() { Name = "Work queue views", Exact = true })
      .GetByRole(AriaRole.Button, new() { Name = "Team", Exact = true }).ClickAsync();
    foreach (var layout in new[] { "List", "Board", "Calendar" })
    {
      var display = page.GetByRole(AriaRole.Navigation, new() { Name = "Work queue display", Exact = true });
      await display.GetByRole(AriaRole.Button, new() { Name = layout, Exact = true }).PressAsync("Space");
      var items = page.Locator(layout == "List" ? "tbody tr" : "article.task-card");
      await Assertions.Expect(items.Filter(new() { HasText = "SYN " })).ToHaveCountAsync(3);
      await CheckTaskAsync(requiredTitle, "Required", "required");
      await CheckTaskAsync(optionalTitle, "Optional", "optional");
      var manual = items.Filter(new() { HasText = manualTitle });
      await Assertions.Expect(manual).ToHaveCountAsync(1);
      await Assertions.Expect(manual).ToContainTextAsync($"MANUAL:{manualKey:N}");
      await Assertions.Expect(manual).Not.ToContainTextAsync("Template task designation:");
      await Assertions.Expect(manual).Not.ToContainTextAsync("template revision");
      await page.SetViewportSizeAsync(320, 844);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));

      async Task CheckTaskAsync(string title, string designation, string key)
      {
        var item = items.Filter(new() { HasText = title });
        await Assertions.Expect(item).ToHaveCountAsync(1);
        await Assertions.Expect(item).ToContainTextAsync("Template task designation: " + designation);
        await Assertions.Expect(item).ToContainTextAsync($"TEMPLATE:{revisionId:N}:{key}");
        await Assertions.Expect(item).ToContainTextAsync("template revision " + revisionId);
        await Assertions.Expect(item).ToContainTextAsync("step " + stepIds[key]);
      }
    }
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/app/work", "workflow-template-lineage");
  }

  [Fact]
  public async Task TemplatePrerequisitesRequireExactBindingAndRemainPendingAfterPlanCreation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-TEMPLATE-EVIDENCE");
    var f = host.Fixture; Guid revisionId; Guid pbcId; var procedureId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      var engagement = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId);
      engagement.ServiceRoute = "FinancialStatementAudit";
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      await db.SaveChangesAsync();
      var request = await PbcService.CreateRequestAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
        new(f.EngagementId, "Synthetic template evidence", "TEST ENTITY", "2026-01-01", "2026-12-31", "Cash", "PDF",
          "", f.Client.Id, f.Staff.Id, f.Reviewer.Id, "2027-01-31", "Confidential", "Signed and readable."));
      Assert.True(request.Succeeded, request.Message); pbcId = request.Value;
      // Pending source identity only; this fixture records no procedure result, applicability approval or review.
      db.AuditProcedures.Add(new AuditProcedure
      {
        Id = procedureId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        SourceProcedureId = "SYN-PICKER-01", Title = "Synthetic source picker procedure",
        Status = AuditProcedureStatuses.Planned, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var draft = await WorkflowTemplateService.CreateDraftAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new(null, "Synthetic evidence-bound plan", "FinancialStatementAudit", null,
          [new("EVIDENCE", WorkflowTaskPhases.AuditPlanning, "Wait for authoritative evidence", "Review the existing sources.",
            null, null, null, null, true, 1, EvidenceRequirements:
              [new("materiality", WorkflowEvidenceKinds.CurrentMateriality), new("request", WorkflowEvidenceKinds.AcceptedPbc),
               new("procedure", WorkflowEvidenceKinds.ReviewedProcedure)])]));
      Assert.True(draft.Succeeded, draft.Message); revisionId = draft.Value;
      var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == revisionId);
      var reviewed = await WorkflowTemplateService.ReviewAsync(db, PbcSeed.Actor(f.Reviewer, "Partner"), revision.TemplateId,
        revision.Id, revision.Revision, "Independently reviewed the operational evidence declarations.");
      Assert.True(reviewed.Succeeded, reviewed.Message);
      var published = await WorkflowTemplateService.PublishAsync(db, PbcSeed.Actor(f.Reviewer, "Partner"), revision.TemplateId,
        revision.Id, revision.Revision, "Publish the exact reviewed operational prerequisites.");
      Assert.True(published.Succeeded, published.Message);
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string> {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true"
    });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    var path = $"/app/engagements/{f.EngagementId}?templateRevisionId={revisionId}#engagement-task-plan";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Radio, new() { Name = "Synthetic evidence-bound plan" })).ToBeCheckedAsync();
    var preview = page.GetByRole(AriaRole.Button, new() { Name = "Preview task plan", Exact = true });
    await preview.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Create operational tasks", Exact = true })).ToHaveCountAsync(0);
    await SelectSourceAsync("request", "Synthetic template evidence", "Cash — Synthetic template evidence");
    await preview.ClickAsync();
    await Assertions.Expect(page.GetByLabel("Exact source reference for request", new() { Exact = true })).ToHaveValueAsync(pbcId.ToString());
    await SelectSourceAsync("procedure", "syn-picker-01", "SYN-PICKER-01 — Synthetic source picker procedure");
    await preview.ClickAsync();
    await Assertions.Expect(page.GetByLabel("Exact source reference for procedure", new() { Exact = true })).ToHaveValueAsync(procedureId.ToString());
    var create = page.GetByRole(AriaRole.Button, new() { Name = "Create operational tasks", Exact = true });
    await Assertions.Expect(create).ToBeDisabledAsync();
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "template-evidence-sources");
    await page.GetByLabel("I reviewed this exact plan, staffing, dates, and template revision.", new() { Exact = true }).CheckAsync();
    await create.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Plan receipt confirmed", Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var rules = await db.WorkflowTaskEvidenceRules.AsNoTracking().ToListAsync();
      Assert.Equal(3, rules.Count);
      Assert.All(rules, rule => { Assert.Equal(WorkflowEvidenceRuleStates.Required, rule.State); Assert.NotNull(rule.WorkflowTemplateEvidenceRequirementId); });
      Assert.Contains(rules, rule => rule.PbcRequestId == pbcId);
      Assert.Contains(rules, rule => rule.AuditProcedureId == procedureId);
      Assert.Single(await db.WorkflowTaskPlanReceipts.ToListAsync());
      Assert.Empty(await db.MaterialityApprovals.ToListAsync());
      var binding = await db.WorkflowTaskBindings.SingleAsync(x => x.TemplateRevisionId == revisionId);
      var evidence = await WorkflowTaskEvidenceQuery.GetAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId, binding.WorkTaskId);
      Assert.True(evidence.Succeeded, evidence.Message);
      Assert.All(evidence.Value!.Rules, rule => Assert.False(rule.Satisfied));
      Assert.Empty(await db.AuditProcedureReviews.ToListAsync());
      var task = await EngagementWorkflowTaskService.GetAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId, binding.WorkTaskId);
      Assert.True(task.Succeeded, task.Message);
      var taskView = Assert.Single(task.Value!.Tasks);
      var completion = await EngagementWorkflowTaskService.CompleteAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        taskView.Id, taskView.Revision);
      Assert.False(completion.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, completion.ErrorCode);
    }
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "template-evidence");

    async Task SelectSourceAsync(string noun, string search, string label)
    {
      await page.GetByRole(AriaRole.Button, new() { Name = $"Open evidence source search for {noun}", Exact = false }).ClickAsync();
      await page.GetByLabel($"Find evidence source for {noun}", new() { Exact = false }).FillAsync(search);
      var response = await page.RunAndWaitForResponseAsync(
        () => page.GetByRole(AriaRole.Button, new() { Name = $"Search evidence sources for {noun}", Exact = false }).ClickAsync(),
        r => r.Url.Contains("/workflow-evidence-sources?", StringComparison.Ordinal) &&
          Uri.UnescapeDataString(new Uri(r.Url).Query.Replace("+", " ")).Contains("query=" + search, StringComparison.Ordinal));
      Assert.Equal(200, response.Status);
      var choice = page.GetByRole(AriaRole.Button, new() { Name = $"Select evidence source for {noun}: {label}", Exact = false });
      await Assertions.Expect(choice).ToBeVisibleAsync();
      await choice.PressAsync("Space");
      await Assertions.Expect(page.GetByText("Evidence source changed. Preview again to verify scope and review the updated digest.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(preview).ToBeFocusedAsync();
    }
  }

  [Fact]
  public async Task ReviewedPrerequisiteRecoversLostReplyAndKeepsOperationalCompletionSeparateFromProfessionalApproval()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-WORKFLOW-EVIDENCE");
    var f = host.Fixture; Guid taskId; Guid secondTaskId;
    const string title = "SYNTHETIC CURRENT MATERIALITY COORDINATION";
    const string secondTitle = "SYNTHETIC INDEPENDENT SOURCE REVIEW";
    await using (var db = host.CreateDbContext()) {
      var made = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
        new(title, WorkflowTaskPhases.AuditPlanning, 1, AssigneeUserId: f.Admin.Id, IdempotencyKey: Guid.NewGuid()));
      Assert.True(made.Succeeded, made.Message); taskId = made.Value;
      var secondMade = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
        new(secondTitle, WorkflowTaskPhases.AuditPlanning, 2, AssigneeUserId: f.Admin.Id, IdempotencyKey: Guid.NewGuid()));
      Assert.True(secondMade.Succeeded, secondMade.Message); secondTaskId = secondMade.Value;
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string> {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true"
    });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    const string path = "/app/work";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    var row = page.Locator("tbody tr").Filter(new() { HasText = title });
    var secondRow = page.Locator("tbody tr").Filter(new() { HasText = secondTitle });
    await Assertions.Expect(row).ToHaveCountAsync(1);
    await Assertions.Expect(secondRow).ToHaveCountAsync(1);
    await row.GetByRole(AriaRole.Button, new() { Name = "Review prerequisites", Exact = true }).ClickAsync();
    var panel = page.GetByRole(AriaRole.Region, new() { Name = "Evidence prerequisites", Exact = true });
    await Assertions.Expect(panel).ToContainTextAsync("No domain-evidence prerequisites");
    await panel.GetByLabel("Reason for the change", new() { Exact = true }).FillAsync("Require current independently approved materiality for this coordination task.");
    await panel.GetByRole(AriaRole.Button, new() { Name = "Review prerequisite", Exact = true }).ClickAsync();
    await panel.GetByLabel("I reviewed this exact prerequisite change.", new() { Exact = true }).CheckAsync();
    var posts = 0;
    await page.RouteAsync("**/workflow/tasks/*/evidence", async route => {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      posts++; var persisted = await route.FetchAsync(); Assert.True(persisted.Ok);
      await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Synthetic lost response after persistence\"}" });
    });
    await panel.GetByRole(AriaRole.Button, new() { Name = "Save reviewed change", Exact = true }).ClickAsync();
    await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Read saved prerequisite change", Exact = true })).ToBeVisibleAsync();
    var pendingStorageKey = $"auditsphere.workflow-evidence:{f.FirmId:D}:{f.Admin.Id:D}:{f.EngagementId:D}:{taskId:D}";
    var pendingKey = await page.EvaluateAsync<string>("key => sessionStorage.getItem(key) ?? ''", pendingStorageKey);
    Assert.NotEmpty(pendingKey);
    var firstReceiptReadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirstReceiptRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var receiptReadCount = 0;
    var receiptRoute = $"**/api/ui/engagements/{f.EngagementId:D}/workflow/tasks/{taskId:D}/evidence/receipts/{pendingKey}";
    await page.RouteAsync(receiptRoute, async route => {
      if (route.Request.Method != "GET") { await route.ContinueAsync(); return; }
      if (receiptReadCount++ == 0) {
        firstReceiptReadEntered.TrySetResult();
        await releaseFirstReceiptRead.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Synthetic delayed receipt lookup failure\"}" });
      } else await route.ContinueAsync();
    });
    try {
      await panel.GetByRole(AriaRole.Button, new() { Name = "Read saved prerequisite change", Exact = true }).ClickAsync();
      await firstReceiptReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
      await secondRow.GetByRole(AriaRole.Button, new() { Name = "Review prerequisites", Exact = true }).ClickAsync();
      await Assertions.Expect(panel).ToContainTextAsync(secondTitle);
      await Assertions.Expect(panel).Not.ToContainTextAsync(title);
      var secondReason = panel.GetByLabel("Reason for the change", new() { Exact = true });
      await Assertions.Expect(secondReason).ToBeVisibleAsync();
      const string secondReasonDraft = "Keep the second task's independently reviewed source draft.";
      await secondReason.FillAsync(secondReasonDraft);
      var reviewPrerequisite = panel.GetByRole(AriaRole.Button, new() { Name = "Review prerequisite", Exact = true });
      await Assertions.Expect(reviewPrerequisite).ToBeDisabledAsync();
      releaseFirstReceiptRead.TrySetResult();
      await Assertions.Expect(reviewPrerequisite).ToBeEnabledAsync();
      await Assertions.Expect(secondReason).ToHaveValueAsync(secondReasonDraft);
      await Assertions.Expect(panel).Not.ToContainTextAsync(
        "A saved receipt was not confirmed. Keep the recovery key and review the task; no change was resent.");

      await row.GetByRole(AriaRole.Button, new() { Name = "Review prerequisites", Exact = true }).ClickAsync();
      await Assertions.Expect(panel).ToContainTextAsync(title);
      await Assertions.Expect(panel).Not.ToContainTextAsync(secondTitle);
      var recovery = panel.GetByRole(AriaRole.Button, new() { Name = "Read saved prerequisite change", Exact = true });
      await Assertions.Expect(recovery).ToBeVisibleAsync();
      Assert.Equal(pendingKey, await page.EvaluateAsync<string>("key => sessionStorage.getItem(key) ?? ''", pendingStorageKey));
      await recovery.ClickAsync();
      await Assertions.Expect(recovery).ToHaveCountAsync(0);
      Assert.Null(await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", pendingStorageKey));
      Assert.Equal(2, receiptReadCount);
    }
    finally {
      releaseFirstReceiptRead.TrySetResult();
      await page.UnrouteAsync(receiptRoute);
    }
    await Assertions.Expect(row).ToContainTextAsync("1 evidence prerequisite(s) pending");
    Assert.Equal(1, posts);
    await Assertions.Expect(row.GetByRole(AriaRole.Button, new() { Name = "Complete task", Exact = true })).ToHaveCountAsync(0);
    await row.GetByRole(AriaRole.Button, new() { Name = "Review prerequisites", Exact = true }).ClickAsync();
    await Assertions.Expect(panel).ToContainTextAsync("Current approved materiality");
    await Assertions.Expect(panel).ToContainTextAsync("Pending");
    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await using (var db = host.CreateDbContext()) {
      Assert.Single(await db.WorkflowTaskEvidenceRules.ToListAsync());
      Assert.Empty(await db.MaterialityApprovals.ToListAsync());
      Assert.Equal(PracticeTimeStates.TaskInProgress, (await db.WorkTasks.SingleAsync(x => x.Id == taskId)).Status);
      Assert.Equal(PracticeTimeStates.TaskInProgress, (await db.WorkTasks.SingleAsync(x => x.Id == secondTaskId)).Status);
    }
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path);
  }
}
