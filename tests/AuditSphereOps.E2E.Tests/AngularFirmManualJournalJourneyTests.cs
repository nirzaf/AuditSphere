using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Security.Cryptography;
using System.Text;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularFirmManualJournalJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-COMP-26-FIRM-MANUAL-JOURNAL-01")]
  public async Task FirmJournalDraftIsReconciledReviewedByAnotherUserAndPosted()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-COMP-26-FIRM-MANUAL-JOURNAL-01");
    var f = host.Fixture;
    var reviewer = PbcSeed.User(f.FirmId, "Staff");
    const string periodCode = "2026-10";
    const string journalNumber = "UI-MANUAL-OPEN-001";
    const string sourceKey = "opening-balance-schedule-test-01";
    var evidenceBytes = Encoding.UTF8.GetBytes("opening balance schedule; authorized and reviewed");
    Guid periodId;
    Guid cashAccountId;
    Guid equityAccountId;

    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(reviewer);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, reviewer, "FinanceReviewer"));
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var manager = PbcSeed.Actor(f.Admin, "FinanceManager");
      cashAccountId = (await LedgerService.CreateFirmAccountAsync(db, manager,
        new CreateFirmAccountRequest("UI-JRN-CASH", "Opening cash", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
      equityAccountId = (await LedgerService.CreateFirmAccountAsync(db, manager,
        new CreateFirmAccountRequest("UI-JRN-EQUITY", "Opening equity", LedgerStates.AccountEquity, LedgerStates.Credit))).Value;
      periodId = (await LedgerService.CreateFirmPeriodAsync(db, manager, new CreateFirmPeriodRequest(periodCode))).Value;
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
      new() { Name = "Manual journals", Exact = true })).ToBeVisibleAsync();
    var ledgerText = await managerPage.Locator("body").InnerTextAsync();
    Assert.Contains("Save draft journal", ledgerText);

    var createAttempts = 0;
    await managerPage.RouteAsync("**/api/ui/finance/journals", async route =>
    {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      if (Interlocked.Increment(ref createAttempts) == 1)
      {
        var response = await route.FetchAsync();
        Assert.Equal(200, response.Status);
        await route.AbortAsync();
      }
      else await route.ContinueAsync();
    });

    await managerPage.Locator("select[name='journalPeriod']").SelectOptionAsync(periodId.ToString());
    await managerPage.Locator("input[name='journalNumber']").FillAsync(journalNumber);
    await managerPage.Locator("select[name='journalPurpose']").SelectOptionAsync("OPENING_BALANCE");
    await managerPage.Locator("input[name='sourceReference']").FillAsync(sourceKey);
    await managerPage.Locator("input[name='journalEvidence']").SetInputFilesAsync(new FilePayload
    {
      Name = "opening-balance.csv", MimeType = "text/csv", Buffer = evidenceBytes
    });
    await managerPage.Locator("input[name='journalCurrency']").FillAsync("QAR");
    var accountSelects = managerPage.Locator("fieldset.journal-lines select");
    var accountSelectCount = await accountSelects.CountAsync();
    Assert.True(accountSelectCount >= 2,
      $"Expected two journal account choices. Found {accountSelectCount}. Selects: {string.Join(" | ", await managerPage.Locator("select").EvaluateAllAsync<string[]>("elements => elements.map(element => (element.getAttribute('name') || '(no name)') + ': ' + element.outerHTML)"))}");
    await accountSelects.Nth(0).SelectOptionAsync(cashAccountId.ToString());
    await managerPage.Locator("input[name='journalDescription0']").FillAsync("Opening bank balance");
    await managerPage.Locator("input[name='journalDebit0']").FillAsync("1250.00");
    await accountSelects.Nth(1).SelectOptionAsync(equityAccountId.ToString());
    await managerPage.Locator("input[name='journalDescription1']").FillAsync("Opening retained balance");
    await managerPage.Locator("input[name='journalCredit1']").FillAsync("1250.00");
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Save draft journal", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Verify the saved journal", Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Check saved journal", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText($"Persisted state confirms journal {journalNumber} is DRAFT.",
      new() { Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Acknowledge saved journal", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.Locator("tbody > tr:has(> th[scope='row'])").Filter(new() { HasText = journalNumber })).ToBeVisibleAsync();

    var submitAttempts = 0;
    await managerPage.RouteAsync("**/api/ui/finance/journals/*/submit", async route =>
    {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      if (Interlocked.Increment(ref submitAttempts) == 1) await route.AbortAsync();
      else await route.ContinueAsync();
    });
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Submit for review", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Verify the journal action", Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Check saved status", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText("Persisted state confirms the journal remains DRAFT.",
      new() { Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Retry exact action", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText("Journal submitted for independent review.",
      new() { Exact = true })).ToBeVisibleAsync();
    Assert.Equal(2, submitAttempts);

    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => pageErrors.Add(error);
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/finance"));
    Assert.DoesNotContain("Save draft journal", await reviewerPage.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
    var journalRow = reviewerPage.Locator("tbody > tr:has(> th[scope='row'])").Filter(new() { HasText = journalNumber });
    await Assertions.Expect(journalRow).ToContainTextAsync("review required", new() { IgnoreCase = true });
    var evidenceDownload = await reviewerPage.RunAndWaitForDownloadAsync(() =>
      journalRow.GetByRole(AriaRole.Button, new() { Name = "Download source: opening-balance.csv", Exact = true }).ClickAsync());
    Assert.Equal("opening-balance.csv", evidenceDownload.SuggestedFilename);
    await using (var stream = await evidenceDownload.CreateReadStreamAsync())
    {
      using var contents = new MemoryStream();
      await stream.CopyToAsync(contents);
      Assert.Equal(evidenceBytes, contents.ToArray());
    }
    await journalRow.GetByRole(AriaRole.Button, new() { Name = "Review and approve", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("Journal approved.", new() { Exact = true })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var journal = await db.FirmJournals.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId && x.JournalNumber == journalNumber);
      Assert.Equal(LedgerStates.JournalApproved, journal.Status);
      Assert.Equal("opening-balance.csv", journal.SupportingEvidenceFileName);
      Assert.Equal(Convert.ToHexString(SHA256.HashData(evidenceBytes)).ToLowerInvariant(), journal.SupportingEvidenceSha256);
      Assert.Equal(f.Admin.Id, journal.SupportingEvidenceUploadedByUserId);
      Assert.Equal(reviewer.Id, journal.ApprovedByUserId);
      Assert.NotNull(journal.ApprovedAt);
      Assert.Equal(2, await db.FirmJournalLines.CountAsync(x => x.FirmId == f.FirmId && x.JournalId == journal.Id));
    }

    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Refresh firm ledger", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText("Journal posted to the firm ledger.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var journal = await db.FirmJournals.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId && x.JournalNumber == journalNumber);
      Assert.Equal(LedgerStates.JournalPosted, journal.Status);
      var posting = await db.FirmPostings.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId && x.JournalId == journal.Id);
      Assert.Equal(1250m, await db.FirmPostingLines.Where(x => x.PostingId == posting.Id).SumAsync(x => x.Debit));
      Assert.Equal(1250m, await db.FirmPostingLines.Where(x => x.PostingId == posting.Id).SumAsync(x => x.Credit));
    }
    Assert.Empty(pageErrors);
  }
}
