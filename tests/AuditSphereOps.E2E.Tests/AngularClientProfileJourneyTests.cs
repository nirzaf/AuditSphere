using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientProfileJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-CLIENT-OFFBOARDING-RECOVERY-E2E")]
  public async Task BlockedOffboardingReviewRecoversTheSameIntentAfterACommittedResponseIsLost()
  {
    const string notes = "Reviewed the current limited-visibility closeout snapshot.";
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-OFFBOARDING-RECOVERY-E2E");
    var f = host.Fixture;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId));
      db.WorkTasks.Add(new WorkTask
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        Title = "Synthetic open offboarding work", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    var path = $"/app/clients/{f.ClientId:D}";
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    var closeoutBodies = new List<string>();
    var firstServerStatus = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    page.PageError += (_, error) => errors.Add(error);
    await page.RouteAsync("**/api/ui/clients/*/offboarding-closeout", async route =>
    {
      if (route.Request.Method != "POST")
      {
        await route.ContinueAsync();
        return;
      }

      closeoutBodies.Add(route.Request.PostData ?? throw new InvalidOperationException("The closeout body was missing."));
      if (closeoutBodies.Count == 1)
      {
        try
        {
          await using var committed = await route.FetchAsync();
          firstServerStatus.TrySetResult(committed.Status);
          await route.AbortAsync("failed");
        }
        catch (Exception exception)
        {
          firstServerStatus.TrySetException(exception);
          throw;
        }
      }
      else
      {
        await route.ContinueAsync();
      }
    });

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Client profile", Exact = true })).ToBeVisibleAsync();
    var readiness = page.Locator("audit-client-offboarding-readiness");
    await readiness.GetByRole(AriaRole.Button, new() { Name = "Check offboarding readiness", Exact = true }).ClickAsync();
    await Assertions.Expect(readiness.GetByRole(AriaRole.Heading,
      new() { Name = "Record a blocked review", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(readiness).ToContainTextAsync("Action required");
    await Assertions.Expect(readiness).ToContainTextAsync("Unknown");
    await readiness.Locator("#offboarding-disposition-notes").FillAsync(notes);
    await readiness.GetByRole(AriaRole.Button, new() { Name = "Record blocked review", Exact = true }).ClickAsync();
    await Assertions.Expect(readiness).ToContainTextAsync("outcome is unconfirmed");
    Assert.Equal(200, await firstServerStatus.Task.WaitAsync(TimeSpan.FromSeconds(5)));

    Guid firstRecordId;
    string firstRequestKey;
    string readinessDigest;
    await using (var proof = host.CreateDbContext())
    {
      var record = await proof.ClientOffboardingRecords.AsNoTracking().SingleAsync(x => x.ClientId == f.ClientId);
      firstRecordId = record.Id;
      firstRequestKey = record.RequestKey!;
      readinessDigest = record.ReadinessDigest;
      Assert.Equal("BLOCKED_DECISION", record.Status);
      Assert.Equal(notes, record.DispositionNotes);
      Assert.Empty(await proof.ClientLifecycleEvents.AsNoTracking().Where(x => x.ClientId == f.ClientId).ToListAsync());
      Assert.Equal(1, await proof.WorkTasks.AsNoTracking().CountAsync(x => x.ClientId == f.ClientId && x.Title == "Synthetic open offboarding work"));
    }

    // Recreate the Angular component through a full page/session bootstrap. Its sessionStorage receipt
    // must recover the exact request instead of generating another closeout decision.
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Client profile", Exact = true })).ToBeVisibleAsync();
    await readiness.GetByRole(AriaRole.Button, new() { Name = "Check offboarding readiness", Exact = true }).ClickAsync();
    await Assertions.Expect(readiness).ToContainTextAsync("A previous review outcome is unconfirmed");
    await readiness.Locator("#offboarding-disposition-notes").FillAsync(notes);
    await readiness.GetByRole(AriaRole.Button, new() { Name = "Retry original review", Exact = true }).ClickAsync();
    await Assertions.Expect(readiness).ToContainTextAsync("A BLOCKED_DECISION review record was saved");
    Assert.Equal(2, closeoutBodies.Count);

    using (var original = JsonDocument.Parse(closeoutBodies[0]))
    using (var replay = JsonDocument.Parse(closeoutBodies[1]))
    {
      Assert.Equal(firstRequestKey, original.RootElement.GetProperty("requestKey").GetString());
      Assert.Equal(firstRequestKey, replay.RootElement.GetProperty("requestKey").GetString());
      Assert.Equal(readinessDigest, original.RootElement.GetProperty("expectedReadinessDigest").GetString());
      Assert.Equal(readinessDigest, replay.RootElement.GetProperty("expectedReadinessDigest").GetString());
      Assert.Equal(notes, replay.RootElement.GetProperty("dispositionNotes").GetString());
      Assert.False(replay.RootElement.GetProperty("archiveClientOnCloseout").GetBoolean());
    }

    await using (var proof = host.CreateDbContext())
    {
      var records = await proof.ClientOffboardingRecords.AsNoTracking().Where(x => x.ClientId == f.ClientId).ToListAsync();
      Assert.Single(records);
      Assert.Equal(firstRecordId, records[0].Id);
      Assert.Equal(firstRequestKey, records[0].RequestKey);
      Assert.Equal("BLOCKED_DECISION", records[0].Status);
      Assert.Empty(await proof.ClientLifecycleEvents.AsNoTracking().Where(x => x.ClientId == f.ClientId).ToListAsync());
      Assert.Empty(errors);
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "ANGULAR-CLIENT-PROFILE-E2E")]
  public async Task ScopedMetadataIndependentPagesReloadMobileAndRevocation(bool canonical)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-PROFILE-E2E");
    var f = host.Fixture;
    Guid manualTaskId;
    await using (var db = host.CreateDbContext())
    {
      await ClientProfileWorkspaceSeed.PopulateAsync(db, f, count: 51);
      var staff = PbcSeed.Actor(f.Staff, "Staff");
      var pending = await PracticeTimeService.CreateTaskAsync(db, staff,
        new("Outstanding manual progress commitment", f.ClientId, f.EngagementId, f.Staff.Id));
      var done = await PracticeTimeService.CreateTaskAsync(db, staff,
        new("Completed manual progress commitment", f.ClientId, f.EngagementId, f.Staff.Id));
      Assert.True(pending.Succeeded, pending.Message);
      Assert.True(done.Succeeded, done.Message);
      Assert.True((await PracticeTimeService.CompleteTaskAsync(db, staff, done.Value)).Succeeded);
      manualTaskId = pending.Value;
    }
    var prefix = canonical ? "" : "/ui";
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string,string> {
      ["AngularUi__Enabled"]="true", ["AngularUi__CanonicalRoutes"]=canonical.ToString() });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    var path = prefix + "/app/clients/" + f.ClientId;
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client profile", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Trading as: Synthetic trading name", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("audit-client dl")).ToContainTextAsync("SYNTHETIC-REG-001");
    await Assertions.Expect(page.Locator("audit-client dl")).ToContainTextAsync("9007199254740993");
    await Assertions.Expect(page.Locator("audit-client dl")).ToContainTextAsync("2026-01-02 12:30 UTC");
    await page.GotoAsync(origin + prefix + "/app/engagements/" + f.EngagementId);
    var tracker = page.Locator("audit-engagement-progress");
    await Assertions.Expect(tracker.Locator(".tracker-figure strong")).ToHaveTextAsync("50%");
    await Assertions.Expect(tracker.GetByLabel("Required step counts", new() { Exact = true }))
      .ToContainTextAsync("1 completed");
    await tracker.Locator("summary").ClickAsync();
    await Assertions.Expect(tracker.GetByRole(AriaRole.Table)).ToContainTextAsync("Completed manual progress commitment");
    // The existing navigation guard refuses leaving while its independent planning read is still pending.
    await Assertions.Expect(page.Locator("audit-engagement-planning")).ToContainTextAsync("No approved budget is available");
    var next = tracker.GetByRole(AriaRole.Link, new() { Name = "Outstanding manual progress commitment", Exact = true });
    await Assertions.Expect(next).ToHaveAttributeAsync("href", prefix + "/app/practice/time?taskId=" + manualTaskId);
    await next.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Practice time & task records", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("select[name='task']")).ToHaveValueAsync(manualTaskId.ToString());
    await page.GotoAsync(origin + path);
    var progress = page.Locator("audit-client-progress");
    await Assertions.Expect(progress).ToContainTextAsync("51 engagements in scope");
    await Assertions.Expect(progress.Locator("tbody tr")).ToHaveCountAsync(50);
    await progress.GetByRole(AriaRole.Button, new() { Name = "Next progress page", Exact = true }).ClickAsync();
    await Assertions.Expect(progress).ToContainTextAsync("Page 2");
    await Assertions.Expect(progress.Locator("tbody tr")).ToHaveCountAsync(1);
    await Assertions.Expect(progress.GetByRole(AriaRole.Heading, new() { Name = "Progress by engagement", Exact = true })).ToBeFocusedAsync();
    await Assertions.Expect(progress.GetByRole(AriaRole.Button, new() { Name = "Next progress page", Exact = true })).ToBeDisabledAsync();
    await progress.GetByRole(AriaRole.Button, new() { Name = "Previous progress page", Exact = true }).ClickAsync();
    await Assertions.Expect(progress).ToContainTextAsync("Page 1");
    await Assertions.Expect(progress.Locator("tbody tr")).ToHaveCountAsync(50);
    await Assertions.Expect(progress.GetByRole(AriaRole.Heading, new() { Name = "Progress by engagement", Exact = true })).ToBeFocusedAsync();
    if (canonical) await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "client-progress-paging");
    var engagements = page.GetByRole(AriaRole.Region, new() { Name = "Associated engagements", Exact = true });
    var contacts = page.GetByRole(AriaRole.Region, new() { Name = "Client contacts", Exact = true });
    await Assertions.Expect(engagements.Locator("tbody tr")).ToHaveCountAsync(10);
    await Assertions.Expect(contacts.Locator("tbody tr")).ToHaveCountAsync(10);
    await engagements.GetByRole(AriaRole.Button, new() {Name="Next engagements",Exact=true}).ClickAsync();
    await Assertions.Expect(engagements).ToContainTextAsync("Page 2");
    await Assertions.Expect(contacts).ToContainTextAsync("Page 1");
    await contacts.GetByRole(AriaRole.Button, new() {Name="Next contacts",Exact=true}).ClickAsync();
    await Assertions.Expect(contacts).ToContainTextAsync("Page 2");
    await page.ReloadAsync();
    await Assertions.Expect(engagements).ToContainTextAsync("Page 2"); await Assertions.Expect(contacts).ToContainTextAsync("Page 2");
    await contacts.GetByLabel("Contacts per page", new(){Exact=true}).SelectOptionAsync("25");
    await Assertions.Expect(contacts.Locator("tbody tr")).ToHaveCountAsync(25);
    await Assertions.Expect(engagements).ToContainTextAsync("Page 2");
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name="Client portal intent",Exact=true })).ToContainTextAsync("Acceptance and Partner activation are required");
    Assert.DoesNotContain("EXCLUDED PRIVATE PROFILE", await page.Locator("body").InnerTextAsync());
    await page.SetViewportSizeAsync(390,844); Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));

    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Archive client", Exact = true })).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId));
      await db.SaveChangesAsync();
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh client", Exact = true }).ClickAsync();
    var archiveClient = page.GetByRole(AriaRole.Button, new() { Name = "Archive client", Exact = true });
    await Assertions.Expect(archiveClient).ToBeEnabledAsync();
    await archiveClient.FocusAsync();
    await Assertions.Expect(archiveClient).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    var restoreClient = page.GetByRole(AriaRole.Button, new() { Name = "Restore client", Exact = true });
    await Assertions.Expect(restoreClient).ToBeVisibleAsync();
    await Assertions.Expect(restoreClient).ToBeFocusedAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Complementary, new() { Name = "Archived client history", Exact = true }))
      .ToContainTextAsync("Authorized staff can still search this client");
    await Assertions.Expect(engagements).ToContainTextAsync("Page 2");
    await Assertions.Expect(contacts.Locator("tbody tr")).ToHaveCountAsync(25);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path,
      canonical ? "client-archive-canonical" : "client-archive-ui");
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(archiveClient).ToBeVisibleAsync();
    await Assertions.Expect(archiveClient).ToBeFocusedAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Complementary, new() { Name = "Archived client history", Exact = true })).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
    {
      var events = await db.ClientLifecycleEvents.Where(x => x.ClientId == f.ClientId).OrderBy(x => x.Revision).ToListAsync();
      Assert.Equal(2, events.Count);
      Assert.All(events, entry => Assert.Equal(f.Staff.Id, entry.ActorUserId));
    }

    var sibling = await SiblingClientSeed.SeedAsync(host.Database, f.FirmId, "CLIENT-PROFILE-SIBLING");
    try
    {
      var documentToken = Guid.NewGuid().ToString("N");
      await page.EvaluateAsync("token => window.__clientProfileRouteToken = token", documentToken);
      async Task<string> NavigateInPlaceAsync(Guid clientId, string? expectedProfileText = null)
      {
        var apiPath = "/api/ui/clients/" + clientId.ToString("D");
        var (status, _) = await PlaywrightResponseCapture.RunAsync(page,
          request => request.Method == "GET" && new Uri(request.Url).AbsolutePath.Equals(apiPath, StringComparison.OrdinalIgnoreCase),
          async () => await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
            prefix + "/app/clients/" + clientId.ToString("D")));
        if (expectedProfileText is null)
          Assert.False(status is >= 200 and < 300, $"A client outside the current scope unexpectedly returned HTTP {status}.");
        else
          Assert.True(status is >= 200 and < 300, $"The scoped client profile returned HTTP {status}.");
        if (expectedProfileText is null)
        {
          await Assertions.Expect(page.GetByRole(AriaRole.Heading,
            new() { Name = "Client unavailable", Exact = true })).ToBeVisibleAsync();
          await Assertions.Expect(page.GetByLabel("Current workspace context")
            .GetByRole(AriaRole.Status)).ToHaveTextAsync("Current context unavailable");
        }
        else
          await Assertions.Expect(page.GetByText(expectedProfileText, new() { Exact = false })).ToBeVisibleAsync();
        return await page.Locator("body").InnerTextAsync();
      }

      var siblingBody = await NavigateInPlaceAsync(sibling.Fixture.ClientId);
      Assert.DoesNotContain("SYNTHETIC-REG-001", siblingBody);
      Assert.DoesNotContain("Synthetic trading name", siblingBody);
      Assert.DoesNotContain("Synthetic contact 000", siblingBody);
      Assert.DoesNotContain(sibling.Marker, siblingBody, StringComparison.OrdinalIgnoreCase);
      Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__clientProfileRouteToken"));

      var randomId = Guid.NewGuid();
      var randomBody = await NavigateInPlaceAsync(randomId);
      Assert.Equal(siblingBody, randomBody);
      Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__clientProfileRouteToken"));

      var restoredBody = await NavigateInPlaceAsync(f.ClientId, "Synthetic trading name");
      Assert.Contains("Synthetic trading name", restoredBody);
      Assert.Contains("SYNTHETIC-REG-001", restoredBody);
      Assert.Contains("Synthetic contact 000", restoredBody);
      Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__clientProfileRouteToken"));

      var siblingOrigin = await host.StartApiForIdentityAsync(sibling.Fixture.Admin,
        new Dictionary<string, string>
        {
          ["AngularUi__Enabled"] = "true",
          ["AngularUi__CanonicalRoutes"] = canonical.ToString()
        });
      await using var siblingContext = await browser.NewContextAsync();
      var siblingPage = await siblingContext.NewPageAsync();
      var siblingErrors = new List<string>();
      siblingPage.PageError += (_, error) => siblingErrors.Add(error);
      var siblingPath = prefix + "/app/clients/" + sibling.Fixture.ClientId.ToString("D");
      await siblingPage.GotoAsync(siblingOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(siblingPath));
      await Assertions.Expect(siblingPage.GetByRole(AriaRole.Heading,
        new() { Name = "Client profile", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(siblingPage.GetByRole(AriaRole.Heading,
        new() { Name = sibling.Marker + " Holdings", Exact = true })).ToBeVisibleAsync();

      var siblingDocumentToken = Guid.NewGuid().ToString("N");
      await siblingPage.EvaluateAsync("token => window.__clientProfileSiblingRouteToken = token", siblingDocumentToken);
      async Task<string> NavigateSiblingInPlaceAsync(Guid clientId)
      {
        var apiPath = "/api/ui/clients/" + clientId.ToString("D");
        var (status, _) = await PlaywrightResponseCapture.RunAsync(siblingPage,
          request => request.Method == "GET" && new Uri(request.Url).AbsolutePath.Equals(apiPath, StringComparison.OrdinalIgnoreCase),
          async () => await siblingPage.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
            prefix + "/app/clients/" + clientId.ToString("D")));
        Assert.False(status is >= 200 and < 300, $"A sibling client unexpectedly returned HTTP {status}.");
        await Assertions.Expect(siblingPage.GetByRole(AriaRole.Heading,
          new() { Name = "Client unavailable", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(siblingPage.GetByLabel("Current workspace context")
          .GetByRole(AriaRole.Status)).ToHaveTextAsync("Current context unavailable");
        return await siblingPage.Locator("body").InnerTextAsync();
      }

      var ownerBody = await NavigateSiblingInPlaceAsync(f.ClientId);
      Assert.DoesNotContain("SYNTHETIC-REG-001", ownerBody);
      Assert.DoesNotContain("Synthetic trading name", ownerBody);
      Assert.DoesNotContain("Synthetic contact 000", ownerBody);
      Assert.DoesNotContain(sibling.Marker, ownerBody, StringComparison.OrdinalIgnoreCase);
      Assert.Equal(siblingDocumentToken,
        await siblingPage.EvaluateAsync<string>("window.__clientProfileSiblingRouteToken"));
      var siblingRandomId = Guid.NewGuid();
      var siblingRandomBody = await NavigateSiblingInPlaceAsync(siblingRandomId);
      Assert.Equal(ownerBody, siblingRandomBody);
      Assert.Equal(siblingDocumentToken,
        await siblingPage.EvaluateAsync<string>("window.__clientProfileSiblingRouteToken"));
      Assert.Empty(siblingErrors);
    }
    finally
    {
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }

    await page.GotoAsync(origin + prefix + "/app/clients/" + Guid.NewGuid());
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name="Client unavailable",Exact=true})).ToBeVisibleAsync();
    Assert.DoesNotContain("SYNTHETIC-REG-001", await page.Locator("body").InnerTextAsync());
    await page.GotoAsync(origin + path); await Assertions.Expect(page.Locator("audit-client dl")).ToContainTextAsync("SYNTHETIC-REG-001");
    await using (var db = host.CreateDbContext())
    { await db.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.EngagementId == null).ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow)); }
    await page.GetByRole(AriaRole.Button, new() {Name="Refresh client",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name="Client unavailable",Exact=true})).ToBeVisibleAsync();
    Assert.DoesNotContain("SYNTHETIC-REG-001", await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
  }
}
