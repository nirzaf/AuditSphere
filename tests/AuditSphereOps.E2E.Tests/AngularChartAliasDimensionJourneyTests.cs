using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularChartAliasDimensionJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-CHART-ALIAS-DIMENSION-01")]
  public async Task StaffCanReviewAndPersistClientChartAliasAndDimension()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-CHART-ALIAS-DIMENSION-01");
    var fixture = host.Fixture;
    string clientName;
    Guid chartId, accountId;
    await using (var db = host.CreateDbContext())
    {
      clientName = (await db.PracticeClients.SingleAsync(x => x.Id == fixture.ClientId)).LegalName;
      db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, fixture.Staff, "AccountingPreparer", clientId: fixture.ClientId));
      await db.SaveChangesAsync();

      var actor = PbcSeed.Actor(fixture.Staff, "AccountingPreparer");
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, actor, fixture.ClientId,
        "ANGULAR-ALIASES", new(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message);
      chartId = chart.Value;
      var accounts = await ClientAccountingService.AddAccountsAsync(db, actor, chartId,
        [new("cash", "1000", "Synthetic operating cash", "ASSET", "DEBIT", true)],
        expectedClientId: fixture.ClientId, expectedVersion: 1);
      Assert.True(accounts.Succeeded, accounts.Message);
      accountId = await db.ClientAccounts.Where(x => x.ChartVersionId == chartId)
        .Select(x => x.Id).SingleAsync();
    }

    var origin = await host.StartApiForIdentityAsync(fixture.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var failDimensions = true;
    await page.RouteAsync("**/api/ui/accounting/clients/*/dimensions", async route =>
    {
      if (failDimensions)
      {
        failDimensions = false;
        await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{}" });
      }
      else await route.ContinueAsync();
    });
    var failAliases = true;
    await page.RouteAsync("**/api/ui/accounting/clients/*/charts/*/aliases", async route =>
    {
      if (failAliases)
      {
        failAliases = false;
        await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{}" });
      }
      else await route.ContinueAsync();
    });
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Accounting workspace", Exact = true })).ToBeVisibleAsync();

    var accounting = page.Locator("audit-accounting");
    await accounting.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByText("Accounting dimensions are unavailable. Retry before continuing.",
      new() { Exact = true })).ToBeVisibleAsync();
    var dimensionForm = accounting.Locator("form").Filter(new()
      { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Add accounting dimension", Exact = true }) });
    await dimensionForm.GetByLabel("Dimension type", new() { Exact = true }).SelectOptionAsync("BRANCH");
    await dimensionForm.GetByLabel("Code", new() { Exact = true }).FillAsync("HQ-DOHA");
    await dimensionForm.GetByLabel("Name", new() { Exact = true }).FillAsync("Doha headquarters");
    var dimensionReview = dimensionForm.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this dimension definition for this client.", Exact = true });
    await dimensionReview.CheckAsync();
    await Assertions.Expect(dimensionForm.GetByRole(AriaRole.Button,
      new() { Name = "Add dimension", Exact = true })).ToBeDisabledAsync();
    await accounting.GetByRole(AriaRole.Button, new() { Name = "Retry dimensions", Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByText("No accounting dimensions configured for this client.",
      new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(dimensionForm.GetByRole(AriaRole.Button,
      new() { Name = "Add dimension", Exact = true })).ToBeEnabledAsync();
    await dimensionForm.GetByLabel("Name", new() { Exact = true }).FillAsync("Doha headquarters - reviewed edit");
    await Assertions.Expect(dimensionForm.GetByRole(AriaRole.Button,
      new() { Name = "Add dimension", Exact = true })).ToBeDisabledAsync();
    await dimensionForm.GetByLabel("Name", new() { Exact = true }).FillAsync("Doha headquarters");
    await dimensionForm.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this dimension definition for this client.", Exact = true }).CheckAsync();
    await dimensionForm.GetByRole(AriaRole.Button, new() { Name = "Add dimension", Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell,
      new() { Name = "Doha headquarters", Exact = true })).ToBeVisibleAsync();

    await accounting.GetByRole(AriaRole.Button, new() { Name = "Open chart of accounts", Exact = true }).ClickAsync();
    var chartPanel = page.Locator("audit-accounting-charts");
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "View accounts · revision 1", Exact = true }).ClickAsync();
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Cell,
      new() { Name = "Synthetic operating cash", Exact = true })).ToBeVisibleAsync();
    var aliasForm = chartPanel.Locator("form").Filter(new()
      { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Add source account alias to revision 1", Exact = true }) });
    await aliasForm.GetByLabel("Target client account", new() { Exact = true })
      .SelectOptionAsync(accountId.ToString());
    await aliasForm.GetByLabel("Source system", new() { Exact = true }).FillAsync("ERP-SYNTHETIC");
    await aliasForm.GetByLabel("Alias code", new() { Exact = true }).FillAsync("BANK-OPERATING");
    await aliasForm.GetByLabel("Alias name", new() { Exact = true }).FillAsync("Operating bank account");
    var aliasReview = aliasForm.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this source account alias within this draft chart.", Exact = true });
    await aliasReview.CheckAsync();
    await Assertions.Expect(chartPanel.GetByText("Source account aliases are unavailable. Retry before continuing.",
      new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(aliasForm.GetByRole(AriaRole.Button, new() { Name = "Add reviewed alias", Exact = true }))
      .ToBeDisabledAsync();
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "Retry aliases", Exact = true }).ClickAsync();
    await Assertions.Expect(chartPanel.GetByText("No source account aliases configured for this revision.",
      new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(aliasForm.GetByRole(AriaRole.Button,
      new() { Name = "Add reviewed alias", Exact = true })).ToBeEnabledAsync();
    await aliasReview.CheckAsync();
    await aliasForm.GetByLabel("Alias name", new() { Exact = true }).FillAsync("Operating bank account - reviewed edit");
    await Assertions.Expect(aliasForm.GetByRole(AriaRole.Button,
      new() { Name = "Add reviewed alias", Exact = true })).ToBeDisabledAsync();
    await aliasForm.GetByLabel("Alias name", new() { Exact = true }).FillAsync("Operating bank account");
    await aliasForm.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this source account alias within this draft chart.", Exact = true }).CheckAsync();
    await aliasForm.GetByRole(AriaRole.Button, new() { Name = "Add reviewed alias", Exact = true }).ClickAsync();
    await Assertions.Expect(aliasForm).ToHaveCountAsync(0);
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "View accounts · revision 1", Exact = true }).ClickAsync();
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Cell,
      new() { Name = "BANK-OPERATING", Exact = true })).ToBeVisibleAsync();

    await page.ReloadAsync();
    await accounting.GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    await Assertions.Expect(accounting.GetByRole(AriaRole.Cell,
      new() { Name = "Doha headquarters", Exact = true })).ToBeVisibleAsync();
    await accounting.GetByRole(AriaRole.Button, new() { Name = "Open chart of accounts", Exact = true }).ClickAsync();
    await chartPanel.GetByRole(AriaRole.Button, new() { Name = "View accounts · revision 1", Exact = true }).ClickAsync();
    await Assertions.Expect(chartPanel.GetByRole(AriaRole.Cell,
      new() { Name = "BANK-OPERATING", Exact = true })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      Assert.Contains(await db.ClientAccountingDimensionDefinitions.Where(x => x.ClientId == fixture.ClientId).ToListAsync(),
        x => x.DimensionType == "BRANCH" && x.Code == "HQ-DOHA" && x.Name == "Doha headquarters");
      Assert.Contains(await db.SourceAccountAliases.Where(x => x.ClientId == fixture.ClientId && x.ChartVersionId == chartId).ToListAsync(),
        x => x.ClientAccountId == accountId && x.AliasCode == "BANK-OPERATING" && x.SourceSystem == "ERP-SYNTHETIC");
    }
    Assert.Empty(errors);
  }
}
