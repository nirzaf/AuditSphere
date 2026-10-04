using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularConfirmationJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-CONFIRMATION-LIFECYCLE")]
  public async Task NativeSignalForms_ObservedDispatch_IndependentAlternativeReview_AndSessionRevocation()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-CONFIRMATION-LIFECYCLE", startLegacyBlazorHosts: false);
    var settings=new Dictionary<string,string>{["AngularUi__Enabled"]="true"};
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,settings);var reviewerOrigin=await host.StartApiForIdentityAsync(host.Fixture.Reviewer,settings);
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext=await browser.NewContextAsync();await using var reviewContext=await browser.NewContextAsync();
    var staff=await staffContext.NewPageAsync();var reviewer=await reviewContext.NewPageAsync();var errors=new List<string>();staff.PageError+=(_,e)=>errors.Add(e);reviewer.PageError+=(_,e)=>errors.Add(e);
    var route="/ui/app/engagements/"+host.Fixture.EngagementId+"/confirmations";
    await staff.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
    await Assertions.Expect(staff.GetByRole(AriaRole.Heading,new(){Name="Confirmations",Exact=true})).ToBeVisibleAsync();
    await staff.GetByText("Prepare confirmation",new(){Exact=true}).ClickAsync();
    foreach(var (label,value) in new[]{("Audit area","CASH_BANK"),("Source record","bank-001"),("Booked amount","123456789.123456"),("Currency","QAR"),("Confirmation date","2026-10-02"),("Respondent","Synthetic bank"),("Validated contact source","Approved contact register")})await staff.GetByLabel(label,new(){Exact=true}).FillAsync(value);
    await Assertions.Expect(staff.GetByRole(AriaRole.Button,new(){Name="Prepare reviewed confirmation",Exact=true})).ToBeDisabledAsync();
    await staff.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed the case identity, amount, date and contact source.",Exact=true}).CheckAsync();
    await staff.GetByRole(AriaRole.Button,new(){Name="Prepare reviewed confirmation",Exact=true}).ClickAsync();
    await Assertions.Expect(staff.Locator("audit-confirmations table")).ToContainTextAsync("Synthetic bank");
    await reviewer.GotoAsync(reviewerOrigin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
    await Open(reviewer);await Act(reviewer,"APPROVE","Approve preparation");
    await staff.ReloadAsync();await Open(staff);await Assertions.Expect(staff.Locator("audit-confirmations")).ToContainTextAsync("Not dispatched");
    await staff.GetByLabel("Case action",new(){Exact=true}).SelectOptionAsync("DISPATCH");await staff.GetByLabel("Observed dispatch reference",new(){Exact=true}).FillAsync("observed-synthetic-provider-reference");await Confirm(staff,"Record observed dispatch");
    await Assertions.Expect(staff.Locator("audit-confirmations table")).ToContainTextAsync("AWAITING_RESPONSE");
    await staff.GetByLabel("Case action",new(){Exact=true}).SelectOptionAsync("RESPONSE");
    foreach(var (label,value) in new[]{("Response origin","FOLLOW_UP"),("Channel","CONTROLLED"),("Receipt / nonresponse observation reference","follow-up-observation"),("Authenticity assessment","No response observed through approved channel")})await staff.GetByLabel(label,new(){Exact=true}).FillAsync(value);
    await staff.GetByLabel("Explicit decision",new(){Exact=true}).SelectOptionAsync("NO_RESPONSE");await Confirm(staff,"Record response observation");
    await Assertions.Expect(staff.Locator("audit-confirmations table")).ToContainTextAsync("ALTERNATIVE_PROCEDURES");
    await reviewer.ReloadAsync();await Open(reviewer);await Act(reviewer,"REVIEW_RESPONSE","Review current response");
    await Act(reviewer,"CLOSE","Close reviewed case",("Reviewer conclusion","No response alone cannot close"));await Assertions.Expect(reviewer.Locator("audit-command-message")).ToContainTextAsync("cannot close");
    await staff.ReloadAsync();await Open(staff);await Act(staff,"ALTERNATIVE","Record alternative work",("Purpose","Inspect subsequent clearing"),("Evidence references (one per line)","immutable-evidence-001"),("Professional conclusion","Evidence supports recorded balance"));
    await reviewer.ReloadAsync();await Open(reviewer);await Act(reviewer,"REVIEW_ALTERNATIVE","Review current alternative");await Act(reviewer,"CLOSE","Close reviewed case",("Reviewer conclusion","Current alternative evidence is adequate"));
    await Assertions.Expect(reviewer.Locator("audit-confirmations table")).ToContainTextAsync("CLOSED");await reviewer.ReloadAsync();await Assertions.Expect(reviewer.Locator("audit-confirmations table")).ToContainTextAsync("CLOSED");await Open(reviewer);await Assertions.Expect(reviewer.GetByRole(AriaRole.Region,new(){Name="Retained closure decision",Exact=true})).ToContainTextAsync("Current alternative evidence is adequate");await Assertions.Expect(reviewer.GetByRole(AriaRole.Region,new(){Name="Retained closure decision",Exact=true})).ToContainTextAsync("Reviewed evidence SHA-256");
    await staff.SetViewportSizeAsync(390,844);await staff.ReloadAsync();Assert.True(await staff.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await using(var db=host.CreateDbContext()){Assert.Single(await db.AuditConfirmationCases.ToListAsync());Assert.Single(await db.AuditAlternativeProcedures.ToListAsync());(await db.Users.SingleAsync(x=>x.Id==host.Fixture.Staff.Id)).SessionEpoch++;await db.SaveChangesAsync();}
    await Assertions.Expect(staff.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});Assert.DoesNotContain("Synthetic bank",await staff.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
  [Fact]
  [Trait("CaseId","ANGULAR-CONFIRMATION-BATCH")]
  public async Task NativeBatchForm_ReviewsAllRows_RejectsPartialDuplicates_AndPersistsExactDrafts()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-CONFIRMATION-BATCH", startLegacyBlazorHosts: false);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    var route="/ui/app/engagements/"+host.Fixture.EngagementId+"/confirmations";
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
    await page.GetByText("Prepare confirmation batch",new(){Exact=true}).ClickAsync();
    await page.GetByLabel("Batch audit area",new(){Exact=true}).FillAsync("CASH_BANK");await page.GetByLabel("Batch currency",new(){Exact=true}).FillAsync("QAR");await page.GetByLabel("Batch confirmation date",new(){Exact=true}).FillAsync("2026-10-02");
    await page.GetByRole(AriaRole.Button,new(){Name="Add batch case",Exact=true}).ClickAsync();
    async Task Fill(int n,string source,string amount)
    {
      foreach(var (label,value) in new[]{("Batch source record "+n,source),("Batch booked amount "+n,amount),("Batch respondent "+n,"Synthetic batch bank "+n),("Batch validated contact source "+n,"Approved synthetic contact register")})await page.GetByLabel(label,new(){Exact=true}).FillAsync(value);
    }
    await Fill(1,"batch-001","123456789.123456");await Fill(2,"batch-002","-1.000001");
    var submit=page.GetByRole(AriaRole.Button,new(){Name="Prepare reviewed batch",Exact=true});var reviewed=page.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed every batch case and the shared area, currency, date and procedure.",Exact=true});
    await Assertions.Expect(submit).ToBeDisabledAsync();await reviewed.CheckAsync();await Assertions.Expect(submit).ToBeEnabledAsync();
    await page.GetByLabel("Batch booked amount 2",new(){Exact=true}).FillAsync("-1.000002");await Assertions.Expect(reviewed).Not.ToBeCheckedAsync();await Assertions.Expect(submit).ToBeDisabledAsync();
    await reviewed.CheckAsync();await page.RunAndWaitForResponseAsync(()=>submit.ClickAsync(),r=>r.Url.EndsWith("/confirmations/batch")&&r.Request.Method=="POST");
    await Assertions.Expect(page.Locator("audit-confirmations table")).ToContainTextAsync("Synthetic batch bank 1");await Assertions.Expect(page.Locator("audit-confirmations table")).ToContainTextAsync("Synthetic batch bank 2");
    await page.ReloadAsync();await Assertions.Expect(page.Locator("audit-confirmations table")).ToContainTextAsync("Synthetic batch bank 2");
    await page.GetByText("Prepare confirmation batch",new(){Exact=true}).ClickAsync();await page.GetByLabel("Batch audit area",new(){Exact=true}).FillAsync("CASH_BANK");await page.GetByLabel("Batch currency",new(){Exact=true}).FillAsync("QAR");await page.GetByLabel("Batch confirmation date",new(){Exact=true}).FillAsync("2026-10-02");await page.GetByRole(AriaRole.Button,new(){Name="Add batch case",Exact=true}).ClickAsync();await Fill(1,"batch-003","1.000001");await Fill(2,"batch-002","1.000001");await reviewed.CheckAsync();
    var refusal=await page.RunAndWaitForResponseAsync(()=>submit.ClickAsync(),r=>r.Url.EndsWith("/confirmations/batch")&&r.Request.Method=="POST");Assert.Equal(400,refusal.Status);
    await Assertions.Expect(page.Locator("audit-command-message")).ToContainTextAsync("entire batch was refused");await Assertions.Expect(reviewed).Not.ToBeCheckedAsync();
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await using(var db=host.CreateDbContext()){var cases=await db.AuditConfirmationCases.OrderBy(x=>x.SourceRecordId).ToListAsync();Assert.Equal(2,cases.Count);Assert.Equal(123456789.123456m,cases[0].BookedAmount);Assert.Equal(-1.000002m,cases[1].BookedAmount);Assert.All(cases,x=>{Assert.Equal("DRAFT",x.Status);Assert.Null(x.DispatchedAt);});}
    Assert.Empty(errors);
  }
  [Fact]
  [Trait("CaseId","ANGULAR-CONFIRMATION-DRAFT-RECOVERY")]
  public async Task TabDrafts_ExplicitRecovery_NavigationProtection_RevisionConflict_AndRevocation()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-CONFIRMATION-DRAFT-RECOVERY", startLegacyBlazorHosts: false);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);await using var context=await browser.NewContextAsync();
    var page=await context.NewPageAsync();var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    var route="/ui/app/engagements/"+host.Fixture.EngagementId+"/confirmations";
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
    await page.GetByText("Prepare confirmation",new(){Exact=true}).ClickAsync();
    async Task Fill(IPage target,string source,string respondent){foreach(var (label,value) in new[]{("Audit area","CASH_BANK"),("Source record",source),("Booked amount","123456789.123456"),("Currency","QAR"),("Confirmation date","2026-10-02"),("Respondent",respondent),("Validated contact source","Approved synthetic register")})await target.GetByLabel(label,new(){Exact=true}).FillAsync(value);}
    await Fill(page,"recover-001","Recoverable synthetic bank");
    var save=page.GetByRole(AriaRole.Button,new(){Name="Save case draft in tab",Exact=true});var recover=page.GetByRole(AriaRole.Button,new(){Name="Recover case draft",Exact=true});
    await save.ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Confirmation draft recovery",Exact=true})).ToContainTextAsync("saved in this tab only");
    await page.ReloadAsync();await page.GetByText("Prepare confirmation",new(){Exact=true}).ClickAsync();await Assertions.Expect(page.GetByLabel("Respondent",new(){Exact=true})).ToHaveValueAsync("");
    await recover.ClickAsync();await Assertions.Expect(page.GetByLabel("Booked amount",new(){Exact=true})).ToHaveValueAsync("123456789.123456");
    var reviewed=page.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed the case identity, amount, date and contact source.",Exact=true});await Assertions.Expect(reviewed).Not.ToBeCheckedAsync();
    await page.GetByLabel("Respondent",new(){Exact=true}).FillAsync("Edited synthetic bank");
    await page.GetByRole(AriaRole.Navigation,new(){Name="Breadcrumb",Exact=true}).GetByRole(AriaRole.Link,new(){Name="Engagement",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();await page.GetByRole(AriaRole.Button,new(){Name="Keep editing",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByLabel("Respondent",new(){Exact=true})).ToHaveValueAsync("Edited synthetic bank");
    await page.GetByRole(AriaRole.Navigation,new(){Name="Breadcrumb",Exact=true}).GetByRole(AriaRole.Link,new(){Name="Engagement",Exact=true}).ClickAsync();await page.GetByRole(AriaRole.Button,new(){Name="Save draft and continue",Exact=true}).ClickAsync();
    await Assertions.Expect(page.Locator("audit-confirmations")).ToHaveCountAsync(0);await page.GotoAsync(origin+route);await page.GetByText("Prepare confirmation",new(){Exact=true}).ClickAsync();await recover.ClickAsync();
    await Assertions.Expect(page.GetByLabel("Respondent",new(){Exact=true})).ToHaveValueAsync("Edited synthetic bank");
    var other=await context.NewPageAsync();await other.GotoAsync(origin+route);await Assertions.Expect(other.GetByRole(AriaRole.Button,new(){Name="Recover case draft",Exact=true})).ToHaveCountAsync(0);
    await other.GetByText("Prepare confirmation",new(){Exact=true}).ClickAsync();await Fill(other,"other-tab-001","Other tab bank");await other.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed the case identity, amount, date and contact source.",Exact=true}).CheckAsync();
    await other.GetByRole(AriaRole.Button,new(){Name="Prepare reviewed confirmation",Exact=true}).ClickAsync();await Assertions.Expect(other.Locator("audit-confirmations table")).ToContainTextAsync("Other tab bank");
    await page.GetByRole(AriaRole.Button,new(){Name="Refresh confirmation register",Exact=true}).ClickAsync();await Assertions.Expect(page.Locator("audit-confirmations table")).ToContainTextAsync("Other tab bank");
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="case draft",Exact=true})).ToContainTextAsync("submission is blocked");await Assertions.Expect(page.GetByLabel("Respondent",new(){Exact=true})).ToHaveValueAsync("Edited synthetic bank");
    await reviewed.CheckAsync();await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Prepare reviewed confirmation",Exact=true})).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Button,new(){Name="Use refreshed revision for case draft",Exact=true}).ClickAsync();await Assertions.Expect(reviewed).Not.ToBeCheckedAsync();await save.ClickAsync();
    await using(var db=host.CreateDbContext()){Assert.Single(await db.AuditConfirmationCases.ToListAsync());(await db.Users.SingleAsync(x=>x.Id==host.Fixture.Staff.Id)).SessionEpoch++;await db.SaveChangesAsync();}
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain("Edited synthetic bank",await page.Locator("body").InnerTextAsync());Assert.Equal(0,await page.EvaluateAsync<int>("() => Object.keys(sessionStorage).filter(k => k.startsWith('auditsphere-tab-draft-v1:')).length"));Assert.Empty(errors);
  }
  [Fact]
  [Trait("CaseId","ANGULAR-CONFIRMATION-DRAFT-STORAGE-FAILURE")]
  public async Task TabStorageFailure_DoesNotPretendSaved_AndKeepsNavigationIntent()
  {
    await using var host=await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-CONFIRMATION-DRAFT-STORAGE-FAILURE", startLegacyBlazorHosts: false);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);await using var context=await browser.NewContextAsync();
    await context.AddInitScriptAsync("Storage.prototype.setItem = function(){ throw new DOMException('Unavailable', 'QuotaExceededError'); }");
    var page=await context.NewPageAsync();var route="/ui/app/engagements/"+host.Fixture.EngagementId+"/confirmations";await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
    await page.GetByText("Prepare confirmation",new(){Exact=true}).ClickAsync();await page.GetByLabel("Respondent",new(){Exact=true}).FillAsync("Memory only bank");
    await page.GetByRole(AriaRole.Button,new(){Name="Save case draft in tab",Exact=true}).ClickAsync();await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Confirmation draft recovery",Exact=true})).ToContainTextAsync("memory only");
    await page.GetByRole(AriaRole.Navigation,new(){Name="Breadcrumb",Exact=true}).GetByRole(AriaRole.Link,new(){Name="Engagement",Exact=true}).ClickAsync();await page.GetByRole(AriaRole.Button,new(){Name="Save draft and continue",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByLabel("Respondent",new(){Exact=true})).ToHaveValueAsync("Memory only bank");
    await page.GetByRole(AriaRole.Navigation,new(){Name="Breadcrumb",Exact=true}).GetByRole(AriaRole.Link,new(){Name="Engagement",Exact=true}).ClickAsync();await page.GetByRole(AriaRole.Button,new(){Name="Discard edits and continue",Exact=true}).ClickAsync();await Assertions.Expect(page.Locator("audit-confirmations")).ToHaveCountAsync(0);
    await using var db=host.CreateDbContext();Assert.Empty(await db.AuditConfirmationCases.ToListAsync());
  }
  private static async Task Open(IPage page)=>await page.GetByRole(AriaRole.Button,new(){Name="Review confirmation for Synthetic bank",Exact=true}).ClickAsync();
  private static async Task Confirm(IPage page,string button){var response=await page.RunAndWaitForResponseAsync(async()=>{await page.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed this exact case and its current evidence revisions.",Exact=true}).CheckAsync();await page.GetByRole(AriaRole.Button,new(){Name=button,Exact=true}).ClickAsync();},r=>r.Url.Contains("/confirmations/",StringComparison.Ordinal)&&r.Url.EndsWith("/actions",StringComparison.Ordinal)&&r.Request.Method=="POST");if(response.Status==200)await Assertions.Expect(page.GetByLabel("Case action",new(){Exact=true})).ToHaveValueAsync("");else await Assertions.Expect(page.GetByLabel("Case action",new(){Exact=true})).ToHaveCountAsync(0);}
  private static async Task Act(IPage page,string action,string button,params (string Label,string Value)[] fields){await page.GetByLabel("Case action",new(){Exact=true}).SelectOptionAsync(action);foreach(var f in fields)await page.GetByLabel(f.Label,new(){Exact=true}).FillAsync(f.Value);await Confirm(page,button);}
}
