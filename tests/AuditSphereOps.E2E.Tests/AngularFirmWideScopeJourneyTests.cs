using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
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
}
