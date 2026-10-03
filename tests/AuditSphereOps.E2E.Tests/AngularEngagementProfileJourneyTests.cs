using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularEngagementProfileJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId","ANGULAR-ENGAGEMENT-PROFILE-E2E")]
  public async Task MetadataPagedHoldsPreservedPlanningFieldsAndRevocation(bool canonical)
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-ENGAGEMENT-PROFILE-E2E");
    var f=host.Fixture; await using(var db=host.CreateDbContext()) { await EngagementProfileWorkspaceSeed.PopulateAsync(db,f); }
    var origin=await host.StartApiForIdentityAsync(f.Admin,new Dictionary<string,string> {
      ["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString() });
    var prefix=canonical?"":"/ui";
    using var playwright=await Playwright.CreateAsync(); await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context=await browser.NewContextAsync(); var page=await context.NewPageAsync();
    var errors=new List<string>(); page.PageError+=(_,e)=>errors.Add(e);
    var path=prefix+"/app/engagements/"+f.EngagementId;
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Engagement details",Exact=true})).ToBeVisibleAsync();
    var profile=page.GetByRole(AriaRole.Region,new(){Name="Engagement profile",Exact=true});
    await Assertions.Expect(profile).ToContainTextAsync("Synthetic annual audit profile");
    await Assertions.Expect(profile).ToContainTextAsync("2026-01-02 12:30 UTC");
    await Assertions.Expect(profile).ToContainTextAsync("9007199254740993");
    await Assertions.Expect(profile.GetByRole(AriaRole.Link,new(){Name="Client profile",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Professional work blocked",Exact=true})).ToBeVisibleAsync();
    var holds=page.GetByRole(AriaRole.Region,new(){Name="Holds and clearance gates",Exact=true});
    await Assertions.Expect(holds).ToContainTextAsync("27 recorded holds · 13 active · 14 released");
    await Assertions.Expect(holds.Locator("tbody tr")).ToHaveCountAsync(10);
    var planning=page.GetByRole(AriaRole.Region,new(){Name="Engagement planning",Exact=true});
    await page.GetByText("Team and budget",new(){Exact=true}).ScrollIntoViewIfNeededAsync();
    await Assertions.Expect(planning.GetByRole(AriaRole.Heading,new(){Name="Prepare budget version",Exact=true})).ToBeVisibleAsync();
    await planning.GetByLabel("Currency",new(){Exact=true}).FillAsync("USD");
    await planning.GetByLabel("Forecast minutes",new(){Exact=true}).FillAsync("123");
    await holds.GetByRole(AriaRole.Button,new(){Name="Next holds",Exact=true}).ClickAsync();
    await Assertions.Expect(holds).ToContainTextAsync("Page 2");
    await Assertions.Expect(planning.GetByLabel("Currency",new(){Exact=true})).ToHaveValueAsync("USD");
    await Assertions.Expect(planning.GetByLabel("Forecast minutes",new(){Exact=true})).ToHaveValueAsync("123");
    await page.ReloadAsync(); await Assertions.Expect(holds).ToContainTextAsync("Page 2");
    await holds.GetByLabel("Holds per page",new(){Exact=true}).SelectOptionAsync("25");
    await Assertions.Expect(holds.Locator("tbody tr")).ToHaveCountAsync(25);
    await Assertions.Expect(holds).ToContainTextAsync("Page 1");
    await page.SetViewportSizeAsync(390,844); Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await using(var db=host.CreateDbContext()) {
      await db.RoleGrants.Where(g=>g.UserId==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow)); }
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh engagement",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Engagement unavailable",Exact=true})).ToBeVisibleAsync();
    Assert.DoesNotContain("Synthetic annual audit profile",await page.Locator("body").InnerTextAsync());
    Assert.DoesNotContain("USD",await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
  }
}
