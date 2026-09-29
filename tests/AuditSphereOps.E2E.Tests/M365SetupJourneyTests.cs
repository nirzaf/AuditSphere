using Microsoft.Playwright;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "Microsoft365Onboarding")]
public sealed class M365SetupJourneyTests
{
  internal const string BootstrapProof = "AuditSphere synthetic E2E bootstrap proof";

  [Fact]
  [Trait("CaseId", "PROP-E2E-05-ADMIN")]
  public async Task BoundAdministrator_OpensExistingDraftWithOneClick()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      enableSetup: true, caseId: "PROP-E2E-05-ADMIN");
    await using (var db = host.CreateDbContext())
    {
      var now = DateTimeOffset.UtcNow;
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, UserId = host.Fixture.Staff.Id,
        Role = "Administrator", GrantedAt = now, GrantedByUserId = host.Fixture.Staff.Id
      });
      var claim = await Microsoft365OnboardingService.ClaimAsync(db, host.Fixture.FirmId,
        "synthetic-installation", BootstrapProof,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BootstrapProof))).ToLowerInvariant(), now);
      Assert.True(claim.Succeeded);
      var session = await db.Microsoft365SetupSessions.SingleAsync(x => x.Id == claim.Value!.SessionId);
      session.ClaimedByUserId = host.Fixture.Staff.Id;
      session.ConsumedAt = now.AddMilliseconds(1);
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, "/app/administration/microsoft365"));
    await page.GetByRole(AriaRole.Button, new() { Name = "Continue setup with this account" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    Assert.Equal(0, await page.Locator("#bootstrap-proof").CountAsync());
    await page.GetByRole(AriaRole.Button, new() { Name = "Continue setup with this account" }).ClickAsync();
    await page.GetByText("Local setup draft opened with your signed-in tenant. Microsoft consent and selected-resource verification are still required.").WaitForAsync();
    await page.GetByText("Draft revision").WaitForAsync();
    Assert.Equal(host.Fixture.Staff.TenantId, await page.Locator("#tenant-id").InputValueAsync());
    await page.GetByRole(AriaRole.Heading, new() { Name = "Connection progress" }).WaitForAsync();
    var setupProgress = page.GetByRole(AriaRole.Progressbar, new() { Name = "Recorded setup checklist steps" });
    Assert.Equal("1", await setupProgress.GetAttributeAsync("value"));
    Assert.Equal("7", await setupProgress.GetAttributeAsync("max"));
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Connection progress" }))
      .ToContainTextAsync("Local checklist progress; live capability verification remains separate.");
    var setupCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_M365_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(setupCaptureDir)) Directory.CreateDirectory(setupCaptureDir);
    foreach (var width in new[] { 390, 1440, 320, 760, 1024, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForTimeoutAsync(250);
      await page.Locator("#tenant-id").WaitForAsync();
      await page.WaitForFunctionAsync("() => document.documentElement.scrollWidth <= window.innerWidth && (window.innerWidth > 760 || document.querySelector('#main-content').getBoundingClientRect().width >= window.innerWidth - 64)");
      Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"),
        $"Microsoft 365 setup overflows at {width}px.");
      if (setupCaptureDir is not null && width is 390 or 1440)
        await page.ScreenshotAsync(new() { Path = Path.Combine(setupCaptureDir, $"m365-setup-{width}.png"), FullPage = true });
    }
    await page.Keyboard.PressAsync("Tab");
    Assert.True(await page.Locator(":focus-visible").CountAsync() > 0);
    await page.SetViewportSizeAsync(390, 900);
    await page.GetByText("Advanced resource IDs, capabilities and folder templates").ClickAsync();
    await page.Locator("#site-id").WaitForAsync();
    Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"),
      "Advanced Microsoft 365 setup overflows at 390px.");
    if (setupCaptureDir is not null)
      await page.ScreenshotAsync(new() { Path = Path.Combine(setupCaptureDir, "m365-setup-advanced-390.png"), FullPage = true });
    await page.GetByText("Advanced resource IDs, capabilities and folder templates").ClickAsync();
    Assert.Contains("Tenant recorded from your Microsoft sign-in", await page.Locator("body").InnerTextAsync());
    Assert.Contains("Enter the approved SharePoint working site URL and save the draft", await page.Locator("body").InnerTextAsync());
    Assert.Contains("Activate only after live provider checks and human review", await page.Locator("body").InnerTextAsync());
    await page.Locator("#site-url").FillAsync("https://synthetic.sharepoint.com/sites/audit");
    await page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
    await page.GetByText("Draft saved. It is not verified or active.").WaitForAsync();
    Assert.Contains("Working site URL saved", await page.Locator("body").InnerTextAsync());
    Assert.Equal("2", await setupProgress.GetAttributeAsync("value"));
    Assert.Contains("Deployment operator verifies selected site, library and root access", await page.Locator("body").InnerTextAsync());
    Assert.Equal(0, await page.Locator("#bootstrap-proof").CountAsync());
    await using var readback = host.CreateDbContext();
    Assert.Equal(host.Fixture.Staff.TenantId,
      (await readback.Microsoft365SetupDrafts.AsNoTracking().SingleAsync()).ExpectedTenantId);
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-05")]
  public async Task SetupDraft_SavesAndResumesWithoutClaimingMicrosoftVerification()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, enableSetup: true);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();

    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, "/setup/microsoft365"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Microsoft 365 onboarding" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await page.Locator("#bootstrap-proof").FillAsync(BootstrapProof);
    await page.GetByRole(AriaRole.Button, new() { Name = "Claim setup" }).ClickAsync();
    await page.GetByText("Setup claimed. Microsoft verification and activation are separate steps.").WaitForAsync();
    await page.Locator("#tenant-id").FillAsync("synthetic-tenant-id");
    await page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
    await page.GetByText("Draft saved. It is not verified or active.").WaitForAsync();
    Assert.Contains("DRAFT", await page.Locator("body").InnerTextAsync());

    var reconnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Microsoft 365 onboarding" }).WaitForAsync();
    await reconnected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await page.Locator("#bootstrap-proof").FillAsync(BootstrapProof);
    await page.GetByRole(AriaRole.Button, new() { Name = "Claim setup" }).ClickAsync();
    await page.WaitForFunctionAsync(
      "() => document.querySelector('#tenant-id')?.value === 'synthetic-tenant-id'");

    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains("Setup claimed. Microsoft verification and activation are separate steps.", body);
    Assert.Contains("Draft revision", body);
    Assert.Equal("NOT_CONFIGURED", await page.Locator("#mail-state").InputValueAsync());
    Assert.Equal("NOT_CONFIGURED", await page.Locator("#records-state").InputValueAsync());
    Assert.Contains("This route only creates a resumable local draft. It does not authenticate to Microsoft, grant permissions, or create SharePoint content.", body);
    Assert.DoesNotContain("Configuration verified and active", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";

  private static Task WaitForCircuitConnectionAsync(IPage page, List<string> diagnostics)
  {
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    page.Console += (_, message) =>
    {
      diagnostics.Add($"console/{message.Type}: {message.Text}");
      if (message.Text.Contains("WebSocket connected to ws://", StringComparison.Ordinal))
        connected.TrySetResult();
    };
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    return connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
  }
}
