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
    }

    var origin = await host.StartApiForIdentityAsync(reviewer, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
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
    var lookup = await page.RunAndWaitForResponseAsync(
      () => pending.GetByRole(AriaRole.Button, new() { Name = "Look up exact request", Exact = true }).ClickAsync(),
      response => response.Url == $"{origin}{apiPath}/receipts/{requestId:D}?requestHash={hash}");
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
  }
}
