using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularFirmLedgerBoundaryJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-FIRM-LEDGER-CLOSE-RECOVERY-01")]
  public async Task FirmLedgerCloseBlockedByUnpostedJournalCanRecoverAfterPosting()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-FIRM-LEDGER-CLOSE-RECOVERY-01");
    var f = host.Fixture;
    var manager = PbcSeed.Actor(f.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(f.Reviewer, "FinanceReviewer");
    Guid periodId, journalId;

    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "FinanceReviewer"));
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = f.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var period = await LedgerService.CreateFirmPeriodAsync(db, manager,
        new CreateFirmPeriodRequest("2026-12"));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
      var debitAccount = await LedgerService.CreateFirmAccountAsync(db, manager,
        new CreateFirmAccountRequest("REC-CASH", "Recovery cash", LedgerStates.AccountAsset,
          LedgerStates.Debit));
      Assert.True(debitAccount.Succeeded, debitAccount.Message);
      var creditAccount = await LedgerService.CreateFirmAccountAsync(db, manager,
        new CreateFirmAccountRequest("REC-EQUITY", "Recovery equity", LedgerStates.AccountEquity,
          LedgerStates.Credit));
      Assert.True(creditAccount.Succeeded, creditAccount.Message);
      var journal = await LedgerService.CreateFirmJournalDraftAsync(db, manager,
        new CreateFirmJournalDraftRequest(periodId, "REC-JRN-001", "MANUAL", "RECOVERY-001", 1,
          "MANUAL", "QAR", [
            new(debitAccount.Value, "Recovery debit", 100m, 0m),
            new(creditAccount.Value, "Recovery credit", 0m, 100m)
          ]));
      Assert.True(journal.Succeeded, journal.Message);
      journalId = journal.Value;
      Assert.Equal(LedgerStates.JournalDraft,
        await db.FirmJournals.AsNoTracking().Where(x => x.Id == journalId)
          .Select(x => x.Status).SingleAsync());
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var origin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Firm ledger & financial operations", Exact = true })).ToBeVisibleAsync();
    var row = page.Locator("section[aria-labelledby='periods-heading']").GetByRole(AriaRole.Row).Filter(new() { HasText = "2026-12" });
    await row.GetByRole(AriaRole.Button, new() { Name = "Close period", Exact = true }).ClickAsync();
    await page.GetByLabel("Close reason", new() { Exact = true }).FillAsync("Recovered after posting all journals");
    await page.GetByRole(AriaRole.Button, new() { Name = "Confirm close", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator("audit-command-message"))
      .ToContainTextAsync("All journals must be posted before period close.");

    await using (var db = host.CreateDbContext())
    {
      var blockedPeriod = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
      Assert.Equal(LedgerStates.PeriodOpen, blockedPeriod.Status);
      Assert.Equal(1, blockedPeriod.Revision);
      Assert.Empty(await db.PeriodCloseDecisions.AsNoTracking()
        .Where(x => x.PeriodId == periodId).ToListAsync());
      Assert.Equal(LedgerStates.JournalDraft,
        await db.FirmJournals.AsNoTracking().Where(x => x.Id == journalId)
          .Select(x => x.Status).SingleAsync());
    }

    await using (var db = host.CreateDbContext())
    {
      Assert.True((await LedgerService.SubmitFirmJournalAsync(db, manager, journalId)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, reviewer, journalId)).Succeeded);
      Assert.True((await LedgerService.PostFirmJournalAsync(db, manager, journalId)).Succeeded);
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Confirm close", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator("audit-command-message"))
      .ToContainTextAsync("Fiscal period successfully closed.");
    await Assertions.Expect(row).ToContainTextAsync("closed");
    await Assertions.Expect(row).ToContainTextAsync("Rev 2");

    await using (var db = host.CreateDbContext())
    {
      var closed = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
      Assert.Equal(LedgerStates.PeriodClosed, closed.Status);
      Assert.Equal(2, closed.Revision);
      var decision = await db.PeriodCloseDecisions.AsNoTracking()
        .SingleAsync(x => x.PeriodId == periodId);
      Assert.Equal("CLOSE", decision.DecisionKind);
      Assert.Equal("Recovered after posting all journals", decision.Reason);
      Assert.Equal(f.Reviewer.Id, decision.DecidedByUserId);
      Assert.Empty(pageErrors);
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-FIRM-LEDGER-BOUNDARIES-01")]
  public async Task FirmLedgerDeniesScopedReadsAndForeignPeriodCloseWithoutLeakingData()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-FIRM-LEDGER-BOUNDARIES-01");
    var f = host.Fixture;
    var scopedUser = PbcSeed.User(f.FirmId, "Staff");
    var foreignFirmId = Guid.NewGuid();
    var foreignManagerUser = PbcSeed.User(foreignFirmId, "Staff");
    var localManager = PbcSeed.Actor(f.Admin, "FinanceManager");
    var foreignManager = PbcSeed.Actor(foreignManagerUser, "FinanceManager");
    var administrator = PbcSeed.Actor(f.Admin, "Administrator");
    var foreignPeriodMarker = "2026-11";
    Guid localPeriodId, foreignPeriodId, financeReviewerGrantId;

    await using (var db = host.CreateDbContext())
    {
      db.FirmSafetyStates.Add(new FirmSafetyState { Id = foreignFirmId });
      db.Users.Add(foreignManagerUser);
      db.Users.Add(scopedUser);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, scopedUser, "FinanceManager", f.ClientId),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "FinanceReviewer"),
        PbcSeed.Grant(foreignFirmId, foreignManagerUser, "FinanceManager"));
      await db.SaveChangesAsync();

      localPeriodId = (await LedgerService.CreateFirmPeriodAsync(db, localManager,
        new CreateFirmPeriodRequest("2026-10"))).Value;
      foreignPeriodId = (await LedgerService.CreateFirmPeriodAsync(db, foreignManager,
        new CreateFirmPeriodRequest(foreignPeriodMarker))).Value;
      financeReviewerGrantId = await db.RoleGrants.AsNoTracking()
        .Where(x => x.FirmId == f.FirmId && x.UserId == f.Reviewer.Id && x.Role == "FinanceReviewer" &&
          x.ClientId == null && x.EngagementId == null && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync();
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var scopedOrigin = await host.StartApiForIdentityAsync(scopedUser, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    var scopedPage = await browser.NewPageAsync();
    var pageErrors = new List<string>();
    scopedPage.PageError += (_, error) => pageErrors.Add($"scoped: {error}");
    await scopedPage.GotoAsync(scopedOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance"));
    await Assertions.Expect(scopedPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm ledger & financial operations", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(scopedPage.GetByText("Sign in with an authorized finance identity to access the firm ledger.",
      new() { Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain(foreignPeriodMarker, await scopedPage.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
    var scopedRead = await scopedPage.EvaluateAsync<string>("""
      async () => {
        const response = await fetch('/api/ui/finance');
        return JSON.stringify({ status: response.status, body: await response.text() });
      }
      """);
    using (var read = System.Text.Json.JsonDocument.Parse(scopedRead))
    {
      Assert.Equal(403, read.RootElement.GetProperty("status").GetInt32());
      Assert.DoesNotContain(foreignPeriodMarker, read.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
    }

    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => pageErrors.Add($"reviewer: {error}");
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance"));
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm ledger & financial operations", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByText("2026-10", new() { Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain(foreignPeriodMarker, await reviewerPage.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Close period", Exact = true }).ClickAsync();
    await reviewerPage.GetByLabel("Close reason").FillAsync("   ");
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Confirm close", Exact = true })).ToBeDisabledAsync();
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

    var closeResponses = await reviewerPage.EvaluateAsync<string>("""
      async () => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const send = async id => {
          const response = await fetch(`/api/ui/finance/periods/${id}/close`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
            body: JSON.stringify({ reason: 'Synthetic unauthorized close probe' })
          });
          return { status: response.status, body: await response.text() };
        };
        const invalid = await fetch('/api/ui/finance/periods/__LOCAL_PERIOD_ID__/close', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
          body: JSON.stringify({ reason: '   ' })
        });
        const invalidBody = await invalid.text();
        const foreign = await send('__FOREIGN_PERIOD_ID__');
        const guessed = await send('__GUESSED_PERIOD_ID__');
        return JSON.stringify({ invalid: { status: invalid.status, body: invalidBody }, foreign, guessed });
      }
      """.Replace("__FOREIGN_PERIOD_ID__", foreignPeriodId.ToString("D"), StringComparison.Ordinal)
        .Replace("__LOCAL_PERIOD_ID__", localPeriodId.ToString("D"), StringComparison.Ordinal)
        .Replace("__GUESSED_PERIOD_ID__", Guid.NewGuid().ToString("D"), StringComparison.Ordinal));
    using (var responses = System.Text.Json.JsonDocument.Parse(closeResponses))
    {
      var root = responses.RootElement;
      var invalid = root.GetProperty("invalid");
      Assert.Equal(400, invalid.GetProperty("status").GetInt32());
      using (var body = System.Text.Json.JsonDocument.Parse(invalid.GetProperty("body").GetString()!))
      {
        Assert.Equal("ledger.invalid", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("A close reason is required.", body.RootElement.GetProperty("message").GetString());
      }
      Assert.Equal(403, root.GetProperty("foreign").GetProperty("status").GetInt32());
      Assert.Equal(403, root.GetProperty("guessed").GetProperty("status").GetInt32());
      Assert.Equal(root.GetProperty("foreign").GetProperty("body").GetString(),
        root.GetProperty("guessed").GetProperty("body").GetString());
      Assert.DoesNotContain(foreignPeriodMarker, root.GetProperty("foreign").GetProperty("body").GetString(), StringComparison.Ordinal);
    }

    await using (var db = host.CreateDbContext())
    {
      var foreign = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == foreignPeriodId);
      Assert.Equal(LedgerStates.PeriodOpen, foreign.Status);
      Assert.Equal(1, foreign.Revision);
      Assert.Empty(await db.PeriodCloseDecisions.AsNoTracking().Where(x => x.PeriodId == foreignPeriodId).ToListAsync());
      var local = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == localPeriodId);
      Assert.Equal(LedgerStates.PeriodOpen, local.Status);
      Assert.Equal(1, local.Revision);
      Assert.Empty(await db.PeriodCloseDecisions.AsNoTracking().Where(x => x.PeriodId == localPeriodId).ToListAsync());

      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, administrator,
        new RevokeRoleGrantRequest(financeReviewerGrantId));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    await reviewerPage.ReloadAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    var revokedText = await reviewerPage.Locator("main").InnerTextAsync();
    Assert.DoesNotContain("2026-10", revokedText);
    Assert.DoesNotContain(foreignPeriodMarker, revokedText);
    Assert.Empty(pageErrors);
  }
}
