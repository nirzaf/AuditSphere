using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularTenantSetupEditJourneyTests
{
  [Fact]
  public async Task ReviewedSetupCanRecoverCommittedReceiptAfterReloadWithoutAnotherMutation()
  {
    await using var host=await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-TENANT-SETUP-EDIT");
    var f=host.Fixture; var tenant=Guid.NewGuid().ToString("D"); f.Admin.TenantId=tenant;
    var draftId=Guid.CreateVersion7(); var now=DateTimeOffset.UtcNow; var sessionId=Guid.CreateVersion7();
    await using(var db=host.CreateDbContext())
    {
      await db.Users.Where(x=>x.Id==f.Admin.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.TenantId,tenant));
      db.Microsoft365SetupSessions.Add(new(){Id=sessionId,FirmId=f.FirmId,InstallationId="synthetic-edit-journey",BootstrapProofHash=new string('a',64),CapabilityHash=new string('b',64),ClaimedByUserId=f.Admin.Id,ClaimedAt=now,ExpiresAt=now.AddHours(1)});
      db.Microsoft365SetupDrafts.Add(new(){Id=draftId,FirmId=f.FirmId,SetupSessionId=sessionId,ExpectedTenantId=tenant,TenantDisplayName="Original synthetic label",CreatedAt=now,UpdatedAt=now});
      await db.SaveChangesAsync();
    }
    var settings=new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]="true"};
    if(Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH") is {Length:>0} assets) settings["AngularUi__BuildPath"]=assets;
    var origin=await host.StartApiForIdentityAsync(f.Admin,settings);
    using var playwright=await Playwright.CreateAsync();await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    var page=await browser.NewPageAsync();var errors=new List<string>();page.PageError+=(_,error)=>errors.Add(error);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl=/app/administration/microsoft365/tenant-connection");
    var editor=page.GetByRole(AriaRole.Region,new(){Name="Edit local setup configuration",Exact=true});
    await editor.GetByRole(AriaRole.Textbox,new(){Name="Tenant label",Exact=true}).FillAsync("Reviewed synthetic label");
    await editor.GetByRole(AriaRole.Combobox,new(){Name="Mail setup",Exact=true}).SelectOptionAsync("CONFIGURED");
    await editor.GetByRole(AriaRole.Button,new(){Name="Review exact changes",Exact=true}).ClickAsync();
    await Assertions.Expect(editor.GetByRole(AriaRole.Table)).ToContainTextAsync("Original synthetic label");
    await Assertions.Expect(editor.GetByRole(AriaRole.Heading,new(){Name="Review setup revision 1",Exact=true})).ToBeFocusedAsync();
    await Assertions.Expect(editor.GetByRole(AriaRole.Button,new(){Name="Save reviewed setup",Exact=true})).ToBeDisabledAsync();
    await editor.GetByRole(AriaRole.Checkbox,new(){Name="I reviewed these exact local changes.",Exact=true}).CheckAsync();
    await editor.GetByRole(AriaRole.Button,new(){Name="Save reviewed setup",Exact=true}).ClickAsync();
    await Assertions.Expect(editor).ToContainTextAsync("Saved local metadata at revision 2");
    page.Dialog+=(_,dialog)=>dialog.AcceptAsync(); await page.ReloadAsync();
    await Assertions.Expect(editor.GetByRole(AriaRole.Button,new(){Name="Check saved receipt",Exact=true})).ToBeVisibleAsync();
    await editor.GetByRole(AriaRole.Button,new(){Name="Check saved receipt",Exact=true}).ClickAsync();
    await Assertions.Expect(editor).ToContainTextAsync("Saved local metadata at revision 2");
    await editor.GetByRole(AriaRole.Button,new(){Name="Acknowledge receipt and refresh",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,new(){Name="Saved setup configuration",Exact=true})).ToContainTextAsync("Mail setupconfigured");
    await using var check=host.CreateDbContext();Assert.Equal(2,(await check.Microsoft365SetupDrafts.SingleAsync(x=>x.Id==draftId)).Revision);
    Assert.Single(await check.Microsoft365AdministrationEvents.Where(x=>x.SetupDraftId==draftId).ToListAsync());Assert.Empty(errors);
  }
}
