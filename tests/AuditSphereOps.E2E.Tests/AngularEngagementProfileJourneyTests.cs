using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularEngagementProfileJourneyTests
{
  [Fact]
  [Trait("CaseId","ANGULAR-ENGAGEMENT-PREPARATION-AUTHORITY")]
  public async Task PreparationLinksFollowTheExactRoleScopeAndDirectRoutesStayProtected()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-ENGAGEMENT-PREPARATION-AUTHORITY");
    var f=host.Fixture;
    Guid progressTask;
    await using(var setup=host.CreateDbContext()) {
      var made=await AuditSphereOps.Application.Practice.EngagementWorkflowTaskService.CreateAsync(setup,
        PbcSeed.Actor(f.Admin,"Administrator"), f.EngagementId,
        new("Synthetic progress refresh commitment",AuditSphereOps.Domain.Practice.WorkflowTaskPhases.AuditPlanning,1,
          AssigneeUserId:f.Staff.Id,IdempotencyKey:Guid.NewGuid()));
      Assert.True(made.Succeeded,made.Message);progressTask=made.Value;
    }
    var origin=await host.StartApiForIdentityAsync(f.Staff,new Dictionary<string,string> {
      ["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]="true",
      ["AngularUi__BuildPath"]="/Users/qts/Repos/AuditSphere/.local/autonomous/evidence/practice-completion-20261026/ui-build/browser" });
    using var playwright=await Playwright.CreateAsync(); await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context=await browser.NewContextAsync(); var page=await context.NewPageAsync();
    var errors=new List<string>(); page.PageError+=(_,e)=>errors.Add(e);
    var path="/app/engagements/"+f.EngagementId;
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Engagement details",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("audit-engagement-progress .tracker-figure strong")).ToHaveTextAsync("0%");
    var transition=Task.Run(async()=> {
      await Task.Delay(TimeSpan.FromSeconds(15));
      await using var changed=host.CreateDbContext();
      var result=await AuditSphereOps.Application.Practice.EngagementWorkflowTaskService.CompleteAsync(changed,
        PbcSeed.Actor(f.Staff,"Staff"),progressTask,1);
      Assert.True(result.Succeeded,result.Message);
    });
    await OwnedBrowserFixtureHandoff.WaitAsync(origin,path,"progress-refresh");
    await transition;
    await Assertions.Expect(page.Locator("audit-engagement-progress .tracker-figure strong")).ToHaveTextAsync("99%",new(){Timeout=45000});
    foreach(var name in new[]{"Prepare analytical review","Prepare reconciliation","Prepare specialist schedule"})
      await Assertions.Expect(page.GetByRole(AriaRole.Link,new(){Name=name,Exact=true})).ToHaveCountAsync(0);
    await page.GotoAsync(origin+path+"/analysis/new");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Prepare analytical review",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync(
      "Ask your firm administrator to confirm your AuditSphere role and client or engagement scope.");
    Assert.Empty(errors);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId","ANGULAR-ENGAGEMENT-PROFILE-E2E")]
  public async Task MetadataPagedHoldsPreservedPlanningFieldsAndRevocation(bool canonical)
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-ENGAGEMENT-PROFILE-E2E");
    var f=host.Fixture; await using(var db=host.CreateDbContext()) {
      await EngagementProfileWorkspaceSeed.PopulateAsync(db,f);
      // Editing is reviewed budget-preparation authority, not Administrator authority.
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Admin,"Manager",f.ClientId,f.EngagementId));
      await db.SaveChangesAsync();
    }
    var origin=await host.StartApiForIdentityAsync(f.Admin,new Dictionary<string,string> {
      ["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString() });
    var prefix=canonical?"":"/ui";
    using var playwright=await Playwright.CreateAsync(); await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context=await browser.NewContextAsync(); var page=await context.NewPageAsync();
    var errors=new List<string>(); page.PageError+=(_,e)=>errors.Add(e);
    var path=prefix+"/app/engagements/"+f.EngagementId;
    var planningPath=$"/api/ui/engagements/{f.EngagementId:D}/planning";
    var (planningStatus, planningResponseBody) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method=="GET" && new Uri(request.Url).AbsolutePath.Equals(planningPath,StringComparison.OrdinalIgnoreCase),
      async () =>
      {
        await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(path));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Engagement details",Exact=true})).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Engagement planning",Exact=true}))
          .ToBeVisibleAsync();
      });
    Assert.True(planningStatus is >= 200 and < 300,
      $"Engagement planning returned HTTP {planningStatus}: {planningResponseBody}");
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

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AdministratorStaffingAndApprovalDoNotConferBudgetPreparation(bool canonical)
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-PLANNING-AUTHORITY-E2E");
    var f=host.Fixture;
    await using(var db=host.CreateDbContext()) {
      await BudgetPreparationReviewSeed.PopulateAsync(db,f);
      var draft=await AuditSphereOps.Application.Practice.PracticeTimeService.ReviseBudgetAsync(db,
        PbcSeed.Actor(f.Staff,"Manager"),new(f.EngagementId,"QAR",[new("Senior","AUDIT",60,"PLANNING")],0));
      Assert.True(draft.Succeeded,draft.Message);
    }
    var origin=await host.StartApiForIdentityAsync(f.Admin,new Dictionary<string,string> {
      ["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString() });
    using var playwright=await Playwright.CreateAsync(); await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    var page=await browser.NewPageAsync(); var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    var path=(canonical?"":"/ui")+"/app/engagements/"+f.EngagementId;
    var planningPath=$"/api/ui/engagements/{f.EngagementId:D}/planning";
    var (planningStatus,planningResponseBody)=await PlaywrightResponseCapture.RunAsync(page,
      request=>request.Method=="GET" && new Uri(request.Url).AbsolutePath.Equals(planningPath,StringComparison.OrdinalIgnoreCase),
      async()=>
      {
        await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(path));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Engagement details",Exact=true})).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Engagement planning",Exact=true}))
          .ToBeVisibleAsync();
      });
    Assert.True(planningStatus is >= 200 and < 300,
      $"Engagement planning returned HTTP {planningStatus}: {planningResponseBody}");
    var planning=page.GetByRole(AriaRole.Region,new(){Name="Engagement planning",Exact=true});
    await Assertions.Expect(planning.GetByRole(AriaRole.Heading,new(){Name="Assign staff",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(planning.GetByRole(AriaRole.Button,new(){Name="Review budget approval",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(planning.GetByRole(AriaRole.Region,new(){Name="Budget preparation",Exact=true})).ToHaveCountAsync(0);
    await page.SetViewportSizeAsync(390,844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    Assert.Empty(errors);
  }
  [Fact]
  [Trait("CaseId", "ANGULAR-COMMUNICATION-HISTORY-BOUNDS")]
  public async Task CommunicationHistoryKeepsFullCountAndKeyboardAccessToBoundedRows()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-COMMUNICATION-HISTORY-BOUNDS");
    var f = host.Fixture;
    var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
    await using (var db = host.CreateDbContext())
    {
      // Explicit recorded-email fixture; this is not a provider send or a delivery assertion.
      db.PbcCommunications.Add(new AuditSphereOps.Domain.Documents.PbcCommunication
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PbcRequestId = host.RequestId, AuthorUserId = f.Staff.Id,
        Kind = AuditSphereOps.Domain.Documents.PbcCommunicationKinds.Email,
        Subject = "Synthetic recorded PBC email", RecipientEmail = "synthetic@example.test",
        PortalUrl = "https://example.test/synthetic-pbc",
        Body = "Synthetic body excluded from the timeline", CreatedAt = DateTimeOffset.UtcNow,
        DeliveryState = AuditSphereOps.Domain.Documents.PbcDeliveryStates.Queued
      });
      db.CorrespondenceDispatchRecords.AddRange(Enumerable.Range(0, 201).Select(index =>
        new AuditSphereOps.Domain.Practice.CorrespondenceDispatchRecord
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, EngagementId = f.EngagementId,
          Purpose = AuditSphereOps.Domain.Practice.CorrespondencePurposes.Commercial,
          RecipientContactId = Guid.NewGuid(), RecipientName = "Synthetic liaison",
          RecipientEmail = "synthetic@example.test", DocumentType = "ENGAGEMENT_LETTER",
          DocumentReference = $"SYNTHETIC-COMMUNICATION-{index:D3}", DocumentRevision = 1,
          DocumentSha256 = new string('a', 64), DispatchedByUserId = f.Admin.Id,
          DispatchedAt = createdAt.AddSeconds(index)
        }));
      await db.SaveChangesAsync();
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var path = $"/app/engagements/{f.EngagementId:D}";
    var communications = page.GetByRole(AriaRole.Region, new() { Name = "Engagement communications", Exact = true });
    var (status, responseBody) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && new Uri(request.Url).AbsolutePath == $"/api/ui/engagements/{f.EngagementId:D}/communications",
      async () =>
      {
        await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
        await page.GetByText("Communications", new() { Exact = true }).ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(communications).ToContainTextAsync("Showing the newest 200 of 202 recorded communications.");
      });
    Assert.Equal(200, status);
    Assert.DoesNotContain("Synthetic body excluded from the timeline", responseBody, StringComparison.Ordinal);
    await Assertions.Expect(communications.Locator("tbody tr")).ToHaveCountAsync(200);
    await Assertions.Expect(communications).ToContainTextAsync("SYNTHETIC-COMMUNICATION-200");
    await Assertions.Expect(communications).Not.ToContainTextAsync("SYNTHETIC-COMMUNICATION-000");
    await Assertions.Expect(communications).Not.ToContainTextAsync("Synthetic body excluded from the timeline");
    await Assertions.Expect(communications.Locator("thead th[scope='col']")).ToHaveCountAsync(6);
    await Assertions.Expect(communications.Locator("tbody th[scope='row']")).ToHaveCountAsync(200);
    await page.SetViewportSizeAsync(390, 844);
    var scroll = communications.GetByRole(AriaRole.Region,
      new() { Name = "Recorded communications table; scroll horizontally if needed", Exact = true });
    await scroll.FocusAsync();
    await Assertions.Expect(scroll).ToBeFocusedAsync();
    for (var attempt = 0; attempt < 8 && !await scroll.EvaluateAsync<bool>("element => element.scrollLeft > 0"); attempt++)
      await scroll.PressAsync("ArrowRight");
    Assert.True(await scroll.EvaluateAsync<bool>("element => element.scrollLeft > 0"));
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await using (var verify = host.CreateDbContext())
      Assert.Equal(201, await verify.CorrespondenceDispatchRecords.CountAsync(x => x.EngagementId == f.EngagementId));
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "communication-history");
  }

}
