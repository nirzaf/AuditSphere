using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;
public sealed class AngularClientAccountRoleJourneyTests
{
  [Fact]
  public async Task ClientAccountRoleProposalRequiresAnotherReviewerAndBlocksGenericAr()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-CLIENT-ACCOUNT-ROLES-01");var f=host.Fixture;
    var maker=PbcSeed.Actor(f.Admin,"Administrator");var reviewer=PbcSeed.Actor(f.Reviewer,"AccountingReviewer");Guid accountId,periodId;string clientName;
    await using(var db=host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Reviewer,"AccountingReviewer"));
      db.AcceptanceDecisions.Add(new AcceptanceDecision{Id=Guid.CreateVersion7(),FirmId=f.FirmId,PracticeClientId=f.ClientId,ServiceRoute="BOOKKEEPING",Decision="Accepted",Generation=1,Rationale="Synthetic account-role service",EvaluationTemplateVersion="TEST-1",EvaluationSnapshotDigest=new string('f',64),DecidedByUserId=f.Reviewer.Id,DecidedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
      Assert.True((await ClientAccountingService.CreateProfileAsync(db,maker,new(f.ClientId,"QA","QAR",1,1,"AUDITSPHERE","NATIVE",ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
      var period=await ClientAccountingService.CreatePeriodAsync(db,maker,new(f.ClientId,"2026",new(2026,1,1),new(2026,12,31),"STATUTORY","QAR"));Assert.True(period.Succeeded);periodId=period.Value;
      var chart=await ClientAccountingService.CreateChartVersionAsync(db,maker,f.ClientId,"AUDITSPHERE",new(2026,1,1));Assert.True(chart.Succeeded);
      Assert.True((await ClientAccountingService.AddAccountsAsync(db,maker,chart.Value,[new("ar","1100","Receivables","ASSET","DEBIT",true),new("expense","6000","Expense","EXPENSE","DEBIT",true)])).Succeeded);
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db,reviewer,chart.Value)).Succeeded);
      accountId=await db.ClientAccounts.Where(x=>x.ChartVersionId==chart.Value&&x.AccountCode=="1100").Select(x=>x.Id).SingleAsync();
      clientName=await db.PracticeClients.Where(x=>x.Id==f.ClientId).Select(x=>x.LegalName).SingleAsync();
    }
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);var errors=new List<string>();
    async Task<IPage> Open(string origin)
    {
      var page=await browser.NewPageAsync();page.PageError+=(_,e)=>errors.Add(e);await page.GotoAsync(origin+"/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");await page.Locator("audit-accounting").GetByRole(AriaRole.Button,new(){Name=clientName,Exact=true}).ClickAsync();
      await page.Locator("audit-account-roles").GetByText("Client posting account roles",new(){Exact=true}).ClickAsync();await page.Locator("audit-account-roles").GetByRole(AriaRole.Button,new(){Name="Refresh client account roles",Exact=true}).ClickAsync();return page;
    }
    var origin=await host.StartApiForIdentityAsync(f.Admin,new Dictionary<string,string>{{"AngularUi__Enabled","true"}});var page=await Open(origin);var roles=page.Locator("audit-account-roles");
    await roles.GetByLabel("Client role account",new(){Exact=true}).SelectOptionAsync(accountId.ToString());await roles.GetByLabel("Posting account role",new(){Exact=true}).SelectOptionAsync("AR");
    await roles.GetByLabel("Role effective from",new(){Exact=true}).FillAsync("2026-01-01");await roles.GetByLabel("Account role proposal reason",new(){Exact=true}).FillAsync("Receivable control review");
    await roles.GetByRole(AriaRole.Checkbox,new(){Name="I checked the client account, role and dates.",Exact=true}).CheckAsync();await roles.GetByRole(AriaRole.Button,new(){Name="Propose client account role",Exact=true}).ClickAsync();
    await Assertions.Expect(roles.GetByText("Pending independent review",new(){Exact=true})).ToBeVisibleAsync();await Assertions.Expect(roles.GetByRole(AriaRole.Button,new(){Name="Review role AR",Exact=true})).ToHaveCountAsync(0);
    var reviewerOrigin=await host.StartApiForIdentityAsync(f.Reviewer,new Dictionary<string,string>{{"AngularUi__Enabled","true"}});var other=await Open(reviewerOrigin);var review=other.Locator("audit-account-roles");
    await review.GetByRole(AriaRole.Button,new(){Name="Review role AR",Exact=true}).ClickAsync();await review.GetByLabel("Account role review reason",new(){Exact=true}).FillAsync("Independent receivables check");
    await review.GetByRole(AriaRole.Checkbox,new(){Name="I independently checked this exact role proposal.",Exact=true}).CheckAsync();await review.GetByRole(AriaRole.Button,new(){Name="Approve client account role",Exact=true}).ClickAsync();
    await Assertions.Expect(review.GetByRole(AriaRole.Cell).Filter(new(){HasText="Independent receivables check"})).ToBeVisibleAsync();
    await roles.GetByRole(AriaRole.Button,new(){Name="Refresh client account roles",Exact=true}).ClickAsync();await Assertions.Expect(roles.GetByRole(AriaRole.Cell).Filter(new(){HasText="Independent receivables check"})).ToBeVisibleAsync();
    await using(var db=host.CreateDbContext())
    {
      Assert.Single(await db.ClientAccountRoleDecisions.Where(x=>x.ClientId==f.ClientId&&x.Decision=="APPROVE").ToListAsync());
      var refused=await ClientOperationalLedgerWorkspace.CreateDraftAsync(db,maker,new(f.ClientId,periodId,"GENERIC-AR","Unexplained receivable",new(2026,1,10),[new("1100","Receivable",10,0),new("6000","Credit",0,10)]));Assert.False(refused.Succeeded);
      Assert.Equal(0,await db.ClientOperationalJournals.CountAsync(x=>x.ClientId==f.ClientId));Assert.Equal(0,await db.FirmJournals.CountAsync());
    }
    Assert.Empty(errors);
  }
}
