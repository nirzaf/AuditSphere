using System.Globalization;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// Exercises the native Angular acceptance workflow against the API host. Acceptance is a human
/// decision: evidence-bearing answers, adverse-answer clearance and fresh exact-action assent are
/// required before a Partner decision can be recorded.
/// </summary>
[Trait("Category", "AuthorizationAndScope")]
public sealed class AcceptanceJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-ACCEPTANCE-PATH-01")]
  public async Task PartnerCompletesEvidenceAndSpecialistReviewBeforeAcceptingClient()
  {
    (Guid FirmId, Guid ClientId, AuditSphereOps.Application.Abstractions.ActorContext Actor) setup = default;
    await using var host = await OwnedHost.StartSupportedAsync("AS-ACCEPTANCE-PATH-01", async database =>
    {
      setup = await SupportedAuditCompletionWorldBuilder.CreateClientBeforeAcceptanceAsync(database, "Partner");
      return setup.FirmId;
    });
    var clientId = setup.ClientId;
    var actor = setup.Actor ?? throw new InvalidOperationException("Supported acceptance setup did not return its actor.");
    AuditSphereOps.Domain.Security.AppUser partner;
    await using (var db = host.CreateDbContext())
      partner = await db.Users.AsNoTracking().SingleAsync(x => x.FirmId == setup.FirmId && x.Id == actor.UserId);
    async Task<AcceptanceChecklist> AnswerPrerequisitesAsync(string bank, params string[] excluded)
    {
      await using var db = host.CreateDbContext();
      var generation = await db.ClientSafetyStates.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Id == clientId)
        .Select(x => x.InputGeneration).SingleAsync();
      var checklist = await AcceptanceChecklistService.LoadAsync(db, actor.FirmId, clientId, null, generation, default);
      Assert.Equal(bank == "CE" ? AcceptancePaths.NewClient : AcceptancePaths.Continuance, checklist.Path);
      Assert.Equal(bank == "CE" ? 62 : 32, checklist.Items.Count);
      Assert.All(checklist.Items, item => Assert.Equal(bank, item.Question.Bank));
      foreach (var item in checklist.Items.Where(item => !excluded.Contains(item.Question.Code)))
      {
        var question = item.Question;
        Assert.Equal("BOOLEAN", question.AnswerType);
        var answer = string.Equals(question.AdverseAnswer, "NO", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
        var evidence = question.Code == "RV-001" ? "SYNTHETIC-PRIOR-CYCLE-EVIDENCE" : $"SYNTHETIC-ACCEPTANCE-{question.Code}";
        var saved = await AcceptanceChecklistService.RecordAnswerAsync(db, actor, clientId, question.Code, answer,
          evidence, expectedGeneration: generation, expectedRevision: item.Answer?.Revision ?? 0);
        Assert.True(saved.Succeeded, saved.Message);
      }
      return await AcceptanceChecklistService.LoadAsync(db, actor.FirmId, clientId, null, generation, default);
    }

    async Task AcceptPreparedContinuanceAsync()
    {
      await using var db = host.CreateDbContext();
      var generation = await db.ClientSafetyStates.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Id == clientId)
        .Select(x => x.InputGeneration).SingleAsync();
      Assert.Equal(2L, generation);
      var request = new AssessmentCommandRequest(Guid.NewGuid(),
        new AssessmentCommandFields("DECISION", generation.ToString(CultureInfo.InvariantCulture),
          ServiceRoute: "AccountingOnly", Decision: "Accepted", Rationale: "Synthetic completed first continuance for retained prior-cycle evidence."));
      var preview = await AssessmentCommandWorkspace.PreviewAsync(db, actor, clientId, request);
      Assert.True(preview.Succeeded, preview.Message);
      var executed = await AssessmentCommandWorkspace.ExecuteAsync(db, actor, clientId,
        request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis, RequestHash = preview.Value.RequestHash });
      Assert.True(executed.Succeeded, executed.Message);
    }

    // Retain the complete official bank; exercise two answers in the browser and prepare the
    // remaining synthetic evidence through the same application owner used by the HTTP boundary.
    var prepared = await AnswerPrerequisitesAsync("CE", "CE-001", "CE-032");
    Assert.Equal(60, prepared.Items.Count(x => x.Answer is not null));

    var origin = await host.StartApiForIdentityAsync(partner, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    page.Console += (_, message) =>
    {
      if (message.Type == "error") diagnostics.Add($"console-error: {message.Text}");
    };

    async Task ConfirmReviewedActionAsync(ILocator reviewButton)
    {
      await reviewButton.ClickAsync();
      var review = page.GetByRole(AriaRole.Region, new() { Name = "Exact assessment action review", Exact = true });
      await Assertions.Expect(review).ToBeVisibleAsync();
      var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed assessment action", Exact = true });
      await Assertions.Expect(confirm).ToBeDisabledAsync();
      await page.GetByLabel("I reviewed this exact assessment action and its effects.", new() { Exact = true }).CheckAsync();
      await confirm.ClickAsync();
      var receipt = page.GetByRole(AriaRole.Region, new() { Name = "Retained assessment receipt", Exact = true });
      await Assertions.Expect(receipt).ToBeVisibleAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Acknowledge assessment receipt", Exact = true }).ClickAsync();
      await Assertions.Expect(receipt).ToHaveCountAsync(0);
    }

    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/clients/{clientId:D}/assessment")}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client acceptance checklist", Exact = true })).ToBeVisibleAsync();
    var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client assessment profile", Exact = true });
    await Assertions.Expect(profile).ToContainTextAsync("Synthetic acceptance client");
    var progress = page.GetByRole(AriaRole.Region, new() { Name = "Evaluation progress", Exact = true });
    await Assertions.Expect(progress).ToContainTextAsync("60 of 62 questions answered");

    // An evidence-bearing answer cannot be reviewed until the required reference is entered.
    var identity = page.GetByRole(AriaRole.Region, new() { Name = "CE-001 assessment question", Exact = true });
    await identity.GetByLabel("Answer for CE-001", new() { Exact = true }).SelectOptionAsync("Yes");
    await identity.GetByRole(AriaRole.Button, new() { Name = "Review answer for CE-001", Exact = true }).ClickAsync();
    await Assertions.Expect(identity.Locator("[role='alert']").First).ToContainTextAsync("evidence reference");
    await identity.GetByLabel("Evidence reference (required)", new() { Exact = true }).FillAsync("REG-CERT-2026-11");
    await ConfirmReviewedActionAsync(identity.GetByRole(AriaRole.Button, new() { Name = "Review answer for CE-001", Exact = true }));
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "CE-001 assessment question", Exact = true }))
      .ToContainTextAsync("Recorded: Yes");

    // An adverse answer stays a blocker until a requested specialist review is cleared with evidence.
    var sanctions = page.GetByRole(AriaRole.Region, new() { Name = "CE-032 assessment question", Exact = true });
    await sanctions.GetByLabel("Answer for CE-032", new() { Exact = true }).SelectOptionAsync("Yes");
    await sanctions.GetByLabel("Evidence reference (required)", new() { Exact = true }).FillAsync("SYNTHETIC-SANCTIONS-SCREEN-2026");
    await ConfirmReviewedActionAsync(sanctions.GetByRole(AriaRole.Button, new() { Name = "Review answer for CE-032", Exact = true }));
    await Assertions.Expect(sanctions).ToContainTextAsync("Adverse answer: specialist clearance is required.");
    await Assertions.Expect(page.GetByText("Checklist blocked", new() { Exact = true })).ToBeVisibleAsync();

    var request = page.GetByRole(AriaRole.Button, new() { Name = "Review specialist request", Exact = true });
    await page.GetByLabel("Review area", new() { Exact = true }).FillAsync("AML");
    await page.GetByLabel("Specialist", new() { Exact = true }).FillAsync("Compliance officer");
    await ConfirmReviewedActionAsync(request);
    var clearance = page.GetByRole(AriaRole.Region, new() { Name = "AML specialist review", Exact = true });
    await Assertions.Expect(clearance).ToContainTextAsync("PENDING");
    await clearance.GetByLabel("Review result", new() { Exact = true }).SelectOptionAsync("HOLD");
    await ConfirmReviewedActionAsync(clearance.GetByRole(AriaRole.Button, new() { Name = "Review specialist result for AML", Exact = true }));
    await Assertions.Expect(clearance).ToContainTextAsync("HOLD");
    await clearance.GetByLabel("Review result", new() { Exact = true }).SelectOptionAsync("CLEARED");
    await clearance.GetByLabel("Evidence reference (required)", new() { Exact = true }).FillAsync("SANCTIONS-SCREEN-77: false positive");
    await ConfirmReviewedActionAsync(clearance.GetByRole(AriaRole.Button, new() { Name = "Review specialist result for AML", Exact = true }));
    await Assertions.Expect(page.GetByText("Checklist ready for human decision", new() { Exact = true })).ToBeVisibleAsync();

    await page.GetByLabel("Service route", new() { Exact = true }).FillAsync("AccountingOnly");
    await page.GetByLabel("Decision", new() { Exact = true }).SelectOptionAsync("Accepted");
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Cleared after the sanctions review.");
    await ConfirmReviewedActionAsync(page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true }));

    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review continuance", Exact = true })).ToBeVisibleAsync();
    await ConfirmReviewedActionAsync(page.GetByRole(AriaRole.Button, new() { Name = "Review continuance", Exact = true }));
    await Assertions.Expect(page.GetByText("CONTINUANCE · Evaluation 2", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(progress).ToContainTextAsync("0 of 32 questions answered");
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "CE-001 assessment question", Exact = true })).ToHaveCountAsync(0);
    // RV is not an eligible bank during NEW_CLIENT. Complete a genuine first continuance before
    // checking retained RV evidence in the next cycle, rather than inserting a historical answer.
    var priorCycle = await AnswerPrerequisitesAsync("RV");
    Assert.True(priorCycle.Ready);
    await AcceptPreparedContinuanceAsync();
    await page.ReloadAsync();
    await ConfirmReviewedActionAsync(page.GetByRole(AriaRole.Button, new() { Name = "Review continuance", Exact = true }));
    await Assertions.Expect(page.GetByText("CONTINUANCE · Evaluation 3", new() { Exact = true })).ToBeVisibleAsync();
    var continuanceQuestion = page.GetByRole(AriaRole.Region, new() { Name = "RV-001 assessment question", Exact = true });
    await Assertions.Expect(continuanceQuestion).ToContainTextAsync("Prior-cycle answer: No");
    await Assertions.Expect(continuanceQuestion).ToContainTextAsync("Evidence reference: SYNTHETIC-PRIOR-CYCLE-EVIDENCE");
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "CE-001 assessment question", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(progress).ToContainTextAsync("0 of 32 questions answered");

    await using (var db = host.CreateDbContext())
    {
      var decision = await db.AcceptanceDecisions.AsNoTracking()
        .SingleAsync(x => x.PracticeClientId == clientId && x.Decision == "Accepted" && x.Generation == 1);
      Assert.Equal("Accepted", decision.Decision);
      Assert.Equal(AcceptancePaths.NewClient, decision.Path);
      Assert.Equal("AccountingOnly", decision.ServiceRoute);
      Assert.Equal("Cleared after the sanctions review.", decision.Rationale);
      var receipts = await db.AssessmentCommandReceipts.Where(x => x.ClientId == clientId).ToListAsync();
      Assert.Equal(9, receipts.Count);
      var continuances = receipts.Where(x => x.Kind == "CONTINUANCE").OrderBy(x => x.Generation).ToArray();
      Assert.Equal(2, continuances.Length);
      Assert.Equal((1L, 2L), (continuances[0].Generation, continuances[0].ResultGeneration));
      Assert.Equal((2L, 3L), (continuances[1].Generation, continuances[1].ResultGeneration));
      var priorDecision = await db.AcceptanceDecisions.AsNoTracking()
        .SingleAsync(x => x.PracticeClientId == clientId && x.Decision == "Accepted" && x.Generation == 2);
      Assert.Equal(AcceptancePaths.Continuance, priorDecision.Path);
    }

    Assert.Empty(diagnostics);
  }
}
