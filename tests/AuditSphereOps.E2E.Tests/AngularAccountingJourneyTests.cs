using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Tests;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAccountingJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-ACCOUNTING-E2E-01")]
  public async Task ReviewedSetupPersistsProfilePeriodAndBook()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-ACCOUNTING-E2E-01", startLegacyBlazorHosts: false);
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    string clientName;
    await using (var db = host.CreateDbContext())
    {
      clientName = (await db.PracticeClients.SingleAsync(x => x.Id == host.Fixture.ClientId)).LegalName;
      var actor = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, actor, host.Fixture.ClientId, "ANGULAR-CHART", new(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message);
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, actor, chart.Value,
        [new("cash", "1000", "Synthetic chart cash", "ASSET", "DEBIT", true)])).Succeeded);
    }
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Accounting workspace", Exact = true })).ToBeVisibleAsync();
    var accounting = page.Locator("audit-accounting");
    await accounting.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await accounting.GetByRole(AriaRole.Button, new() { Name = "Open chart of accounts", Exact = true }).ClickAsync();
    await page.Locator("audit-accounting-charts").GetByRole(AriaRole.Button, new() { Name = "View accounts · revision 1", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator("audit-accounting-charts").GetByRole(AriaRole.Cell, new() { Name = "Synthetic chart cash", Exact = true })).ToBeVisibleAsync();
    var chartPanel = page.Locator("audit-accounting-charts");
    await chartPanel.GetByLabel("Chart source scope", new() { Exact = true }).FillAsync("ANGULAR-NEXT");
    await chartPanel.GetByLabel("Chart effective from", new() { Exact = true }).FillAsync("2026-01-01");
    var chartReview = chartPanel.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this client and the latest chart version 1.", Exact = true });
    await chartReview.CheckAsync();
    await Assertions.Expect(chartReview).ToBeCheckedAsync();
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Button, new() { Name = "Create draft chart", Exact = true })).ToBeEnabledAsync();
    await chartPanel.GetByLabel("Chart source scope", new() { Exact = true }).FillAsync("ANGULAR-NEXT-EDIT");
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Button, new() { Name = "Create draft chart", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(chartReview).Not.ToBeCheckedAsync();
    await chartPanel.GetByLabel("Chart source scope", new() { Exact = true }).FillAsync("ANGULAR-NEXT");
    await chartReview.CheckAsync();
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Button, new() { Name = "Create draft chart", Exact = true })).ToBeEnabledAsync();
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "Create draft chart", Exact = true }).ClickAsync();
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "View accounts · revision 2", Exact = true }).ClickAsync();
    await chartPanel.GetByLabel("Stable account identity", new() { Exact = true }).FillAsync("cash-next");
    await chartPanel.GetByLabel("Account code", new() { Exact = true }).FillAsync("1000");
    await chartPanel.GetByLabel("Account name", new() { Exact = true }).FillAsync("Reviewed next cash");
    await chartPanel.GetByLabel("Account classification", new() { Exact = true }).FillAsync("ASSET");
    var accountReview = chartPanel.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this account and its parent within this draft chart.", Exact = true });
    await accountReview.CheckAsync();
    await Assertions.Expect(accountReview).ToBeCheckedAsync();
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Button, new() { Name = "Add reviewed account", Exact = true })).ToBeEnabledAsync();
    await chartPanel.GetByLabel("Account name", new() { Exact = true }).FillAsync("Reviewed next cash - edited");
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Button, new() { Name = "Add reviewed account", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(accountReview).Not.ToBeCheckedAsync();
    await chartPanel.GetByLabel("Account name", new() { Exact = true }).FillAsync("Reviewed next cash");
    await accountReview.CheckAsync();
    await Assertions.Expect(accountReview).ToBeCheckedAsync();
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Button, new() { Name = "Add reviewed account", Exact = true })).ToBeEnabledAsync();
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "Add reviewed account", Exact = true }).ClickAsync();
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "View accounts · revision 2", Exact = true }).ClickAsync();
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Cell, new() { Name = "Reviewed next cash", Exact = true })).ToBeVisibleAsync();
    var profile = accounting.Locator("form").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Create accounting profile", Exact = true }) });
    await profile.GetByLabel("Jurisdiction", new() { Exact = true }).FillAsync("QA");
    await profile.GetByLabel("Functional currency", new() { Exact = true }).FillAsync("QAR");
    await profile.GetByLabel("Source system", new() { Exact = true }).FillAsync("ANGULAR-SOURCE");
    await profile.GetByLabel("Source identifier", new() { Exact = true }).FillAsync("SYNTHETIC");
    await Assertions.Expect(profile.GetByRole(AriaRole.Button, new() { Name = "Save profile" })).ToBeDisabledAsync();
    await profile.GetByRole(AriaRole.Checkbox).CheckAsync();
    await profile.GetByRole(AriaRole.Button, new() { Name = "Save profile" }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Heading, new() { Name = "Revise accounting profile", Exact = true })).ToBeVisibleAsync();
    var periods = accounting.Locator("form").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Create reporting period", Exact = true }) });
    await periods.GetByLabel("Period code", new() { Exact = true }).FillAsync("2026");
    await periods.GetByLabel("Start date", new() { Exact = true }).FillAsync("2026-01-01");
    await periods.GetByLabel("End date", new() { Exact = true }).FillAsync("2026-12-31");
    await periods.GetByLabel("Reporting basis", new() { Exact = true }).FillAsync("STATUTORY");
    await periods.GetByLabel("Reporting currency", new() { Exact = true }).FillAsync("QAR");
    await periods.GetByRole(AriaRole.Checkbox).CheckAsync();
    await periods.GetByRole(AriaRole.Button, new() { Name = "Create period", Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell, new() { Name = "2026", Exact = true })).ToBeVisibleAsync();
    Guid periodId;
    await using (var db = host.CreateDbContext())
      periodId = (await db.ClientReportingPeriods.SingleAsync(x => x.ClientId == host.Fixture.ClientId)).Id;
    var books = accounting.Locator("form").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Create reporting book", Exact = true }) });
    await books.GetByLabel("Period", new() { Exact = true }).SelectOptionAsync(periodId.ToString());
    await books.GetByLabel("Book code", new() { Exact = true }).FillAsync("STAT");
    await books.GetByLabel("Basis", new() { Exact = true }).FillAsync("STATUTORY");
    await books.GetByLabel("Inclusion rule", new() { Exact = true }).FillAsync("STATUTORY_ONLY");
    await books.GetByLabel("Currency", new() { Exact = true }).FillAsync("QAR");
    await books.GetByRole(AriaRole.Checkbox).CheckAsync();
    await books.GetByRole(AriaRole.Button, new() { Name = "Create book", Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell, new() { Name = "STAT", Exact = true })).ToBeVisibleAsync();
    await page.ReloadAsync();
    await accounting.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell, new() { Name = "STAT", Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal("ANGULAR-SOURCE", (await db.ClientAccountingProfiles.SingleAsync(x => x.ClientId == host.Fixture.ClientId)).SourceSystem);
      Assert.Equal(periodId, (await db.ClientReportingBooks.SingleAsync(x => x.ClientId == host.Fixture.ClientId)).PeriodId);
    }
    await accounting.GetByRole(AriaRole.Button, new() { Name = "2026 · close/reopen", Exact = true }).ClickAsync();
    var lifecycle = page.Locator("audit-period-lifecycle");
    await Assertions.Expect(lifecycle.GetByText("Current close checks have no blockers.", new() { Exact = false })).ToBeVisibleAsync();
    await lifecycle.GetByLabel("Decision reason", new() { Exact = true }).FillAsync("Synthetic reviewed close");
    await lifecycle.GetByRole(AriaRole.Checkbox).CheckAsync();
    await lifecycle.GetByRole(AriaRole.Button, new() { Name = "Close period", Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell, new() { Name = "CLOSED / 2", Exact = true })).ToBeVisibleAsync();
    // Use the existing package builder fixture; package generation and artifacts remain real local Application work.
    var sourcePackage = await FinancialArtifactJourneyTests.CreatePackageAsync(host);
    var roll = accounting.Locator("form").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Roll forward closed period", Exact = true }) });
    await roll.GetByLabel("Closed prior period", new() { Exact = true }).SelectOptionAsync(periodId.ToString());
    await roll.GetByRole(AriaRole.Button, new() { Name = "Load validated source packages", Exact = true }).ClickAsync();
    await roll.GetByLabel("Validated source package", new() { Exact = true }).SelectOptionAsync(sourcePackage.PackageId.ToString());
    string sourceHash;
    await using (var db = host.CreateDbContext())
      sourceHash = (await db.FinancialPackages.SingleAsync(x => x.Id == sourcePackage.PackageId)).CalculationHash;
    await Assertions.Expect(roll.GetByLabel("Source SHA-256", new() { Exact = true })).ToHaveValueAsync(sourceHash);
    await roll.GetByLabel("New period code", new() { Exact = true }).FillAsync("2027");
    await roll.GetByLabel("Next start date", new() { Exact = true }).FillAsync("2027-01-01");
    await roll.GetByLabel("Next end date", new() { Exact = true }).FillAsync("2027-12-31");
    await roll.GetByLabel("Next reporting basis", new() { Exact = true }).FillAsync("STATUTORY");
    await roll.GetByLabel("Next reporting currency", new() { Exact = true }).FillAsync("QAR");
    await roll.GetByLabel("Prior closing amount", new() { Exact = true }).FillAsync("100.25");
    await roll.GetByLabel("Current opening amount", new() { Exact = true }).FillAsync("100.25");
    await roll.GetByLabel("Opening evidence reference", new() { Exact = true }).FillAsync("synthetic-opening-evidence");
    await Assertions.Expect(roll.GetByRole(AriaRole.Button, new() { Name = "Create next draft period", Exact = true })).ToBeDisabledAsync();
    await roll.GetByRole(AriaRole.Checkbox).CheckAsync();
    await roll.GetByRole(AriaRole.Button, new() { Name = "Create next draft period", Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell, new() { Name = "2027", Exact = true })).ToBeVisibleAsync();
    await accounting.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this opening bridge and exact source evidence.", Exact = true }).CheckAsync();
    await accounting.GetByRole(AriaRole.Button, new() { Name = "Approve opening bridge", Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Button, new() { Name = "Approve opening bridge", Exact = true })).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext())
    {
      var next = await db.ClientReportingPeriods.SingleAsync(x => x.ClientId == host.Fixture.ClientId && x.PeriodCode == "2027");
      Assert.Equal(periodId, next.PriorPeriodId);
      var bridge = await db.OpeningBalanceBridges.SingleAsync(x => x.CurrentPeriodId == next.Id);
      Assert.Equal("APPROVED", bridge.Status);
      Assert.Equal(100.25m, bridge.CurrentOpeningAmount);
      Assert.Equal(host.Fixture.Admin.Id, bridge.ApprovedByUserId);
      Assert.Equal(sourcePackage.PackageId, bridge.SourcePackageId);
      Assert.Equal(sourceHash, bridge.SourceHash);
      Assert.Equal("DRAFT", (await db.ClientReportingBooks.SingleAsync(x => x.PeriodId == next.Id)).Status);
    }
    await accounting.GetByRole(AriaRole.Button, new() { Name = "2026 · close/reopen", Exact = true }).ClickAsync();
    await lifecycle.GetByLabel("Decision reason", new() { Exact = true }).FillAsync("Synthetic correction");
    await lifecycle.GetByRole(AriaRole.Checkbox).CheckAsync();
    await lifecycle.GetByRole(AriaRole.Button, new() { Name = "Reopen period", Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell, new() { Name = "DRAFT / 3", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell, new() { Name = "2 → 3", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell, new() { Name = "Synthetic correction", Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
      Assert.Equal("Synthetic correction", Assert.Single(await db.ClientPeriodAmendments.Where(x => x.PeriodId == periodId).ToListAsync()).Reason);
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "AccountingReviewer"));
      await db.SaveChangesAsync();
    }
    var reviewerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Reviewer,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    await page.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await accounting.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await accounting.GetByRole(AriaRole.Button, new() { Name = "Open chart of accounts", Exact = true }).ClickAsync();
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "View accounts · revision 2", Exact = true }).ClickAsync();
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "Review publication snapshot", Exact = true }).ClickAsync();
    await chartPanel.GetByRole(AriaRole.Checkbox, new() { Name = "I independently reviewed the entire chart and this exact account snapshot.", Exact = true }).CheckAsync();
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "Publish reviewed chart", Exact = true }).ClickAsync();
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Heading, new() { Name = "Revision 2 · APPROVED", Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
      Assert.Equal(host.Fixture.Reviewer.Id, (await db.ClientChartVersions.SingleAsync(x => x.ClientId == host.Fixture.ClientId && x.Version == 2)).PublishedByUserId);
    Assert.Empty(errors);
  }
}
