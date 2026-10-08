using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularFirmWideScopeJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-FIRM-WIDE-READ-01")]
  public async Task FirmLedgerAndPracticeLeadsRequireFirmWideRoleGrants()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-FIRM-WIDE-READ-01");
    var f = host.Fixture;
    var financeReviewer = PbcSeed.User(f.FirmId, "Staff");
    var clientFinanceManager = PbcSeed.User(f.FirmId, "Staff");
    var auditPartner = PbcSeed.User(f.FirmId, "Staff");
    var relationshipManager = PbcSeed.User(f.FirmId, "Staff");
    var clientRelationshipManager = PbcSeed.User(f.FirmId, "Staff");
    var clientRelationshipIdentity = PbcSeed.User(f.FirmId, "Client");
    const string periodCode = "2026-09";
    const string accountName = "SYN-PAR-002-FIRM-LEDGER-ACCOUNT";
    const string leadName = "SYN-PAR-002-FIRM-COMMERCIAL-LEAD";

    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(financeReviewer, clientFinanceManager, auditPartner,
        relationshipManager, clientRelationshipManager, clientRelationshipIdentity);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, financeReviewer, "FinanceReviewer"),
        PbcSeed.Grant(f.FirmId, clientFinanceManager, "FinanceManager", clientId: f.ClientId),
        PbcSeed.Grant(f.FirmId, auditPartner, "Partner"),
        PbcSeed.Grant(f.FirmId, relationshipManager, "RelationshipManager"),
        PbcSeed.Grant(f.FirmId, clientRelationshipManager, "RelationshipManager", clientId: f.ClientId),
        PbcSeed.Grant(f.FirmId, clientRelationshipIdentity, "RelationshipManager"));
      db.FirmPeriods.Add(new FirmPeriod
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PeriodCode = periodCode,
        Status = LedgerStates.PeriodOpen, Revision = 1
      });
      db.FirmAccounts.Add(new FirmAccount
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, Code = "SYN-PAR-002",
        Name = accountName, AccountType = LedgerStates.AccountAsset,
        NormalSide = LedgerStates.Debit
      });
      db.Leads.Add(new Lead
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, Name = leadName,
        Source = "Synthetic authorization fixture", Status = CrmStates.LeadNew,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var pageErrors = new List<string>();

    async Task<string> ReadAsync(Domain.Security.AppUser user, string route,
      string expectedText, bool denied, bool clientPortal = false)
    {
      var origin = await host.StartApiForIdentityAsync(user, settings);
      await using var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      page.PageError += (_, error) => pageErrors.Add($"{route} {user.Email}: {error}");
      await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
      if (denied)
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync(expectedText);
      else if (clientPortal)
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
          new() { Name = expectedText, Exact = true })).ToBeVisibleAsync();
      else
        await Assertions.Expect(page.GetByText(expectedText, new() { Exact = true })).ToBeVisibleAsync();
      return await page.Locator("main").InnerTextAsync();
    }

    var authorizedFinance = await ReadAsync(financeReviewer, "/app/finance", periodCode, denied: false);
    Assert.Contains(accountName, authorizedFinance, StringComparison.Ordinal);
    const string financeDenied = "Sign in with an authorized finance identity to access the firm ledger.";
    var scopedFinance = await ReadAsync(clientFinanceManager, "/app/finance", financeDenied, denied: true);
    Assert.DoesNotContain(periodCode, scopedFinance, StringComparison.Ordinal);
    Assert.DoesNotContain(accountName, scopedFinance, StringComparison.Ordinal);
    var partnerFinance = await ReadAsync(auditPartner, "/app/finance", financeDenied, denied: true);
    Assert.DoesNotContain(periodCode, partnerFinance, StringComparison.Ordinal);
    Assert.DoesNotContain(accountName, partnerFinance, StringComparison.Ordinal);

    var authorizedLeads = await ReadAsync(relationshipManager, "/app/practice/leads", leadName, denied: false);
    Assert.Contains(leadName, authorizedLeads, StringComparison.Ordinal);
    const string leadsDenied = "Leads unavailable. A current firm-wide commercial assignment is required.";
    var scopedLeads = await ReadAsync(clientRelationshipManager, "/app/practice/leads", leadsDenied, denied: true);
    Assert.DoesNotContain(leadName, scopedLeads, StringComparison.Ordinal);
    var clientClassified = await ReadAsync(clientRelationshipIdentity, "/app/practice/leads",
      "Client portal", denied: false, clientPortal: true);
    Assert.DoesNotContain(leadName, clientClassified, StringComparison.Ordinal);
    Assert.Empty(pageErrors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-FIRM-BOOKS-ROLE-SCOPE-01")]
  public async Task FirmBooksSeparatesFirmWideFinanceRolesAndDeniesScopedOrExternalIdentities()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-FIRM-BOOKS-ROLE-SCOPE-01");
    var f = host.Fixture;
    var financeManager = PbcSeed.User(f.FirmId, "Staff");
    var financeReviewer = PbcSeed.User(f.FirmId, "Staff");
    var clientScopedManager = PbcSeed.User(f.FirmId, "Staff");
    var partner = PbcSeed.User(f.FirmId, "Staff");
    var externalFinanceManager = PbcSeed.User(f.FirmId, "Client");
    const string payeeMarker = "SYN-PAR-002-FIRM-BOOKS-ROLE-MATRIX";
    Guid expenseId;

    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(financeManager, financeReviewer, clientScopedManager, partner, externalFinanceManager);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, financeManager, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, financeReviewer, "FinanceReviewer"),
        PbcSeed.Grant(f.FirmId, clientScopedManager, "FinanceManager", f.ClientId),
        PbcSeed.Grant(f.FirmId, partner, "Partner"),
        PbcSeed.Grant(f.FirmId, externalFinanceManager, "FinanceManager"));
      await db.SaveChangesAsync();

      var expenseAccount = await LedgerService.CreateFirmAccountAsync(db,
        PbcSeed.Actor(financeManager, "FinanceManager"),
        new CreateFirmAccountRequest("SYN-RM-EXP", "Role matrix expense", LedgerStates.AccountExpense, LedgerStates.Debit));
      var cashAccount = await LedgerService.CreateFirmAccountAsync(db,
        PbcSeed.Actor(financeManager, "FinanceManager"),
        new CreateFirmAccountRequest("SYN-RM-CASH", "Role matrix cash", LedgerStates.AccountAsset, LedgerStates.Debit));
      Assert.True(expenseAccount.Succeeded, expenseAccount.Message);
      Assert.True(cashAccount.Succeeded, cashAccount.Message);
      var recorded = await FirmExpenseService.RecordAsync(db, PbcSeed.Actor(financeManager, "FinanceManager"),
        new RecordFirmExpenseRequest(new DateOnly(2026, 10, 6), FirmExpenseCategories.Rent,
          payeeMarker, "Synthetic finance role-scope fixture", 125m, "QAR",
          expenseAccount.Value, cashAccount.Value, "role-matrix.pdf", "application/pdf", "%PDF role matrix"u8.ToArray()));
      Assert.True(recorded.Succeeded, recorded.Message);
      expenseId = recorded.Value;
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var pageErrors = new List<string>();

    async Task<(string Origin, Microsoft.Playwright.IBrowserContext Context, IPage Page)> SignInAsync(
      AuditSphereOps.Domain.Security.AppUser user)
    {
      var origin = await host.StartApiForIdentityAsync(user, settings);
      var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      page.PageError += (_, error) => pageErrors.Add($"{user.Email}: {error}");
      await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
        Uri.EscapeDataString("/app/finance/books"));
      return (origin, context, page);
    }

    var managerSession = await SignInAsync(financeManager);
    await using var managerContext = managerSession.Context;
    var managerPage = managerSession.Page;
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm books", Exact = true })).ToBeVisibleAsync();
    var managerRow = managerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = payeeMarker });
    await Assertions.Expect(managerRow).ToBeVisibleAsync();
    await Assertions.Expect(managerRow.GetByRole(AriaRole.Button,
      new() { Name = "Submit", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(1, await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Record expense", Exact = true }).CountAsync());
    Assert.Equal(0, await managerRow.GetByRole(AriaRole.Button,
      new() { Name = "Approve", Exact = true }).CountAsync());
    var managerPayload = await managerSession.Context.APIRequest.GetAsync(
      managerSession.Origin + "/api/ui/finance/books");
    Assert.True(managerPayload.Ok);
    using (var payload = System.Text.Json.JsonDocument.Parse(await managerPayload.TextAsync()))
    {
      Assert.True(payload.RootElement.GetProperty("canPrepare").GetBoolean());
      Assert.False(payload.RootElement.GetProperty("canReview").GetBoolean());
      Assert.Contains(payload.RootElement.GetProperty("expenses").EnumerateArray(), x =>
        x.GetProperty("id").GetGuid() == expenseId);
    }

    var reviewerSession = await SignInAsync(financeReviewer);
    await using var reviewerContext = reviewerSession.Context;
    var reviewerPage = reviewerSession.Page;
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm books", Exact = true })).ToBeVisibleAsync();
    var reviewerRow = reviewerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = payeeMarker });
    await Assertions.Expect(reviewerRow).ToBeVisibleAsync();
    Assert.Equal(0, await reviewerPage.GetByRole(AriaRole.Button,
      new() { Name = "Record expense", Exact = true }).CountAsync());
    Assert.Equal(0, await reviewerRow.GetByRole(AriaRole.Button,
      new() { Name = "Submit", Exact = true }).CountAsync());
    var reviewerPayload = await reviewerSession.Context.APIRequest.GetAsync(
      reviewerSession.Origin + "/api/ui/finance/books");
    Assert.True(reviewerPayload.Ok);
    using (var payload = System.Text.Json.JsonDocument.Parse(await reviewerPayload.TextAsync()))
    {
      Assert.False(payload.RootElement.GetProperty("canPrepare").GetBoolean());
      Assert.True(payload.RootElement.GetProperty("canReview").GetBoolean());
    }

    const string deniedMessage = "Firm books require a firm-wide finance assignment.";
    foreach (var (user, clientPortal) in new[]
    {
      (clientScopedManager, false),
      (partner, false),
      (externalFinanceManager, true)
    })
    {
      var deniedSession = await SignInAsync(user);
      await using var deniedContext = deniedSession.Context;
      var deniedPage = deniedSession.Page;
      if (clientPortal)
        await Assertions.Expect(deniedPage.GetByRole(AriaRole.Heading,
          new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
      else
        await Assertions.Expect(deniedPage.GetByRole(AriaRole.Alert)).ToContainTextAsync(deniedMessage);
      Assert.DoesNotContain(payeeMarker, await deniedPage.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
      var response = await deniedContext.APIRequest.GetAsync(
        deniedSession.Origin + "/api/ui/finance/books");
      Assert.Equal(403, response.Status);
      Assert.DoesNotContain(payeeMarker, await response.TextAsync(), StringComparison.Ordinal);
    }

    await using (var db = host.CreateDbContext())
    {
      var expense = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.Id == expenseId);
      Assert.Equal(FirmExpenseStates.Draft, expense.Status);
      Assert.Null(expense.JournalId);
      Assert.Null(expense.ReviewedByUserId);
      Assert.Null(expense.PostingId);
    }
    Assert.Empty(pageErrors);
  }
}
