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
    var inputsResponseTask = preparerPage.WaitForResponseAsync(response =>
      response.Url.Contains($"/api/ui/engagements/{host.Fixture.EngagementId:D}/financial-package-inputs", StringComparison.Ordinal));
    await preparerPage.GotoAsync(preparerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/ui/app/engagements/{host.Fixture.EngagementId:D}/financial-package"));
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Build financial package", Exact = true })).ToBeVisibleAsync();
    var inputsResponse = await inputsResponseTask;
    Assert.True(inputsResponse.Ok, $"Financial-package inputs returned HTTP {inputsResponse.Status}.");
    var mappingSelect = preparerPage.Locator("select[name='mapping']");

    await mappingSelect.SelectOptionAsync(seed.MappingId.ToString("D"));
    await preparerPage.Locator("select[name='plan']").SelectOptionAsync(seed.PlanId.ToString("D"));
    await preparerPage.Locator("select[name='period']").SelectOptionAsync(seed.PeriodId.ToString("D"));
    await preparerPage.Locator("input[name='template']").FillAsync("e2e-cash-flow-v2");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Add movement", Exact = true }).ClickAsync();
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Choose source lines", Exact = true }).ClickAsync();
    var acceptedLine = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "1100" });
    await Assertions.Expect(acceptedLine).ToContainTextAsync("175000");
    await acceptedLine.GetByRole(AriaRole.Button, new() { Name = "Add to movement 1", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Textbox, new() { Name = "Contribution amount", Exact = true }))
      .ToHaveValueAsync("175000");

    await preparerPage.Locator("input[name='cashBeginning']").FillAsync("0.000001");
    await preparerPage.Locator("input[name='cashEnding']").FillAsync("175000.000001");
    await preparerPage.Locator("input[name='cashAccounts']").FillAsync("1000");
    await preparerPage.Locator("input[name='openingEvidence']").FillAsync("synthetic opening bank statement");
    await preparerPage.Locator("input[name='closingEvidence']").FillAsync("synthetic closing bank statement");
    await preparerPage.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true }).FillAsync("Synthetic customer cash receipt");
    await preparerPage.GetByRole(AriaRole.Textbox, new() { Name = "Amount", Exact = true }).FillAsync("175000.00");
    await preparerPage.GetByRole(AriaRole.Textbox, new() { Name = "Movement date", Exact = true }).FillAsync("2026-06-30");
    await preparerPage.GetByRole(AriaRole.Textbox, new() { Name = "Activity code", Exact = true }).FillAsync("CASH_RECEIPTS");
    await preparerPage.GetByRole(AriaRole.Textbox, new() { Name = "Evidence references (comma separated)", Exact = true })
      .FillAsync("accepted GL batch");
    await preparerPage.GetByRole(AriaRole.Textbox, new() { Name = "Classification rationale", Exact = true })
      .FillAsync("Synthetic receipt classified for the browser journey.");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Add disclosure", Exact = true }).ClickAsync();
    await preparerPage.GetByRole(AriaRole.Textbox, new() { Name = "Code", Exact = true }).FillAsync("CASH_POLICY");
    await preparerPage.GetByRole(AriaRole.Textbox, new() { Name = "Response", Exact = true }).FillAsync("Synthetic cash policy disclosure.");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Queue package build", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading, new() { Name = "Build completed", Exact = true }))
      .ToBeVisibleAsync(new() { Timeout = 30_000 });
    var operationText = await preparerPage.Locator("section.panel[role='status'] code").InnerTextAsync();
    Assert.True(Guid.TryParse(operationText, out var operationId));
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Link, new() { Name = "Open built package", Exact = true }))
      .ToBeVisibleAsync();
    Guid? builtPackageId = null;
    for (var attempt = 0; attempt < 60 && builtPackageId is null; attempt++)
    {
      await using var db = host.CreateDbContext();
      builtPackageId = await db.FinancialPackages.AsNoTracking().Where(x => x.EngagementId == host.Fixture.EngagementId &&
          x.TemplateVersion == "e2e-cash-flow-v2").OrderByDescending(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
      if (builtPackageId is not null) break;
      await preparerPage.WaitForTimeoutAsync(500);
    }
    Assert.NotNull(builtPackageId);
    var packageId = builtPackageId!.Value;
    var packageRoute = $"/ui/app/accounting/packages/{packageId:D}";
    await using var reviewerContext = await browser.NewContextAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    reviewerPage.PageError += (_, error) => errors.Add(error);
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(packageRoute));
    var otherActorRead = await reviewerPage.GotoAsync(reviewerOrigin +
      $"/api/ui/engagements/{host.Fixture.EngagementId:D}/financial-package-builds/{operationId:D}");
    Assert.Equal(403, otherActorRead?.Status);
    await reviewerPage.GotoAsync(reviewerOrigin + packageRoute);
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Cash-flow source review", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Status).Filter(new() { HasText = "Not approved" })).ToBeVisibleAsync();
    Assert.True(await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Record source decision", Exact = true }).IsVisibleAsync());

    await preparerPage.GotoAsync(preparerOrigin + packageRoute);
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Cash-flow source review", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Status).Filter(new() { HasText = "Not approved" })).ToBeVisibleAsync();
    Assert.False(await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Record source decision", Exact = true }).IsVisibleAsync());
    await reviewerPage.GetByRole(AriaRole.Textbox, new() { Name = "Reason", Exact = true })
      .FillAsync("Independent review of the exact source allocation and accepted batch.");
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
