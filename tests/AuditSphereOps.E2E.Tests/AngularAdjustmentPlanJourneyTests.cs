using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAdjustmentPlanJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-ADJUSTMENT-PLAN-REVIEW")]
  public async Task NativeQueueCurrentReflectionMobileAndEpochLossRemainExact()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-ADJUSTMENT-PLAN-REVIEW");
    AdjustmentPlanReviewSeed.Result seed;
    await using(var db=host.CreateDbContext()) seed=await AdjustmentPlanReviewSeed.SeedAsync(db,host.Fixture);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    var errors=new List<string>();page.PageError+=(_,error)=>errors.Add(error);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl=/ui/app/accounting/adjustment-plans");
    await page.GetByRole(AriaRole.Link,new(){Name="Review plan",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByText("1 eligible · 1 excluded · 2 blocked",new(){Exact=true})).ToBeVisibleAsync();
    Assert.Contains("FY26",await page.Locator("body").InnerTextAsync());
    Assert.Contains("NOT_REFLECTED",await page.GetByRole(AriaRole.Region,new(){Name="Plan journal membership",Exact=true}).InnerTextAsync());
    await page.ReloadAsync();await page.GetByText("1 eligible · 1 excluded · 2 blocked",new(){Exact=true}).WaitForAsync();
    await using(var db=host.CreateDbContext())
      Assert.True((await SourceReconciliationService.ResolveAsync(db,PbcSeed.Actor(host.Fixture.Reviewer,"AccountingReviewer"),seed.SourceId,"AJ-SYN",1,ReflectionStates.Reflected,"Synthetic changed source bridge")).Succeeded);
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh eligibility review",Exact=true}).ClickAsync();
    await page.GetByText("0 eligible · 1 excluded · 3 blocked",new(){Exact=true}).WaitForAsync();
    await page.GetByText("Source reflection changed after this plan was created. Review the new decision and create a replacement plan.",new(){Exact=true}).WaitForAsync();
    await page.SetViewportSizeAsync(390,844);
    Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    await page.GotoAsync(origin+"/ui/app/accounting/adjustment-plans/"+Guid.NewGuid());
    await page.GetByText("This information is unavailable in your current scope.",new(){Exact=true}).WaitForAsync();
    Assert.DoesNotContain("AJ-SYN",await page.Locator("body").InnerTextAsync());
    await page.GotoAsync(origin+"/ui/app/accounting/adjustment-plans/"+seed.PlanId);
    await page.GetByText("0 eligible · 1 excluded · 3 blocked",new(){Exact=true}).WaitForAsync();
    await using(var db=host.CreateDbContext()) await db.Users.Where(u=>u.Id==host.Fixture.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain("AJ-SYN",await page.Locator("body").InnerTextAsync());
    Assert.Empty(errors);
  }
}
