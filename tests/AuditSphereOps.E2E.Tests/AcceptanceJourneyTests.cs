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
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-ACCEPTANCE-PATH-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var clientId = host.Fixture.ClientId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(partner);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner"));
      var templateId = Guid.NewGuid();
      var continuanceTemplateId = Guid.NewGuid();
      db.QuestionnaireTemplates.Add(new QuestionnaireTemplate { Id = templateId, Bank = "CE", Version = "JOURNEY-1", Name = "Journey bank", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
      db.QuestionnaireTemplates.Add(new QuestionnaireTemplate { Id = continuanceTemplateId, Bank = "RV", Version = "JOURNEY-1", Name = "Continuance delta bank", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
      db.QuestionDefinitions.AddRange(
        new QuestionDefinition { Id = Guid.NewGuid(), TemplateId = templateId, QuestionCode = "CE-T1", Section = "A.1", Category = "Identity", PromptText = "Has the client's legal existence been verified?", RequiresEvidence = true, AdverseAnswer = "NO", SortOrder = 1 },
        new QuestionDefinition { Id = Guid.NewGuid(), TemplateId = templateId, QuestionCode = "CE-T2", Section = "A.4", Category = "AML", PromptText = "Are there sanctions matches requiring action?", AdverseAnswer = "YES", SortOrder = 2 },
        new QuestionDefinition { Id = Guid.NewGuid(), TemplateId = continuanceTemplateId, QuestionCode = "RV-T1", Section = "R.1", Category = "Continuance", PromptText = "Have there been material changes since the prior evaluation?", EscalatesOnChange = true, SortOrder = 1 });
      await db.SaveChangesAsync();
    }

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
    await Assertions.Expect(profile).ToContainTextAsync("PBC TEST CLIENT");
    var progress = page.GetByRole(AriaRole.Region, new() { Name = "Evaluation progress", Exact = true });
    await Assertions.Expect(progress).ToContainTextAsync("0 of 2 questions answered");

    // An evidence-bearing answer cannot be reviewed until the required reference is entered.
    var identity = page.GetByRole(AriaRole.Region, new() { Name = "CE-T1 assessment question", Exact = true });
    await identity.GetByLabel("Answer for CE-T1", new() { Exact = true }).SelectOptionAsync("Yes");
    await identity.GetByRole(AriaRole.Button, new() { Name = "Review answer for CE-T1", Exact = true }).ClickAsync();
    await Assertions.Expect(identity.Locator("[role='alert']").First).ToContainTextAsync("evidence reference");
    await identity.GetByLabel("Evidence reference (required)", new() { Exact = true }).FillAsync("REG-CERT-2026-11");
    await ConfirmReviewedActionAsync(identity.GetByRole(AriaRole.Button, new() { Name = "Review answer for CE-T1", Exact = true }));
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "CE-T1 assessment question", Exact = true }))
      .ToContainTextAsync("Recorded: Yes");

    // An adverse answer stays a blocker until a requested specialist review is cleared with evidence.
    var sanctions = page.GetByRole(AriaRole.Region, new() { Name = "CE-T2 assessment question", Exact = true });
    await sanctions.GetByLabel("Answer for CE-T2", new() { Exact = true }).SelectOptionAsync("Yes");
    await ConfirmReviewedActionAsync(sanctions.GetByRole(AriaRole.Button, new() { Name = "Review answer for CE-T2", Exact = true }));
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
    await using (var db = host.CreateDbContext())
    {
      db.EvaluationResponses.Add(new EvaluationResponse
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PracticeClientId = clientId,
        Bank = "RV", QuestionId = "RV-T1", Answer = "No", EvidenceReference = "SYNTHETIC-PRIOR-CYCLE-EVIDENCE",
        Generation = 1, Revision = 1, AnsweredByUserId = partner.Id, AnsweredAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    await ConfirmReviewedActionAsync(page.GetByRole(AriaRole.Button, new() { Name = "Review continuance", Exact = true }));
    await Assertions.Expect(page.GetByText("CONTINUANCE · Evaluation 2", new() { Exact = true })).ToBeVisibleAsync();
    var continuanceQuestion = page.GetByRole(AriaRole.Region, new() { Name = "RV-T1 assessment question", Exact = true });
    await Assertions.Expect(continuanceQuestion).ToContainTextAsync("Prior-cycle answer: No");
    await Assertions.Expect(continuanceQuestion).ToContainTextAsync("Evidence reference: SYNTHETIC-PRIOR-CYCLE-EVIDENCE");
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "CE-T1 assessment question", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(progress).ToContainTextAsync("0 of 1 questions answered");

    await using (var db = host.CreateDbContext())
    {
      var decision = await db.AcceptanceDecisions.AsNoTracking()
        .SingleAsync(x => x.PracticeClientId == clientId && x.Decision == "Accepted");
      Assert.Equal("Accepted", decision.Decision);
      Assert.Equal(AcceptancePaths.NewClient, decision.Path);
      Assert.Equal("AccountingOnly", decision.ServiceRoute);
      Assert.Equal("Cleared after the sanctions review.", decision.Rationale);
      var receipts = await db.AssessmentCommandReceipts.Where(x => x.ClientId == clientId).ToListAsync();
      Assert.Equal(7, receipts.Count);
      var continuance = Assert.Single(receipts, x => x.Kind == "CONTINUANCE");
      Assert.Equal((1L, 2L), (continuance.Generation, continuance.ResultGeneration));
    }

    Assert.Empty(diagnostics);
  }
}
