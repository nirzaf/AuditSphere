using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularTenantSetupMetadataJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SavedSetupIsDisplayedSeparatelyFromConsentAndLiveVerification(bool canonical)
  {
    await using var host = await OwnedHost.StartAsync(startWorker:false,caseId:"ANGULAR-TENANT-SETUP-METADATA");
    var f = host.Fixture;
    var expectedTenantId = Guid.NewGuid().ToString("D");
    f.Admin.TenantId = expectedTenantId;
    var draftId = Guid.CreateVersion7();
    await using (var db = host.CreateDbContext())
    {
      await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.TenantId, expectedTenantId));
      var now = DateTimeOffset.UtcNow;
      var sessionId = Guid.CreateVersion7();
      db.Microsoft365SetupSessions.Add(new Microsoft365SetupSession { Id=sessionId, FirmId=f.FirmId,
        InstallationId="synthetic-setup-metadata",BootstrapProofHash=new string('a',64),CapabilityHash=new string('b',64),
        ClaimedByUserId=f.Admin.Id,ClaimedAt=now,ExpiresAt=now.AddHours(1) });
      db.Microsoft365SetupDrafts.Add(new Microsoft365SetupDraft { Id=draftId,FirmId=f.FirmId,
        SetupSessionId=sessionId,ExpectedTenantId=expectedTenantId,TenantDisplayName="Synthetic tenant setup label",
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
    Assert.Equal(0, await page.GetByLabel("Installation proof", new() { Exact = true }).CountAsync());
    await Assertions.Expect(page.GetByText(expectedTenantId, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Synthetic tenant setup label",new(){Exact=true})).ToBeVisibleAsync();
    var saved=page.GetByRole(AriaRole.Region,new(){Name="Saved setup configuration",Exact=true});
    await Assertions.Expect(saved).ToContainTextAsync("Mail setupconfigured");
    await Assertions.Expect(saved).ToContainTextAsync("Records setupnot configured");
    await Assertions.Expect(saved).ToContainTextAsync("not verification results");
    await Assertions.Expect(page.GetByText("Friendly setup label only; Microsoft tenant ID remains the identity boundary.",new(){Exact=true})).ToBeVisibleAsync();
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 844);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
        $"Saved tenant setup overflows the {width}px viewport.");
    }
    await page.SetViewportSizeAsync(390, 844);
    await page.Keyboard.PressAsync("Tab");
    Assert.True(await page.Locator(":focus-visible").CountAsync() > 0);
    await page.ReloadAsync();
    await Assertions.Expect(saved).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(expectedTenantId, new() { Exact = true })).ToBeVisibleAsync();
    await using (var check = host.CreateDbContext())
    {
      var persisted = await check.Microsoft365SetupDrafts.SingleAsync(x => x.Id == draftId);
      Assert.Equal(expectedTenantId, persisted.ExpectedTenantId);
      Assert.Equal(1, persisted.Revision);
    }
    Assert.Empty(errors);
  }
}
