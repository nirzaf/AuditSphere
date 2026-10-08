using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAssessmentDecisionRouteJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "AS-PAR-002-ANG-ASSESS-ID-01")]
  public async Task ExactDecisionIdRequiresItsClientGrantAndKeepsAssessmentDetailsScoped(bool canonical)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-ASSESS-ID-01");
    var f = host.Fixture;
    var partner = PbcSeed.User(f.FirmId, "Staff");
    var unrelatedManager = PbcSeed.User(f.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateRegistration = "SYN-PAR-002-PRIVATE-ASSESSMENT-REGISTRATION";
    Guid decisionId;
    await using (var db = host.CreateDbContext())
    {
      decisionId = await AssessmentParitySeed.PopulateAsync(db, f);
      await db.QuestionnaireTemplates.Where(x => x.Bank == "CE" && x.IsActive)
        .ExecuteUpdateAsync(x => x.SetProperty(template => template.IsActive, false));
      var templateId = Guid.NewGuid();
      db.QuestionnaireTemplates.Add(new QuestionnaireTemplate
      {
        Id = templateId, Bank = "CE", Version = "AS-PAR-002-v1", Name = "Synthetic scoped assessment",
        IsActive = true, CreatedAt = DateTimeOffset.UtcNow
      });
      db.QuestionDefinitions.AddRange(
        new QuestionDefinition { Id = Guid.NewGuid(), TemplateId = templateId, QuestionCode = "CE-UI-1", Section = "A", Category = "Acceptance", PromptText = "Synthetic question one", SortOrder = 1 },
        new QuestionDefinition { Id = Guid.NewGuid(), TemplateId = templateId, QuestionCode = "CE-UI-2", Section = "A", Category = "Acceptance", PromptText = "Synthetic question two", SortOrder = 2 });
      (await db.PracticeClients.SingleAsync(x => x.Id == f.ClientId)).RegistrationNumber = privateRegistration;
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = f.FirmId,
        LegalName = "SYN-PAR-002-UNRELATED-ASSESSMENT-CLIENT", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.AddRange(partner, unrelatedManager);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, partner, "Partner", clientId: f.ClientId),
        PbcSeed.Grant(f.FirmId, unrelatedManager, "Manager", clientId: unrelatedClientId));
      db.EvaluationResponses.Add(new EvaluationResponse
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Bank = "CE", QuestionId = "CE-UI-1", Answer = "Yes", AnsweredByUserId = partner.Id,
        AnsweredAt = DateTimeOffset.UtcNow, Generation = 2
      });
      db.SpecialistClearances.Add(new SpecialistClearance
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Area = "Independence", SpecialistName = "Synthetic scoped specialist", Status = "HOLD",
        EvidenceReference = "SYN-PAR-002-SPECIALIST-HOLD", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = canonical.ToString()
    };
    var prefix = canonical ? string.Empty : "/ui";
    var partnerOrigin = await host.StartApiForIdentityAsync(partner, settings);
    var managerOrigin = await host.StartApiForIdentityAsync(unrelatedManager, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using (var partnerContext = await browser.NewContextAsync())
    {
      var page = await partnerContext.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);
      await page.GotoAsync(partnerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
        prefix + "/app/assessments/" + decisionId.ToString("D")));
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Recorded professional decision", Exact = true }))
        .ToContainTextAsync("Exact synthetic historical decision");
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Client assessment profile", Exact = true }))
        .ToContainTextAsync(privateRegistration);
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Evaluation progress", Exact = true }))
        .ToContainTextAsync("1 of 2 questions answered");
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Evaluation progress", Exact = true }))
        .ToContainTextAsync("0 of 1 specialist reviews cleared");
      Assert.Contains("decisionId=" + decisionId, page.Url, StringComparison.OrdinalIgnoreCase);
      foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
      {
        await page.SetViewportSizeAsync(width, 900);
        Assert.True(await page.EvaluateAsync<bool>(
          "() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
          $"Assessment route overflows at {width}px.");
        if (width <= 700)
        {
          var mainBox = await page.Locator("#main").BoundingBoxAsync();
          Assert.NotNull(mainBox);
          Assert.True(mainBox!.Width >= width - 64,
            $"Assessment content is squeezed to {mainBox.Width}px at {width}px.");
        }
        await Assertions.Expect(page.GetByRole(AriaRole.Region,
          new() { Name = "Client assessment profile", Exact = true }))
          .ToContainTextAsync(privateRegistration);
      }
      await page.Keyboard.PressAsync("Tab");
      Assert.True(await page.Locator(":focus-visible").CountAsync() > 0);
      Assert.Empty(errors);
    }

    await using (var managerContext = await browser.NewContextAsync())
    {
      var page = await managerContext.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);
      await page.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
        prefix + "/app/assessments/" + decisionId.ToString("D")));
      await Assertions.Expect(page.GetByRole(AriaRole.Alert))
        .ToContainTextAsync("Access denied.");
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Client assessment profile", Exact = true })).ToHaveCountAsync(0);
      var body = await page.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privateRegistration, body, StringComparison.Ordinal);
      Assert.DoesNotContain("Exact synthetic historical decision", body, StringComparison.Ordinal);

      await using var exactDecision = await managerContext.APIRequest.GetAsync(managerOrigin +
        "/api/ui/assessments/" + decisionId.ToString("D"));
      await using var guessedDecision = await managerContext.APIRequest.GetAsync(managerOrigin +
        "/api/ui/assessments/" + Guid.NewGuid().ToString("D"));
      Assert.Equal(403, exactDecision.Status);
      Assert.Equal(403, guessedDecision.Status);
      Assert.Equal(await exactDecision.TextAsync(), await guessedDecision.TextAsync());
      Assert.DoesNotContain(privateRegistration, await exactDecision.TextAsync(), StringComparison.Ordinal);
      Assert.Empty(errors);
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task LegacyDecisionDeepLinkEnforcesPartnerGrantAndRecoversLostDecisionOnce(bool canonical)
  {
    await using var host = await OwnedHost.StartAsync(
      startWorker: false, caseId: "ANGULAR-ASSESSMENT-DECISION-LINK");
    var f = host.Fixture;
    var senior = PbcSeed.User(f.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      await AssessmentParitySeed.PopulateAsync(db, f);
      db.Users.Add(senior);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, senior, "Senior", clientId: f.ClientId));
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = canonical.ToString()
    };
    var seniorOrigin = await host.StartApiForIdentityAsync(senior, settings);
    var origin = await host.StartApiForIdentityAsync(f.Admin, settings);
    var prefix = canonical ? string.Empty : "/ui";
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    // Both the ordinary detail route and the legacy Partner decision deep link must
    // preserve the legacy staff gate. The decision URL must not reveal assessment data.
    await using (var seniorContext = await browser.NewContextAsync())
    {
      var seniorPage = await seniorContext.NewPageAsync();
      await seniorPage.GotoAsync(seniorOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
        prefix + "/app/assessments/" + f.ClientId + "/decision"));
      await Assertions.Expect(seniorPage.GetByRole(AriaRole.Alert))
        .ToContainTextAsync("Access denied");
      await Assertions.Expect(seniorPage.GetByRole(AriaRole.Region,
        new() { Name = "Client assessment profile", Exact = true })).ToHaveCountAsync(0);
      await Assertions.Expect(seniorPage.GetByRole(AriaRole.Button,
        new() { Name = "Review Partner decision", Exact = true })).ToHaveCountAsync(0);
      await seniorPage.GotoAsync(seniorOrigin + prefix + "/app/assessments/" + f.ClientId);
      await Assertions.Expect(seniorPage.GetByRole(AriaRole.Alert))
        .ToContainTextAsync("Access denied");
      await Assertions.Expect(seniorPage.GetByRole(AriaRole.Region,
        new() { Name = "Client assessment profile", Exact = true })).ToHaveCountAsync(0);
    }

    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
      prefix + "/app/assessments/" + f.ClientId + "/decision"));
    await Assertions.Expect(page.GetByRole(AriaRole.Region,
      new() { Name = "Client assessment profile", Exact = true })).ToContainTextAsync("PBC TEST CLIENT");
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Review Partner decision", Exact = true })).ToBeVisibleAsync();
    Assert.Contains("/app/clients/" + f.ClientId + "/assessment", page.Url, StringComparison.Ordinal);

    await page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true }).ClickAsync();
    await page.GetByLabel("Service route", new() { Exact = true }).FillAsync("AccountingOnly");
    await page.GetByLabel("Decision", new() { Exact = true }).SelectOptionAsync("Declined");
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Deep-link reviewed decision recovery");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true }).ClickAsync();

    var review = page.GetByRole(AriaRole.Region, new() { Name = "Exact assessment action review", Exact = true });
    await Assertions.Expect(review).ToContainTextAsync("Deep-link reviewed decision recovery");
    var assent = page.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this exact assessment action and its effects.", Exact = true });
    var confirm = page.GetByRole(AriaRole.Button,
      new() { Name = "Confirm reviewed assessment action", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();

    var dispatched = 0;
    var commandRoute = "**/api/ui/clients/" + f.ClientId + "/assessment/commands";
    await page.RouteAsync(commandRoute, async route =>
    {
      var response = await route.FetchAsync();
      Assert.Equal(200, response.Status);
      dispatched++;
      await route.AbortAsync("failed");
    });
    await assent.CheckAsync();
    await confirm.ClickAsync();
    var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Assessment request recovery", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await page.ReloadAsync();
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Verify assessment receipt", Exact = true }).ClickAsync();
    var receipt = page.GetByRole(AriaRole.Region, new() { Name = "Retained assessment receipt", Exact = true });
    await Assertions.Expect(receipt).ToContainTextAsync("Deep-link reviewed decision recovery");
    await page.GetByRole(AriaRole.Button, new() { Name = "Acknowledge assessment receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,
      new() { Name = "Recorded professional decision", Exact = true }))
      .ToContainTextAsync("Deep-link reviewed decision recovery");
    await page.UnrouteAsync(commandRoute);

    await using (var proof = host.CreateDbContext())
    {
      Assert.Single(await proof.AssessmentCommandReceipts.Where(x => x.ClientId == f.ClientId && x.Kind == "DECISION").ToListAsync());
      Assert.Single(await proof.AcceptanceDecisions.Where(x => x.PracticeClientId == f.ClientId &&
        x.Rationale == "Deep-link reviewed decision recovery").ToListAsync());
    }
    Assert.Equal(1, dispatched);
    Assert.Empty(errors);
  }
}
