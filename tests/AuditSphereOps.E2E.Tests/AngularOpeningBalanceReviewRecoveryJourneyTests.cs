using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditCompletion")]
public sealed class AngularOpeningBalanceReviewRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "R06-OPENING-BALANCE-REVIEW-RECOVERY")]
  public async Task UnavailableLookupPreservesPendingReviewUntilExactReceiptReturns()
  {
    SupportedAuditCompletionWorld world = null!;
    await using var host = await OwnedHost.StartSupportedAsync("R06-OPENING-BALANCE-REVIEW-RECOVERY", async database =>
    {
      world = await SupportedAuditCompletionWorldBuilder.CreateAsync(database, includeSibling: false);
      return world.Primary.FirmId;
    });
    var scope = world.Primary;
    Guid verificationId;
    AppUser reviewer;
    await using (var db = host.CreateDbContext())
    {
      var prepared = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, scope.Preparer,
        new(scope.EngagementId, null, null, "Synthetic prior signed statements", new(2026, 1, 1), "QAR",
          1000.123456m, 1000.123456m, true, "Synthetic opening balance comparison", ["synthetic-prior-source"], 0));
      Assert.True(prepared.Succeeded, prepared.Message);
      Assert.Equal(1, prepared.Value!.Revision);
      verificationId = prepared.Value.VerificationId;
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
    var panel = page.GetByRole(AriaRole.Region, new() { Name = "Opening-balance verification", Exact = true });
    await panel.GetByLabel("Exact as-of date", new() { Exact = true }).FillAsync("2026-01-01");
    await panel.GetByRole(AriaRole.Button, new() { Name = "Load exact opening-balance basis", Exact = true }).ClickAsync();
    var displayed = panel.Locator("[data-ob-record]");
    await Assertions.Expect(displayed).ToContainTextAsync($"{verificationId:D} · revision 1");
    await Assertions.Expect(displayed).ToContainTextAsync("Not currently independently reviewed");
    var preview = panel.GetByRole(AriaRole.Button, new() { Name = "Preview independent review", Exact = true });
    await preview.ClickAsync();
    await panel.GetByLabel("I reviewed this exact intent and evidence. Preparation is not independent approval.",
      new() { Exact = true }).CheckAsync();

    var apiPath = $"/api/ui/engagements/{scope.EngagementId:D}/completion/opening-balances";
    var sent = 0;
    var committed = new TaskCompletionSource<(int Status, string Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
    await page.RouteAsync($"**{apiPath}/{verificationId:D}/review", async route =>
    {
      Interlocked.Increment(ref sent);
      await using var response = await route.FetchAsync();
      committed.TrySetResult((response.Status, await response.TextAsync()));
      await route.AbortAsync();
    });
    await panel.GetByRole(AriaRole.Button, new() { Name = "Confirm independent review", Exact = true }).ClickAsync();
    var (status, body) = await committed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    Assert.Equal(200, status);
    using var envelope = JsonDocument.Parse(body);
    var original = envelope.RootElement.GetProperty("value");
    var requestId = original.GetProperty("requestId").GetGuid();
    var hash = original.GetProperty("requestHash").GetString();
    var receiptId = original.GetProperty("id").GetGuid();
    var pending = panel.Locator("[data-ob-pending]");
    await Assertions.Expect(pending).ToContainTextAsync(requestId.ToString("D"));
    await page.ReloadAsync();
    await Assertions.Expect(pending).ToContainTextAsync(requestId.ToString("D"));
    await pending.GetByRole(AriaRole.Button, new() { Name = "Load saved request scope", Exact = true }).ClickAsync();
    var lookupUrl = $"{origin}{apiPath}/receipts/{requestId:D}?requestHash={hash}";
    var lookup = pending.GetByRole(AriaRole.Button, new() { Name = "Look up exact request", Exact = true });
    await page.RouteAsync(lookupUrl, route => route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{}" }));
    var unavailable = await page.RunAndWaitForResponseAsync(() => lookup.ClickAsync(), response => response.Url == lookupUrl);
    Assert.Equal(503, unavailable.Status);
    await Assertions.Expect(lookup).ToBeEnabledAsync();
    await Assertions.Expect(pending).ToContainTextAsync(requestId.ToString("D"));
    await Assertions.Expect(preview).ToBeDisabledAsync();

    await page.UnrouteAsync(lookupUrl);
    var confirmed = await page.RunAndWaitForResponseAsync(() => lookup.ClickAsync(), response => response.Url == lookupUrl);
    Assert.Equal(200, confirmed.Status);
    using var recovered = JsonDocument.Parse(await confirmed.TextAsync());
    Assert.True(recovered.RootElement.GetProperty("found").GetBoolean());
    Assert.Equal(receiptId, recovered.RootElement.GetProperty("receipt").GetProperty("id").GetGuid());
    await Assertions.Expect(pending).ToHaveCountAsync(0);
    await Assertions.Expect(panel.GetByText("Exact verification state refreshed.", new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(displayed).ToContainTextAsync("basis revision 1");
    Assert.Equal(1, Volatile.Read(ref sent));

    await using var verify = host.CreateDbContext();
    var retained = await verify.OpeningBalanceVerifications.AsNoTracking().SingleAsync();
    Assert.Equal((verificationId, reviewer.Id, 2L, 1L),
      (retained.Id, retained.ReviewedByUserId!.Value, retained.Revision, retained.ReviewedBasisRevision!.Value));
    Assert.True(OpeningBalanceReviewCurrentness.IsCurrent(retained));
    var receipt = await verify.OpeningBalanceCommandReceipts.AsNoTracking().SingleAsync();
    Assert.Equal((receiptId, requestId, reviewer.Id, verificationId), (receipt.Id, receipt.RequestId, receipt.ActorId, receipt.VerificationId));
  }
}
