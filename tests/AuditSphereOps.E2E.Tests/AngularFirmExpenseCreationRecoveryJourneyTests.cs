using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularFirmExpenseCreationRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-FIRM-EXP-CREATE-RECOVERY-01")]
  public async Task UnknownCreateCanBeReconciledOrRetriedExactlyWithoutDuplicateExpenses()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-FIRM-EXP-CREATE-RECOVERY-01");
    var f = host.Fixture;
    var actor = PbcSeed.Actor(f.Admin, "FinanceManager");
    Guid expenseAccountId, paymentAccountId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
      await db.SaveChangesAsync();
      expenseAccountId = (await LedgerService.CreateFirmAccountAsync(db, actor,
        new CreateFirmAccountRequest("REC-CREATE-6100", "Synthetic expense creation", LedgerStates.AccountExpense, LedgerStates.Debit))).Value;
      paymentAccountId = (await LedgerService.CreateFirmAccountAsync(db, actor,
        new CreateFirmAccountRequest("REC-CREATE-1000", "Synthetic expense cash", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var origin = await host.StartApiForIdentityAsync(f.Admin, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/finance/books"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Firm books", Exact = true })).ToBeVisibleAsync();

    const string acceptedPayee = "SYNTHETIC-EXPENSE-CREATE-ACCEPTED";
    const string unacceptedPayee = "SYNTHETIC-EXPENSE-CREATE-NOT-DISPATCHED";
    var postAttempts = 0;
    string? duplicateResponseId = null;
    var firstPostStatus = 0;
    var firstPostBody = string.Empty;
    var firstPostCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await page.RouteAsync("**/api/ui/finance/books/expenses", async route =>
    {
      if (!string.Equals(route.Request.Method, "POST", StringComparison.Ordinal))
      {
        await route.ContinueAsync();
        return;
      }

      var attempt = Interlocked.Increment(ref postAttempts);
      if (attempt == 1)
      {
        var accepted = await route.FetchAsync();
        firstPostStatus = accepted.Status;
        firstPostBody = await accepted.TextAsync();
        firstPostCompleted.TrySetResult();
        if (accepted.Status == 200)
        {
          using var firstJson = JsonDocument.Parse(firstPostBody);
          var firstId = firstJson.RootElement.GetProperty("value").GetString();
          var repeated = await route.FetchAsync();
          Assert.Equal(200, repeated.Status);
          using var repeatedJson = JsonDocument.Parse(await repeated.BodyAsync());
          duplicateResponseId = repeatedJson.RootElement.GetProperty("value").GetString();
          Assert.Equal(firstId, duplicateResponseId);
        }
        await route.AbortAsync();
      }
      else if (attempt == 2)
      {
        // Simulate a request that never reached the API. The recovery read must report no receipt.
        await route.AbortAsync();
      }
      else
      {
        await route.ContinueAsync();
      }
    });

    await FillExpenseAsync(page, acceptedPayee, "Accepted then hidden response", expenseAccountId, paymentAccountId);
    await page.GetByRole(AriaRole.Button, new() { Name = "Record expense", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Verify the saved expense", Exact = true })).ToBeVisibleAsync();
    await firstPostCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Assert.True(firstPostStatus == 200, $"The create API returned HTTP {firstPostStatus}: {firstPostBody}");
    Guid diagnosticRequestId;
    string diagnosticRequestHash;
    await using (var db = host.CreateDbContext())
    {
      var row = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId && x.Payee == acceptedPayee);
      diagnosticRequestId = row.CreateRequestId!.Value;
      diagnosticRequestHash = row.CreateRequestHash!;
    }
    var directLookup = await page.EvaluateAsync<string>($$"""
      async () => {
        const response = await fetch('/api/ui/finance/books/expenses/receipts/{{diagnosticRequestId:D}}?requestHash={{diagnosticRequestHash}}');
        return response.status + ' ' + await response.text();
      }
      """);
    Assert.Contains("\"found\":true", directLookup, StringComparison.Ordinal);
    await page.GetByRole(AriaRole.Button, new() { Name = "Check saved expense", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Persisted state confirms expense");
    await Assertions.Expect(page.Locator("main")).ToContainTextAsync("is DRAFT.");
    await page.GetByRole(AriaRole.Button, new() { Name = "Acknowledge saved expense", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Persisted state confirms the saved expense. The request was not repeated.", new() { Exact = true })).ToBeVisibleAsync();

    Guid acceptedExpenseId;
    Guid acceptedRequestId;
    string acceptedRequestHash;
    await using (var db = host.CreateDbContext())
    {
      var row = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId && x.Payee == acceptedPayee);
      acceptedExpenseId = row.Id;
      acceptedRequestId = row.CreateRequestId!.Value;
      acceptedRequestHash = row.CreateRequestHash!;
      Assert.NotNull(row.CreateRequestId);
      Assert.Equal(FirmExpenseStates.Draft, row.Status);
      Assert.Equal(Hashing.Sha256Hex(row.EvidenceContent), row.EvidenceSha256);
      Assert.Single(await db.FirmExpenses.AsNoTracking().Where(x => x.FirmId == f.FirmId && x.Payee == acceptedPayee).ToListAsync());

      var request = new RecordFirmExpenseRequest(row.ExpenseDate, row.Category, row.Payee, row.Description, row.Amount,
        row.Currency, row.ExpenseAccountId, row.PaymentAccountId, row.EvidenceFileName, row.EvidenceContentType,
        row.EvidenceContent, row.CreateRequestId, row.CreateRequestHash);
      var repeated = await FirmExpenseService.RecordAsync(db, actor, request);
      Assert.True(repeated.Succeeded, repeated.Message);
      Assert.Equal(row.Id, repeated.Value);
      var changed = request with { Payee = row.Payee + " changed", RequestHash = null };
      changed = changed with { RequestHash = FirmExpenseService.CreateRequestHash(actor, changed) };
      var conflict = await FirmExpenseService.RecordAsync(db, actor, changed);
      Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
      Assert.Single(await db.FirmExpenses.AsNoTracking().Where(x => x.FirmId == f.FirmId && x.Payee == acceptedPayee).ToListAsync());
    }

    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    var reviewerPage = await browser.NewPageAsync();
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/finance/books"));
    var deniedLookup = await reviewerPage.EvaluateAsync<string>($$"""
      async () => {
        const response = await fetch('/api/ui/finance/books/expenses/receipts/{{acceptedRequestId:D}}?requestHash={{acceptedRequestHash}}');
        return JSON.stringify({ status: response.status, body: await response.text() });
      }
      """);
    using (var denial = JsonDocument.Parse(deniedLookup))
    {
      Assert.Equal(403, denial.RootElement.GetProperty("status").GetInt32());
      var body = denial.RootElement.GetProperty("body").GetString()!;
      Assert.DoesNotContain(acceptedPayee, body, StringComparison.Ordinal);
      Assert.DoesNotContain(acceptedExpenseId.ToString("D"), body, StringComparison.Ordinal);
    }

    await FillExpenseAsync(page, unacceptedPayee, "Aborted before API dispatch", expenseAccountId, paymentAccountId);
    await page.GetByRole(AriaRole.Button, new() { Name = "Record expense", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Verify the saved expense", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Check saved expense", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("No matching expense receipt is retained yet. A retry stays bound to the same request reference and exact source file.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Retry exact expense request", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Expense recorded as a draft.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = unacceptedPayee })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var rows = await db.FirmExpenses.AsNoTracking().Where(x => x.FirmId == f.FirmId && (x.Payee == acceptedPayee || x.Payee == unacceptedPayee)).ToListAsync();
      Assert.Equal(2, rows.Count);
      Assert.All(rows, x =>
      {
        Assert.NotNull(x.CreateRequestId);
        Assert.Matches("^[a-f0-9]{64}$", x.CreateRequestHash);
      });
      Assert.Equal(2, rows.Select(x => x.CreateRequestId).Distinct().Count());
    }
    Assert.Equal(3, postAttempts);
    Assert.Equal(duplicateResponseId, acceptedExpenseId.ToString("D"));
    Assert.Empty(errors);

    async Task FillExpenseAsync(IPage target, string payee, string description, Guid expenseAccount, Guid paymentAccount)
    {
      await target.GetByLabel("Date", new() { Exact = true }).FillAsync("2026-10-12");
      // RENT is the form's explicit default category; account controls use stable form names.
      await target.GetByLabel("Payee", new() { Exact = true }).FillAsync(payee);
      await target.GetByLabel("Description", new() { Exact = true }).FillAsync(description);
      await target.GetByLabel("Amount", new() { Exact = true }).FillAsync("325.00");
      await target.GetByLabel("Currency", new() { Exact = true }).FillAsync("QAR");
      await target.Locator("select[name='expenseAccount']").SelectOptionAsync(expenseAccount.ToString("D"));
      await target.Locator("select[name='paymentAccount']").SelectOptionAsync(paymentAccount.ToString("D"));
      await target.GetByLabel("Source document", new() { Exact = true }).SetInputFilesAsync(new FilePayload
      {
        Name = "creation-receipt.pdf", MimeType = "application/pdf", Buffer = Encoding.UTF8.GetBytes("%PDF synthetic expense creation receipt")
      });
    }
  }
}
