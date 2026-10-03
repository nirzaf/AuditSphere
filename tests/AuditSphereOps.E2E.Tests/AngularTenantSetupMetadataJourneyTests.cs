using AuditSphereOps.Domain.Microsoft365;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularTenantSetupMetadataJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SavedSetupIsDisplayedSeparatelyFromConsentAndLiveVerification(bool canonical)
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker:false,caseId:"ANGULAR-TENANT-SETUP-METADATA");
    var f = host.Fixture;
    await using (var db = host.CreateDbContext())
    {
      var now = DateTimeOffset.UtcNow;
      var sessionId = Guid.CreateVersion7();
      db.Microsoft365SetupSessions.Add(new Microsoft365SetupSession { Id=sessionId, FirmId=f.FirmId,
        InstallationId="synthetic-setup-metadata",BootstrapProofHash=new string('a',64),CapabilityHash=new string('b',64),
        ClaimedByUserId=f.Admin.Id,ClaimedAt=now,ExpiresAt=now.AddHours(1) });
      db.Microsoft365SetupDrafts.Add(new Microsoft365SetupDraft { Id=Guid.CreateVersion7(),FirmId=f.FirmId,
        SetupSessionId=sessionId,ExpectedTenantId=Guid.NewGuid().ToString("D"),TenantDisplayName="Synthetic tenant setup label",
        MailState="CONFIGURED",RecordsState="NOT_CONFIGURED",CreatedAt=now,UpdatedAt=now });
      await db.SaveChangesAsync();
    }
    var origin=await host.StartApiForIdentityAsync(f.Admin,new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()});
    using var playwright=await Playwright.CreateAsync();
    await using var browser=await PlaywrightBrowser.LaunchAsync(playwright);
    var page=await browser.NewPageAsync(new(){ViewportSize=new(){Width=390,Height=844}});
    var errors=new List<string>();page.PageError+=(_,error)=>errors.Add(error);
    var prefix=canonical ? "" : "/ui";
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app/administration/microsoft365/tenant-connection"));
    await Assertions.Expect(page.GetByText("Synthetic tenant setup label",new(){Exact=true})).ToBeVisibleAsync();
    var saved=page.GetByRole(AriaRole.Region,new(){Name="Saved setup configuration",Exact=true});
    await Assertions.Expect(saved).ToContainTextAsync("Mail setupconfigured");
    await Assertions.Expect(saved).ToContainTextAsync("Records setupnot configured");
    await Assertions.Expect(saved).ToContainTextAsync("not verification results");
    await Assertions.Expect(page.GetByText("Friendly setup label only; Microsoft tenant ID remains the identity boundary.",new(){Exact=true})).ToBeVisibleAsync();
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await page.ReloadAsync();
    await Assertions.Expect(saved).ToBeVisibleAsync();
    Assert.Empty(errors);
  }
}
