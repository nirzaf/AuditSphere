using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class ClientScopeJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-FIRM-READ-01")]
  public async Task FirmLedgerAndLeadsRequireFirmWideRoleGrants()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-FIRM-READ-01");
    var financeReviewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var clientFinanceManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var auditPartner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var relationshipManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var clientRelationshipManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var clientRelationshipIdentity = PbcSeed.User(host.Fixture.FirmId, "Client");
    const string periodCode = "2026-09";
    const string accountName = "SYN-PAR-002-FIRM-LEDGER-ACCOUNT";
    const string leadName = "SYN-PAR-002-FIRM-COMMERCIAL-LEAD";
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(financeReviewer, clientFinanceManager, auditPartner, relationshipManager,
        clientRelationshipManager, clientRelationshipIdentity);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, financeReviewer, "FinanceReviewer"),
        PbcSeed.Grant(host.Fixture.FirmId, clientFinanceManager, "FinanceManager", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, auditPartner, "Partner"),
        PbcSeed.Grant(host.Fixture.FirmId, relationshipManager, "RelationshipManager"),
        PbcSeed.Grant(host.Fixture.FirmId, clientRelationshipManager, "RelationshipManager", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, clientRelationshipIdentity, "RelationshipManager"));
      db.FirmPeriods.Add(new FirmPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PeriodCode = periodCode,
        Status = LedgerStates.PeriodOpen, Revision = 1
      });
      db.FirmAccounts.Add(new FirmAccount
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, Code = "SYN-PAR-002",
        Name = accountName, AccountType = LedgerStates.AccountAsset, NormalSide = LedgerStates.Debit
      });
      db.Leads.Add(new Lead
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, Name = leadName,
        Source = "Synthetic regression fixture", Status = CrmStates.LeadNew, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var financeReviewerUrl = await host.StartWebForIdentityAsync(financeReviewer);
    var clientFinanceUrl = await host.StartWebForIdentityAsync(clientFinanceManager);
    var auditPartnerUrl = await host.StartWebForIdentityAsync(auditPartner);
    var relationshipManagerUrl = await host.StartWebForIdentityAsync(relationshipManager);
    var clientRelationshipUrl = await host.StartWebForIdentityAsync(clientRelationshipManager);
    var clientIdentityUrl = await host.StartWebForIdentityAsync(clientRelationshipIdentity);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    async Task<string> ReadPageAsync(string origin, string route, string marker, bool denied)
    {
      await using var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      var diagnostics = new List<string>();
      var connected = WaitForCircuitConnectionAsync(page, diagnostics);
      await page.GotoAsync(SignInUrl(origin, route));
      if (denied)
        await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
      else
        await page.GetByText(marker, new() { Exact = true }).WaitForAsync();
      await connected;
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
      if (!denied && route is ("/app/finance" or "/app/practice/leads"))
      {
        foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
        {
          await page.SetViewportSizeAsync(width, 900);
          await page.WaitForFunctionAsync("() => document.documentElement.scrollWidth <= window.innerWidth + 1", null, new() { Timeout = 5000 });
          if (width == 320 && Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } narrowCaptureDir)
          {
            Directory.CreateDirectory(narrowCaptureDir);
            await page.ScreenshotAsync(new() { Path = Path.Combine(narrowCaptureDir, $"{(route == "/app/finance" ? "finance" : "leads")}-320.png"), FullPage = true });
          }
          var offenders = await page.EvaluateAsync<string[]>("""() => [...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > window.innerWidth + 1 && getComputedStyle(e).display !== 'none').slice(0, 12).map(e => `${e.tagName}.${e.className?.toString().slice(0, 50)} right=${Math.round(e.getBoundingClientRect().right)}`)""");
          var metrics = await page.EvaluateAsync<string>("() => `inner=${innerWidth} doc=${document.documentElement.scrollWidth} body=${document.body.scrollWidth}`");
          Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
            $"{route} overflows at {width}px: {metrics}; {string.Join(", ", offenders)}");
          if ((width == 390 || width == 1440) && Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } captureDir)
          {
            Directory.CreateDirectory(captureDir);
            await page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"{(route == "/app/finance" ? "finance" : "leads")}-{width}.png"), FullPage = true });
          }
        }
      }
      var body = await page.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
      return body;
    }

    var authorizedFinance = await ReadPageAsync(financeReviewerUrl, "/app/finance", periodCode, denied: false);
    Assert.Contains(accountName, authorizedFinance);
    var scopedFinance = await ReadPageAsync(clientFinanceUrl, "/app/finance", "Access unavailable", denied: true);
    Assert.DoesNotContain(periodCode, scopedFinance);
    Assert.DoesNotContain(accountName, scopedFinance);
    var partnerFinance = await ReadPageAsync(auditPartnerUrl, "/app/finance", "Access unavailable", denied: true);
    Assert.DoesNotContain(periodCode, partnerFinance);
    Assert.DoesNotContain(accountName, partnerFinance);

    var authorizedLeads = await ReadPageAsync(relationshipManagerUrl, "/app/practice/leads", leadName, denied: false);
    Assert.Contains(leadName, authorizedLeads);
    var scopedLeads = await ReadPageAsync(clientRelationshipUrl, "/app/practice/leads", "Access unavailable", denied: true);
    Assert.DoesNotContain(leadName, scopedLeads);
    var clientClassified = await ReadPageAsync(clientIdentityUrl, "/app/practice/leads", "Access unavailable", denied: true);
    Assert.DoesNotContain(leadName, clientClassified);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ADV-CONSOLIDATION-READ-01")]
  public async Task AdvancedConsolidationReadRejectsClientIdentityWithErroneousGroupGrant()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ADV-CONSOLIDATION-READ-01");
    var groupId = Guid.NewGuid();
    var scopeId = Guid.NewGuid();
    const string privateGroupName = "SYN-PAR-002-PRIVATE-ADVANCED-GROUP";
    await using (var db = host.CreateDbContext())
    {
      db.ClientGroups.Add(new ClientGroup
      {
        Id = groupId, FirmId = host.Fixture.FirmId, Code = "SYN-PAR-002-PRIVATE",
        Name = privateGroupName, CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
      {
        Id = scopeId, FirmId = host.Fixture.FirmId, GroupId = groupId, PeriodId = Guid.NewGuid(),
        Method = AdvancedConsolidationMethods.AcquisitionNci, ReportingCurrency = "QAR",
        OpeningBasis = "OPENING-2026", CreatedByUserId = host.Fixture.Admin.Id
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, GroupId = groupId,
        UserId = host.Fixture.Client.Id, Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = host.Fixture.Admin.Id
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, GroupId = groupId,
        UserId = host.Fixture.Staff.Id, Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = host.Fixture.Admin.Id
      });
      await db.SaveChangesAsync();
    }

    var clientOrigin = await host.StartWebForIdentityAsync(host.Fixture.Client);
    var staffOrigin = await host.StartWebForIdentityAsync(host.Fixture.Staff);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(clientOrigin, $"/app/consolidation/advanced/{scopeId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateGroupName, body);
    Assert.DoesNotContain(scopeId.ToString("D"), body);

    await page.GotoAsync(SignInUrl(clientOrigin, "/app/consolidation"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "No group access" }).WaitForAsync();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateGroupName, body);
    Assert.DoesNotContain(scopeId.ToString("D"), body);

    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffDiagnostics = new List<string>();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, staffDiagnostics);
    await staffPage.GotoAsync(SignInUrl(staffOrigin, "/app/consolidation"));
    await staffPage.GetByRole(AriaRole.Heading, new() { Name = $"{privateGroupName} · v1", Exact = true }).WaitForAsync();
    await staffConnected;
    var staffBody = await staffPage.Locator("body").InnerTextAsync();
    Assert.Contains(privateGroupName, staffBody);
    Assert.Contains(AdvancedConsolidationMethods.AcquisitionNci, staffBody);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    Assert.DoesNotContain(staffDiagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ADV-CONSOLIDATION-STALE-ROUTE-01")]
  public async Task AdvancedConsolidationClearsPriorGroupWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ADV-CONSOLIDATION-STALE-ROUTE-01");
    var authorizedGroupId = Guid.NewGuid();
    var authorizedScopeId = Guid.NewGuid();
    var unauthorizedGroupId = Guid.NewGuid();
    var unauthorizedScopeId = Guid.NewGuid();
    const string privateGroupName = "SYN-PAR-002-ADV-GROUP-PRIVATE";
    const string unauthorizedGroupName = "SYN-PAR-002-ADV-GROUP-UNAUTHORIZED";
    await using (var db = host.CreateDbContext())
    {
      var now = DateTimeOffset.UtcNow;
      db.ClientGroups.AddRange(
        new ClientGroup
        {
          Id = authorizedGroupId, FirmId = host.Fixture.FirmId, Code = "SYN-PAR-002-ADV-A",
          Name = privateGroupName, CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = now
        },
        new ClientGroup
        {
          Id = unauthorizedGroupId, FirmId = host.Fixture.FirmId, Code = "SYN-PAR-002-ADV-B",
          Name = unauthorizedGroupName, CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = now
        });
      db.ConsolidationScopeVersions.AddRange(
        new ConsolidationScopeVersion
        {
          Id = authorizedScopeId, FirmId = host.Fixture.FirmId, GroupId = authorizedGroupId,
          PeriodId = Guid.NewGuid(), Method = AdvancedConsolidationMethods.AcquisitionNci,
          ReportingCurrency = "QAR", OpeningBasis = "OPENING-2026", CreatedByUserId = host.Fixture.Admin.Id
        },
        new ConsolidationScopeVersion
        {
          Id = unauthorizedScopeId, FirmId = host.Fixture.FirmId, GroupId = unauthorizedGroupId,
          PeriodId = Guid.NewGuid(), Method = AdvancedConsolidationMethods.AcquisitionNci,
          ReportingCurrency = "QAR", OpeningBasis = "OPENING-2026", CreatedByUserId = host.Fixture.Admin.Id
        });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, GroupId = authorizedGroupId,
        UserId = host.Fixture.Staff.Id, Role = "AccountingPreparer", GrantedAt = now,
        GrantedByUserId = host.Fixture.Admin.Id
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/consolidation/advanced/{authorizedScopeId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = privateGroupName }).WaitForAsync();
    await connected;
    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/consolidation/advanced/{unauthorizedScopeId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateGroupName, body);
    Assert.DoesNotContain(unauthorizedGroupName, body);
    Assert.DoesNotContain(authorizedScopeId.ToString("D"), body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/consolidation/advanced/{authorizedScopeId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = privateGroupName }).WaitForAsync();
    await page.Locator("[aria-label='Scoped advanced consolidation counts']").GetByText("0", new() { Exact = true }).First.WaitForAsync();
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px') || document.querySelector('.audit-sidebar')?.getBoundingClientRect().left >= -1");
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Advanced consolidation overflows at {width}px.");
      if (Environment.GetEnvironmentVariable("AUDITSPHERE_ADVANCED_UI_CAPTURE_DIR") is { Length: > 0 } captureDir &&
          width is 390 or 1440)
      {
        Directory.CreateDirectory(captureDir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"advanced-{width}.png"), FullPage = true });
      }
    }
    var manifestEditor = page.Locator("#advanced-source-manifest");
    await manifestEditor.FillAsync("{\"sources\":[],\"reviewedJournals\":[],\"note\":\"synthetic draft\"}");
    await page.WaitForFunctionAsync("() => document.querySelector('[data-draft-scope] .draft-status')?.textContent?.includes('Draft saved') === true");
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = privateGroupName }).WaitForAsync();
    await Assertions.Expect(page.Locator("#advanced-source-manifest"))
      .ToHaveValueAsync("{\"sources\":[],\"reviewedJournals\":[],\"note\":\"synthetic draft\"}");
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle); // Preserve focus through the interactive-render replacement.
    var submitSchedule = page.GetByRole(AriaRole.Button, new() { Name = "Submit schedule" });
    await submitSchedule.FocusAsync();
    await Assertions.Expect(submitSchedule).ToHaveCSSAsync("outline-style", "solid");
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FIRM-ADMIN-READ-01")]
  public async Task FirmAdministrationRejectsClientIdentityWithErroneousAdminGrant()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FIRM-ADMIN-READ-01");
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Client, "Administrator"));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(host.Fixture.Client);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app/administration"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Firm safety state", body);
    Assert.DoesNotContain("Firm role grants", body);
    Assert.DoesNotContain(host.Fixture.Client.Email, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FIRM-ADMIN-REVOKE-01")]
  public async Task FirmAdministrationRefreshClearsDirectoryAfterAdminGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FIRM-ADMIN-REVOKE-01");
    var administrator = PbcSeed.User(host.Fixture.FirmId, "Staff");
    Guid invitationGrantId;
    Guid invitationId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(administrator);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, administrator, "Administrator"));
      await db.SaveChangesAsync();
      var invitation = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"),
        new ApplyRoleGrantAndInvitationRequest(host.Fixture.Staff.Id, "Staff", "CLIENT", host.Fixture.ClientId));
      Assert.True(invitation.Succeeded, invitation.Message);
      invitationGrantId = invitation.Value!.RoleGrantId;
      invitationId = invitation.Value.InvitationId;
    }

    var origin = await host.StartWebForIdentityAsync(administrator);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app/administration"));
    await connected;
    await OpenRolesTabAsync(page, page.GetByRole(AriaRole.Heading, new() { Name = "Firm role grants" }));
    Assert.Contains(host.Fixture.Client.Email, await page.Locator("body").InnerTextAsync());
    var documentToken = await page.EvaluateAsync<string>("window.__adminRevocationToken = crypto.randomUUID()");
    var copyPage = await context.NewPageAsync();
    var copyConnected = WaitForCircuitConnectionAsync(copyPage, diagnostics);
    await copyPage.GotoAsync(SignInUrl(origin, "/app/administration"));
    await copyConnected;
    await OpenRolesTabAsync(copyPage, copyPage.GetByRole(AriaRole.Button, new() { Name = "Copy invitation" }));
    await copyPage.EvaluateAsync("() => { window.__adminCopyCount = 0; window.auditSphereExports.copyText = () => { window.__adminCopyCount += 1; }; }");

    await using (var db = host.CreateDbContext())
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(invitationGrantId));
      Assert.True(revoked.Succeeded, revoked.Message);
      var staleCopy = await FirmAdministrationQuery.GetCopyableInvitationAsync(db,
        PbcSeed.Actor(administrator, "Administrator"), invitationId);
      Assert.False(staleCopy.Succeeded);
    }
    await copyPage.EvaluateAsync("() => { const button = [...document.querySelectorAll('button')].find(x => x.textContent?.trim() === 'Copy invitation'); button?.click(); }");
    await copyPage.GetByRole(AriaRole.Button, new() { Name = "Copy invitation" }).WaitForAsync(new() { State = WaitForSelectorState.Detached });
    Assert.Equal(0, await copyPage.EvaluateAsync<int>("window.__adminCopyCount"));

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == administrator.Id &&
        x.Role == "Administrator" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    await page.EvaluateAsync("() => { const button = [...document.querySelectorAll('button')].find(x => x.textContent?.trim() === 'Refresh administration'); button?.click(); }");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(host.Fixture.Client.Email, body);
    Assert.DoesNotContain("Firm role grants", body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__adminRevocationToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-OPERATIONS-REVOKE-01")]
  public async Task OperationsRefreshClearsLedgerAfterAdminGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-OPERATIONS-REVOKE-01");
    var operatorUser = PbcSeed.User(host.Fixture.FirmId, "Staff");
    const string privateKind = "SYN-PAR-002-PRIVATE-OPERATION";
    Guid grantId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(operatorUser);
      var grant = PbcSeed.Grant(host.Fixture.FirmId, operatorUser, "Administrator");
      grantId = grant.Id;
      db.RoleGrants.Add(grant);
      db.DurableOperations.Add(new DurableOperation
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, OperationKind = privateKind,
        TargetId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), ExpectedRevision = 1,
        IdempotencyKey = Guid.NewGuid().ToString("N"), RequestDigest = new string('a', 64), RequestBytes = [1],
        Status = OperationState.DEAD_LETTER, CreatedAt = DateTimeOffset.UtcNow,
        NextAttemptAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(operatorUser);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app/operations"));
    await page.GetByText(privateKind, new() { Exact = true }).WaitForAsync();
    await connected;
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Firm operating mode" })).ToContainTextAsync("LOCAL_ONLY");
    var operationCounts = page.Locator("[aria-label='Latest durable operation counts']");
    await Assertions.Expect(operationCounts).ToContainTextAsync("Needs attention");
    await Assertions.Expect(operationCounts.GetByText("1", new() { Exact = true })).ToHaveCountAsync(1);
    var operationsCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_OPERATIONS_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(operationsCaptureDir)) Directory.CreateDirectory(operationsCaptureDir);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("expected => document.documentElement.clientWidth === expected && (expected >= 960 || (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px'))", width);
      await page.GetByText(privateKind, new() { Exact = true }).WaitForAsync();
      var documentWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
      Assert.True(documentWidth <= width + 1, $"Operations document is {documentWidth}px wide at {width}px viewport.");
      if (operationsCaptureDir is not null && width is 390 or 1440)
        await page.ScreenshotAsync(new() { Path = Path.Combine(operationsCaptureDir, $"operations-{width}.png"), FullPage = true });
    }
    await page.SetViewportSizeAsync(1440, 900);
    await page.GetByRole(AriaRole.Link, new() { Name = "Back to portfolio" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    var refreshButton = page.GetByRole(AriaRole.Button, new() { Name = "Refresh operations" });
    await Assertions.Expect(refreshButton).ToBeFocusedAsync();
    await Assertions.Expect(refreshButton).ToHaveCSSAsync("outline-style", "solid");
    var documentToken = await page.EvaluateAsync<string>("window.__operationsToken = crypto.randomUUID()");

    await using (var db = host.CreateDbContext())
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grantId));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    await page.EvaluateAsync("() => { const button = [...document.querySelectorAll('button')].find(x => x.textContent?.trim() === 'Refresh operations'); button?.click(); }");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    Assert.DoesNotContain(privateKind, await page.Locator("body").InnerTextAsync());
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__operationsToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-LEADS-REVOKE-01")]
  public async Task PracticeLeadsRefreshClearsCommercialRowsAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-LEADS-REVOKE-01");
    var commercialUser = PbcSeed.User(host.Fixture.FirmId, "Staff");
    const string privateLead = "SYN-PAR-002-PRIVATE-LEAD";
    Guid grantId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(commercialUser);
      var grant = PbcSeed.Grant(host.Fixture.FirmId, commercialUser, "RelationshipManager");
      grantId = grant.Id;
      db.RoleGrants.Add(grant);
      db.Leads.Add(new Lead
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, Name = privateLead,
        Source = "Synthetic regression fixture", Status = CrmStates.LeadNew,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(commercialUser);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app/practice/leads"));
    await page.GetByText(privateLead, new() { Exact = true }).WaitForAsync();
    await connected;
    var documentToken = await page.EvaluateAsync<string>("window.__leadsToken = crypto.randomUUID()");

    await using (var db = host.CreateDbContext())
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grantId));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    await page.EvaluateAsync("() => { const button = [...document.querySelectorAll('button')].find(x => x.textContent?.trim() === 'Refresh leads'); button?.click(); }");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    Assert.DoesNotContain(privateLead, await page.Locator("body").InnerTextAsync());
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__leadsToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FINANCE-REVOKE-01")]
  public async Task FirmFinanceRefreshClearsLedgerAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FINANCE-REVOKE-01");
    var financeUser = PbcSeed.User(host.Fixture.FirmId, "Staff");
    const string privateAccount = "SYN-PAR-002-PRIVATE-FIRM-ACCOUNT";
    Guid grantId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(financeUser);
      var grant = PbcSeed.Grant(host.Fixture.FirmId, financeUser, "FinanceReviewer");
      grantId = grant.Id;
      db.RoleGrants.Add(grant);
      db.FirmAccounts.Add(new FirmAccount
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, Code = "SYN-PAR-002-FINANCE-REVOKE",
        Name = privateAccount, AccountType = LedgerStates.AccountAsset,
        NormalSide = LedgerStates.Debit
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(financeUser);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app/finance"));
    await page.GetByText(privateAccount, new() { Exact = true }).WaitForAsync();
    await connected;
    var documentToken = await page.EvaluateAsync<string>("window.__financeToken = crypto.randomUUID()");

    await using (var db = host.CreateDbContext())
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grantId));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    await page.EvaluateAsync("() => { const button = [...document.querySelectorAll('button')].find(x => x.textContent?.trim() === 'Refresh firm ledger'); button?.click(); }");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    Assert.DoesNotContain(privateAccount, await page.Locator("body").InnerTextAsync());
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__financeToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-PORTFOLIO-READ-01")]
  public async Task ClientIdentityWithErroneousFirmWideStaffGrantCannotViewPortfolio()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-PORTFOLIO-READ-01");
    const string privateClientName = "SYN-PAR-002-PRIVATE-PORTFOLIO-CLIENT";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, LegalName = privateClientName,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Client, "Staff"));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(host.Fixture.Client);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "No firm, client or engagement scope" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateClientName, body);
    Assert.DoesNotContain(host.Fixture.Client.Email, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-PORTFOLIO-EXPORT-REVOCATION-01")]
  public async Task PortfolioCsvRechecksGrantAndClearsRowsAfterRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-PORTFOLIO-EXPORT-REVOCATION-01");
    const string privateClientName = "=SYN-PAR-002-PORTFOLIO-REVOKED-CLIENT";
    var staff = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var clientId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(staff);
      db.PracticeClients.Add(new PracticeClient
      {
        Id = clientId, FirmId = host.Fixture.FirmId, LegalName = privateClientName,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, staff, "Staff", clientId: clientId));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(staff);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app"));
    await page.GetByText(privateClientName, new() { Exact = true }).WaitForAsync();
    await connected;
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh portfolio" }).ClickAsync();
    await page.GetByText(privateClientName, new() { Exact = true }).WaitForAsync();

    var documentToken = await page.EvaluateAsync<string>("window.__portfolioRevocationToken = crypto.randomUUID()");
    await page.EvaluateAsync("() => { window.__portfolioDownloads = []; window.auditSphereExports.downloadText = (name, text, type) => window.__portfolioDownloads.push({ name, text, type }); }");
    await page.GetByRole(AriaRole.Button, new() { Name = "Download scoped CSV" }).ClickAsync();
    await page.WaitForFunctionAsync("window.__portfolioDownloads.length === 1");
    var csv = await page.EvaluateAsync<string>("window.__portfolioDownloads[0].text");
    Assert.Contains($"\"'{privateClientName}\"", csv);
    Assert.Equal("auditsphere-portfolio.csv", await page.EvaluateAsync<string>("window.__portfolioDownloads[0].name"));
    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == staff.Id && x.Role == "Staff" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Download scoped CSV" }).ClickAsync();
    try { await page.GetByText(privateClientName, new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 }); }
    catch (TimeoutException ex)
    {
      throw new Xunit.Sdk.XunitException($"Export did not clear revoked portfolio.\n{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}\n{ex.Message}");
    }
    Assert.Equal(1, await page.EvaluateAsync<int>("window.__portfolioDownloads.length"));
    Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Download scoped CSV" }).CountAsync());
    Assert.DoesNotContain(privateClientName, await page.Locator("body").InnerTextAsync());
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__portfolioRevocationToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-TIME-ENGAGEMENT-SCOPE-01")]
  public async Task EngagementScopedTimeQueueHidesSiblingTasksEntriesAndClientPeriods()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-TIME-ENGAGEMENT-SCOPE-01");
    var engagementManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var siblingEngagementId = Guid.NewGuid();
    var assignedTaskId = Guid.NewGuid();
    var siblingTaskId = Guid.NewGuid();
    const string periodCode = "SYN-PAR-002-TIME-CLIENT-PERIOD";
    const string assignedTaskTitle = "SYN-PAR-002-ASSIGNED-ENGAGEMENT-TASK";
    const string siblingTaskTitle = "SYN-PAR-002-SIBLING-ENGAGEMENT-TASK";
    const string assignedNarrative = "SYN-PAR-002-ASSIGNED-TIME-NARRATIVE";
    const string siblingNarrative = "SYN-PAR-002-SIBLING-TIME-NARRATIVE";
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(engagementManager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, engagementManager, "Manager",
        clientId: host.Fixture.ClientId, engagementId: host.Fixture.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = periodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.WorkTasks.AddRange(
        new WorkTask
        {
          Id = assignedTaskId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = host.Fixture.EngagementId, Title = assignedTaskTitle, CreatedAt = DateTimeOffset.UtcNow
        },
        new WorkTask
        {
          Id = siblingTaskId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = siblingEngagementId, Title = siblingTaskTitle, CreatedAt = DateTimeOffset.UtcNow
        });
      db.TimeEntries.AddRange(
        new TimeEntry
        {
          Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = host.Fixture.EngagementId, TaskId = assignedTaskId, UserId = host.Fixture.Staff.Id,
          WorkDate = new DateOnly(2026, 9, 10), StartMinute = 540, DurationMinutes = 60,
          Role = "Staff", Activity = "Fieldwork", Narrative = assignedNarrative,
          BillableClassification = PracticeTimeStates.NonBillable,
          Status = PracticeTimeStates.TimeSubmitted, SubmittedAt = DateTimeOffset.UtcNow,
          NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
        },
        new TimeEntry
        {
          Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = siblingEngagementId, TaskId = siblingTaskId, UserId = host.Fixture.Staff.Id,
          WorkDate = new DateOnly(2026, 9, 11), StartMinute = 540, DurationMinutes = 60,
          Role = "Staff", Activity = "Review", Narrative = siblingNarrative,
          BillableClassification = PracticeTimeStates.NonBillable,
          Status = PracticeTimeStates.TimeSubmitted, SubmittedAt = DateTimeOffset.UtcNow,
          NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(engagementManager);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app/practice/time"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Practice time & task records" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains(assignedTaskTitle, body);
    Assert.Contains(assignedNarrative, body);
    Assert.DoesNotContain(siblingTaskTitle, body);
    Assert.DoesNotContain(siblingNarrative, body);
    Assert.DoesNotContain(periodCode, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ASSESS-01")]
  public async Task AssessmentDecisionIdResolvesScopeBeforeExposingAssessmentDetails()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ASSESS-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    var decisionId = Guid.NewGuid();
    const string privateRegistration = "SYNTHETIC-PAR-002-PRIVATE-ASSESSMENT-REGISTRATION";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated assessment client", CreatedAt = DateTimeOffset.UtcNow
      });
      var client = await db.PracticeClients.SingleAsync(x => x.Id == host.Fixture.ClientId);
      client.RegistrationNumber = privateRegistration;
      db.Users.AddRange(partner, unrelatedManager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId));
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, unrelatedManager, "Manager", clientId: unrelatedClientId));
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = decisionId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        Decision = "Declined", ServiceRoute = "SyntheticAudit", Generation = 1,
        Rationale = "Synthetic scope regression decision", EvaluationTemplateVersion = "SYNTHETIC-v1",
        EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = partner.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      var templateId = Guid.NewGuid();
      db.QuestionnaireTemplates.Add(new QuestionnaireTemplate
      {
        Id = templateId, Bank = "CE", Version = "ASSESS-UI-1", Name = "Synthetic assessment UI",
        IsActive = true, CreatedAt = DateTimeOffset.UtcNow
      });
      db.QuestionDefinitions.AddRange(
        new QuestionDefinition { Id = Guid.NewGuid(), TemplateId = templateId, QuestionCode = "CE-UI-1", Section = "A", Category = "Acceptance", PromptText = "Synthetic question one", SortOrder = 1 },
        new QuestionDefinition { Id = Guid.NewGuid(), TemplateId = templateId, QuestionCode = "CE-UI-2", Section = "A", Category = "Acceptance", PromptText = "Synthetic question two", SortOrder = 2 });
      db.EvaluationResponses.Add(new EvaluationResponse
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        Bank = "CE", QuestionId = "CE-UI-1", Answer = "Yes", AnsweredByUserId = partner.Id,
        AnsweredAt = DateTimeOffset.UtcNow
      });
      db.SpecialistClearances.Add(new SpecialistClearance
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        Area = "Independence", SpecialistName = "Synthetic specialist", Status = "HOLD",
        EvidenceReference = "SYNTHETIC-ASSESSMENT-HOLD", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var partnerUrl = await host.StartWebForIdentityAsync(partner);
    await using var partnerContext = await browser.NewContextAsync();
    var partnerPage = await partnerContext.NewPageAsync();
    await partnerPage.SetViewportSizeAsync(390, 900);
    var partnerConnected = WaitForCircuitConnectionAsync(partnerPage, []);
    await partnerPage.GotoAsync(SignInUrl(partnerUrl, $"/app/assessments/{decisionId:D}"));
    await partnerPage.GetByText(privateRegistration).WaitForAsync();
    await partnerPage.GetByText("Decision recorded:").WaitForAsync();
    await partnerConnected;
    var questionnaireProgress = partnerPage.GetByRole(AriaRole.Progressbar, new() { Name = "Questionnaire responses completed" });
    Assert.Equal("1", await questionnaireProgress.GetAttributeAsync("value"));
    Assert.Equal("2", await questionnaireProgress.GetAttributeAsync("max"));
    await Assertions.Expect(partnerPage.GetByRole(AriaRole.Region, new() { Name = "Evaluation progress" }))
      .ToContainTextAsync("0 / 1 specialist clearances");
    var assessmentCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_ASSESSMENT_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(assessmentCaptureDir)) Directory.CreateDirectory(assessmentCaptureDir);
    foreach (var width in new[] { 390, 1440, 320, 760, 1024, 1920 })
    {
      await partnerPage.SetViewportSizeAsync(width, 900);
      await partnerPage.WaitForTimeoutAsync(250);
      await partnerPage.GetByText(privateRegistration).WaitForAsync();
      await partnerPage.WaitForFunctionAsync("() => document.documentElement.scrollWidth <= window.innerWidth && (window.innerWidth > 760 || document.querySelector('#main-content').getBoundingClientRect().width >= window.innerWidth - 64)");
      Assert.False(await partnerPage.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"),
        $"Assessment page overflows at {width}px.");
      if (width <= 760)
      {
        var mainBox = await partnerPage.Locator("#main-content").BoundingBoxAsync();
        Assert.NotNull(mainBox);
        Assert.True(mainBox!.Width >= width - 64, $"Assessment workspace is squeezed to {mainBox.Width}px at {width}px.");
      }
      if (assessmentCaptureDir is not null && width is 390 or 1440)
        await partnerPage.ScreenshotAsync(new() { Path = Path.Combine(assessmentCaptureDir, $"assessment-{width}.png"), FullPage = true });
    }
    await partnerPage.Keyboard.PressAsync("Tab");
    Assert.True(await partnerPage.Locator(":focus-visible").CountAsync() > 0);

    var managerUrl = await host.StartWebForIdentityAsync(unrelatedManager);
    await using var managerContext = await browser.NewContextAsync();
    var managerPage = await managerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var managerConnected = WaitForCircuitConnectionAsync(managerPage, diagnostics);
    await managerPage.GotoAsync(SignInUrl(managerUrl, $"/app/assessments/{decisionId:D}"));
    await managerPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    await managerConnected;
    var body = await managerPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateRegistration, body);
    Assert.DoesNotContain("Decision recorded:", body);
    Assert.DoesNotContain("Client workspace", body);
    var deniedMessage = await managerPage.GetByRole(AriaRole.Alert).InnerTextAsync();
    await managerPage.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/assessments/{Guid.NewGuid():D}");
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Alert)).ToContainTextAsync("The assessment is not available in the current scope.");
    Assert.Equal(deniedMessage, await managerPage.GetByRole(AriaRole.Alert).InnerTextAsync());
    Assert.DoesNotContain(privateRegistration, await managerPage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-QUEUE-01")]
  public async Task ClientScopedPartnerCannotSeeAccountingEvidenceFromAnotherClient()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCT-QUEUE-01");
    var allowedPartner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedPartner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateEvidenceReference = "SYNTHETIC-PAR-002-PRIVATE-ACCOUNTING-EVIDENCE";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated accounting client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.AddRange(allowedPartner, unrelatedPartner);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, allowedPartner, "Partner", clientId: host.Fixture.ClientId));
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, unrelatedPartner, "Partner", clientId: unrelatedClientId));
      var periodId = Guid.NewGuid();
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = "SYN-2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.SpecialistAccountingSchedules.Add(new SpecialistAccountingSchedule
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, PeriodId = periodId, Area = "PAYROLL",
        MethodologyVersion = "SYNTHETIC-TEST-v1", EvidenceReference = privateEvidenceReference,
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var allowedUrl = await host.StartWebForIdentityAsync(allowedPartner);
    await using var allowedContext = await browser.NewContextAsync();
    var allowedPage = await allowedContext.NewPageAsync();
    var allowedConnected = WaitForCircuitConnectionAsync(allowedPage, []);
    await allowedPage.GotoAsync(SignInUrl(allowedUrl, "/app/accounting/evidence"));
    await allowedPage.GetByText(privateEvidenceReference).WaitForAsync();
    await allowedConnected;

    var unrelatedUrl = await host.StartWebForIdentityAsync(unrelatedPartner);
    await using var unrelatedContext = await browser.NewContextAsync();
    var unrelatedPage = await unrelatedContext.NewPageAsync();
    var diagnostics = new List<string>();
    var unrelatedConnected = WaitForCircuitConnectionAsync(unrelatedPage, diagnostics);
    await unrelatedPage.GotoAsync(SignInUrl(unrelatedUrl, "/app/accounting/evidence"));
    await unrelatedPage.GetByText("Evidence records", new() { Exact = true }).WaitForAsync();
    await unrelatedConnected;
    var body = await unrelatedPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateEvidenceReference, body);
    Assert.Contains("No ECL, inventory, specialist, analytical or journal-risk records are available in the selected scope.", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    await using (var db = host.CreateDbContext())
    {
      var revoked = await db.RoleGrants.Where(x => x.UserId == allowedPartner.Id && x.Role == "Partner" && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      Assert.Equal(1, revoked);
    }

    await allowedPage.GetByRole(AriaRole.Button, new() { Name = "Refresh queue" }).ClickAsync();
    await allowedPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var revokedBody = await allowedPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateEvidenceReference, revokedBody);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-WORKSPACE-01")]
  public async Task AccountingWorkspaceIgnoresOtherRoleAndMismatchedEngagementGrants()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCT-WORKSPACE-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    var unrelatedEngagementId = Guid.NewGuid();
    const string privateClientName = "SYNTHETIC-PAR-002-UNRELATED-ACCOUNTING-CLIENT";
    const string privatePeriodCode = "SYN-PRIVATE-ACCOUNTING-PERIOD";
    const string privateEvidenceReference = "SYNTHETIC-PAR-002-UNRELATED-ACCOUNTING-EVIDENCE";
    const string privateDifferenceArea = "SYNTHETIC-PAR-002-UNRELATED-DIFFERENCE";
    const string allowedDifferenceArea = "SYNTHETIC-PAR-002-ASSIGNED-DIFFERENCE";
    const string stalePeriodCode = "SYN-PAR-002-ACCOUNTING-STALE-CONTEXT";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = privateClientName, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = unrelatedEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = unrelatedClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(partner);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Staff", clientId: unrelatedClientId),
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId, engagementId: unrelatedEngagementId));
      db.ClientAccountingProfiles.Add(new ClientAccountingProfile
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
        Jurisdiction = "SYNTHETIC", FunctionalCurrency = "QAR", SourceSystem = "TEST",
        SourceSystemIdentifier = "SYNTHETIC-UNRELATED", CreatedByUserId = host.Fixture.Staff.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
        PeriodCode = privatePeriodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = stalePeriodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.SpecialistAccountingSchedules.Add(new SpecialistAccountingSchedule
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
        EngagementId = unrelatedEngagementId, Area = "PAYROLL", MethodologyVersion = "SYNTHETIC-TEST-v1",
        EvidenceReference = privateEvidenceReference, CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
        EngagementId = unrelatedEngagementId, AccountArea = privateDifferenceArea, DifferenceType = "UNADJUSTED",
        Description = "Synthetic unrelated-client difference", Amount = 987m, Currency = "QAR",
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, AccountArea = allowedDifferenceArea, DifferenceType = "UNADJUSTED",
        Description = "Synthetic assigned-client difference", Amount = 12m, Currency = "QAR",
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var baseUrl = await host.StartWebForIdentityAsync(partner);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(baseUrl, "/app/accounting"));
    await page.GetByText("1 explicitly granted legal entity").WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateClientName, body);
    Assert.DoesNotContain(privatePeriodCode, body);
    Assert.Contains(stalePeriodCode, body);
    var workflowProgress = page.Locator(".audit-accounting-progress progress");
    Assert.Equal(1, await workflowProgress.CountAsync());
    Assert.Equal("1", await workflowProgress.GetAttributeAsync("max"));
    Assert.Equal("0", await workflowProgress.GetAttributeAsync("value"));
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("() => document.documentElement.clientWidth === window.innerWidth");
      if (width < 960)
        await page.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')");
      if (width == 1440)
        await page.WaitForFunctionAsync("() => { const drawer = document.querySelector('.audit-sidebar'); const rect = drawer?.getBoundingClientRect(); return rect && rect.width > 200 && rect.left >= -1; }");
      await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
      if ((width == 320 || width == 390 || width == 1440) && Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } captureDir)
      {
        Directory.CreateDirectory(captureDir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"accounting-workspace-{width}.png"), FullPage = true });
      }
      var overflowDetails = await page.EvaluateAsync<string>("""() => JSON.stringify({ viewport: innerWidth, document: document.documentElement.scrollWidth, offenders: [...document.querySelectorAll('body *')].filter(x => x.getBoundingClientRect().right > innerWidth + 1 && getComputedStyle(x).position !== 'fixed').slice(0, 8).map(x => ({ tag: x.tagName, className: typeof x.className === 'string' ? x.className : '', width: Math.round(x.getBoundingClientRect().width), right: Math.round(x.getBoundingClientRect().right) })) })""");
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Accounting workspace overflows the {width}px viewport: {overflowDetails}");
    }
    await page.Locator("#accounting-context-selector").FocusAsync();
    Assert.Equal("solid", await page.Locator("#accounting-context-selector")
      .EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    var evidencePage = await context.NewPageAsync();
    var evidenceConnected = WaitForCircuitConnectionAsync(evidencePage, diagnostics);
    await evidencePage.GotoAsync(SignInUrl(baseUrl, "/app/accounting/evidence"));
    await evidencePage.GetByText("Evidence records", new() { Exact = true }).WaitForAsync();
    await evidenceConnected;
    Assert.DoesNotContain(privateEvidenceReference, await evidencePage.Locator("body").InnerTextAsync());
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await evidencePage.SetViewportSizeAsync(width, 900);
      await evidencePage.WaitForFunctionAsync("() => document.documentElement.clientWidth === window.innerWidth");
      if (width < 960)
        await evidencePage.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')");
      await evidencePage.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
      var evidenceOverflow = await evidencePage.EvaluateAsync<string>("""() => JSON.stringify({ viewport: innerWidth, document: document.documentElement.scrollWidth, offenders: [...document.querySelectorAll('body *')].filter(x => x.getBoundingClientRect().right > innerWidth + 1 && getComputedStyle(x).position !== 'fixed').slice(0, 8).map(x => ({ tag: x.tagName, className: typeof x.className === 'string' ? x.className : '', width: Math.round(x.getBoundingClientRect().width), right: Math.round(x.getBoundingClientRect().right) })) })""");
      Assert.True(await evidencePage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Accounting evidence overflows the {width}px viewport: {evidenceOverflow}");
      if ((width == 390 || width == 1440) && Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } captureDir)
      {
        Directory.CreateDirectory(captureDir);
        await evidencePage.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"accounting-evidence-{width}.png"), FullPage = true });
      }
    }
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    var recordsPage = await context.NewPageAsync();
    var recordsConnected = WaitForCircuitConnectionAsync(recordsPage, diagnostics);
    await recordsPage.GotoAsync(SignInUrl(baseUrl, "/app/accounting/differences"));
    await recordsPage.Locator("h1").WaitForAsync();
    Assert.Equal("Audit differences", await recordsPage.Locator("h1").InnerTextAsync());
    await recordsConnected;
    await recordsPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var recordsBody = await recordsPage.Locator("body").InnerTextAsync();
    Assert.Contains(allowedDifferenceArea, recordsBody);
    Assert.DoesNotContain(privateDifferenceArea, recordsBody);
    Assert.DoesNotContain(privateClientName, recordsBody);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    await using (var db = host.CreateDbContext())
    {
      var revoked = await db.RoleGrants.Where(x => x.UserId == partner.Id && x.Role == "Partner" && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      Assert.Equal(2, revoked);
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh workspace" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var revokedWorkspace = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(stalePeriodCode, revokedWorkspace);
    Assert.DoesNotContain("1 explicitly granted legal entity", revokedWorkspace);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-WORKSPACE-SIBLING-TASK-01")]
  public async Task AccountingWorkspaceHidesSiblingEngagementTaskSummary()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ACCT-WORKSPACE-SIBLING-TASK-01");
    var scopedUser = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var assignedOwner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    assignedOwner.DisplayName = "SYN-PAR-002-WORKSPACE-ASSIGNED-OWNER";
    var siblingOwner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    siblingOwner.DisplayName = "SYN-PAR-002-WORKSPACE-SIBLING-OWNER";
    var siblingEngagementId = Guid.NewGuid();
    var periodId = Guid.NewGuid();

    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(scopedUser, assignedOwner, siblingOwner);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, scopedUser, "AccountingPreparer",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = "SYN-PAR-002-WORKSPACE-PERIOD", StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 12, 31), Basis = "STATUTORY", Currency = "QAR",
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.WorkTasks.AddRange(
        new WorkTask
        {
          Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = host.Fixture.EngagementId, ReportingPeriodId = periodId,
          Title = "Synthetic assigned workflow task", AssigneeUserId = assignedOwner.Id,
          DueDate = new DateOnly(2026, 9, 30), CreatedAt = DateTimeOffset.UtcNow
        },
        new WorkTask
        {
          Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = siblingEngagementId, ReportingPeriodId = periodId,
          Title = "Synthetic sibling workflow task", AssigneeUserId = siblingOwner.Id,
          DueDate = new DateOnly(2026, 9, 1), CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var origin = await host.StartWebForIdentityAsync(scopedUser);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app/accounting"));
    await page.GetByText("1 explicitly granted legal entity").WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(siblingOwner.DisplayName, body);
    Assert.Contains(assignedOwner.DisplayName, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-PERIOD-01")]
  public async Task AccountingPeriodRequiresClientGrantAndClearsPriorPeriodOnRouteChange()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCT-PERIOD-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    var unrelatedEngagementId = Guid.NewGuid();
    const string assignedPeriodCode = "SYN-ASSIGNED-ACCOUNTING-PERIOD";
    const string privatePeriodCode = "SYN-PRIVATE-ACCOUNTING-PERIOD-DETAIL";
    var assignedPeriodId = Guid.NewGuid();
    var privatePeriodId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "SYNTHETIC-PAR-002-UNRELATED-PERIOD-CLIENT", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = unrelatedEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = unrelatedClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(partner);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Staff", clientId: unrelatedClientId),
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId, engagementId: unrelatedEngagementId));
      db.ClientReportingPeriods.AddRange(
        new ClientReportingPeriod
        {
          Id = assignedPeriodId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          PeriodCode = assignedPeriodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
          Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
        },
        new ClientReportingPeriod
        {
          Id = privatePeriodId, FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
          PeriodCode = privatePeriodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
          Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }

    var baseUrl = await host.StartWebForIdentityAsync(partner);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(baseUrl, $"/app/accounting/periods/{assignedPeriodId:D}"));
    await page.Locator("h1").WaitForAsync();
    await connected;
    var assignedBody = await page.Locator("body").InnerTextAsync();
    Assert.Contains(assignedPeriodCode, assignedBody);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      if (width < 960)
        await page.WaitForFunctionAsync("() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')");
      else if (width == 1440)
        await page.WaitForFunctionAsync("() => document.querySelector('.audit-main-content')?.getBoundingClientRect().left >= 230");
      await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
      if ((width == 390 || width == 1440) && Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } periodCaptureDir)
      {
        Directory.CreateDirectory(periodCaptureDir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(periodCaptureDir, $"period-detail-{width}.png"), FullPage = true });
      }
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Accounting period overflows the {width}px viewport.");
    }
    var periodBackLink = page.GetByRole(AriaRole.Link, new() { Name = "Back to accounting workspace" });
    await periodBackLink.FocusAsync();
    Assert.Equal("solid", await periodBackLink.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/accounting/periods/{privatePeriodId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(assignedPeriodCode, body);
    Assert.DoesNotContain(privatePeriodCode, body);
    Assert.DoesNotContain("SYNTHETIC-PAR-002-UNRELATED-PERIOD-CLIENT", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/accounting/periods/{assignedPeriodId:D}");
    await page.GetByText(assignedPeriodCode).WaitForAsync();
    await using (var db = host.CreateDbContext())
    {
      var revoked = await db.RoleGrants.Where(x => x.UserId == partner.Id && x.Role == "Partner" && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      Assert.Equal(2, revoked);
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh period" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var revokedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(assignedPeriodCode, revokedBody);
    Assert.DoesNotContain(privatePeriodCode, revokedBody);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-PERIOD-MAINT-01")]
  public async Task PeriodMaintenanceQueuesDoNotWidenEngagementGrantToClientPeriodScope()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCT-PERIOD-MAINT-01");
    var clientPartner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var engagementPreparer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    const string privatePeriodCode = "SYN-PAR-002-CLIENT-LEVEL-PERIOD";
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(clientPartner, engagementPreparer);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, clientPartner, "Partner", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, engagementPreparer, "AccountingPreparer",
          clientId: host.Fixture.ClientId, engagementId: host.Fixture.EngagementId));
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = privatePeriodCode, StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Closed,
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var partnerUrl = await host.StartWebForIdentityAsync(clientPartner);
    var engagementUrl = await host.StartWebForIdentityAsync(engagementPreparer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var diagnostics = new List<string>();

    foreach (var route in new[] { "/app/accounting/rollforward", "/app/accounting/restatements" })
    {
      await using var partnerContext = await browser.NewContextAsync();
      var partnerPage = await partnerContext.NewPageAsync();
      var partnerConnected = WaitForCircuitConnectionAsync(partnerPage, diagnostics);
      await partnerPage.GotoAsync(SignInUrl(partnerUrl, route));
      var prerenderedHeading = await partnerPage.QuerySelectorAsync("h1");
      await partnerConnected;
      if (prerenderedHeading is not null)
        await partnerPage.WaitForFunctionAsync("element => !element.isConnected", prerenderedHeading);
      await Assertions.Expect(partnerPage.Locator("body")).ToContainTextAsync(privatePeriodCode);
      if (route.EndsWith("rollforward", StringComparison.Ordinal))
        Assert.Contains(privatePeriodCode, await partnerPage.Locator("body").InnerTextAsync());
      else
      {
        await partnerPage.GetByRole(AriaRole.Heading, new() { Name = "Request a restatement" }).WaitForAsync();
        Assert.Contains(privatePeriodCode, await partnerPage.Locator("body").InnerTextAsync());
      }

      await using var engagementContext = await browser.NewContextAsync();
      var engagementPage = await engagementContext.NewPageAsync();
      var engagementConnected = WaitForCircuitConnectionAsync(engagementPage, diagnostics);
      await engagementPage.GotoAsync(SignInUrl(engagementUrl, route));
      await engagementPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
      await engagementConnected;
      var body = await engagementPage.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privatePeriodCode, body);
      Assert.DoesNotContain("PBC TEST CLIENT", body);
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCEPT-01")]
  public async Task AcceptanceDecisionRequiresPartnerGrantForExactClient()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCEPT-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated acceptance client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.AddRange(partner, unrelatedManager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId));
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, unrelatedManager, "Manager", clientId: unrelatedClientId));
      await db.SaveChangesAsync();
    }

    var partnerUrl = await host.StartWebForIdentityAsync(partner);
    var managerUrl = await host.StartWebForIdentityAsync(unrelatedManager);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var partnerContext = await browser.NewContextAsync();
    var partnerPage = await partnerContext.NewPageAsync();
    var partnerConnected = WaitForCircuitConnectionAsync(partnerPage, []);
    await partnerPage.GotoAsync(SignInUrl(partnerUrl, $"/app/assessments/{host.Fixture.ClientId:D}/decision"));
    await partnerPage.Locator("#decision-outcome").WaitForAsync();
    await partnerConnected;
    var decisionCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_ASSESSMENT_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(decisionCaptureDir)) Directory.CreateDirectory(decisionCaptureDir);
    foreach (var width in new[] { 390, 1440, 320, 760, 1024, 1920 })
    {
      await partnerPage.SetViewportSizeAsync(width, 900);
      await partnerPage.WaitForTimeoutAsync(250);
      await partnerPage.Locator("#decision-outcome").WaitForAsync();
      await partnerPage.WaitForFunctionAsync("() => document.documentElement.scrollWidth <= window.innerWidth && (window.innerWidth > 760 || document.querySelector('#main-content').getBoundingClientRect().width >= window.innerWidth - 64)");
      Assert.False(await partnerPage.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"),
        $"Decision form overflows at {width}px.");
      if (decisionCaptureDir is not null && width is 390 or 1440)
        await partnerPage.ScreenshotAsync(new() { Path = Path.Combine(decisionCaptureDir, $"decision-{width}.png"), FullPage = true });
    }

    await using var managerContext = await browser.NewContextAsync();
    var managerPage = await managerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var managerConnected = WaitForCircuitConnectionAsync(managerPage, diagnostics);
    await managerPage.GotoAsync(SignInUrl(managerUrl, $"/app/assessments/{host.Fixture.ClientId:D}/decision"));
    await managerPage.GetByRole(AriaRole.Heading, new() { Name = "Decision unavailable" }).WaitForAsync();
    await managerConnected;
    Assert.False(await managerPage.Locator("#decision-outcome").CountAsync() > 0);
    Assert.False(await managerPage.GetByRole(AriaRole.Button, new() { Name = "Record Partner Decision" }).CountAsync() > 0);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ENGAGEMENT-STALE-ROUTE-01")]
  public async Task EngagementDetailClearsPriorEngagementWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ENGAGEMENT-STALE-ROUTE-01");
    var siblingEngagementId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/engagements/{host.Fixture.EngagementId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement details" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await page.GetByText(host.Fixture.EngagementId.ToString("D")).WaitForAsync(new() { Timeout = 15000 });
    var authorizedBody = await page.Locator("body").InnerTextAsync();
    Assert.Contains(host.Fixture.EngagementId.ToString("D"), authorizedBody);

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{siblingEngagementId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement unavailable" }).WaitForAsync();
    var deniedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(host.Fixture.EngagementId.ToString("D"), deniedBody);
    Assert.DoesNotContain(siblingEngagementId.ToString("D"), deniedBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{host.Fixture.EngagementId:D}");
    await page.GetByText(host.Fixture.EngagementId.ToString("D")).WaitForAsync(new() { Timeout = 15000 });
    var restoredBody = await page.Locator("body").InnerTextAsync();
    Assert.Contains(host.Fixture.EngagementId.ToString("D"), restoredBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-COMP-01")]
  public async Task CompletionChecklistDeniesSiblingEngagementAndKeepsRepresentationPrivate()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-COMP-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateNarrative = "SYNTHETIC-PAR-002-PRIVATE-REPRESENTATION";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated completion client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      db.WrittenRepresentations.Add(new WrittenRepresentation
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, Code = "SYN-R-01", Title = "Synthetic representation",
        Narrative = privateNarrative
      });
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffDiagnostics = new List<string>();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, staffDiagnostics);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/engagements/{host.Fixture.EngagementId:D}/completion"));
    await staffPage.GetByText(privateNarrative).WaitForAsync();
    await staffConnected;

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/engagements/{host.Fixture.EngagementId:D}/completion"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateNarrative, body);
    Assert.DoesNotContain("SYN-R-01", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ENG-01")]
  public async Task EngagementDetailDeniesUnassignedScopeAndClearsOnRouteChange()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ENG-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    var unrelatedEngagementId = Guid.NewGuid();
    const string unrelatedClientName = "SYN-PAR-002-ENG-PRIVATE-CLIENT";
    const string unrelatedServiceRoute = "SYN-PAR-002-ENG-PRIVATE-SERVICE";
    const string privateHoldReason = "SYN-PAR-002-ENG-PRIVATE-HOLD";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = unrelatedClientName, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new Engagement
      {
        Id = unrelatedEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = unrelatedClientId, ServiceRoute = unrelatedServiceRoute,
        Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.EngagementHolds.Add(new EngagementHold
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, EngagementId = host.Fixture.EngagementId,
        HoldKind = "Synthetic review", Reason = privateHoldReason, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffDiagnostics = new List<string>();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, staffDiagnostics);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/engagements/{host.Fixture.EngagementId:D}"));
    await staffPage.GetByText("PBC TEST CLIENT", new() { Exact = true }).First.WaitForAsync(); // also shown in the breadcrumb
    await staffPage.GetByText(privateHoldReason, new() { Exact = true }).WaitForAsync();
    await staffConnected;
    var documentToken = Guid.NewGuid().ToString("N");
    await staffPage.EvaluateAsync("token => window.__engagementRouteTestToken = token", documentToken);
    await staffPage.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{unrelatedEngagementId:D}");
    await staffPage.GetByRole(AriaRole.Heading, new() { Name = "Engagement unavailable" }).WaitForAsync();
    var transitionedBody = await staffPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("PBC TEST CLIENT", transitionedBody);
    Assert.DoesNotContain("Professional Work Blocked", transitionedBody);
    Assert.DoesNotContain(privateHoldReason, transitionedBody);
    Assert.DoesNotContain(unrelatedClientName, transitionedBody);
    Assert.DoesNotContain(unrelatedServiceRoute, transitionedBody);
    Assert.Equal(documentToken, await staffPage.EvaluateAsync<string>("window.__engagementRouteTestToken"));

    await staffPage.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{host.Fixture.EngagementId:D}");
    await staffPage.GetByText("PBC TEST CLIENT", new() { Exact = true }).First.WaitForAsync(); // also shown in the breadcrumb
    await staffPage.GetByText(privateHoldReason, new() { Exact = true }).WaitForAsync();
    Assert.DoesNotContain(staffDiagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/engagements/{host.Fixture.EngagementId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("PBC TEST CLIENT", body);
    Assert.DoesNotContain("Professional Work Blocked", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-AUDIT-PLAN-READ-01")]
  public async Task AuditPlanRequiresExactEngagementGrantAndClearsOnRouteChange()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-AUDIT-PLAN-READ-01");
    const string privateRisk = "SYN-PAR-002-AUDIT-PLAN-PRIVATE-RISK";
    var siblingClientId = Guid.NewGuid();
    var siblingEngagementId = Guid.NewGuid();
    var unrelatedClientId = Guid.NewGuid();
    var unrelatedManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.AddRange(
        new PracticeClient
        {
          Id = siblingClientId, FirmId = host.Fixture.FirmId,
          LegalName = "Synthetic sibling audit-plan client", CreatedAt = DateTimeOffset.UtcNow
        },
        new PracticeClient
        {
          Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
          LegalName = "Synthetic unrelated audit-plan client", CreatedAt = DateTimeOffset.UtcNow
        });
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = siblingClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClientId, FirmId = host.Fixture.FirmId });
      db.Users.Add(unrelatedManager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, unrelatedManager, "Manager",
        clientId: unrelatedClientId));
      db.AuditRisks.Add(new AuditRisk
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id,
        AccountArea = "Synthetic audit area", Assertion = "Completeness", Description = privateRisk,
        Drivers = "Synthetic test driver", Severity = RiskSeverities.Normal,
        SignificanceDecision = SignificanceDecisions.Normal, ResponseDescription = "Synthetic response",
        Status = RiskStatuses.Identified, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using (var staffContext = await browser.NewContextAsync())
    {
      var page = await staffContext.NewPageAsync();
      var diagnostics = new List<string>();
      var connected = WaitForCircuitConnectionAsync(page, diagnostics);
      await page.GotoAsync(SignInUrl(host.StaffUrl,
        $"/app/engagements/{host.Fixture.EngagementId:D}/audit-plan"));
      await page.GetByText(privateRisk, new() { Exact = true }).WaitForAsync();
      await connected;

      var documentToken = Guid.NewGuid().ToString("N");
      await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
      await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
        $"/app/engagements/{siblingEngagementId:D}/audit-plan");
      await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
      var body = await page.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privateRisk, body);
      Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    }

    await using (var revokeContext = await browser.NewContextAsync())
    {
      var revokePage = await revokeContext.NewPageAsync();
      var revokeDiagnostics = new List<string>();
      var revokeConnected = WaitForCircuitConnectionAsync(revokePage, revokeDiagnostics);
      await revokePage.GotoAsync(SignInUrl(host.StaffUrl,
        $"/app/engagements/{host.Fixture.EngagementId:D}/audit-plan"));
      await revokePage.GetByText(privateRisk, new() { Exact = true }).WaitForAsync();
      await revokeConnected;

      const string unpersistedRisk = "SYN-PAR-002-REVOKED-RISK-MUST-NOT-PERSIST";
      var staleActor = PbcSeed.Actor(host.Fixture.Staff, "Staff");
      var riskForm = revokePage.GetByRole(AriaRole.Group, new() { Name = "Record an identified risk" });
      await riskForm.GetByLabel("Account or disclosure area").FillAsync("Synthetic area");
      await riskForm.GetByLabel("Assertion").FillAsync("Completeness");
      await riskForm.GetByLabel("Risk description").FillAsync(unpersistedRisk);
      await riskForm.GetByLabel("Drivers").FillAsync("Synthetic test driver");
      await riskForm.GetByLabel("Planned response").FillAsync("Synthetic response");

      await using (var db = host.CreateDbContext())
      {
        var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == host.Fixture.FirmId &&
          x.UserId == host.Fixture.Staff.Id && x.Role == "Staff" && x.EngagementId == host.Fixture.EngagementId &&
          x.RevokedAt == null);
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
          PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
        Assert.True(revoked.Succeeded, revoked.Message);
        var staleWrite = await AuditPlanningService.CreateAuditRiskAsync(db, staleActor,
          new CreateAuditRiskRequest(host.Fixture.EngagementId, "Synthetic area", "Completeness",
            unpersistedRisk, "Synthetic test driver", SignificanceDecisions.Normal, null, "Synthetic response"));
        Assert.False(staleWrite.Succeeded);
        Assert.Equal(ErrorCodes.GenerationStale, staleWrite.ErrorCode);
      }

      await revokePage.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
        $"/app/engagements/{siblingEngagementId:D}/audit-plan");
      await revokePage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
      var revokedBody = await revokePage.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privateRisk, revokedBody);
      Assert.DoesNotContain(unpersistedRisk, revokedBody);
      Assert.DoesNotContain(revokeDiagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
      await using (var verify = host.CreateDbContext())
        Assert.False(await verify.AuditRisks.AnyAsync(x => x.Description == unpersistedRisk));
    }

    var unrelatedUrl = await host.StartWebForIdentityAsync(unrelatedManager);
    await using var unrelatedContext = await browser.NewContextAsync();
    var unrelatedPage = await unrelatedContext.NewPageAsync();
    var unrelatedDiagnostics = new List<string>();
    var unrelatedConnected = WaitForCircuitConnectionAsync(unrelatedPage, unrelatedDiagnostics);
    await unrelatedPage.GotoAsync(SignInUrl(unrelatedUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-plan"));
    await unrelatedPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    await unrelatedConnected;
    var unrelatedBody = await unrelatedPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateRisk, unrelatedBody);
    Assert.DoesNotContain(unrelatedDiagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-REV-01")]
  public async Task ReviewPointReadAndDispositionAreEngagementScoped()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-REV-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    var point = new ReviewPoint
    {
      Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
      EngagementId = host.Fixture.EngagementId, TargetId = Guid.NewGuid(), TargetKind = "workpaper",
      TargetRevision = 1, Comment = "SYNTHETIC-PAR-002-PRIVATE-REVIEW-COMMENT",
      RaisedByUserId = host.Fixture.Staff.Id, RaisedAt = DateTimeOffset.UtcNow
    };
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated review client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      db.ReviewPoints.Add(point);
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffDiagnostics = new List<string>();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, staffDiagnostics);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/reviews/{point.Id:D}"));
    await staffPage.GetByText(point.Comment).WaitForAsync();
    await staffConnected;
    staffPage.PageError += (_, error) => staffDiagnostics.Add($"page-error: {error}");
    await staffPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var clearButton = staffPage.GetByRole(AriaRole.Button, new() { Name = "Clear Review Point" });
    Assert.True(await clearButton.IsEnabledAsync());
    await clearButton.ClickAsync();
    try { await staffPage.GetByText("Disposition updated successfully.").WaitForAsync(new() { Timeout = 8000 }); }
    catch (TimeoutException ex)
    {
      await using var check = host.CreateDbContext();
      var cleared = await check.ReviewPoints.AsNoTracking().Where(x => x.Id == point.Id).Select(x => x.Cleared).SingleAsync();
      throw new Xunit.Sdk.XunitException($"Review-point disposition did not complete (database cleared={cleared}).\n{await staffPage.Locator("body").InnerTextAsync()}\n{string.Join("\n", staffDiagnostics)}\n{ex.Message}");
    }
    await using (var verify = host.CreateDbContext())
      Assert.True((await verify.ReviewPoints.AsNoTracking().SingleAsync(x => x.Id == point.Id)).Cleared);

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/reviews/{point.Id:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Review point unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(point.Comment, body);
    Assert.DoesNotContain("Review Context", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-REV-STALE-ROUTE-01")]
  public async Task ReviewPointClearsPriorPointOnSameDocumentIdChange()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-REV-STALE-ROUTE-01");
    var otherClientId = Guid.NewGuid();
    var otherEngagementId = Guid.NewGuid();
    var authorizedPoint = new ReviewPoint
    {
      Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
      EngagementId = host.Fixture.EngagementId, TargetId = Guid.NewGuid(), TargetKind = "workpaper",
      TargetRevision = 1, Comment = "SYN-PAR-002-REVIEW-ROUTE-PRIVATE-COMMENT",
      RaisedByUserId = host.Fixture.Staff.Id, RaisedAt = DateTimeOffset.UtcNow, Significant = true
    };
    var unauthorizedPoint = new ReviewPoint
    {
      Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = otherClientId,
      EngagementId = otherEngagementId, TargetId = Guid.NewGuid(), TargetKind = "workpaper",
      TargetRevision = 1, Comment = "SYN-PAR-002-OTHER-CLIENT-REVIEW-COMMENT",
      RaisedByUserId = host.Fixture.Staff.Id, RaisedAt = DateTimeOffset.UtcNow
    };
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = otherClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated review client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new Engagement
      {
        Id = otherEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = otherClientId,
        ServiceRoute = "Synthetic unrelated review engagement", Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.ReviewPoints.AddRange(authorizedPoint, unauthorizedPoint);
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/reviews/{authorizedPoint.Id:D}"));
    await page.GetByText(authorizedPoint.Comment, new() { Exact = true }).WaitForAsync();
    await connected;
    await Assertions.Expect(page.GetByText("This significant review point remains open and blocks the engagement completion gate.")).ToBeVisibleAsync();
    var reviewCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_DETAIL_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(reviewCaptureDir)) Directory.CreateDirectory(reviewCaptureDir);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("expected => document.documentElement.clientWidth === expected && (expected >= 960 || (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px'))", width);
      await Assertions.Expect(page.GetByText(authorizedPoint.Comment, new() { Exact = true })).ToBeVisibleAsync();
      var documentWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
      Assert.True(documentWidth <= width + 1, $"Review point document is {documentWidth}px wide at {width}px viewport.");
      if (reviewCaptureDir is not null && width is 390 or 1440)
        await page.ScreenshotAsync(new() { Path = Path.Combine(reviewCaptureDir, $"review-{width}.png"), FullPage = true });
    }
    await page.Locator("[aria-label='Review point navigation']").GetByRole(AriaRole.Link, new() { Name = "Engagement" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    var completionLink = page.GetByRole(AriaRole.Link, new() { Name = "Completion checklist" });
    await Assertions.Expect(completionLink).ToBeFocusedAsync();
    await Assertions.Expect(completionLink).ToHaveCSSAsync("outline-style", "solid");

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__reviewPointRouteToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/reviews/{unauthorizedPoint.Id:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Review point unavailable" })
      .WaitForAsync(new() { Timeout = 5000 });
    var deniedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(authorizedPoint.Comment, deniedBody);
    Assert.DoesNotContain(unauthorizedPoint.Comment, deniedBody);
    Assert.DoesNotContain("Review Context", deniedBody);
    Assert.DoesNotContain("Clear Review Point", deniedBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__reviewPointRouteToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/reviews/{authorizedPoint.Id:D}");
    await page.GetByText(authorizedPoint.Comment, new() { Exact = true }).WaitForAsync();
    var restoredBody = await page.Locator("body").InnerTextAsync();
    Assert.Contains("BLOCKING", restoredBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__reviewPointRouteToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ARCH-STALE-ROUTE-01")]
  public async Task RecordsArchiveClearsPriorManifestOnUnauthorizedRouteChange()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ARCH-STALE-ROUTE-01");
    var otherClientId = Guid.NewGuid();
    var otherEngagementId = Guid.NewGuid();
    var archive = new Archive
    {
      Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
      EngagementId = host.Fixture.EngagementId, ProfileId = "SYN-PAR-002-PRIVATE-ARCHIVE-PROFILE",
      ProfileVersion = 1, Status = ArchiveStates.Issued, CreatedAt = DateTimeOffset.UtcNow
    };
    var manifest = new ArchiveManifest
    {
      Id = Guid.NewGuid(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, ArchiveId = archive.Id, Version = 1, Status = "BUILT",
      ManifestDigest = new string('a', 64), EntryCount = 1, CompletenessStatus = "COMPLETE",
      BuiltAt = DateTimeOffset.UtcNow
    };
    var entry = new ArchiveManifestEntry
    {
      Id = Guid.NewGuid(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, ArchiveManifestId = manifest.Id, Ordinal = 1,
      EntryKind = "WORKPAPER", SourceKind = "SYNTHETIC", RelativeName = "SYN-PAR-002-PRIVATE-ARCHIVE-FILE",
      ContentHash = new string('b', 64), ByteCount = 32
    };
    var otherArchive = new Archive
    {
      Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = otherClientId,
      EngagementId = otherEngagementId, ProfileId = "SYN-PAR-002-UNAUTHORIZED-ARCHIVE-PROFILE",
      ProfileVersion = 1, Status = ArchiveStates.Issued, CreatedAt = DateTimeOffset.UtcNow
    };
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = otherClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated archive client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new Engagement
      {
        Id = otherEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = otherClientId,
        ServiceRoute = "Synthetic unrelated archive engagement", Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Archives.AddRange(archive, otherArchive);
      db.ArchiveManifests.Add(manifest);
      db.ArchiveManifestEntries.Add(entry);
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/records/archives/{archive.Id:D}"));
    await connected;
    try { await page.GetByText(archive.ProfileId, new() { Exact = false }).WaitForAsync(new() { Timeout = 5000 }); }
    catch (TimeoutException ex)
    {
      throw new Xunit.Sdk.XunitException($"Authorized synthetic archive did not load.\n{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}\n{ex.Message}");
    }
    await page.GetByText(entry.RelativeName, new() { Exact = true }).WaitForAsync();
    var archiveSummary = page.Locator("[aria-label='Scoped archive summary']");
    await Assertions.Expect(archiveSummary).ToContainTextAsync("Manifest entries");
    await Assertions.Expect(archiveSummary.GetByText("1", new() { Exact = true })).ToHaveCountAsync(2);
    var archiveCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_RELEASE_ARCHIVE_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(archiveCaptureDir)) Directory.CreateDirectory(archiveCaptureDir);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("expected => document.documentElement.clientWidth === expected && (expected >= 960 || (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px'))", width);
      await page.GetByText(entry.RelativeName, new() { Exact = true }).WaitForAsync();
      var documentWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
      Assert.True(documentWidth <= width + 1, $"Archive document is {documentWidth}px wide at {width}px viewport.");
      if (archiveCaptureDir is not null && width is 390 or 1440)
        await page.ScreenshotAsync(new() { Path = Path.Combine(archiveCaptureDir, $"archive-{width}.png"), FullPage = true });
    }
    await page.SetViewportSizeAsync(1440, 900);
    await page.Locator(".breadcrumb-nav").GetByRole(AriaRole.Link, new() { Name = "Portfolio" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    var engagementLink = page.Locator("[aria-label='Archive navigation']").GetByRole(AriaRole.Link, new() { Name = "Engagement" });
    await Assertions.Expect(engagementLink).ToBeFocusedAsync();
    await Assertions.Expect(engagementLink).ToHaveCSSAsync("outline-style", "solid");

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__recordsArchiveRouteToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/records/archives/{otherArchive.Id:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Archive unavailable" })
      .WaitForAsync(new() { Timeout = 5000 });
    var deniedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(archive.ProfileId, deniedBody);
    Assert.DoesNotContain(entry.RelativeName, deniedBody);
    Assert.DoesNotContain(manifest.ManifestDigest, deniedBody);
    Assert.DoesNotContain("Archive Manifest", deniedBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__recordsArchiveRouteToken"));

    var deniedMessage = await page.GetByRole(AriaRole.Alert).InnerTextAsync();
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/records/archives/{Guid.NewGuid():D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Archive unavailable" }).WaitForAsync();
    Assert.Equal(deniedMessage, await page.GetByRole(AriaRole.Alert).InnerTextAsync());
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__recordsArchiveRouteToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/records/archives/{archive.Id:D}");
    await page.GetByText(archive.ProfileId, new() { Exact = false }).WaitForAsync();
    await page.GetByText(entry.RelativeName, new() { Exact = true }).WaitForAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__recordsArchiveRouteToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FIND-01")]
  public async Task FindingDetailsRequireCurrentClientAndEngagementScope()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-FIND-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateImpact = "SYNTHETIC-PAR-002-PRIVATE-FINDING-IMPACT";
    const string privateResponse = "SYNTHETIC-PAR-002-PRIVATE-FINDING-RESPONSE";
    Guid findingId;
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated finding client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      var created = await AuditPlanningService.CreateFindingAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"),
        new CreateFindingRequest(host.Fixture.EngagementId, "Synthetic scoped finding", privateImpact,
          false, 125m, null));
      Assert.True(created.Succeeded, created.Message);
      findingId = created.Value!.FindingId;
      var response = await AuditPlanningService.RecordFindingResponseAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"),
        new RecordFindingResponseRequest(findingId, privateResponse, false));
      Assert.True(response.Succeeded, response.Message);
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, []);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/findings/{findingId:D}"));
    await staffPage.GetByText(privateImpact).WaitForAsync();
    await Assertions.Expect(staffPage.Locator("blockquote.management-response")).ToContainTextAsync(privateResponse);
    await staffConnected;

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/findings/{findingId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Finding unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateImpact, body);
    Assert.DoesNotContain(privateResponse, body);
    Assert.DoesNotContain("125.00", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-POP-01")]
  public async Task AuditPopulationAndLinkedReceiptsRequireCurrentEngagementScope()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-POP-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privatePurpose = "SYNTHETIC-PAR-002-PRIVATE-POPULATION-PURPOSE";
    const string privateParameters = "SYNTHETIC-PAR-002-PRIVATE-EXTRACTION-PARAMETERS";
    const string privateReceiptToken = "SYNTHETIC-PAR-002-PRIVATE-RECEIPT-TOKEN";
    const string privateFileName = "SYNTHETIC-PAR-002-PRIVATE-SOURCE.csv";
    Guid populationId;
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated population client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      var created = await AuditPlanningService.CreatePopulationVersionAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"),
        new CreatePopulationRequest(host.Fixture.EngagementId, privatePurpose, "Existence",
          "Synthetic source receipt", privateParameters, 3, 98765.43m, "QAR", null));
      Assert.True(created.Succeeded, created.Message);
      populationId = created.Value!.PopulationId;

      var receiptId = Guid.NewGuid();
      db.SourceReceipts.Add(new SourceReceipt
      {
        Id = receiptId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, SourceType = "CSV_IMPORT", ReceiptToken = privateReceiptToken,
        Sha256Digest = new string('a', 64), ByteCount = 3, OriginalFileName = privateFileName,
        AcquiredAt = DateTimeOffset.UtcNow, AcquiredByUserId = host.Fixture.Staff.Id
      });
      db.EvidenceLinks.Add(new EvidenceLink
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, SourceReceiptId = receiptId,
        Purpose = "SYNTHETIC-PAR-002-PRIVATE-EVIDENCE-PURPOSE", Assertion = "Existence",
        RelevanceReliabilityAssessment = "Synthetic evidence scope fixture", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, []);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/populations/{populationId:D}"));
    await staffPage.GetByText(privatePurpose).WaitForAsync();
    await Assertions.Expect(staffPage.GetByText(privateReceiptToken)).ToBeVisibleAsync();
    await staffConnected;

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/audit/populations/{populationId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Population unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privatePurpose, body);
    Assert.DoesNotContain(privateParameters, body);
    Assert.DoesNotContain(privateReceiptToken, body);
    Assert.DoesNotContain(privateFileName, body);
    Assert.DoesNotContain("98765.43", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-WP-01")]
  public async Task WorkpaperAndSubmissionHistoryRequireCurrentEngagementScope()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-WP-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateTitle = "SYNTHETIC-PAR-002-PRIVATE-WORKPAPER-TITLE";
    const string privateObjective = "SYNTHETIC-PAR-002-PRIVATE-WORKPAPER-OBJECTIVE";
    const string privateProcedure = "SYNTHETIC-PAR-002-PRIVATE-WORKPAPER-PROCEDURE";
    const string privateSnapshotConclusion = "SYNTHETIC-PAR-002-PRIVATE-SUBMISSION-CONCLUSION";
    Guid workpaperId;
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated workpaper client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      var created = await AuditPlanningService.CreateWorkpaperAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"),
        new CreateWorkpaperRequest(host.Fixture.EngagementId, "SYN-WP-01", privateTitle,
          privateObjective, "SYNTHETIC-TEMPLATE-v1", null, privateProcedure));
      Assert.True(created.Succeeded, created.Message);
      workpaperId = created.Value!.WorkpaperId;
      db.WorkpaperSubmissions.Add(new WorkpaperSubmission
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, WorkpaperId = workpaperId,
        ActorId = host.Fixture.Staff.Id, Revision = 2, WorkPerformed = "Synthetic frozen snapshot",
        Conclusion = privateSnapshotConclusion, SubmittedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, []);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/workpapers/{workpaperId:D}"));
    await staffPage.GetByText(privateObjective).WaitForAsync();
    await Assertions.Expect(staffPage.GetByText(privateProcedure)).ToBeVisibleAsync();
    await Assertions.Expect(staffPage.GetByText(privateSnapshotConclusion)).ToBeVisibleAsync();
    await staffConnected;
    await using (var db = host.CreateDbContext())
    {
      var changed = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId &&
          x.UserId == host.Fixture.Staff.Id && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      Assert.Equal(1, changed);
    }
    await staffPage.Locator("#wp-work").FillAsync("SYNTHETIC-PAR-002-WORK-AFTER-REVOCATION");
    await staffPage.GetByRole(AriaRole.Heading, new() { Name = "Workpaper unavailable" }).WaitForAsync();
    var revokedBody = await staffPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateTitle, revokedBody);
    Assert.DoesNotContain(privateObjective, revokedBody);
    Assert.DoesNotContain(privateSnapshotConclusion, revokedBody);
    Assert.DoesNotContain("SYNTHETIC-PAR-002-WORK-AFTER-REVOCATION", revokedBody);
    await using (var verify = host.CreateDbContext())
      Assert.False(await verify.WorkpaperDrafts.AnyAsync(x => x.WorkpaperId == workpaperId));

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/audit/workpapers/{workpaperId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateTitle, body);
    Assert.DoesNotContain(privateObjective, body);
    Assert.DoesNotContain(privateProcedure, body);
    Assert.DoesNotContain(privateSnapshotConclusion, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-CLIENT-01")]
  public async Task ClientProfileRequiresCoveringClientGrant()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-CLIENT-01");
    var unrelatedManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateContact = "SYNTHETIC-PAR-002-PRIVATE-CONTACT";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated profile client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(unrelatedManager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, unrelatedManager, "Manager", clientId: unrelatedClientId));
      db.ClientContacts.Add(new ClientContact
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        FullName = privateContact, Email = "private-contact@example.test", Role = "Synthetic CFO",
        ValidFrom = DateTimeOffset.UtcNow, Primary = true
      });
      await db.SaveChangesAsync();
    }

    var adminUrl = await host.StartWebForIdentityAsync(host.Fixture.Admin);
    var unrelatedUrl = await host.StartWebForIdentityAsync(unrelatedManager);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var adminContext = await browser.NewContextAsync();
    var adminPage = await adminContext.NewPageAsync();
    var adminConnected = WaitForCircuitConnectionAsync(adminPage, []);
    await adminPage.GotoAsync(SignInUrl(adminUrl, $"/app/clients/{host.Fixture.ClientId:D}"));
    await adminPage.GetByText(privateContact, new() { Exact = true }).WaitForAsync();
    await adminConnected;

    foreach (var origin in new[] { host.StaffUrl, unrelatedUrl })
    {
      await using var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      var diagnostics = new List<string>();
      var connected = WaitForCircuitConnectionAsync(page, diagnostics);
      await page.GotoAsync(SignInUrl(origin, $"/app/clients/{host.Fixture.ClientId:D}"));
      await page.GetByRole(AriaRole.Heading, new() { Name = "Client unavailable" }).WaitForAsync();
      await connected;
      var body = await page.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privateContact, body);
      Assert.DoesNotContain("private-contact@example.test", body);
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-CLIENT-STALE-ROUTE-01")]
  public async Task ClientProfileClearsPriorClientOnUnauthorizedSameDocumentRouteChange()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-CLIENT-STALE-ROUTE-01");
    var manager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateClientName = "SYNTHETIC-PAR-002-PRIVATE-CLIENT-A";
    const string privateContact = "SYNTHETIC-PAR-002-PRIVATE-CONTACT-A";
    const string privateEngagement = "SYNTHETIC-PAR-002-PRIVATE-ENGAGEMENT-A";
    const string unrelatedClientName = "SYNTHETIC-PAR-002-UNRELATED-CLIENT-B";
    await using (var db = host.CreateDbContext())
    {
      var client = await db.PracticeClients.SingleAsync(x => x.Id == host.Fixture.ClientId);
      client.LegalName = privateClientName;
      client.RegistrationNumber = "SYNTHETIC-REGISTRATION-A";
      var engagement = await db.Engagements.SingleAsync(x => x.Id == host.Fixture.EngagementId);
      engagement.ServiceRoute = privateEngagement;
      db.ClientContacts.Add(new ClientContact
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PracticeClientId = client.Id,
        FullName = privateContact, Email = "private-contact-a@synthetic.test", Role = "Synthetic CFO",
        ValidFrom = DateTimeOffset.UtcNow, Primary = true
      });
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = unrelatedClientName, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new Engagement
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PracticeClientId = unrelatedClientId,
        ServiceRoute = "SYNTHETIC-PAR-002-UNRELATED-ENGAGEMENT-B", Status = "Active",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(manager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, manager, "Manager",
        clientId: host.Fixture.ClientId));
      await db.SaveChangesAsync();
    }

    var managerUrl = await host.StartWebForIdentityAsync(manager);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(managerUrl, $"/app/clients/{host.Fixture.ClientId:D}"));
    await connected;
    try
    {
      await page.GetByText(privateContact, new() { Exact = true }).WaitForAsync(new() { Timeout = 5000 });
    }
    catch (TimeoutException ex)
    {
      throw new Xunit.Sdk.XunitException($"Authorized client profile did not load.\n{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}\n{ex.Message}");
    }
    await page.GetByText(privateEngagement, new() { Exact = true }).WaitForAsync();
    await page.GetByText(privateClientName, new() { Exact = true }).WaitForAsync();

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__clientDetailRouteToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/clients/{unrelatedClientId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Client unavailable" })
      .WaitForAsync(new() { Timeout = 5000 });
    var deniedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateClientName, deniedBody);
    Assert.DoesNotContain("SYNTHETIC-REGISTRATION-A", deniedBody);
    Assert.DoesNotContain(privateContact, deniedBody);
    Assert.DoesNotContain("private-contact-a@synthetic.test", deniedBody);
    Assert.DoesNotContain(privateEngagement, deniedBody);
    Assert.DoesNotContain("SYNTHETIC-PAR-002-UNRELATED-ENGAGEMENT-B", deniedBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__clientDetailRouteToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/clients/{host.Fixture.ClientId:D}");
    await page.GetByText(privateContact, new() { Exact = true }).WaitForAsync();
    await page.GetByText(privateEngagement, new() { Exact = true }).WaitForAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__clientDetailRouteToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-REV-REVOKE-01")]
  public async Task ReviewPointClearsProtectedStateWhenCurrentGrantIsRevoked()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-REV-REVOKE-01");
    var reviewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    const string privateComment = "SYN-PAR-002-REVOKED-REVIEW-COMMENT";
    var point = new ReviewPoint
    {
      Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
      EngagementId = host.Fixture.EngagementId, TargetId = Guid.NewGuid(), TargetKind = "workpaper",
      TargetRevision = 1, Comment = privateComment, RaisedByUserId = host.Fixture.Staff.Id,
      RaisedAt = DateTimeOffset.UtcNow, Significant = true
    };
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(reviewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, reviewer, "Auditor",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.ReviewPoints.Add(point);
      await db.SaveChangesAsync();
    }

    var reviewerUrl = await host.StartWebForIdentityAsync(reviewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(reviewerUrl, $"/app/reviews/{point.Id:D}"));
    await page.GetByText(privateComment, new() { Exact = true }).WaitForAsync();
    await connected;
    await using (var db = host.CreateDbContext())
    {
      var revoked = await db.RoleGrants.Where(x => x.UserId == reviewer.Id &&
          x.Role == "Auditor" && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      Assert.Equal(1, revoked);
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Clear Review Point" }).ClickAsync(new() { Force = true });
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateComment, body);
    Assert.DoesNotContain("BLOCKING", body);
    Assert.DoesNotContain("Clear Review Point", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    await using var verify = host.CreateDbContext();
    Assert.False((await verify.ReviewPoints.AsNoTracking().SingleAsync(x => x.Id == point.Id)).Cleared);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-CLIENT-PORTAL-READ-01")]
  public async Task ClientPortalHidesPbcRequestsOutsideAssignedEngagement()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-CLIENT-PORTAL-READ-01");
    const string siblingMarker = "SYN-PAR-002-PRIVATE-SIBLING-REQUEST";
    await using (var db = host.CreateDbContext())
    {
      var now = DateTimeOffset.UtcNow;
      var siblingEngagementId = Guid.NewGuid();
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        ServiceRoute = "SYNTHETIC-SIBLING-SERVICE", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.PbcRequests.Add(new PbcRequest
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = siblingEngagementId, Objective = siblingMarker, EntityScope = "Synthetic sibling",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Area = siblingMarker,
        RequestedFormat = "PDF", ClientOwnerUserId = host.Fixture.Client.Id,
        FirmOwnerUserId = host.Fixture.Staff.Id, ReviewerUserId = host.Fixture.Reviewer.Id,
        DueDate = "2026-10-01", Confidentiality = "Confidential", AcceptanceCriteria = siblingMarker,
        State = PbcStates.Sent, CreatedAt = now, CreatedByUserId = host.Fixture.Staff.Id, UpdatedAt = now
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.ClientUrl, "/portal"));
    await page.GetByText("Cash", new() { Exact = true }).WaitForAsync();
    await connected;

    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(siblingMarker, body);
    var documentToken = await page.EvaluateAsync<string>("window.__clientPortalRefreshToken = crypto.randomUUID()");
    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == host.Fixture.Client.Id && x.Role == "ClientUser" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    // Revocation now clears the entire protected shell without a refresh action.
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true }).WaitForAsync(new() { Timeout = 15000 });
    body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Cash", body);
    Assert.DoesNotContain(siblingMarker, body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__clientPortalRefreshToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-CLIENT-PBC-STALE-ROUTE-01")]
  public async Task ClientPbcRequestClearsThreadWhenNavigatingToAnotherRecipientsRequest()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-CLIENT-PBC-STALE-ROUTE-01");
    const string privateMarker = "SYN-PAR-002-CLIENT-PBC-ROUTE-PRIVATE";
    const string otherRecipientMarker = "SYN-PAR-002-CLIENT-PBC-ROUTE-OTHER-RECIPIENT";
    var otherClient = PbcSeed.User(host.Fixture.FirmId, "Client");
    var otherRequestId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      var now = DateTimeOffset.UtcNow;
      var currentRequest = await db.PbcRequests.SingleAsync(x => x.Id == host.RequestId);
      currentRequest.Objective = privateMarker;
      currentRequest.Area = privateMarker;
      db.Users.Add(otherClient);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, otherClient, "ClientUser",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.PbcRequests.Add(new PbcRequest
      {
        Id = otherRequestId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, Objective = otherRecipientMarker,
        EntityScope = "Synthetic recipient scope", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Area = otherRecipientMarker, RequestedFormat = "PDF", ClientOwnerUserId = otherClient.Id,
        FirmOwnerUserId = host.Fixture.Staff.Id, ReviewerUserId = host.Fixture.Reviewer.Id,
        DueDate = "2026-10-01", Confidentiality = "Confidential",
        AcceptanceCriteria = "Synthetic test only", State = PbcStates.Sent,
        CreatedAt = now, CreatedByUserId = host.Fixture.Staff.Id, UpdatedAt = now
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.ClientUrl, $"/portal/requests/{host.RequestId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = privateMarker }).WaitForAsync();
    await connected;
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.GetByRole(AriaRole.Heading, new() { Name = privateMarker }).WaitForAsync();
      await page.WaitForFunctionAsync("() => { const b = [...document.querySelectorAll('button')].find(x => x.textContent?.includes('Refresh request')); return !!b && !b.disabled && !!document.getElementById('request-heading'); }");
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
        $"Client request overflows the {width}px viewport: " + await page.EvaluateAsync<string>("() => JSON.stringify({scroll:document.documentElement.scrollWidth, offenders:[...document.querySelectorAll('body *')].filter(e => {const r=e.getBoundingClientRect(); return r.right > innerWidth + 1 && r.width > 0 && getComputedStyle(e).position !== 'fixed';}).slice(0,12).map(e => ({tag:e.tagName,class:e.className?.toString().slice(0,70),right:Math.round(e.getBoundingClientRect().right),width:Math.round(e.getBoundingClientRect().width)}))})"));
      if (width == 1440 && Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } captureDir)
      {
        Directory.CreateDirectory(captureDir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"client-request-{width}.png"), FullPage = true });
      }
    }
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_UI_CAPTURE_DIR") is { Length: > 0 } mobileCaptureDir)
    {
      await page.SetViewportSizeAsync(390, 900);
      await page.GetByRole(AriaRole.Heading, new() { Name = privateMarker }).WaitForAsync();
      await page.WaitForFunctionAsync("() => !!document.getElementById('request-heading') && !document.body.innerText.includes('Loading the request')");
      Directory.CreateDirectory(mobileCaptureDir);
      await page.ScreenshotAsync(new() { Path = Path.Combine(mobileCaptureDir, "client-request-390.png"), FullPage = true });
    }

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/portal/requests/{otherRequestId:D}");
    try
    {
      await page.GetByRole(AriaRole.Heading, new() { Name = "Request unavailable" }).WaitForAsync(new() { Timeout = 5000 });
    }
    catch (TimeoutException ex)
    {
      throw new Xunit.Sdk.XunitException($"Recipient change did not clear the prior PBC thread. URL={page.Url}\n{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}\n{ex.Message}");
    }
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateMarker, body);
    Assert.DoesNotContain(otherRecipientMarker, body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/portal/requests/{host.RequestId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = privateMarker }).WaitForAsync();
    const string privateDraft = "SYN-PAR-002-CLIENT-PBC-REVOKED-DRAFT";
    await page.GetByLabel("Reply to the assigned auditor or accountant").FillAsync(privateDraft);
    var storageKey = $"auditsphere:draft:v1:{Uri.EscapeDataString($"pbc-client-reply-{host.RequestId}")}";
    await page.WaitForFunctionAsync("key => localStorage.getItem(key) !== null", storageKey);
    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == host.Fixture.Client.Id && x.Role == "ClientUser" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    await page.EvaluateAsync("() => { const button = [...document.querySelectorAll('button')].find(x => x.textContent?.trim() === 'Refresh request'); button?.click(); }");
    await page.GetByRole(AriaRole.Heading, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Request unavailable|Access unavailable)$") }).WaitForAsync();
    body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateMarker, body);
    Assert.DoesNotContain(privateDraft, body);
    Assert.Null(await page.EvaluateAsync<string?>("key => localStorage.getItem(key)", storageKey));
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-PBC-ROLE-READ-01")]
  public async Task PbcInboxRequiresAllowedRoleGrantForTheTargetEngagement()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-PBC-ROLE-READ-01");
    const string privateMarker = "SYN-PAR-002-PBC-ROLE-COMPOSITION-PRIVATE";
    var identity = PbcSeed.User(host.Fixture.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      var request = await db.PbcRequests.SingleAsync(x => x.Id == host.RequestId);
      request.Objective = privateMarker;
      request.Area = privateMarker;

      var siblingEngagementId = Guid.NewGuid();
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, ServiceRoute = "SYNTHETIC-SIBLING",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = "Active",
        ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(identity);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, identity, "Staff", host.Fixture.ClientId, siblingEngagementId),
        PbcSeed.Grant(host.Fixture.FirmId, identity, "FinanceManager",
          host.Fixture.ClientId, host.Fixture.EngagementId));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(identity);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, $"/app/engagements/{host.Fixture.EngagementId:D}/pbc"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Prepared-by-client requests" }).WaitForAsync();
    await connected;

    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains("Access unavailable", body);
    Assert.DoesNotContain(privateMarker, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-PBC-STALE-READ-01")]
  public async Task RevokedStaffGrantClearsOpenPbcInboxAfterNextCommand()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-PBC-STALE-READ-01");
    const string privateMarker = "SYN-PAR-002-STALE-PBC-PRIVATE";
    const string privateDraft = "SYN-PAR-002-STALE-PBC-DRAFT";
    await using (var db = host.CreateDbContext())
    {
      var request = await db.PbcRequests.SingleAsync(x => x.Id == host.RequestId);
      request.Objective = privateMarker;
      request.Area = privateMarker;
      await db.SaveChangesAsync();
    }
    var stagedBytes = "SYNTHETIC-PBC-REVOCATION-FIXTURE"u8.ToArray();
    var stagedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stagedBytes)).ToLowerInvariant();
    var stagedActor = PbcSeed.Actor(host.Fixture.Client, "ClientUser");
    Guid uploadId;
    var chunkPath = Path.Combine(host.StagingRoot, "synthetic-revocation-chunk.part");
    Directory.CreateDirectory(host.StagingRoot);
    await File.WriteAllBytesAsync(chunkPath, stagedBytes);
    await using (var db = host.CreateDbContext())
    {
      var started = await PbcService.StartUploadAsync(db, stagedActor,
        new StartPbcUploadRequest(host.RequestId, "AuditSphere-Synthetic-Revocation.txt", "text/plain",
          stagedBytes.Length, stagedHash));
      Assert.True(started.Succeeded, started.Message);
      uploadId = started.Value!.UploadIntentId;
      var recorded = await PbcService.RecordChunkAsync(db, stagedActor,
        new RecordPbcUploadChunkRequest(uploadId, 0, 0, stagedBytes.Length, stagedHash,
          started.Value.Capability!, chunkPath));
      Assert.True(recorded.Succeeded, recorded.Message);
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/engagements/{host.Fixture.EngagementId:D}/pbc"));
    await page.GetByRole(AriaRole.Heading, new() { Name = privateMarker }).WaitForAsync();
    await connected;

    await page.GetByLabel("Files required").FillAsync(privateDraft);
    var scope = $"pbc-new-request-{host.Fixture.EngagementId}";
    var storageKey = $"auditsphere:draft:v1:{Uri.EscapeDataString(scope)}";
    await page.WaitForFunctionAsync("key => localStorage.getItem(key) !== null", storageKey);
    Assert.Contains(privateDraft, await page.EvaluateAsync<string?>("key => localStorage.getItem(key)", storageKey));

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == host.Fixture.Staff.Id && x.Role == "Staff" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    await page.EvaluateAsync("() => { const button = [...document.querySelectorAll('button')].find(x => x.textContent?.trim() === 'Complete staged transfer'); button?.click(); }");
    try { await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync(new() { Timeout = 5000 }); }
    catch (TimeoutException ex)
    {
      throw new Xunit.Sdk.XunitException($"Revoked inbox remained rendered.\n{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}\n{ex.Message}");
    }
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateMarker, body);
    Assert.DoesNotContain(privateDraft, body);
    Assert.Null(await page.EvaluateAsync<string?>("key => localStorage.getItem(key)", storageKey));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    await using var verify = host.CreateDbContext();
    Assert.Single(await verify.PbcRequests.AsNoTracking().Where(x => x.FirmId == host.Fixture.FirmId).ToListAsync());
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-PBC-STALE-ROUTE-01")]
  public async Task PbcInboxClearsRenderedStateWhenNavigatingToUnauthorizedSiblingEngagement()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-PBC-STALE-ROUTE-01");
    const string privateMarker = "SYN-PAR-002-PBC-ROUTE-PRIVATE";
    var siblingEngagementId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      var now = DateTimeOffset.UtcNow;
      var currentRequest = await db.PbcRequests.SingleAsync(x => x.Id == host.RequestId);
      currentRequest.Objective = privateMarker;
      currentRequest.Area = privateMarker;
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, ServiceRoute = "SYNTHETIC-SIBLING",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = "Active",
        ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.PbcRequests.Add(new PbcRequest
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = siblingEngagementId, Objective = "SYN-PAR-002-SIBLING-REQUEST",
        EntityScope = "Synthetic sibling", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Area = "SYN-PAR-002-SIBLING-REQUEST", RequestedFormat = "PDF",
        ClientOwnerUserId = host.Fixture.Client.Id, FirmOwnerUserId = host.Fixture.Staff.Id,
        ReviewerUserId = host.Fixture.Reviewer.Id, DueDate = "2026-10-01",
        Confidentiality = "Confidential", AcceptanceCriteria = "Synthetic test only",
        State = PbcStates.Sent, CreatedAt = now, CreatedByUserId = host.Fixture.Staff.Id,
        UpdatedAt = now
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/engagements/{host.Fixture.EngagementId:D}/pbc"));
    await page.GetByRole(AriaRole.Heading, new() { Name = privateMarker }).WaitForAsync();
    await connected;

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{siblingEngagementId:D}/pbc");
    try
    {
      await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    }
    catch (TimeoutException ex)
    {
      throw new Xunit.Sdk.XunitException($"Sibling route did not clear the inbox. URL={page.Url}\n{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}\n{ex.Message}");
    }
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateMarker, body);
    Assert.DoesNotContain("SYN-PAR-002-SIBLING-REQUEST", body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-PBC-01")]
  public async Task RevokedClientGrantClearsOpenRequestAfterNextCommand()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-PBC-01");
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);

    await page.GotoAsync(SignInUrl(host.ClientUrl, $"/portal/requests/{host.RequestId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "PBC request" }).WaitForAsync();
    await connected;
    Assert.Contains("Bank statements", await page.Locator("body").InnerTextAsync());
    await page.GetByLabel("Reply to the assigned auditor or accountant").FillAsync("Synthetic reply after revocation");

    await using (var db = host.CreateDbContext())
    {
      var revokedAt = DateTimeOffset.UtcNow;
      var changed = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId &&
          x.UserId == host.Fixture.Client.Id && x.Role == "ClientUser" && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, revokedAt));
      Assert.Equal(1, changed);
    }

    Assert.Contains("Bank statements", await page.Locator("body").InnerTextAsync());
    try
    {
      await page.GetByRole(AriaRole.Button, new() { Name = "Send reply" }).ClickAsync(new() { Timeout = 10000 });
    }
    catch (TimeoutException ex)
    {
      if (!await page.GetByRole(AriaRole.Heading, new() { Name = "Request unavailable" }).IsVisibleAsync())
        throw new Xunit.Sdk.XunitException($"Reply action did not complete.\n{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}\n{ex.Message}");
    }
    await page.GetByRole(AriaRole.Heading, new() { Name = "Request unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Bank statements", body);
    Assert.DoesNotContain("Synthetic reply after revocation", body);
    Assert.DoesNotContain("Cash", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    await using var verify = host.CreateDbContext();
    Assert.False(await verify.PbcCommunications.AnyAsync(x => x.FirmId == host.Fixture.FirmId &&
      x.PbcRequestId == host.RequestId && x.Body == "Synthetic reply after revocation"));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-CLIENT-PBC-DRAFT-01")]
  public async Task ClientCannotSeeOwnUnsentDraftPbcRequest()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-CLIENT-PBC-DRAFT-01");
    const string draftMarker = "SYN-PAR-002-UNSENT-CLIENT-DRAFT";
    var draftId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      var now = DateTimeOffset.UtcNow;
      db.PbcRequests.Add(new PbcRequest
      {
        Id = draftId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, Objective = draftMarker, EntityScope = "Synthetic client",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Area = draftMarker,
        RequestedFormat = "PDF", ClientOwnerUserId = host.Fixture.Client.Id,
        FirmOwnerUserId = host.Fixture.Staff.Id, ReviewerUserId = host.Fixture.Reviewer.Id,
        DueDate = "2026-10-01", Confidentiality = "Confidential", AcceptanceCriteria = draftMarker,
        State = PbcStates.Draft, CreatedAt = now, CreatedByUserId = host.Fixture.Staff.Id, UpdatedAt = now
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.ClientUrl, "/portal"));
    await page.GetByText("Cash", new() { Exact = true }).WaitForAsync();
    await connected;
    Assert.DoesNotContain(draftMarker, await page.Locator("body").InnerTextAsync());

    await page.GotoAsync($"{host.ClientUrl}/portal/requests/{draftId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Request unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(draftMarker, body);
    Assert.DoesNotContain("Prepare a controlled upload", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-02")]
  public async Task UnrelatedClientCannotViewSiblingPbcRequestOrItsDescription()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-02");
    var unrelatedClientUrl = await host.StartUnrelatedClientWebAsync();
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);

    await page.GotoAsync(SignInUrl(unrelatedClientUrl, $"/portal/requests/{host.RequestId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Request unavailable" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains("This request is not available in the current client scope.", body);
    Assert.DoesNotContain("Bank statements", body);
    Assert.DoesNotContain("Cash", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  /// <summary>
  /// Selects the AuditSphere Roles tab once the interactive render is live. A click on the static
  /// prerender is ignored, so the click is retried until the expected element appears.
  /// </summary>
  private static async Task OpenRolesTabAsync(IPage page, ILocator expected)
  {
    for (var attempt = 0; attempt < 10; attempt++)
    {
      await page.GetByRole(AriaRole.Tab, new() { Name = "AuditSphere Roles" }).ClickAsync();
      try { await expected.WaitForAsync(new() { Timeout = 3000 }); return; }
      catch (TimeoutException) { }
    }
    await expected.WaitForAsync();
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
