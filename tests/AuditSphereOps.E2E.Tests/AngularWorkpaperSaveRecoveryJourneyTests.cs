using System.Collections.Concurrent;
using System.Text.Json;
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
    await page.RouteAsync($"**/api/ui/audit/workpapers/{workpaperId:D}/commands/actions", route =>
    {
      if (Action(route.Request) != "DISCARD_DRAFT") return route.ContinueAsync();
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
    await ConfirmReviewedAction(page, "DISCARD_DRAFT");
    await Assertions.Expect(dialog.GetByRole(AriaRole.Alert).GetByText(
      "This record changed since you loaded it. Refresh and review the current revision.", new() { Exact = true }))
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
    await ConfirmReviewedAction(page, "DISCARD_DRAFT");
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
    await ConfirmReviewedAction(page, "DISCARD_DRAFT", keyboard: true);
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

    await OwnedBrowserFixtureHandoff.WaitAsync(origin, $"/auth/sign-in?returnUrl={Uri.EscapeDataString(path)}", "workpaper-command");

    var work = "Reviewed work performed after source inspection.";
    var conclusion = "The selected evidence supports this conclusion.";
    var writeCount = 0;
    var draftUrl = $"**/api/ui/audit/workpapers/{workpaperId:D}/commands/actions";
    await page.RouteAsync(draftUrl, async route =>
    {
      if (Action(route.Request) != "SAVE_DRAFT") { await route.ContinueAsync(); return; }
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
      new() { Name = "Workpaper request recovery", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Submit for review", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Save draft", Exact = true })).ToBeDisabledAsync();

    await recovery.GetByRole(AriaRole.Button, new() { Name = "Look up original workpaper receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The original workpaper receipt was verified. Refreshing current persisted state.", new() { Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(recovery).ToHaveCountAsync(0);
    Assert.Equal(1, writeCount);

    await page.GetByRole(AriaRole.Button, new() { Name = "Submit for review", Exact = true }).ClickAsync();
    await ConfirmReviewedAction(page, "SUBMIT");
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

  [Fact]
  [Trait("CaseId", "AS-PAR-002-WP-INFLIGHT-01")]
  public async Task InFlightSaveThenStayPersistsNewerEditsOnceWithoutNavigating()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-WP-INFLIGHT-01");
    var f = host.Fixture;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    Guid workpaperId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
      var created = await AuditPlanningService.CreateWorkpaperAsync(db, staff,
        new CreateWorkpaperRequest(f.EngagementId, "WP-INFLIGHT-STAY", "Synthetic in-flight save workpaper",
          "Preserve edits across pending save and navigation", "INFLIGHT-STAY-v1", null, "Perform a synthetic control"));
      Assert.True(created.Succeeded, created.Message);
      workpaperId = created.Value!.WorkpaperId;
      var initial = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(),
          "Seed work performed.", "Seed conclusion."));
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
    page.PageError += (_, error) => errors.Add(error);
    page.Console += (_, message) =>
    {
      if (message.Type == "error") errors.Add($"console-error: {message.Text}");
    };

    var firstSaveReachedDatabase = new TaskCompletionSource<(int Status, Guid RequestId, string Work, string Conclusion)>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    var secondSaveAcknowledged = new TaskCompletionSource<(int Status, Guid RequestId, string Work, string Conclusion)>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirstResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var saveIntents = new ConcurrentQueue<(Guid RequestId, string Work, string Conclusion)>();
    var saveRequestCount = 0;
    var saveUrl = $"**/api/ui/audit/workpapers/{workpaperId:D}/commands/actions";
    await page.RouteAsync(saveUrl, async route =>
    {
      if (Action(route.Request) != "SAVE_DRAFT") { await route.ContinueAsync(); return; }

      var count = Interlocked.Increment(ref saveRequestCount);
      using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
      var requestId = Guid.Parse(body.RootElement.GetProperty("requestId").GetString()!);
      var fields = body.RootElement.GetProperty("fields").GetProperty("save");
      var work = fields.GetProperty("workPerformed").GetString()!;
      var conclusion = fields.GetProperty("conclusion").GetString()!;
      saveIntents.Enqueue((requestId, work, conclusion));

      var response = await route.FetchAsync();
      var result = (response.Status, requestId, work, conclusion);
      if (count == 1)
      {
        firstSaveReachedDatabase.TrySetResult(result);
        await releaseFirstResponse.Task.WaitAsync(TimeSpan.FromSeconds(30));
      }

      await route.FulfillAsync(new RouteFulfillOptions { Response = response });
      if (count == 2) secondSaveAcknowledged.TrySetResult(result);
    });

    var path = $"/app/audit/workpapers/{workpaperId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Workpaper: Synthetic in-flight save workpaper", Exact = true })).ToBeVisibleAsync();

    const string originalWork = "The first in-flight save snapshot.";
    const string originalConclusion = "The original save conclusion.";
    const string newerWork = "Newer work entered while the original save is pending.";
    const string newerConclusion = "Newer conclusion entered while the original save is pending.";
    await page.GetByLabel("Work performed", new() { Exact = true }).FillAsync(originalWork);
    await page.GetByLabel("Conclusion", new() { Exact = true }).FillAsync(originalConclusion);
    try
    {
      await page.GetByRole(AriaRole.Button, new() { Name = "Save draft", Exact = true }).ClickAsync();
      var first = await firstSaveReachedDatabase.Task.WaitAsync(TimeSpan.FromSeconds(30));
      Assert.Equal(200, first.Status);
      Assert.Equal(originalWork, first.Work);
      Assert.Equal(originalConclusion, first.Conclusion);
      await using (var proof = host.CreateDbContext())
      {
        var committed = await proof.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
        Assert.Equal(2, committed.DraftRevision);
        Assert.Equal(originalWork, committed.WorkPerformed);
        Assert.Equal(originalConclusion, committed.Conclusion);
      }

      var workPerformed = page.GetByLabel("Work performed", new() { Exact = true });
      var conclusionField = page.GetByLabel("Conclusion", new() { Exact = true });
      Assert.True(await workPerformed.IsEnabledAsync());
      Assert.True(await conclusionField.IsEnabledAsync());
      await workPerformed.FillAsync(newerWork);
      await conclusionField.FillAsync(newerConclusion);
      await Assertions.Expect(page.GetByLabel("Work performed", new() { Exact = true })).ToHaveValueAsync(newerWork);
      await Assertions.Expect(page.GetByLabel("Conclusion", new() { Exact = true })).ToHaveValueAsync(newerConclusion);

      await page.GetByRole(AriaRole.Link, new() { Name = "Audit plan", Exact = true }).Last.ClickAsync();
      Assert.Contains(path, page.Url, StringComparison.Ordinal);
      Assert.Equal(1, Volatile.Read(ref saveRequestCount));
      Assert.Equal(0, await page.GetByRole(AriaRole.Dialog).CountAsync());

      releaseFirstResponse.TrySetResult();
      var dialog = page.GetByRole(AriaRole.Dialog);
      await Assertions.Expect(dialog.GetByRole(AriaRole.Heading,
        new() { Name = "Unsubmitted edits", Exact = true })).ToBeVisibleAsync();
      Assert.Equal(1, Volatile.Read(ref saveRequestCount));
      await dialog.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync();
      Assert.Contains(path, page.Url, StringComparison.Ordinal);
      await Assertions.Expect(page.GetByLabel("Work performed", new() { Exact = true })).ToHaveValueAsync(newerWork);
      await Assertions.Expect(page.GetByLabel("Conclusion", new() { Exact = true })).ToHaveValueAsync(newerConclusion);

      var second = await secondSaveAcknowledged.Task.WaitAsync(TimeSpan.FromSeconds(30));
      Assert.Equal(200, second.Status);
      Assert.NotEqual(first.RequestId, second.RequestId);
      Assert.Equal(newerWork, second.Work);
      Assert.Equal(newerConclusion, second.Conclusion);
      await page.WaitForTimeoutAsync(1_000); // Longer than the 750 ms autosave debounce; no repeat should appear.

      Assert.Equal(2, Volatile.Read(ref saveRequestCount));
      Assert.Equal(2, saveIntents.Count);
      Assert.Equal((first.RequestId, originalWork, originalConclusion), saveIntents.ElementAt(0));
      Assert.Equal((second.RequestId, newerWork, newerConclusion), saveIntents.ElementAt(1));
      Assert.Contains(path, page.Url, StringComparison.Ordinal);
      await using (var proof = host.CreateDbContext())
      {
        var saved = await proof.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
        Assert.Equal("ACTIVE", saved.Lifecycle);
        Assert.Equal(3, saved.DraftRevision);
        Assert.Equal(newerWork, saved.WorkPerformed);
        Assert.Equal(newerConclusion, saved.Conclusion);
        Assert.Empty(await proof.WorkpaperSubmissions.AsNoTracking().Where(x => x.WorkpaperId == workpaperId).ToListAsync());
      }
      Assert.Empty(errors);
    }
    finally
    {
      // Do not leave the intercepted request blocked if an earlier assertion fails.
      releaseFirstResponse.TrySetResult();
    }
  }

  private static string Action(IRequest request)
  {
    using var body = JsonDocument.Parse(request.PostData ?? "{}");
    return body.RootElement.GetProperty("fields").GetProperty("action").GetString()!;
  }

  private static async Task ConfirmReviewedAction(IPage page, string action, bool keyboard = false)
  {
    var review = page.GetByRole(AriaRole.Dialog).Filter(new LocatorFilterOptions
    { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Review exact workpaper action", Exact = true }) });
    await Assertions.Expect(review.GetByRole(AriaRole.Region, new() { Name = "Exact workpaper preview", Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(review.GetByText(action + " ·", new() { Exact = false })).ToBeVisibleAsync();
    var confirm = review.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed workpaper action", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    var assent = review.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this exact workpaper, acknowledged draft and proposed effect.", Exact = true });
    if (keyboard)
    {
      foreach (var width in new[] { 320, 390, 768, 1024, 1440 })
      {
        await page.SetViewportSizeAsync(width, 900);
        await page.WaitForFunctionAsync("w => document.documentElement.clientWidth === w", width);
        Assert.True(await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth") <= width + 1, $"Workpaper review overflows at {width}px.");
      }
      await assent.FocusAsync(); await assent.PressAsync("Space"); await Assertions.Expect(assent).ToBeCheckedAsync();
      await confirm.FocusAsync(); await confirm.PressAsync("Enter");
    }
    else { await assent.CheckAsync(); await confirm.ClickAsync(); }
  }

}
