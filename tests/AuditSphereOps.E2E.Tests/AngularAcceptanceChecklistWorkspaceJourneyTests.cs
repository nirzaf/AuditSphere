using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularAcceptanceChecklistWorkspaceJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true",
    ["AngularUi__CanonicalRoutes"] = "true"
  };

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-ACCEPTANCE-WORKSPACE-01")]
  public async Task StaffChecklistShowsScopedWorkspaceProtectsDraftOnRefreshAndClearsAfterRevocation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-ACCEPTANCE-WORKSPACE-01");
    var f = host.Fixture;
    var staff = PbcSeed.User(f.FirmId, "Staff");
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var reviewId = Guid.NewGuid();
    const string privateRegistration = "SYN-PAR-002-ACCEPTANCE-REGISTRATION";
    const string recordedEvidence = "SYN-PAR-002-RECORDED-ANSWER-EVIDENCE";
    await using (var db = host.CreateDbContext())
    {
      var priorDecisionId = await AssessmentParitySeed.PopulateAsync(db, f);
      (await db.PracticeClients.SingleAsync(x => x.Id == f.ClientId)).RegistrationNumber = privateRegistration;
      db.Users.AddRange(staff, manager);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, staff, "Staff", clientId: f.ClientId),
        PbcSeed.Grant(f.FirmId, manager, "Manager", clientId: f.ClientId));
      db.EvaluationResponses.Add(new EvaluationResponse
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Bank = "CE", QuestionId = "CE-001", Answer = "No", EvidenceReference = recordedEvidence,
        AnsweredByUserId = staff.Id, AnsweredAt = DateTimeOffset.UtcNow, Generation = 2, Revision = 1
      });
      db.SpecialistClearances.Add(new SpecialistClearance
      {
        Id = reviewId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Area = "Independence", SpecialistName = "Synthetic independence specialist",
        Status = "PENDING", CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientWorkspaces.Add(new ClientWorkspace
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        AcceptanceDecisionId = priorDecisionId, Purpose = "PRIMARY",
        LogicalKey = "SYN-PAR-002-ACCEPTANCE-WORKSPACE", State = ClientWorkspaceStates.WaitingForIntegration,
        Revision = 1, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var staffOrigin = await host.StartApiForIdentityAsync(staff, Angular);
    var managerOrigin = await host.StartApiForIdentityAsync(manager, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using (var context = await browser.NewContextAsync())
    {
      var page = await context.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);
      var assessmentReads = 0;
      await page.RouteAsync("**/api/ui/clients/" + f.ClientId + "/assessment**", async route =>
      {
        if (route.Request.Method == "GET") assessmentReads++;
        await route.ContinueAsync();
      });
      await page.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
        "/app/clients/" + f.ClientId.ToString("D") + "/assessment"));

      await Assertions.Expect(page.GetByRole(AriaRole.Heading,
        new() { Name = "Client acceptance checklist", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Client assessment profile", Exact = true })).ToContainTextAsync(privateRegistration);
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Client workspace status", Exact = true }))
        .ToContainTextAsync(ClientWorkspaceStates.WaitingForIntegration);
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Evaluation progress", Exact = true })).ToContainTextAsync("1 of 62 questions answered");
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Independence specialist review", Exact = true }))
        .ToContainTextAsync("PENDING");
      await Assertions.Expect(page.GetByRole(AriaRole.Button,
        new() { Name = "Review answer for CE-001", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Button,
        new() { Name = "Review specialist result for Independence", Exact = true })).ToHaveCountAsync(0);
      await Assertions.Expect(page.GetByRole(AriaRole.Button,
        new() { Name = "Review Partner decision", Exact = true })).ToHaveCountAsync(0);

      var question = page.GetByRole(AriaRole.Region,
        new() { Name = "CE-001 assessment question", Exact = true });
      await question.GetByLabel("Answer for CE-001", new() { Exact = true }).SelectOptionAsync("Yes");
      await question.GetByLabel("Evidence reference (required)", new() { Exact = true })
        .FillAsync("SYN-PAR-002-UNSUBMITTED-EVIDENCE");
      var readsBeforePrompt = assessmentReads;
      await page.GetByRole(AriaRole.Button,
        new() { Name = "Refresh current evaluation", Exact = true }).ClickAsync();
      var discardDialog = page.GetByRole(AriaRole.Dialog);
      await Assertions.Expect(discardDialog.GetByRole(AriaRole.Heading,
        new() { Name = "Unsubmitted assessment edits", Exact = true })).ToBeVisibleAsync();
      await discardDialog.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync();
      await Assertions.Expect(question.GetByLabel("Answer for CE-001", new() { Exact = true }))
        .ToHaveValueAsync("Yes");
      Assert.Equal(readsBeforePrompt, assessmentReads);

      await page.GetByRole(AriaRole.Button,
        new() { Name = "Refresh current evaluation", Exact = true }).ClickAsync();
      await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button,
        new() { Name = "Discard edits and continue", Exact = true }).ClickAsync();
      await Assertions.Expect(question.GetByLabel("Answer for CE-001", new() { Exact = true }))
        .ToHaveValueAsync("No");
      await Assertions.Expect(question.GetByLabel("Evidence reference (required)", new() { Exact = true }))
        .ToHaveValueAsync(recordedEvidence);

      await using (var db = host.CreateDbContext())
        await db.RoleGrants.Where(x => x.UserId == staff.Id && x.Role == "Staff" &&
          x.ClientId == f.ClientId && x.RevokedAt == null)
          .ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
      await page.GetByRole(AriaRole.Button,
        new() { Name = "Refresh current evaluation", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Acceptance unavailable");
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Client assessment profile", Exact = true })).ToHaveCountAsync(0);
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "CE-001 assessment question", Exact = true })).ToHaveCountAsync(0);
      var body = await page.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privateRegistration, body, StringComparison.Ordinal);
      Assert.DoesNotContain(recordedEvidence, body, StringComparison.Ordinal);
      Assert.Empty(errors);
    }

    await using (var context = await browser.NewContextAsync())
    {
      var page = await context.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);
      await page.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
        "/app/clients/" + f.ClientId.ToString("D") + "/assessment"));
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Independence specialist review", Exact = true }))
        .ToContainTextAsync("Synthetic independence specialist");
      await Assertions.Expect(page.GetByRole(AriaRole.Button,
        new() { Name = "Review specialist result for Independence", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Button,
        new() { Name = "Review Partner decision", Exact = true })).ToHaveCountAsync(0);
      await Assertions.Expect(page.GetByRole(AriaRole.Button,
        new() { Name = "Review continuance", Exact = true })).ToHaveCountAsync(0);
      Assert.Empty(errors);
    }
  }
}
