using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularWorkpaperSaveRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-WORKPAPER-DISCARD-REFUSAL-01")]
  public async Task RefusedNavigationDiscardKeepsUserAndActiveDraftInPlace()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-WORKPAPER-DISCARD-REFUSAL-01");
    var f = host.Fixture;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    Guid workpaperId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
      var created = await AuditPlanningService.CreateWorkpaperAsync(db, staff,
        new CreateWorkpaperRequest(f.EngagementId, "WP-DISCARD-REFUSAL", "Synthetic discard refusal workpaper",
          "Keep the active draft on refusal", "DISCARD-REFUSAL-v1", null, "Perform a synthetic control"));
      Assert.True(created.Succeeded, created.Message);
      workpaperId = created.Value!.WorkpaperId;
      var saved = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(),
          "Active draft remains until accepted discard.", "Retain this conclusion."));
      Assert.True(saved.Succeeded, saved.Message);
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    var discardRefusalInjected = false;
    page.PageError += (_, error) => errors.Add(error);
    page.Console += (_, message) =>
    {
      if (message.Type == "error" && !(discardRefusalInjected && message.Text.Contains("409", StringComparison.Ordinal)))
        errors.Add($"console-error: {message.Text}");
    };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/audit/workpapers/{workpaperId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Workpaper: Synthetic discard refusal workpaper", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Work performed", new() { Exact = true }).FillAsync("These edits are still local.");
    await page.RouteAsync($"**/api/ui/audit/workpapers/{workpaperId:D}/draft/discard", route =>
    {
      discardRefusalInjected = true;
      return route.FulfillAsync(new RouteFulfillOptions
      {
        Status = 409,
        ContentType = "application/json",
        Body = "{\"code\":\"revision.stale\",\"message\":\"Synthetic stale discard refusal.\"}"
      });
    });
    await page.GetByRole(AriaRole.Link, new() { Name = "Audit plan", Exact = true }).Last.ClickAsync();
    var dialog = page.GetByRole(AriaRole.Dialog);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Unsubmitted edits", Exact = true }))
      .ToBeVisibleAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Discard edits and continue", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("This record changed since you loaded it. Refresh and review the current revision.", new() { Exact = true }))
      .ToBeVisibleAsync();
    Assert.Contains($"/app/audit/workpapers/{workpaperId:D}", page.Url, StringComparison.Ordinal);
    await using (var proof = host.CreateDbContext())
    {
      var active = await proof.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal("ACTIVE", active.Lifecycle);
      Assert.Equal("Active draft remains until accepted discard.", active.WorkPerformed);
      Assert.Equal("Retain this conclusion.", active.Conclusion);
      Assert.Equal(1, active.DraftRevision);
      Assert.Empty(await proof.WorkpaperSubmissions.AsNoTracking().Where(x => x.WorkpaperId == workpaperId).ToListAsync());
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-WORKPAPER-NAV-DISCARD-01")]
  public async Task DiscardingBeforeNavigationDiscardsSavedDraftAndDoesNotPersistCurrentEdits()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-WORKPAPER-NAV-DISCARD-01");
    var f = host.Fixture;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    Guid workpaperId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
      var created = await AuditPlanningService.CreateWorkpaperAsync(db, staff,
        new CreateWorkpaperRequest(f.EngagementId, "WP-NAV-DISCARD", "Synthetic navigation discard workpaper",
          "Prove navigation discard lifecycle", "NAV-DISCARD-v1", null, "Perform a synthetic control"));
      Assert.True(created.Succeeded, created.Message);
      workpaperId = created.Value!.WorkpaperId;
      var saved = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(),
          "Previously saved draft content.", "Previously saved draft conclusion."));
      Assert.True(saved.Succeeded, saved.Message);
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    page.Console += (_, message) =>
    {
      if (message.Type == "error") errors.Add($"console-error: {message.Text}");
    };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/audit/workpapers/{workpaperId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Workpaper: Synthetic navigation discard workpaper", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Work performed", new() { Exact = true }).FillAsync("Unsubmitted edits must not be persisted.");
    await page.GetByRole(AriaRole.Link, new() { Name = "Audit plan", Exact = true }).Last.ClickAsync();
    var dialog = page.GetByRole(AriaRole.Dialog);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Unsubmitted edits", Exact = true }))
      .ToBeVisibleAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Discard edits and continue", Exact = true }).ClickAsync();
    await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/app/engagements/{f.EngagementId:D}/audit-plan$"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Audit plan & strategy", Exact = true })).ToBeVisibleAsync();
    await using (var proof = host.CreateDbContext())
    {
      var discarded = await proof.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal("DISCARDED", discarded.Lifecycle);
      Assert.Equal("Previously saved draft content.", discarded.WorkPerformed);
      Assert.Equal("Previously saved draft conclusion.", discarded.Conclusion);
      Assert.DoesNotContain("Unsubmitted edits must not be persisted.", discarded.WorkPerformed, StringComparison.Ordinal);
      Assert.Empty(await proof.WorkpaperSubmissions.AsNoTracking().Where(x => x.WorkpaperId == workpaperId).ToListAsync());
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-WORKPAPER-DISCARD-01")]
  public async Task DiscardDraftFromWorkpaperPagePersistsDiscardedLifecycle()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-WORKPAPER-DISCARD-01");
    var f = host.Fixture;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    Guid workpaperId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
      var created = await AuditPlanningService.CreateWorkpaperAsync(db, staff,
        new CreateWorkpaperRequest(f.EngagementId, "WP-DISCARD", "Synthetic discard workpaper",
          "Prove discarded draft state", "DISCARD-v1", null, "Perform a synthetic control"));
      Assert.True(created.Succeeded, created.Message);
      workpaperId = created.Value!.WorkpaperId;
      var saved = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(),
          "Disposable draft work performed.", "Disposable draft conclusion."));
      Assert.True(saved.Succeeded, saved.Message);
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    page.Console += (_, message) =>
    {
      if (message.Type == "error") errors.Add($"console-error: {message.Text}");
    };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/audit/workpapers/{workpaperId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Workpaper: Synthetic discard workpaper", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByLabel("Work performed", new() { Exact = true }))
      .ToHaveValueAsync("Disposable draft work performed.");

    await page.GetByRole(AriaRole.Button, new() { Name = "Discard draft", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Draft discarded.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByLabel("Work performed", new() { Exact = true })).ToHaveValueAsync("");
    await using (var proof = host.CreateDbContext())
    {
      var discarded = await proof.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal("DISCARDED", discarded.Lifecycle);
      Assert.Equal("Disposable draft work performed.", discarded.WorkPerformed);
      Assert.Equal("Disposable draft conclusion.", discarded.Conclusion);
      Assert.Equal(1, discarded.DraftRevision);
      Assert.Empty(await proof.WorkpaperSubmissions.AsNoTracking().Where(x => x.WorkpaperId == workpaperId).ToListAsync());
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-WORKPAPER-SAVE-RECOVERY-01")]
  public async Task LostDraftSaveResponseIsReconciledBeforeSubmission()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-WORKPAPER-SAVE-RECOVERY-01");
    var f = host.Fixture;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    Guid workpaperId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
      var created = await AuditPlanningService.CreateWorkpaperAsync(db, staff,
        new CreateWorkpaperRequest(f.EngagementId, "WP-RECOVERY", "Synthetic recovery workpaper",
          "Prove exact save reconciliation", "RECOVERY-v1", null, "Perform a synthetic control"));
      Assert.True(created.Succeeded, created.Message);
      workpaperId = created.Value!.WorkpaperId;
      var initial = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(),
          "Original work performed.", "Original conclusion."));
      Assert.True(initial.Succeeded, initial.Message);
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    var lostAcknowledgementInjected = false;
    page.PageError += (_, error) => errors.Add(error);
    page.Console += (_, message) =>
    {
      if (message.Type != "error") return;
      // The routed POST is intentionally aborted after the API commits; its browser
      // network error is expected, while unrelated console errors still fail the journey.
      if (lostAcknowledgementInjected && message.Text.Contains("net::ERR_FAILED", StringComparison.Ordinal)) return;
      errors.Add($"console-error: {message.Text}");
    };
    var path = $"/app/audit/workpapers/{workpaperId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Workpaper: Synthetic recovery workpaper", Exact = true })).ToBeVisibleAsync();

    var work = "Reviewed work performed after source inspection.";
    var conclusion = "The selected evidence supports this conclusion.";
    var writeCount = 0;
    var draftUrl = $"**/api/ui/audit/workpapers/{workpaperId:D}/draft";
    await page.RouteAsync(draftUrl, async route =>
    {
      var response = await route.FetchAsync();
      Assert.Equal(200, response.Status);
      writeCount++;
      // The API commits successfully; only the browser acknowledgement is lost.
      lostAcknowledgementInjected = true;
      await route.AbortAsync("failed");
    });
    await page.GetByLabel("Work performed", new() { Exact = true }).FillAsync(work);
    await page.GetByLabel("Conclusion", new() { Exact = true }).FillAsync(conclusion);
    var recovery = page.GetByRole(AriaRole.Region,
      new() { Name = "Workpaper save recovery", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Submit for review", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Save draft", Exact = true })).ToBeDisabledAsync();

    await recovery.GetByRole(AriaRole.Button, new() { Name = "Check saved draft", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The persisted draft confirms that save.", new() { Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(recovery).ToHaveCountAsync(0);
    Assert.Equal(1, writeCount);

    await page.GetByRole(AriaRole.Button, new() { Name = "Submit for review", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The submission was recorded.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var proof = host.CreateDbContext())
    {
      var draft = await proof.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal(2, draft.DraftRevision);
      Assert.Equal(work, draft.WorkPerformed);
      Assert.Equal(conclusion, draft.Conclusion);
      Assert.Equal("CONSUMED", draft.Lifecycle);
      var submission = await proof.WorkpaperSubmissions.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal(work, submission.WorkPerformed);
      Assert.Equal(conclusion, submission.Conclusion);
    }
    Assert.Empty(errors);
  }
}
