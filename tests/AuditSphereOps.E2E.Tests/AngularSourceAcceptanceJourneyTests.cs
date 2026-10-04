using AuditSphereOps.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularSourceAcceptanceJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-SOURCE-ACCEPTANCE")]
  public async Task IndependentReview_TabRecovery_CurrentSelection_UnknownSaveReloadAndRevocation()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-SOURCE-ACCEPTANCE", startLegacyBlazorHosts: false); var first=await Seed(host); var second=await Seed(host);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Admin,new Dictionary<string,string>{{"AngularUi__Enabled","true"}});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(Route(first)));var evidence=page.GetByLabel("Acceptance evidence reference",new(){Exact=true});var assent=page.GetByLabel("I independently reviewed this exact source and its effect on the selected pointer.",new(){Exact=true});var accept=page.GetByRole(AriaRole.Button,new(){Name="Accept reviewed source",Exact=true});
    await Assertions.Expect(accept).ToBeDisabledAsync();await evidence.FillAsync("Independent browser source evidence");await assent.CheckAsync();await page.GetByRole(AriaRole.Button,new(){Name="Save acceptance tab draft",Exact=true}).ClickAsync();await page.ReloadAsync();await page.GetByRole(AriaRole.Button,new(){Name="Recover acceptance tab draft",Exact=true}).ClickAsync();await Assertions.Expect(evidence).ToHaveValueAsync("Independent browser source evidence");await Assertions.Expect(assent).Not.ToBeCheckedAsync();await assent.CheckAsync();await accept.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Retained acceptance decision",Exact=true})).ToBeVisibleAsync();await Assertions.Expect(page.GetByText("This is the currently selected source.",new(){Exact=true})).ToBeVisibleAsync();
    var writes=0;await page.RouteAsync($"**/datasets/{second}/source/acceptance",async r=>{if(r.Request.Method!="POST"){await r.ContinueAsync();return;}writes++;var response=await r.FetchAsync();Assert.Equal(200,response.Status);await r.AbortAsync("failed");});
    await page.GotoAsync(origin+Route(second));await Assertions.Expect(page.Locator("body")).ToContainTextAsync(first.ToString("D"));await evidence.FillAsync("Replacement source evidence");await assent.CheckAsync();await accept.ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Acceptance outcome needs review",Exact=true})).ToBeVisibleAsync();await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Acceptance outcome needs review",Exact=true})).ToBeVisibleAsync();await page.GetByRole(AriaRole.Button,new(){Name="Read persisted acceptance",Exact=true}).ClickAsync();await page.GetByRole(AriaRole.Button,new(){Name="I reviewed the persisted acceptance",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByText("This is the currently selected source.",new(){Exact=true})).ToBeVisibleAsync();Assert.Equal(1,writes);
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    await using(var db=host.CreateDbContext()){Assert.Equal(2,await db.SourceAcceptanceDecisions.CountAsync());Assert.Equal(3,await db.ClientSafetyStates.Where(x=>x.Id==host.Fixture.ClientId).Select(x=>x.InputGeneration).SingleAsync());await db.Users.Where(x=>x.Id==host.Fixture.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));}
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});Assert.DoesNotContain("Replacement source evidence",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
  [Fact]
  [Trait("CaseId","ANGULAR-SOURCE-GATES")]
  public async Task ImporterIsBlocked_StaleSelectionCannotCommit_AndNavigationGuardsIntent()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-SOURCE-GATES", startLegacyBlazorHosts: false);var id=await Seed(host);var self=await Seed(host,host.Fixture.Admin.Id);var origin=await host.StartApiForIdentityAsync(host.Fixture.Admin,new Dictionary<string,string>{{"AngularUi__Enabled","true"}});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(Route(self)));await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("other than the source importer");await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Accept reviewed source",Exact=true})).ToBeDisabledAsync();
    await page.GotoAsync(origin+Route(id));var evidence=page.GetByLabel("Acceptance evidence reference",new(){Exact=true});await evidence.FillAsync("Unsubmitted evidence");await page.GetByRole(AriaRole.Link,new(){Name="Back to trial balance intake",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();await page.GetByRole(AriaRole.Button,new(){Name="Keep editing",Exact=true}).ClickAsync();await Assertions.Expect(evidence).ToHaveValueAsync("Unsubmitted evidence");
    await page.GetByLabel("I independently reviewed this exact source and its effect on the selected pointer.",new(){Exact=true}).CheckAsync();await using(var db=host.CreateDbContext())await db.ClientSafetyStates.Where(x=>x.Id==host.Fixture.ClientId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.InputGeneration,x=>x.InputGeneration+1));await page.GetByRole(AriaRole.Button,new(){Name="Accept reviewed source",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("changed");await page.GetByRole(AriaRole.Button,new(){Name="Refresh source acceptance",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByLabel("I independently reviewed this exact source and its effect on the selected pointer.",new(){Exact=true})).Not.ToBeCheckedAsync();await using var proof=host.CreateDbContext();Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());
  }
  private static string Route(Guid id)=>$"/ui/app/accounting/sources/{id}/acceptance";
  private static async Task<Guid> Seed(OwnedBlazorHost host,Guid? importer=null)
  {
    await using var db=host.CreateDbContext();var f=host.Fixture;var period=Guid.NewGuid();db.ClientReportingPeriods.Add(new(){Id=period,FirmId=f.FirmId,ClientId=f.ClientId,PeriodCode=period.ToString("N"),StartDate=new(2026,1,1),EndDate=new(2026,12,31),Currency="QAR",Basis="IFRS",Status="ACTIVE",CreatedByUserId=f.Admin.Id,CreatedAt=DateTimeOffset.UtcNow});var id=Guid.NewGuid();var hash=id.ToString("N")+id.ToString("N");db.TrialBalanceDatasets.Add(new(){Id=id,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,PeriodId=period,LegalEntityKey="SYN-BROWSER-SOURCE",Currency="QAR",Balanced=true,ValidationStatus="Accepted",RawFileSha256Hex=hash,NormalizedDatasetDigest=hash,Sha256Hex=hash,ImportedByUserId=importer??f.Staff.Id,ImportedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();await db.TrialBalanceDatasets.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ImportState,"SEALED"));return id;
  }
}
