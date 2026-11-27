using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularValuationPreparationJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-VALUATION-PREPARATION")]
  public async Task SourceOnlyReconciliationBlocksNewEclAndInventoryPreparation()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-VALUATION-PREPARATION");
    var seed=await AccountingAnalysisReviewSeed.SeedAsync(host.Database,false,false);var f=seed.Fixture;
    Guid rec;await using(var db=host.CreateDbContext())rec=await db.EclAssessments.Where(x=>x.Id==seed.Evidence["ECL"]).Select(x=>x.ReconciliationId).SingleAsync();
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["Application__FirmId"]=f.FirmId.ToString("D")});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+$"/ui/app/accounting/reconciliations/{rec}");
    await page.GetByRole(AriaRole.Link,new(){Name="Prepare ECL from this reconciliation",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("distinct independently evidenced control population");
    Assert.Equal(0,await page.GetByRole(AriaRole.Heading,new(){Name="Explicit valuation inputs",Exact=true}).CountAsync());
    await page.GetByRole(AriaRole.Link,new(){Name="← Back to exact reconciliation",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Prepare inventory valuation from this reconciliation",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("distinct independently evidenced control population");
    Assert.Equal(0,await page.GetByRole(AriaRole.Heading,new(){Name="Explicit valuation inputs",Exact=true}).CountAsync());
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    await using(var db=host.CreateDbContext())
    {
      Assert.Empty(await db.ValuationPreparations.Where(x=>x.ReconciliationId==rec).ToListAsync());
      Assert.Equal("DRAFT",await db.EclAssessments.Where(x=>x.Id==seed.Evidence["ECL"]).Select(x=>x.Status).SingleAsync());
      Assert.Equal("DRAFT",await db.InventoryValuationAssessments.Where(x=>x.Id==seed.Evidence["INVENTORY"]).Select(x=>x.Status).SingleAsync());
      Assert.Equal(100.123456m,await db.AccountingReconciliations.Where(x=>x.Id==rec).Select(x=>x.SourceTotal).SingleAsync());
    }
    Assert.Empty(errors);
  }
}
