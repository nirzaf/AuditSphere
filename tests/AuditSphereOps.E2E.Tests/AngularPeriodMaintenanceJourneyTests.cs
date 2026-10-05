using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AccountingAndReporting")]
public sealed class AngularPeriodMaintenanceJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true"
  };

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-ROLLFORWARD-FLOW-01")]
  public async Task DedicatedRollforwardWorkbenchCreatesReviewedDraftAndOpeningBridge()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-ROLLFORWARD-FLOW-01");
    const string priorCode = "SYN-ROLLFORWARD-2025";
    const string nextCode = "SYN-ROLLFORWARD-2025-NEXT";
    Guid priorPeriodId;
    await using (var db = host.CreateDbContext())
    {
      priorPeriodId = Guid.CreateVersion7();
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff,
        "AccountingPreparer", host.Fixture.ClientId));
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = priorPeriodId,
        FirmId = host.Fixture.FirmId,
        ClientId = host.Fixture.ClientId,
        PeriodCode = priorCode,
        StartDate = new DateOnly(2025, 1, 1),
        EndDate = new DateOnly(2025, 12, 31),
        Basis = "STATUTORY",
        Currency = "QAR",
        Status = AccountingWorkflowStates.Closed,
        Revision = 2,
        CreatedByUserId = host.Fixture.Admin.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/app/accounting/rollforward")}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Period roll-forward", Exact = true })).ToBeVisibleAsync();

    await page.Locator("select[name='client']").SelectOptionAsync(host.Fixture.ClientId.ToString("D"));
    var priorPicker = page.Locator("select[name='prior']");
    await Assertions.Expect(priorPicker).ToContainTextAsync(priorCode);
    await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("1 closed periods for client");
    await page.GetByLabel("New period code", new() { Exact = true }).FillAsync(nextCode);
    await page.GetByLabel("Source SHA-256", new() { Exact = true }).FillAsync(Hashing.Sha256Hex("rollforward-source"));
    await page.GetByLabel("Evidence reference", new() { Exact = true }).FillAsync("synthetic reviewed closing evidence");
    await page.GetByLabel("I reviewed the closed prior period, opening amounts and exact source evidence.",
      new() { Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Create draft period", Exact = true }).ClickAsync();

    await Assertions.Expect(page.GetByText(
      "Draft period and opening bridge created; a reviewer must approve the opening bridge separately.",
      new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { Name = nextCode, Exact = true })).ToBeVisibleAsync();
    await using (var verify = host.CreateDbContext())
    {
      var next = await verify.ClientReportingPeriods.SingleAsync(x => x.ClientId == host.Fixture.ClientId && x.PeriodCode == nextCode);
      Assert.Equal(priorPeriodId, next.PriorPeriodId);
      Assert.Equal(AccountingWorkflowStates.Draft, next.Status);
      var bridge = await verify.OpeningBalanceBridges.SingleAsync(x => x.CurrentPeriodId == next.Id);
      Assert.Equal("RECONCILED", bridge.Status);
      Assert.Equal("synthetic reviewed closing evidence", bridge.EvidenceReference);
      Assert.Null(bridge.ApprovedByUserId);
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-RESTATEMENT-FLOW-01")]
  public async Task RestatementWorkbenchCreatesAndIndependentlyApprovesImmutableLineage()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-RESTATEMENT-FLOW-01");
    var (originalPackageId, _) = await FinancialPackageFixture.CreatePackageAsync(host);
    Guid revisedPackageId;
    Guid periodId;
    string originalHash;
    string revisedHash;
    await using (var db = host.CreateDbContext())
    {
      var original = await db.FinancialPackages.SingleAsync(x => x.Id == originalPackageId);
      Assert.NotNull(original.PeriodId);
      periodId = original.PeriodId!.Value;
      originalHash = original.CalculationHash;
      revisedHash = Hashing.Sha256Hex("restatement-revised-package");
      var revised = new FinancialPackage();
      db.Entry(revised).CurrentValues.SetValues(original);
      revised.Id = Guid.CreateVersion7();
      revised.TemplateVersion = "e2e-restatement-revised-v1";
      revised.CalculationHash = revisedHash;
      revised.CreatedAt = DateTimeOffset.UtcNow;
      revisedPackageId = revised.Id;
      db.FinancialPackages.Add(revised);
      await db.ClientReportingPeriods.Where(x => x.Id == periodId)
        .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AccountingWorkflowStates.Closed));
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "AccountingPreparer", host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "AccountingReviewer", host.Fixture.ClientId));
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var preparerContext = await browser.NewContextAsync();
    var preparerPage = await preparerContext.NewPageAsync();
    var errors = new List<string>();
    preparerPage.PageError += (_, error) => errors.Add(error);
    var preparerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Staff, Angular);
    await preparerPage.GotoAsync($"{preparerOrigin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/app/accounting/restatements")}");
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Period restatements", Exact = true })).ToBeVisibleAsync();
    await preparerPage.Locator("select[name='client']").SelectOptionAsync(host.Fixture.ClientId.ToString("D"));
    var periodPicker = preparerPage.Locator("select[name='period']");
    await Assertions.Expect(periodPicker).ToContainTextAsync("FY2026-E2E");
    await preparerPage.Locator("select[name='original']").SelectOptionAsync(originalPackageId.ToString("D"));
    await preparerPage.Locator("select[name='revised']").SelectOptionAsync(revisedPackageId.ToString("D"));
    await preparerPage.GetByLabel("Revised basis", new() { Exact = true }).FillAsync("IFRS restated basis");
    await preparerPage.Locator("select[name='type']").SelectOptionAsync("RESTATED_ERROR");
    await preparerPage.GetByLabel("Evidence reference", new() { Exact = true }).FillAsync("SYN-RESTATEMENT-EVIDENCE");
    await preparerPage.GetByLabel("Reason", new() { Exact = true }).FillAsync("Correct a synthetic prior-period classification error.");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Create restatement request", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText(
      "Restatement request created; an independent reviewer must approve it.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Cell,
      new() { Name = "submitted", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Button,
      new() { Name = "Approve independently", Exact = true })).ToHaveCountAsync(0);

    await using var reviewerContext = await browser.NewContextAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    reviewerPage.PageError += (_, error) => errors.Add(error);
    var reviewerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Reviewer, Angular);
    await reviewerPage.GotoAsync($"{reviewerOrigin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/app/accounting/restatements")}");
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Period restatements", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Cell,
      new() { Name = "SYN-RESTATEMENT-EVIDENCE", Exact = true })).ToBeVisibleAsync();
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Approve independently", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("Restatement independently approved.", new() { Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Cell,
      new() { Name = "approved", Exact = true })).ToBeVisibleAsync();

    await using (var verify = host.CreateDbContext())
    {
      var original = await verify.FinancialPackages.SingleAsync(x => x.Id == originalPackageId);
      var revised = await verify.FinancialPackages.SingleAsync(x => x.Id == revisedPackageId);
      Assert.Equal(AccountingPackageStates.PackageValidated, original.Status);
      Assert.Equal(AccountingPackageStates.PackageValidated, revised.Status);
      Assert.Equal(originalHash, original.CalculationHash);
      Assert.Equal(revisedHash, revised.CalculationHash);
      var restatement = await verify.ClientPeriodRestatements.SingleAsync(x => x.PeriodId == periodId);
      Assert.Equal(AccountingWorkflowStates.Approved, restatement.Status);
      Assert.Equal(originalHash, restatement.OriginalPackageHash);
      Assert.Equal(revisedHash, restatement.RevisedPackageHash);
      Assert.Equal(host.Fixture.Reviewer.Id, restatement.ApprovedByUserId);
      Assert.Equal("SYN-RESTATEMENT-EVIDENCE", restatement.EvidenceReference);
    }
    Assert.Empty(errors);
  }
}
