using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularFirmLedgerScopedReviewerJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-FIRM-LEDGER-SCOPED-REVIEWER-01")]
  public async Task ClientScopedFinanceReviewerCannotReadOrCloseFirmWideLedgerPeriod()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FIRM-LEDGER-SCOPED-REVIEWER-01");
    var f = host.Fixture;
    var manager = PbcSeed.Actor(f.Admin, "FinanceManager");
    var reviewer = PbcSeed.User(f.FirmId, "Staff");
    Guid periodId;
    const string periodMarker = "2026-10";

    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(reviewer);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, reviewer, "FinanceReviewer", f.ClientId));
      await db.SaveChangesAsync();
      var period = await LedgerService.CreateFirmPeriodAsync(db, manager,
        new CreateFirmPeriodRequest(periodMarker));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
    }

    var origin = await host.StartApiForIdentityAsync(reviewer, new Dictionary<string, string>
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
    await Assertions.Expect(page.GetByText(
      "Sign in with an authorized finance identity to access the firm ledger.",
      new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Close period", Exact = true })).ToHaveCountAsync(0);
    var rendered = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(periodMarker, rendered, StringComparison.Ordinal);

    var responses = await page.EvaluateAsync<string>("""
      async id => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const read = await fetch('/api/ui/finance');
        const close = await fetch(`/api/ui/finance/periods/${id}/close`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
          body: JSON.stringify({ reason: 'Synthetic scoped reviewer close attempt' })
        });
        return JSON.stringify({ readStatus: read.status, readBody: await read.text(), closeStatus: close.status });
      }
      """, periodId.ToString("D"));
    using (var result = System.Text.Json.JsonDocument.Parse(responses))
    {
      Assert.Equal(403, result.RootElement.GetProperty("readStatus").GetInt32());
      Assert.Equal(403, result.RootElement.GetProperty("closeStatus").GetInt32());
      Assert.DoesNotContain(periodMarker, result.RootElement.GetProperty("readBody").GetString(), StringComparison.Ordinal);
    }

    await using (var db = host.CreateDbContext())
    {
      var period = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
      Assert.Equal(LedgerStates.PeriodOpen, period.Status);
      Assert.Equal(1, period.Revision);
      Assert.Empty(await db.PeriodCloseDecisions.AsNoTracking()
        .Where(x => x.PeriodId == periodId).ToListAsync());
    }
    Assert.Empty(pageErrors);
  }
}
