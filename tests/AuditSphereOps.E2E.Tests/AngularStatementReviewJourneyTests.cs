using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularStatementReviewJourneyTests
{
  [Fact][Trait("CaseId", "ANGULAR-STATEMENT-REVIEW")]
  public async Task ApprovedStatementContributionsAndEvidenceReturnRetainContextWithoutStaleTotals()
  {
    await using var host = await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-STATEMENT-REVIEW");
    StatementReviewSeed.Context seed; await using(var db=host.CreateDbContext()) seed=await StatementReviewSeed.SeedAsync(db,host.Fixture,true);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    var errors=new List<string>();page.PageError+=(_,e)=>errors.Add(e);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString($"/ui/app/engagements/{host.Fixture.EngagementId}/statements?section=position&filter=cash"));
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Approved statement basis",Exact=true})).ToContainTextAsync(seed.DatasetId.ToString());
    await page.GetByRole(AriaRole.Button,new(){Name="Inspect line CASH ASSETS",Exact=true}).ClickAsync();
    var accounts=page.GetByRole(AriaRole.Region,new(){Name="Exact account contributions",Exact=true});
    await Assertions.Expect(accounts).ToContainTextAsync("100.123486");await Assertions.Expect(accounts).ToContainTextAsync("31 accounts");
    await page.GetByRole(AriaRole.Button,new(){Name="Next contributions",Exact=true}).ClickAsync();
    await Assertions.Expect(accounts).ToContainTextAsync("Page 2 of 2");await Assertions.Expect(accounts).ToContainTextAsync("1030");
    await page.GetByRole(AriaRole.Button,new(){Name="Inspect procedure CSH-01",Exact=true}).ClickAsync();
    var evidence=page.GetByRole(AriaRole.Complementary,new(){Name="Supporting procedure evidence",Exact=true});
    await Assertions.Expect(evidence).ToContainTextAsync("No linked received files");
    await using(var db=host.CreateDbContext()) await db.AuditProcedures.Where(x=>x.Id==seed.CashProcedureId).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Title,"Changed synthetic supporting context"));
    await page.GetByRole(AriaRole.Button,new(){Name="Return to statement line",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Statement basis changed",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(accounts).ToBeHiddenAsync();await Assertions.Expect(page.GetByLabel("Statement line filter",new(){Exact=true})).ToHaveValueAsync("cash");
    await page.GetByRole(AriaRole.Button,new(){Name="Use refreshed statement basis",Exact=true}).ClickAsync();
    await Assertions.Expect(accounts).ToContainTextAsync("Page 2 of 2");await Assertions.Expect(accounts).ToContainTextAsync("Changed synthetic supporting context");
    await page.GetByRole(AriaRole.Button,new(){Name="Inspect exact trial balance",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Exact source inspection",Exact=true})).ToContainTextAsync(seed.DatasetId.ToString());
    await page.GetByRole(AriaRole.Button,new(){Name="Close exact source",Exact=true}).ClickAsync();
    var downloadTask=page.WaitForDownloadAsync();await page.GetByRole(AriaRole.Button,new(){Name="Export complete statement contributions",Exact=true}).ClickAsync();var download=await downloadTask;
    Assert.Contains(seed.MappingId.ToString(),download.SuggestedFilename);
    await page.SetViewportSizeAsync(390,844);Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    foreach (var name in new[] { "Statement table scroll area", "Contribution table scroll area" })
    {
      var scroll = page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });
      Assert.Equal("0", await scroll.GetAttributeAsync("tabindex"));
      Assert.True(await scroll.EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth"));
      await scroll.PressAsync("End");
      await Assertions.Expect(scroll).ToBeFocusedAsync();
      await scroll.PressAsync("ArrowRight");
      await page.WaitForFunctionAsync("name => document.querySelector(`[aria-label=\"${name}\"]`).scrollLeft > 0", name,
        new() { Timeout = 5000 });
    }
    await page.ReloadAsync();await Assertions.Expect(accounts).ToContainTextAsync("Page 2 of 2");
    await using(var db=host.CreateDbContext()) await db.Users.Where(x=>x.Id==host.Fixture.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync(new(){Timeout=15000});
    Assert.DoesNotContain("100.123486",await page.Locator("body").InnerTextAsync());Assert.Empty(errors);
  }

  [Fact][Trait("CaseId", "ANGULAR-STATEMENT-STALE")]
  public async Task ChangedGenerationHidesOldContributionAndRefusesExport()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-STATEMENT-STALE");
    await using(var db=host.CreateDbContext()) await StatementReviewSeed.SeedAsync(db,host.Fixture);
    var origin=await host.StartApiForIdentityAsync(host.Fixture.Staff,new Dictionary<string,string>{["AngularUi__Enabled"]="true"});
    using var pw=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(pw);var page=await browser.NewPageAsync();
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString($"/ui/app/engagements/{host.Fixture.EngagementId}/statements?section=position"));
    await Assertions.Expect(page.GetByRole(AriaRole.Table,new(){Name="Statement lines",Exact=true})).ToContainTextAsync("100.123456");
    await using(var db=host.CreateDbContext()) await db.ClientSafetyStates.Where(x=>x.Id==host.Fixture.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.InputGeneration,s=>s.InputGeneration+1));
    await page.GetByRole(AriaRole.Button,new(){Name="Inspect line CASH ASSETS",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByText("Statement inputs changed after mapping approval. Review a new mapping before using current totals.",new(){Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Table,new(){Name="Statement lines",Exact=true})).ToBeHiddenAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Export complete statement contributions",Exact=true})).ToBeHiddenAsync();
    Assert.DoesNotContain("100.123456",await page.Locator("body").InnerTextAsync());
  }
}
