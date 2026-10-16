using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularEngagementActivationJourneyTests
{
  [Fact]
  public async Task RecurringPeriodRequiresItsOwnDecisionBeforeOfferingActivationReview()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-RECURRING-RISK"); var f = host.Fixture;
    await using (var db = host.CreateDbContext())
    {
      await EngagementActivationReviewSeed.PopulateAsync(db, f);
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, ServiceRoute = "FinancialStatementAudit",
        PeriodStart = "2025-01-01", PeriodEnd = "2025-12-31", ServiceProfileId = "Synthetic prior period", CreatedAt = DateTimeOffset.UtcNow.AddYears(-1)
      });
      await db.SaveChangesAsync();
    }
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
      { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    var path = "/app/engagements/" + f.EngagementId + "/activation";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByText("Record a Partner acceptance decision", new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Prepare activation review", Exact = true })).ToHaveCountAsync(0);
    var checklist = page.GetByRole(AriaRole.Link, new() { Name = "Open the acceptance checklist", Exact = true });
    await Assertions.Expect(checklist).ToBeVisibleAsync();
    await Assertions.Expect(checklist).ToHaveAttributeAsync("href",
      $"/app/clients/{f.ClientId}/assessment?engagementId={f.EngagementId}");
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path);
    await using (var db = host.CreateDbContext())
    {
      var generation = await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync();
      db.AcceptanceDecisions.Add(new()
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, EngagementId = f.EngagementId,
        ServiceRoute = "FinancialStatementAudit", Decision = "Accepted", Generation = generation,
        Path = AuditSphereOps.Domain.Acceptance.AcceptancePaths.Continuance, Rationale = "Synthetic new-period risk fixture",
        EvaluationTemplateVersion = "synthetic.v1", EvaluationSnapshotDigest = new string('c', 64),
        DecidedByUserId = f.Staff.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync(); Assert.Empty(await db.EngagementActivations.ToListAsync());
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh activation context", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Prepare activation review", Exact = true })).ToBeEnabledAsync();
    Assert.Empty(errors);
  }

  [Theory][InlineData(false)][InlineData(true)]
  public async Task ExactPartnerReviewLostResponseReceiptReloadAndRevocation(bool canonical)
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-PARTNER-ACTIVATION");var f=host.Fixture;
    await using(var db=host.CreateDbContext()){await EngagementActivationReviewSeed.PopulateAsync(db,f);}
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()});var prefix=canonical?"":"/ui";
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();
    var errors=new System.Collections.Concurrent.ConcurrentQueue<string>();page.PageError+=(_,e)=>errors.Enqueue(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app/engagements/"+f.EngagementId+"/activation?holdPage=1&holdPageSize=25"));
    var basis=page.GetByRole(AriaRole.Region,new(){Name="Activation context",Exact=true});await Assertions.Expect(basis).ToContainTextAsync("9007199254740993");
    await page.GetByRole(AriaRole.Button,new(){Name="Prepare activation review",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Confirm engagement activation",Exact=true})).ToBeDisabledAsync();
    await page.GetByLabel("I reviewed these exact engagement and acceptance prerequisites.",new(){Exact=true}).CheckAsync();
    var observed=false;await page.RouteAsync("**/api/ui/engagements/*/activation-review",async route=>{
      if(route.Request.Method!="POST"){await route.ContinueAsync();return;}
      var response=await route.FetchAsync();Assert.Equal(200,response.Status);observed=true;await response.DisposeAsync();await route.AbortAsync();});
    await page.GetByRole(AriaRole.Button,new(){Name="Confirm engagement activation",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Activation outcome unconfirmed",Exact=true})).ToBeVisibleAsync();Assert.True(observed);
    await page.ReloadAsync();await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Activation outcome unconfirmed",Exact=true})).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Verify activation receipt",Exact=true}).ClickAsync();var receipt=page.GetByRole(AriaRole.Region,new(){Name="Activation receipt",Exact=true});
    await Assertions.Expect(receipt).ToContainTextAsync("9007199254740994");await Assertions.Expect(receipt).ToContainTextAsync("NEW_CLIENT");
    await page.GetByRole(AriaRole.Button,new(){Name="Acknowledge activation result",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Activation outcome unconfirmed",Exact=true})).ToHaveCountAsync(0);
    // Acknowledging triggers a fresh context read. Wait for that response before revoking the
    // Partner grant, otherwise the in-flight read can observe revocation and remove this control.
    await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Refresh activation context",Exact=true})).ToBeVisibleAsync();
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth+1"));
    await using(var db=host.CreateDbContext()){Assert.Single(await db.EngagementActivations.ToListAsync());await db.RoleGrants.Where(g=>g.UserId==f.Staff.Id&&g.Role=="Partner").ExecuteUpdateAsync(x=>x.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow));}
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh activation context",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Activation unavailable",Exact=true})).ToBeVisibleAsync();
    Assert.DoesNotContain("Synthetic annual audit",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
}
