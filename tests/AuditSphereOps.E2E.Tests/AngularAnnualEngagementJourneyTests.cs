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
    Guid sourceId; Guid templateRevisionId;
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
      var reviewed = await WorkflowTemplateService.ReviewAsync(db, PbcSeed.Actor(f.Reviewer, "Partner"), revision.TemplateId,
        revision.Id, revision.Revision, "Independently review annual task instructions.");
      Assert.True(reviewed.Succeeded, reviewed.Message);
      var published = await WorkflowTemplateService.PublishAsync(db, PbcSeed.Actor(f.Reviewer, "Partner"), revision.TemplateId,
        revision.Id, revision.Revision, "Publish independent annual task instructions.");
      Assert.True(published.Succeeded, published.Message);
    }
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
      { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    var path = "/app/clients/" + f.ClientId + "/engagements/new?annualSourceEngagementId=" + sourceId;
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/clients/" + f.ClientId));
    await page.GetByRole(AriaRole.Row).Filter(new() { HasText = sourceId.ToString() })
      .GetByRole(AriaRole.Link, new() { Name = "Prepare next annual period", Exact = true }).ClickAsync();
    var annual = page.GetByRole(AriaRole.Region, new() { Name = "Annual engagement preparation", Exact = true });
    await Assertions.Expect(annual).ToContainTextAsync("Partner continuance");
    await Assertions.Expect(annual).ToContainTextAsync("2027-01-01 to 2027-12-31");
    await Assertions.Expect(annual).ToContainTextAsync("Synthetic annual liaison");
    await Assertions.Expect(annual).ToContainTextAsync(role == "Manager"
      ? "No approved mapping exists for the source period."
      : "Mapping references require accounting read access to the source engagement.");
    await annual.GetByRole(AriaRole.Button, new() { Name = "Use annual suggestion", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByLabel("Period start", new() { Exact = true })).ToHaveValueAsync("2027-01-01");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review engagement", Exact = true }).ClickAsync();
    var review = page.GetByRole(AriaRole.Region, new() { Name = "Reviewed engagement intent", Exact = true });
    await Assertions.Expect(review).ToContainTextAsync(sourceId.ToString());
    var confirm = review.GetByRole(AriaRole.Button, new() { Name = "Confirm blocked creation", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    await page.GetByLabel("I reviewed this exact client, service, profile and period.", new() { Exact = true }).CheckAsync();
    await confirm.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Engagement creation receipt", Exact = true })).ToContainTextAsync(sourceId.ToString());
    await using (var db = host.CreateDbContext())
    {
      var receipt = await db.EngagementCreations.AsNoTracking().SingleAsync();
      var created = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == receipt.EngagementId);
      Assert.Equal(("Draft", true, "2027-01-01", "2027-12-31"),
        (created.Status, created.ProfessionalWorkBlocked, created.PeriodStart, created.PeriodEnd));
      await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Review current-year continuance", Exact = true }))
        .ToHaveAttributeAsync("href", $"/app/clients/{f.ClientId}/assessment?engagementId={created.Id}");
      Assert.False(await db.AcceptanceDecisions.AnyAsync(x => x.EngagementId == created.Id));
      Assert.Empty(await db.EngagementActivations.ToListAsync());
      Assert.Equal("2026-12-31", (await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == sourceId)).PeriodEnd);
    }
    if (role == "Manager")
    {
      await page.GetByRole(AriaRole.Link, new() { Name = "Review current-year task plan", Exact = true }).ClickAsync();
      await page.GetByRole(AriaRole.Radio, new() { Name = "Synthetic annual tasks" }).CheckAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Preview task plan", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "1. Prepare fresh annual work", Exact = true })).ToBeVisibleAsync();
      var createTasks = page.GetByRole(AriaRole.Button, new() { Name = "Create operational tasks", Exact = true });
      await Assertions.Expect(createTasks).ToBeDisabledAsync();
      await OwnedBrowserFixtureHandoff.WaitAsync(origin, path);
      await page.GetByLabel("I reviewed this exact plan, staffing, dates, and template revision.", new() { Exact = true }).CheckAsync();
      await createTasks.ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Plan receipt confirmed", Exact = true })).ToBeVisibleAsync();
      await using var db = host.CreateDbContext();
      var created = await db.EngagementCreations.AsNoTracking().SingleAsync();
      var plan = await db.WorkflowTaskPlanReceipts.AsNoTracking().SingleAsync();
      Assert.Equal((created.EngagementId, templateRevisionId, 1), (plan.EngagementId, plan.TemplateRevisionId, plan.TaskCount));
      Assert.False(await db.WorkTasks.AnyAsync(x => x.EngagementId == sourceId));
      Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == created.EngagementId)).ProfessionalWorkBlocked);
    }
    await page.GetByRole(AriaRole.Link, new() { Name = "Client profile", Exact = true }).ClickAsync();
    var table = page.GetByRole(AriaRole.Table, new() { Name = "Authorized client engagements", Exact = true });
    await Assertions.Expect(table).ToBeVisibleAsync();
    await Assertions.Expect(table.GetByRole(AriaRole.Cell, new() { Name = "2027-01-01 to 2027-12-31", Exact = true })).ToBeVisibleAsync();
    Assert.Empty(errors);
  }
}
