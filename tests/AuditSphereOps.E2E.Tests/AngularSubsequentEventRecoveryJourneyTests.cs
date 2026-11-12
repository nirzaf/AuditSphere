using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditCompletion")]
public sealed class AngularSubsequentEventRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "R05-SUBSEQUENT-EVENT-REVIEW-RECOVERY")]
  public async Task LostCommittedReviewResponseSurvivesReloadAndRecoversExactReceipt()
  {
    SupportedAuditCompletionWorld world = null!;
    await using var host = await OwnedHost.StartSupportedAsync("R05-SUBSEQUENT-EVENT-REVIEW-RECOVERY", async database =>
    {
      world = await SupportedAuditCompletionWorldBuilder.CreateAsync(database, includeSibling: false);
      return world.Primary.FirmId;
    });
    var scope = world.Primary;
    Guid eventId;
    AppUser reviewer;
    AppUser replacement;
    await using (var db = host.CreateDbContext())
    {
      var prepared = await AuditCompletionService.RecordSubsequentEventAsync(db, scope.Preparer,
        new(scope.EngagementId, null, new(2026, 12, 31), new(2027, 1, 20),
          "Synthetic classified subsequent event", SubsequentEventClassifications.NonAdjusting,
          false, true, "Note 27", "Synthetic condition arose after the reporting date.", "QAR", 250000.123456m, []));
      Assert.True(prepared.Succeeded, prepared.Message);
      Assert.Equal(1, prepared.Value!.Revision);
      eventId = prepared.Value.ReviewId;
      reviewer = await db.Users.AsNoTracking().SingleAsync(x => x.Id == world.Reviewer.UserId);
      replacement = await db.Users.AsNoTracking().SingleAsync(x => x.Id == scope.Preparer.UserId);
    }

    var angular = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var origin = await host.StartApiForIdentityAsync(reviewer, angular);
    var replacementOrigin = await host.StartApiForIdentityAsync(replacement, angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var completion = $"/app/engagements/{scope.EngagementId:D}/completion";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(completion));
    var panel = page.GetByRole(AriaRole.Region, new() { Name = "Subsequent events", Exact = true });
    var apiPath = $"/api/ui/engagements/{scope.EngagementId:D}/completion/subsequent-events";
    var listed = await page.RunAndWaitForResponseAsync(
      () => panel.GetByRole(AriaRole.Button, new() { Name = "Refresh event list", Exact = true }).ClickAsync(),
      response => response.Url == $"{origin}{apiPath}?skip=0");
    Assert.Equal(200, listed.Status);
    var selection = panel.Locator("select[name='seSelection']");
    await Assertions.Expect(selection).ToBeVisibleAsync();
    await selection.SelectOptionAsync(eventId.ToString("D"));
    await panel.GetByRole(AriaRole.Button, new() { Name = "Load exact selected basis", Exact = true }).ClickAsync();
    var preview = panel.GetByRole(AriaRole.Button, new() { Name = "Preview event independent review", Exact = true });
    await preview.ClickAsync();
    await panel.GetByLabel("I reviewed this exact event intent and evidence. Preparation is not independent approval.",
      new() { Exact = true }).CheckAsync();

    var sent = 0;
    var committed = new TaskCompletionSource<(int Status, string Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
    await page.RouteAsync($"**{apiPath}/{eventId:D}/review", async route =>
    {
      Interlocked.Increment(ref sent);
      await using var response = await route.FetchAsync();
      committed.TrySetResult((response.Status, await response.TextAsync()));
      await route.AbortAsync();
    });
    await panel.GetByRole(AriaRole.Button, new() { Name = "Confirm independent event review", Exact = true }).ClickAsync();
    var (status, body) = await committed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    Assert.Equal(200, status);
    using var envelope = JsonDocument.Parse(body);
    var original = envelope.RootElement.GetProperty("value");
    var requestId = original.GetProperty("requestId").GetGuid();
    var hash = original.GetProperty("requestHash").GetString();
    var receiptId = original.GetProperty("id").GetGuid();
    var pending = panel.Locator("[data-se-pending]");
    await Assertions.Expect(pending).ToContainTextAsync(requestId.ToString("D"));
    await page.ReloadAsync();
    await Assertions.Expect(pending).ToContainTextAsync(requestId.ToString("D"));
    await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Create a different event", Exact = true })).ToBeDisabledAsync();
    await pending.GetByRole(AriaRole.Button, new() { Name = "Load saved request scope", Exact = true }).ClickAsync();

    var lookupUrl = $"{origin}{apiPath}/receipts/{requestId:D}?requestHash={hash}";
    var storageKey = $"auditsphere-subsequent-events-pending-v1:{scope.FirmId:D}:{reviewer.Id:D}:{scope.EngagementId:D}";
    var saved = await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", storageKey);
    Assert.NotNull(saved);
    var fetchedLookup = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseLookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await page.RouteAsync(lookupUrl, async route =>
    {
      await using var response = await route.FetchAsync();
      fetchedLookup.TrySetResult(await response.TextAsync());
      await releaseLookup.Task.WaitAsync(TimeSpan.FromSeconds(30));
      await route.FulfillAsync(new() { Response = response });
    });
    try
    {
      await pending.GetByRole(AriaRole.Button, new() { Name = "Look up exact request", Exact = true }).ClickAsync();
      using var held = JsonDocument.Parse(await fetchedLookup.Task.WaitAsync(TimeSpan.FromSeconds(15)));
      Assert.True(held.RootElement.GetProperty("found").GetBoolean());
      Assert.Equal(receiptId, held.RootElement.GetProperty("receipt").GetProperty("id").GetGuid());

      // Both owned loopback hosts use the same browser cookie scope. Use the real sign-in
      // route while the original tab and its sessionStorage stay mounted.
      await SignInAsAsync(replacementOrigin, replacement.Id);
      await Assertions.Expect(pending).ToHaveCountAsync(0, new() { Timeout = 15000 });
      await using var otherLookup = await page.Context.APIRequest.GetAsync(lookupUrl);
      Assert.Equal(200, otherLookup.Status);
      using var otherResult = JsonDocument.Parse(await otherLookup.TextAsync());
      Assert.False(otherResult.RootElement.GetProperty("found").GetBoolean());
      Assert.Equal(JsonValueKind.Null, otherResult.RootElement.GetProperty("receipt").ValueKind);

      var delivered = await page.RunAndWaitForResponseAsync(() =>
      {
        releaseLookup.TrySetResult();
        return Task.CompletedTask;
      }, response => response.Url == lookupUrl);
      Assert.Equal(200, delivered.Status);
      await delivered.FinishedAsync();
      await Assertions.Expect(panel.Locator("[data-se-history]")).ToHaveCountAsync(0);
      await Assertions.Expect(panel).Not.ToContainTextAsync(requestId.ToString("D"));
      Assert.Equal(saved, await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", storageKey));
    }
    finally
    {
      releaseLookup.TrySetResult();
      await page.UnrouteAsync(lookupUrl);
    }

    await SignInAsAsync(origin, reviewer.Id);
    await Assertions.Expect(pending).ToContainTextAsync(requestId.ToString("D"), new() { Timeout = 15000 });
    await pending.GetByRole(AriaRole.Button, new() { Name = "Load saved request scope", Exact = true }).ClickAsync();
    var lookup = await page.RunAndWaitForResponseAsync(
      () => pending.GetByRole(AriaRole.Button, new() { Name = "Look up exact request", Exact = true }).ClickAsync(),
      response => response.Url == lookupUrl);
    Assert.Equal(200, lookup.Status);
    using var recovered = JsonDocument.Parse(await lookup.TextAsync());
    Assert.True(recovered.RootElement.GetProperty("found").GetBoolean());
    Assert.Equal(receiptId, recovered.RootElement.GetProperty("receipt").GetProperty("id").GetGuid());
    await Assertions.Expect(pending).ToHaveCountAsync(0);
    await Assertions.Expect(panel.Locator("[data-se-history]")).ToContainTextAsync("Exact event state refreshed.");
    await Assertions.Expect(panel.Locator("[data-se-record]")).ToContainTextAsync("reviewed revision 1");
    Assert.Equal(1, Volatile.Read(ref sent));

    await using var verify = host.CreateDbContext();
    var retained = await verify.SubsequentEventReviews.AsNoTracking().SingleAsync();
    Assert.Equal((eventId, reviewer.Id, 2L, 1L),
      (retained.Id, retained.ReviewedByUserId!.Value, retained.Revision, retained.ReviewedRevision!.Value));
    var receipt = await verify.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().SingleAsync();
    Assert.Equal((receiptId, requestId, reviewer.Id), (receipt.Id, receipt.RequestId, receipt.ActorId));

    async Task SignInAsAsync(string signInOrigin, Guid expectedActor)
    {
      await using var signIn = await page.Context.APIRequest.GetAsync(signInOrigin + "/auth/sign-in",
        new() { MaxRedirects = 0 });
      Assert.Equal(302, signIn.Status);
      await using var session = await page.Context.APIRequest.GetAsync(origin + "/api/ui/session");
      Assert.Equal(200, session.Status);
      using var identity = JsonDocument.Parse(await session.TextAsync());
      Assert.Equal(expectedActor, identity.RootElement.GetProperty("userId").GetGuid());
    }
  }
}
