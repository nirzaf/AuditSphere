using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditWorkflow")]
public sealed class AuditAndReleaseJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-COMPLETION-STALE-ROUTE-01")]
  public async Task CompletionClearsPriorEngagementWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-COMPLETION-STALE-ROUTE-01");
    var (packageId, _) = await FinancialArtifactJourneyTests.CreatePackageAsync(host);
    var unauthorizedEngagementId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new Engagement
      {
        Id = unauthorizedEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.WrittenRepresentations.Add(new WrittenRepresentation
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, Code = "SYN-LONG-01", Title = "Synthetic representation",
        Narrative = "Synthetic completion narrative with a long unbroken source reference " + new string('R', 120)
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/completion"));
    await page.GetByText(packageId.ToString(), new() { Exact = true }).WaitForAsync();
    await connected;
    await Assertions.Expect(page.Locator("[aria-label='Scoped completion counts']")).ToContainTextAsync("Representations obtained");
    await Assertions.Expect(page.GetByText("SYN-LONG-01")).ToBeVisibleAsync();
    var completionCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_AUDIT_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(completionCaptureDir)) Directory.CreateDirectory(completionCaptureDir);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("expected => document.documentElement.clientWidth === expected && (expected >= 960 || getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')", width);
      var documentWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
      var overflow = await page.EvaluateAsync<string>("""
        () => [...document.querySelectorAll('body, main, .audit-workspace, .completion-shell, .audit-workspace-panel, .table-wrap, .mud-table-container, .audit-record-toolbar, .mud-grid, .mud-grid-item')]
          .slice(0, 30).map(element => { const rect = element.getBoundingClientRect(); return `${element.tagName}.${element.className?.toString().slice(0, 32)}:${Math.round(rect.left)}-${Math.round(rect.right)}:${getComputedStyle(element).minWidth}`; }).join(' | ')
        """);
      Assert.True(documentWidth <= width + 1, $"Completion document is {documentWidth}px wide at {width}px viewport. {overflow}");
      if (completionCaptureDir is not null && width is 390 or 1440)
        await page.ScreenshotAsync(new() { Path = Path.Combine(completionCaptureDir, $"completion-{width}.png"), FullPage = true });
    }
    await page.SetViewportSizeAsync(1280, 900);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Engagement Completion Checklist" })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "Audit plan" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    var completionFieldworkLink = page.GetByRole(AriaRole.Link, new() { Name = "Fieldwork control center" });
    await Assertions.Expect(completionFieldworkLink).ToBeFocusedAsync();
    await Assertions.Expect(completionFieldworkLink).ToHaveCSSAsync("outline-style", "solid");
    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{unauthorizedEngagementId:D}/completion");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(packageId.ToString(), body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/completion/{host.Fixture.EngagementId:D}");
    await Assertions.Expect(page.GetByText(packageId.ToString(), new() { Exact = true })).ToBeVisibleAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-AUDIT-FIELDWORK-STALE-ROUTE-01")]
  public async Task AuditFieldworkClearsPriorEngagementWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-AUDIT-FIELDWORK-STALE-ROUTE-01");
    var unauthorizedEngagementId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new Engagement
      {
        Id = unauthorizedEngagementId, FirmId = host.Fixture.FirmId,
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
    await page.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-fieldwork"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Controlled Audit Fieldwork" }).WaitForAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Publish and adopt 2026.1" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await page.GetByRole(AriaRole.Button, new() { Name = "Publish and adopt 2026.1" }).ClickAsync();
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1")).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Versioned audit program" })
      .GetByText("165", new() { Exact = true })).ToBeVisibleAsync();

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{unauthorizedEngagementId:D}/audit-fieldwork");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Fieldwork unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("AUDIT-WORKING-PROCESS", body);
    Assert.DoesNotContain("165", body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-fieldwork");
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1")).ToBeVisibleAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-WORKPAPER-STALE-ROUTE-01")]
  public async Task WorkpaperClearsPriorWorkpaperWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-WORKPAPER-STALE-ROUTE-01");
    var siblingEngagementId = Guid.NewGuid();
    var authorizedWorkpaperId = Guid.NewGuid();
    var siblingWorkpaperId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Workpapers.Add(new Workpaper
      {
        Id = authorizedWorkpaperId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id, Index = "R-E2E-STALE-A",
        Title = "Synthetic stale-route workpaper", Objective = "Authorize the first workpaper read",
        TemplateVersion = "SYNTHETIC-v1", Procedure = "Synthetic procedure",
        WorkPerformed = "Authorized work performed marker", Status = WorkpaperStatuses.Working,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.Workpapers.Add(new Workpaper
      {
        Id = siblingWorkpaperId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = siblingEngagementId, ActorId = host.Fixture.Staff.Id, Index = "R-E2E-STALE-B",
        Title = "Sibling engagement workpaper", Objective = "Must never leak across engagements",
        TemplateVersion = "SYNTHETIC-v1", Procedure = "Synthetic procedure",
        WorkPerformed = "Sibling work performed marker", Status = WorkpaperStatuses.Working,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/workpapers/{authorizedWorkpaperId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper: Synthetic stale-route workpaper" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await Assertions.Expect(page.GetByText("Workpaper Details")).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("R-E2E-STALE-A")).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("[aria-label='Scoped workpaper summary']")).ToContainTextAsync("Frozen submissions");
    var workpaperCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_DETAIL_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(workpaperCaptureDir)) Directory.CreateDirectory(workpaperCaptureDir);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("expected => document.documentElement.clientWidth === expected && (expected >= 960 || getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')", width);
      var documentWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
      Assert.True(documentWidth <= width + 1, $"Workpaper document is {documentWidth}px wide at {width}px viewport.");
      if (workpaperCaptureDir is not null && width is 390 or 1440)
        await page.ScreenshotAsync(new() { Path = Path.Combine(workpaperCaptureDir, $"workpaper-{width}.png"), FullPage = true });
    }
    await page.Locator("[aria-label='Workpaper navigation']").GetByRole(AriaRole.Link, new() { Name = "Audit plan" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    var workpaperFieldworkLink = page.GetByRole(AriaRole.Link, new() { Name = "Fieldwork control center" });
    await Assertions.Expect(workpaperFieldworkLink).ToBeFocusedAsync();
    await Assertions.Expect(workpaperFieldworkLink).ToHaveCSSAsync("outline-style", "solid");

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/audit/workpapers/{siblingWorkpaperId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper unavailable" }).WaitForAsync();
    var deniedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Synthetic stale-route workpaper", deniedBody);
    Assert.DoesNotContain("R-E2E-STALE-A", deniedBody);
    Assert.DoesNotContain("Sibling engagement workpaper", deniedBody);
    Assert.DoesNotContain("R-E2E-STALE-B", deniedBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/audit/workpapers/{authorizedWorkpaperId:D}");
    await Assertions.Expect(page.GetByText("R-E2E-STALE-A")).ToBeVisibleAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FINDING-STALE-ROUTE-01")]
  public async Task FindingClearsPriorFindingWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FINDING-STALE-ROUTE-01");
    var siblingEngagementId = Guid.NewGuid();
    var authorizedFindingId = Guid.NewGuid();
    var siblingFindingId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Findings.Add(new Finding
      {
        Id = authorizedFindingId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id,
        FindingType = "Understated revenue", ImpactDescription = "Synthetic stale-route finding impact",
        Status = FindingStatuses.Open, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Findings.Add(new Finding
      {
        Id = siblingFindingId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = siblingEngagementId, ActorId = host.Fixture.Staff.Id,
        FindingType = "Sibling engagement finding", ImpactDescription = "Sibling impact must never leak",
        Status = FindingStatuses.Open, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/findings/{authorizedFindingId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Audit Finding" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await Assertions.Expect(page.GetByText("Synthetic stale-route finding impact")).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("[aria-label='Scoped finding summary']")).ToContainTextAsync("Management response recorded");
    var findingCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_DETAIL_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(findingCaptureDir)) Directory.CreateDirectory(findingCaptureDir);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("expected => document.documentElement.clientWidth === expected && (expected >= 960 || getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')", width);
      var documentWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
      Assert.True(documentWidth <= width + 1, $"Finding document is {documentWidth}px wide at {width}px viewport.");
      if (findingCaptureDir is not null && width is 390 or 1440)
        await page.ScreenshotAsync(new() { Path = Path.Combine(findingCaptureDir, $"finding-{width}.png"), FullPage = true });
    }
    await page.GetByRole(AriaRole.Link, new() { Name = "Audit plan" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    var findingEngagementLink = page.GetByRole(AriaRole.Link, new() { Name = "Engagement" });
    await Assertions.Expect(findingEngagementLink).ToBeFocusedAsync();
    await Assertions.Expect(findingEngagementLink).ToHaveCSSAsync("outline-style", "solid");

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/findings/{siblingFindingId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Finding unavailable" }).WaitForAsync();
    var deniedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Understated revenue", deniedBody);
    Assert.DoesNotContain("Synthetic stale-route finding impact", deniedBody);
    Assert.DoesNotContain("Sibling engagement finding", deniedBody);
    Assert.DoesNotContain("Sibling impact must never leak", deniedBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/findings/{authorizedFindingId:D}");
    await Assertions.Expect(page.GetByText("Synthetic stale-route finding impact")).ToBeVisibleAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-AUDIT-PLAN-STALE-ROUTE-01")]
  public async Task AuditPlanClearsPriorEngagementWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-AUDIT-PLAN-STALE-ROUTE-01");
    var siblingEngagementId = Guid.NewGuid();
    var assessmentId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.MaterialityAssessments.Add(new MaterialityAssessment
      {
        Id = assessmentId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id,
        BenchmarkSource = "Total assets", BenchmarkVersion = "AFS-v1", Rationale = "Synthetic stale-route fixture",
        BenchmarkAmount = 1_000_000m, RateApplied = 0.05m, OverallMateriality = 50_000m,
        PerformanceMateriality = 37_500m, ClearlyTrivialThreshold = 2_500m, Status = MaterialityStatuses.Draft,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/plans/{host.Fixture.EngagementId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Audit Plan & Strategy" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await Assertions.Expect(page.GetByText("Total assets")).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("[aria-label='Scoped audit plan counts']")).ToContainTextAsync("Identified risks");
    var planCaptureDir = Environment.GetEnvironmentVariable("AUDITSPHERE_AUDIT_UI_CAPTURE_DIR");
    if (!string.IsNullOrWhiteSpace(planCaptureDir)) Directory.CreateDirectory(planCaptureDir);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("expected => document.documentElement.clientWidth === expected && (expected >= 960 || getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')", width);
      var documentWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
      Assert.True(documentWidth <= width + 1, $"Audit plan document is {documentWidth}px wide at {width}px viewport.");
      if (planCaptureDir is not null && width is 390 or 1440)
        await page.ScreenshotAsync(new() { Path = Path.Combine(planCaptureDir, $"plan-{width}.png"), FullPage = true });
    }
    await page.GetByRole(AriaRole.Link, new() { Name = "Fieldwork control center" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    var programLibraryLink = page.GetByRole(AriaRole.Link, new() { Name = "Program library" });
    await Assertions.Expect(programLibraryLink).ToBeFocusedAsync();
    await Assertions.Expect(programLibraryLink).ToHaveCSSAsync("outline-style", "solid");

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/audit/plans/{siblingEngagementId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var deniedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Total assets", deniedBody);
    Assert.DoesNotContain("1,000,000.00", deniedBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/audit/plans/{host.Fixture.EngagementId:D}");
    await Assertions.Expect(page.GetByText("Total assets")).ToBeVisibleAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-POPULATION-STALE-ROUTE-01")]
  public async Task AuditPopulationClearsPriorPopulationWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-POPULATION-STALE-ROUTE-01");
    var siblingEngagementId = Guid.NewGuid();
    var authorizedPopulationId = Guid.NewGuid();
    var siblingPopulationId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.PopulationVersions.Add(new PopulationVersion
      {
        Id = authorizedPopulationId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id,
        Purpose = "Synthetic stale-route population", Assertion = "Existence and accuracy",
        SourceReceiptReference = "SYN-POP-RECEIPT", ExtractionParameters = "synthetic",
        RowCount = 12000, MonetaryControlTotal = 4_800_000m, Currency = "QAR",
        Status = PopulationStatuses.PendingApproval, CreatedAt = DateTimeOffset.UtcNow
      });
      db.PopulationVersions.Add(new PopulationVersion
      {
        Id = siblingPopulationId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = siblingEngagementId, ActorId = host.Fixture.Staff.Id,
        Purpose = "Sibling population must never leak", Assertion = "Existence and accuracy",
        SourceReceiptReference = "SYN-POP-SIBLING", ExtractionParameters = "synthetic",
        RowCount = 1, MonetaryControlTotal = 1m, Currency = "QAR",
        Status = PopulationStatuses.PendingApproval, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/populations/{authorizedPopulationId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Population Record" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await Assertions.Expect(page.GetByText("Synthetic stale-route population")).ToBeVisibleAsync();

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/audit/populations/{siblingPopulationId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Population unavailable" }).WaitForAsync();
    var deniedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Synthetic stale-route population", deniedBody);
    Assert.DoesNotContain("Sibling population must never leak", deniedBody);
    Assert.DoesNotContain("12,000", deniedBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/audit/populations/{authorizedPopulationId:D}");
    await Assertions.Expect(page.GetByText("Synthetic stale-route population")).ToBeVisibleAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-RELEASE-STALE-ROUTE-01")]
  public async Task ReleaseClearsPriorCandidateWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-RELEASE-STALE-ROUTE-01");
    var workpaperId = Guid.NewGuid();
    Guid candidateId;
    var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "Reviewer");
    var partner = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Workpapers.Add(new Workpaper
      {
        Id = workpaperId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id, Index = "R-E2E-STALE-REL",
        Title = "Synthetic stale-route release workpaper", Objective = "Exercise release route reauthorization",
        TemplateVersion = "SYNTHETIC-v1", Procedure = "Synthetic procedure", Status = WorkpaperStatuses.Working,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var artifact = "synthetic-stale-route-release"u8.ToArray();
      var digest = Hashing.Sha256Hex(artifact);
      var approval = await ApprovalService.CreateAsync(db, reviewer,
        new CreateApprovalRequest("WORKPAPER", workpaperId, 1, 1, 1, digest));
      Assert.True(approval.Succeeded, approval.Message);
      var candidate = await ReleaseService.CreateCandidateAsync(db, partner,
        new CreateReleaseCandidateRequest(approval.Value!, "WORKPAPER", workpaperId, 1, 1, 1, digest));
      Assert.True(candidate.Succeeded, candidate.Message);
      candidateId = candidate.Value;
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/releases/{candidateId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Release candidate" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await page.GetByText(candidateId.ToString("D")).WaitForAsync(new() { Timeout = 15000 });
    var authorizedBody = await page.Locator("body").InnerTextAsync();
    Assert.Contains(candidateId.ToString("D"), authorizedBody);

    var missingCandidateId = Guid.NewGuid();
    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/releases/{missingCandidateId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Candidate unavailable" }).WaitForAsync();
    var deniedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(candidateId.ToString("D"), deniedBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/releases/{candidateId:D}");
    await page.GetByText(candidateId.ToString("D")).WaitForAsync(new() { Timeout = 15000 });
    var restoredBody = await page.Locator("body").InnerTextAsync();
    Assert.Contains(candidateId.ToString("D"), restoredBody);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-WORKPAPER-REFRESH-REVOKED-01")]
  public async Task ReloadCurrentTargetClearsWorkpaperAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-WORKPAPER-REFRESH-REVOKED-01");
    var workpaperId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Workpapers.Add(new Workpaper
      {
        Id = workpaperId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id,
        Index = "R-E2E-REVOKE-A", Title = "Synthetic revoked-grant workpaper",
        Objective = "Exercise fail-closed reload after revocation",
        TemplateVersion = "SYNTHETIC-v1", Procedure = "Synthetic procedure",
        WorkPerformed = "Revoked-grant work performed marker",
        Status = WorkpaperStatuses.Working, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/workpapers/{workpaperId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper: Synthetic revoked-grant workpaper" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await Assertions.Expect(page.GetByText("Workpaper Details")).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var grants = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == host.Fixture.Staff.Id && x.RevokedAt == null &&
        (x.Role == "Partner" || x.Role == "Staff")).ToListAsync();
      Assert.NotEmpty(grants);
      foreach (var grant in grants)
      {
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
          PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Reload current target" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Synthetic revoked-grant workpaper", body);
    Assert.DoesNotContain("R-E2E-REVOKE-A", body);
    Assert.DoesNotContain("Revoked-grant work performed marker", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FINDING-REFRESH-REVOKED-01")]
  public async Task FindingRefreshClearsAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FINDING-REFRESH-REVOKED-01");
    var findingId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Findings.Add(new Finding
      {
        Id = findingId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id,
        FindingType = "Understated revenue", ImpactDescription = "Synthetic revocation finding impact",
        Status = FindingStatuses.Open, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/findings/{findingId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Audit Finding" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await Assertions.Expect(page.GetByText("Synthetic revocation finding impact")).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var grants = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == host.Fixture.Staff.Id && x.RevokedAt == null &&
        (x.Role == "Partner" || x.Role == "Staff")).ToListAsync();
      Assert.NotEmpty(grants);
      foreach (var grant in grants)
      {
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
          PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh finding" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Finding unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Understated revenue", body);
    Assert.DoesNotContain("Synthetic revocation finding impact", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-AUDIT-PLAN-REFRESH-REVOKED-01")]
  public async Task AuditPlanRefreshClearsAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-AUDIT-PLAN-REFRESH-REVOKED-01");
    var assessmentId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.MaterialityAssessments.Add(new MaterialityAssessment
      {
        Id = assessmentId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id,
        BenchmarkSource = "Total assets", BenchmarkVersion = "AFS-v1", Rationale = "Synthetic revocation fixture",
        BenchmarkAmount = 1_000_000m, RateApplied = 0.05m, OverallMateriality = 50_000m,
        PerformanceMateriality = 37_500m, ClearlyTrivialThreshold = 2_500m, Status = MaterialityStatuses.Draft,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/plans/{host.Fixture.EngagementId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Audit Plan & Strategy" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await Assertions.Expect(page.GetByText("Total assets")).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var grants = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == host.Fixture.Staff.Id && x.RevokedAt == null &&
        (x.Role == "Partner" || x.Role == "Staff")).ToListAsync();
      Assert.NotEmpty(grants);
      foreach (var grant in grants)
      {
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
          PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh plan" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Total assets", body);
    Assert.DoesNotContain("1,000,000.00", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-009-AGGREGATE-DIFFERENCES-UI-01")]
  public async Task AuditFieldworkRecordsHumanAggregateConclusionBoundToDifferenceSchedule()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-009-AGGREGATE-DIFFERENCES-UI-01");
    var assessmentId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.MaterialityAssessments.Add(new MaterialityAssessment
      {
        Id = assessmentId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id,
        BenchmarkSource = "Total assets", BenchmarkVersion = "AFS-v1", Rationale = "Synthetic E2E fixture",
        BenchmarkAmount = 1_000_000m, RateApplied = 0.05m, OverallMateriality = 50_000m,
        PerformanceMateriality = 37_500m, ClearlyTrivialThreshold = 2_500m, Status = MaterialityStatuses.Draft,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.MaterialityApprovals.Add(new MaterialityApproval
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, MaterialityAssessmentId = assessmentId,
        ApprovedByUserId = host.Fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow
      });
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, AccountArea = "Revenue", DifferenceType = "KNOWN",
        Description = "Synthetic unadjusted cut-off difference", Amount = 12_000m, Currency = "QAR",
        MaterialityReference = "AFS-v1", QualitativeConcerns = "Synthetic only", Status = AuditDifferenceStatuses.Evaluated,
        CreatedByUserId = host.Fixture.Staff.Id, EvaluatedByUserId = host.Fixture.Reviewer.Id,
        Evaluation = "Synthetic individual evaluation", CreatedAt = DateTimeOffset.UtcNow, EvaluatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-fieldwork"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Controlled Audit Fieldwork" }).WaitForAsync();
    await connected;
    await page.GetByRole(AriaRole.Heading, new() { Name = "Aggregate differences and reporting assessment" }).WaitForAsync();
    await page.GetByLabel("Required: professional aggregate conclusion and reporting impact")
      .FillAsync("Human assessment: evaluate unadjusted QAR amounts and qualitative factors; reporting impact remains subject to partner judgment.");
    await page.GetByRole(AriaRole.Button, new() { Name = "Record aggregate conclusion" }).ClickAsync();
    await Assertions.Expect(page.GetByText("Human assessment: evaluate unadjusted QAR amounts and qualitative factors; reporting impact remains subject to partner judgment.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("SUBMITTED", new() { Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-04")]
  public async Task AuditProgramAndReviewedFieldworkSurviveReconnectAndFreezeWorkpaper()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-04");
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-fieldwork"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Controlled Audit Fieldwork" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await page.GetByRole(AriaRole.Button, new() { Name = "Publish and adopt 2026.1" }).ClickAsync();
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1")).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Versioned audit program" })
      .GetByText("165", new() { Exact = true })).ToBeVisibleAsync();

    await Assertions.Expect(page.GetByText("Showing 9 of 165 authorized procedures")).ToBeVisibleAsync();
    var sectionSelector = page.Locator(".audit-fieldwork-filter .mud-select").First;
    await sectionSelector.ClickAsync();
    await page.GetByRole(AriaRole.Option, new() { Name = "All sections" }).ClickAsync();
    await Assertions.Expect(page.GetByText("Showing 165 of 165 authorized procedures")).ToBeVisibleAsync();
    await sectionSelector.ClickAsync();
    await page.GetByRole(AriaRole.Option).Filter(new() { HasText = "Section 1 —" }).ClickAsync();
    await Assertions.Expect(page.GetByText("Showing 9 of 165 authorized procedures")).ToBeVisibleAsync();
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_AUDIT_UI_CAPTURE_DIR") is { Length: > 0 } auditCaptureDir)
    {
      Directory.CreateDirectory(auditCaptureDir);
      foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
      {
        await page.SetViewportSizeAsync(width, 900);
        await page.WaitForFunctionAsync("() => innerWidth > 760 || getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px'");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var fieldworkWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        var fieldworkOverflow = await page.EvaluateAsync<string>("""
          () => [...document.querySelectorAll('body *')]
            .filter(element => { const rect = element.getBoundingClientRect(); return rect.right > innerWidth + 1 && rect.left < innerWidth; })
            .slice(0, 24).map(element => `${element.tagName}.${element.className?.toString().slice(0, 70)}:${Math.round(element.getBoundingClientRect().right)}`).join(' | ')
          """);
        Assert.True(fieldworkWidth <= width + 1, $"Fieldwork document is {fieldworkWidth}px wide at {width}px viewport. {fieldworkOverflow}");
        if (width is 390 or 1440)
          await page.ScreenshotAsync(new() { Path = Path.Combine(auditCaptureDir, $"fieldwork-{width}.png"), FullPage = true });
      }
    }
    var libraryConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, "/app/audit/library"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Library versions" }).WaitForAsync();
    await libraryConnected;
    var refreshLibrary = page.GetByRole(AriaRole.Button, new() { Name = "Refresh library" });
    await page.GetByRole(AriaRole.Link, new() { Name = "Back to portfolio" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(refreshLibrary).ToBeFocusedAsync();
    await Assertions.Expect(refreshLibrary).ToHaveCSSAsync("outline-style", "solid");
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_AUDIT_UI_CAPTURE_DIR") is { Length: > 0 } libraryCaptureDir)
    {
      foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
      {
        await page.SetViewportSizeAsync(width, 900);
        await page.WaitForFunctionAsync("() => innerWidth > 760 || getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px'");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var libraryWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        var libraryOverflow = await page.EvaluateAsync<string>("""
          () => [...document.querySelectorAll('body *')]
            .filter(element => { const rect = element.getBoundingClientRect(); return rect.right > innerWidth + 1 && rect.left < innerWidth; })
            .slice(0, 24).map(element => `${element.tagName}.${element.className?.toString().slice(0, 70)}:${Math.round(element.getBoundingClientRect().right)}`).join(' | ')
          """);
        Assert.True(libraryWidth <= width + 1, $"Library document is {libraryWidth}px wide at {width}px viewport. {libraryOverflow}");
        if (width is 390 or 1440)
          await page.ScreenshotAsync(new() { Path = Path.Combine(libraryCaptureDir, $"library-{width}.png"), FullPage = true });
      }
    }
    await page.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-fieldwork"));
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1")).ToBeVisibleAsync();

    Guid procedureId;
    string sourceProcedureId;
    await using (var db = host.CreateDbContext())
    {
      var procedure = await db.AuditProcedures.AsNoTracking()
        .Where(x => x.EngagementId == host.Fixture.EngagementId)
        .OrderBy(x => x.SourceSectionNumber).ThenBy(x => x.SourceProcedureId).FirstAsync();
      procedureId = procedure.Id;
      sourceProcedureId = procedure.SourceProcedureId;
    }
    var procedureRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = sourceProcedureId });
    await procedureRow.GetByRole(AriaRole.Button, new() { Name = "Applicable" }).ClickAsync();
    await Assertions.Expect(procedureRow).ToContainTextAsync("APPLICABLE");

    Guid scheduleId;
    Guid selectionId;
    Guid itemTestId;
    Guid findingId;
    Guid workpaperId;
    var staff = PbcSeed.Actor(host.Fixture.Staff, "Staff");
    var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "Reviewer");
    await using (var db = host.CreateDbContext())
    {
      var sourceHash = Hashing.Sha256Hex("synthetic-audit-source-schedule");
      var schedule = await AuditFieldworkService.CreateScheduleAsync(db, staff, new CreateScheduleRequest(
        host.Fixture.EngagementId, "SYNTHETIC_GL", "TEST-ENTITY", "E2E-SOURCE-001", null,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "DEBIT_MINUS_CREDIT", sourceHash, 125m,
        [new ScheduleRowInput("ROW-001", 1, "1000", "Synthetic cash item", 125m, "QAR",
          new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 30), null, null, "{}")]));
      Assert.True(schedule.Succeeded, schedule.Message);
      scheduleId = schedule.Value!.ScheduleId;
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
        new ReviewScheduleRequest(scheduleId, "Complete synthetic source schedule", true))).Succeeded);

      var selection = await AuditFieldworkService.CreateSelectionAsync(db, staff, new CreateSelectionRequest(
        host.Fixture.EngagementId, procedureId, scheduleId, null, "TARGETED", "Synthetic high-value selection",
        [new SelectionItemInput("ROW-001", 125m, "QAR", "Synthetic test item")]));
      Assert.True(selection.Succeeded, selection.Message);
      selectionId = selection.Value!.SelectionId;
      Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
        new ReviewSelectionRequest(selectionId, AuditSelectionStatuses.Reviewed, "Independent selection review."))).Succeeded);

      var selectedItemId = await db.AuditSelectionItems.AsNoTracking()
        .Where(x => x.SelectionId == selectionId).Select(x => x.Id).SingleAsync();
      var itemTest = await AuditFieldworkService.RecordItemTestAsync(db, staff, new RecordItemTestRequest(
        selectedItemId, "Agreed synthetic row to source evidence.", ["synthetic-evidence-001"],
        AuditItemTestResults.Pass, null, null, null));
      Assert.True(itemTest.Succeeded, itemTest.Message);
      itemTestId = itemTest.Value!.AuditItemTestId;
      Assert.True((await AuditFieldworkService.ReviewItemTestAsync(db, reviewer,
        new ReviewItemTestRequest(itemTestId, AuditItemTestReviewDecisions.Reviewed, "Independent result review."))).Succeeded);

      var finding = await AuditPlanningService.CreateFindingAsync(db, staff, new CreateFindingRequest(
        host.Fixture.EngagementId, "Synthetic cut-off exception", "Synthetic test exception retained for review.",
        false, 125m, null));
      Assert.True(finding.Succeeded, finding.Message);
      findingId = finding.Value!.FindingId;
      var workpaper = await AuditPlanningService.CreateWorkpaperAsync(db, staff, new CreateWorkpaperRequest(
        host.Fixture.EngagementId, "E2E-04", "Synthetic source test", "Test scoped source evidence",
        "E2E-TEMPLATE-v1", procedureId, "Inspect selected source row and resolve exception."));
      Assert.True(workpaper.Succeeded, workpaper.Message);
      workpaperId = workpaper.Value!.WorkpaperId;
      var draft = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(),
          "Agreed ROW-001 to the synthetic source schedule.",
          "The selected item agrees; the separately recorded finding remains open."));
      Assert.True(draft.Succeeded, draft.Message);
    }

    var workpaperConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/workpapers/{workpaperId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper: Synthetic source test" }).WaitForAsync();
    await workpaperConnected;
    await Assertions.Expect(page.Locator(".workpaper-shell p.notice-text[role='status']")).ToContainTextAsync("Saved at");
    Assert.Equal("Agreed ROW-001 to the synthetic source schedule.", await page.GetByLabel("Work performed").InputValueAsync());
    Assert.Equal("The selected item agrees; the separately recorded finding remains open.",
      await page.GetByLabel("Conclusion").InputValueAsync());
    await using (var verifyDraft = host.CreateDbContext())
    {
      var saved = await verifyDraft.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal("Agreed ROW-001 to the synthetic source schedule.", saved.WorkPerformed);
      Assert.Equal("The selected item agrees; the separately recorded finding remains open.", saved.Conclusion);
    }

    var reconnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.EvaluateAsync("localStorage.clear()");
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper: Synthetic source test" }).WaitForAsync();
    await reconnected;
    Assert.Equal("Agreed ROW-001 to the synthetic source schedule.", await page.GetByLabel("Work performed").InputValueAsync());
    Assert.Equal("The selected item agrees; the separately recorded finding remains open.",
      await page.GetByLabel("Conclusion").InputValueAsync());
    await page.GetByRole(AriaRole.Button, new() { Name = "Submit for review" }).ClickAsync();
    await Assertions.Expect(page.GetByText("The submission was recorded.")).ToBeVisibleAsync();

    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/findings/{findingId:D}"));
    await Assertions.Expect(page.GetByText("Synthetic cut-off exception")).ToBeVisibleAsync();
    await using var verify = host.CreateDbContext();
    Assert.Equal(AuditScheduleStatuses.Approved,
      await verify.AuditSchedules.Where(x => x.Id == scheduleId).Select(x => x.Status).SingleAsync());
    Assert.Equal(AuditSelectionStatuses.Reviewed,
      await verify.AuditSelections.Where(x => x.Id == selectionId).Select(x => x.Status).SingleAsync());
    Assert.Single(await verify.AuditItemTestReviews.Where(x => x.AuditItemTestId == itemTestId).ToListAsync());
    Assert.Single(await verify.WorkpaperSubmissions.Where(x => x.WorkpaperId == workpaperId).ToListAsync());
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("Category", "ReleaseAndRecords")]
  [Trait("CaseId", "PROP-E2E-09")]
  public async Task ExpiredProtectionBlocksReleaseAndIsNeverPresentedAsVerified()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-09",
      requireProtectionAttestation: true);
    var artifact = "synthetic-release-artifact"u8.ToArray();
    var digest = Hashing.Sha256Hex(artifact);
    var workpaperId = Guid.NewGuid();
    var siblingEngagementId = Guid.NewGuid();
    var siblingStaff = PbcSeed.User(host.Fixture.FirmId, "Staff");
    Guid candidateId;
    var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "Reviewer");
    var partner = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
    await using (var db = host.CreateDbContext())
    {
      db.Workpapers.Add(new Workpaper
      {
        Id = workpaperId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id, Index = "R-E2E-09",
        Title = "Synthetic release candidate", Objective = "Exercise fail-closed release gates",
        TemplateVersion = "SYNTHETIC-v1", Procedure = "Synthetic procedure", Status = WorkpaperStatuses.Working,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(siblingStaff);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, siblingStaff, "Staff",
        host.Fixture.ClientId, siblingEngagementId));
      await db.SaveChangesAsync();
      var approval = await ApprovalService.CreateAsync(db, reviewer,
        new CreateApprovalRequest("WORKPAPER", workpaperId, 1, 1, 1, digest));
      Assert.True(approval.Succeeded, approval.Message);
      var candidate = await ReleaseService.CreateCandidateAsync(db, partner,
        new CreateReleaseCandidateRequest(approval.Value!, "WORKPAPER", workpaperId, 1, 1, 1, digest));
      Assert.True(candidate.Succeeded, candidate.Message);
      candidateId = candidate.Value;
      var checkpointStore = new LocalAppendOnlyCheckpointStore(Path.Combine(host.RunRoot, "checkpoints"));
      var checkpoint = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, checkpointStore, partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "synthetic-local-release-checkpoint", digest, artifact));
      Assert.True(checkpoint.Succeeded, checkpoint.Message);
      db.ProtectionAttestations.Add(new ProtectionAttestation
      {
        Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ArtifactId = workpaperId, ArtifactHash = digest,
        Binding = "synthetic://audit-sphere-test/records", ProfileId = "AUDITSPHERE-SYNTHETIC-RECORD",
        ProfileVersion = 1, ObservedState = "PROTECTED", VerificationTime = DateTimeOffset.UtcNow.AddDays(-2),
        Verifier = "synthetic-fixture", ExpiryTime = DateTimeOffset.UtcNow.AddMinutes(-1),
        RecheckRule = "TEST_ONLY", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
      });
      await db.SaveChangesAsync();
    }

    var expired = await ReleaseService.IssueAsync(host.CreateDbContext(), partner,
      new IssueReleaseRequest(candidateId, 1, digest, "synthetic-expired-release"),
      new ReleaseSafetyOptions { RequireExternalCheckpointBeforeDelivery = true, RequireProtectionAttestation = true });
    Assert.False(expired.Succeeded);
    Assert.Contains("Protection attestation has expired", expired.Message);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    var siblingStaffUrl = await host.StartWebForIdentityAsync(siblingStaff);
    await using (var siblingContext = await browser.NewContextAsync())
    {
      var siblingPage = await siblingContext.NewPageAsync();
      var siblingConnected = WaitForCircuitConnectionAsync(siblingPage, []);
      await siblingPage.GotoAsync(SignInUrl(siblingStaffUrl, $"/app/releases/{candidateId:D}"));
      await siblingPage.GetByRole(AriaRole.Heading, new() { Name = "Candidate unavailable" }).WaitForAsync();
      await siblingConnected;
      var siblingBody = await siblingPage.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(candidateId.ToString("D"), siblingBody, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain(digest, siblingBody, StringComparison.Ordinal);
      Assert.DoesNotContain("AUDITSPHERE-SYNTHETIC-RECORD", siblingBody, StringComparison.Ordinal);
      Assert.DoesNotContain("synthetic-local-release-checkpoint", siblingBody, StringComparison.Ordinal);
    }

    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var connected = WaitForCircuitConnectionAsync(page, []);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/releases/{candidateId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Release candidate" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains("expired — Profile AUDITSPHERE-SYNTHETIC-RECORD", body);
    Assert.Contains("No external records-provider acceptance is claimed", body);
    Assert.DoesNotContain("verified — Profile AUDITSPHERE-SYNTHETIC-RECORD", body);
    Assert.False(await page.GetByRole(AriaRole.Button, new() { Name = "Issue release" }).IsEnabledAsync());
    await using var verify = host.CreateDbContext();
    Assert.Empty(await verify.Releases.AsNoTracking().Where(x => x.ReleaseCandidateId == candidateId).ToListAsync());

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__releaseRouteToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/releases/{Guid.NewGuid():D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Candidate unavailable" }).WaitForAsync();
    body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(digest, body);
    Assert.DoesNotContain("AUDITSPHERE-SYNTHETIC-RECORD", body);
    Assert.DoesNotContain("synthetic-local-release-checkpoint", body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__releaseRouteToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/releases/{candidateId:D}");
    await page.GetByText(digest, new() { Exact = true }).WaitForAsync();
    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == host.Fixture.Staff.Id &&
        x.Role == "Staff" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, partner,
        new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh candidate" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(digest, body);
    Assert.DoesNotContain("AUDITSPHERE-SYNTHETIC-RECORD", body);
    Assert.DoesNotContain("synthetic-local-release-checkpoint", body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__releaseRouteToken"));
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
    return connected.Task.WaitAsync(TimeSpan.FromSeconds(15));
  }
}
