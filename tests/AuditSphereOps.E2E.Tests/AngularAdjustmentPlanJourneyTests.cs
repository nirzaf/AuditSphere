using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAdjustmentPlanJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-ADJUSTMENT-PLAN-COMMAND")]
  public async Task ReviewedCreationFinalizationAndRetainedHistoryPreserveExactSource()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-ADJUSTMENT-PLAN-COMMAND");
    AdjustmentPlanReviewSeed.Result seed;
    await using(var db=host.CreateDbContext()) seed=await AdjustmentPlanReviewSeed.SeedAsync(db,host.Fixture);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    var errors=new List<string>();page.PageError+=(_,error)=>errors.Add(error);
    await page.GotoAsync(origin+$"/auth/sign-in?returnUrl=/ui/app/accounting/sources/{seed.SourceId}/adjustment-plan");
    await page.GetByRole(AriaRole.Checkbox,new(){Name="Include AJ-SYN revision 1",Exact=true}).CheckAsync();
    await page.GetByLabel("Plan command rationale",new(){Exact=true}).FillAsync("Synthetic reviewed plan selection");
    await page.GetByLabel("Plan command evidence reference",new(){Exact=true}).FillAsync("Synthetic source and journal bridge");
    await page.GetByRole(AriaRole.Button,new(){Name="Preview plan command",Exact=true}).ClickAsync();
    var assent=page.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed the exact source, period, membership, source reflection, rationale, evidence and proposed result.",Exact=true});
    await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Create reviewed plan",Exact=true})).ToBeDisabledAsync();
    await assent.CheckAsync();await page.GetByRole(AriaRole.Button,new(){Name="Create reviewed plan",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Draft plan retained",Exact=true}).WaitForAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Review retained plan",Exact=true}).ClickAsync();
    await page.GetByText("1 eligible · 0 excluded · 0 blocked",new(){Exact=true}).WaitForAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Review plan finalization",Exact=true}).ClickAsync();
    await page.GetByLabel("Plan command rationale",new(){Exact=true}).FillAsync("Synthetic reviewed calculation");
    await page.GetByLabel("Plan command evidence reference",new(){Exact=true}).FillAsync("Synthetic exact calculation evidence");
    await page.GetByRole(AriaRole.Button,new(){Name="Preview plan command",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Exact plan command preview",Exact=true})).ToContainTextAsync("200.246912");
    await assent.CheckAsync();await page.GetByRole(AriaRole.Button,new(){Name="Finalize reviewed calculation",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Calculation retained",Exact=true}).WaitForAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Review retained plan",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Adjustment plan eligibility",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Retained native plan command history",Exact=true})).ToContainTextAsync("Synthetic reviewed calculation");
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Retained native plan command history",Exact=true})).ToContainTextAsync("Synthetic reviewed calculation");
    await page.SetViewportSizeAsync(390,844);
    Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    await using(var db=host.CreateDbContext())
    {
      var plan=await db.AdjustmentPlans.SingleAsync(p=>p.Id!=seed.PlanId);
      Assert.Equal("Finalized",plan.Status);Assert.Equal(200.246912m,plan.AppliedDebits);Assert.Equal(1,plan.AppliedJournalCount);
      Assert.Equal(2,await db.AdjustmentPlanActions.CountAsync());
      Assert.Equal(100.123456m,await db.TrialBalanceRows.Where(r=>r.DatasetId==seed.SourceId && r.Amount>0).SumAsync(r=>r.Amount));
      Assert.Empty(await db.FinancialPackages.ToListAsync());
      await db.Users.Where(u=>u.Id==host.Fixture.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain("Synthetic reviewed calculation",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId","ANGULAR-ADJUSTMENT-PLAN-REVIEW")]
  public async Task NativeQueueCurrentReflectionMobileAndEpochLossRemainExact()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-ADJUSTMENT-PLAN-REVIEW");
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
