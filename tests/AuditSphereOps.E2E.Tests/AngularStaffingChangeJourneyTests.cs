using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularStaffingChangeJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AssignmentAndRevocationLostResponsesReconcileAfterReloadWithoutRetry(bool canonical)
  {
    await using var host = await OwnedHost.StartAsync(startWorker:false, caseId:"ANGULAR-STAFFING-REVIEW");
    var f = host.Fixture;
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string,string> {
      ["AngularUi__Enabled"]="true", ["AngularUi__CanonicalRoutes"]=canonical.ToString() });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_,e) => errors.Add(e);
    var path = (canonical?"":"/ui")+"/app/engagements/"+f.EngagementId;
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(path));
    await page.GetByText("Team and budget",new(){Exact=true}).ScrollIntoViewIfNeededAsync();
    var planning = page.GetByRole(AriaRole.Region,new(){Name="Engagement planning",Exact=true});
    var roleAssent = planning.GetByLabel("I reviewed the engagement role and client-site access.",new(){Exact=true});
    var assignmentReview = planning.GetByRole(AriaRole.Button,new(){Name="Review team assignment",Exact=true});
    var previewPattern = "**/staffing-change/preview";
    var calls=0;
    await page.RouteAsync("**/staffing-change",async route=> {
      Interlocked.Increment(ref calls);var result=await route.FetchAsync();Assert.Equal(200,result.Status);await route.AbortAsync(); });
    await page.RouteAsync(previewPattern,async route=> await route.FulfillAsync(new(){Status=400,ContentType="application/json",Body="{\"message\":\"Synthetic staffing preview was refused.\"}"}));
    await planning.GetByLabel("Person",new(){Exact=true}).SelectOptionAsync(f.Reviewer.Id.ToString());
    await roleAssent.CheckAsync();
    await Assertions.Expect(assignmentReview).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(assignmentReview).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(planning.GetByRole(AriaRole.Status)).ToContainTextAsync("Synthetic staffing preview was refused.");
    await Assertions.Expect(planning.GetByRole(AriaRole.Region,new(){Name="Staffing change review",Exact=true})).ToHaveCountAsync(0);
    await page.UnrouteAsync(previewPattern);
    Assert.Equal(0,calls);

    await roleAssent.CheckAsync();
    await Assertions.Expect(assignmentReview).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(assignmentReview).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    var review = planning.GetByRole(AriaRole.Region,new(){Name="Staffing change review",Exact=true});
    await Assertions.Expect(review).ToContainTextAsync("Full Control");
    var confirm = review.GetByRole(AriaRole.Button,new(){Name="Confirm staffing change",Exact=true});
    var confirmationAssent = review.GetByLabel("I reviewed this exact staffing change and its access effects.",new(){Exact=true});
    var cancel = review.GetByRole(AriaRole.Button,new(){Name="Cancel staffing review",Exact=true});
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    await Assertions.Expect(review).ToBeFocusedAsync();
    await page.SetViewportSizeAsync(320,844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),"Staffing review overflows a 320px viewport.");
    Assert.True(await review.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"),"Staffing review content overflows its region.");
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
    await Assertions.Expect(roleAssent).ToBeFocusedAsync();
    await Assertions.Expect(roleAssent).Not.ToBeCheckedAsync();
    Assert.Equal(0,calls);

    await page.Keyboard.PressAsync("Space");
    await Assertions.Expect(roleAssent).ToBeCheckedAsync();
    await Assertions.Expect(assignmentReview).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(assignmentReview).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(review).ToBeFocusedAsync();
    async Task ConfirmAndRecover()
    {
      await page.Keyboard.PressAsync("Tab");
      await Assertions.Expect(confirmationAssent).ToBeFocusedAsync();
      await Assertions.Expect(confirmationAssent).Not.ToBeCheckedAsync();
      await page.Keyboard.PressAsync("Space");
      await Assertions.Expect(confirm).ToBeEnabledAsync();
      await page.Keyboard.PressAsync("Tab");
      await Assertions.Expect(confirm).ToBeFocusedAsync();
      await page.Keyboard.PressAsync("Enter");
      await Assertions.Expect(planning.GetByRole(AriaRole.Button,new(){Name="Verify staffing request receipt",Exact=true})).ToBeVisibleAsync();
      // Pending recovery is visible before the command is sent. Wait until the fetched
      // response has been aborted and Angular has settled the unknown outcome before reload.
      await Assertions.Expect(planning.GetByRole(AriaRole.Button,new(){Name="Verify staffing request receipt",Exact=true})).ToBeEnabledAsync();
      await page.ReloadAsync(); await page.GetByText("Team and budget",new(){Exact=true}).ScrollIntoViewIfNeededAsync();
      await planning.GetByRole(AriaRole.Button,new(){Name="Verify staffing request receipt",Exact=true}).ClickAsync();
      await Assertions.Expect(planning.GetByRole(AriaRole.Button,new(){Name="Acknowledge staffing change",Exact=true})).ToBeVisibleAsync();
      await planning.GetByRole(AriaRole.Button,new(){Name="Acknowledge staffing change",Exact=true}).ClickAsync();
    }
    await ConfirmAndRecover();
    string name;
    await using(var db=host.CreateDbContext()) { name=(await db.Users.SingleAsync(x=>x.Id==f.Reviewer.Id)).DisplayName;Assert.Single(await db.StaffingChanges.ToListAsync()); }
    var revoke = planning.GetByRole(AriaRole.Button,new(){Name="Revoke "+name,Exact=true});
    await TabTo(revoke);
    await page.Keyboard.PressAsync("Enter");
    var reviewRevocation = planning.GetByRole(AriaRole.Button,new(){Name="Review revocation",Exact=true});
    await TabTo(reviewRevocation);
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(review).ToContainTextAsync("protected sessions invalidated");
    await Assertions.Expect(review).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(confirmationAssent).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Space");
    await Assertions.Expect(confirm).ToBeEnabledAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(confirm).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(cancel).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(review).ToHaveCountAsync(0);
    await Assertions.Expect(revoke).ToBeVisibleAsync();
    await Assertions.Expect(revoke).ToBeEnabledAsync();
    await Assertions.Expect(revoke).ToBeFocusedAsync();
    Assert.Equal(1,calls);
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(reviewRevocation).ToBeVisibleAsync();
    await TabTo(reviewRevocation);
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(review).ToBeFocusedAsync();
    Assert.Equal(1,calls);
    await ConfirmAndRecover();
    await Assertions.Expect(planning).ToContainTextAsync("Nobody is staffed on this engagement yet.");
    Assert.Equal(2,calls);
    await using(var db=host.CreateDbContext()) {
      Assert.Equal(2,await db.StaffingChanges.CountAsync());Assert.NotNull((await db.EngagementStaffAssignments.SingleAsync()).RevokedAt);
      Assert.Equal(f.Reviewer.SessionEpoch+1,(await db.Users.SingleAsync(x=>x.Id==f.Reviewer.Id)).SessionEpoch);
    }

    async Task TabTo(ILocator target)
    {
      for(var i=0;i<64;i++)
      {
        if(await target.EvaluateAsync<bool>("element => element === document.activeElement")) return;
        await page.Keyboard.PressAsync("Tab");
      }
      Assert.True(await target.EvaluateAsync<bool>("element => element === document.activeElement"),"Keyboard Tab could not reach the requested planning control.");
    }

    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));Assert.Empty(errors);
  }
}
