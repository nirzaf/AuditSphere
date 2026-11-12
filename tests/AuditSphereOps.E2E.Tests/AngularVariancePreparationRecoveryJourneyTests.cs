using System.Text.Json;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditCompletion")]
public sealed class AngularVariancePreparationRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "R03-VARIANCE-PREPARATION-RECOVERY")]
  public async Task LostPreparationResponseRemainsBoundToItsOriginalPeriod()
  {
    SupportedAuditCompletionWorld world = null!;
    await using var host = await OwnedHost.StartSupportedAsync("R03-VARIANCE-PREPARATION-RECOVERY", async database =>
    {
      world = await SupportedAuditCompletionWorldBuilder.CreateAsync(database, includeSibling: false);
      return world.Primary.FirmId;
    });
    var scope = world.Primary;
    AppUser preparer;
    await using (var db = host.CreateDbContext())
      preparer = await db.Users.AsNoTracking().SingleAsync(x => x.Id == scope.Preparer.UserId);
    var origin = await host.StartApiForIdentityAsync(preparer, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var completion = $"/app/engagements/{scope.EngagementId:D}/completion";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(completion));
    var panel = page.GetByRole(AriaRole.Region, new() { Name = "Analytical completion variance investigation", Exact = true });
    var period = panel.GetByLabel("Period reference", new() { Exact = true });
    var load = panel.GetByRole(AriaRole.Button, new() { Name = "Load exact variance basis", Exact = true });
    var prepare = panel.GetByRole(AriaRole.Button, new() { Name = "Review preparation preview", Exact = true });
    await panel.GetByLabel("Account area", new() { Exact = true }).FillAsync("Revenue");
    await period.FillAsync("2026-Q1");
    await load.ClickAsync();
    await panel.GetByLabel("Expected amount", new() { Exact = true }).FillAsync("100.123456");
    await panel.GetByLabel("Actual amount", new() { Exact = true }).FillAsync("150.123456");
    await panel.GetByLabel("Investigation threshold", new() { Exact = true }).FillAsync("10");
    await panel.GetByLabel("Currency", new() { Exact = true }).FillAsync("QAR");
    await prepare.ClickAsync();
    await panel.GetByLabel("I reviewed this exact preview and intend to submit this one request.", new() { Exact = true }).CheckAsync();

    var apiPath = $"/api/ui/engagements/{scope.EngagementId:D}/completion/variance-investigations";
    var sent = 0;
    var committed = new TaskCompletionSource<(int Status, string Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
    await page.RouteAsync($"**{apiPath}/prepare", async route =>
    {
      Interlocked.Increment(ref sent);
      await using var response = await route.FetchAsync();
      committed.TrySetResult((response.Status, await response.TextAsync()));
      await route.AbortAsync();
    });
    await panel.GetByRole(AriaRole.Button, new() { Name = "Submit this exact request once", Exact = true }).ClickAsync();
    var (status, body) = await committed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    Assert.Equal(200, status);
    using var envelope = JsonDocument.Parse(body);
    var original = envelope.RootElement.GetProperty("value");
    var requestId = original.GetProperty("requestId").GetGuid();
    var hash = original.GetProperty("requestHash").GetString();
    var receiptId = original.GetProperty("id").GetGuid();
    var varianceId = original.GetProperty("varianceInvestigationId").GetGuid();
    var pending = panel.Locator("[data-variance-pending-reference]");
    await Assertions.Expect(pending).ToContainTextAsync(requestId.ToString("D"));
    await page.ReloadAsync();
    await Assertions.Expect(pending).ToContainTextAsync(requestId.ToString("D"));
    await panel.GetByLabel("Account area", new() { Exact = true }).FillAsync("Revenue");
    await period.FillAsync("2026-Q2");
    await load.ClickAsync();
    await Assertions.Expect(panel.GetByText("No variance investigation exists for this exact account area and period.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(panel.Locator("[data-variance-record]")).ToHaveCountAsync(0);
    await Assertions.Expect(prepare).ToHaveCountAsync(0);
    await Assertions.Expect(pending).ToContainTextAsync("REVENUE / 2026-Q1");

    await pending.GetByRole(AriaRole.Button, new() { Name = "Load pending request scope", Exact = true }).ClickAsync();
    var lookup = await page.RunAndWaitForResponseAsync(
      () => pending.GetByRole(AriaRole.Button, new() { Name = "Look up this exact request", Exact = true }).ClickAsync(),
      response => response.Url == $"{origin}{apiPath}/receipts/{requestId:D}?requestHash={hash}");
    Assert.Equal(200, lookup.Status);
    using var recovered = JsonDocument.Parse(await lookup.TextAsync());
    Assert.True(recovered.RootElement.GetProperty("found").GetBoolean());
    Assert.Equal(receiptId, recovered.RootElement.GetProperty("receipt").GetProperty("id").GetGuid());
    await Assertions.Expect(pending).ToHaveCountAsync(0);
    await Assertions.Expect(panel.GetByText("the exact variance state was refreshed.", new() { Exact = false })).ToBeVisibleAsync();
    var record = panel.Locator($"[data-variance-record='{varianceId:D}']");
    await Assertions.Expect(record).ToContainTextAsync("Not independently reviewed.");
    await Assertions.Expect(record).ToContainTextAsync(VarianceInvestigationConclusions.Unexplained);
    Assert.Equal(1, Volatile.Read(ref sent));

    await using var verify = host.CreateDbContext();
    var retained = await verify.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleAsync();
    Assert.Equal((varianceId, "2026-Q1", 1L, 50m), (retained.Id, retained.PeriodReference, retained.Revision, retained.DifferenceAmount));
    Assert.Null(retained.ReviewedByUserId);
    var receipt = await verify.AuditCompletionVarianceCommandReceipts.AsNoTracking().SingleAsync();
    Assert.Equal((receiptId, requestId, preparer.Id), (receipt.Id, receipt.RequestId, receipt.ActorId));
  }
}
