using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.Json;

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
    var assetScheduleId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      var safety = await db.ClientSafetyStates.AsNoTracking().SingleAsync(x =>
        x.FirmId == host.Fixture.FirmId && x.Id == host.Fixture.ClientId);
      var now = DateTimeOffset.UtcNow;
      db.SpecialistAccountingSchedules.Add(new SpecialistAccountingSchedule
      {
        Id = assetScheduleId,
        FirmId = host.Fixture.FirmId,
        ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId,
        PeriodId = seed.PeriodId,
        InputGeneration = safety.InputGeneration,
        Revision = 1,
        Area = "ASSETS",
        MethodologyVersion = "synthetic-e2e-asset-v1",
        DepreciationMethod = "STRAIGHT_LINE",
        UsefulLifeMonths = 120,
        OpeningAmount = 100m,
        DepreciationAmount = 10m,
        ManagementAmount = 90m,
        CalculatedAmount = 90m,
        ClosingAmount = 90m,
        Difference = 0m,
        AssumptionsHash = new string('a', 64),
        EvidenceReference = "synthetic-reviewed-asset-register",
        ReviewConclusion = "Synthetic fixture: the exact schedule revision was independently reviewed.",
        Status = AccountingWorkflowStates.Approved,
        CreatedByUserId = host.Fixture.Staff.Id,
        ReviewedByUserId = host.Fixture.Reviewer.Id,
        CreatedAt = now,
        ReviewedAt = now.AddSeconds(1)
      });
      await db.SaveChangesAsync();
      var discovered = await FinancialScheduleSourceQuery.GetAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer"), host.Fixture.EngagementId, seed.PeriodId);
      Assert.True(discovered.Succeeded, discovered.Message);
      Assert.Contains(discovered.Value!.SpecialistSources, x => x.ScheduleId == assetScheduleId &&
        x.ComponentCode == "DEPRECIATION" && x.SignedAmount == "10");
    }
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var preparerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Reviewer, settings);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var preparerContext = await browser.NewContextAsync();
    var preparerPage = await preparerContext.NewPageAsync();
    var errors = new List<string>();
    preparerPage.PageError += (_, error) => errors.Add(error);
    var inputsStatus = 0;
    var inputsBody = string.Empty;
    var scheduleSourcesBody = string.Empty;
    var scheduleSourceReadCount = 0;
    var secondScheduleSourceRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    await preparerPage.RouteAsync("**/financial-package-inputs", async route =>
    {
      var response = await route.FetchAsync();
      inputsStatus = response.Status;
      inputsBody = await response.TextAsync();
      await route.FulfillAsync(new() { Status = response.Status, ContentType = "application/json", Body = inputsBody });
    });
    await preparerPage.RouteAsync("**/source-lines**", async route =>
    {
      var response = await route.FetchAsync();
      var body = await response.TextAsync();
      scheduleSourcesBody = body;
      await route.FulfillAsync(new() { Status = response.Status, ContentType = "application/json", Body = body });
      if (Interlocked.Increment(ref scheduleSourceReadCount) >= 2)
        secondScheduleSourceRead.TrySetResult(true);
    });
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

    if (inputsStatus != 200 || !inputsBody.Contains(seed.MappingId.ToString("D"), StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException($"The build-input API did not return the seeded mapping. HTTP {inputsStatus}: {inputsBody}. Page: {await preparerPage.Locator("body").InnerTextAsync()}");

    try
    {
      await preparerPage.GetByLabel("Approved mapping", new() { Exact = true })
        .SelectOptionAsync(seed.MappingId.ToString("D"), new() { Timeout = 10000 });
    }
    catch (PlaywrightException error)
    {
      throw new InvalidOperationException($"Approved mapping selector did not render the seeded mapping. HTTP {inputsStatus}: {inputsBody}. " +
        $"Page: {await preparerPage.Locator("body").InnerTextAsync()}. JavaScript errors: {string.Join(" | ", errors)}", error);
    }
    await preparerPage.GetByLabel("Finalized adjustment plan", new() { Exact = true }).SelectOptionAsync(seed.PlanId.ToString("D"));
    await preparerPage.GetByLabel("Reporting period", new() { Exact = true }).SelectOptionAsync(seed.PeriodId.ToString("D"));
    await preparerPage.GetByLabel("Template version", new() { Exact = true }).FillAsync("e2e-cash-flow-v2");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Add movement", Exact = true }).ClickAsync();
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Choose source lines", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "4000" })).ToContainTextAsync("175000");
    Assert.Contains(assetScheduleId.ToString("D"), scheduleSourcesBody, StringComparison.OrdinalIgnoreCase);
    var acceptedLine = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "4000" });
    await Assertions.Expect(acceptedLine).ToContainTextAsync("175000");
    await acceptedLine.GetByRole(AriaRole.Button, new() { Name = "Add to movement 1", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByLabel("Contribution amount", new() { Exact = true })).ToHaveValueAsync("-175000");

    // The finalized adjustment plan closes the selected cash account at QAR 175,000.
    // Keep the browser inputs aligned to that adjusted population and its cash-flow bridge.
    await preparerPage.GetByLabel("Opening cash", new() { Exact = true }).FillAsync("0");
    await preparerPage.GetByLabel("Closing cash", new() { Exact = true }).FillAsync("175000");
    await preparerPage.GetByLabel("Cash account codes (comma separated)", new() { Exact = true }).FillAsync("1000");
    await preparerPage.GetByLabel("Opening cash evidence (comma separated)", new() { Exact = true }).FillAsync("synthetic opening bank statement");
    await preparerPage.GetByLabel("Closing cash evidence (comma separated)", new() { Exact = true }).FillAsync("synthetic closing bank statement");
    await preparerPage.GetByLabel("Description", new() { Exact = true }).FillAsync("Synthetic customer cash receipt");
    await preparerPage.GetByLabel("Amount", new() { Exact = true }).FillAsync("175000.00");
    await preparerPage.GetByLabel("Movement date", new() { Exact = true }).FillAsync("2026-06-30");
    await preparerPage.GetByLabel("Activity code", new() { Exact = true }).FillAsync("CASH_RECEIPTS");
    await preparerPage.GetByLabel("Evidence references (comma separated)", new() { Exact = true }).FillAsync("accepted GL batch");
    await preparerPage.GetByLabel("Classification rationale", new() { Exact = true }).FillAsync("Synthetic receipt classified for the browser journey.");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Hide source lines", Exact = true }).ClickAsync();

    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Add movement", Exact = true }).ClickAsync();
    var secondMovement = preparerPage.GetByRole(AriaRole.Group).Filter(new() { HasText = "Movement 2" });
    await secondMovement.GetByLabel("Noncash source provenance (source links only; a selected calculation role governs the bridge)", new() { Exact = true }).CheckAsync();
    await preparerPage.GetByLabel("Description", new() { Exact = true }).Nth(1).FillAsync("Synthetic depreciation expense");
    await preparerPage.GetByLabel("Amount", new() { Exact = true }).Nth(1).FillAsync("10");
    await preparerPage.GetByLabel("Movement date", new() { Exact = true }).Nth(1).FillAsync("2026-06-30");
    await preparerPage.GetByLabel("Activity code", new() { Exact = true }).Nth(1).FillAsync("DEPRECIATION");
    await preparerPage.GetByLabel("Evidence references (comma separated)", new() { Exact = true }).Nth(1)
      .FillAsync("synthetic-reviewed-asset-register");
    await preparerPage.GetByLabel("Classification rationale", new() { Exact = true }).Nth(1)
      .FillAsync("Noncash depreciation amount tied to the exact independently approved asset schedule component.");
    await secondMovement.GetByRole(AriaRole.Button, new() { Name = "Choose source lines", Exact = true }).ClickAsync();
    await Assertions.Expect(secondMovement.GetByRole(AriaRole.Button,
      new() { Name = "Hide source lines", Exact = true })).ToBeVisibleAsync();
    await secondScheduleSourceRead.Task.WaitAsync(TimeSpan.FromSeconds(20));
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Current independently approved asset schedule components", Exact = true }),
      $"The source picker response did not render approved schedule components. Response: {scheduleSourcesBody}").ToBeVisibleAsync();
    var depreciationSource = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "DEPRECIATION" });
    await Assertions.Expect(depreciationSource).ToContainTextAsync("10 QAR");
    await depreciationSource.GetByRole(AriaRole.Button, new() { Name = "Add to movement 2", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Approved asset schedule sources", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(preparerPage.Locator("form[aria-label='Financial package build']"))
      .ToContainTextAsync(assetScheduleId.ToString("D"));

    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Add disclosure", Exact = true }).ClickAsync();
    await preparerPage.GetByLabel("Code", new() { Exact = true }).FillAsync("CASH_POLICY");
    await preparerPage.GetByLabel("Response", new() { Exact = true }).FillAsync("Synthetic cash policy disclosure.");
    await preparerPage.Locator("select[name='fxAssessmentState']").SelectOptionAsync("ASSESSED_ZERO");
    await preparerPage.GetByLabel("FX assessment evidence reference", new() { Exact = true })
      .FillAsync("synthetic-QAR-only-cash-population");
    await preparerPage.GetByLabel("FX assessment rationale", new() { Exact = true })
      .FillAsync("Synthetic reporting period contains QAR-only cash and no foreign-currency cash effects.");
    // The journey supplies no equity or note schedule, so the submit control reads as a draft with open blockers.
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Queue draft build with open blockers", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Heading, new() { Name = "Build finished", Exact = true }))
      .ToBeVisibleAsync(new() { Timeout = 30_000 });
    var openPackage = preparerPage.GetByRole(AriaRole.Link, new() { Name = "Open built package", Exact = true });
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
    var sourceReviewRequests = new List<(string RequestId, string Decision, string? Reason, string LinkDigest, string? PreviousReviewId)>();
    var dropFirstSourceReviewResponse = true;
    var firstSourceReviewResponseDropped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    await reviewerPage.RouteAsync("**/cash-flow-source-review", async route =>
    {
      if (!string.Equals(route.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
      {
        await route.ContinueAsync();
        return;
      }

      using var request = JsonDocument.Parse(route.Request.PostData ?? throw new InvalidOperationException("Source review request body was empty."));
      var body = request.RootElement;
      sourceReviewRequests.Add((body.GetProperty("requestId").GetString()!, body.GetProperty("decision").GetString()!,
        body.GetProperty("reason").ValueKind == JsonValueKind.Null ? null : body.GetProperty("reason").GetString(),
        body.GetProperty("expectedLinkDigest").GetString()!,
        body.GetProperty("expectedPreviousReviewId").ValueKind == JsonValueKind.Null ? null : body.GetProperty("expectedPreviousReviewId").GetString()));

      if (dropFirstSourceReviewResponse)
      {
        dropFirstSourceReviewResponse = false;
        var response = await route.FetchAsync();
        Assert.Equal(200, response.Status);
        await route.AbortAsync("failed");
        firstSourceReviewResponseDropped.TrySetResult(true);
        return;
      }

      await route.ContinueAsync();
    });
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(packageRoute));
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Cash-flow source review", Exact = true })).ToBeVisibleAsync();
    var reviewReason = "Independent review of the exact source allocation and accepted batch.";
    await reviewerPage.GetByLabel("Reason", new() { Exact = true }).FillAsync(reviewReason);
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Record source decision", Exact = true }).ClickAsync();
    await firstSourceReviewResponseDropped.Task.WaitAsync(TimeSpan.FromSeconds(30));
    await reviewerPage.ReloadAsync();
    await Assertions.Expect(reviewerPage.GetByLabel("Reason", new() { Exact = true })).ToHaveValueAsync(reviewReason);
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Record source decision", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Status).Filter(new() { HasText = "Approved by an independent reviewer" })).ToBeVisibleAsync();
    Assert.Equal(2, sourceReviewRequests.Count);
    Assert.Equal(sourceReviewRequests[0], sourceReviewRequests[1]);

    await using (var db = host.CreateDbContext())
    {
      var allocation = await db.FinancialPackageCashFlowAllocations.AsNoTracking().SingleAsync(x => x.FinancialPackageId == packageId);
      var scheduleAllocation = await db.FinancialPackageCashFlowScheduleAllocations.AsNoTracking()
        .SingleAsync(x => x.FinancialPackageId == packageId);
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      Assert.Equal(0m, package.CashBeginning);
      Assert.Equal(175_000m, package.CashEnding);
      Assert.Equal(seed.SourceLineId, allocation.GeneralLedgerLineId);
      Assert.True(await db.FinancialPackageCashFlowLines.AsNoTracking().AnyAsync(x =>
        x.Id == allocation.CashFlowLineId && x.FinancialPackageId == packageId && x.Amount == 175_000m));
      Assert.Equal(host.Fixture.Staff.Id, allocation.CreatedByUserId);
      Assert.Equal("-175000.00", allocation.SignedAmount.ToString("0.00"));
      Assert.Equal(assetScheduleId, scheduleAllocation.SpecialistScheduleId);
      Assert.Equal("DEPRECIATION", scheduleAllocation.ComponentCode);
      Assert.Equal(10m, scheduleAllocation.SignedAmount);
      Assert.Equal(host.Fixture.Staff.Id, scheduleAllocation.CreatedByUserId);
      Assert.Matches("^[a-f0-9]{64}$", scheduleAllocation.SourceScheduleHash);
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
