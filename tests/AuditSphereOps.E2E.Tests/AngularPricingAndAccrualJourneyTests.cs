using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularPricingAndAccrualJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true",
    ["AngularUi__CanonicalRoutes"] = "true"
  };

  [Fact]
  [Trait("CaseId", "COM-01-PRICING-POLICY-BROWSER")]
  public async Task PricingPolicyIsSubmittedAndApprovedByDifferentAdministrators()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "COM-01-PRICING-POLICY-BROWSER");
    var f = host.Fixture;
    var partner = PbcSeed.User(f.FirmId, "Staff");
    partner.DisplayName = "Synthetic pricing approver";
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(partner);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, partner, "Partner"));
      await db.SaveChangesAsync();
    }

    var authorOrigin = await host.StartApiForIdentityAsync(f.Admin, Angular);
    var approverOrigin = await host.StartApiForIdentityAsync(partner, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var errors = new List<string>();

    var authorContext = await browser.NewContextAsync();
    var authorPage = await authorContext.NewPageAsync();
    authorPage.PageError += (_, error) => errors.Add("author: " + error);
    await authorPage.GotoAsync(authorOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/practice/commercial-settings"));
    await Assertions.Expect(authorPage.GetByRole(AriaRole.Heading,
      new() { Name = "Commercial settings", Exact = true })).ToBeVisibleAsync();
    var policy = authorPage.Locator("section[aria-labelledby='pricing-policy-title']");
    await Assertions.Expect(policy.GetByText("No pricing policy is configured for any currency; quotations are not bounded yet.",
      new() { Exact = true })).ToBeVisibleAsync();
    await policy.GetByLabel("Currency", new() { Exact = true }).FillAsync("QAR");
    await policy.GetByLabel("Minimum fee (empty for none)", new() { Exact = true }).FillAsync("100");
    await policy.GetByLabel("Maximum fee (empty for none)", new() { Exact = true }).FillAsync("5000");
    await policy.GetByLabel("Maximum discount % (empty for none)", new() { Exact = true }).FillAsync("10");
    await policy.GetByLabel("Quotation validity window (days)", new() { Exact = true }).FillAsync("30");
    await policy.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed the effect on future quotations in this currency.", Exact = true }).CheckAsync();
    await policy.GetByRole(AriaRole.Button,
      new() { Name = "Save pricing policy draft", Exact = true }).ClickAsync();

    var row = policy.GetByRole(AriaRole.Row).Filter(new() { HasText = "QAR" });
    await Assertions.Expect(row).ToContainTextAsync("Draft");
    await row.GetByRole(AriaRole.Button,
      new() { Name = "Submit for approval", Exact = true }).ClickAsync();
    await Assertions.Expect(row).ToContainTextAsync("Pending approval");

    var approverContext = await browser.NewContextAsync();
    var approverPage = await approverContext.NewPageAsync();
    approverPage.PageError += (_, error) => errors.Add("approver: " + error);
    await approverPage.GotoAsync(approverOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/practice/commercial-settings"));
    await Assertions.Expect(approverPage.GetByRole(AriaRole.Heading,
      new() { Name = "Commercial settings", Exact = true })).ToBeVisibleAsync();
    var review = approverPage.Locator("section[aria-labelledby='pricing-policy-title']");
    var pending = review.GetByRole(AriaRole.Row).Filter(new() { HasText = "QAR" });
    await Assertions.Expect(pending).ToContainTextAsync("Pending approval");
    await review.GetByLabel("Approval reason (5 to 1000 characters)", new() { Exact = true })
      .FillAsync("Synthetic policy reviewed for future quotations.");
    await pending.GetByRole(AriaRole.Button,
      new() { Name = "Approve with recorded reason", Exact = true }).ClickAsync();
    await Assertions.Expect(pending).ToContainTextAsync("Approved · in force");

    await using (var db = host.CreateDbContext())
    {
      var saved = await db.FirmPricingPolicies.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId && x.Currency == "QAR");
      Assert.Equal(FirmPricingPolicyStates.Approved, saved.Status);
      Assert.Equal(f.Admin.Id, saved.CreatedByUserId);
      Assert.Equal(partner.Id, saved.ApprovedByUserId);
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "STE-NXT-014-END-OF-SERVICE-BLOCKED-BROWSER")]
  public async Task EndOfServicePageExplainsProfessionalGateAndDoesNotOfferAccrual()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "STE-NXT-014-END-OF-SERVICE-BLOCKED-BROWSER");
    var f = host.Fixture;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Admin, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/app/finance/end-of-service"));
    var workspace = page.Locator("audit-end-of-service");
    await Assertions.Expect(workspace.GetByRole(AriaRole.Heading,
      new() { Name = "End-of-service provision", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(workspace).ToContainTextAsync(
      "Accruals stay blocked until finance records the treatment named by a qualified accountant and a Partner confirms it.");
    var accrual = workspace.Locator("section[aria-labelledby='accrual-heading']");
    await Assertions.Expect(accrual.GetByRole(AriaRole.Alert)).ToContainTextAsync("treatment");
    await Assertions.Expect(accrual.GetByRole(AriaRole.Button,
      new() { Name = "Save accrual as a draft journal", Exact = true })).ToHaveCountAsync(0);
    Assert.Empty(errors);
  }
}
