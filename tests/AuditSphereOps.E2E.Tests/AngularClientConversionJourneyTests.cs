using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularClientConversionJourneyTests
{
 [Theory][InlineData(false)][InlineData(true)]
 public async Task ReviewedConversionLostResponseRecoversAfterReloadWithoutDuplicateClient(bool canonical)
 {
  await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-CLIENT-CONVERSION");var f=host.Fixture;Guid id;
  await using(var db=host.CreateDbContext())id=await ClientConversionReviewSeed.PopulateAsync(db,f);
  var origin=await host.StartApiForIdentityAsync(f.Admin,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()});
  using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
  var path=(canonical?"":"/ui")+"/app/practice/proposals/"+id+"/client-conversion";await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(path));
  await page.GetByLabel("Legal name",new(){Exact=true}).FillAsync("Reviewed browser synthetic client");await page.GetByRole(AriaRole.Button,new(){Name="Review client conversion",Exact=true}).ClickAsync();
  var review=page.GetByRole(AriaRole.Region,new(){Name="Client conversion review",Exact=true});await Assertions.Expect(review).ToContainTextAsync("No invitation or portal access is granted");
  await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Confirm client conversion",Exact=true})).ToBeDisabledAsync();
  var calls=0;await page.RouteAsync("**/client-conversion",async route=>{if(route.Request.Method!="POST"){await route.ContinueAsync();return;}Interlocked.Increment(ref calls);var response=await route.FetchAsync();Assert.Equal(200,response.Status);await route.AbortAsync();});
  await review.GetByLabel("I reviewed this exact legal identity, client match and access effects.",new(){Exact=true}).CheckAsync();await review.GetByRole(AriaRole.Button,new(){Name="Confirm client conversion",Exact=true}).ClickAsync();
  await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Verify conversion receipt",Exact=true})).ToBeVisibleAsync();await page.ReloadAsync();await page.GetByRole(AriaRole.Button,new(){Name="Verify conversion receipt",Exact=true}).ClickAsync();
  var receipt=page.GetByRole(AriaRole.Region,new(){Name="Client conversion receipt",Exact=true});await Assertions.Expect(receipt).ToContainTextAsync("Professional acceptance remains");await receipt.GetByRole(AriaRole.Button,new(){Name="Acknowledge client conversion",Exact=true}).ClickAsync();
  await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Converted client",Exact=true})).ToBeVisibleAsync();Assert.Equal(1,calls);
  await using(var db=host.CreateDbContext()){var retained=await db.ClientConversions.SingleAsync();Assert.Equal(id,retained.ProposalId);Assert.Equal(f.Admin.Id,retained.ActorId);Assert.Equal("PROSPECT",(await db.PracticeClients.SingleAsync(x=>x.Id==retained.ClientId)).Status);Assert.Single(await db.ClientPortalIntents.Where(x=>x.PracticeClientId==retained.ClientId).ToListAsync());}
  await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));Assert.Empty(errors);
 }
}
