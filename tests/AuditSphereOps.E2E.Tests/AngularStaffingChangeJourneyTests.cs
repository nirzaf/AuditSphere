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
    await using var host = await OwnedBlazorHost.StartAsync(startWorker:false, caseId:"ANGULAR-STAFFING-REVIEW");
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
    await planning.GetByLabel("Person",new(){Exact=true}).SelectOptionAsync(f.Reviewer.Id.ToString());
    await planning.GetByLabel("I reviewed the engagement role and client-site access.",new(){Exact=true}).CheckAsync();
    await planning.GetByRole(AriaRole.Button,new(){Name="Review team assignment",Exact=true}).ClickAsync();
    var review = planning.GetByRole(AriaRole.Region,new(){Name="Staffing change review",Exact=true});
    await Assertions.Expect(review).ToContainTextAsync("Full Control");
    await Assertions.Expect(review.GetByRole(AriaRole.Button,new(){Name="Confirm staffing change",Exact=true})).ToBeDisabledAsync();
    var calls=0;
    await page.RouteAsync("**/staffing-change",async route=> {
      Interlocked.Increment(ref calls);var result=await route.FetchAsync();Assert.Equal(200,result.Status);await route.AbortAsync(); });
    async Task ConfirmAndRecover()
    {
      await review.GetByLabel("I reviewed this exact staffing change and its access effects.",new(){Exact=true}).CheckAsync();
      await review.GetByRole(AriaRole.Button,new(){Name="Confirm staffing change",Exact=true}).ClickAsync();
      await Assertions.Expect(planning.GetByRole(AriaRole.Button,new(){Name="Verify staffing request receipt",Exact=true})).ToBeVisibleAsync();
      await page.ReloadAsync(); await page.GetByText("Team and budget",new(){Exact=true}).ScrollIntoViewIfNeededAsync();
      await planning.GetByRole(AriaRole.Button,new(){Name="Verify staffing request receipt",Exact=true}).ClickAsync();
      await Assertions.Expect(planning.GetByRole(AriaRole.Button,new(){Name="Acknowledge staffing change",Exact=true})).ToBeVisibleAsync();
      await planning.GetByRole(AriaRole.Button,new(){Name="Acknowledge staffing change",Exact=true}).ClickAsync();
    }
    await ConfirmAndRecover();
    string name;
    await using(var db=host.CreateDbContext()) { name=(await db.Users.SingleAsync(x=>x.Id==f.Reviewer.Id)).DisplayName;Assert.Single(await db.StaffingChanges.ToListAsync()); }
    await planning.GetByRole(AriaRole.Button,new(){Name="Revoke "+name,Exact=true}).ClickAsync();
    await planning.GetByRole(AriaRole.Button,new(){Name="Review revocation",Exact=true}).ClickAsync();
    await Assertions.Expect(review).ToContainTextAsync("protected sessions invalidated");
    await ConfirmAndRecover();
    await Assertions.Expect(planning).ToContainTextAsync("Nobody is staffed on this engagement yet.");
    Assert.Equal(2,calls);
    await using(var db=host.CreateDbContext()) {
      Assert.Equal(2,await db.StaffingChanges.CountAsync());Assert.NotNull((await db.EngagementStaffAssignments.SingleAsync()).RevokedAt);
      Assert.Equal(f.Reviewer.SessionEpoch+1,(await db.Users.SingleAsync(x=>x.Id==f.Reviewer.Id)).SessionEpoch);
    }
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));Assert.Empty(errors);
  }
}
