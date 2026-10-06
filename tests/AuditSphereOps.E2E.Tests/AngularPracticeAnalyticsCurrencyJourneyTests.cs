using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularPracticeAnalyticsCurrencyJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-PRACTICE-ANALYTICS-CURRENCY-01")]
  public async Task CompatibleCurrencyShowsEconomicsAndMixedCurrencyFailsClosed()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-PRACTICE-ANALYTICS-CURRENCY-01");
    var f = host.Fixture;
    var administrator = PbcSeed.Actor(f.Admin, "Administrator");
    var partnerUser = PbcSeed.User(f.FirmId, "Staff");
    var partner = PbcSeed.Actor(partnerUser, "Partner");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    Guid qarRateCard;
    Guid taskId;
    Guid timeEntryId;

    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(partnerUser);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, partnerUser, "Partner"));
      await db.SaveChangesAsync();
      qarRateCard = (await PracticeTimeService.ReviseRateCardAsync(db, administrator,
        new("Staff", "AUDIT", "QAR", 600m))).Value;
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, partner, qarRateCard)).Succeeded);
      var budget = (await PracticeTimeService.ReviseBudgetAsync(db, administrator,
        new(f.EngagementId, "QAR", [new("Staff", "AUDIT", 60, BudgetPhases.Fieldwork)]))).Value;
      Assert.True((await PracticeTimeService.ApproveBudgetAsync(db, partner, budget)).Succeeded);
      taskId = (await PracticeTimeService.CreateTaskAsync(db, administrator,
        new("QAR analytics work", f.ClientId, f.EngagementId, f.Staff.Id))).Value;
      timeEntryId = (await PracticeTimeService.SaveTimeDraftAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
        new(taskId, today, 540, 60, "Staff", "AUDIT", Currency: "QAR"))).Value;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, PbcSeed.Actor(f.Staff, "Staff"), timeEntryId)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, administrator, timeEntryId)).Succeeded);
      Assert.True((await PracticeAnalyticsQuery.RecordCostRateAsync(db, partner, f.Staff.Id, 200m, "QAR", today.AddDays(-1))).Succeeded);
    }

    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(new()
    {
      ViewportSize = new() { Width = 1440, Height = 1000 }
    });
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/practice/analytics"));

    var table = page.GetByRole(AriaRole.Table, new() { Name = "Engagement economics", Exact = false });
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Practice analytics", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(table).ToContainTextAsync("QAR");
    await Assertions.Expect(table).ToContainTextAsync("600.00");
    await Assertions.Expect(table).ToContainTextAsync("200.00");
    await Assertions.Expect(table).ToContainTextAsync("-200.00");

    await using (var db = host.CreateDbContext())
    {
      var usdRateCard = (await PracticeTimeService.ReviseRateCardAsync(db, administrator,
        new("Staff", "AUDIT", "USD", 600m))).Value;
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, partner, usdRateCard)).Succeeded);
      var task = (await PracticeTimeService.CreateTaskAsync(db, administrator,
        new("USD analytics work", f.ClientId, f.EngagementId, f.Staff.Id))).Value;
      var entry = (await PracticeTimeService.SaveTimeDraftAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
        new(task, today, 600, 60, "Staff", "AUDIT", Currency: "USD"))).Value;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, PbcSeed.Actor(f.Staff, "Staff"), entry)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, administrator, entry)).Succeeded);
    }

    await page.ReloadAsync();
    await Assertions.Expect(table).ToContainTextAsync("Mixed — financial totals unavailable");
    var engagementRow = table.GetByRole(AriaRole.Row).Filter(new() { HasText = "PBC TEST CLIENT" });
    await Assertions.Expect(engagementRow).ToContainTextAsync("—");
    await Assertions.Expect(engagementRow).ToContainTextAsync("n/a");
    Assert.Empty(errors);
  }
}
