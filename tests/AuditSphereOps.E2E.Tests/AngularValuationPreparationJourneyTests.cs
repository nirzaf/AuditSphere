using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularValuationPreparationJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-VALUATION-PREPARATION")]
  public async Task ExactEclAndInventoryPreparationRemainSeparateFromReviewAndClearAfterRevocation()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-VALUATION-PREPARATION");
    var seed=await AccountingAnalysisReviewSeed.SeedAsync(host.Database,false,false);var f=seed.Fixture;
    Guid rec;await using(var db=host.CreateDbContext())rec=await db.EclAssessments.Where(x=>x.Id==seed.Evidence["ECL"]).Select(x=>x.ReconciliationId).SingleAsync();
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["Application__FirmId"]=f.FirmId.ToString("D")});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+$"/ui/app/accounting/reconciliations/{rec}");
    await page.GetByRole(AriaRole.Link,new(){Name="Prepare ECL from this reconciliation",Exact=true}).ClickAsync();
    await Enter(page,"ECL",new Dictionary<string,string>{["Probability of default"]="0.100000",["Loss given default"]="0.500000",["Management overlay"]="2.000000",["Management expected loss"]="8.000000",["Booked amount"]="8.000000"});
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("7.006173");
    await page.GetByRole(AriaRole.Button,new(){Name="Prepare reviewed valuation",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Retained preparation receipt",Exact=true}).WaitForAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Inspect exact prepared valuation",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Analysis evidence",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("7.006173");
    await page.GetByRole(AriaRole.Link,new(){Name="Inspect exact reconciliation",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Prepare inventory valuation from this reconciliation",Exact=true}).ClickAsync();
    await Enter(page,"INVENTORY",new Dictionary<string,string>{["Quantity"]="2.000001",["Unit cost"]="6.000001",["NRV per unit"]="5.000001",["Obsolescence reserve"]="0.100001",["Book amount"]="12.000001"});
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("9.900006");
    await page.GetByRole(AriaRole.Button,new(){Name="Prepare reviewed valuation",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Retained preparation receipt",Exact=true}).WaitForAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Inspect exact prepared valuation",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Analysis evidence",Exact=true})).ToBeVisibleAsync();
    await page.ReloadAsync();await Assertions.Expect(page.Locator("body")).ToContainTextAsync("9.900006");
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    await using(var db=host.CreateDbContext())
    {
      var preparations=await db.ValuationPreparations.Where(x=>x.ReconciliationId==rec).ToListAsync();Assert.Equal(2,preparations.Count);
      foreach(var p in preparations)Assert.Equal(f.Staff.Id,p.ActorId);
      var eclId=preparations.Single(x=>x.Kind=="ECL").EvidenceId;
      var inventoryId=preparations.Single(x=>x.Kind=="INVENTORY").EvidenceId;
      Assert.Equal("DRAFT",await db.EclAssessments.Where(x=>x.Id==eclId).Select(x=>x.Status).SingleAsync());
      Assert.Equal("DRAFT",await db.InventoryValuationAssessments.Where(x=>x.Id==inventoryId).Select(x=>x.Status).SingleAsync());
      Assert.Equal(100.123456m,await db.AccountingReconciliations.Where(x=>x.Id==rec).Select(x=>x.SourceTotal).SingleAsync());
      await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain("9.900006",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
  private static async Task Enter(IPage page,string kind,Dictionary<string,string> amounts)
  {
    await page.GetByRole(AriaRole.Heading,new(){Name="Explicit valuation inputs",Exact=true}).WaitForAsync();
    await page.GetByLabel("Methodology version",new(){Exact=true}).FillAsync("synthetic."+kind.ToLowerInvariant()+".v1");
    await page.GetByLabel("Assumptions SHA-256",new(){Exact=true}).FillAsync(new string('a',64));
    foreach(var(label,value)in amounts)await page.GetByLabel(label,new(){Exact=true}).FillAsync(value);
    await page.GetByLabel("Reason / source rationale",new(){Exact=true}).FillAsync("Synthetic browser source rationale");
    await page.GetByLabel("Evidence reference",new(){Exact=true}).FillAsync("SYNTHETIC-VALUATION-EVIDENCE");
    await page.GetByRole(AriaRole.Button,new(){Name="Preview exact calculation",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Review calculation before preparation",Exact=true}).WaitForAsync();
    await page.GetByLabel("I reviewed this exact source, all inputs and calculation.",new(){Exact=true}).CheckAsync();
  }
}
