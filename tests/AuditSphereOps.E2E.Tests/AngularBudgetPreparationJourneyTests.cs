using System.Text.Json;
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
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-BUDGET-REVIEW-E2E");var f=host.Fixture;
    await using(var db=host.CreateDbContext()){await BudgetPreparationReviewSeed.PopulateAsync(db,f);}
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()});
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    var path=(canonical?"":"/ui")+"/app/engagements/"+f.EngagementId;
    var (planningResponse, planningResponseBody) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" &&
        request.Url.Contains($"/api/ui/engagements/{f.EngagementId}/planning", StringComparison.Ordinal),
      async () =>
      {
        await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(path));
        await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Engagement planning",Exact=true}))
          .ToBeVisibleAsync();
      });
    Assert.Equal(200, planningResponse);
    using var planningDocument = JsonDocument.Parse(planningResponseBody);
    Assert.True(planningDocument.RootElement.GetProperty("canPrepareBudget").GetBoolean(),
      "The synthetic client Manager must have current budget-preparation authority before this journey begins.");
    var planning=page.GetByRole(AriaRole.Region,new(){Name="Engagement planning",Exact=true});
    var reviewTrigger=planning.GetByRole(AriaRole.Button,new(){Name="Review budget preparation",Exact=true});
    var calls=0;
    await page.RouteAsync("**/budget-preparation",async route=>{Interlocked.Increment(ref calls);var response=await route.FetchAsync();Assert.Equal(200,response.Status);await route.AbortAsync();});
    await reviewTrigger.FocusAsync();await page.Keyboard.PressAsync("Enter");
    var review=planning.GetByRole(AriaRole.Region,new(){Name="Reviewed budget preparation",Exact=true});await Assertions.Expect(review).ToContainTextAsync("100.125");await Assertions.Expect(review).ToContainTextAsync("801.000000");
    await Assertions.Expect(review).ToBeFocusedAsync();
    var cancel=review.GetByRole(AriaRole.Button,new(){Name="Cancel budget review",Exact=true});
    await cancel.FocusAsync();await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(review).ToHaveCountAsync(0);await Assertions.Expect(reviewTrigger).ToBeFocusedAsync();Assert.Equal(0,calls);
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(review).ToBeVisibleAsync();await Assertions.Expect(review).ToBeFocusedAsync();
    await Assertions.Expect(review.GetByRole(AriaRole.Button,new(){Name="Confirm draft budget preparation",Exact=true})).ToBeDisabledAsync();
    var assent=review.GetByLabel("I reviewed these exact rates and authorize draft preparation.",new(){Exact=true});
    var confirm=review.GetByRole(AriaRole.Button,new(){Name="Confirm draft budget preparation",Exact=true});
    await assent.FocusAsync();await page.Keyboard.PressAsync("Space");await Assertions.Expect(assent).ToBeCheckedAsync();await Assertions.Expect(confirm).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");await Assertions.Expect(confirm).ToBeFocusedAsync();await Assertions.Expect(confirm).ToHaveCSSAsync("outline-style","solid");
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(planning.GetByRole(AriaRole.Button,new(){Name="Verify budget request receipt",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(planning.GetByText("The outcome is not confirmed. Refresh to see the persisted state before repeating this action.",new(){Exact=true})).ToBeVisibleAsync();
    var (restoredPlanningStatus, restoredPlanningBody) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && new Uri(request.Url).AbsolutePath.Equals(
        $"/api/ui/engagements/{f.EngagementId}/planning", StringComparison.OrdinalIgnoreCase),
      async () =>
      {
        await page.ReloadAsync();
        await Assertions.Expect(planning).ToBeVisibleAsync();
      });
    Assert.True(restoredPlanningStatus is >= 200 and < 300,
      $"Planning recovery returned HTTP {restoredPlanningStatus}: {restoredPlanningBody}");
    await planning.GetByRole(AriaRole.Button,new(){Name="Verify budget request receipt",Exact=true}).ClickAsync();
    await Assertions.Expect(planning.GetByRole(AriaRole.Region,new(){Name="Budget preparation receipt",Exact=true})).ToContainTextAsync("801.000000");
    await planning.GetByRole(AriaRole.Button,new(){Name="Acknowledge budget preparation",Exact=true}).ClickAsync();
    await Assertions.Expect(planning).ToContainTextAsync("Latest version 1");Assert.Equal(1,calls);
    await Assertions.Expect(planning).ToContainTextAsync("A different authorized manager or partner must approve this draft.");
    await using(var db=host.CreateDbContext()){Assert.Single(await db.EngagementBudgets.ToListAsync());Assert.Single(await db.BudgetPreparations.ToListAsync());Assert.Null((await db.EngagementBudgets.SingleAsync()).ApprovedByUserId);}
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));Assert.Empty(errors);
  }
}
