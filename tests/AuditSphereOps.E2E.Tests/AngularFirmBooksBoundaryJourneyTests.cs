using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularFirmBooksBoundaryJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-FIRM-BOOKS-BOUNDARIES-01")]
  public async Task FirmBooksRejectMalformedAndOversizedUploadsHideForeignExpensesAndClearAfterSessionExpiry()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-FIRM-BOOKS-BOUNDARIES-01");
    var f = host.Fixture;
    var foreignFirmId = Guid.NewGuid();
    var foreignUser = PbcSeed.User(foreignFirmId, "Staff");
    var localActor = PbcSeed.Actor(f.Admin, "FinanceManager");
    var foreignActor = PbcSeed.Actor(foreignUser, "FinanceManager");
    const string foreignPayee = "SYNTHETIC-FOREIGN-FIRM-EXPENSE-MUST-NOT-LEAK";
    Guid localExpenseAccountId, localPaymentAccountId, foreignExpenseAccountId, foreignPaymentAccountId, foreignExpenseId;

    await using (var db = host.CreateDbContext())
    {
      db.FirmSafetyStates.Add(new AuditSphereOps.Domain.Completion.FirmSafetyState { Id = foreignFirmId });
      db.Users.Add(foreignUser);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
        PbcSeed.Grant(foreignFirmId, foreignUser, "FinanceManager"));
      await db.SaveChangesAsync();

      localExpenseAccountId = (await LedgerService.CreateFirmAccountAsync(db, localActor,
        new CreateFirmAccountRequest("SYN-EXP-6100", "Synthetic local office rent", LedgerStates.AccountExpense, LedgerStates.Debit))).Value;
      localPaymentAccountId = (await LedgerService.CreateFirmAccountAsync(db, localActor,
        new CreateFirmAccountRequest("SYN-CASH-1000", "Synthetic local bank", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
      foreignExpenseAccountId = (await LedgerService.CreateFirmAccountAsync(db, foreignActor,
        new CreateFirmAccountRequest("SYN-EXP-6100", "Synthetic foreign office rent", LedgerStates.AccountExpense, LedgerStates.Debit))).Value;
      foreignPaymentAccountId = (await LedgerService.CreateFirmAccountAsync(db, foreignActor,
        new CreateFirmAccountRequest("SYN-CASH-1000", "Synthetic foreign bank", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;

      var foreignExpense = await FirmExpenseService.RecordAsync(db, foreignActor, new RecordFirmExpenseRequest(
        new DateOnly(2026, 10, 1), FirmExpenseCategories.Rent, foreignPayee, "Foreign firm isolation fixture", 123m, "QAR",
        foreignExpenseAccountId, foreignPaymentAccountId, "foreign.pdf", "application/pdf", "%PDF foreign"u8.ToArray()));
      Assert.True(foreignExpense.Succeeded, foreignExpense.Message);
      foreignExpenseId = foreignExpense.Value;
    }

    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/finance/books"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Firm books", Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain(foreignPayee, await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);

    var responseJson = await page.EvaluateAsync<string>("""
      async () => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const headers = { 'X-XSRF-TOKEN': token };
        const malformed = await fetch('/api/ui/finance/books/expenses', {
          method: 'POST', headers, body: new FormData()
        });
        const malformedBody = await malformed.text();

        const form = new FormData();
        form.set('expenseDate', '2026-10-05');
        form.set('category', 'RENT');
        form.set('payee', 'SYNTHETIC-OVERSIZED-LOCAL-UPLOAD');
        form.set('description', 'Oversized upload rejection probe');
        form.set('amount', '123.00');
        form.set('currency', 'QAR');
        form.set('expenseAccountId', '__LOCAL_EXPENSE_ACCOUNT__');
        form.set('paymentAccountId', '__LOCAL_PAYMENT_ACCOUNT__');
        form.set('requestId', '6f1d2c3e-4b5a-4c7d-8e9f-0a1b2c3d4e5f');
        form.set('requestHash', 'a'.repeat(64));
        form.set('evidence', new File([new Uint8Array(5242881)], 'oversized.pdf', { type: 'application/pdf' }));
        const oversized = await fetch('/api/ui/finance/books/expenses', {
          method: 'POST', headers, body: form
        });
        const oversizedBody = await oversized.text();

        const submitForeign = await fetch('/api/ui/finance/books/expenses/__FOREIGN_EXPENSE_ID__/submit', {
          method: 'POST', headers: { ...headers, 'Content-Type': 'application/json' }, body: '{}'
        });
        const submitForeignBody = await submitForeign.text();
        const submitUnknown = await fetch('/api/ui/finance/books/expenses/__UNKNOWN_EXPENSE_ID__/submit', {
          method: 'POST', headers: { ...headers, 'Content-Type': 'application/json' }, body: '{}'
        });
        const submitUnknownBody = await submitUnknown.text();
        const postForeign = await fetch('/api/ui/finance/books/expenses/__FOREIGN_EXPENSE_ID__/post', {
          method: 'POST', headers: { ...headers, 'Content-Type': 'application/json' }, body: '{}'
        });
        const postForeignBody = await postForeign.text();
        const postUnknown = await fetch('/api/ui/finance/books/expenses/__UNKNOWN_EXPENSE_ID__/post', {
          method: 'POST', headers: { ...headers, 'Content-Type': 'application/json' }, body: '{}'
        });
        const postUnknownBody = await postUnknown.text();
        return JSON.stringify({
          malformedStatus: malformed.status, malformedBody,
          oversizedStatus: oversized.status, oversizedBody,
          submitForeignStatus: submitForeign.status, submitForeignBody,
          submitUnknownStatus: submitUnknown.status, submitUnknownBody,
          postForeignStatus: postForeign.status, postForeignBody,
          postUnknownStatus: postUnknown.status, postUnknownBody
        });
      }
      """.Replace("__LOCAL_EXPENSE_ACCOUNT__", localExpenseAccountId.ToString("D"), StringComparison.Ordinal)
        .Replace("__LOCAL_PAYMENT_ACCOUNT__", localPaymentAccountId.ToString("D"), StringComparison.Ordinal)
        .Replace("__FOREIGN_EXPENSE_ID__", foreignExpenseId.ToString("D"), StringComparison.Ordinal)
        .Replace("__UNKNOWN_EXPENSE_ID__", Guid.NewGuid().ToString("D"), StringComparison.Ordinal));

    using (var responses = JsonDocument.Parse(responseJson))
    {
      var root = responses.RootElement;
      Assert.Equal(400, root.GetProperty("malformedStatus").GetInt32());
      Assert.Contains("request.invalid", root.GetProperty("malformedBody").GetString(), StringComparison.Ordinal);
      Assert.Equal(400, root.GetProperty("oversizedStatus").GetInt32());
      Assert.Contains("up to 5 MB", root.GetProperty("oversizedBody").GetString(), StringComparison.Ordinal);
      Assert.Equal(403, root.GetProperty("submitForeignStatus").GetInt32());
      Assert.Equal(403, root.GetProperty("submitUnknownStatus").GetInt32());
      Assert.Equal(root.GetProperty("submitForeignBody").GetString(), root.GetProperty("submitUnknownBody").GetString());
      Assert.Equal(403, root.GetProperty("postForeignStatus").GetInt32());
      Assert.Equal(403, root.GetProperty("postUnknownStatus").GetInt32());
      Assert.Equal(root.GetProperty("postForeignBody").GetString(), root.GetProperty("postUnknownBody").GetString());
      foreach (var property in new[] { "submitForeignBody", "submitUnknownBody", "postForeignBody", "postUnknownBody" })
        Assert.DoesNotContain(foreignPayee, root.GetProperty(property).GetString(), StringComparison.Ordinal);
    }

    await using (var db = host.CreateDbContext())
    {
      var foreign = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.Id == foreignExpenseId);
      Assert.Equal(FirmExpenseStates.Draft, foreign.Status);
      Assert.Null(foreign.JournalId);
      Assert.Null(foreign.PostingId);
      Assert.Empty(await db.FirmExpenses.AsNoTracking().Where(x => x.FirmId == f.FirmId).ToListAsync());
      await db.Users.Where(x => x.Id == f.Admin.Id)
        .ExecuteUpdateAsync(x => x.SetProperty(user => user.SessionEpoch, user => user.SessionEpoch + 1));
    }

    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain(foreignPayee, await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
    Assert.DoesNotContain("SYNTHETIC-OVERSIZED-LOCAL-UPLOAD", await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
    Assert.Empty(pageErrors);
  }
}
