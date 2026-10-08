using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularFirmExpenseActionRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-FIRM-EXP-RECOVERY-01")]
  public async Task SubmitAndReviewRequirePersistedStateCheckAfterUnconfirmedResponses()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-FIRM-EXP-RECOVERY-01");
    var f = host.Fixture;
    var preparer = PbcSeed.Actor(f.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(f.Reviewer, "FinanceReviewer");
    const string payee = "Synthetic recovery expense";
    Guid expenseId;

    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();
      var expenseAccount = await LedgerService.CreateFirmAccountAsync(db, preparer,
        new CreateFirmAccountRequest("REC-EXP-6100", "Synthetic recovery expense", LedgerStates.AccountExpense, LedgerStates.Debit));
      var paymentAccount = await LedgerService.CreateFirmAccountAsync(db, preparer,
        new CreateFirmAccountRequest("REC-CASH-1000", "Synthetic recovery cash", LedgerStates.AccountAsset, LedgerStates.Debit));
      Assert.True(expenseAccount.Succeeded, expenseAccount.Message);
      Assert.True(paymentAccount.Succeeded, paymentAccount.Message);
      Assert.True((await LedgerService.CreateFirmPeriodAsync(db, preparer,
        new CreateFirmPeriodRequest("2026-10"))).Succeeded);
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = f.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var expense = await FirmExpenseService.RecordAsync(db, preparer,
        new RecordFirmExpenseRequest(new DateOnly(2026, 10, 12), "RENT", payee,
          "Synthetic expense for outcome reconciliation", 325m, "QAR", expenseAccount.Value,
          paymentAccount.Value, "recovery-receipt.pdf", "application/pdf",
          "%PDF synthetic recovery evidence"u8.ToArray()));
      Assert.True(expense.Succeeded, expense.Message);
      expenseId = expense.Value;
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
    var expenseRow = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = payee });
    var submitCalls = 0;
    var submitRoute = $"**/api/ui/finance/books/expenses/{expenseId:D}/submit";
    await preparerPage.RouteAsync(submitRoute, async route =>
    {
      var attempt = Interlocked.Increment(ref submitCalls);
      if (attempt == 1) await route.AbortAsync();
      else await route.ContinueAsync();
    });
    await expenseRow.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Verify the saved expense action", Exact = true })).ToBeVisibleAsync();
    await preparerPage.GetByRole(AriaRole.Button,
      new() { Name = "Refresh persisted expense state", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText(
      "The exact expense is still DRAFT. Review it before allowing a deliberate retry.", new() { Exact = true })).ToBeVisibleAsync();
    Assert.Equal(1, submitCalls);
    await preparerPage.GetByRole(AriaRole.Button,
      new() { Name = "Acknowledge unchanged state and allow deliberate retry", Exact = true }).ClickAsync();
    await expenseRow.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText(
      "Submitted; its journal awaits review.", new() { Exact = true })).ToBeVisibleAsync();
    Assert.Equal(2, submitCalls);

    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => errors.Add($"reviewer: {error}");
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/finance/books"));
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm books", Exact = true })).ToBeVisibleAsync();
    var reviewerRow = reviewerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = payee });
    var reviewCalls = 0;
    var reviewRoute = $"**/api/ui/finance/books/expenses/{expenseId:D}/review";
    await reviewerPage.RouteAsync(reviewRoute, async route =>
    {
      Interlocked.Increment(ref reviewCalls);
      var accepted = await route.FetchAsync();
      Assert.Equal(200, accepted.Status);
      await route.AbortAsync();
    });
    await reviewerRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Verify the saved expense action", Exact = true })).ToBeVisibleAsync();
    await reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Refresh persisted expense state", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText(
      "Persisted state confirms the requested expense action was saved.", new() { Exact = true })).ToBeVisibleAsync();
    Assert.Equal(1, reviewCalls);
    await reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Acknowledge verified expense action", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerRow.GetByRole(AriaRole.Button,
      new() { Name = "Approve", Exact = true })).ToHaveCountAsync(0);

    await using (var db = host.CreateDbContext())
    {
      var expense = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.Id == expenseId);
      Assert.Equal(FirmExpenseStates.Approved, expense.Status);
      Assert.Equal(f.Reviewer.Id, expense.ReviewedByUserId);
      Assert.NotEqual(expense.PreparedByUserId, expense.ReviewedByUserId);
      Assert.Equal("Agreed to the source document.", expense.ReviewComment);
      var journal = await db.FirmJournals.AsNoTracking().SingleAsync(x => x.Id == expense.JournalId);
      Assert.Equal(LedgerStates.JournalApproved, journal.Status);
      Assert.Single(await db.FirmJournals.AsNoTracking()
        .Where(x => x.SourceKind == "EXPENSE" && x.SourceKey == expenseId.ToString("D")).ToListAsync());
    }
    Assert.Empty(errors);
  }
}
