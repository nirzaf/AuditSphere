using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularMappingApprovalJourneyTests
{
  private const string Assent="I independently reviewed this exact mapping, its full allocations and applicability.";

  [Fact][Trait("CaseId","ANGULAR-MAPPING-REVIEW")]
  public async Task NativeIndependentApprovalRecoversLostResponseAndRevokedSession()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-MAPPING-REVIEW", startLegacyBlazorHosts: false);
    var seed=await Seed(host);var origin=await host.StartApiForIdentityAsync(host.Fixture.Reviewer,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString($"/ui/app/accounting/mappings/{seed.MappingId}"));
    await page.GetByRole(AriaRole.Link,new(){Name="Review mapping approval",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Exact version and applicability",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Approve reviewed mapping",Exact=true})).ToBeDisabledAsync();
    await page.GetByLabel(Assent,new(){Exact=true}).CheckAsync();
    var writes=0;
    await page.RouteAsync("**/mappings/*/approval",async r=>{
      if(r.Request.Method!="POST") {await r.ContinueAsync();return;}
      writes++;var response=await r.FetchAsync();Assert.Equal(200,response.Status);await r.AbortAsync("failed");
    });
    await page.GetByRole(AriaRole.Button,new(){Name="Approve reviewed mapping",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Mapping approval outcome needs review",Exact=true})).ToBeVisibleAsync();
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Mapping approval outcome needs review",Exact=true})).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Read persisted mapping",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="I reviewed the persisted mapping",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Mapping approval outcome needs review",Exact=true})).ToBeHiddenAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Approve reviewed mapping",Exact=true})).ToBeDisabledAsync();
    Assert.Equal(1,writes);await page.SetViewportSizeAsync(390,844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    await using(var db=host.CreateDbContext()) {
      var retained=await db.MappingVersions.SingleAsync();Assert.Equal("APPROVED",retained.Status);Assert.Equal(host.Fixture.Reviewer.Id,retained.ApprovedByUserId);
      Assert.Equal(2,await db.ClientSafetyStates.Where(x=>x.Id==host.Fixture.ClientId).Select(x=>x.InputGeneration).SingleAsync());
      Assert.Empty(await db.SourceAcceptanceDecisions.ToListAsync());
      await db.Users.Where(x=>x.Id==host.Fixture.Reviewer.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain(seed.DatasetId.ToString(),await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }

  [Fact][Trait("CaseId","ANGULAR-MAPPING-GATES")]
  public async Task CreatorAndChangedSourceGenerationCannotApprove()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-MAPPING-GATES", startLegacyBlazorHosts: false);
    var seed=await Seed(host);
    var settings=new Dictionary<string,string>{["AngularUi__Enabled"]="true"};
    var staff=await host.StartApiForIdentityAsync(host.Fixture.Staff,settings);
    var reviewer=await host.StartApiForIdentityAsync(host.Fixture.Reviewer,settings);
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    var route=$"/ui/app/accounting/mappings/{seed.MappingId}/approval";
    await page.GotoAsync(staff+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("preparer cannot approve");
    await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Approve reviewed mapping",Exact=true})).ToBeDisabledAsync();
    await page.GotoAsync(reviewer+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
    await page.GetByLabel(Assent,new(){Exact=true}).CheckAsync();
    await using(var db=host.CreateDbContext())
      await db.ClientSafetyStates.Where(x=>x.Id==host.Fixture.ClientId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.InputGeneration,x=>x.InputGeneration+1));
    await page.GetByRole(AriaRole.Button,new(){Name="Approve reviewed mapping",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("audit-command-message").GetByRole(AriaRole.Status)).ToContainTextAsync("changed");
    await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Approve reviewed mapping",Exact=true})).ToBeDisabledAsync();
    Assert.False(await page.GetByLabel(Assent,new(){Exact=true}).IsCheckedAsync());
    await using var proof=host.CreateDbContext();Assert.Equal("DRAFT",await proof.MappingVersions.Select(x=>x.Status).SingleAsync());
    Assert.Null(await proof.MappingVersions.Select(x=>x.ApprovedByUserId).SingleAsync());
  }

  private static async Task<MappingApprovalSeed.Context> Seed(OwnedBlazorHost host) {
    await using var db=host.CreateDbContext();return await MappingApprovalSeed.SeedAsync(db,host.Fixture);
  }
}
