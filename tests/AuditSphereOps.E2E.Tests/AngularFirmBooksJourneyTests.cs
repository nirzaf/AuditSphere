using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularFirmBooksJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-FIRM-BOOKS-01")]
  public async Task AngularFirmBooksKeepsExpenseReviewIndependentAndPostsBalancedTrialBalance()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-FIRM-BOOKS-01");
    var f = host.Fixture;
    var preparer = PbcSeed.Actor(f.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(f.Reviewer, "FinanceReviewer");
    Guid expenseAccountId, paymentAccountId, periodId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceReviewer"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();

      expenseAccountId = (await LedgerService.CreateFirmAccountAsync(db, preparer,
        new CreateFirmAccountRequest("SYN-EXP-6100", "Synthetic office rent", LedgerStates.AccountExpense, LedgerStates.Debit))).Value;
      paymentAccountId = (await LedgerService.CreateFirmAccountAsync(db, preparer,
        new CreateFirmAccountRequest("SYN-CASH-1000", "Synthetic bank", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
      periodId = (await LedgerService.CreateFirmPeriodAsync(db, preparer,
        new CreateFirmPeriodRequest("2026-09"))).Value;
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = f.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var preparerOrigin = await host.StartApiForIdentityAsync(f.Admin, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var errors = new List<string>();

    var preparerPage = await browser.NewPageAsync();
    preparerPage.PageError += (_, error) => errors.Add($"preparer: {error}");
    await preparerPage.GotoAsync(preparerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance/books"));
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm books", Exact = true })).ToBeVisibleAsync();
    var booksResponse = await preparerPage.Context.APIRequest.GetAsync(preparerOrigin + "/api/ui/finance/books");
    Assert.True(booksResponse.Ok, $"Firm-books API returned HTTP {(int)booksResponse.Status}: {await booksResponse.TextAsync()}");
    Assert.Equal(1, await preparerPage.Locator("select[name='expenseAccount']").CountAsync());
    await preparerPage.GetByLabel("Date", new() { Exact = true }).FillAsync("2026-09-15");
    await preparerPage.GetByLabel("Payee", new() { Exact = true }).FillAsync("Synthetic office landlord");
    await preparerPage.GetByLabel("Description", new() { Exact = true }).FillAsync("September office rent");
    await preparerPage.GetByLabel("Amount", new() { Exact = true }).FillAsync("6500.00");
    await preparerPage.Locator("select[name='expenseAccount']").SelectOptionAsync(expenseAccountId.ToString("D"));
    await preparerPage.Locator("select[name='paymentAccount']").SelectOptionAsync(paymentAccountId.ToString("D"));
    await preparerPage.GetByLabel("Source document", new() { Exact = true }).SetInputFilesAsync(
      new FilePayload { Name = "synthetic-rent.pdf", MimeType = "application/pdf", Buffer = "%PDF synthetic rent evidence"u8.ToArray() });
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Record expense", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText("Expense recorded as a draft.", new() { Exact = true })).ToBeVisibleAsync();

    var expenseRow = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic office landlord" });
    await expenseRow.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText("Submitted; its journal awaits review.", new() { Exact = true })).ToBeVisibleAsync();
    Guid expenseId;
    await using (var db = host.CreateDbContext())
    {
      var expense = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId);
      expenseId = expense.Id;
      Assert.Equal(FirmExpenseStates.Submitted, expense.Status);
      Assert.Equal(f.Admin.Id, expense.PreparedByUserId);
      Assert.NotEmpty(expense.EvidenceSha256);
      Assert.Equal(6500m, expense.Amount);
    }

    var selfReviewResponse = await preparerPage.EvaluateAsync<int>("""
      async () => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const response = await fetch('/api/ui/finance/books/expenses/__EXPENSE_ID__/review', {
          method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
          body: JSON.stringify({ approve: false, comment: 'Synthetic self-review attempt' })
        });
        return response.status;
      }
      """.Replace("__EXPENSE_ID__", expenseId.ToString("D"), StringComparison.Ordinal));
    Assert.Equal(403, selfReviewResponse);
    var ownExpenseRow = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic office landlord" });
    Assert.Equal(0, await ownExpenseRow.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).CountAsync());
    await using (var db = host.CreateDbContext())
    {
      var expense = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.Id == expenseId);
      Assert.Equal(FirmExpenseStates.Submitted, expense.Status);
      Assert.Null(expense.ReviewedByUserId);
      Assert.Null(expense.ReviewComment);
    }

    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => errors.Add($"reviewer: {error}");
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance/books"));
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm books", Exact = true })).ToBeVisibleAsync();
    var reviewRow = reviewerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic office landlord" });
    await reviewRow.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
    var rejectionDialog = reviewerPage.GetByRole(AriaRole.Dialog);
    var rejectionReason = rejectionDialog.GetByLabel("Rejection reason", new() { Exact = true });
    var rejectExpense = rejectionDialog.GetByRole(AriaRole.Button, new() { Name = "Reject expense", Exact = true });
    Assert.False(await rejectExpense.IsEnabledAsync());
    await rejectionReason.FillAsync("Receipt does not show the expense date.");
    Assert.True(await rejectExpense.IsEnabledAsync());
    await rejectExpense.ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("Rejected with the recorded reason.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var expense = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.Id == expenseId);
      Assert.Equal(FirmExpenseStates.Rejected, expense.Status);
      Assert.Equal(f.Reviewer.Id, expense.ReviewedByUserId);
      Assert.NotEqual(expense.PreparedByUserId, expense.ReviewedByUserId);
      Assert.Equal("Receipt does not show the expense date.", expense.ReviewComment);
    }

    await preparerPage.GetByLabel("Payee", new() { Exact = true }).FillAsync("Synthetic approved office landlord");
    await preparerPage.GetByLabel("Description", new() { Exact = true }).FillAsync("September office rent supported by receipt");
    await preparerPage.GetByLabel("Amount", new() { Exact = true }).FillAsync("6500.00");
    await preparerPage.GetByLabel("Source document", new() { Exact = true }).SetInputFilesAsync(
      new FilePayload { Name = "synthetic-rent-approved.pdf", MimeType = "application/pdf", Buffer = "%PDF synthetic approved rent evidence"u8.ToArray() });
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Record expense", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText("Expense recorded as a draft.", new() { Exact = true })).ToBeVisibleAsync();
    var secondPreparerRow = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic approved office landlord" });
    await secondPreparerRow.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText("Submitted; its journal awaits review.", new() { Exact = true })).ToBeVisibleAsync();
    await reviewerPage.ReloadAsync();
    var secondReviewRow = reviewerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic approved office landlord" });
    await secondReviewRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("Approved.", new() { Exact = true })).ToBeVisibleAsync();

    await preparerPage.ReloadAsync();
    var approvedRow = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic approved office landlord" });
    await approvedRow.GetByRole(AriaRole.Button, new() { Name = "Post to ledger", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText("Posted to the firm ledger.", new() { Exact = true })).ToBeVisibleAsync();
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Calculate", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Rowheader,
      new() { Name = "SYN-EXP-6100 Synthetic office rent", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(preparerPage.GetByLabel("Firm financial summary", new() { Exact = true })).ToContainTextAsync("Balanced.");
    await Assertions.Expect(preparerPage.GetByLabel("Firm financial summary", new() { Exact = true })).ToContainTextAsync("reconciles");
    await preparerPage.SetViewportSizeAsync(390, 844);
    Assert.True(await preparerPage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));

    await using (var db = host.CreateDbContext())
    {
      var rejected = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.Id == expenseId);
      Assert.Equal(FirmExpenseStates.Rejected, rejected.Status);
      Assert.Equal("Receipt does not show the expense date.", rejected.ReviewComment);
      var posted = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.Payee == "Synthetic approved office landlord");
      Assert.Equal(FirmExpenseStates.Posted, posted.Status);
      Assert.NotNull(posted.PostingId);
      Assert.Equal(LedgerStates.PeriodOpen,
        await db.FirmPeriods.Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync());
      Assert.Single(await db.FirmPostings.Where(x => x.PeriodId == periodId).ToListAsync());
    }
    Assert.Empty(errors);
  }
}
