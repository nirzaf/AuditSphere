using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAnalyticalPreparationJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-ANALYTICAL-PREPARATION")]
  public async Task ReviewedPreparationRecoversALostAcknowledgementWithoutDuplicateAndKeepsHumanReviewSeparate()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-ANALYTICAL-PREPARATION", startLegacyBlazorHosts: false);
    var seed=await AccountingAnalysisReviewSeed.SeedAsync(host.Database,false,true);var f=seed.Fixture;
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["Application__FirmId"]=f.FirmId.ToString("D")});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);await using var context=await browser.NewContextAsync();
    var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);page.Console+=(_,e)=>{if(e.Type=="error"&&!e.Text.Contains("net::ERR_FAILED",StringComparison.Ordinal))errors.Add(e.Text);};
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString($"/ui/app/engagements/{f.EngagementId}/analysis/new"));
    await page.GetByRole(AriaRole.Heading,new(){Name="Prepare analytical review",Exact=true}).WaitForAsync();
    await page.GetByLabel("Analysis area",new(){Exact=true}).FillAsync("Revenue");
    await page.GetByLabel("Measure",new(){Exact=true}).FillAsync("Annual movement");
    await page.GetByLabel("Current amount",new(){Exact=true}).FillAsync("120.123456");
    await page.GetByLabel("Prior amount",new(){Exact=true}).FillAsync("100.000000");
    await page.GetByLabel("Denominator basis",new(){Exact=true}).FillAsync("prior-year total");
    await page.GetByLabel("Formula version",new(){Exact=true}).FillAsync("synthetic.analytical.v1");
    await page.GetByLabel("Preparer explanation",new(){Exact=true}).FillAsync("Synthetic source rationale.");
    await page.GetByLabel("Seasonality explanation (optional)",new(){Exact=true}).FillAsync("Seasonality reviewed.");
    await page.GetByLabel("Reason for preparation",new(){Exact=true}).FillAsync("Prepare synthetic analytical review.");
    await page.GetByLabel("Evidence reference",new(){Exact=true}).FillAsync("SYNTHETIC-ANALYTICAL-REF");
    await page.GetByRole(AriaRole.Button,new(){Name="Preview exact analysis",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("0.201235");
    await page.GetByLabel("I reviewed this exact period, all entered values, the formula basis and the evidence reference.",new(){Exact=true}).CheckAsync();
    var writes=0;await page.RouteAsync("**/api/ui/engagements/*/analytical-preparation",async route=>
    {
      if(route.Request.Method!="POST"){await route.ContinueAsync();return;}
      Interlocked.Increment(ref writes);var response=await route.FetchAsync();Assert.Equal(200,response.Status);await route.AbortAsync("failed");
    });
    await page.GetByRole(AriaRole.Button,new(){Name="Retain analytical preparation",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Preparation outcome needs reconciliation",Exact=true}).WaitForAsync();
    await page.ReloadAsync();await page.GetByRole(AriaRole.Heading,new(){Name="Preparation outcome needs reconciliation",Exact=true}).WaitForAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Check retained receipt",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Retained preparation receipt",Exact=true}).WaitForAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("The retained receipt confirms the preparation");
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    await using(var db=host.CreateDbContext())
    {
      var rows=await db.AccountingAnalysisPreparations.ToListAsync();Assert.Single(rows);Assert.Equal(f.Staff.Id,rows[0].ActorId);
      var analysis=await db.AnalyticalReviews.SingleAsync(x=>x.Id==rows[0].EvidenceId);Assert.Equal("DRAFT",analysis.Status);Assert.Null(analysis.ReviewedAt);
      Assert.Equal("120.123456",analysis.CurrentAmount.ToString("0.000000",System.Globalization.CultureInfo.InvariantCulture));
    }
    Assert.Equal(1,writes);Assert.Empty(errors);
  }
}
