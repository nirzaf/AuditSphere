using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularFirmLedgerRoleMatrixJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-FIRM-LEDGER-ROLE-MATRIX-01")]
  public async Task FinanceManagerCanReadButNotCloseWhileReviewerCanCloseAndPartnerIsDenied()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FIRM-LEDGER-ROLE-MATRIX-01");
    var f = host.Fixture;
    var manager = PbcSeed.Actor(f.Admin, "FinanceManager");
    var reviewer = PbcSeed.User(f.FirmId, "Staff");
    var partner = PbcSeed.User(f.FirmId, "Staff");
    Guid periodId;
    const string periodMarker = "2026-10";
    const string accountMarker = "ROLE-MATRIX-CASH";

    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(reviewer, partner);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, reviewer, "FinanceReviewer"),
        PbcSeed.Grant(f.FirmId, partner, "Partner"));
      await db.SaveChangesAsync();
      var period = await LedgerService.CreateFirmPeriodAsync(db, manager,
        new CreateFirmPeriodRequest(periodMarker));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
      var account = await LedgerService.CreateFirmAccountAsync(db, manager,
        new CreateFirmAccountRequest(accountMarker, "Synthetic role matrix cash", LedgerStates.AccountAsset,
          LedgerStates.Debit));
      Assert.True(account.Succeeded, account.Message);
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var managerOrigin = await host.StartApiForIdentityAsync(f.Admin, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(reviewer, settings);
    var partnerOrigin = await host.StartApiForIdentityAsync(partner, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var pageErrors = new List<string>();

    var managerPage = await browser.NewPageAsync();
    managerPage.PageError += (_, error) => pageErrors.Add($"manager: {error}");
    await managerPage.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance"));
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm ledger & financial operations", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.Locator("tbody tr").GetByText(periodMarker, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByText(accountMarker, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Close period", Exact = true })).ToHaveCountAsync(0);

    var managerCloseStatus = await managerPage.EvaluateAsync<int>("""
      async id => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const response = await fetch(`/api/ui/finance/periods/${id}/close`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
          body: JSON.stringify({ reason: 'Synthetic manager close refusal' })
        });
        return response.status;
      }
      """, periodId.ToString("D"));
    Assert.Equal(403, managerCloseStatus);

    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => pageErrors.Add($"reviewer: {error}");
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance"));
    await Assertions.Expect(reviewerPage.GetByText(periodMarker, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Close period", Exact = true })).ToBeVisibleAsync();

    var partnerPage = await browser.NewPageAsync();
    partnerPage.PageError += (_, error) => pageErrors.Add($"partner: {error}");
    await partnerPage.GotoAsync(partnerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance"));
    await Assertions.Expect(partnerPage.GetByText(
      "Sign in with an authorized finance identity to access the firm ledger.",
      new() { Exact = true })).ToBeVisibleAsync();
    var partnerRead = await partnerPage.EvaluateAsync<string>("""
      async () => {
        const response = await fetch('/api/ui/finance');
        return JSON.stringify({ status: response.status, body: await response.text() });
      }
      """);
    using (var response = System.Text.Json.JsonDocument.Parse(partnerRead))
    {
      Assert.Equal(403, response.RootElement.GetProperty("status").GetInt32());
      var body = response.RootElement.GetProperty("body").GetString();
      Assert.DoesNotContain(periodMarker, body, StringComparison.Ordinal);
      Assert.DoesNotContain(accountMarker, body, StringComparison.Ordinal);
    }
    var partnerPageText = await partnerPage.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(periodMarker, partnerPageText, StringComparison.Ordinal);
    Assert.DoesNotContain(accountMarker, partnerPageText, StringComparison.Ordinal);

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
