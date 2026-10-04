using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// Browser journeys for Microsoft 365 tenant administration against the in-process simulated tenant
/// (Test environment only). Live Microsoft acceptance is a separate, externally controlled test.
/// </summary>
[Trait("Category", "Microsoft365Onboarding")]
public sealed class TenantAdministrationJourneyTests
{
  internal sealed record Seeded(OwnedHost Host, AppUser Admin, string TenantId, string MemberObjectId, string GroupObjectId);

  internal static async Task<Seeded> SeedAsync(OwnedHost host, bool verified)
  {
    var tenantId = Guid.NewGuid().ToString("D");
    var now = DateTimeOffset.UtcNow;
    var admin = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, TenantId = tenantId, Subject = Guid.NewGuid().ToString("D"),
      Email = "tenant.admin@example.test", DisplayName = "Tenant Journey Admin", UserKind = "Staff", CreatedAt = now
    };
    var second = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, TenantId = tenantId, Subject = Guid.NewGuid().ToString("D"),
      Email = "second.admin@example.test", DisplayName = "Second Journey Admin", UserKind = "Staff", CreatedAt = now
    };
    await using var db = host.CreateDbContext();
    db.Users.AddRange(admin, second);
    foreach (var user in new[] { admin, second })
      db.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, UserId = user.Id,
        Role = "Administrator", GrantedAt = now, GrantedByUserId = user.Id });
    var sessionId = Guid.CreateVersion7();
    var connectionId = Guid.CreateVersion7();
    var draftId = Guid.CreateVersion7();
    db.Microsoft365SetupSessions.Add(new Microsoft365SetupSession
    {
      Id = sessionId, FirmId = host.Fixture.FirmId, InstallationId = "journey", BootstrapProofHash = new string('a', 64),
      CapabilityHash = new string('b', 64), ClaimedByUserId = admin.Id, ClaimedAt = now, ExpiresAt = now.AddHours(1)
    });
    db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
    {
      Id = connectionId, FirmId = host.Fixture.FirmId, TenantId = tenantId, LoginClientIdReference = "slot:login",
      RuntimeCredentialReference = "slot:reader", State = Microsoft365RevisionStates.ConsentRequired, ConsentState = "REQUIRED", CreatedAt = now
    });
    db.Microsoft365SetupDrafts.Add(new Microsoft365SetupDraft
    {
      Id = draftId, FirmId = host.Fixture.FirmId, SetupSessionId = sessionId, ConnectionRevisionId = connectionId,
      State = Microsoft365RevisionStates.ConsentRequired, ExpectedTenantId = tenantId, CreatedAt = now, UpdatedAt = now
    });
    if (verified)
    {
      // Equivalent to a completed consent flow; the connect journey below exercises the real flow.
      var attemptId = Guid.CreateVersion7();
      db.TenantConsentAttempts.Add(new TenantConsentAttempt
      {
        Id = attemptId, FirmId = host.Fixture.FirmId, SetupDraftId = draftId, InitiatedByUserId = admin.Id,
        InitiatingSessionEpoch = admin.SessionEpoch, InitiatorObjectId = admin.Subject, ExpectedTenantId = tenantId,
        ApplicationClientId = Guid.NewGuid().ToString("D"), StateHash = new string('c', 64),
        State = TenantConsentAttemptStates.ConsentVerified, CreatedAt = now, ExpiresAt = now.AddMinutes(10),
        ConsentingTenantId = tenantId, ConsentingObjectId = admin.Subject, ConsentVerifiedAt = now
      });
      foreach (var (capability, permission) in new[] { (Microsoft365Capabilities.DirectoryRead, "User.Read.All"),
                 (Microsoft365Capabilities.TenantUserProvisioning, "User.Create"), (Microsoft365Capabilities.GuestInvitation, "User.Invite.All"),
                 (Microsoft365Capabilities.GroupMembership, "GroupMember.ReadWrite.All") })
        db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification
        {
          Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, ConnectionRevisionId = connectionId, ConsentAttemptId = attemptId,
          TenantId = tenantId, Capability = capability, Permission = permission, State = CapabilityVerificationStates.Verified,
          DiagnosticCode = "simulated-verified", ObservedByUserId = admin.Id, ObservedAt = now
        });
    }
    await db.SaveChangesAsync();
    return new(host, admin, tenantId, Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
  }

  internal static Dictionary<string, string> Simulation(Seeded seeded, bool provisioning = true) => new()
  {
    ["TenantAdministration__Simulation__Enabled"] = "true",
    ["TenantAdministration__Provisioning__Enabled"] = provisioning ? "true" : "false",
    ["TenantAdministration__GuestInvitation__Enabled"] = "true",
    ["TenantAdministration__GroupMembership__Enabled"] = "true",
    ["TenantAdministration__GuestRedirectUrl"] = "http://127.0.0.1/auth/landing",
    ["TenantAdministration__Simulation__Users__0__ObjectId"] = seeded.Admin.Subject,
    ["TenantAdministration__Simulation__Users__0__DisplayName"] = seeded.Admin.DisplayName,
    ["TenantAdministration__Simulation__Users__0__UserPrincipalName"] = seeded.Admin.Email,
    ["TenantAdministration__Simulation__Users__1__ObjectId"] = seeded.MemberObjectId,
    ["TenantAdministration__Simulation__Users__1__DisplayName"] = "Directory Journey Member",
    ["TenantAdministration__Simulation__Users__1__UserPrincipalName"] = "journey.member@example.test",
    ["TenantAdministration__Simulation__Groups__0__ObjectId"] = seeded.GroupObjectId,
    ["TenantAdministration__Simulation__Groups__0__DisplayName"] = "AuditSphere Journey Team",
  };

  [Fact]
  [Trait("CaseId", "M365-ADMIN-E2E-01")]
  public async Task ConnectTenant_VerifiesConsentIdentityAndShowsDerivedDashboardStatus()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "M365-ADMIN-E2E-01");
    var seeded = await SeedAsync(host, verified: false);
    var origin = await host.StartWebForIdentityAsync(seeded.Admin, Simulation(seeded));
    await using var session = await BrowserSession.OpenAsync(origin, "/app/administration");
    var page = session.Page;
    await page.GetByRole(AriaRole.Heading, new() { Name = "Setup progress" }).WaitForAsync();
    async Task CaptureAdministrationAsync(string state)
    {
      if (Environment.GetEnvironmentVariable("AUDITSPHERE_ADMIN_UI_CAPTURE_DIR") is not { Length: > 0 } captureDir) return;
      Directory.CreateDirectory(captureDir);
      foreach (var width in new[] { 390, 1440 })
      {
        await page.SetViewportSizeAsync(width, 900);
        await page.GetByRole(AriaRole.Heading, new() { Name = "Setup progress" }).WaitForAsync();
        if (width < 960)
          await page.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')");
        else
          await page.WaitForFunctionAsync("() => document.querySelector('.audit-sidebar')?.getBoundingClientRect().left >= -1");
        await page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"administration-{state}-{width}.png"), FullPage = true });
      }
      await page.SetViewportSizeAsync(1280, 900);
    }
    await CaptureAdministrationAsync("before-consent");
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.GetByRole(AriaRole.Heading, new() { Name = "Setup progress" }).WaitForAsync();
      if (width < 960)
        await page.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')");
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Administration overflows the {width}px viewport.");
    }
    await page.SetViewportSizeAsync(1440, 900);
    Assert.True(await page.Locator(".audit-admin-users-table .mud-table-container").EvaluateAsync<bool>(
      "element => element.scrollWidth > element.clientWidth"), "The wide Users register should scroll within its panel.");
    var refreshButton = page.GetByRole(AriaRole.Button, new() { Name = "Refresh administration" });
    await refreshButton.FocusAsync();
    Assert.True(await refreshButton.EvaluateAsync<bool>("element => document.activeElement === element"));
    var before = await page.Locator("body").InnerTextAsync();
    Assert.Contains("Tenant administrator consent has not been verified", before);

    await page.GotoAsync(origin + "/app/administration/microsoft365/tenant-connection");
    async Task CaptureTenantPageAsync(string state)
    {
      if (Environment.GetEnvironmentVariable("AUDITSPHERE_TENANT_UI_CAPTURE_DIR") is not { Length: > 0 } captureDir) return;
      Directory.CreateDirectory(captureDir);
      foreach (var width in new[] { 390, 1440 })
      {
        await page.SetViewportSizeAsync(width, 900);
        await page.GetByRole(AriaRole.Heading, new() { Name = "Microsoft 365 tenant connection" }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Tenant connection", Exact = true }).WaitForAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"tenant-{state}-{width}.png"), FullPage = true });
      }
      await page.SetViewportSizeAsync(1280, 900);
    }
    await CaptureTenantPageAsync("before-consent");
    var permissionProgress = page.GetByRole(AriaRole.Progressbar, new() { Name = "Current enabled Microsoft permission checks verified" });
    await permissionProgress.WaitForAsync();
    var verifiedBeforeConsent = int.Parse((await permissionProgress.GetAttributeAsync("value"))!);
    await page.GetByRole(AriaRole.Button, new() { Name = "Connect Microsoft 365 tenant" }).ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("result=verified"), new() { Timeout = 30000 });
    await page.GetByText("Tenant administrator consent was verified").WaitForAsync();
    await page.GetByText(seeded.Admin.Subject, new() { Exact = false }).WaitForAsync();
    await page.GetByText("User.Create", new() { Exact = true }).First.WaitForAsync();
    await CaptureTenantPageAsync("after-consent");
    var verifiedAfterConsent = int.Parse((await permissionProgress.GetAttributeAsync("value"))!);
    var enabledChecks = int.Parse((await permissionProgress.GetAttributeAsync("max"))!);
    Assert.True(verifiedAfterConsent > verifiedBeforeConsent && verifiedAfterConsent <= enabledChecks);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.GetByRole(AriaRole.Heading, new() { Name = "Tenant connection", Exact = true }).WaitForAsync();
      if (width < 960)
        await page.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')");
      var overflow = await page.EvaluateAsync<string>("""() => JSON.stringify({ width: innerWidth, scroll: document.documentElement.scrollWidth, elements: [...document.querySelectorAll('*')].filter(e => e.getBoundingClientRect().right > innerWidth + 1).slice(0, 8).map(e => ({ tag: e.tagName, cls: String(e.className).slice(0, 90), right: Math.round(e.getBoundingClientRect().right) })) })""");
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Tenant connection overflows the {width}px viewport: {overflow}");
    }
    var verifyButton = page.GetByRole(AriaRole.Button, new() { Name = "Verify all capabilities" });
    await verifyButton.FocusAsync();
    Assert.True(await verifyButton.EvaluateAsync<bool>("element => document.activeElement === element"));

    await using (var db = host.CreateDbContext())
    {
      var attempt = await db.TenantConsentAttempts.SingleAsync();
      Assert.Equal(TenantConsentAttemptStates.ConsentVerified, attempt.State);
      Assert.Equal(seeded.Admin.Subject, attempt.ConsentingObjectId);
      Assert.Equal(4, await db.TenantCapabilityVerifications.CountAsync(x => x.State == CapabilityVerificationStates.Verified &&
        x.Capability != Microsoft365Capabilities.SelectedSite));
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Verify all capabilities" }).ClickAsync();
    await page.GetByText("Every enabled capability was verified separately for this tenant.").WaitForAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Filter by user principal name domain (optional)" }).FillAsync("example.test");
    await page.GetByRole(AriaRole.Button, new() { Name = "Search directory" }).ClickAsync();
    await page.GetByText("Directory Journey Member").WaitForAsync();
    await page.SetViewportSizeAsync(320, 900);
    await page.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')");
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "Tenant directory results overflow the 320px viewport.");
    await page.GotoAsync(origin + "/app/administration");
    await page.GetByText("Consent verified").First.WaitForAsync();
    await CaptureAdministrationAsync("after-consent");
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Tenant administrator consent has not been verified", body);
    Assert.Contains("Consent verified", body);
    Assert.Contains("Microsoft Tenant", body);
    await page.SetViewportSizeAsync(390, 900);
    await page.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')");
    foreach (var (tabName, heading) in new[] {
      ("AuditSphere Roles", "User and role administration"),
      ("Microsoft Directory", "Microsoft directory"),
      ("Invitations", "Invitations"),
      ("Groups", "Microsoft groups"),
      ("Access History", "Access history"),
      ("Disabled / Revoked", "Disabled and revoked access") })
    {
      await page.GetByRole(AriaRole.Tab, new() { Name = tabName }).ClickAsync();
      await page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).WaitForAsync();
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Administration {tabName} overflows the 390px viewport.");
    }
    await page.GetByRole(AriaRole.Tab, new() { Name = "Microsoft 365" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Permission matrix" }).WaitForAsync();
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "Administration Microsoft 365 overflows the 390px viewport.");
    foreach (var (tabName, heading) in new[] { ("Firm & Security", "Firm safety state"), ("Audit History", "Administration audit history") })
    {
      await page.GetByRole(AriaRole.Tab, new() { Name = tabName }).ClickAsync();
      await page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).WaitForAsync();
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Administration {tabName} overflows the 390px viewport.");
    }
    session.AssertNoPageErrors();
  }

  [Fact]
  [Trait("CaseId", "M365-ADMIN-E2E-02")]
  public async Task AssignExistingDirectoryUser_WithReviewedScope_ThenRevokeAccess()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "M365-ADMIN-E2E-02");
    var seeded = await SeedAsync(host, verified: true);
    var origin = await host.StartWebForIdentityAsync(seeded.Admin, Simulation(seeded));
    await using var session = await BrowserSession.OpenAsync(origin, "/app/administration");
    var page = session.Page;
    await page.GetByRole(AriaRole.Tab, new() { Name = "Microsoft Directory" }).ClickAsync();
    await page.Locator("#directory-search").FillAsync("Directory Journey");
    await page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Verify and select" }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Assign role and scope" }).ClickAsync();

    var dialog = page.GetByRole(AriaRole.Dialog);
    await SelectAsync(page, dialog.Locator("#assign-role"), "Staff");
    await SelectAsync(page, dialog.Locator("#assign-scope"), "Client");
    await SelectAsync(page, dialog.Locator("#assign-client"), (await ClientNameAsync(host))!);
    await dialog.Locator("#assign-reason").FillAsync("Joined the journey client team");
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Preview change" }).ClickAsync();
    await dialog.GetByText("Scope expansion: Yes").WaitForAsync();
    var save = dialog.GetByRole(AriaRole.Button, new() { Name = "Save access" });
    Assert.True(await save.IsDisabledAsync());
    await dialog.GetByLabel("I confirm this access expansion and its independence impact").CheckAsync();
    await save.ClickAsync();
    await page.GetByText("Access saved.").WaitForAsync();

    Guid grantId;
    await using (var db = host.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(x => x.Subject == seeded.MemberObjectId);
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == user.Id && x.RevokedAt == null);
      Assert.Equal(("Staff", (Guid?)host.Fixture.ClientId), (grant.Role, grant.ClientId));
      Assert.Equal("Joined the journey client team", grant.Reason);
      grantId = grant.Id;
    }

    await page.GetByRole(AriaRole.Tab, new() { Name = "AuditSphere Roles" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Firm role grants" }).WaitForAsync();
    var row = page.Locator("tr", new() { HasText = "Directory Journey Member" });
    await row.GetByRole(AriaRole.Button, new() { Name = "Revoke" }).ClickAsync();
    var confirm = page.GetByRole(AriaRole.Dialog);
    // UX-012: confirming without the required reason explains itself and submits nothing.
    await confirm.GetByRole(AriaRole.Button, new() { Name = "Revoke access" }).ClickAsync();
    await Assertions.Expect(confirm.GetByRole(AriaRole.Alert)).ToContainTextAsync("Enter a reason before confirming. Nothing has been submitted.");
    await using (var db = host.CreateDbContext())
      Assert.Null((await db.RoleGrants.AsNoTracking().SingleAsync(x => x.Id == grantId)).RevokedAt);
    await confirm.GetByLabel("Reason (required)").FillAsync("Left the engagement");
    await confirm.GetByRole(AriaRole.Button, new() { Name = "Revoke access" }).ClickAsync();
    await page.GetByText("Role grant revoked").WaitForAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.NotNull((await db.RoleGrants.SingleAsync(x => x.Id == grantId)).RevokedAt);
      Assert.Contains(await db.RoleGrantChangeEvidences.ToListAsync(), x => x.RoleGrantId == grantId && x.Reason == "Left the engagement");
    }
    session.AssertNoPageErrors();
  }

  [Fact]
  [Trait("CaseId", "M365-ADMIN-E2E-03")]
  public async Task CreateUserWhenEnabled_ShowsPasswordOnce_AndRecoversUnknownOutcome()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "M365-ADMIN-E2E-03");
    var seeded = await SeedAsync(host, verified: true);
    var origin = await host.StartWebForIdentityAsync(seeded.Admin, Simulation(seeded));
    await using var session = await BrowserSession.OpenAsync(origin, "/app/administration");
    var page = session.Page;
    await page.GetByRole(AriaRole.Tab, new() { Name = "Microsoft Directory" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Create Microsoft 365 user" }).WaitForAsync();

    async Task CreateAsync(string upn, string name)
    {
      await page.Locator("#create-display-name").FillAsync(name);
      await page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
      await page.Locator("#create-upn").FillAsync(upn);
      await page.Locator("#create-nickname").FillAsync(upn.Split('@')[0]);
      await page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync(); // default Staff role
      await SelectAsync(page, page.Locator("#create-scope"), "Firm-wide");
      await page.Locator("#create-reason").FillAsync("New joiner onboarding");
      await page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
      await page.GetByText("Request key").WaitForAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Create user" }).ClickAsync();
    }

    await CreateAsync("journey.new@example.test", "Journey New Joiner");
    await page.GetByText("One-time initial password").WaitForAsync();
    var password = await page.Locator("#one-time-password").InnerTextAsync();
    Assert.True(password.Length >= 16);
    await page.GetByRole(AriaRole.Button, new() { Name = "I have securely recorded it" }).ClickAsync();
    await page.Locator("#one-time-password").WaitForAsync(new() { State = WaitForSelectorState.Detached });
    await using (var db = host.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(x => x.Email == "journey.new@example.test");
      Assert.Single(await db.RoleGrants.Where(x => x.UserId == user.Id && x.ClientId == null).ToListAsync());
      var persisted = System.Text.Json.JsonSerializer.Serialize(new object[] {
        await db.Microsoft365ExternalOperations.ToListAsync(), await db.Microsoft365AdministrationEvents.ToListAsync(),
        await db.Users.ToListAsync(), await db.DirectoryUserObservations.ToListAsync() });
      Assert.DoesNotContain(password, persisted);
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Start another" }).ClickAsync();
    await CreateAsync("unknown.journey@example.test", "Journey Timeout User");
    await page.GetByText("Microsoft did not confirm the result").WaitForAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Reconcile" }).First.ClickAsync();
    await page.GetByText("Reconciled by immutable Microsoft identity").WaitForAsync();
    await using (var db = host.CreateDbContext())
    {
      var operation = await db.Microsoft365ExternalOperations.SingleAsync(x => x.TargetDescriptor == "unknown.journey@example.test");
      Assert.Equal(ExternalOperationStates.Bound, operation.State);
      Assert.Equal(1, operation.AttemptCount);
      Assert.Single(await db.Users.Where(x => x.Email == "unknown.journey@example.test").ToListAsync());
    }
    session.AssertNoPageErrors();
  }

  [Fact]
  [Trait("CaseId", "M365-ADMIN-E2E-04")]
  public async Task GuestInvitation_IsClientScoped_AndGuestCannotOpenAdministration()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "M365-ADMIN-E2E-04");
    var seeded = await SeedAsync(host, verified: true);
    var origin = await host.StartWebForIdentityAsync(seeded.Admin, Simulation(seeded, provisioning: false));
    await using var session = await BrowserSession.OpenAsync(origin, "/app/administration");
    var page = session.Page;
    await page.GetByRole(AriaRole.Tab, new() { Name = "Microsoft Directory" }).ClickAsync();
    await page.GetByText("Tenant user provisioning is not enabled for this deployment").WaitForAsync();
    await page.GetByRole(AriaRole.Tab, new() { Name = "Invitations" }).ClickAsync();
    await page.GetByLabel("Invite new guest").CheckAsync();
    await page.Locator("#guest-email").FillAsync("journey.cfo@client.test");
    await SelectAsync(page, page.Locator("#guest-client"), (await ClientNameAsync(host))!);
    await page.Locator("#guest-reason").FillAsync("Client finance contact for PBC");
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview invitation" }).ClickAsync();
    await page.GetByText("Firm-wide access is impossible for client users").WaitForAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Send Microsoft invitation" }).ClickAsync();
    await page.GetByText("Guest invited and bound").WaitForAsync();

    AppUser guest;
    await using (var db = host.CreateDbContext())
    {
      guest = await db.Users.SingleAsync(x => x.Email == "journey.cfo@client.test");
      Assert.Equal("Client", guest.UserKind);
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == guest.Id);
      Assert.Equal(("ClientUser", (Guid?)host.Fixture.ClientId), (grant.Role, grant.ClientId));
    }
    var guestOrigin = await host.StartWebForIdentityAsync(guest);
    await using var guestSession = await BrowserSession.OpenAsync(guestOrigin, "/app/administration");
    await guestSession.Page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var guestBody = await guestSession.Page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Setup progress", guestBody);
    Assert.DoesNotContain(seeded.Admin.Email, guestBody);
    session.AssertNoPageErrors();
  }

  [Fact]
  [Trait("CaseId", "M365-ADMIN-E2E-05")]
  public async Task GroupMembershipAndFailureStates_AreExplicit()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "M365-ADMIN-E2E-05");
    var seeded = await SeedAsync(host, verified: true);
    var origin = await host.StartWebForIdentityAsync(seeded.Admin, Simulation(seeded));
    await using var session = await BrowserSession.OpenAsync(origin, "/app/administration");
    var page = session.Page;
    await page.GetByRole(AriaRole.Tab, new() { Name = "Groups" }).ClickAsync();
    await page.Locator("#group-object-id").FillAsync(seeded.GroupObjectId);
    await page.Locator("#group-purpose").FillAsync("Journey team collaboration");
    await page.Locator("#group-reason").FillAsync("Team channel membership");
    await page.GetByRole(AriaRole.Button, new() { Name = "Add to allowlist" }).ClickAsync();
    await page.GetByText("AuditSphere Journey Team").WaitForAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "View members" }).ClickAsync();
    await SelectAsync(page, page.Locator("#group-member-user"), "Tenant Journey Admin");
    await page.Locator("#group-member-reason").FillAsync("Joins journey team");
    await page.GetByRole(AriaRole.Button, new() { Name = "Add member" }).ClickAsync();
    await page.GetByText("Microsoft added the member.").WaitForAsync();
    Assert.Contains("Existing membership: Not a member", await page.Locator("body").InnerTextAsync());
    await using (var db = host.CreateDbContext())
      Assert.Contains(await db.Microsoft365AdministrationEvents.ToListAsync(), x => x.Operation == "ADD_GROUP_MEMBER_OUTCOME" && x.NewState == "ACCEPTED");

    // Failure state: an unverified capability explains the blocker instead of offering the action.
    await using (var db = host.CreateDbContext())
    {
      db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification
      {
        Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, TenantId = seeded.TenantId,
        Capability = Microsoft365Capabilities.GuestInvitation, Permission = "User.Invite.All",
        State = CapabilityVerificationStates.NotGranted, DiagnosticCode = "app-role-not-granted",
        ObservedByUserId = seeded.Admin.Id, ObservedAt = DateTimeOffset.UtcNow.AddSeconds(1)
      });
      await db.SaveChangesAsync();
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh administration" }).ClickAsync();
    await page.GetByText("Guest invitations is enabled but NOT_GRANTED").First.WaitForAsync();
    await page.GetByRole(AriaRole.Tab, new() { Name = "Invitations" }).ClickAsync();
    await page.GetByLabel("Invite new guest").CheckAsync();
    await page.GetByText("Guest invitation is enabled but not verified").WaitForAsync();
    session.AssertNoPageErrors();
  }

  private static async Task<string?> ClientNameAsync(OwnedHost host)
  {
    await using var db = host.CreateDbContext();
    return await db.PracticeClients.Where(x => x.Id == host.Fixture.ClientId).Select(x => x.LegalName).SingleAsync();
  }

  /// <summary>Opens a MudSelect and chooses the option with the given text.</summary>
  private static async Task SelectAsync(IPage page, ILocator select, string option)
  {
    var item = page.Locator(".mud-popover-open .mud-list-item", new() { HasText = option }).First;
    // A freshly rendered server dialog may precede the popover's interactive registration.
    // Retry only opening the menu; the option and every business mutation are clicked once.
    for (var attempt = 0; attempt < 4; attempt++)
    {
      if (!await item.IsVisibleAsync()) await select.ClickAsync(new() { Timeout = 5000 });
      try { await item.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 }); break; }
      catch (TimeoutException) when (attempt < 3) { }
    }
    await item.ClickAsync();
    await item.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 });
  }

  private sealed class BrowserSession : IAsyncDisposable
  {
    private readonly IPlaywright playwright;
    private readonly IBrowser browser;
    private readonly List<string> diagnostics = [];
    public IPage Page { get; private set; } = default!;

    private BrowserSession(IPlaywright playwright, IBrowser browser) { this.playwright = playwright; this.browser = browser; }

    public static async Task<BrowserSession> OpenAsync(string origin, string returnUrl)
    {
      var playwright = await Playwright.CreateAsync();
      var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      var session = new BrowserSession(playwright, browser);
      var context = await browser.NewContextAsync();
      session.Page = await context.NewPageAsync();
      session.Page.PageError += (_, error) => session.diagnostics.Add("page-error: " + error);
      await session.Page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}");
      await session.Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
      return session;
    }

    public void AssertNoPageErrors() => Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    public async ValueTask DisposeAsync()
    {
      await browser.DisposeAsync();
      playwright.Dispose();
    }
  }
}
