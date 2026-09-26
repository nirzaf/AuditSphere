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
  [InlineData("restatements", "Load closed-period packages", false)]
  [InlineData("rollforward", "Load closed periods", false)]
  [InlineData("restatements", "Load closed-period packages", true)]
  [InlineData("rollforward", "Load closed periods", true)]
  [Trait("CaseId", "AS-PAR-002-PERIOD-WORKBENCH-REVOKE-01")]
  public async Task SelectionReloadClearsAllProtectedContentWhenAccessChanges(
    string route, string loadButton, bool disableUser)
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
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
    page.PageError += (_, error) => errors.Add(error);
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    page.Console += (_, message) =>
    {
      if (message.Text.Contains("WebSocket connected to ws://", StringComparison.Ordinal))
        connected.TrySetResult();
    };
    await page.GotoAsync($"{host.StaffUrl}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/accounting/{route}")}");
    var prerenderedHeading = await page.QuerySelectorAsync("h1");
    await connected.Task.WaitAsync(TimeSpan.FromSeconds(15));
    if (prerenderedHeading is not null)
      await page.WaitForFunctionAsync("element => !element.isConnected", prerenderedHeading);
    var load = page.GetByRole(AriaRole.Button, new() { Name = loadButton, Exact = true });
    await Assertions.Expect(load).ToBeEnabledAsync();
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync(periodCode);
    await Assertions.Expect(page.Locator("body")).ToContainTextAsync("PBC TEST CLIENT");
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

    await load.ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(periodCode, body);
    Assert.DoesNotContain("PBC TEST CLIENT", body);
    Assert.DoesNotContain("Scoped period history", body);
    Assert.DoesNotContain("Restatement history", body);
    Assert.Equal(token, await page.EvaluateAsync<string>("window.__periodScopeToken"));
    Assert.Empty(errors);
    await using var verify = host.CreateDbContext();
    Assert.Equal(1, await verify.ClientReportingPeriods.CountAsync());
    Assert.Empty(await verify.ClientPeriodRestatements.ToListAsync());
  }
}
