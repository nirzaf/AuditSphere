using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularReviewPointCommandJourneyTests
{
  public static TheoryData<string, string> Actions => new()
  {
    { "CLEAR", "success" }, { "CLEAR", "lost-committed" }, { "CLEAR", "lost-uncommitted" },
    { "REOPEN", "success" }, { "REOPEN", "lost-committed" }, { "REOPEN", "lost-uncommitted" }
  };
  [Theory, MemberData(nameof(Actions))]
  [Trait("CaseId", "AS-ANG-REVIEW-POINT-RECEIPT-01")]
  public async Task DispositionsRequireFreshReviewAndRecoverTheSameRequest(string action, string mode)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-ANG-REVIEW-POINT-RECEIPT-01");
    var world = await ReviewPointCommandReceiptSeed.SeedAsync(host.Database); var f = world.Fixture; var pointId = world.ReviewPointId;
    long initialRevision; int initialEvents; Guid targetId;
    await using (var setup = host.CreateDbContext())
    {
      if (action == "REOPEN")
      {
        var clear = await AuditPlanningService.SetReviewPointDispositionAsync(setup, world.Staff, pointId, true, 1L);
        Assert.True(clear.Succeeded, clear.Message);
      }
      var point = await setup.ReviewPoints.AsNoTracking().SingleAsync(x => x.Id == pointId);
      initialRevision = point.DispositionRevision; targetId = point.TargetId;
      initialEvents = await setup.ReviewPointDispositionEvents.CountAsync(x => x.ReviewPointId == pointId);
    }
    var origin = await StartAsync(host, f); var path = $"/app/reviews/{pointId:D}";
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); var identities = new List<(string Id, string Hash)>();
    page.PageError += (_, error) => errors.Add(error);
    var actionsUrl = $"/api/ui/reviews/{pointId:D}/commands/actions";
    page.Request += (_, request) =>
    {
      if (request.Method != "POST" || !request.Url.EndsWith(actionsUrl, StringComparison.Ordinal)) return;
      using var json = JsonDocument.Parse(request.PostData!);
      identities.Add((json.RootElement.GetProperty("requestId").GetString()!, json.RootElement.GetProperty("expectedRequestHash").GetString()!));
      Assert.Equal(initialRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), json.RootElement.GetProperty("expectedDispositionRevision").GetString());
    };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Review point", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("section[aria-labelledby='context-heading']")).ToContainTextAsync("9007199254740993");
    if (action == "CLEAR" && mode == "success") await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "review-point-command");
    await PrepareAsync(page, action); Assert.Empty(identities);
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact review point preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync(targetId.ToString("D"));
    await Assertions.Expect(preview).ToContainTextAsync("9007199254740993");
    await Assertions.Expect(preview.GetByRole(AriaRole.Table, new() { Name = "Disposition before and proposed after", Exact = true })).ToContainTextAsync("Disposition revision");
    if (action == "CLEAR" && mode == "success")
    {
      foreach (var width in new[] { 320, 390, 768, 1024, 1440 })
      {
        await page.SetViewportSizeAsync(width, 900); await page.WaitForFunctionAsync("w => document.documentElement.clientWidth === w", width);
        Assert.True(await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth") <= width + 1, $"Review point overflows at {width}px.");
      }
    }
    var pattern = "**" + actionsUrl;
    if (mode != "success") await page.RouteAsync(pattern, async route => { if (mode == "lost-committed") await route.FetchAsync(); await route.AbortAsync(); });
    await ConfirmAsync(page, keyboard: action == "CLEAR" && mode == "success");
    if (mode != "success")
    {
      var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Exact review point request recovery", Exact = true });
      await Assertions.Expect(recovery).ToBeVisibleAsync(); await page.UnrouteAsync(pattern); await page.ReloadAsync();
      await Assertions.Expect(recovery).ToContainTextAsync(identities[0].Id); await Assertions.Expect(recovery).ToContainTextAsync(identities[0].Hash);
      await Assertions.Expect(preview).ToHaveCountAsync(0);
      var recoveryStateStatus = -1;
      var recoveryStateCaptureArmed = 0;
      var recoveryStateCaptureStarted = 0;
      IRequest? recoveryStateRequest = null;
      const int maxRecoveryStateBodyCharacters = 32_768;
      var recoveryStateBody = new TaskCompletionSource<(string? Body, string? CaptureError)>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      page.Request += (_, request) =>
      {
        if (Volatile.Read(ref recoveryStateCaptureArmed) == 0 || request.Method != "GET" ||
            !request.Url.EndsWith($"/api/ui/reviews/{pointId:D}/commands/state", StringComparison.Ordinal)) return;
        Interlocked.CompareExchange(ref recoveryStateRequest, request, null);
      };
      page.Response += async (_, response) =>
      {
        try
        {
          if (!ReferenceEquals(response.Request, Volatile.Read(ref recoveryStateRequest))) return;
          if (Interlocked.CompareExchange(ref recoveryStateCaptureStarted, 1, 0) != 0) return;
          Volatile.Write(ref recoveryStateStatus, response.Status);
          try
          {
            var body = await response.TextAsync();
            recoveryStateBody.TrySetResult(body.Length <= maxRecoveryStateBodyCharacters
              ? (body, null)
              : (null, "body exceeded capture limit"));
          }
          catch (Exception exception) { recoveryStateBody.TrySetResult((null, exception.GetType().Name)); }
        }
        catch (Exception exception)
        {
          recoveryStateBody.TrySetResult((null, exception.GetType().Name));
        }
      };
      int lookupStatus; string lookupBody;
      try
      {
        var recoveryReads = await PlaywrightResponseCapture.RunMultipleAsync(page,
          [
            request => request.Method == "GET" && request.Url.Contains($"/api/ui/reviews/{pointId:D}/commands/receipts/{identities[0].Id}?requestHash={identities[0].Hash}", StringComparison.Ordinal),
            request => request.Method == "GET" && Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) &&
              uri.AbsolutePath == $"/api/ui/reviews/{pointId:D}/commands/state"
          ],
          () =>
          {
            Volatile.Write(ref recoveryStateCaptureArmed, 1);
            return recovery.GetByRole(AriaRole.Button, new() { Name = "Look up exact disposition receipt", Exact = true }).ClickAsync();
          });
        (lookupStatus, lookupBody) = recoveryReads[0];
        Assert.Equal(200, recoveryReads[1].Status);
      }
      catch (TimeoutException exception)
      {
        static bool TryProperty(JsonElement parent, string name, out JsonElement value)
        {
          value = default;
          return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value);
        }
        static string Clip(string value, int limit) => value.Length <= limit ? value : value[..Math.Max(0, limit - 1)] + "…";
        static string RawValue(JsonElement value, int limit) => value.ValueKind switch
        {
          JsonValueKind.String => Clip(value.GetString() ?? "null", limit),
          JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => Clip(value.GetRawText(), limit),
          JsonValueKind.Object => "<object>",
          JsonValueKind.Array => "<array>",
          _ => "<undefined>"
        };
        static string RawProperty(JsonElement parent, string name, int limit = 120) =>
          TryProperty(parent, name, out var value) ? RawValue(value, limit) : "<missing>";
        static string SummarizeState(int status, string? body, string? captureError)
        {
          if (body is null)
            return $"HTTP {status}; body unavailable ({Clip(captureError ?? "capture pending", 80)})";
          try
          {
            using var stateDocument = JsonDocument.Parse(body);
            var root = stateDocument.RootElement;
            if (TryProperty(root, "value", out var wrapped) && wrapped.ValueKind == JsonValueKind.Object) root = wrapped;
            if (!TryProperty(root, "reviewPoint", out var point) || point.ValueKind != JsonValueKind.Object)
              return $"HTTP {status}; responseCode={RawProperty(root, "code", 80)}";

            var blockers = "<missing>";
            if (TryProperty(root, "blockers", out var blockerItems) && blockerItems.ValueKind == JsonValueKind.Array)
            {
              var values = blockerItems.EnumerateArray().Take(3).Select(item => RawValue(item, 120)).ToArray();
              var omitted = blockerItems.GetArrayLength() - values.Length;
              blockers = "[" + string.Join(" | ", values) + (omitted > 0 ? $" | +{omitted} more" : "") + "]";
            }
            return $"HTTP {status}; point={RawProperty(point, "reviewPointId", 80)}; engagement={RawProperty(point, "engagementId", 80)}; target={RawProperty(point, "targetKind", 80)}/{RawProperty(point, "targetId", 80)}@{RawProperty(point, "targetRevision", 80)}; dispositionRevision={RawProperty(point, "dispositionRevision", 80)}; cleared={RawProperty(point, "cleared", 16)}; canChangeDisposition={RawProperty(root, "canChangeDisposition", 16)}; blockers={Clip(blockers, 420)}";
          }
          catch (Exception exception)
          {
            return $"HTTP {status}; state summary unavailable ({Clip(exception.GetType().Name, 60)}); body length={body.Length}";
          }
        }
        var status = Volatile.Read(ref recoveryStateStatus);
        (string? Body, string? CaptureError) capture = (null, null);
        if (recoveryStateBody.Task.IsCompletedSuccessfully)
          capture = await recoveryStateBody.Task;
        else if (status >= 0)
        {
          var completed = await Task.WhenAny(recoveryStateBody.Task, Task.Delay(TimeSpan.FromMilliseconds(250)));
          if (completed == recoveryStateBody.Task) capture = await recoveryStateBody.Task;
          else capture = (null, "capture pending");
        }
        var stateSummary = status < 0 ? "command-state response status not captured" : SummarizeState(status, capture.Body, capture.CaptureError);
        string recoveryText;
        try { recoveryText = Clip(await recovery.InnerTextAsync(), 320); }
        catch (Exception panelException) { recoveryText = $"unavailable ({Clip(panelException.GetType().Name, 60)})"; }
        throw new TimeoutException($"{Clip(exception.Message, 400)} Reconcile state: {stateSummary}; recovery panel: {recoveryText}", exception);
      }
      Assert.True(lookupStatus == 200, $"Exact synthetic receipt lookup returned HTTP {lookupStatus}: {lookupBody}");
      using var lookupDocument = JsonDocument.Parse(lookupBody);
      Assert.Equal(mode == "lost-committed", lookupDocument.RootElement.GetProperty("found").GetBoolean());
      Assert.Single(identities);
      if (mode == "lost-uncommitted")
      {
        await Assertions.Expect(recovery).ToContainTextAsync("No receipt was found at lookup time.");
        await PrepareAsync(page, action); Assert.Single(identities); await ConfirmAsync(page);
      }
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Verified review point receipt", Exact = true })).ToContainTextAsync(action);
    Assert.Equal(mode == "lost-uncommitted" ? 2 : 1, identities.Count); Assert.All(identities, identity => Assert.Equal(identities[0], identity));
    await using (var db = host.CreateDbContext())
    {
      var receipt = await db.ReviewPointCommandReceipts.SingleAsync(x => x.ReviewPointId == pointId);
      Assert.Equal(f.Staff.Id, receipt.ActorId); Assert.Equal(f.Staff.SessionEpoch, receipt.ActorEpoch); Assert.Equal(action, receipt.Action);
      Assert.Equal(Guid.Parse(identities[0].Id), receipt.RequestId); Assert.Equal(identities[0].Hash, receipt.RequestHash);
      var point = await db.ReviewPoints.AsNoTracking().SingleAsync(x => x.Id == pointId);
      Assert.Equal(initialRevision + 1, point.DispositionRevision); Assert.Equal(action == "CLEAR", point.Cleared);
      Assert.Equal(targetId, point.TargetId); Assert.Equal(9007199254740993L, point.TargetRevision);
      Assert.Equal(initialEvents + 1, await db.ReviewPointDispositionEvents.CountAsync(x => x.ReviewPointId == pointId));
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-ANG-REVIEW-POINT-STALE-02")]
  public async Task StalePreviewRetainsRequestAndLookupDenialNeverDispatchesAgain()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-ANG-REVIEW-POINT-STALE-02");
    var world = await ReviewPointCommandReceiptSeed.SeedAsync(host.Database); var origin = await StartAsync(host, world.Fixture);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync(); var writes = 0;
    page.Request += (_, request) => { if (request.Method == "POST" && request.Url.EndsWith("/commands/actions", StringComparison.Ordinal)) writes++; };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/reviews/{world.ReviewPointId:D}"));
    await PrepareAsync(page, "CLEAR");
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh review point", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact review point preview", Exact = true })).ToHaveCountAsync(0);
    await PrepareAsync(page, "CLEAR");
    await using (var db = host.CreateDbContext())
    {
      var changed = await AuditPlanningService.SetReviewPointDispositionAsync(db, world.Admin, world.ReviewPointId, true, 1L);
      Assert.True(changed.Succeeded, changed.Message);
    }
    await ConfirmAsync(page);
    var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Exact review point request recovery", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync(); Assert.Equal(1, writes);
    await page.RouteAsync("**/commands/receipts/**", route => route.FulfillAsync(new() { Status = 403, ContentType = "application/json", Body = "{\"message\":\"Synthetic exact receipt lookup denied.\"}" }));
    await recovery.GetByRole(AriaRole.Button, new() { Name = "Look up exact disposition receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Exact review point receipt lookup could not be verified. Keep the request identity and do not repeat blindly.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh review point", Exact = true }).ClickAsync();
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Preview reopening review point", Exact = true })).ToBeDisabledAsync();
    Assert.Equal(1, writes);
    await using var verify = host.CreateDbContext(); Assert.False(await verify.ReviewPointCommandReceipts.AnyAsync(x => x.ReviewPointId == world.ReviewPointId));
    Assert.Equal(2L, await verify.ReviewPoints.Where(x => x.Id == world.ReviewPointId).Select(x => x.DispositionRevision).SingleAsync());
  }

  [Fact]
  [Trait("CaseId", "AS-ANG-REVIEW-POINT-SCOPE-03")]
  public async Task MixedScopeAndUnavailableIdentityClearPreviewWithoutGrantingDisposition()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-ANG-REVIEW-POINT-SCOPE-03");
    var world = await ReviewPointCommandReceiptSeed.SeedAsync(host.Database); var f = world.Fixture;
    var foreignPoint = Guid.NewGuid(); var siblingPoint = Guid.NewGuid(); var siblingEngagement = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement { Id = siblingEngagement, FirmId = f.FirmId, PracticeClientId = f.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      db.ReviewPoints.AddRange(
        Point(foreignPoint, host.Fixture.FirmId, host.Fixture.ClientId, host.Fixture.EngagementId, host.Fixture.Staff.Id, "FOREIGN-POINT-PRIVATE"),
        Point(siblingPoint, f.FirmId, f.ClientId, siblingEngagement, f.Staff.Id, "SIBLING-POINT-PRIVATE"));
      await db.SaveChangesAsync();
    }
    var origin = await StartAsync(host, f);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync(); var writes = 0;
    page.Request += (_, request) => { if (request.Method == "POST" && request.Url.EndsWith("/commands/actions", StringComparison.Ordinal)) writes++; };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/reviews/{world.ReviewPointId:D}")); await PrepareAsync(page, "CLEAR");
    foreach (var pointId in new[] { siblingPoint, foreignPoint, Guid.NewGuid() })
    {
      await page.EvaluateAsync("p => { history.pushState({}, '', p); dispatchEvent(new PopStateEvent('popstate')); }", $"/app/reviews/{pointId:D}");
      await Assertions.Expect(page.GetByText("The requested review point was not found in the current firm scope.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact review point preview", Exact = true })).ToHaveCountAsync(0);
      await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("SYNTHETIC review point");
      await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("FOREIGN-POINT-PRIVATE");
      await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("SIBLING-POINT-PRIVATE");
    }
    await page.EvaluateAsync("p => { history.pushState({}, '', p); dispatchEvent(new PopStateEvent('popstate')); }", $"/app/reviews/{world.ReviewPointId:D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Preview clearing review point", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact review point preview", Exact = true })).ToHaveCountAsync(0);
    await page.RouteAsync($"**/api/ui/reviews/{world.ReviewPointId:D}", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(new {
      id = foreignPoint, engagementId = f.EngagementId, targetKind = "workpaper", targetId = Guid.NewGuid(), targetRevision = "9007199254740993",
      raisedByUserId = f.Staff.Id, raisedAt = DateTimeOffset.UtcNow, significant = true, cleared = false, comment = "WRONG-RETURNED-POINT", status = "BLOCKING", dispositionRevision = "1", canChangeDisposition = true }) }));
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh review point", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Unsupported response. Refresh the page; if this persists the workspace version may be out of date.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("WRONG-RETURNED-POINT"); Assert.Equal(0, writes);
  }

  [Fact]
  [Trait("CaseId", "AS-ANG-REVIEW-POINT-ROLE-04")]
  public async Task FrozenPointIsReadableWithoutDispositionControlsAndReviewerRoleDoesNotWidenAccess()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-ANG-REVIEW-POINT-ROLE-04");
    var world = await ReviewPointCommandReceiptSeed.SeedAsync(host.Database);
    await using (var db = host.CreateDbContext()) await ReviewPointCommandReceiptSeed.FreezeAsync(db, world);
    var origin = await StartAsync(host, world.Fixture);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync(); var writes = 0;
    page.Request += (_, request) => { if (request.Method == "POST" && request.Url.Contains("/commands/", StringComparison.Ordinal)) writes++; };
    var path = $"/app/reviews/{world.ReviewPointId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.Locator("section[aria-labelledby='comment-heading']")).ToContainTextAsync("SYNTHETIC review point");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Preview clearing review point", Exact = true })).ToHaveCountAsync(0);
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh review point", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator("section[aria-labelledby='comment-heading']")).ToContainTextAsync("SYNTHETIC review point");
    Assert.Equal(0, writes);
    var reviewerOrigin = await host.StartApiForIdentityAsync(world.Fixture.Reviewer,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true", ["Application__FirmId"] = world.Fixture.FirmId.ToString("D") });
    await page.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByText("The requested review point was not found in the current firm scope.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("SYNTHETIC review point");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Preview clearing review point", Exact = true })).ToHaveCountAsync(0);
  }

  private static ReviewPoint Point(Guid id, Guid firm, Guid client, Guid engagement, Guid actor, string comment) => new()
  { Id = id, FirmId = firm, ClientId = client, EngagementId = engagement, TargetKind = "workpaper", TargetId = Guid.NewGuid(), TargetRevision = 7,
    RaisedByUserId = actor, RaisedAt = DateTimeOffset.UtcNow, Significant = true, Comment = comment };
  private static Task<string> StartAsync(OwnedHost host, PbcSeed.Fixture f) => host.StartApiForIdentityAsync(f.Staff,
    new Dictionary<string, string> { ["AngularUi__Enabled"] = "true", ["Application__FirmId"] = f.FirmId.ToString("D") });
  private static async Task PrepareAsync(IPage page, string action)
  {
    await page.GetByRole(AriaRole.Button, new() { Name = action == "CLEAR" ? "Preview clearing review point" : "Preview reopening review point", Exact = true }).ClickAsync();
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact review point preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync(action);
    await Assertions.Expect(preview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed disposition", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(preview.GetByLabel("I reviewed this exact review point, immutable target and disposition change.", new() { Exact = true })).Not.ToBeCheckedAsync();
  }
  private static async Task ConfirmAsync(IPage page, bool keyboard = false)
  {
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact review point preview", Exact = true });
    var assent = preview.GetByLabel("I reviewed this exact review point, immutable target and disposition change.", new() { Exact = true });
    var execute = preview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed disposition", Exact = true });
    if (keyboard)
    {
      await assent.FocusAsync(); await page.Keyboard.PressAsync("Space"); await Assertions.Expect(assent).ToBeCheckedAsync();
      await Assertions.Expect(execute).ToBeEnabledAsync();
      await assent.PressAsync("Tab"); await Assertions.Expect(execute).ToBeFocusedAsync(); await page.Keyboard.PressAsync("Enter");
    }
    else { await assent.CheckAsync(); await execute.ClickAsync(); }
  }
}
