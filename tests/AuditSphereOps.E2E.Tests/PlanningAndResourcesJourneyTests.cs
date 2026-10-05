using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// A Partner staffs the engagement, calculates materiality from the mapped trial balance, assesses and assigns a
/// risk through Angular, records the mandatory independent review of a red risk, and sees an over-allocated week.
/// </summary>
[Trait("Category", "AuditPlanning")]
public sealed class PlanningAndResourcesJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-STE-PLANNING-01")]
  public async Task PartnerStaffsCalculatesMaterialityReviewsRedRiskAndSeesTheGrid()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-STE-PLANNING-01");
    var f = host.Fixture;
    var partner = PbcSeed.User(f.FirmId, "Staff"); partner.DisplayName = "Pat Partner";
    var manager = PbcSeed.User(f.FirmId, "Staff"); manager.DisplayName = "Mona Manager";
    var senior = PbcSeed.User(f.FirmId, "Staff"); senior.DisplayName = "Sam Senior";
    Guid redRisk;
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(partner, manager, senior);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, partner, "Partner"), PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId, f.EngagementId));
      db.StaffCertifications.Add(new StaffCertification { Id = Guid.NewGuid(), FirmId = f.FirmId, UserId = manager.Id, Name = "ACCA", RecordedAt = DateTimeOffset.UtcNow, RecordedByUserId = partner.Id });
      var datasetId = Guid.NewGuid();
      var mappingId = Guid.NewGuid();
      var accounts = new (string Code, decimal Amount, string Destination, string Section)[]
        { ("1000", 900_000m, "CASH", "ASSETS"), ("3000", 100_000m, "EQUITY", "EQUITY"), ("4000", -1_500_000m, "REVENUE", "INCOME"), ("5000", 500_000m, "COST_OF_SALES", "EXPENSE") };
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, SourceKind = "Raw", Currency = "QAR", Balanced = true,
        ValidationStatus = "Accepted", NormalizedDatasetDigest = Hashing.Sha256Hex(datasetId.ToString()), ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = senior.Id
      });
      db.TrialBalanceRows.AddRange(accounts.Select(a => new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = a.Code, AccountName = a.Destination, Amount = a.Amount, Currency = "QAR", Entity = "E" }));
      await db.SaveChangesAsync();
      await db.TrialBalanceDatasets.Where(x => x.Id == datasetId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, DatasetId = datasetId, TaxonomyVersion = "tax-v1",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved, CreatedByUserId = senior.Id,
        ApprovedByUserId = manager.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      db.MappingAllocations.AddRange(accounts.Select(a => new MappingAllocation
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, MappingVersionId = mappingId,
        SourceAccountCode = a.Code, DestinationCode = a.Destination, StatementSection = a.Section, Fraction = 1m, Rationale = "Mapped", CreatedAt = DateTimeOffset.UtcNow
      }));
      await db.SaveChangesAsync();
      var managerActor = new ActorContext(manager.Id, f.FirmId, manager.SessionEpoch, ["Manager"]);
      redRisk = (await AuditPlanningService.CreateAuditRiskAsync(db, managerActor, new CreateAuditRiskRequest(f.EngagementId, "Revenue", "Occurrence",
        "Presumed fraud risk in revenue recognition", "Incentives", SignificanceDecisions.Significant, null, "Journal and cut-off testing"))).Value!.RiskId;
      Assert.True((await RiskBandService.AssessAsync(db, managerActor, new(redRisk, 2, 2, true, "Presumed fraud risk"))).Succeeded);
    }

    var origin = await host.StartApiForIdentityAsync(partner,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await (await browser.NewContextAsync()).NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    page.Console += (_, message) => diagnostics.Add($"console-{message.Type}: {message.Text}");
    async Task SettleAsync()
    {
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
      await page.WaitForTimeoutAsync(500);
    }

    // Staffing at the four levels (associate omitted: the fixture's staff user is added to prove the list order).
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/engagements/{f.EngagementId:D}")}");
    await page.GetByText("Team and budget", new() { Exact = true }).ScrollIntoViewIfNeededAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement team" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    var planning = page.GetByRole(AriaRole.Region, new() { Name = "Engagement planning", Exact = true });
    async Task StaffAsync(Guid userId, string name, string level)
    {
      await planning.GetByLabel("Person", new() { Exact = true }).SelectOptionAsync(userId.ToString());
      await planning.GetByLabel("Level", new() { Exact = true }).SelectOptionAsync(level);
      await planning.GetByLabel("I reviewed the engagement role and client-site access.", new() { Exact = true }).CheckAsync();
      await planning.GetByRole(AriaRole.Button, new() { Name = "Review team assignment", Exact = true }).ClickAsync();
      var review = planning.GetByRole(AriaRole.Region, new() { Name = "Staffing change review", Exact = true });
      await Assertions.Expect(review).ToContainTextAsync("Full Control");
      await review.GetByLabel("I reviewed this exact staffing change and its access effects.", new() { Exact = true }).CheckAsync();
      await review.GetByRole(AriaRole.Button, new() { Name = "Confirm staffing change", Exact = true }).ClickAsync();
      await planning.GetByRole(AriaRole.Button, new() { Name = "Acknowledge staffing change", Exact = true }).ClickAsync();
      await Assertions.Expect(planning).ToContainTextAsync(name);
    }
    await StaffAsync(manager.Id, "Mona Manager", "AUDIT_MANAGER");
    await StaffAsync(senior.Id, "Sam Senior", "SENIOR_AUDITOR");
    var team = planning;
    await Assertions.Expect(team).ToContainTextAsync("Audit Manager");
    await Assertions.Expect(team).ToContainTextAsync("Senior Auditor");
    await Assertions.Expect(team).ToContainTextAsync("Senior");

    // Materiality from the mapped trial balance.
    await page.GotoAsync($"{origin}/app/engagements/{f.EngagementId:D}/audit-plan");
    var calculator = page.GetByRole(AriaRole.Region, new() { Name = "Materiality calculator", Exact = true });
    await calculator.GetByRole(AriaRole.Heading, new() { Name = "Materiality calculator" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    var benchmark = calculator.GetByRole(AriaRole.Combobox, new() { Name = "Benchmark", Exact = true });
    await benchmark.SelectOptionAsync(new SelectOptionValue { Label = "Revenue (income section) — 1,500,000.00" });
    await page.GetByLabel("Rationale for the benchmark", new() { Exact = true }).FillAsync("Revenue drives user focus for this trading entity.");
    await page.GetByLabel("Rationale for the benchmark", new() { Exact = true }).PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Calculate materiality" }).ClickAsync();
    await Assertions.Expect(page.GetByText("Materiality calculated; an independent Manager or Partner must approve it.")).ToBeVisibleAsync();
    var thresholds = page.Locator("[aria-label='Materiality thresholds']");
    await Assertions.Expect(thresholds).ToContainTextAsync("15,000.00 QAR");
    await Assertions.Expect(thresholds).ToContainTextAsync("11,250.00 QAR");
    await Assertions.Expect(thresholds).ToContainTextAsync("750.00 QAR");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Approve calculated materiality", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Approve materiality", Exact = true })).ToHaveCountAsync(0);

    // The preparer cannot approve their own calculation; an independent Manager can.
    var managerOrigin = await host.StartApiForIdentityAsync(manager,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    var managerPage = await (await browser.NewContextAsync()).NewPageAsync();
    await managerPage.GotoAsync($"{managerOrigin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/engagements/{f.EngagementId:D}/audit-plan")}");
    var materialityApproval = managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Approve calculated materiality", Exact = true });
    await Assertions.Expect(materialityApproval).ToBeVisibleAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Approve materiality", Exact = true })).ToHaveCountAsync(0);
    await materialityApproval.ClickAsync();
    await Assertions.Expect(managerPage.GetByText("Materiality approved.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var assessment = await db.MaterialityAssessments.AsNoTracking()
        .Where(x => x.EngagementId == f.EngagementId).OrderByDescending(x => x.CreatedAt).FirstAsync();
      var approval = await db.MaterialityApprovals.AsNoTracking()
        .SingleAsync(x => x.MaterialityAssessmentId == assessment.Id);
      Assert.Equal(partner.Id, assessment.ActorId);
      Assert.Equal(manager.Id, approval.ApprovedByUserId);
    }

    // Create, assess and assign a separate risk through Angular's route controls.
    var risksSection = page.Locator("section[aria-labelledby='risks-heading']");
    await risksSection.GetByText("Record an identified risk", new() { Exact = true }).ClickAsync();
    await risksSection.GetByLabel("Account or disclosure area", new() { Exact = true }).FillAsync("Cash");
    await risksSection.GetByLabel("Assertion", new() { Exact = true }).FillAsync("Valuation");
    await risksSection.GetByLabel("Risk description", new() { Exact = true }).FillAsync("Cash balance may be misstated.");
    await risksSection.GetByLabel("Drivers", new() { Exact = true }).FillAsync("Complex foreign currency balances.");
    await risksSection.GetByLabel("Planned response", new() { Exact = true }).FillAsync("Agree balances to independent bank evidence.");
    await risksSection.GetByRole(AriaRole.Button, new() { Name = "Record risk", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The identified risk was recorded.", new() { Exact = true })).ToBeVisibleAsync();
    var cashCard = page.Locator("[data-risk='Cash']");
    await Assertions.Expect(cashCard).ToContainTextAsync("NOT ASSESSED");
    var assess = cashCard.GetByRole(AriaRole.Button, new() { Name = "Assess band for Cash", Exact = true });
    await Assertions.Expect(assess).ToBeDisabledAsync();
    await cashCard.Locator("select").Nth(0).SelectOptionAsync(new SelectOptionValue { Label = "2 Moderate" });
    await cashCard.Locator("select").Nth(1).SelectOptionAsync(new SelectOptionValue { Label = "2 Moderate" });
    await page.GetByLabel("Band rationale for Cash", new() { Exact = true }).FillAsync("Moderate foreign currency valuation exposure.");
    await Assertions.Expect(assess).ToBeEnabledAsync();
    await assess.ClickAsync();
    await Assertions.Expect(cashCard).ToContainTextAsync("AMBER");
    var owner = cashCard.GetByLabel("Owner for Cash", new() { Exact = true });
    await owner.SelectOptionAsync(manager.Id.ToString());
    await cashCard.GetByRole(AriaRole.Button, new() { Name = "Assign owner for Cash", Exact = true }).ClickAsync();
    await Assertions.Expect(cashCard).ToContainTextAsync("Owner: Mona Manager (Audit Manager).");
    await using (var db = host.CreateDbContext())
    {
      var risk = await db.AuditRisks.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId && x.AccountArea == "Cash");
      var assessment = await db.RiskBandAssessments.AsNoTracking().SingleAsync(x => x.RiskId == risk.Id);
      var assignment = await db.RiskOwnerAssignments.AsNoTracking().SingleAsync(x => x.RiskBandAssessmentId == assessment.Id);
      Assert.Equal(RiskBands.Amber, assessment.Band);
      Assert.Equal(manager.Id, assignment.OwnerUserId);
    }

    // Red risk routing and the mandatory independent Partner review.
    var card = page.Locator("[data-risk='Revenue']");
    await Assertions.Expect(card).ToContainTextAsync("RED");
    await Assertions.Expect(card).ToContainTextAsync("Partner review required");
    var partnerReview = page.GetByLabel("Partner review note for Revenue", new() { Exact = true });
    var clear = page.GetByRole(AriaRole.Button, new() { Name = "Record Partner review for Revenue", Exact = true });
    await Assertions.Expect(clear).ToBeDisabledAsync();
    await partnerReview.FillAsync("Journal testing and cut-off procedures address the fraud risk.");
    await partnerReview.PressAsync("Tab");
    await clear.ClickAsync();
    await Assertions.Expect(card).ToContainTextAsync("Partner reviewed (Pat Partner)");
    await using (var db = host.CreateDbContext())
      Assert.True(await db.RiskPartnerClearances.AnyAsync(x => x.RiskId == redRisk && x.PartnerUserId == partner.Id));

    // The resource grid shows an over-allocated week.
    await using (var db = host.CreateDbContext())
    {
      var partnerActor = new ActorContext(partner.Id, f.FirmId, partner.SessionEpoch, ["Partner"]);
      Assert.True((await ResourcePlanningService.SaveProfileAsync(db, partnerActor, new(senior.Id, "Audit", "IFRS", 1200, 75m))).Succeeded);
      Assert.True((await ResourcePlanningService.SetAllocationAsync(db, partnerActor, new(f.EngagementId, senior.Id, DateOnly.FromDateTime(DateTime.Today), 1800))).Succeeded);
    }
    await page.GotoAsync($"{origin}/app/practice/resources");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Resource planning" }).WaitForAsync();
    await SettleAsync();
    var row = page.Locator("tr[data-user='Sam Senior']");
    await Assertions.Expect(row).ToContainTextAsync("Over-allocated");
    await Assertions.Expect(row).ToContainTextAsync("30 / 20 h");

    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }
}
