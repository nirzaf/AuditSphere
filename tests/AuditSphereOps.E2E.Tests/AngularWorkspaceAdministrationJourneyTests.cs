using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularWorkspaceAdministrationJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-WORKSPACE-ADMINISTRATION")]
  public async Task AcceptedClientReview_ShowsRealBlockers_WithoutInventingFoldersOrPrivilegedSiteAccess()
  {
    await using var host = await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-WORKSPACE-ADMINISTRATION");
    var seeded = await TenantAdministrationSeed.SeedAsync(host,verified:true);
    var settings = TenantAdministrationSeed.Simulation(seeded); settings["AngularUi__Enabled"]="true";
    var origin = await host.StartApiForIdentityAsync(seeded.Admin,settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fmicrosoft365%2Ftenant-connection");
    var panel=page.Locator("audit-workspace-administration");
    await Assertions.Expect(panel.GetByRole(AriaRole.Heading,new(){Name="Client and engagement workspaces",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(panel).ToContainTextAsync("No accepted client workspace intents exist");
    await Assertions.Expect(panel).ToContainTextAsync("Full Control over the entire client site");
    await Assertions.Expect(panel.GetByRole(AriaRole.Link,new(){Name="Open verified site",Exact=true})).ToHaveCountAsync(0);
    await using(var db=host.CreateDbContext()){
      var decision=Guid.NewGuid();var f=host.Fixture;var now=DateTimeOffset.UtcNow;
      db.AcceptanceDecisions.Add(new AcceptanceDecision{Id=decision,FirmId=f.FirmId,PracticeClientId=f.ClientId,Decision="Accepted",Rationale="Synthetic accepted browser fixture",ServiceRoute="FinancialStatementAudit",EvaluationTemplateVersion="synthetic-v1",EvaluationSnapshotDigest=new string('a',64),DecidedByUserId=seeded.Admin.Id,DecidedAt=now});
      db.ClientWorkspaces.Add(new ClientWorkspace{Id=Guid.NewGuid(),FirmId=f.FirmId,PracticeClientId=f.ClientId,AcceptanceDecisionId=decision,LogicalKey="workspace/"+f.ClientId,State=ClientWorkspaceStates.WaitingForIntegration,CreatedAt=now});await db.SaveChangesAsync();}
    await panel.GetByRole(AriaRole.Button,new(){Name="Refresh workspace status",Exact=true}).ClickAsync();
    await Assertions.Expect(panel.Locator("h3 a")).ToHaveAttributeAsync("href","/ui/app/clients/"+host.Fixture.ClientId);
    await Assertions.Expect(panel.GetByRole(AriaRole.Link,new(){Name="Review worker operation evidence and recovery",Exact=true})).ToHaveAttributeAsync("href","/ui/app/operations");
    await panel.GetByRole(AriaRole.Button,new(){Name="Review client workspace",Exact=true}).ClickAsync();
    await Assertions.Expect(panel.GetByRole(AriaRole.Heading,new(){Name="Review client workspace",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(panel).ToContainTextAsync("An active, consent-verified working-site connection");
    await panel.GetByLabel("Reason",new(){Exact=true}).FillAsync("Reviewing a synthetic accepted client");
    await panel.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed the target, resource binding, approved templates and current state.",Exact=true}).CheckAsync();
    await Assertions.Expect(panel.GetByRole(AriaRole.Button,new(){Name="Provision reviewed workspace",Exact=true})).ToBeDisabledAsync();
    await panel.GetByRole(AriaRole.Button,new(){Name="Close review",Exact=true}).ClickAsync();
    await panel.GetByRole(AriaRole.Button,new(){Name="Review engagement repository",Exact=true}).ClickAsync();
    await Assertions.Expect(panel.GetByRole(AriaRole.Heading,new(){Name="Review engagement repository",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(panel.GetByRole(AriaRole.Button,new(){Name="Provision reviewed workspace",Exact=true})).ToBeDisabledAsync();
    await using var final=host.CreateDbContext();Assert.Null((await final.ClientWorkspaces.SingleAsync()).RemoteItemId);
    Assert.Empty(await final.RepositoryBindings.ToListAsync());Assert.Empty(await final.ClientSharePointSites.ToListAsync());Assert.Empty(errors);
  }
}
