using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularBudgetApprovalJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task IndependentApprovalLostResponseReloadAndReceiptNeverResubmit(bool canonical)
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker:false, caseId:"ANGULAR-BUDGET-APPROVAL-E2E", startLegacyBlazorHosts: false);
    var f = host.Fixture;
    await using (var db = host.CreateDbContext()) { await BudgetApprovalReviewSeed.PopulateAsync(db, f); }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string,string> {
      ["AngularUi__Enabled"]="true", ["AngularUi__CanonicalRoutes"]=canonical.ToString() });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_,e) => errors.Add(e);
    var path = (canonical?"":"/ui")+"/app/engagements/"+f.EngagementId;
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(path));
    await page.GetByText("Team and budget",new(){Exact=true}).ScrollIntoViewIfNeededAsync();
    var planning = page.GetByRole(AriaRole.Region,new(){Name="Engagement planning",Exact=true});
    await planning.GetByLabel("I reviewed this draft for independent approval.",new(){Exact=true}).CheckAsync();
    await planning.GetByRole(AriaRole.Button,new(){Name="Review budget approval",Exact=true}).ClickAsync();
    var review = planning.GetByRole(AriaRole.Region,new(){Name="Reviewed budget approval",Exact=true});
    await Assertions.Expect(review).ToContainTextAsync("801.000000");
    await Assertions.Expect(review.GetByRole(AriaRole.Button,new(){Name="Confirm budget approval",Exact=true})).ToBeDisabledAsync();
    await review.GetByLabel("I reviewed this exact draft and authorize independent approval.",new(){Exact=true}).CheckAsync();
    var calls=0;
    await page.RouteAsync("**/budget-approval",async route=> {
      Interlocked.Increment(ref calls);var result=await route.FetchAsync();Assert.Equal(200,result.Status);await route.AbortAsync(); });
    await review.GetByRole(AriaRole.Button,new(){Name="Confirm budget approval",Exact=true}).ClickAsync();
    await Assertions.Expect(planning.GetByRole(AriaRole.Button,new(){Name="Verify approval request receipt",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(planning).Not.ToContainTextAsync("No approved budget is available.");
    await page.ReloadAsync();await page.GetByText("Team and budget",new(){Exact=true}).ScrollIntoViewIfNeededAsync();
    await planning.GetByRole(AriaRole.Button,new(){Name="Verify approval request receipt",Exact=true}).ClickAsync();
    await Assertions.Expect(planning.GetByRole(AriaRole.Region,new(){Name="Budget approval receipt",Exact=true})).ToContainTextAsync("801.000000");
    await planning.GetByRole(AriaRole.Button,new(){Name="Acknowledge budget approval",Exact=true}).ClickAsync();
    await Assertions.Expect(planning).ToContainTextAsync("Actuals include approved time only.");
    await Assertions.Expect(planning.GetByRole(AriaRole.Region,new(){Name="Budget approval receipt",Exact=true})).ToHaveCountAsync(0);
    Assert.Equal(1,calls);
    await using(var db=host.CreateDbContext()) { Assert.Single(await db.BudgetApprovals.ToListAsync());Assert.Equal(f.Admin.Id,(await db.EngagementBudgets.SingleAsync()).ApprovedByUserId); }
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));Assert.Empty(errors);
  }
}
