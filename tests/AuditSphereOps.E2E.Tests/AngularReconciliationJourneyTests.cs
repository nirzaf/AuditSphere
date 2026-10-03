using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularReconciliationJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-RECONCILIATION-REVIEW")]
  public async Task RetainedProofSourceChangeAndRevocationRemainVisibleAndScoped()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-RECONCILIATION-REVIEW");
    var s=await ReconciliationReviewSeed.SeedAsync(host.Database);var f=s.Fixture;
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string> { ["AngularUi__Enabled"]="true",["Application__FirmId"]=f.FirmId.ToString("D") });
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    var errors=new List<string>();page.PageError+=(_,error)=>errors.Add(error);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl=/ui/app/accounting/evidence");
    await page.GetByRole(AriaRole.Link,new(){Name="Inspect reconciliation",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Latest retained item proof",Exact=true}).WaitForAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("100.123456");
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("27 complete items");
    Assert.DoesNotContain("HIDDEN SYNTHETIC",await page.Locator("body").InnerTextAsync());
    Assert.Equal(25,await page.Locator("tbody tr").CountAsync());
    await page.GetByRole(AriaRole.Button,new(){Name="Next",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);
    await page.SetViewportSizeAsync(390,844);
    Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    await using(var db=host.CreateDbContext())
      await db.TrialBalanceDatasets.Where(x=>x.Id==s.SourceId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.NormalizedDatasetDigest,new string('e',64)));
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh current eligibility",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Evidence reuse blocked",Exact=true}).WaitForAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("100.123456");
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Prepare new source-bound evidence");
    await using(var db=host.CreateDbContext())
      await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.SessionEpoch,v=>v.SessionEpoch+1));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain("100.123456",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
}
