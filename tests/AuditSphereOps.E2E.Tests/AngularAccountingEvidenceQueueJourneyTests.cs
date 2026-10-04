using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAccountingEvidenceQueueJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-EVIDENCE-QUEUE-ACCESS")]
  public async Task ScopedEvidenceCountsRemainIsolatedAndDisappearAfterEpochLoss()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-EVIDENCE-QUEUE-ACCESS");
    var (f, _) = await AccountingEvidenceQueueSeed.SeedAsync(host.Database);
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string> {
      ["AngularUi__Enabled"] = "true", ["Application__FirmId"] = f.FirmId.ToString("D") });
    using var pw = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(pw);
    var page = await browser.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=/ui/app/accounting/evidence");
    await page.GetByText("SYNTHETIC-A", new() { Exact = true }).WaitForAsync();
    Assert.DoesNotContain("SYNTHETIC-B", await page.Locator("body").InnerTextAsync());
    await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("1 evidence records");
    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    await using (var db = host.CreateDbContext())
      await db.Users.Where(u => u.Id == f.Staff.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain("SYNTHETIC-A", await page.Locator("body").InnerTextAsync());
    Assert.Empty(errors);
  }
}
