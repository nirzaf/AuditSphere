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
    await using var host = await OwnedHost.StartAsync(startWorker:false, caseId:"ANGULAR-BUDGET-APPROVAL-E2E");
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
    var draftAssent = planning.GetByLabel("I reviewed this draft for independent approval.",new(){Exact=true});
    var reviewButton = planning.GetByRole(AriaRole.Button,new(){Name="Review budget approval",Exact=true});
    var previewPattern = "**/budget-approval/preview";
    var calls=0;
    await page.RouteAsync("**/budget-approval",async route=> {
      Interlocked.Increment(ref calls);var result=await route.FetchAsync();Assert.Equal(200,result.Status);await route.AbortAsync(); });
    await page.RouteAsync(previewPattern,async route=> await route.FulfillAsync(new(){Status=400,ContentType="application/json",Body="{\"message\":\"Synthetic approval preview was refused.\"}"}));
    await draftAssent.CheckAsync();
    await Assertions.Expect(reviewButton).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(reviewButton).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(planning.GetByRole(AriaRole.Status)).ToContainTextAsync("Synthetic approval preview was refused.");
    await Assertions.Expect(planning.GetByRole(AriaRole.Region,new(){Name="Reviewed budget approval",Exact=true})).ToHaveCountAsync(0);
    await page.UnrouteAsync(previewPattern);
    Assert.Equal(0,calls);

    await draftAssent.CheckAsync();
    await Assertions.Expect(reviewButton).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(reviewButton).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    var review = planning.GetByRole(AriaRole.Region,new(){Name="Reviewed budget approval",Exact=true});
    await Assertions.Expect(review).ToContainTextAsync("801.000000");
    var confirmationAssent = review.GetByLabel("I reviewed this exact draft and authorize independent approval.",new(){Exact=true});
    var confirm = review.GetByRole(AriaRole.Button,new(){Name="Confirm budget approval",Exact=true});
    var cancel = review.GetByRole(AriaRole.Button,new(){Name="Cancel approval review",Exact=true});
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    await Assertions.Expect(review).ToBeFocusedAsync();
    await page.SetViewportSizeAsync(320,844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),"Budget approval review overflows a 320px viewport.");
    Assert.True(await review.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"),"Budget approval review content overflows its region.");
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(confirmationAssent).ToBeFocusedAsync();
    await Assertions.Expect(confirmationAssent).Not.ToBeCheckedAsync();
    await page.Keyboard.PressAsync("Space");
    await Assertions.Expect(confirm).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(confirm).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(cancel).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(review).ToHaveCountAsync(0);
    await Assertions.Expect(draftAssent).ToBeFocusedAsync();
    await Assertions.Expect(draftAssent).Not.ToBeCheckedAsync();
    Assert.Equal(0,calls);
    await page.Keyboard.PressAsync("Space");
    await Assertions.Expect(draftAssent).ToBeCheckedAsync();
    await Assertions.Expect(reviewButton).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(reviewButton).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(review).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(confirmationAssent).ToBeFocusedAsync();
    await Assertions.Expect(confirmationAssent).Not.ToBeCheckedAsync();
    await page.Keyboard.PressAsync("Space");
    await Assertions.Expect(confirm).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(confirm).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    var verifyReceipt = planning.GetByRole(AriaRole.Button,new(){Name="Verify approval request receipt",Exact=true});
    await Assertions.Expect(verifyReceipt).ToBeVisibleAsync();
    await Assertions.Expect(verifyReceipt).ToBeEnabledAsync();
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
