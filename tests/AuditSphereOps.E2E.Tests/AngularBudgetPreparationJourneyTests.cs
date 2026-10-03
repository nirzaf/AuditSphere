using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularBudgetPreparationJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ExactReviewLostResponseReloadAndRetainedReceiptNeverDuplicateBudget(bool canonical)
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-BUDGET-REVIEW-E2E");var f=host.Fixture;
    await using(var db=host.CreateDbContext()){await BudgetPreparationReviewSeed.PopulateAsync(db,f);}
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()});
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    var path=(canonical?"":"/ui")+"/app/engagements/"+f.EngagementId;
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(path));
    await page.GetByText("Team and budget",new(){Exact=true}).ScrollIntoViewIfNeededAsync();
    var planning=page.GetByRole(AriaRole.Region,new(){Name="Engagement planning",Exact=true});
    await planning.GetByRole(AriaRole.Button,new(){Name="Review budget preparation",Exact=true}).ClickAsync();
    var review=planning.GetByRole(AriaRole.Region,new(){Name="Reviewed budget preparation",Exact=true});await Assertions.Expect(review).ToContainTextAsync("100.125");await Assertions.Expect(review).ToContainTextAsync("801.000000");
    await Assertions.Expect(review.GetByRole(AriaRole.Button,new(){Name="Confirm draft budget preparation",Exact=true})).ToBeDisabledAsync();
    await review.GetByLabel("I reviewed these exact rates and authorize draft preparation.",new(){Exact=true}).CheckAsync();
    var calls=0;
    await page.RouteAsync("**/budget-preparation",async route=>{Interlocked.Increment(ref calls);var response=await route.FetchAsync();Assert.Equal(200,response.Status);await route.AbortAsync();});
    await review.GetByRole(AriaRole.Button,new(){Name="Confirm draft budget preparation",Exact=true}).ClickAsync();
    await Assertions.Expect(planning.GetByRole(AriaRole.Button,new(){Name="Verify budget request receipt",Exact=true})).ToBeVisibleAsync();
    await page.ReloadAsync();await page.GetByText("Team and budget",new(){Exact=true}).ScrollIntoViewIfNeededAsync();
    await planning.GetByRole(AriaRole.Button,new(){Name="Verify budget request receipt",Exact=true}).ClickAsync();
    await Assertions.Expect(planning.GetByRole(AriaRole.Region,new(){Name="Budget preparation receipt",Exact=true})).ToContainTextAsync("801.000000");
    await planning.GetByRole(AriaRole.Button,new(){Name="Acknowledge budget preparation",Exact=true}).ClickAsync();
    await Assertions.Expect(planning).ToContainTextAsync("Latest version 1");Assert.Equal(1,calls);
    await Assertions.Expect(planning).ToContainTextAsync("A different authorized manager or partner must approve this draft.");
    await using(var db=host.CreateDbContext()){Assert.Single(await db.EngagementBudgets.ToListAsync());Assert.Single(await db.BudgetPreparations.ToListAsync());Assert.Null((await db.EngagementBudgets.SingleAsync()).ApprovedByUserId);}
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));Assert.Empty(errors);
  }
}
