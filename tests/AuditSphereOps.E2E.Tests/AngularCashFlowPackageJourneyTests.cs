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
    var inputsResponseTask = new TaskCompletionSource<(int Status, string Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
    preparerPage.Response += async (_, response) =>
    {
      if (!response.Url.Contains("/financial-package-inputs", StringComparison.Ordinal)) return;
      try { inputsResponseTask.TrySetResult((response.Status, await response.TextAsync())); }
      catch (Exception error) { inputsResponseTask.TrySetException(error); }
    };
    await preparerPage.GotoAsync(preparerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/ui/app/engagements/{host.Fixture.EngagementId:D}/financial-package"));
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Build financial package", Exact = true })).ToBeVisibleAsync();
    try
    {
      await preparerPage.Locator("form[aria-label='Financial package build']")
        .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }
    catch (TimeoutException)
    {
      throw new InvalidOperationException("Financial package inputs did not render. Page text: " +
        await preparerPage.Locator("body").InnerTextAsync() + " JavaScript errors: " + string.Join(" | ", errors));
    }

    var inputsResponse = await inputsResponseTask.Task.WaitAsync(TimeSpan.FromSeconds(10));
    if (inputsResponse.Status != 200 || !inputsResponse.Body.Contains(seed.MappingId.ToString("D"), StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException($"The build-input API did not return the seeded mapping. HTTP {inputsResponse.Status}: {inputsResponse.Body}. Page: {await preparerPage.Locator("body").InnerTextAsync()}");

    try
    {
      await preparerPage.GetByLabel("Approved mapping", new() { Exact = true })
        .SelectOptionAsync(seed.MappingId.ToString("D"), new() { Timeout = 10000 });
    }
    catch (PlaywrightException error)
    {
      throw new InvalidOperationException($"Approved mapping selector did not render the seeded mapping. HTTP {inputsResponse.Status}: {inputsResponse.Body}. " +
        $"Page: {await preparerPage.Locator("body").InnerTextAsync()}. JavaScript errors: {string.Join(" | ", errors)}", error);
    }
    await preparerPage.GetByLabel("Finalized adjustment plan", new() { Exact = true }).SelectOptionAsync(seed.PlanId.ToString("D"));
    await preparerPage.GetByLabel("Reporting period", new() { Exact = true }).SelectOptionAsync(seed.PeriodId.ToString("D"));
    await preparerPage.GetByLabel("Template version", new() { Exact = true }).FillAsync("e2e-cash-flow-v2");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Add movement", Exact = true }).ClickAsync();
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Choose source lines", Exact = true }).ClickAsync();
    var acceptedLine = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "4000" });
    await Assertions.Expect(acceptedLine).ToContainTextAsync("175000");
    await acceptedLine.GetByRole(AriaRole.Button, new() { Name = "Add to movement 1", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.Locator("input[name='alloc0-0']")).ToHaveValueAsync("175000");

    await preparerPage.GetByLabel("Opening cash", new() { Exact = true }).FillAsync("0.000001");
    await preparerPage.GetByLabel("Closing cash", new() { Exact = true }).FillAsync("175000.000001");
    await preparerPage.GetByLabel("Cash account codes (comma separated)", new() { Exact = true }).FillAsync("1000");
    await preparerPage.GetByLabel("Opening cash evidence (comma separated)", new() { Exact = true }).FillAsync("synthetic opening bank statement");
    await preparerPage.GetByLabel("Closing cash evidence (comma separated)", new() { Exact = true }).FillAsync("synthetic closing bank statement");
    await preparerPage.GetByLabel("Description", new() { Exact = true }).FillAsync("Synthetic customer cash receipt");
    await preparerPage.GetByLabel("Amount", new() { Exact = true }).FillAsync("175000.00");
    await preparerPage.GetByLabel("Movement date", new() { Exact = true }).FillAsync("2026-06-30");
    await preparerPage.GetByLabel("Activity code", new() { Exact = true }).FillAsync("CASH_RECEIPTS");
    await preparerPage.GetByLabel("Evidence references (comma separated)", new() { Exact = true }).FillAsync("accepted GL batch");
    await preparerPage.GetByLabel("Classification rationale", new() { Exact = true }).FillAsync("Synthetic receipt classified for the browser journey.");
    await preparerPage.Locator("input[name='dcode0']").FillAsync("CASH_POLICY");
    await preparerPage.Locator("textarea[name='dresp0']").FillAsync("Synthetic cash policy disclosure.");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Queue package build", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading, new() { Name = "Package build progress", Exact = true })).ToBeVisibleAsync();
    var openPackage = preparerPage.GetByRole(AriaRole.Link, new() { Name = "Open the package", Exact = true });
    await Assertions.Expect(openPackage).ToBeVisibleAsync(new() { Timeout = 30_000 });
    var href = await openPackage.GetAttributeAsync("href");
    Assert.NotNull(href);
    var packageId = Guid.Parse(href!.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1]);
    var packageRoute = $"/ui/app/accounting/packages/{packageId:D}";
    await preparerPage.GotoAsync(preparerOrigin + packageRoute);
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
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      Assert.Equal(0.000001m, package.CashBeginning);
      Assert.Equal(175_000.000001m, package.CashEnding);
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
