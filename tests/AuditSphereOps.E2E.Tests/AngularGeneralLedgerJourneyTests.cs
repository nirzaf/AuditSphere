using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularGeneralLedgerJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-GL-INSPECTION")]
  public async Task ScopedSource_Filters_ServerPages_FullJournal_MobileAndRevocation()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-GL-INSPECTION", startLegacyBlazorHosts: false);var id=await Seed(host);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Admin,new Dictionary<string,string>{{"AngularUi__Enabled","true"}});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(Route(host.Fixture.EngagementId)));
    await page.GetByRole(AriaRole.Button,new(){Name="Inspect ledger "+id,Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="General ledger lines",Exact=true})).ToContainTextAsync("100.123456");
    await page.GetByRole(AriaRole.Button,new(){Name="Next ledger lines",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Navigation,new(){Name="Ledger line pages",Exact=true})).ToContainTextAsync("Page 2 of 2");
    await page.GetByLabel("Account code prefix",new(){Exact=true}).FillAsync("100");await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="General ledger lines",Exact=true})).Not.ToBeVisibleAsync();
    await page.GetByLabel("Posted from",new(){Exact=true}).FillAsync("2026-06-01");await page.GetByLabel("Posted to",new(){Exact=true}).FillAsync("2026-06-30");await page.GetByLabel("Stable journal ID",new(){Exact=true}).FillAsync("J-000");await page.GetByLabel("Intercompany counterparty",new(){Exact=true}).FillAsync("SYN-PARTNER");await page.GetByRole(AriaRole.Button,new(){Name="Apply ledger filters",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("1 filtered lines");await page.GetByRole(AriaRole.Button,new(){Name="Inspect journal J-000 from line J-000-L0000",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Complete journal lines",Exact=true})).ToContainTextAsync("4000");await Assertions.Expect(page.Locator("body")).ToContainTextAsync("All 2 lines in this journal");
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    await using(var db=host.CreateDbContext()){Assert.Empty(await db.SourceAcceptanceDecisions.ToListAsync());Assert.Empty(await db.GeneralLedgerCompletenessBridges.ToListAsync());await db.Users.Where(x=>x.Id==host.Fixture.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));}
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});Assert.DoesNotContain("SYNTHETIC-GL",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
  [Fact]
  [Trait("CaseId","ANGULAR-GL-RECOVERY")]
  public async Task FailedRead_ClearsData_ExplicitRefreshRecovers_AndAnotherContextIsRefused()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-GL-RECOVERY", startLegacyBlazorHosts: false);var id=await Seed(host);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Admin,new Dictionary<string,string>{{"AngularUi__Enabled","true"}});using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(Route(host.Fixture.EngagementId)));await page.GetByRole(AriaRole.Button,new(){Name="Inspect ledger "+id,Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="General ledger lines",Exact=true})).ToBeVisibleAsync();
    var fail=true;await page.RouteAsync($"**/general-ledger/{id}?*",async r=>{if(fail){await r.FulfillAsync(new(){Status=503,ContentType="application/json",Body="{}"});return;}await r.ContinueAsync();});
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh ledger records",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Temporarily unavailable");await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="General ledger lines",Exact=true})).Not.ToBeVisibleAsync();
    fail=false;await page.GetByRole(AriaRole.Button,new(){Name="Refresh ledger records",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="General ledger lines",Exact=true})).ToBeVisibleAsync();
    await page.GotoAsync(origin+Route(Guid.NewGuid()));await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();Assert.DoesNotContain("SYNTHETIC-GL",await page.Locator("body").InnerTextAsync());
  }
  private static string Route(Guid id)=>$"/ui/app/engagements/{id}/general-ledger";
  private static async Task<Guid> Seed(OwnedBlazorHost host){await using var db=host.CreateDbContext();return await GeneralLedgerWorkspaceSeed.SeedAsync(db,host.Fixture);}
}
