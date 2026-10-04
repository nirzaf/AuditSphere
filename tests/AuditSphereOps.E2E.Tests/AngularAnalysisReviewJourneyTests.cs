using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAnalysisReviewJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-ANALYSIS-REVIEW")]
  public async Task NativeEvidenceNavigationPreservesExactInputsProvenanceAndRevocationFences()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-ANALYSIS-REVIEW", startLegacyBlazorHosts: false);var s=await AccountingAnalysisReviewSeed.SeedAsync(host.Database);var f=s.Fixture;
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["Application__FirmId"]=f.FirmId.ToString("D")});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl=/ui/app/accounting/evidence");
    foreach(var(kind,id) in s.Evidence)
    {
      await page.Locator($"a[href='/ui/app/accounting/evidence/{kind}/{id}']").ClickAsync();
      await page.GetByRole(AriaRole.Heading,new(){Name="Retained amounts and inputs",Exact=true}).WaitForAsync();
      await Assertions.Expect(page.Locator("body")).ToContainTextAsync(kind);
      Assert.DoesNotContain("HIDDEN SYNTHETIC",await page.Locator("body").InnerTextAsync());Assert.Equal(0,await page.Locator("audit-analysis-review form").CountAsync());
      if(kind=="SPECIALIST")
      {
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("110.123456");await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(25);
        await page.GetByRole(AriaRole.Button,new(){Name="Next",Exact=true}).ClickAsync();await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);
      }
      if(kind=="JOURNAL_RISK")await Assertions.Expect(page.Locator("body")).ToContainTextAsync("no original source digest or input generation");
      await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
      await page.GetByRole(AriaRole.Link,new(){Name="← Back to accounting evidence",Exact=true}).ClickAsync();
      await page.GetByRole(AriaRole.Heading,new(){Name="Account-area evidence",Exact=true}).WaitForAsync();
    }
    await page.Locator($"a[href='/ui/app/accounting/evidence/ECL/{s.Evidence["ECL"]}']").ClickAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("7.006173");
    await using(var db=host.CreateDbContext())await db.TrialBalanceDatasets.Where(x=>x.Id==s.SourceId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.NormalizedDatasetDigest,new string('e',64)));
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh current checks",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Current verification blockers",Exact=true}).WaitForAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("7.006173");await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Prepare new source-bound evidence");
    await using(var db=host.CreateDbContext())await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.SessionEpoch,v=>v.SessionEpoch+1));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain("7.006173",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
}
