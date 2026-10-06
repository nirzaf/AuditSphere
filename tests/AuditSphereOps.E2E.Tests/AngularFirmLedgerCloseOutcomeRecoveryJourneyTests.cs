using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularFirmLedgerCloseOutcomeRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-FIRM-LEDGER-CLOSE-OUTCOME-01")]
  public async Task LostCloseResponseIsReconciledBeforeAnyExplicitRetry()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-FIRM-LEDGER-CLOSE-OUTCOME-01");
    var f = host.Fixture;
    var manager = PbcSeed.Actor(f.Admin, "FinanceManager");
    Guid acceptedPeriodId, retryPeriodId;

    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();
      acceptedPeriodId = (await LedgerService.CreateFirmPeriodAsync(db, manager,
        new CreateFirmPeriodRequest("2026-10"))).Value;
      retryPeriodId = (await LedgerService.CreateFirmPeriodAsync(db, manager,
        new CreateFirmPeriodRequest("2026-11"))).Value;
    }

    var origin = await host.StartApiForIdentityAsync(f.Reviewer, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Firm ledger & financial operations", Exact = true })).ToBeVisibleAsync();

    var acceptedCalls = 0;
    await page.RouteAsync($"**/api/ui/finance/periods/{acceptedPeriodId:D}/close", async route =>
    {
      Interlocked.Increment(ref acceptedCalls);
      var accepted = await route.FetchAsync();
      Assert.Equal(200, accepted.Status);
      await route.AbortAsync();
    });
    var acceptedRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "2026-10" });
    await acceptedRow.GetByRole(AriaRole.Button, new() { Name = "Close period", Exact = true }).ClickAsync();
    await page.GetByLabel("Close reason", new() { Exact = true }).FillAsync("Accepted close response was lost");
    await page.GetByRole(AriaRole.Button, new() { Name = "Confirm close", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Verify saved period close", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Confirm close", Exact = true })).ToHaveCountAsync(0);
    Assert.Equal(1, acceptedCalls);

    await page.GetByRole(AriaRole.Button,
      new() { Name = "Verify saved period state", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText(
      "The saved period is CLOSED at revision 2. No retry is needed.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,
      new() { Name = "Acknowledge saved close", Exact = true }).ClickAsync();
    await Assertions.Expect(acceptedRow.GetByRole(AriaRole.Button,
      new() { Name = "Close period", Exact = true })).ToHaveCountAsync(0);
    Assert.Equal(1, acceptedCalls);

    var retryCalls = 0;
    await page.RouteAsync($"**/api/ui/finance/periods/{retryPeriodId:D}/close", async route =>
    {
      var attempt = Interlocked.Increment(ref retryCalls);
      if (attempt == 1) await route.AbortAsync();
      else await route.ContinueAsync();
    });
    var retryRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "2026-11" });
    await retryRow.GetByRole(AriaRole.Button, new() { Name = "Close period", Exact = true }).ClickAsync();
    await page.GetByLabel("Close reason", new() { Exact = true }).FillAsync("Explicit retry after persisted open-state check");
    await page.GetByRole(AriaRole.Button, new() { Name = "Confirm close", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Verify saved period close", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,
      new() { Name = "Verify saved period state", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText(
      "The saved period remains OPEN at revision 1. Review the current period and reason before you choose whether to retry.",
      new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button,
      new() { Name = "Acknowledge period is still open", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Confirm period close", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Confirm close", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator("audit-command-message"))
      .ToContainTextAsync("Fiscal period successfully closed.");
    Assert.Equal(2, retryCalls);

    await using (var db = host.CreateDbContext())
    {
      var acceptedPeriod = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == acceptedPeriodId);
      Assert.Equal(LedgerStates.PeriodClosed, acceptedPeriod.Status);
      Assert.Equal(2, acceptedPeriod.Revision);
      Assert.Equal("Accepted close response was lost", await db.PeriodCloseDecisions.AsNoTracking()
        .Where(x => x.PeriodId == acceptedPeriodId).Select(x => x.Reason).SingleAsync());

      var retriedPeriod = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == retryPeriodId);
      Assert.Equal(LedgerStates.PeriodClosed, retriedPeriod.Status);
      Assert.Equal(2, retriedPeriod.Revision);
      Assert.Equal("Explicit retry after persisted open-state check", await db.PeriodCloseDecisions.AsNoTracking()
        .Where(x => x.PeriodId == retryPeriodId).Select(x => x.Reason).SingleAsync());
      Assert.Equal(2, await db.PeriodCloseDecisions.AsNoTracking().CountAsync());
    }
    Assert.Empty(pageErrors);
  }
}
