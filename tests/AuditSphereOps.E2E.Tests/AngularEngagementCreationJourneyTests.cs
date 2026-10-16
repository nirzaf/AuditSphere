using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularEngagementCreationJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId","ANGULAR-ENGAGEMENT-CREATION-E2E")]
  public async Task ReviewBlockedCreationDraftLostResponseRecoveryAndRevocation(bool canonical)
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-ENGAGEMENT-CREATION-E2E");var f=host.Fixture;
    await using(var db=host.CreateDbContext()) {
      await EngagementCreationReviewSeed.PopulateAsync(db,f); }
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()});
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    page.Dialog+=async(_,d)=>{Assert.Equal("beforeunload",d.Type);await d.AcceptAsync();};
    var prefix=canonical?"":"/ui";var profile=prefix+"/app/clients/"+f.ClientId;var path=profile+"/engagements/new";
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(profile));
    await page.GetByRole(AriaRole.Link,new(){Name="Review new engagement",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Create blocked engagement",Exact=true})).ToBeVisibleAsync();
    await page.GetByLabel("Service route",new(){Exact=true}).FillAsync("AccountingOnly");
    await page.GetByLabel("Service profile",new(){Exact=true}).FillAsync("SYNTHETIC-REVIEWED-CREATION");
    await page.GetByLabel("Period start",new(){Exact=true}).FillAsync("2027-01-01");
    await page.GetByLabel("Period end",new(){Exact=true}).FillAsync("2027-12-31");
    var creationPath = "/api/ui/clients/" + f.ClientId + "/engagement-creation";
    var mutations = new System.Collections.Concurrent.ConcurrentQueue<string>();
    page.Request += (_, request) => {
      var requestPath = new Uri(request.Url).AbsolutePath;
      if (request.Method == "POST" && (requestPath == creationPath || requestPath == creationPath + "/preview"))
        mutations.Enqueue(requestPath);
    };
    var reviewButton = page.GetByRole(AriaRole.Button, new() { Name = "Review engagement", Exact = true });
    var intent = page.GetByRole(AriaRole.Region, new() { Name = "Reviewed engagement intent", Exact = true });
    var assent = page.GetByLabel("I reviewed this exact client, service, profile and period.", new() { Exact = true });
    var validation = page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Enter a service route" });
    var detailsForm = page.GetByRole(AriaRole.Region, new() { Name = "Engagement details", Exact = true }).Locator("form");
    foreach (var invalid in new[] {
      (Label: "Service profile", Value: "", Corrected: "SYNTHETIC-REVIEWED-CREATION"),
      (Label: "Period start", Value: "2028-01-01", Corrected: "2027-01-01"),
    }) {
      // Begin with an assented review so an invalid edit must also invalidate prior authority.
      var beforeReview = mutations.Count;
      await reviewButton.ClickAsync();
      await Assertions.Expect(intent).ToBeVisibleAsync();
      Assert.Equal(beforeReview + 1, mutations.Count);
      Assert.Equal(creationPath + "/preview", mutations.Last());
      await assent.CheckAsync();
      await Assertions.Expect(intent.GetByRole(AriaRole.Button, new() { Name = "Confirm blocked creation", Exact = true })).ToBeEnabledAsync();

      await page.GetByLabel(invalid.Label, new() { Exact = true }).FillAsync(invalid.Value);
      await Assertions.Expect(validation).ToBeVisibleAsync();
      await Assertions.Expect(validation).ToContainTextAsync("start on or before end");
      await Assertions.Expect(reviewButton).ToBeDisabledAsync();
      await Assertions.Expect(intent).ToHaveCountAsync(0);
      await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Confirm blocked creation", Exact = true })).ToHaveCountAsync(0);
      // Exercise the submit guard even though the visible review button is disabled.
      await detailsForm.DispatchEventAsync("submit");
      await Assertions.Expect(validation).ToBeVisibleAsync();
      Assert.Equal(beforeReview + 1, mutations.Count);

      await page.GetByLabel(invalid.Label, new() { Exact = true }).FillAsync(invalid.Corrected);
      await Assertions.Expect(validation).ToHaveCountAsync(0);
      await Assertions.Expect(reviewButton).ToBeEnabledAsync();
      await Assertions.Expect(intent).ToHaveCountAsync(0);
      Assert.Equal(beforeReview + 1, mutations.Count);
      await reviewButton.ClickAsync();
      await Assertions.Expect(intent).ToBeVisibleAsync();
      await Assertions.Expect(assent).Not.ToBeCheckedAsync();
      await Assertions.Expect(intent.GetByRole(AriaRole.Button, new() { Name = "Confirm blocked creation", Exact = true })).ToBeDisabledAsync();
      Assert.Equal(beforeReview + 2, mutations.Count);
      Assert.Equal(creationPath + "/preview", mutations.Last());
    }
    // Native maxlength prevents an oversized route from entering the Angular model. Do not
    // bypass that browser boundary merely to manufacture an unreachable invalid UI state.
    var routeInput = page.GetByLabel("Service route", new() { Exact = true });
    await Assertions.Expect(routeInput).ToHaveAttributeAsync("maxlength", "50");
    var beforeRouteEdit = mutations.Count;
    await routeInput.FillAsync(new string('A', 51));
    await Assertions.Expect(routeInput).ToHaveValueAsync(new string('A', 50));
    await Assertions.Expect(intent).ToHaveCountAsync(0);
    Assert.Equal(beforeRouteEdit, mutations.Count);
    await routeInput.FillAsync("AccountingOnly");
    await reviewButton.ClickAsync();
    await Assertions.Expect(intent).ToBeVisibleAsync();
    await Assertions.Expect(assent).Not.ToBeCheckedAsync();
    Assert.DoesNotContain(creationPath, mutations);
    await using (var db = host.CreateDbContext()) {
      Assert.Single(await db.Engagements.ToListAsync());
      Assert.Empty(await db.EngagementCreations.ToListAsync());
    }
    await page.GetByRole(AriaRole.Button,new(){Name="Save engagement draft",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Client profile",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Save draft and continue",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Review new engagement",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Restore engagement draft",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByLabel("Service profile",new(){Exact=true})).ToHaveValueAsync("SYNTHETIC-REVIEWED-CREATION");
    await page.GetByRole(AriaRole.Button,new(){Name="Review engagement",Exact=true}).ClickAsync();
    var review=page.GetByRole(AriaRole.Region,new(){Name="Reviewed engagement intent",Exact=true});await Assertions.Expect(review).ToContainTextAsync("Draft · professional work blocked");
    var confirm=review.GetByRole(AriaRole.Button,new(){Name="Confirm blocked creation",Exact=true});await Assertions.Expect(confirm).ToBeDisabledAsync();
    await page.GetByLabel("I reviewed this exact client, service, profile and period.",new(){Exact=true}).CheckAsync();
    var endpoint="**/api/ui/clients/"+f.ClientId+"/engagement-creation";
    await page.RouteAsync(endpoint,async route=>{if(route.Request.Method=="POST"){await using var accepted=await route.FetchAsync();Assert.Equal(200,accepted.Status);await route.AbortAsync("failed");}else await route.ContinueAsync();});
    await confirm.ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Creation outcome unconfirmed",Exact=true})).ToBeVisibleAsync();
    await page.UnrouteAsync(endpoint);await page.ReloadAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Verify creation receipt",Exact=true}).ClickAsync();
    var recorded=page.GetByRole(AriaRole.Region,new(){Name="Engagement creation receipt",Exact=true});await Assertions.Expect(recorded).ToContainTextAsync("SYNTHETIC-REVIEWED-CREATION");
    await page.GetByRole(AriaRole.Button,new(){Name="Acknowledge creation result",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Creation outcome unconfirmed",Exact=true})).ToHaveCountAsync(0);
    await page.GetByLabel("Service route",new(){Exact=true}).FillAsync("AccountingOnly");
    await page.GetByLabel("Service profile",new(){Exact=true}).FillAsync("SYNTHETIC-REVIEWED-CREATION");
    await page.GetByLabel("Period start",new(){Exact=true}).FillAsync("2027-01-01");
    await page.GetByLabel("Period end",new(){Exact=true}).FillAsync("2027-12-31");
    await page.GetByRole(AriaRole.Button,new(){Name="Review engagement",Exact=true}).ClickAsync();
    var existing=page.GetByRole(AriaRole.Region,new(){Name="Existing engagement",Exact=true});
    await Assertions.Expect(existing).ToContainTextAsync("Matching engagement already exists");
    var inspect=existing.GetByRole(AriaRole.Link,new(){Name="Inspect existing engagement",Exact=true});
    await Assertions.Expect(inspect).ToHaveAttributeAsync("href",new System.Text.RegularExpressions.Regex("/app/engagements/"));
    await Assertions.Expect(existing.GetByRole(AriaRole.Button,new(){Name="Confirm blocked creation",Exact=true})).ToHaveCountAsync(0);
    await page.GetByRole(AriaRole.Button,new(){Name="Discard engagement edits",Exact=true}).ClickAsync();
    await page.GetByLabel("Service route",new(){Exact=true}).FillAsync("AccountingOnly");
    await page.GetByLabel("Service profile",new(){Exact=true}).FillAsync("SYNTHETIC-REVIEWED-CREATION");
    await page.GetByLabel("Period start",new(){Exact=true}).FillAsync("2027-06-01");
    await page.GetByLabel("Period end",new(){Exact=true}).FillAsync("2028-05-31");
    await page.GetByRole(AriaRole.Button,new(){Name="Review engagement",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByText("An engagement for this client and service overlaps the requested period.",new(){Exact=false})).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Confirm blocked creation",Exact=true})).ToHaveCountAsync(0);
    await page.GetByRole(AriaRole.Button,new(){Name="Discard engagement edits",Exact=true}).ClickAsync();
    if (canonical) await OwnedBrowserFixtureHandoff.WaitAsync(origin, path);
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("()=>document.documentElement.scrollWidth<=innerWidth+1"));
    await using(var db=host.CreateDbContext()) {
      Assert.Equal(2,await db.Engagements.CountAsync());Assert.Single(await db.EngagementCreations.ToListAsync());
      var e=await db.Engagements.SingleAsync(e=>e.ServiceProfileId=="SYNTHETIC-REVIEWED-CREATION");Assert.Equal("Draft",e.Status);Assert.True(e.ProfessionalWorkBlocked);
      Assert.Empty(await db.EngagementActivations.ToListAsync());
      await db.RoleGrants.Where(g=>g.UserId==f.Staff.Id&&g.Role=="Manager").ExecuteUpdateAsync(s=>s.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow)); }
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh creation context",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Engagement creation unavailable",Exact=true})).ToBeVisibleAsync();
    Assert.DoesNotContain("SYNTHETIC-REVIEWED-CREATION",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
}
