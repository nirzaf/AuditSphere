using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularCashFlowPackageJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-CP11-CASH-FLOW-PACKAGE-BROWSER-01")]
  public async Task PreparerBuildsFromAcceptedGlAndIndependentReviewerApprovesPersistedLinks()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: true,
      caseId: "AS-CP11-CASH-FLOW-PACKAGE-BROWSER-01");
    var seed = await FinancialPackageFixture.CreatePackageAsync(host);
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var preparerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Reviewer, settings);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var preparerContext = await browser.NewContextAsync();
    var preparerPage = await preparerContext.NewPageAsync();
    var errors = new List<string>();
    preparerPage.PageError += (_, error) => errors.Add(error);
    await preparerPage.GotoAsync(preparerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/ui/app/accounting/packages/new"));
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Prepare financial-package inputs", Exact = true })).ToBeVisibleAsync();

    await preparerPage.GetByLabel("Adjustment plan ID", new() { Exact = true }).FillAsync(seed.PlanId.ToString("D"));
    await preparerPage.GetByLabel("Approved mapping ID", new() { Exact = true }).FillAsync(seed.MappingId.ToString("D"));
    await preparerPage.GetByLabel("Period start", new() { Exact = true }).FillAsync("2026-01-01");
    await preparerPage.GetByLabel("Period end", new() { Exact = true }).FillAsync("2026-12-31");
    await preparerPage.GetByLabel("Template version", new() { Exact = true }).FillAsync("e2e-cash-flow-v2");
    await preparerPage.GetByLabel("Engagement ID", new() { Exact = true }).FillAsync(host.Fixture.EngagementId.ToString("D"));
    await preparerPage.GetByLabel("Reporting period ID", new() { Exact = true }).FillAsync(seed.PeriodId.ToString("D"));
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Load accepted lines", Exact = true }).ClickAsync();
    var acceptedLine = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "1000" });
    await Assertions.Expect(acceptedLine).ToContainTextAsync("175000");
    await acceptedLine.GetByRole(AriaRole.Button, new() { Name = "Add contribution", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.Locator("input[name='line-0-0']")).ToHaveValueAsync(seed.SourceLineId.ToString("D"));
    await Assertions.Expect(preparerPage.Locator("input[name='contribution-0-0']")).ToHaveValueAsync("175000");

    await preparerPage.GetByLabel("Opening cash", new() { Exact = true }).FillAsync("0.00");
    await preparerPage.GetByLabel("Closing cash", new() { Exact = true }).FillAsync("175000.00");
    await preparerPage.GetByLabel("Cash account codes, comma separated", new() { Exact = true }).FillAsync("1000");
    await preparerPage.GetByLabel("Opening evidence references", new() { Exact = true }).FillAsync("synthetic opening bank statement");
    await preparerPage.GetByLabel("Closing evidence references", new() { Exact = true }).FillAsync("synthetic closing bank statement");
    await preparerPage.GetByLabel("Description", new() { Exact = true }).FillAsync("Synthetic customer cash receipt");
    await preparerPage.GetByLabel("Signed amount", new() { Exact = true }).FillAsync("175000.00");
    await preparerPage.GetByLabel("Movement date", new() { Exact = true }).FillAsync("2026-06-30");
    await preparerPage.GetByLabel("Activity code", new() { Exact = true }).FillAsync("CASH_RECEIPTS");
    await preparerPage.GetByLabel("Evidence references", new() { Exact = true }).FillAsync("accepted GL batch");
    await preparerPage.GetByLabel("Classification rationale", new() { Exact = true }).FillAsync("Synthetic receipt classified for the browser journey.");
    await preparerPage.Locator("input[name='disclosure-code-0']").FillAsync("CASH_POLICY");
    await preparerPage.Locator("textarea[name='disclosure-response-0']").FillAsync("Synthetic cash policy disclosure.");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Add equity line", Exact = true }).ClickAsync();
    await preparerPage.Locator("input[name='equity-code-0']").FillAsync("RETAINED_EARNINGS");
    await preparerPage.Locator("input[name='equity-description-0']").FillAsync("Retained earnings");
    await preparerPage.Locator("input[name='equity-profit-0']").FillAsync("175000.00");
    await preparerPage.Locator("input[name='equity-close-0']").FillAsync("175000.00");
    await preparerPage.Locator("input[name='equity-evidence-0']").FillAsync("synthetic equity schedule");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Queue package build", Exact = true }).ClickAsync();
    var operation = preparerPage.Locator("section[aria-labelledby='submit-heading'] p[role='status']");
    await Assertions.Expect(operation).ToContainTextAsync("Build operation:");
    var openPackage = preparerPage.GetByRole(AriaRole.Link, new() { Name = "Open resulting package", Exact = true });
    for (var attempt = 0; attempt < 60; attempt++)
    {
      if (await openPackage.CountAsync() > 0) break;
      if (await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Refresh operation", Exact = true }).CountAsync() > 0)
        await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Refresh operation", Exact = true }).ClickAsync();
      await preparerPage.WaitForTimeoutAsync(500);
    }
    await Assertions.Expect(openPackage).ToBeVisibleAsync();
    var packageRoute = await openPackage.GetAttributeAsync("href");
    Assert.NotNull(packageRoute);
    var packageId = Guid.Parse(packageRoute!.Split('/').Last());
    await openPackage.ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Cash-flow source review", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Status).Filter(new() { HasText = "Not approved" })).ToBeVisibleAsync();
    Assert.False(await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Record source decision", Exact = true }).IsVisibleAsync());

    await using var reviewerContext = await browser.NewContextAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    reviewerPage.PageError += (_, error) => errors.Add(error);
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(packageRoute));
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Cash-flow source review", Exact = true })).ToBeVisibleAsync();
    await reviewerPage.GetByLabel("Reason", new() { Exact = true }).FillAsync("Independent review of the exact source allocation and accepted batch.");
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Record source decision", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Status).Filter(new() { HasText = "Approved by an independent reviewer" })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var allocation = await db.FinancialPackageCashFlowAllocations.AsNoTracking().SingleAsync(x => x.FinancialPackageId == packageId);
      Assert.Equal(seed.SourceLineId, allocation.GeneralLedgerLineId);
      Assert.True(await db.FinancialPackageCashFlowLines.AsNoTracking().AnyAsync(x =>
        x.Id == allocation.CashFlowLineId && x.FinancialPackageId == packageId && x.Amount == 175_000m));
      Assert.Equal(host.Fixture.Staff.Id, allocation.CreatedByUserId);
      Assert.Equal("175000.00", allocation.SignedAmount.ToString("0.00"));
      var review = await db.FinancialPackageCashFlowSourceReviews.AsNoTracking().SingleAsync(x => x.FinancialPackageId == packageId);
      Assert.Equal("APPROVED", review.Decision);
      Assert.Equal(host.Fixture.Reviewer.Id, review.ReviewedByUserId);
      Assert.Equal(allocation.SourceImportBatchId, review.SourceImportBatchId);
      Assert.True(await db.FinancialPackageValidations.AsNoTracking().AnyAsync(x =>
        x.FinancialPackageId == packageId && x.Code == "CASH_FLOW_SOURCE_LINEAGE" && x.Passed));
    }
    Assert.Empty(errors);
  }
}
