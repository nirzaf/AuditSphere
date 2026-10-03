using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientContactCreationJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId","ANGULAR-CLIENT-CONTACT-CREATION-E2E")]
  public async Task ReviewPrimaryReplacementDraftLostResponseRecoveryAndRevocation(bool canonical)
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-CLIENT-CONTACT-CREATION-E2E");var f=host.Fixture;
    await using(var db=host.CreateDbContext()) {
      await ClientProfileWorkspaceSeed.PopulateAsync(db,f);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Manager",f.ClientId));await db.SaveChangesAsync(); }
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()});
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    page.Dialog+=async(_,d)=>{Assert.Equal("beforeunload",d.Type);await d.AcceptAsync();};
    var prefix=canonical?"":"/ui";var profile=prefix+"/app/clients/"+f.ClientId;var path=profile+"/contacts/new";
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(profile));
    await page.GetByRole(AriaRole.Link,new(){Name="Add client contact",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Add client contact",Exact=true})).ToBeVisibleAsync();
    await page.GetByLabel("Full name",new(){Exact=true}).FillAsync("Synthetic created contact");
    await page.GetByLabel("Email address",new(){Exact=true}).FillAsync("created@example.test");
    await page.GetByLabel("Role or title",new(){Exact=true}).FillAsync("Finance");
    await page.GetByLabel("Set as primary contact",new(){Exact=true}).CheckAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Save contact draft",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Client profile",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Save draft and continue",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Add client contact",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Restore contact draft",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByLabel("Full name",new(){Exact=true})).ToHaveValueAsync("Synthetic created contact");
    await page.GetByRole(AriaRole.Button,new(){Name="Review contact",Exact=true}).ClickAsync();
    var review=page.GetByRole(AriaRole.Region,new(){Name="Reviewed contact intent",Exact=true});await Assertions.Expect(review).ToContainTextAsync("Primary contacts to replace");
    var confirm=review.GetByRole(AriaRole.Button,new(){Name="Confirm contact creation",Exact=true});await Assertions.Expect(confirm).ToBeDisabledAsync();
    await page.GetByLabel("I reviewed these exact details and primary-contact changes.",new(){Exact=true}).CheckAsync();
    var endpoint="**/api/ui/clients/"+f.ClientId+"/contact-creation";
    await page.RouteAsync(endpoint,async route=>{if(route.Request.Method=="POST"){await using var accepted=await route.FetchAsync();Assert.Equal(200,accepted.Status);await route.AbortAsync("failed");}else await route.ContinueAsync();});
    await confirm.ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Contact outcome unconfirmed",Exact=true})).ToBeVisibleAsync();
    await page.UnrouteAsync(endpoint);await page.ReloadAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Verify contact receipt",Exact=true}).ClickAsync();
    var recorded=page.GetByRole(AriaRole.Region,new(){Name="Contact creation receipt",Exact=true});await Assertions.Expect(recorded).ToContainTextAsync("9007199254740994");
    await page.GetByRole(AriaRole.Button,new(){Name="Acknowledge contact result",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Contact outcome unconfirmed",Exact=true})).ToHaveCountAsync(0);
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("()=>document.documentElement.scrollWidth<=innerWidth+1"));
    await using(var db=host.CreateDbContext()) {
      Assert.Equal(28,await db.ClientContacts.CountAsync());Assert.Single(await db.ClientContactCreations.ToListAsync());
      Assert.Single(await db.ClientContacts.Where(c=>c.Primary).ToListAsync());
      await db.RoleGrants.Where(g=>g.UserId==f.Staff.Id&&g.Role=="Manager").ExecuteUpdateAsync(s=>s.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow)); }
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh contact context",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Contact workspace unavailable",Exact=true})).ToBeVisibleAsync();
    Assert.DoesNotContain("created@example.test",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
}
