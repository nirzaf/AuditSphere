using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularResourcePlanningJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task KeyboardFormsCapacityAvailabilityAndAllocationsRemainServerOwned(bool canonical)
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-RESOURCE-FORMS");
    var f=host.Fixture;
    await using(var db=host.CreateDbContext()) {
      var staff=await db.Users.SingleAsync(x=>x.Id==f.Staff.Id);staff.DisplayName="Synthetic resource staff";
      await db.SaveChangesAsync();
      var actor=PbcSeed.Actor(f.Admin,"Administrator");
      Assert.True((await StaffingService.AssignAsync(db,actor,new(f.EngagementId,f.Staff.Id,StaffingLevels.StaffAssociate))).Succeeded);
      Assert.True((await ResourcePlanningService.SaveProfileAsync(db,actor,new(f.Staff.Id,"Audit","IFRS",1200,75m))).Succeeded);
    }
    var origin=await host.StartApiForIdentityAsync(f.Admin,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()});
    var prefix=canonical ? "" : "/ui";
    using var playwright=await Playwright.CreateAsync();
    await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    var page=await (await browser.NewContextAsync(new(){ViewportSize=new(){Width=390,Height=844}})).NewPageAsync();
    var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app/practice/resources"));
    var table=page.GetByRole(AriaRole.Table,new(){Name="Resource grid",Exact=true});
    await Assertions.Expect(table).ToContainTextAsync("Synthetic resource staff");
    Assert.Equal(7, await table.Locator("thead th").CountAsync());
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    async Task ConfirmAndAcknowledgeAsync()
    {
      var review = page.GetByRole(AriaRole.Region, new() { Name = "Exact planning action review", Exact = true });
      await Assertions.Expect(review).ToBeVisibleAsync();
      var confirm=page.GetByRole(AriaRole.Button,new(){Name="Confirm reviewed planning action",Exact=true});
      await Assertions.Expect(confirm).ToBeDisabledAsync();
      await page.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed this exact planning action and its effects.",Exact=true}).CheckAsync();
      await Assertions.Expect(confirm).ToBeEnabledAsync();
      await confirm.PressAsync("Enter");
      await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Retained planning receipt",Exact=true})).ToBeVisibleAsync();
      await page.GetByRole(AriaRole.Button,new(){Name="Acknowledge planning receipt",Exact=true}).PressAsync("Enter");
      await Assertions.Expect(table).ToBeVisibleAsync();
    }
    async Task SubmitAndConfirmAsync(ILocator submitButton)
    {
      var previewResponse = page.WaitForResponseAsync(response =>
        response.Request.Method == "POST" && response.Url.EndsWith("/api/ui/practice/resources/preview", StringComparison.Ordinal));
      await submitButton.PressAsync("Enter");
      Assert.Equal(200, (await previewResponse).Status);
      await ConfirmAndAcknowledgeAsync();
    }
    var profile=page.GetByRole(AriaRole.Form,new(){Name="Save profile",Exact=true});
    await profile.GetByLabel("Team member",new(){Exact=true}).SelectOptionAsync(f.Staff.Id.ToString());
    await Assertions.Expect(profile.GetByLabel("Weekly capacity (hours)",new(){Exact=true})).ToHaveValueAsync("20");
    await profile.GetByLabel("Target utilization %",new(){Exact=true}).FillAsync("101");
    await profile.GetByRole(AriaRole.Button,new(){Name="Save profile",Exact=true}).PressAsync("Enter");
    await Assertions.Expect(profile.GetByRole(AriaRole.Alert)).ToContainTextAsync("0 to 100");
    await profile.GetByLabel("Target utilization %",new(){Exact=true}).FillAsync("80");
    await SubmitAndConfirmAsync(profile.GetByRole(AriaRole.Button,new(){Name="Save profile",Exact=true}));
    var allocation=page.GetByRole(AriaRole.Form,new(){Name="Save allocation",Exact=true});
    await allocation.GetByLabel("Engagement",new(){Exact=true}).SelectOptionAsync(f.EngagementId.ToString());
    await allocation.GetByLabel("Team member",new(){Exact=true}).SelectOptionAsync(f.Staff.Id.ToString());
    await allocation.GetByLabel("Planned hours",new(){Exact=true}).FillAsync("30");
    await SubmitAndConfirmAsync(allocation.GetByRole(AriaRole.Button,new(){Name="Save allocation",Exact=true}));
    await Assertions.Expect(table.Locator("tr[data-user='Synthetic resource staff']")).ToContainTextAsync("Over-allocated");
    await Assertions.Expect(page.Locator("[aria-labelledby='allocations-heading']")).ToContainTextAsync("30 hours");
    var certification=page.GetByRole(AriaRole.Form,new(){Name="Add certification",Exact=true});
    await certification.GetByLabel("Team member",new(){Exact=true}).SelectOptionAsync(f.Staff.Id.ToString());
    await certification.GetByLabel("Certification",new(){Exact=true}).FillAsync("Synthetic qualification");
    await page.SetViewportSizeAsync(1400,900);
    await page.GetByRole(AriaRole.Link,new(){Name="Portfolio",Exact=true}).First.ClickAsync();
    var dialog=page.GetByRole(AriaRole.Dialog);await Assertions.Expect(dialog).ToContainTextAsync("Unsubmitted planning edits");
    await dialog.GetByRole(AriaRole.Button,new(){Name="Keep editing",Exact=true}).ClickAsync();
    await Assertions.Expect(certification.GetByLabel("Certification",new(){Exact=true})).ToHaveValueAsync("Synthetic qualification");
    var dispatched=0;const string commandRoute="**/api/ui/practice/resources/commands";
    await page.RouteAsync(commandRoute,async route=>{var response=await route.FetchAsync();Assert.Equal(200,response.Status);dispatched++;await route.AbortAsync("failed");});
    var certificationPreview = page.WaitForResponseAsync(response =>
      response.Request.Method == "POST" && response.Url.EndsWith("/api/ui/practice/resources/preview", StringComparison.Ordinal));
    await certification.GetByRole(AriaRole.Button,new(){Name="Add certification",Exact=true}).PressAsync("Enter");
    Assert.Equal(200, (await certificationPreview).Status);
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact planning action review", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed this exact planning action and its effects.",Exact=true}).CheckAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Confirm reviewed planning action",Exact=true}).ClickAsync();
    var recovery=page.GetByRole(AriaRole.Region,new(){Name="Planning request recovery",Exact=true});await Assertions.Expect(recovery).ToBeVisibleAsync();
    await page.ReloadAsync();await Assertions.Expect(recovery).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Verify planning receipt",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Retained planning receipt",Exact=true})).ToContainTextAsync("Synthetic qualification");
    await page.GetByRole(AriaRole.Button,new(){Name="Acknowledge planning receipt",Exact=true}).ClickAsync();
    Assert.Equal(1,dispatched);await page.UnrouteAsync(commandRoute);
    await Assertions.Expect(table).ToContainTextAsync("Synthetic qualification");
    var availability=page.GetByRole(AriaRole.Form,new(){Name="Record unavailability",Exact=true});
    await availability.GetByLabel("Team member",new(){Exact=true}).SelectOptionAsync(f.Staff.Id.ToString());
    await availability.GetByLabel("Hours per day",new(){Exact=true}).FillAsync("8");
    await SubmitAndConfirmAsync(availability.GetByRole(AriaRole.Button,new(){Name="Record unavailability",Exact=true}));
    await using(var db=host.CreateDbContext()) {
      Assert.Single(await db.StaffCertifications.Where(x=>x.UserId==f.Staff.Id && x.Name=="Synthetic qualification").ToListAsync());
      Assert.Single(await db.StaffAvailabilities.Where(x=>x.UserId==f.Staff.Id).ToListAsync());
      Assert.Equal(1800,(await db.StaffAllocations.SingleAsync(x=>x.UserId==f.Staff.Id)).PlannedMinutes);
    }
    Assert.Empty(errors);
  }
}
