using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularJournalManagementJourneyTests
{
  [Fact][Trait("CaseId","ANGULAR-JOURNAL-CLIENT-MANAGEMENT")]
  public async Task ClientQueueExactDispositionLostAcknowledgmentAndRevocationAreNativeAndScoped()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-JOURNAL-CLIENT-MANAGEMENT", startLegacyBlazorHosts: false);Guid id;
    await using(var db=host.CreateDbContext())id=await JournalReviewSeed.SeedAsync(db,host.Fixture);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Client,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl=/ui/portal");await page.GetByRole(AriaRole.Link,new(){Name="AJ-SYN · revision 1",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Exact management journal context",Exact=true})).ToContainTextAsync("Your authenticated client response");
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Management journal lines",Exact=true})).ToContainTextAsync("100.123456");
    await page.GetByLabel("Management disposition",new(){Exact=true}).SelectOptionAsync("PARTIAL");await page.GetByLabel("Management response rationale",new(){Exact=true}).FillAsync("Synthetic accepted and rejected portions reviewed");await page.GetByLabel("Management response evidence reference",new(){Exact=true}).FillAsync("Synthetic exact line bridge");
    await page.GetByRole(AriaRole.Button,new(){Name="Preview management response",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Management disposition preview",Exact=true})).ToContainTextAsync("PARTIAL · SIGNED_IN");
    await page.GetByLabel("I reviewed the exact journal revision, source, complete lines, disposition and evidence.",new(){Exact=true}).CheckAsync();
    var url="**/api/ui/portal/accounting/journals/"+id;await page.RouteAsync(url,async r=>{if(r.Request.Method!="POST"){await r.ContinueAsync();return;}var response=await r.FetchAsync();Assert.True(response.Ok);await r.FulfillAsync(new(){Status=503,ContentType="application/json",Body="{\"code\":\"synthetic.lost.ack\"}"});});
    await page.GetByRole(AriaRole.Button,new(){Name="Record reviewed management response",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Unconfirmed management response",Exact=true})).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Back to client portal",Exact=true}).ClickAsync();Assert.Contains("/portal/accounting/journals/"+id,page.Url);
    await page.GetByRole(AriaRole.Button,new(){Name="Check retained management receipt",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Recovered management receipt",Exact=true})).ToContainTextAsync("PARTIAL · SIGNED_IN");
    await page.GetByRole(AriaRole.Button,new(){Name="Acknowledge retained management receipt",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Persisted management response",Exact=true})).ToContainTextAsync(host.Fixture.Client.Id.ToString());
    await page.ReloadAsync();await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Retained management response",Exact=true})).ToBeVisibleAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Management response editor",Exact=true})).ToBeHiddenAsync();
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth+1"));
    await page.GotoAsync(origin+"/ui/portal/accounting/journals/"+Guid.NewGuid());await Assertions.Expect(page.GetByText("This information is unavailable in your current scope.",new(){Exact=true})).ToBeVisibleAsync();Assert.DoesNotContain("100.123456",await page.Locator("body").InnerTextAsync());
    await page.GotoAsync(origin+"/ui/portal/accounting/journals/"+id);await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Retained management response",Exact=true})).ToBeVisibleAsync();
    await using(var db=host.CreateDbContext()){Assert.Equal("Draft",await db.AdjustmentJournals.Where(x=>x.Id==id).Select(x=>x.Status).SingleAsync());Assert.Equal(1,await db.AdjustmentJournalActions.CountAsync(x=>x.Action=="MANAGEMENT"));await db.Users.Where(x=>x.Id==host.Fixture.Client.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));}
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});Assert.DoesNotContain("100.123456",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
  [Fact][Trait("CaseId","ANGULAR-JOURNAL-OFFLINE-MANAGEMENT")]
  public async Task OfflineStaffEvidenceAppearsInImmutableHistoryWithoutClaimingClientAuthentication()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-JOURNAL-OFFLINE-MANAGEMENT", startLegacyBlazorHosts: false);Guid id;
    await using(var db=host.CreateDbContext())id=await JournalReviewSeed.SeedAsync(db,host.Fixture);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString("/ui/app/accounting/journals/"+id));await page.GetByRole(AriaRole.Link,new(){Name="Review management response",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Exact management journal context",Exact=true})).ToContainTextAsync("Staff records offline management evidence");
    await page.GetByLabel("Management response rationale",new(){Exact=true}).FillAsync("Synthetic evidenced client acceptance");await page.GetByLabel("Management response evidence reference",new(){Exact=true}).FillAsync("Synthetic signed client letter");await page.GetByRole(AriaRole.Button,new(){Name="Preview management response",Exact=true}).ClickAsync();
    await page.GetByLabel("I reviewed the exact journal revision, source, complete lines, disposition and evidence.",new(){Exact=true}).CheckAsync();await page.GetByRole(AriaRole.Button,new(){Name="Record reviewed management response",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Persisted management response",Exact=true})).ToContainTextAsync("Offline evidence; no client authentication is claimed.");
    await page.GetByRole(AriaRole.Link,new(){Name="Open technical journal review",Exact=true}).ClickAsync();await page.GetByRole(AriaRole.Button,new(){NameRegex=new System.Text.RegularExpressions.Regex("^Inspect retained revision ")}).ClickAsync();
    var history=page.GetByRole(AriaRole.Region,new(){Name="Retained journal revision evidence",Exact=true});await Assertions.Expect(history).ToContainTextAsync("ACCEPTED · OFFLINE · exact revision 1");await Assertions.Expect(history).ToContainTextAsync("100.123456");await Assertions.Expect(page.GetByLabel("Account 1",new(){Exact=true})).ToBeHiddenAsync();
  }
}
