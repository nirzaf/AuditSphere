using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularEvidenceActionsJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-EVIDENCE-ACTIONS-KEYBOARD")]
  public async Task KeyboardReviewPreviewCancelAndFreshAssentPreserveFocusOwnership()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-EVIDENCE-ACTIONS-KEYBOARD");
    var seed=await AccountingAnalysisReviewSeed.SeedAsync(host.Database,false,false);var f=seed.Fixture;var id=seed.Evidence["ECL"];
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["Application__FirmId"]=f.FirmId.ToString("D")});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);
    await using var context=await browser.NewContextAsync();var page=await context.NewPageAsync();var errors=new List<string>();
    page.PageError+=(_,e)=>errors.Add(e);
    var route=$"/ui/app/accounting/evidence/ECL/{id}/actions";
    var back=page.GetByRole(AriaRole.Link,new(){Name="← Back to exact evidence",Exact=true});
    var previewReached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releasePreview=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var previewCalls=0;var actionPosts=0;
    page.Request+=(_,request)=>{if(request.Method=="POST"&&new Uri(request.Url).AbsolutePath.EndsWith("/actions",StringComparison.Ordinal))Interlocked.Increment(ref actionPosts);};
    await page.RouteAsync("**/api/ui/accounting/evidence/ECL/*/preview",async interception=>
    {
      if(interception.Request.Method=="POST"&&Interlocked.Increment(ref previewCalls)==2)
      {
        previewReached.TrySetResult();await releasePreview.Task;
      }
      await interception.ContinueAsync();
    });
    try
    {
      await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(route));
      await page.GetByRole(AriaRole.Heading,new(){Name="Prepare reviewed action",Exact=true}).WaitForAsync();
      var resultSelect=page.GetByRole(AriaRole.Combobox,new(){Name="Procedure result",Exact=true});
      var selectable=resultSelect.Locator("option:not([value='']):not([disabled])").First;
      await selectable.WaitForAsync(new(){State=WaitForSelectorState.Attached});
      await resultSelect.SelectOptionAsync((await selectable.GetAttributeAsync("value"))!);
      await page.GetByLabel("Reason / human conclusion",new(){Exact=true}).FillAsync("Synthetic keyboard review intent");
      await page.GetByLabel("Evidence / corroboration reference",new(){Exact=true}).FillAsync("SYNTHETIC-KEYBOARD-EVIDENCE");

      var previewButton=page.GetByRole(AriaRole.Button,new(){Name="Preview exact action",Exact=true});
      await Assertions.Expect(previewButton).ToBeEnabledAsync();
      await previewButton.FocusAsync();await previewButton.PressAsync("Enter");
      var reviewHeading=page.GetByRole(AriaRole.Heading,new(){Name="Review before dispatch",Exact=true});
      await Assertions.Expect(reviewHeading).ToBeVisibleAsync();
      Assert.True(await reviewHeading.EvaluateAsync<bool>("element => element === document.activeElement"),
        "Keyboard review should move focus to the newly rendered exact-action preview.");

      var assent=page.GetByLabel("I reviewed this exact source, result revision and human decision.",new(){Exact=true});
      var cancel=page.GetByRole(AriaRole.Button,new(){Name="Cancel review",Exact=true});
      await assent.FocusAsync();await assent.PressAsync("Space");
      await Assertions.Expect(assent).ToBeCheckedAsync();
      await cancel.FocusAsync();await cancel.PressAsync("Enter");
      await Assertions.Expect(reviewHeading).ToHaveCountAsync(0);
      await Assertions.Expect(previewButton).ToBeFocusedAsync();
      Assert.Equal(0,Volatile.Read(ref actionPosts));

      // A cancelled preview cannot carry its assent into a fresh review. Hold the second
      // response while focus moves elsewhere to ensure completion does not steal it back.
      var secondPreview=previewButton.PressAsync("Enter");
      await previewReached.Task.WaitAsync(TimeSpan.FromSeconds(20));
      await back.FocusAsync();
      releasePreview.TrySetResult();
      await secondPreview;
      await Assertions.Expect(reviewHeading).ToBeVisibleAsync();
      await Assertions.Expect(back).ToBeFocusedAsync();
      await Assertions.Expect(assent).Not.ToBeCheckedAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Record reviewed action",Exact=true})).ToBeDisabledAsync();

      await assent.FocusAsync();await assent.PressAsync("Space");
      await Assertions.Expect(assent).ToBeCheckedAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Record reviewed action",Exact=true})).ToBeEnabledAsync();
      await OwnedBrowserFixtureHandoff.WaitAsync(origin,route,"evidence-action-pre-dispatch");
      Assert.Equal(0,Volatile.Read(ref actionPosts));
      Assert.Empty(errors);
    }
    finally { releasePreview.TrySetResult(); }
  }

  [Fact]
  [Trait("CaseId","ANGULAR-EVIDENCE-ACTIONS")]
  public async Task ExactNativeLinkAndIndependentDecisionRetainEvidenceAndClearAfterRevocation()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-EVIDENCE-ACTIONS");
    var seed=await AccountingAnalysisReviewSeed.SeedAsync(host.Database,false,false);var f=seed.Fixture;var id=seed.Evidence["ECL"];
    var settings=new Dictionary<string,string>{["AngularUi__Enabled"]="true",["Application__FirmId"]=f.FirmId.ToString("D")};
    var staffOrigin=await host.StartApiForIdentityAsync(f.Staff,settings);var reviewerOrigin=await host.StartApiForIdentityAsync(f.Reviewer,settings);
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);
    await using var staffContext=await browser.NewContextAsync();await using var reviewerContext=await browser.NewContextAsync();
    var staff=await staffContext.NewPageAsync();var reviewer=await reviewerContext.NewPageAsync();var errors=new List<string>();
    staff.PageError+=(_,e)=>errors.Add(e);reviewer.PageError+=(_,e)=>errors.Add(e);
    var route=$"/ui/app/accounting/evidence/ECL/{id}/actions";
    await staff.GotoAsync(staffOrigin+"/auth/sign-in?returnUrl="+route);
    await staff.GetByRole(AriaRole.Heading,new(){Name="Prepare reviewed action",Exact=true}).WaitForAsync();
    var resultSelect=staff.GetByRole(AriaRole.Combobox,new(){Name="Procedure result",Exact=true});
    var selectable=resultSelect.Locator("option:not([value='']):not([disabled])").First;
    await selectable.WaitForAsync(new(){State=WaitForSelectorState.Attached});
    await resultSelect.SelectOptionAsync((await selectable.GetAttributeAsync("value"))!);
    await Prepare(staff,"Synthetic exact source and procedure link","SYNTHETIC-LINK-EVIDENCE");
    await Assertions.Expect(staff.Locator("body")).ToContainTextAsync("7.006173");
    await staff.GetByRole(AriaRole.Button,new(){Name="Record reviewed action",Exact=true}).ClickAsync();
    await staff.GetByRole(AriaRole.Heading,new(){Name="Retained action receipt",Exact=true}).WaitForAsync();
    await Assertions.Expect(staff.Locator("body")).ToContainTextAsync("1 retained native actions");
    await reviewer.GotoAsync(reviewerOrigin+"/auth/sign-in?returnUrl="+route);
    await reviewer.GetByRole(AriaRole.Heading,new(){Name="Prepare reviewed action",Exact=true}).WaitForAsync();
    await reviewer.GetByRole(AriaRole.Combobox,new(){Name="Action",Exact=true}).SelectOptionAsync("REVIEW");
    await reviewer.GetByRole(AriaRole.Combobox,new(){Name="Decision",Exact=true}).SelectOptionAsync("APPROVED");
    await Prepare(reviewer,"Synthetic independent auditor conclusion","SYNTHETIC-REVIEW-CORROBORATION");
    await reviewer.GetByRole(AriaRole.Button,new(){Name="Record reviewed action",Exact=true}).ClickAsync();
    await reviewer.GetByRole(AriaRole.Heading,new(){Name="Retained action receipt",Exact=true}).WaitForAsync();
    await Assertions.Expect(reviewer.Locator("body")).ToContainTextAsync("2 retained native actions");
    await Assertions.Expect(reviewer.Locator("body")).ToContainTextAsync("A human review is already retained");
    Assert.Equal(0,await reviewer.Locator("audit-evidence-actions form").CountAsync());
    await reviewer.SetViewportSizeAsync(390,844);Assert.True(await reviewer.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    await using(var db=host.CreateDbContext())
    {
      Assert.Equal(2,await db.AccountingEvidenceActions.CountAsync(x=>x.EvidenceId==id));
      Assert.Equal("APPROVED",await db.EclAssessments.Where(x=>x.Id==id).Select(x=>x.Status).SingleAsync());
      var reconciliation=await db.EclAssessments.Where(a=>a.Id==id).Select(a=>a.ReconciliationId).SingleAsync();
      Assert.Equal(100.123456m,await db.AccountingReconciliations.Where(x=>x.Id==reconciliation).Select(x=>x.SourceTotal).SingleAsync());
      await db.Users.Where(x=>x.Id==f.Reviewer.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    }
    await Assertions.Expect(reviewer.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain("7.006173",await reviewer.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }
  private static async Task Prepare(IPage page,string reason,string evidence)
  {
    await page.GetByLabel("Reason / human conclusion",new(){Exact=true}).FillAsync(reason);
    await page.GetByLabel("Evidence / corroboration reference",new(){Exact=true}).FillAsync(evidence);
    await page.GetByRole(AriaRole.Button,new(){Name="Preview exact action",Exact=true}).ClickAsync();
    await page.GetByRole(AriaRole.Heading,new(){Name="Review before dispatch",Exact=true}).WaitForAsync();
    await page.GetByLabel("I reviewed this exact source, result revision and human decision.",new(){Exact=true}).CheckAsync();
  }
}
