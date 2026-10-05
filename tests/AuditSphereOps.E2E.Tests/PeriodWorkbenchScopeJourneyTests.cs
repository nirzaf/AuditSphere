using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AccountingAndReporting")]
public sealed class PeriodWorkbenchScopeJourneyTests
{
  [Theory]
  [InlineData("restatements", false)]
  [InlineData("rollforward", false)]
  [InlineData("restatements", true)]
  [InlineData("rollforward", true)]
  [Trait("CaseId", "AS-PAR-002-PERIOD-WORKBENCH-REVOKE-01")]
  public async Task OpenPeriodWorkbenchClearsScopedContentWhenAccessChanges(string route, bool disableUser)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: $"AS-PAR-002-{route.ToUpperInvariant()}-{(disableUser ? "DISABLED" : "REVOKED")}");
    Guid grantId;
    const string periodCode = "SYN-PRIVATE-CLOSED-PERIOD";
    await using (var db = host.CreateDbContext())
    {
      var grant = PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "AccountingPreparer",
        host.Fixture.ClientId);
      grantId = grant.Id;
      db.RoleGrants.Add(grant);
      // An engagement-only grant must not rescue a revoked client-level grant.
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "AccountingPreparer",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = periodCode, StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31),
        Basis = "IFRS", Currency = "QAR", Status = AccountingWorkflowStates.Closed,
        CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    var maintenanceResponses = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    page.Response += async (_, response) =>
    {
      if (!response.Url.Contains("/api/ui/accounting/period-maintenance", StringComparison.Ordinal)) return;
      try { maintenanceResponses.Add($"{response.Status}: {await response.TextAsync()}"); }
      catch (Exception error) { maintenanceResponses.Add($"{response.Status}: {error.GetType().Name}"); }
    };
    var heading = route == "restatements" ? "Period restatements" : "Period roll-forward";
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/ui/app/accounting/{route}")}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }))
      .ToBeVisibleAsync(new() { Timeout = 20000 });
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10000 });
    var clientPicker = page.Locator("select[name='client']");
    Assert.True(await clientPicker.CountAsync() == 1,
      $"{await page.Locator("body").InnerTextAsync()}\nMaintenance responses: {string.Join(" | ", maintenanceResponses)}");
    await clientPicker.SelectOptionAsync(host.Fixture.ClientId.ToString("D"));
    var periodPicker = route == "restatements" ? page.Locator("select[name='period']") : page.Locator("select[name='prior']");
    await Assertions.Expect(periodPicker).ToContainTextAsync(periodCode);
    await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("1 closed periods for client");
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("PBC TEST CLIENT");

    // The Angular forms and route layouts remain usable at narrow and wide viewports.
    if (!disableUser)
    {
      foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
      {
        await page.SetViewportSizeAsync(width, 900);
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
          $"{route} overflows the {width}px viewport.");
      }

      await clientPicker.FocusAsync();
      Assert.True(await clientPicker.EvaluateAsync<bool>("element => document.activeElement === element"));
    }

    var token = await page.EvaluateAsync<string>("window.__periodScopeToken = crypto.randomUUID()");
    await using (var db = host.CreateDbContext())
    {
      if (disableUser)
        await db.Users.Where(x => x.Id == host.Fixture.Staff.Id)
          .ExecuteUpdateAsync(x => x.SetProperty(u => u.Disabled, true));
      else
      {
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
          PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grantId));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
    }

    // Open Angular content is invalidated by the session epoch/disabled state without another user action.
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true }))
      .ToBeVisibleAsync(new() { Timeout = 15000 });
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(periodCode, body);
    Assert.DoesNotContain("PBC TEST CLIENT", body);
    Assert.DoesNotContain("Scoped period history", body);
    Assert.DoesNotContain("Restatement history", body);
    Assert.Equal(0, await page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).CountAsync());
    Assert.Equal(token, await page.EvaluateAsync<string>("window.__periodScopeToken"));
    Assert.Empty(errors);
    await using var verify = host.CreateDbContext();
    Assert.Equal(1, await verify.ClientReportingPeriods.CountAsync());
    Assert.Empty(await verify.ClientPeriodRestatements.ToListAsync());
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ROLLFORWARD-STALE-SOURCE-01")]
  public async Task StaleSourcePackageLeavesAuthorizedWorkbenchAvailable()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ROLLFORWARD-STALE-SOURCE-01");
    var (packageId, _) = await FinancialPackageFixture.CreatePackageAsync(host);
    Guid priorPeriodId;
    ClientReportingPeriod prior;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff,
        "AccountingPreparer", host.Fixture.ClientId));
      await db.ClientReportingPeriods.Where(x => x.ClientId == host.Fixture.ClientId)
        .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AccountingWorkflowStates.Closed));
      await db.SaveChangesAsync();
      prior = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.ClientId == host.Fixture.ClientId);
      priorPeriodId = prior.Id;
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    var maintenanceResponses = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    page.Response += async (_, response) =>
    {
      if (!response.Url.Contains("/api/ui/accounting/period-maintenance", StringComparison.Ordinal)) return;
      try { maintenanceResponses.Add($"{response.Status}: {await response.TextAsync()}"); }
      catch (Exception error) { maintenanceResponses.Add($"{response.Status}: {error.GetType().Name}"); }
    };
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/ui/app/accounting/rollforward")}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Period roll-forward", Exact = true }))
      .ToBeVisibleAsync(new() { Timeout = 20000 });
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10000 });
    var clientPicker = page.Locator("select[name='client']");
    Assert.True(await clientPicker.CountAsync() == 1,
      $"{await page.Locator("body").InnerTextAsync()}\nMaintenance responses: {string.Join(" | ", maintenanceResponses)}");
    await clientPicker.SelectOptionAsync(host.Fixture.ClientId.ToString("D"));
    var priorPicker = page.Locator("select[name='prior']");
    await Assertions.Expect(priorPicker).ToContainTextAsync(prior.PeriodCode);
    await priorPicker.SelectOptionAsync(priorPeriodId.ToString("D"));
    var packagePicker = page.Locator("select[name='package']");
    await Assertions.Expect(packagePicker).ToContainTextAsync(packageId.ToString("D"));
    await packagePicker.SelectOptionAsync(packageId.ToString("D"));

    var nextStart = prior.EndDate.AddDays(1);
    var nextEnd = new DateOnly(nextStart.Year, 12, 31);
    await page.GetByLabel("New period code", new() { Exact = true }).FillAsync("SYN-STALE-NEXT");
    await page.GetByLabel("Start date", new() { Exact = true }).FillAsync(nextStart.ToString("yyyy-MM-dd"));
    await page.GetByLabel("End date", new() { Exact = true }).FillAsync(nextEnd.ToString("yyyy-MM-dd"));
    await page.GetByLabel("Basis", new() { Exact = true }).FillAsync(prior.Basis);
    await page.GetByLabel("Currency", new() { Exact = true }).FillAsync(prior.Currency);
    await page.GetByLabel("Prior closing amount", new() { Exact = true }).FillAsync("100.00");
    await page.GetByLabel("Current opening amount", new() { Exact = true }).FillAsync("100.00");
    await page.GetByLabel("Source SHA-256", new() { Exact = true }).FillAsync(new string('0', 64));
    await page.GetByLabel("Evidence reference", new() { Exact = true }).FillAsync("synthetic-source-conflict");
    await page.GetByLabel("I reviewed the closed prior period, opening amounts and exact source evidence.", new() { Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Create draft period", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The request was not accepted.");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Period roll-forward", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToHaveCountAsync(0);
    await using var verify = host.CreateDbContext();
    Assert.Equal(1, await verify.ClientReportingPeriods.CountAsync());
    Assert.Empty(await verify.OpeningBalanceBridges.ToListAsync());
    Assert.Empty(errors);
  }
}
