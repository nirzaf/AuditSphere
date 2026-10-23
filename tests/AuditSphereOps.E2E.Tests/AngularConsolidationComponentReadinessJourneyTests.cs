using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "Accessibility")]
public sealed class AngularConsolidationComponentReadinessJourneyTests
{
  private const string CaseId = "AS-PAR-002-ANG-CONSOL-COMPONENT-READINESS-01";
  private const string ScopeCaseId = "AS-PAR-002-ANG-CONSOL-SCOPE-READINESS-01";
  private const string CorrectionLineageCaseId = "AS-PAR-002-ANG-CONSOL-CORRECTION-LINEAGE-01";

  [Fact]
  [Trait("CaseId", CaseId)]
  public async Task ReviewerApprovalReadinessTracksTheCurrentGroupPerimeter()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: CaseId);
    var f = host.Fixture;
    var seeded = await SeedReadinessFixtureAsync(host);
    var groupId = seeded.GroupId;
    var scopeId = seeded.ScopeId;
    var addedClientId = Guid.NewGuid();
    var componentId = seeded.ComponentId;
    var groupCode = seeded.GroupCode;
    var groupName = seeded.GroupName;

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH") is { Length: > 0 } buildPath)
      settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    var path = $"/app/consolidation/scopes/{scopeId:D}";

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    var componentApprovalRequests = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    await page.RouteAsync("**/api/ui/consolidation/components/*/approve", async route =>
    {
      if (route.Request.Method == "POST")
      {
        componentApprovalRequests.Add(route.Request.Url);
        await route.AbortAsync();
      }
      else
        await route.ContinueAsync();
    });

    var initialWorkspaceResponse = page.WaitForResponseAsync(response => response.Request.Method == "GET" &&
      new Uri(response.Url).AbsolutePath == $"/api/ui/consolidation/scopes/{scopeId:D}");
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    Assert.Equal(200, (await initialWorkspaceResponse).Status);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = $"{groupName} ({groupCode}) · v1", Exact = true })).ToBeVisibleAsync();

    var components = page.Locator("#components");
    var componentRow = components.GetByRole(AriaRole.Row).Filter(new() { HasText = "PBC TEST CLIENT" });
    await Assertions.Expect(componentRow).ToHaveCountAsync(1);
    await Assertions.Expect(componentRow.Locator("td").Nth(5).Locator("audit-status .status-chip")).ToHaveTextAsync("submitted");
    await Assertions.Expect(componentRow.Locator("td").Nth(6).Locator("audit-status .status-chip")).ToHaveTextAsync("unavailable");
    var approve = componentRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true });
    await Assertions.Expect(approve).ToBeEnabledAsync();
    Assert.Null(await approve.GetAttributeAsync("aria-describedby"));
    Assert.Equal(0, await components.Locator($"#component-approval-blocker-{componentId:D}").CountAsync());

    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = addedClientId, FirmId = f.FirmId, LegalName = "Synthetic later group member", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var addedMembership = await ConsolidationService.AddMembershipAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new GroupMembershipRequest(groupId, addedClientId, new DateOnly(2020, 1, 1), null,
          "CONTROLLED", 100m, 100m, "SYNTHETIC-LATER-MEMBERSHIP"));
      Assert.True(addedMembership.Succeeded, addedMembership.Message);
    }

    await using (var proof = host.CreateDbContext())
    {
      Assert.Equal(2, await proof.ClientGroups.AsNoTracking().Where(x => x.Id == groupId)
        .Select(x => x.Revision).SingleAsync());
      Assert.Equal(1, await proof.ConsolidationScopeVersions.AsNoTracking().Where(x => x.Id == scopeId)
        .Select(x => x.GroupRevision).SingleAsync());
    }

    var refreshedWorkspaceResponse = page.WaitForResponseAsync(response => response.Request.Method == "GET" &&
      new Uri(response.Url).AbsolutePath == $"/api/ui/consolidation/scopes/{scopeId:D}");
    await page.ReloadAsync();
    Assert.Equal(200, (await refreshedWorkspaceResponse).Status);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = $"{groupName} ({groupCode}) · v1", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(componentRow).ToHaveCountAsync(1);
    await Assertions.Expect(componentRow.Locator("td").Nth(5).Locator("audit-status .status-chip")).ToHaveTextAsync("submitted");
    await Assertions.Expect(componentRow.Locator("td").Nth(6).Locator("audit-status .status-chip")).ToHaveTextAsync("stale");
    await Assertions.Expect(approve).ToBeDisabledAsync();

    var blockerId = $"component-approval-blocker-{componentId:D}";
    Assert.Equal(blockerId, await approve.GetAttributeAsync("aria-describedby"));
    var blocker = page.Locator($"#{blockerId}");
    await Assertions.Expect(blocker).ToBeVisibleAsync();
    await Assertions.Expect(blocker).ToContainTextAsync("group perimeter changed");
    await Assertions.Expect(blocker).ToContainTextAsync("new scope version");
    Assert.Equal(componentId.ToString("D"), await approve.EvaluateAsync<string>(
      "element => element.getAttribute('aria-describedby')?.replace('component-approval-blocker-', '') ?? ''"));

    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>(
      "() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "The consolidation component readiness row must not overflow the document at 390px.");
    var componentTable = components.GetByRole(AriaRole.Region, new() { Name = "Components table", Exact = true });
    await page.Locator("main").FocusAsync();
    var tabStops = await page.Locator("a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex='0']").CountAsync();
    for (var attempt = 0; attempt <= tabStops; attempt++)
    {
      if (await componentTable.EvaluateAsync<bool>("element => element === document.activeElement")) break;
      await page.Keyboard.PressAsync("Tab");
    }
    await Assertions.Expect(componentTable).ToBeFocusedAsync();
    Assert.True(await componentTable.EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth"));
    await page.Keyboard.PressAsync("ArrowRight");
    await page.WaitForFunctionAsync("() => document.querySelector('[aria-label=\"Components table\"]').scrollLeft > 0");

    // Exercise the component handler while disabled; the route records and aborts any dispatch so this proves UI gating only.
    await approve.DispatchEventAsync("click");
    await page.EvaluateAsync<bool>(
      "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(true))))");
    Assert.Empty(componentApprovalRequests);
    Assert.Empty(pageErrors);

    await using (var proof = host.CreateDbContext())
    {
      var component = await proof.ConsolidationComponents.AsNoTracking().SingleAsync(x => x.Id == componentId);
      Assert.Equal(AccountingWorkflowStates.Submitted, component.Status);
      Assert.Null(component.ApprovedByUserId);
      Assert.Null(component.ApprovedAt);
    }

    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "consolidation-component-readiness");
  }

  [Fact]
  [Trait("CaseId", CorrectionLineageCaseId)]
  [Trait("CompletionUnit", "WU-013")]
  public async Task ComponentCorrectionHandoffPersistsExactSourceAndPreservesApprovedPinAndRun()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: CorrectionLineageCaseId);
    var f = host.Fixture;
    var seeded = await SeedReadinessFixtureAsync(host);
    var preparer = PbcSeed.Actor(f.Staff, "AccountingPreparer");
    var reviewer = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    var methodOwner = PbcSeed.User(f.FirmId, "Staff");
    Guid periodId;
    Guid runId;
    Guid externalPackId;
    Guid approvedByUserId;
    DateTimeOffset approvedAt;
    long originalGroupRevision;
    string componentPackageHash;
    string externalPackDigest;
    string runHash;
    Guid[] originalRunLineIds;

    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(methodOwner);
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, GroupId = seeded.GroupId, UserId = methodOwner.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = f.Admin.Id
      });
      await db.SaveChangesAsync();

      var groupCapability = await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, seeded.GroupId, AccountingCapabilityServiceKinds.GroupReporting,
          "IFRS", "2026", "ANNUAL", "QAR", "STATUTORY", ConsolidationCalculator.RestrictedMethod,
          "PARTNER", "GROUP"));
      Assert.True(groupCapability.Succeeded, groupCapability.Message);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, groupCapability.Value,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "synthetic-correction-lineage-local")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db,
        PbcSeed.Actor(methodOwner, "Partner"), groupCapability.Value,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "synthetic-correction-lineage-method-owner")).Succeeded);

      var componentApproval = await ConsolidationService.ApproveComponentAsync(db, reviewer, seeded.ComponentId);
      Assert.True(componentApproval.Succeeded, componentApproval.Message);
      var scopeApproval = await ConsolidationService.ApproveScopeAsync(db, reviewer, seeded.ScopeId);
      Assert.True(scopeApproval.Succeeded, scopeApproval.Message);
      var run = await ConsolidationService.RunAsync(db, preparer, seeded.ScopeId);
      Assert.True(run.Succeeded, run.Message);
      runId = run.Value;
      var runApproval = await ConsolidationService.ApproveRunAsync(db, reviewer, runId);
      Assert.True(runApproval.Succeeded, runApproval.Message);

      var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleAsync(x => x.Id == seeded.ScopeId);
      periodId = scope.PeriodId;
      originalGroupRevision = scope.GroupRevision;
      var component = await db.ConsolidationComponents.AsNoTracking().SingleAsync(x => x.Id == seeded.ComponentId);
      Assert.NotNull(component.ExternalComponentPackId);
      externalPackId = component.ExternalComponentPackId!.Value;
      componentPackageHash = component.PackageHash;
      Assert.NotNull(component.ApprovedByUserId);
      Assert.NotNull(component.ApprovedAt);
      approvedByUserId = component.ApprovedByUserId!.Value;
      approvedAt = component.ApprovedAt!.Value;
      externalPackDigest = await db.ExternalComponentPacks.AsNoTracking()
        .Where(x => x.Id == externalPackId).Select(x => x.PackDigest).SingleAsync();
      var approvedRun = await db.ConsolidationRuns.AsNoTracking().SingleAsync(x => x.Id == runId);
      runHash = approvedRun.RunHash;
      originalRunLineIds = await db.ConsolidationRunLines.AsNoTracking().Where(x => x.RunId == runId)
        .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
      Assert.NotEmpty(originalRunLineIds);
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH") is { Length: > 0 } buildPath)
      settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(f.Staff, settings);
    var sourcePath = $"/app/consolidation/scopes/{seeded.ScopeId:D}";

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    var sourceWorkspaceResponse = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && new Uri(request.Url).AbsolutePath == $"/api/ui/consolidation/scopes/{seeded.ScopeId:D}",
      () => page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(sourcePath)));
    Assert.Equal(200, sourceWorkspaceResponse.Status);
    using (var sourceWorkspace = JsonDocument.Parse(sourceWorkspaceResponse.Body))
    {
      var scope = sourceWorkspace.RootElement.GetProperty("scope");
      Assert.Equal(seeded.ScopeId, scope.GetProperty("id").GetGuid());
      Assert.Equal(seeded.GroupId, scope.GetProperty("groupId").GetGuid());
      Assert.Equal(periodId, scope.GetProperty("periodId").GetGuid());
      Assert.Equal(JsonValueKind.Null, scope.GetProperty("supersedesScopeVersionId").ValueKind);
      Assert.Equal(JsonValueKind.Null, scope.GetProperty("supersedesComponentId").ValueKind);
      var component = sourceWorkspace.RootElement.GetProperty("components").EnumerateArray()
        .Single(x => x.GetProperty("id").GetGuid() == seeded.ComponentId);
      Assert.Equal("APPROVED", component.GetProperty("status").GetString());
      Assert.Equal(externalPackId, component.GetProperty("externalComponentPackId").GetGuid());
      Assert.Equal(componentPackageHash, component.GetProperty("packageHash").GetString());
    }

    var componentRow = page.Locator("#components").GetByRole(AriaRole.Row).Filter(new() { HasText = "PBC TEST CLIENT" });
    await Assertions.Expect(componentRow).ToHaveCountAsync(1);
    var correctionSourceRead = page.WaitForResponseAsync(response => response.Request.Method == "GET" &&
      new Uri(response.Url).AbsolutePath == $"/api/ui/consolidation/scopes/{seeded.ScopeId:D}" &&
      new Uri(response.Url).Query.Contains($"selectedComponentId={seeded.ComponentId:D}", StringComparison.OrdinalIgnoreCase));
    await componentRow.GetByRole(AriaRole.Button, new() { Name = "Correct source", Exact = true }).ClickAsync();
    var correctionSourceResponse = await correctionSourceRead;
    Assert.Equal(200, correctionSourceResponse.Status);
    var correctionSource = page.Locator("section[aria-labelledby='component-correction-heading']");
    await Assertions.Expect(correctionSource.GetByRole(AriaRole.Heading,
      new() { Name = "Correct this component", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(correctionSource).ToContainTextAsync(seeded.ScopeId.ToString("D"));
    await Assertions.Expect(correctionSource).ToContainTextAsync(seeded.ComponentId.ToString("D"));
    await correctionSource.GetByRole(AriaRole.Link,
      new() { Name = "Open the authorized new-scope correction handoff", Exact = true }).ClickAsync();

    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Correct a component in a new scope version", Exact = true })).ToBeVisibleAsync();
    Assert.Contains($"correctionScopeId={seeded.ScopeId:D}", new Uri(page.Url).Query, StringComparison.OrdinalIgnoreCase);
    Assert.Contains($"correctionGroupId={seeded.GroupId:D}", new Uri(page.Url).Query, StringComparison.OrdinalIgnoreCase);
    Assert.Contains($"correctionComponentId={seeded.ComponentId:D}", new Uri(page.Url).Query, StringComparison.OrdinalIgnoreCase);
    await Assertions.Expect(page.Locator("section[aria-labelledby='component-correction-heading']"))
      .ToContainTextAsync(seeded.ScopeId.ToString("D"));
    await page.GetByRole(AriaRole.Link,
      new() { Name = "Continue to the existing new-scope form", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Start or roll forward a consolidation scope", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Group revision 1")).ToBeVisibleAsync();
    var scopeForm = page.Locator("form").Filter(new()
      { Has = page.GetByRole(AriaRole.Button, new() { Name = "Create scope version", Exact = true }) });
    var periodSelector = scopeForm.Locator("select[name='scopePeriod']");
    await Assertions.Expect(periodSelector).ToBeVisibleAsync();
    await periodSelector.SelectOptionAsync(periodId.ToString("D"));
    await scopeForm.Locator("select[name='scopeMethod']").SelectOptionAsync(ConsolidationCalculator.RestrictedMethod);
    await scopeForm.Locator("select[name='scopeCurrency']").SelectOptionAsync("QAR");
    await scopeForm.Locator("select[name='scopeOpeningBasis']").SelectOptionAsync("INITIAL");

    string? newScopeReadbackPath = null;
    string? createRequestPostData = null;
    // Retain both bodies before the creation handoff can navigate away from the form.
    var handoffResponses = await PlaywrightResponseCapture.RunMultipleAsync(page,
    [
      request =>
      {
        if (request.Method != "POST" || new Uri(request.Url).AbsolutePath != "/api/ui/consolidation/scopes")
          return false;
        createRequestPostData = request.PostData;
        return true;
      },
      request =>
      {
        var path = new Uri(request.Url).AbsolutePath;
        if (request.Method != "GET" ||
          !path.StartsWith("/api/ui/consolidation/scopes/", StringComparison.Ordinal) ||
          path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 5 ||
          path == $"/api/ui/consolidation/scopes/{seeded.ScopeId:D}")
          return false;
        newScopeReadbackPath = path;
        return true;
      }
    ],
      () => page.GetByRole(AriaRole.Button, new() { Name = "Create scope version", Exact = true }).ClickAsync());
    var (createResponseStatus, createReceiptBody) = handoffResponses[0];
    Assert.Equal(200, createResponseStatus);
    using var createRequest = JsonDocument.Parse(createRequestPostData ?? throw new InvalidOperationException("The scope request body was not captured."));
    var createInput = createRequest.RootElement;
    Assert.Equal(seeded.GroupId, createInput.GetProperty("groupId").GetGuid());
    Assert.Equal(periodId, createInput.GetProperty("periodId").GetGuid());
    Assert.Equal(originalGroupRevision, createInput.GetProperty("expectedGroupRevision").GetInt64());
    Assert.Equal(ConsolidationCalculator.RestrictedMethod, createInput.GetProperty("method").GetString());
    Assert.Equal("QAR", createInput.GetProperty("reportingCurrency").GetString());
    Assert.Equal("INITIAL_CONSOLIDATION", createInput.GetProperty("openingBasis").GetString());
    Assert.Equal(seeded.ScopeId, createInput.GetProperty("supersedesScopeVersionId").GetGuid());
    Assert.Equal(seeded.ComponentId, createInput.GetProperty("supersedesComponentId").GetGuid());
    Assert.Equal(JsonValueKind.Null, createInput.GetProperty("priorScopeVersionId").ValueKind);
    using var createReceipt = JsonDocument.Parse(createReceiptBody);
    var newScopeId = createReceipt.RootElement.GetProperty("value").GetGuid();
    var newScopeReadback = handoffResponses[1];
    Assert.Equal(200, newScopeReadback.Status);
    Assert.Equal($"/api/ui/consolidation/scopes/{newScopeId:D}", newScopeReadbackPath);
    using (var readback = JsonDocument.Parse(newScopeReadback.Body))
    {
      var scope = readback.RootElement.GetProperty("scope");
      Assert.Equal(newScopeId, scope.GetProperty("id").GetGuid());
      Assert.Equal(seeded.GroupId, scope.GetProperty("groupId").GetGuid());
      Assert.Equal(periodId, scope.GetProperty("periodId").GetGuid());
      Assert.Equal(2, scope.GetProperty("version").GetInt32());
      Assert.Equal(seeded.ScopeId, scope.GetProperty("supersedesScopeVersionId").GetGuid());
      Assert.Equal(seeded.ComponentId, scope.GetProperty("supersedesComponentId").GetGuid());
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = $"{seeded.GroupName} ({seeded.GroupCode}) · v2", Exact = true })).ToBeVisibleAsync();
    var createdScopeSummary = page.Locator("section[aria-labelledby='scope-heading']");
    await Assertions.Expect(createdScopeSummary).ToContainTextAsync("Correction source scope");
    await Assertions.Expect(createdScopeSummary).ToContainTextAsync(seeded.ScopeId.ToString("D"));
    await Assertions.Expect(createdScopeSummary).ToContainTextAsync("Correction source component");
    await Assertions.Expect(createdScopeSummary).ToContainTextAsync(seeded.ComponentId.ToString("D"));

    await using (var proof = host.CreateDbContext())
    {
      var sourceScope = await proof.ConsolidationScopeVersions.AsNoTracking().SingleAsync(x => x.Id == seeded.ScopeId);
      Assert.Equal(1, sourceScope.Version);
      Assert.Equal(originalGroupRevision, sourceScope.GroupRevision);
      Assert.Equal(AccountingWorkflowStates.Approved, sourceScope.Status);
      Assert.Null(sourceScope.SupersedesScopeVersionId);
      Assert.Null(sourceScope.SupersedesComponentId);
      var createdScope = await proof.ConsolidationScopeVersions.AsNoTracking().SingleAsync(x => x.Id == newScopeId);
      Assert.Equal(seeded.ScopeId, createdScope.SupersedesScopeVersionId);
      Assert.Equal(seeded.ComponentId, createdScope.SupersedesComponentId);
      Assert.Empty(await proof.ConsolidationComponents.AsNoTracking().Where(x => x.ScopeVersionId == newScopeId).ToListAsync());
      Assert.Empty(await proof.ConsolidationRuns.AsNoTracking().Where(x => x.ScopeVersionId == newScopeId).ToListAsync());

      var sourceComponent = await proof.ConsolidationComponents.AsNoTracking().SingleAsync(x => x.Id == seeded.ComponentId);
      Assert.Equal(AccountingWorkflowStates.Approved, sourceComponent.Status);
      Assert.Equal(externalPackId, sourceComponent.ExternalComponentPackId);
      Assert.Equal(componentPackageHash, sourceComponent.PackageHash);
      Assert.Equal(approvedByUserId, sourceComponent.ApprovedByUserId);
      Assert.Equal(approvedAt, sourceComponent.ApprovedAt);
      var sourcePack = await proof.ExternalComponentPacks.AsNoTracking().SingleAsync(x => x.Id == externalPackId);
      Assert.Equal(ExternalComponentPackStates.Approved, sourcePack.Status);
      Assert.Equal(externalPackDigest, sourcePack.PackDigest);

      var retainedRun = await proof.ConsolidationRuns.AsNoTracking().SingleAsync(x => x.Id == runId);
      Assert.Equal(seeded.ScopeId, retainedRun.ScopeVersionId);
      Assert.Equal(AccountingWorkflowStates.Approved, retainedRun.Status);
      Assert.Equal(runHash, retainedRun.RunHash);
      var retainedRunLineIds = await proof.ConsolidationRunLines.AsNoTracking().Where(x => x.RunId == runId)
        .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
      Assert.Equal(originalRunLineIds, retainedRunLineIds);
    }
    Assert.Empty(pageErrors);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", ScopeCaseId)]
  public async Task ScopeApprovalReadinessTracksTheCurrentGroupPerimeter(bool approveReadyScope)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: ScopeCaseId);
    var f = host.Fixture;
    var seeded = await SeedReadinessFixtureAsync(host);
    var addedClientId = Guid.NewGuid();
    var checker = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    var methodOwner = PbcSeed.User(f.FirmId, "Staff");

    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(methodOwner);
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, GroupId = seeded.GroupId, UserId = methodOwner.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = f.Admin.Id
      });
      await db.SaveChangesAsync();

      var groupCapability = await ClientAccountingService.CreateCapabilityProfileAsync(db, checker,
        new CapabilityProfileRequest(null, seeded.GroupId, AccountingCapabilityServiceKinds.GroupReporting,
          "IFRS", "2026", "ANNUAL", "QAR", "STATUTORY", ConsolidationCalculator.RestrictedMethod,
          "PARTNER", "GROUP"));
      Assert.True(groupCapability.Succeeded, groupCapability.Message);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, checker, groupCapability.Value,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "synthetic-e2e-group-profile-local")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db,
        PbcSeed.Actor(methodOwner, "Partner"), groupCapability.Value,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "synthetic-e2e-group-method-owner")).Succeeded);

      var componentApproval = await ConsolidationService.ApproveComponentAsync(db, checker, seeded.ComponentId);
      Assert.True(componentApproval.Succeeded, componentApproval.Message);
    }

    await using (var proof = host.CreateDbContext())
    {
      var component = await proof.ConsolidationComponents.AsNoTracking().SingleAsync(x => x.Id == seeded.ComponentId);
      Assert.Equal(AccountingWorkflowStates.Approved, component.Status);
      Assert.Equal(f.Reviewer.Id, component.ApprovedByUserId);
      Assert.NotNull(component.ApprovedAt);
      Assert.Equal(AccountingWorkflowStates.Draft,
        await proof.ConsolidationScopeVersions.AsNoTracking().Where(x => x.Id == seeded.ScopeId)
          .Select(x => x.Status).SingleAsync());
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH") is { Length: > 0 } buildPath)
      settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    var path = $"/app/consolidation/scopes/{seeded.ScopeId:D}";

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    var scopeApprovalRequests = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    await page.RouteAsync("**/api/ui/consolidation/scopes/*/approve", async route =>
    {
      if (route.Request.Method == "POST")
      {
        scopeApprovalRequests.Add(route.Request.Url);
        if (approveReadyScope) await route.ContinueAsync();
        else await route.AbortAsync();
      }
      else
        await route.ContinueAsync();
    });

    var initialResponse = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && new Uri(request.Url).AbsolutePath == $"/api/ui/consolidation/scopes/{seeded.ScopeId:D}",
      () => page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path)));
    Assert.Equal(200, initialResponse.Status);
    using (var initialWorkspace = JsonDocument.Parse(initialResponse.Body))
    {
      Assert.True(initialWorkspace.RootElement.GetProperty("canApproveScope").GetBoolean());
      Assert.Equal(JsonValueKind.Null, initialWorkspace.RootElement.GetProperty("scopeApprovalBlocker").ValueKind);
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = $"{seeded.GroupName} ({seeded.GroupCode}) · v1", Exact = true })).ToBeVisibleAsync();

    var components = page.Locator("#components");
    var componentRow = components.GetByRole(AriaRole.Row).Filter(new() { HasText = "PBC TEST CLIENT" });
    await Assertions.Expect(componentRow).ToHaveCountAsync(1);
    await Assertions.Expect(componentRow.Locator("td").Nth(5).Locator("audit-status .status-chip")).ToHaveTextAsync("approved");
    var approveScope = page.GetByRole(AriaRole.Button, new() { Name = "Approve perimeter", Exact = true });
    await Assertions.Expect(approveScope).ToBeEnabledAsync();
    Assert.Null(await approveScope.GetAttributeAsync("aria-describedby"));

    if (approveReadyScope)
    {
      var approvalResponse = page.WaitForResponseAsync(response => response.Request.Method == "POST" &&
        response.Url.EndsWith($"/api/ui/consolidation/scopes/{seeded.ScopeId:D}/approve", StringComparison.Ordinal));
      await approveScope.PressAsync("Enter");
      Assert.Equal(200, (await approvalResponse).Status);
      await Assertions.Expect(page.GetByText("Perimeter approved.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(approveScope).ToHaveCountAsync(0);
      await using var proof = host.CreateDbContext();
      var approved = await proof.ConsolidationScopeVersions.AsNoTracking().SingleAsync(x => x.Id == seeded.ScopeId);
      Assert.Equal(AccountingWorkflowStates.Approved, approved.Status);
      Assert.Equal(f.Reviewer.Id, approved.ApprovedByUserId);
      Assert.NotNull(approved.ApprovedAt);
      Assert.Single(scopeApprovalRequests);
      Assert.Empty(pageErrors);
      return;
    }

    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = addedClientId, FirmId = f.FirmId, LegalName = "Synthetic later group member", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var addedMembership = await ConsolidationService.AddMembershipAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new GroupMembershipRequest(seeded.GroupId, addedClientId, new DateOnly(2020, 1, 1), null,
          "CONTROLLED", 100m, 100m, "SYNTHETIC-LATER-MEMBERSHIP"));
      Assert.True(addedMembership.Succeeded, addedMembership.Message);
    }

    await using (var proof = host.CreateDbContext())
    {
      Assert.Equal(2, await proof.ClientGroups.AsNoTracking().Where(x => x.Id == seeded.GroupId)
        .Select(x => x.Revision).SingleAsync());
      Assert.Equal(1, await proof.ConsolidationScopeVersions.AsNoTracking().Where(x => x.Id == seeded.ScopeId)
        .Select(x => x.GroupRevision).SingleAsync());
    }

    var refreshedResponse = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && new Uri(request.Url).AbsolutePath == $"/api/ui/consolidation/scopes/{seeded.ScopeId:D}",
      () => page.ReloadAsync());
    Assert.Equal(200, refreshedResponse.Status);
    using (var staleWorkspace = JsonDocument.Parse(refreshedResponse.Body))
    {
      Assert.False(staleWorkspace.RootElement.GetProperty("canApproveScope").GetBoolean());
      Assert.Contains("group perimeter changed", staleWorkspace.RootElement.GetProperty("scopeApprovalBlocker").GetString(),
        StringComparison.OrdinalIgnoreCase);
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = $"{seeded.GroupName} ({seeded.GroupCode}) · v1", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(componentRow).ToHaveCountAsync(1);
    await Assertions.Expect(componentRow.Locator("td").Nth(5).Locator("audit-status .status-chip")).ToHaveTextAsync("approved");
    await Assertions.Expect(approveScope).ToBeDisabledAsync();
    var blockerReference = await approveScope.GetAttributeAsync("aria-describedby");
    Assert.False(string.IsNullOrWhiteSpace(blockerReference));
    var blockerId = blockerReference!.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    var blocker = page.Locator($"#{blockerId}");
    await Assertions.Expect(blocker).ToBeVisibleAsync();
    await Assertions.Expect(blocker).ToContainTextAsync("group perimeter changed");
    await Assertions.Expect(blocker).ToContainTextAsync("new scope version");

    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>(
      "() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "The consolidation scope approval workspace must not overflow the document at 390px.");
    await approveScope.DispatchEventAsync("click");
    await page.EvaluateAsync<bool>(
      "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(true))))");
    Assert.Empty(scopeApprovalRequests);
    Assert.Empty(pageErrors);

    await using (var proof = host.CreateDbContext())
    {
      var scope = await proof.ConsolidationScopeVersions.AsNoTracking().SingleAsync(x => x.Id == seeded.ScopeId);
      Assert.Equal(AccountingWorkflowStates.Draft, scope.Status);
      Assert.Null(scope.ApprovedByUserId);
      Assert.Null(scope.ApprovedAt);
    }

    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "consolidation-perimeter-readiness");
  }

  private sealed record ReadinessFixture(Guid GroupId, Guid ScopeId, Guid ComponentId, string GroupCode, string GroupName);

  private static async Task<ReadinessFixture> SeedReadinessFixtureAsync(OwnedHost host)
  {
    var f = host.Fixture;
    var groupId = Guid.NewGuid();
    var scopeId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    const string groupCode = "SYN-COMP-READY-01";
    const string groupName = "Synthetic component readiness group";
    const string sourceReference = "SYN-COMP-READY-SOURCE-01";
    const string lineManifest = "CASH|100.000000|QAR|SYN-COMP-READY-LINE-001\nREVENUE|-100.000000|QAR|SYN-COMP-READY-LINE-002";
    var now = DateTimeOffset.UtcNow;
    var lines = new[]
    {
      new ExternalComponentPackLineRequest("CASH", 100m, "QAR", "SYN-COMP-READY-LINE-001"),
      new ExternalComponentPackLineRequest("REVENUE", -100m, "QAR", "SYN-COMP-READY-LINE-002")
    };

    await using (var db = host.CreateDbContext())
    {
      db.ClientGroups.Add(new ClientGroup
      {
        Id = groupId, FirmId = f.FirmId, Code = groupCode, Name = groupName, Revision = 1,
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      db.GroupAccessGrants.AddRange(
        new GroupAccessGrant
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, GroupId = groupId, UserId = f.Staff.Id,
          Role = "AccountingPreparer", GrantedAt = now, GrantedByUserId = f.Admin.Id
        },
        new GroupAccessGrant
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, GroupId = groupId, UserId = f.Reviewer.Id,
          Role = "AccountingReviewer", GrantedAt = now, GrantedByUserId = f.Admin.Id
        },
        new GroupAccessGrant
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, GroupId = groupId, UserId = f.Admin.Id,
          Role = "Administrator", GrantedAt = now, GrantedByUserId = f.Admin.Id
        });
      db.ClientGroupMemberships.Add(new ClientGroupMembership
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, GroupId = groupId, ClientId = f.ClientId,
        EffectiveFrom = new DateOnly(2020, 1, 1), Status = AccountingWorkflowStates.Approved,
        OwnershipPercent = 100m, EconomicInterestPercent = 100m, ControlMethod = "CONTROLLED",
        EvidenceReference = "SYNTHETIC-INITIAL-MEMBERSHIP",
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "FY2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
      {
        Id = scopeId, FirmId = f.FirmId, GroupId = groupId, PeriodId = periodId, Version = 1,
        GroupRevision = 1, ReportingCurrency = "QAR", Method = ConsolidationCalculator.RestrictedMethod,
        OpeningBasis = "SYNTHETIC-INITIAL-CONSOLIDATION", Status = AccountingWorkflowStates.Draft,
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      await db.SaveChangesAsync();
    }

    var maker = PbcSeed.Actor(f.Staff, "AccountingPreparer");
    var checker = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    Guid externalPackId;
    Guid componentId;
    await using (var db = host.CreateDbContext())
    {
      var submittedPack = await ConsolidationService.SubmitExternalComponentPackAsync(db, maker,
        new ExternalComponentPackRequest(scopeId, f.ClientId, f.EngagementId, "2026-01-01", "2026-12-31",
          "IFRS", "QAR", "STATUTORY", "synthetic-taxonomy-v1", "synthetic-mapping-v1", sourceReference,
          Hashing.Sha256Hex("synthetic raw source " + sourceReference), Hashing.Sha256Hex(lineManifest), 0m, lines,
          ExpectedGroupRevision: 1));
      Assert.True(submittedPack.Succeeded, submittedPack.Message);
      externalPackId = submittedPack.Value;

      var reconciledPack = await ConsolidationService.ReconcileExternalComponentPackAsync(db, checker,
        new ExternalComponentReconciliationRequest(externalPackId, "SYNTHETIC-SOURCE-RECONCILIATION-01",
          ExpectedGroupRevision: 1));
      Assert.True(reconciledPack.Succeeded, reconciledPack.Message);
      var approvedPack = await ConsolidationService.ApproveExternalComponentPackAsync(
        db, checker, externalPackId, expectedGroupRevision: 1);
      Assert.True(approvedPack.Succeeded, approvedPack.Message);

      var submittedComponent = await ConsolidationService.SubmitExternalComponentAsync(db, maker,
        new ExternalComponentRequest(scopeId, externalPackId, ExpectedGroupRevision: 1));
      Assert.True(submittedComponent.Succeeded, submittedComponent.Message);
      componentId = submittedComponent.Value;
    }

    await using (var proof = host.CreateDbContext())
    {
      var pack = await proof.ExternalComponentPacks.AsNoTracking().SingleAsync(x => x.Id == externalPackId);
      Assert.Equal(ExternalComponentPackStates.Approved, pack.Status);
      Assert.Equal(ExternalComponentReconciliationStates.Reconciled, pack.ReconciliationStatus);
      var component = await proof.ConsolidationComponents.AsNoTracking().SingleAsync(x => x.Id == componentId);
      Assert.Equal(AccountingWorkflowStates.Submitted, component.Status);
      Assert.Equal(f.Staff.Id, component.SubmittedByUserId);
      Assert.Null(component.ApprovedByUserId);
      Assert.Null(component.ApprovedAt);
    }

    return new ReadinessFixture(groupId, scopeId, componentId, groupCode, groupName);
  }
}
