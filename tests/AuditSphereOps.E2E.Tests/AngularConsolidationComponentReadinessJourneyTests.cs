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

  [Fact]
  [Trait("CaseId", CaseId)]
  public async Task ReviewerApprovalReadinessTracksTheCurrentGroupPerimeter()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: CaseId);
    var f = host.Fixture;
    var groupId = Guid.NewGuid();
    var scopeId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var addedClientId = Guid.NewGuid();
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
    Guid componentId;
    Guid externalPackId;
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
      response.Url.Contains($"/api/ui/consolidation/scopes/{scopeId:D}", StringComparison.Ordinal));
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    Assert.Equal(200, (await initialWorkspaceResponse).Status);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = $"{groupName} ({groupCode}) · v1", Exact = true })).ToBeVisibleAsync();

    var components = page.Locator("#components");
    var componentRow = components.GetByRole(AriaRole.Row).Filter(new() { HasText = "PBC TEST CLIENT" });
    await Assertions.Expect(componentRow).ToHaveCountAsync(1);
    await Assertions.Expect(componentRow.Locator("audit-status .status-chip")).ToHaveTextAsync("submitted");
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
      response.Url.Contains($"/api/ui/consolidation/scopes/{scopeId:D}", StringComparison.Ordinal));
    await page.ReloadAsync();
    Assert.Equal(200, (await refreshedWorkspaceResponse).Status);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = $"{groupName} ({groupCode}) · v1", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(componentRow).ToHaveCountAsync(1);
    await Assertions.Expect(componentRow.Locator("audit-status .status-chip")).ToHaveTextAsync("submitted");
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
}
