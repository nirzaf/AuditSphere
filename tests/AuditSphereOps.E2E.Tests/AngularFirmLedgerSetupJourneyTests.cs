using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularFirmLedgerSetupJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-COMP-26-FIRM-LEDGER-SETUP-01")]
  public async Task FinanceManagerCanCreateFirmAccountsAndPeriodsWithUnknownOutcomeRecovery_ReviewerCannotCreateThem()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-COMP-26-FIRM-LEDGER-SETUP-01");
    var f = host.Fixture;
    var reviewer = PbcSeed.User(f.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(reviewer);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var managerOrigin = await host.StartApiForIdentityAsync(f.Admin, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(reviewer, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var pageErrors = new List<string>();
    var managerPage = await browser.NewPageAsync();
    managerPage.PageError += (_, error) => pageErrors.Add(error);
    await managerPage.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/finance"));
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm ledger & financial operations", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Set up firm ledger", Exact = true })).ToBeVisibleAsync();

    const string accountCode = "UI-SETUP-1000";
    var accountAttempts = 0;
    var acceptedAccountStatus = 0;
    await managerPage.RouteAsync("**/api/ui/finance/accounts", async route =>
    {
      if (!string.Equals(route.Request.Method, "POST", StringComparison.Ordinal))
      {
        await route.ContinueAsync();
        return;
      }
      if (Interlocked.Increment(ref accountAttempts) == 1)
      {
        var response = await route.FetchAsync();
        acceptedAccountStatus = response.Status;
        await route.AbortAsync();
      }
      else await route.ContinueAsync();
    });

    await managerPage.GetByLabel("Account code", new() { Exact = true }).FillAsync(accountCode);
    await managerPage.GetByLabel("Account name", new() { Exact = true }).FillAsync("Synthetic operating bank");
    await managerPage.Locator("select[name='accountType']").SelectOptionAsync("ASSET");
    await managerPage.Locator("select[name='normalSide']").SelectOptionAsync("DEBIT");
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Create account", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Verify saved ledger setup", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Check saved setup", Exact = true })).ToBeEnabledAsync();
    Assert.Equal(200, acceptedAccountStatus);
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Check saved setup", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText(
      $"Persisted state confirms account {accountCode} Synthetic operating bank.", new() { Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Acknowledge saved setup", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = accountCode })).ToBeVisibleAsync();

    const string periodCode = "2026-12";
    var periodAttempts = 0;
    await managerPage.RouteAsync("**/api/ui/finance/periods", async route =>
    {
      if (!string.Equals(route.Request.Method, "POST", StringComparison.Ordinal))
      {
        await route.ContinueAsync();
        return;
      }
      if (Interlocked.Increment(ref periodAttempts) == 1) await route.AbortAsync();
      else await route.ContinueAsync();
    });
    await managerPage.GetByLabel("Fiscal month", new() { Exact = true }).FillAsync(periodCode);
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Create fiscal period", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Verify saved ledger setup", Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Check saved setup", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText(
      "No period with this exact code is present in persisted firm ledger state.", new() { Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Retry exact setup", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText("Fiscal period opened.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = periodCode })).ToBeVisibleAsync();
    Assert.Equal(2, periodAttempts);

    await using (var db = host.CreateDbContext())
    {
      var account = await db.FirmAccounts.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId && x.Code == accountCode);
      Assert.Equal("Synthetic operating bank", account.Name);
      Assert.Equal(LedgerStates.AccountAsset, account.AccountType);
      Assert.Equal(LedgerStates.Debit, account.NormalSide);
      Assert.True(account.PostingAllowed);
      Assert.Single(await db.FirmPeriods.AsNoTracking().Where(x => x.FirmId == f.FirmId && x.PeriodCode == periodCode).ToListAsync());
    }

    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => pageErrors.Add(error);
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/finance"));
    await Assertions.Expect(reviewerPage.GetByText(accountCode, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Set up firm ledger", Exact = true })).ToHaveCountAsync(0);
    var deniedStatus = await reviewerPage.EvaluateAsync<int>("""
      async () => {
        await fetch('/api/ui/session');
        const proof = document.cookie.split(';').map(x => x.trim())
          .find(x => x.startsWith('XSRF-TOKEN='));
        const token = proof ? decodeURIComponent(proof.slice('XSRF-TOKEN='.length)) : '';
        const response = await fetch('/api/ui/finance/accounts', {
          method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
          body: JSON.stringify({ code: 'REVIEWER-DENIED', name: 'Denied', accountType: 'ASSET', normalSide: 'DEBIT' })
        });
        return response.status;
      }
      """);
    Assert.Equal(403, deniedStatus);
    await using (var db = host.CreateDbContext())
      Assert.Empty(await db.FirmAccounts.AsNoTracking().Where(x => x.FirmId == f.FirmId && x.Code == "REVIEWER-DENIED").ToListAsync());
    Assert.Empty(pageErrors);
  }
}
