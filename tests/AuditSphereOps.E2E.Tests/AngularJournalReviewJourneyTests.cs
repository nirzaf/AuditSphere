using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularJournalReviewJourneyTests
{
  [Fact][Trait("CaseId","ANGULAR-JOURNAL-REVIEW")]
  public async Task ExactLineCorrectionUnknownReceiptRecoveryReturnAndIndependentPostPreserveHistory()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-JOURNAL-REVIEW");
    Guid id;await using(var db=host.CreateDbContext())id=await JournalReviewSeed.SeedAsync(db,host.Fixture);
    var settings=new Dictionary<string,string>{["AngularUi__Enabled"]="true"};
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,settings);
    var reviewerOrigin=await host.StartApiForIdentityAsync(host.Fixture.Reviewer,settings);
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);
    var page=await browser.NewPageAsync();var reviewer=await browser.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);reviewer.PageError+=(_,e)=>errors.Add(e);
    var route="/ui/app/accounting/journals/"+id;
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Exact journal context",Exact=true})).ToContainTextAsync("AJ-SYN");
    await page.GetByLabel("Debit 1",new(){Exact=true}).FillAsync("200.123456");
    await page.GetByLabel("Credit 2",new(){Exact=true}).FillAsync("200.123455");
    await page.GetByRole(AriaRole.Button,new(){Name="Preview exact journal action",Exact=true}).ClickAsync();
    var preview=page.GetByRole(AriaRole.Region,new(){Name="Journal action review",Exact=true});
    await Assertions.Expect(preview).ToContainTextAsync("Unbalanced");await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Confirm reviewed journal action",Exact=true})).ToBeHiddenAsync();
    await page.GetByLabel("Credit 2",new(){Exact=true}).FillAsync("200.123456");
    var intercepted=false;
    await page.RouteAsync("**/api/ui/accounting/journals/"+id+"/actions",async interceptedRoute=>{
      if(intercepted){await interceptedRoute.ContinueAsync();return;}
      intercepted=true;var response=await interceptedRoute.FetchAsync();Assert.True(response.Ok);
      await interceptedRoute.FulfillAsync(new(){Status=503,ContentType="application/json",Body="{\"code\":\"synthetic.lost.ack\",\"message\":\"Synthetic lost acknowledgement\"}"});
    });
    await Confirm(page);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Unconfirmed journal action",Exact=true})).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Check persisted journal receipt",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByText("The retained receipt confirms the action. Acknowledge it before another action.",new(){Exact=true})).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Acknowledge retained journal receipt",Exact=true}).ClickAsync();
    var context=page.GetByRole(AriaRole.Region,new(){Name="Exact journal context",Exact=true});
    await Assertions.Expect(context).ToContainTextAsync("Revision 2");await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Current journal lines",Exact=true})).ToContainTextAsync("200.123456");
    await page.GetByRole(AriaRole.Button,new(){NameRegex=new System.Text.RegularExpressions.Regex("^Inspect retained revision ")}).First.ClickAsync();
    var historical=page.GetByRole(AriaRole.Region,new(){Name="Retained journal revision evidence",Exact=true});
    await Assertions.Expect(historical).ToContainTextAsync("100.123456");await Assertions.Expect(historical).ToContainTextAsync("200.123456");
    await page.GetByRole(AriaRole.Button,new(){Name="Close journal revision evidence",Exact=true}).ClickAsync();
    await page.GetByLabel("Journal action",new(){Exact=true}).SelectOptionAsync("SUBMIT");await Confirm(page);
    await Assertions.Expect(context).ToContainTextAsync("Submitted",new(){IgnoreCase=true});
    await reviewer.GotoAsync(reviewerOrigin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
    await Assertions.Expect(reviewer.GetByRole(AriaRole.Region,new(){Name="Exact journal context",Exact=true})).ToContainTextAsync("Submitted",new(){IgnoreCase=true});
    await reviewer.GetByLabel("Journal action",new(){Exact=true}).SelectOptionAsync("RETURN");
    await reviewer.GetByLabel("Action reason",new(){Exact=true}).FillAsync("Correct the retained exact evidence");
    await Confirm(reviewer);
    await Assertions.Expect(reviewer.GetByRole(AriaRole.Region,new(){Name="Exact journal context",Exact=true})).ToContainTextAsync("Returned",new(){IgnoreCase=true});
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh journal review",Exact=true}).ClickAsync();
    await Assertions.Expect(context).ToContainTextAsync("Returned",new(){IgnoreCase=true});await Assertions.Expect(context).ToContainTextAsync("Correct the retained exact evidence");
    await page.GetByLabel("Debit 1",new(){Exact=true}).FillAsync("201.123456");await page.GetByLabel("Credit 2",new(){Exact=true}).FillAsync("201.123456");await Confirm(page);
    await Assertions.Expect(context).ToContainTextAsync("Revision 3");await page.GetByLabel("Journal action",new(){Exact=true}).SelectOptionAsync("SUBMIT");await Confirm(page);
    await Assertions.Expect(context).ToContainTextAsync("Submitted",new(){IgnoreCase=true});
    await reviewer.GetByRole(AriaRole.Button,new(){Name="Refresh journal review",Exact=true}).ClickAsync();
    await Assertions.Expect(reviewer.GetByLabel("Journal action",new(){Exact=true})).ToHaveValueAsync("POST");await Confirm(reviewer);
    await Assertions.Expect(reviewer.GetByRole(AriaRole.Region,new(){Name="Exact journal context",Exact=true})).ToContainTextAsync("Posted",new(){IgnoreCase=true});
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh journal review",Exact=true}).ClickAsync();
    await Assertions.Expect(context).ToContainTextAsync("Posted",new(){IgnoreCase=true});await Assertions.Expect(page.GetByLabel("Debit 1",new(){Exact=true})).ToBeHiddenAsync();
    await page.GetByLabel("Reversal journal number",new(){Exact=true}).FillAsync("AJ-SYN-R");await Confirm(page);
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Journal revision timeline",Exact=true})).ToContainTextAsync("REVERSE");
    await page.GetByRole(AriaRole.Link,new(){Name="Open linked reversal draft",Exact=true}).ClickAsync();
    await Assertions.Expect(context).ToContainTextAsync("AJ-SYN-R");await Assertions.Expect(context).ToContainTextAsync("Draft",new(){IgnoreCase=true});
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    await using(var db=host.CreateDbContext())await db.Users.Where(x=>x.Id==host.Fixture.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain("201.123456",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }

  [Fact][Trait("CaseId","ANGULAR-JOURNAL-STALE")]
  public async Task ChangedPeriodRefusesReviewedMutationAndClearsOldPreviewOnRefresh()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-JOURNAL-STALE");
    Guid id;await using(var db=host.CreateDbContext())id=await JournalReviewSeed.SeedAsync(db,host.Fixture);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString("/ui/app/accounting/journals/"+id));
    await page.GetByRole(AriaRole.Button,new(){Name="Preview exact journal action",Exact=true}).ClickAsync();
    var assent=page.GetByLabel("I reviewed the exact source, period, purpose, lines, rationale and requested journal action.",new(){Exact=true});await assent.CheckAsync();
    await using(var db=host.CreateDbContext()) {var period=await db.AdjustmentJournals.Where(x=>x.Id==id).Select(x=>x.PeriodId).SingleAsync();await db.ClientReportingPeriods.Where(x=>x.Id==period).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Status,"CLOSED"));}
    await page.GetByRole(AriaRole.Button,new(){Name="Confirm reviewed journal action",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByText("This record changed since you loaded it. Refresh and review the current revision.",new(){Exact=true})).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh journal review",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByText("The reporting period or book is closed or unavailable.",new(){Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(assent).ToBeHiddenAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Journal action editor",Exact=true})).ToBeHiddenAsync();
    await using var verify=host.CreateDbContext();Assert.Empty(await verify.AdjustmentJournalActions.ToListAsync());Assert.Equal("Draft",await verify.AdjustmentJournals.Where(x=>x.Id==id).Select(x=>x.Status).SingleAsync());
  }
  private static async Task Confirm(IPage page)
  {
    await page.GetByRole(AriaRole.Button,new(){Name="Preview exact journal action",Exact=true}).ClickAsync();
    await page.GetByLabel("I reviewed the exact source, period, purpose, lines, rationale and requested journal action.",new(){Exact=true}).CheckAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Confirm reviewed journal action",Exact=true}).ClickAsync();
  }
}
