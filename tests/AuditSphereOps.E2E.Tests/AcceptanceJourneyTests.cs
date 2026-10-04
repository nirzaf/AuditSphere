using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// Engagement acceptance in the real UI: answers need evidence, an adverse answer blocks the Partner until a specialist
/// review with evidence is recorded, the decision is recorded, and the engagement is created blocked and then
/// activated by the Partner only from that current unconditional acceptance.
/// </summary>
[Trait("Category", "AuthorizationAndScope")]
public sealed class AcceptanceJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-ACCEPTANCE-PATH-01")]
  public async Task AdverseAnswerNeedsReview_ThenPartnerAcceptsCreatesAndActivatesTheEngagement()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-ACCEPTANCE-PATH-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var clientId = host.Fixture.ClientId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(partner);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner"));
      var templateId = Guid.NewGuid();
      db.QuestionnaireTemplates.Add(new QuestionnaireTemplate { Id = templateId, Bank = "CE", Version = "JOURNEY-1", Name = "Journey bank", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
      db.QuestionDefinitions.AddRange(
        new QuestionDefinition { Id = Guid.NewGuid(), TemplateId = templateId, QuestionCode = "CE-T1", Section = "A.1", Category = "Identity", PromptText = "Has the client's legal existence been verified?", RequiresEvidence = true, AdverseAnswer = "NO", SortOrder = 1 },
        new QuestionDefinition { Id = Guid.NewGuid(), TemplateId = templateId, QuestionCode = "CE-T2", Section = "A.4", Category = "AML", PromptText = "Are there sanctions matches requiring action?", AdverseAnswer = "YES", SortOrder = 2 });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(partner);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    page.Console += (_, message) => diagnostics.Add($"console-{message.Type}: {message.Text}");
    async Task SettleAsync()
    {
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
      await page.WaitForTimeoutAsync(500);
    }

    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/clients/{clientId:D}/assessment")}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Acceptance checklist" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    var checklist = page.GetByRole(AriaRole.Region, new() { Name = "Acceptance checklist" });
    await Assertions.Expect(checklist).ToContainTextAsync("New client onboarding");
    await Assertions.Expect(checklist).ToContainTextAsync("2 outstanding before a Partner can accept");

    // An evidence-bearing question refuses a bare answer, then accepts it with a reference.
    await page.GetByLabel("Answer for CE-T1", new() { Exact = true }).SelectOptionAsync("Yes");
    await page.GetByRole(AriaRole.Button, new() { Name = "Save answer for CE-T1" }).ClickAsync();
    await Assertions.Expect(page.Locator(".command-result", new() { HasText = "evidence" })).ToBeVisibleAsync();
    await page.GetByLabel("Evidence reference for CE-T1", new() { Exact = true }).FillAsync("REG-CERT-2026-11");
    await page.GetByLabel("Evidence reference for CE-T1", new() { Exact = true }).PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Save answer for CE-T1" }).ClickAsync();
    await Assertions.Expect(page.Locator("[data-question='CE-T1']")).ToContainTextAsync("REG-CERT-2026-11");

    // A sanctions match is answered "Yes": every field is filled but the Partner is still blocked.
    await page.GetByLabel("Answer for CE-T2", new() { Exact = true }).SelectOptionAsync("Yes");
    await page.GetByRole(AriaRole.Button, new() { Name = "Save answer for CE-T2" }).ClickAsync();
    await Assertions.Expect(checklist).ToContainTextAsync("CE-T2 is an adverse answer");
    await Assertions.Expect(page.Locator("[data-question='CE-T2']")).ToContainTextAsync("adverse — needs specialist review");

    // The specialist review must be requested, then cleared with evidence.
    await page.GetByLabel("Review area", new() { Exact = true }).SelectOptionAsync("AML");
    await page.GetByLabel("Specialist", new() { Exact = true }).FillAsync("Compliance officer");
    await page.GetByLabel("Specialist", new() { Exact = true }).PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Request specialist review" }).ClickAsync();
    await Assertions.Expect(checklist).ToContainTextAsync("AML review requested.");
    await page.GetByRole(AriaRole.Button, new() { Name = "Clear AML" }).ClickAsync();
    await Assertions.Expect(page.Locator(".command-result", new() { HasText = "evidence reference" })).ToBeVisibleAsync();
    await page.GetByLabel("Review evidence for AML", new() { Exact = true }).FillAsync("SANCTIONS-SCREEN-77: false positive");
    await page.GetByLabel("Review evidence for AML", new() { Exact = true }).PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Clear AML" }).ClickAsync();
    await Assertions.Expect(checklist).ToContainTextAsync("The checklist is complete and cleared.");

    // Partner decision.
    await page.GotoAsync($"{origin}/app/assessments/{clientId:D}/decision");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Partner Acceptance / Continuance Decision" }).WaitForAsync();
    await SettleAsync();
    await page.Locator("#decision-outcome .mud-select").First.ClickAsync();
    await page.Locator(".mud-popover-open").GetByText("Accepted", new() { Exact = true }).ClickAsync();
    await page.Locator("#decision-service").FillAsync("AccountingOnly");
    await page.Locator("#decision-rationale").FillAsync("Cleared after the sanctions review.");
    await page.Locator("#decision-rationale").PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Record Partner Decision" }).ClickAsync();
    await using (var db = host.CreateDbContext())
    {
      var deadline = DateTime.UtcNow.AddSeconds(15);
      AcceptanceDecision? decision = null;
      while (DateTime.UtcNow < deadline && decision is null)
      {
        decision = await db.AcceptanceDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.PracticeClientId == clientId && x.Decision == "Accepted");
        if (decision is null) await Task.Delay(300);
      }
      Assert.NotNull(decision);
      Assert.Equal(AcceptancePaths.NewClient, decision!.Path);
    }

    // Create the engagement (blocked), then activate it as the Partner.
    await page.GotoAsync($"{origin}/app/clients/{clientId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Create engagement" }).WaitForAsync();
    await SettleAsync();
    await page.GetByLabel("Service route (e.g. FinancialStatementAudit)").FillAsync("AccountingOnly");
    await page.GetByLabel("Service profile").FillAsync("ACC-2026");
    await page.GetByLabel("Period start (yyyy-MM-dd)").FillAsync("2026-01-01");
    await page.GetByLabel("Period end (yyyy-MM-dd)").FillAsync("2026-12-31");
    await page.GetByLabel("Period end (yyyy-MM-dd)").PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Create engagement" }).ClickAsync();
    Guid engagementId = Guid.Empty;
    await using (var db = host.CreateDbContext())
    {
      var deadline = DateTime.UtcNow.AddSeconds(15);
      while (DateTime.UtcNow < deadline && engagementId == Guid.Empty)
      {
        engagementId = await db.Engagements.AsNoTracking().Where(x => x.PracticeClientId == clientId && x.ServiceRoute == "AccountingOnly")
          .Select(x => x.Id).SingleOrDefaultAsync();
        if (engagementId == Guid.Empty) await Task.Delay(300);
      }
      Assert.NotEqual(Guid.Empty, engagementId);
      Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId)).ProfessionalWorkBlocked);
    }

    await page.GotoAsync($"{origin}/app/engagements/{engagementId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement activation" }).WaitForAsync();
    await SettleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Activate engagement (Partner)" }).ClickAsync();
    await Assertions.Expect(page.GetByText("Activated on", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 15000 });
    await using (var db = host.CreateDbContext())
      Assert.Equal(("Active", false), ((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId)) is var e ? (e.Status, e.ProfessionalWorkBlocked) : default));

    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    Assert.DoesNotContain(diagnostics, x => x.Contains("unhandled exception on the current circuit", StringComparison.OrdinalIgnoreCase));
  }
}
