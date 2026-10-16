using System.Text.Json;
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

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "AS-PAR-002-ANG-ROLLFORWARD-FLOW-01")]
  public async Task DedicatedRollforwardWorkbenchCreatesReviewedDraftAndOpeningBridge(bool dropResponse)
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
    if (!dropResponse) await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/app/accounting/rollforward");
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
    var dispatches = 0;
    string? retainedRequestId = null;
    string? retainedRequestHash = null;
    page.Request += (_, request) =>
    {
      if (request.Method != "POST" || !request.Url.EndsWith("/actions", StringComparison.Ordinal)) return;
      dispatches++;
      using var input = JsonDocument.Parse(request.PostData!);
      retainedRequestId = input.RootElement.GetProperty("requestId").GetString();
      retainedRequestHash = input.RootElement.GetProperty("expectedRequestHash").GetString();
    };
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview roll-forward", Exact = true }).ClickAsync();
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact maintenance preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync(nextCode);
    await Assertions.Expect(preview).ToContainTextAsync("2026-01-01");
    await Assertions.Expect(preview).ToContainTextAsync(Hashing.Sha256Hex("rollforward-source"));
    Assert.Equal(0, dispatches);
    await page.GetByLabel("I reviewed this exact preview, source evidence and effect.", new() { Exact = true }).CheckAsync();
    if (dropResponse)
      await DropResponseReloadAndRecoverAsync(page, host.Fixture.ClientId, "Create draft period", "ROLL_FORWARD");
    else
      await page.GetByRole(AriaRole.Button, new() { Name = "Create draft period", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Verified maintenance receipt", Exact = true })).ToContainTextAsync("ROLL_FORWARD");
    Assert.Equal(1, dispatches);

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
      var receipt = await verify.PeriodMaintenanceReceipts.SingleAsync(x => x.ClientId == host.Fixture.ClientId && x.Action == "ROLL_FORWARD");
      Assert.Equal(host.Fixture.Staff.Id, receipt.ActorId);
      Assert.Equal(Guid.Parse(retainedRequestId!), receipt.RequestId);
      Assert.Equal(retainedRequestHash, receipt.RequestHash);
      Assert.Equal(1, await verify.ClientReportingPeriods.CountAsync(x => x.ClientId == host.Fixture.ClientId && x.PeriodCode == nextCode));
    }
    Assert.Empty(errors);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  [Trait("CaseId", "AS-PAR-002-ANG-RESTATEMENT-FLOW-01")]
  public async Task RestatementWorkbenchCreatesAndIndependentlyApprovesImmutableLineage(bool dropCreationResponse, bool dropApprovalResponse)
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
    var createDispatches = 0;
    preparerPage.Request += (_, request) => { if (request.Method == "POST" && request.Url.EndsWith("/actions", StringComparison.Ordinal)) createDispatches++; };
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Preview restatement request", Exact = true }).ClickAsync();
    var creationPreview = preparerPage.GetByRole(AriaRole.Region, new() { Name = "Exact maintenance preview", Exact = true });
    await Assertions.Expect(creationPreview).ToContainTextAsync(originalPackageId.ToString("D"));
    await Assertions.Expect(creationPreview).ToContainTextAsync(revisedPackageId.ToString("D"));
    await Assertions.Expect(creationPreview).ToContainTextAsync("SYN-RESTATEMENT-EVIDENCE");
    Assert.Equal(0, createDispatches);
    await preparerPage.GetByLabel("I reviewed this exact preview, source evidence and effect.", new() { Exact = true }).CheckAsync();
    if (dropCreationResponse)
      await DropResponseReloadAndRecoverAsync(preparerPage, host.Fixture.ClientId, "Create restatement request", "CREATE_RESTATEMENT");
    else
      await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Create restatement request", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Region, new() { Name = "Verified maintenance receipt", Exact = true })).ToContainTextAsync("CREATE_RESTATEMENT");
    Assert.Equal(1, createDispatches);
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
    var approvalDispatches = 0;
    reviewerPage.Request += (_, request) => { if (request.Method == "POST" && request.Url.EndsWith("/actions", StringComparison.Ordinal)) approvalDispatches++; };
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Review independent approval", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Region, new() { Name = "Exact maintenance preview", Exact = true })).ToContainTextAsync("Independently approve");
    Assert.Equal(0, approvalDispatches);
    await reviewerPage.GetByLabel("I reviewed this exact preview, source evidence and effect.", new() { Exact = true }).CheckAsync();
    if (dropApprovalResponse)
      await DropResponseReloadAndRecoverAsync(reviewerPage, host.Fixture.ClientId, "Approve independently", "APPROVE_RESTATEMENT");
    else
      await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Approve independently", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Region, new() { Name = "Verified maintenance receipt", Exact = true })).ToContainTextAsync("APPROVE_RESTATEMENT");
    Assert.Equal(1, approvalDispatches);
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
      var receipts = await verify.PeriodMaintenanceReceipts.Where(x => x.ClientId == host.Fixture.ClientId).ToListAsync();
      Assert.Single(receipts, x => x.Action == "CREATE_RESTATEMENT" && x.ActorId == host.Fixture.Staff.Id);
      Assert.Single(receipts, x => x.Action == "APPROVE_RESTATEMENT" && x.ActorId == host.Fixture.Reviewer.Id);
    }
    Assert.Empty(errors);
  }

  private static async Task DropResponseReloadAndRecoverAsync(IPage page, Guid clientId, string button, string action)
  {
    var dispatches = 0;
    string? requestId = null;
    string? requestHash = null;
    var pattern = "**/api/ui/accounting/period-maintenance/clients/*/actions";
    await page.RouteAsync(pattern, async route =>
    {
      dispatches++;
      using var input = JsonDocument.Parse(route.Request.PostData!);
      Assert.Equal(action, input.RootElement.GetProperty("action").GetString());
      Assert.True(input.RootElement.GetProperty("reviewed").GetBoolean());
      requestId = input.RootElement.GetProperty("requestId").GetString();
      requestHash = input.RootElement.GetProperty("expectedRequestHash").GetString();
      await route.FetchAsync();
      await route.AbortAsync();
    });
    await page.GetByRole(AriaRole.Button, new() { Name = button, Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact request recovery", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(1, dispatches);
    Assert.NotNull(requestId);
    Assert.NotNull(requestHash);
    await page.UnrouteAsync(pattern);
    page.Request += (_, request) => { if (request.Method == "POST" && request.Url.EndsWith("/actions", StringComparison.Ordinal)) dispatches++; };
    await page.ReloadAsync();
    await page.Locator("select[name='client']").SelectOptionAsync(clientId.ToString("D"));
    var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Exact request recovery", Exact = true });
    await Assertions.Expect(recovery).ToContainTextAsync(requestId!);
    await Assertions.Expect(recovery).ToContainTextAsync(requestHash!);
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact maintenance preview", Exact = true })).ToHaveCountAsync(0);
    await recovery.GetByRole(AriaRole.Button, new() { Name = "Look up exact receipt", Exact = true }).ClickAsync();
    var receipt = page.GetByRole(AriaRole.Region, new() { Name = "Verified maintenance receipt", Exact = true });
    await Assertions.Expect(receipt).ToContainTextAsync(action);
    await Assertions.Expect(receipt).ToContainTextAsync(requestId!);
    await Assertions.Expect(receipt).ToContainTextAsync(requestHash!);
    Assert.Equal(1, dispatches);
  }

}
