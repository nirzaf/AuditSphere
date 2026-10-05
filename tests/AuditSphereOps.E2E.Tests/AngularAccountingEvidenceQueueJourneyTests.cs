using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAccountingEvidenceQueueJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-ACCT-EVIDENCE-01")]
  public async Task PartnerEvidenceQueueShowsOnlyTheAssignedClient()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-ACCT-EVIDENCE-01");
    var (f, sibling) = await AccountingEvidenceQueueSeed.SeedAsync(host.Database);
    var siblingPartner = PbcSeed.User(f.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(siblingPartner);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, siblingPartner, "Partner", clientId: sibling.Client));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(siblingPartner,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Faccounting%2Fevidence");
    await Assertions.Expect(page.GetByText("SYNTHETIC-B", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("1 evidence records");
    var body = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain("SYNTHETIC-A", body, StringComparison.Ordinal);
    Assert.Empty(errors);
  }

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
