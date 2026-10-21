using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAnnualEngagementJourneyTests
{
  [Theory]
  [InlineData("Manager")]
  [InlineData("RelationshipManager")]
  public async Task PriorPeriodReferenceCreatesOneReviewedBlockedAnnualShellWithoutCopyingApprovals(string role)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-ANNUAL-ENGAGEMENT"); var f = host.Fixture;
    Guid sourceId; Guid templateId; Guid templateRevisionId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, role, f.ClientId)); await db.SaveChangesAsync();
      var source = await EngagementLifecycleService.CreateDraftAsync(db, PbcSeed.Actor(f.Staff, role),
        new(f.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "Synthetic annual reference"));
      Assert.True(source.Succeeded, source.Message); sourceId = source.Value;
      db.ClientContacts.Add(new ClientContact { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        FullName = "Synthetic annual liaison", Email = "annual@example.test", Role = "Liaison", Primary = true });
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner")); await db.SaveChangesAsync();
      var draft = await WorkflowTemplateService.CreateDraftAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new(null, "Synthetic annual tasks", "AccountingOnly", null,
          [new("ANNUAL", WorkflowTaskPhases.AuditPlanning, "Prepare fresh annual work", "Review the new year independently.",
            null, 30, null, null, true, 1)]));
      Assert.True(draft.Succeeded, draft.Message); templateRevisionId = draft.Value;
      var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == templateRevisionId);
      templateId = revision.TemplateId;
      var reviewed = await WorkflowTemplateService.ReviewAsync(db, PbcSeed.Actor(f.Reviewer, "Partner"), revision.TemplateId,
        revision.Id, revision.Revision, "Independently review annual task instructions.");
      Assert.True(reviewed.Succeeded, reviewed.Message);
      var published = await WorkflowTemplateService.PublishAsync(db, PbcSeed.Actor(f.Reviewer, "Partner"), revision.TemplateId,
        revision.Id, revision.Revision, "Publish independent annual task instructions.");
      Assert.True(published.Succeeded, published.Message);
      var priorPreview = await EngagementWorkflowPlanService.PreviewAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        sourceId, new(templateRevisionId));
      Assert.True(priorPreview.Succeeded, priorPreview.Message);
      var priorPlan = await EngagementWorkflowPlanService.ExecuteAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), sourceId,
        new(templateRevisionId, priorPreview.Value!.PreviewDigest, Guid.NewGuid()));
      Assert.True(priorPlan.Succeeded, priorPlan.Message);
    }
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
      { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    var path = "/app/clients/" + f.ClientId + "/engagements/new?annualSourceEngagementId=" + sourceId;
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/clients/" + f.ClientId));
    var sibling = await SiblingClientSeed.SeedAsync(host.Database, f.FirmId, "ANGULAR-ANNUAL-FOREIGN");
    try
    {
      var foreignSourceUrl = origin + "/app/clients/" + f.ClientId + "/engagements/new?annualSourceEngagementId=" +
        sibling.Fixture.EngagementId;
      var foreignSourceResponse = await page.RunAndWaitForResponseAsync(
        () => page.GotoAsync(foreignSourceUrl),
        response => response.Request.Method == "GET" && response.Url.EndsWith(
          $"/api/ui/clients/{f.ClientId}/annual-engagement/{sibling.Fixture.EngagementId}", StringComparison.Ordinal));
      Assert.Equal(403, foreignSourceResponse.Status);
      await Assertions.Expect(page.GetByText("Annual source unavailable. Return to the client profile to review manual creation.",
        new() { Exact = true })).ToBeVisibleAsync();
      Assert.DoesNotContain(sibling.Marker, await page.Locator("body").InnerTextAsync());
    }
    finally { PbcSeed.DeleteDirectory(sibling.StagingRoot); }
    await page.GotoAsync(origin + "/app/clients/" + f.ClientId);
    await page.GetByRole(AriaRole.Row).Filter(new() { HasText = sourceId.ToString() })
      .GetByRole(AriaRole.Link, new() { Name = "Prepare next annual period", Exact = true }).ClickAsync();
    var annual = page.GetByRole(AriaRole.Region, new() { Name = "Annual engagement preparation", Exact = true });
    await Assertions.Expect(annual).ToContainTextAsync("Partner continuance");
    await Assertions.Expect(annual).ToContainTextAsync("2027-01-01 to 2027-12-31");
    await Assertions.Expect(annual).ToContainTextAsync("Synthetic annual liaison");
    await Assertions.Expect(annual).ToContainTextAsync("Confirm their current authority and routing separately; these suggestions grant no access or consent.");
    await Assertions.Expect(annual).ToContainTextAsync(role == "Manager"
      ? "No approved mapping exists for the source period."
      : "Mapping references require accounting read access to the source engagement.");
    await annual.GetByRole(AriaRole.Button, new() { Name = "Use annual suggestion", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByLabel("Published task template reference", new() { Exact = true })).ToHaveValueAsync(templateRevisionId.ToString());
    await Assertions.Expect(page.GetByLabel("Period start", new() { Exact = true })).ToHaveValueAsync("2027-01-01");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review engagement", Exact = true }).ClickAsync();
    var review = page.GetByRole(AriaRole.Region, new() { Name = "Reviewed engagement intent", Exact = true });
    await Assertions.Expect(review).ToContainTextAsync(sourceId.ToString());
    await Assertions.Expect(review).ToContainTextAsync(templateRevisionId.ToString());
    var confirm = review.GetByRole(AriaRole.Button, new() { Name = "Confirm blocked creation", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    await page.GetByLabel("I reviewed this exact client, service, profile and period.", new() { Exact = true }).CheckAsync();

    Guid currentTemplateRevisionId;
    await using (var db = host.CreateDbContext())
    {
      var draft = await WorkflowTemplateService.CreateDraftAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new(templateId, "Synthetic annual tasks", "AccountingOnly", null,
          [new("ANNUAL", WorkflowTaskPhases.AuditPlanning, "Prepare fresh annual work", "Review the new year independently.",
            null, 30, null, null, true, 1)]));
      Assert.True(draft.Succeeded, draft.Message); currentTemplateRevisionId = draft.Value;
    }
    await using (var db = host.CreateDbContext())
    {
      var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == currentTemplateRevisionId);
      var reviewed = await WorkflowTemplateService.ReviewAsync(db, PbcSeed.Actor(f.Reviewer, "Partner"), templateId,
        revision.Id, revision.Revision, "Independently review revised annual task instructions.");
      Assert.True(reviewed.Succeeded, reviewed.Message);
    }
    await using (var db = host.CreateDbContext())
    {
      var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == currentTemplateRevisionId);
      var published = await WorkflowTemplateService.PublishAsync(db, PbcSeed.Actor(f.Reviewer, "Partner"), templateId,
        revision.Id, revision.Revision, "Publish revised annual task instructions.");
      Assert.True(published.Succeeded, published.Message);
      var priorPlan = await db.WorkflowTaskPlanReceipts.AsNoTracking().SingleAsync(x => x.EngagementId == sourceId);
      Assert.Equal((templateRevisionId, 1), (priorPlan.TemplateRevisionId, priorPlan.TaskCount));
      Assert.Equal(1, await db.WorkTasks.CountAsync(x => x.EngagementId == sourceId));
    }
    var staleResponse = await page.RunAndWaitForResponseAsync(
      () => confirm.ClickAsync(),
      response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath ==
        $"/api/ui/clients/{f.ClientId}/engagement-creation");
    Assert.Equal(409, staleResponse.Status);
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Reviewed engagement intent", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByLabel("I reviewed this exact client, service, profile and period.", new() { Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Engagement creation receipt", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByText("This record changed since you loaded it. Refresh and review the current revision.",
      new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.Empty(await db.EngagementCreations.AsNoTracking().ToListAsync());
      Assert.False(await db.Engagements.AsNoTracking().AnyAsync(x => x.PracticeClientId == f.ClientId &&
        x.PeriodStart == "2027-01-01" && x.PeriodEnd == "2027-12-31"));
    }

    await using (var db = host.CreateDbContext())
    {
      var oldContact = await db.ClientContacts.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId &&
        x.PracticeClientId == f.ClientId && x.FullName == "Synthetic annual liaison");
      var deactivated = await PracticeCrmService.UpdateClientContactAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new(oldContact.Id, oldContact.FullName, oldContact.Email, oldContact.Role,
          Primary: oldContact.Primary, IsActive: false));
      Assert.True(deactivated.Succeeded, deactivated.Message);
    }

    // Explicitly discard the refused stale fields so the page's normal beforeunload guard stays inactive during refresh.
    await page.GetByRole(AriaRole.Button, new() { Name = "Discard engagement edits", Exact = true }).ClickAsync();
    // Reload the authorized route so the annual source query is current before selecting the new revision.
    await page.GotoAsync(origin + path);
    annual = page.GetByRole(AriaRole.Region, new() { Name = "Annual engagement preparation", Exact = true });
    await Assertions.Expect(annual).Not.ToContainTextAsync("Synthetic annual liaison");
    await Assertions.Expect(annual).ToContainTextAsync("No eligible contact suggestion is available.");
    var templateChoice = page.GetByLabel("Published task template reference", new() { Exact = true });
    await Assertions.Expect(templateChoice.Locator($"option[value='{templateRevisionId}']")).ToHaveCountAsync(0);
    await Assertions.Expect(templateChoice.Locator($"option[value='{currentTemplateRevisionId}']")).ToHaveCountAsync(1);
    await annual.GetByRole(AriaRole.Button, new() { Name = "Use annual suggestion", Exact = true }).ClickAsync();
    await Assertions.Expect(templateChoice).ToHaveValueAsync(currentTemplateRevisionId.ToString());
    await Assertions.Expect(page.GetByLabel("Period start", new() { Exact = true })).ToHaveValueAsync("2027-01-01");
    if (role == "Manager")
    {
      // Preserve an annual draft through normal navigation, then restore it on the manual route.
      await page.GetByRole(AriaRole.Link, new() { Name = "Client profile", Exact = true }).ClickAsync();
      await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button,
        new() { Name = "Save draft and continue", Exact = true }).ClickAsync();
      await page.GetByRole(AriaRole.Link, new() { Name = "Review new engagement", Exact = true }).ClickAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Restore engagement draft", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByLabel("Period start", new() { Exact = true })).ToHaveValueAsync("2027-01-01");
      await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review engagement", Exact = true })).ToBeDisabledAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Reviewed engagement intent", Exact = true })).ToHaveCountAsync(0);
      var returnSource = page.GetByRole(AriaRole.Link,
        new() { Name = "Return to the retained annual source to review again", Exact = true });
      await Assertions.Expect(returnSource).ToHaveAttributeAsync("href", path);
      await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "annual-source-safety");
      await returnSource.ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Annual engagement preparation", Exact = true })).ToContainTextAsync(sourceId.ToString());
      await Assertions.Expect(page.GetByLabel("Period start", new() { Exact = true })).ToHaveValueAsync("2027-01-01");
      await Assertions.Expect(page.GetByLabel("Published task template reference", new() { Exact = true })).ToHaveValueAsync(currentTemplateRevisionId.ToString());
      await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review engagement", Exact = true })).ToBeEnabledAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Reviewed engagement intent", Exact = true })).ToHaveCountAsync(0);
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Review engagement", Exact = true }).ClickAsync();
    review = page.GetByRole(AriaRole.Region, new() { Name = "Reviewed engagement intent", Exact = true });
    await Assertions.Expect(review).ToContainTextAsync(sourceId.ToString());
    confirm = review.GetByRole(AriaRole.Button, new() { Name = "Confirm blocked creation", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    await page.GetByLabel("I reviewed this exact client, service, profile and period.", new() { Exact = true }).CheckAsync();
    var createResponse = await page.RunAndWaitForResponseAsync(
      () => confirm.ClickAsync(),
      response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath ==
        $"/api/ui/clients/{f.ClientId}/engagement-creation");
    Assert.Equal(200, createResponse.Status);
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Engagement creation receipt", Exact = true })).ToContainTextAsync(sourceId.ToString());
    await using (var db = host.CreateDbContext())
    {
      var receipt = await db.EngagementCreations.AsNoTracking().SingleAsync();
      var created = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == receipt.EngagementId);
      Assert.Equal(("Draft", true, "2027-01-01", "2027-12-31"),
        (created.Status, created.ProfessionalWorkBlocked, created.PeriodStart, created.PeriodEnd));
      Assert.Contains(currentTemplateRevisionId.ToString(), receipt.InputJson);
      await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Review current-year task plan", Exact = true }))
        .ToHaveAttributeAsync("href", $"/app/engagements/{created.Id}?templateRevisionId={currentTemplateRevisionId}#engagement-task-plan");
      await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Review current-year continuance", Exact = true }))
        .ToHaveAttributeAsync("href", $"/app/clients/{f.ClientId}/assessment?engagementId={created.Id}");
      Assert.False(await db.AcceptanceDecisions.AnyAsync(x => x.EngagementId == created.Id));
      Assert.Empty(await db.RoleGrants.AsNoTracking().Where(x => x.EngagementId == created.Id).ToListAsync());
      Assert.Empty(await db.EngagementActivations.ToListAsync());
      Assert.Equal("2026-12-31", (await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == sourceId)).PeriodEnd);
    }
    if (role == "Manager")
    {
      await page.GetByRole(AriaRole.Link, new() { Name = "Review current-year task plan", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Radio, new() { Name = "Synthetic annual tasks" })).ToBeCheckedAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Preview task plan", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "1. Prepare fresh annual work", Exact = true })).ToBeVisibleAsync();
      var createTasks = page.GetByRole(AriaRole.Button, new() { Name = "Create operational tasks", Exact = true });
      await Assertions.Expect(createTasks).ToBeDisabledAsync();
      await page.GetByLabel("I reviewed this exact plan, staffing, dates, and template revision.", new() { Exact = true }).CheckAsync();
      await createTasks.ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Plan receipt confirmed", Exact = true })).ToBeVisibleAsync();
      await using var db = host.CreateDbContext();
      var created = await db.EngagementCreations.AsNoTracking().SingleAsync();
      var plan = await db.WorkflowTaskPlanReceipts.AsNoTracking().SingleAsync(x => x.EngagementId == created.EngagementId);
      Assert.Equal((created.EngagementId, currentTemplateRevisionId, 1), (plan.EngagementId, plan.TemplateRevisionId, plan.TaskCount));
      var priorPlan = await db.WorkflowTaskPlanReceipts.AsNoTracking().SingleAsync(x => x.EngagementId == sourceId);
      Assert.Equal((templateRevisionId, 1), (priorPlan.TemplateRevisionId, priorPlan.TaskCount));
      Assert.Equal(1, await db.WorkTasks.CountAsync(x => x.EngagementId == sourceId));
      Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == created.EngagementId)).ProfessionalWorkBlocked);
    }
    await page.GetByRole(AriaRole.Link, new() { Name = "Client profile", Exact = true }).ClickAsync();
    var table = page.GetByRole(AriaRole.Table, new() { Name = "Authorized client engagements", Exact = true });
    await Assertions.Expect(table).ToBeVisibleAsync();
    await Assertions.Expect(table.GetByRole(AriaRole.Cell, new() { Name = "2027-01-01 to 2027-12-31", Exact = true })).ToBeVisibleAsync();
    Assert.Empty(errors);
  }
}
