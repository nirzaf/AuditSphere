using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAcceptanceCrossFirmIsolationJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true",
    ["AngularUi__CanonicalRoutes"] = "true"
  };

  [Fact]
  [Trait("Category", "AuthorizationAndScope")]
  [Trait("CaseId", "AS-PAR-002-ANG-ACCEPTANCE-CROSS-FIRM-01")]
  public async Task ForeignAndGuessedClientOrEngagementIdsAreOpaqueAcrossAcceptanceWorkspaces()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-ACCEPTANCE-CROSS-FIRM-01");
    var f = host.Fixture;
    var foreignFirmId = Guid.NewGuid();
    var foreignClientId = Guid.NewGuid();
    var foreignEngagementId = Guid.NewGuid();
    const string privateForeignMarker = "SYN-FOREIGN-ACCEPTANCE-CLIENT-MUST-NOT-LEAK";

    await using (var db = host.CreateDbContext())
    {
      await AssessmentParitySeed.PopulateAsync(db, f);
      await EngagementActivationReviewSeed.PopulateAsync(db, f);
      db.FirmSafetyStates.Add(new AuditSphereOps.Domain.Completion.FirmSafetyState { Id = foreignFirmId });
      db.PracticeClients.Add(new PracticeClient
      {
        Id = foreignClientId,
        FirmId = foreignFirmId,
        LegalName = privateForeignMarker,
        RegistrationNumber = privateForeignMarker,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState
      {
        Id = foreignClientId,
        FirmId = foreignFirmId
      });
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = foreignEngagementId,
        FirmId = foreignFirmId,
        PracticeClientId = foreignClientId,
        ServiceRoute = "FinancialStatementAudit",
        ServiceProfileId = privateForeignMarker,
        PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31",
        Status = "Draft",
        ProfessionalWorkBlocked = true,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
      "/app/clients/" + f.ClientId.ToString("D") + "/assessment"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Client acceptance checklist", Exact = true })).ToBeVisibleAsync();

    var unknownClientId = Guid.NewGuid();
    var unknownEngagementId = Guid.NewGuid();
    var deniedPairs = new[]
    {
      (Foreign: $"/api/ui/clients/{foreignClientId:D}/assessment", Unknown: $"/api/ui/clients/{unknownClientId:D}/assessment"),
      (Foreign: $"/api/ui/clients/{foreignClientId:D}/engagement-creation", Unknown: $"/api/ui/clients/{unknownClientId:D}/engagement-creation"),
      (Foreign: $"/api/ui/engagements/{foreignEngagementId:D}/activation-review", Unknown: $"/api/ui/engagements/{unknownEngagementId:D}/activation-review")
    };

    foreach (var pair in deniedPairs)
    {
      await using var foreign = await context.APIRequest.GetAsync(origin + pair.Foreign);
      await using var unknown = await context.APIRequest.GetAsync(origin + pair.Unknown);
      var foreignBody = await foreign.TextAsync();
      var unknownBody = await unknown.TextAsync();
      Assert.Equal(403, foreign.Status);
      Assert.Equal(403, unknown.Status);
      Assert.Equal(unknownBody, foreignBody);
      Assert.DoesNotContain(privateForeignMarker, foreignBody, StringComparison.Ordinal);
    }

    var protectedRoutes = new[]
    {
      ($"/app/clients/{foreignClientId:D}/assessment", "Client acceptance checklist"),
      ($"/app/clients/{foreignClientId:D}/engagements/new", "Engagement creation unavailable"),
      ($"/app/engagements/{foreignEngagementId:D}/activation", "Activation unavailable")
    };
    foreach (var (path, unavailableHeading) in protectedRoutes)
    {
      await page.GotoAsync(origin + path);
      await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
      if (unavailableHeading != "Client acceptance checklist")
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
          new() { Name = unavailableHeading, Exact = true })).ToBeVisibleAsync();
      var body = await page.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privateForeignMarker, body, StringComparison.Ordinal);
    }

    Assert.Empty(pageErrors);
  }
}
