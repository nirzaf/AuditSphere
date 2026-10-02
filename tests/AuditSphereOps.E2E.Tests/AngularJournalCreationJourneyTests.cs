using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularJournalCreationJourneyTests
{
  [Fact][Trait("CaseId","ANGULAR-JOURNAL-CREATION")]
  public async Task ExactSourceCreationRecoversUnknownResultAndRetainsOriginalLinesInNativeHistory()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-JOURNAL-CREATION");Guid source,journal;
    await using(var db=host.CreateDbContext()){journal=await JournalReviewSeed.SeedAsync(db,host.Fixture);source=await db.AdjustmentJournals.Where(x=>x.Id==journal).Select(x=>x.BaseDatasetId).SingleAsync();}
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString("/ui/app/accounting/journals/"+journal));
    await page.GetByRole(AriaRole.Button,new(){Name="Inspect exact journal source",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Link,new(){Name="Prepare new adjustment journal",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Journal creation source context",Exact=true})).ToContainTextAsync(source.ToString());
    await page.GetByLabel("Journal number",new(){Exact=true}).FillAsync("AJ-BROWSER-NEW");await page.GetByLabel("New journal rationale",new(){Exact=true}).FillAsync("Synthetic browser creation rationale");await page.GetByLabel("New journal evidence reference",new(){Exact=true}).FillAsync("Synthetic source evidence");
    await page.GetByLabel("New account 1",new(){Exact=true}).FillAsync("1000");await page.GetByLabel("New account 2",new(){Exact=true}).FillAsync("3000");
    await page.GetByLabel("New debit 1",new(){Exact=true}).FillAsync("200.123456");await page.GetByLabel("New debit 1",new(){Exact=true}).PressAsync("Tab");await Assertions.Expect(page.GetByLabel("New credit 1",new(){Exact=true})).ToBeFocusedAsync();await page.GetByLabel("New credit 2",new(){Exact=true}).FillAsync("200.123455");
    await page.GetByRole(AriaRole.Button,new(){Name="Preview new journal",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="New journal creation review",Exact=true})).ToContainTextAsync("Unbalanced");await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Create reviewed journal draft",Exact=true})).ToBeHiddenAsync();
    await page.GetByLabel("New credit 2",new(){Exact=true}).FillAsync("200.123456");await page.GetByRole(AriaRole.Button,new(){Name="Preview new journal",Exact=true}).ClickAsync();
    await page.GetByLabel("I reviewed the exact source, purpose, origin, rationale, evidence and complete new journal lines.",new(){Exact=true}).CheckAsync();
    await page.RouteAsync("**/api/ui/datasets/"+source+"/journal-drafts",async route=>{if(route.Request.Method!="POST"){await route.ContinueAsync();return;}var response=await route.FetchAsync();Assert.True(response.Ok);await route.FulfillAsync(new(){Status=503,ContentType="application/json",Body="{\"code\":\"synthetic.lost.ack\"}"});});
    await page.GetByRole(AriaRole.Button,new(){Name="Create reviewed journal draft",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Unconfirmed journal creation",Exact=true})).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Check persisted creation receipt",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Link,new(){Name="Open created journal draft",Exact=true})).ToBeHiddenAsync();await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Draft retained",Exact=true})).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Acknowledge retained creation receipt",Exact=true}).ClickAsync();await page.GetByRole(AriaRole.Link,new(){Name="Open created journal draft",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Exact journal context",Exact=true})).ToContainTextAsync("AJ-BROWSER-NEW");
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Journal revision timeline",Exact=true})).ToContainTextAsync("CREATE");
    await page.GetByRole(AriaRole.Button,new(){NameRegex=new System.Text.RegularExpressions.Regex("^Inspect retained revision ")}).ClickAsync();
    var history=page.GetByRole(AriaRole.Region,new(){Name="Retained journal revision evidence",Exact=true});await Assertions.Expect(history).ToContainTextAsync("No journal existed before this retained creation event.");await Assertions.Expect(history).ToContainTextAsync("200.123456");
    await page.ReloadAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Journal revision timeline",Exact=true})).ToContainTextAsync("CREATE");
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await using(var db=host.CreateDbContext()){Assert.Equal(1,await db.AdjustmentJournalActions.CountAsync(x=>x.Action=="CREATE"));Assert.Equal(2,await db.AdjustmentJournals.CountAsync());await db.Users.Where(x=>x.Id==host.Fixture.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));}
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});Assert.DoesNotContain("200.123456",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
}
