using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAccountingPreparationCreationJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-ACCOUNTING-PREPARATION-CREATION")]
  public async Task ReconciliationAndSpecialistPreparationHaveReviewedReceiptsAndRecoverLostReply()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-ACCOUNTING-PREPARATION-CREATION", startLegacyBlazorHosts: false);
    var seed=await ReconciliationReviewSeed.SeedAsync(host.Database);var f=seed.Fixture;
    await using(var db=host.CreateDbContext())
      await db.ClientReportingPeriods.Where(x=>x.FirmId==f.FirmId&&x.ClientId==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Status,"ACTIVE"));
    Guid sourcePeriod;await using(var contextDb=host.CreateDbContext())
      sourcePeriod=await contextDb.TrialBalanceDatasets.Where(x=>x.Id==seed.SourceId).Select(x=>x.PeriodId!.Value).SingleAsync();
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["Application__FirmId"]=f.FirmId.ToString("D")});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);await using var context=await browser.NewContextAsync();
    var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);page.Console+=(_,e)=>{if(e.Type=="error"&&!e.Text.Contains("net::ERR_FAILED",StringComparison.Ordinal))errors.Add(e.Text);};
    var reconciliationUrl=$"/ui/app/engagements/{f.EngagementId}/reconciliation/new?sourceKind=TRIAL_BALANCE&sourceId={seed.SourceId}";
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(reconciliationUrl));
    await page.GetByRole(AriaRole.Heading,new(){Name="Prepare reconciliation",Exact=true}).WaitForAsync();
    await page.GetByLabel("Reconciliation area",new(){Exact=true}).FillAsync("CASH");
    await page.GetByLabel("Account codes (one per line or comma separated, up to 100)",new(){Exact=true}).FillAsync("1000");
    await page.GetByLabel("Reason",new(){Exact=true}).FillAsync("Prepare a synthetic source-bound reconciliation.");
    await page.GetByLabel("Evidence reference",new(){Exact=true}).FillAsync("SYNTHETIC-RECON-EVIDENCE");
    await page.GetByRole(AriaRole.Button,new(){Name="Preview exact totals",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("100.123456");
    await page.GetByLabel("I reviewed this exact source, account selection, date, calculation and evidence reference.",new(){Exact=true}).CheckAsync();
    var writes=0;await page.RouteAsync("**/api/ui/engagements/*/reconciliation-preparation",async route=>
    {
      if(route.Request.Method!="POST"||route.Request.Url.EndsWith("/preview",StringComparison.Ordinal)){await route.ContinueAsync();return;}
      Interlocked.Increment(ref writes);var response=await route.FetchAsync();Assert.Equal(200,response.Status);await route.AbortAsync("failed");
    });
    await page.GetByRole(AriaRole.Button,new(){Name="Save reviewed reconciliation",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Reconciliation outcome needs reconciliation",Exact=true}).WaitForAsync();
    await page.ReloadAsync();await page.GetByRole(AriaRole.Heading,new(){Name="Reconciliation outcome needs reconciliation",Exact=true}).WaitForAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Check retained receipt",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("The retained receipt confirms this exact reconciliation");
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("revision 2");

    var specialistUrl=$"/ui/app/engagements/{f.EngagementId}/specialists/new?periodId={sourcePeriod}&area=ASSETS";
    await page.GotoAsync(origin+specialistUrl);
    await page.GetByRole(AriaRole.Heading,new(){Name="Prepare specialist schedule",Exact=true}).WaitForAsync();
    await page.GetByLabel("Methodology version",new(){Exact=true}).FillAsync("SYNTHETIC-ASSETS-V1");
    await page.GetByLabel("Assumptions SHA-256",new(){Exact=true}).FillAsync(new string('e',64));
    await page.GetByLabel("openingAmount",new(){Exact=true}).FillAsync("100");
    await page.GetByLabel("additionsAmount",new(){Exact=true}).FillAsync("20");
    await page.GetByLabel("depreciationAmount",new(){Exact=true}).FillAsync("10");
    await page.GetByLabel("managementAmount",new(){Exact=true}).FillAsync("110");
    await page.GetByLabel("Depreciation method",new(){Exact=true}).FillAsync("STRAIGHT_LINE");
    await page.GetByLabel("Useful life in months",new(){Exact=true}).FillAsync("120");
    await page.GetByLabel("Preparation evidence reference",new(){Exact=true}).FillAsync("SYNTHETIC-ASSET-EVIDENCE");
    await page.GetByLabel("Reason for preparation",new(){Exact=true}).FillAsync("Prepare a synthetic asset schedule.");
    await page.GetByRole(AriaRole.Button,new(){Name="Preview schedule calculation",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("110.000000");
    await page.GetByLabel("I reviewed the exact profile inputs, calculation, evidence and revision relationship.",new(){Exact=true}).CheckAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Retain reviewed schedule revision",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Retained schedule receipt",Exact=true}).WaitForAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("ASSETS · revision 1");
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    await using(var db=host.CreateDbContext())
    {
      var recon=await db.AccountingReconciliationPreparations.SingleAsync();Assert.Equal(f.Staff.Id,recon.ActorId);
      var schedule=await db.SpecialistSchedulePreparations.SingleAsync();Assert.Equal(f.Staff.Id,schedule.ActorId);
      Assert.Equal(1,await db.SpecialistAccountingSchedules.Where(x=>x.Id==schedule.ScheduleId).Select(x=>x.Revision).SingleAsync());
    }
    Assert.Equal(1,writes);Assert.Empty(errors);
  }
}
