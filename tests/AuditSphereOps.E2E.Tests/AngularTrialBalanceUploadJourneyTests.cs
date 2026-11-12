using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularTrialBalanceUploadJourneyTests
{
  private const string Csv = "PeriodCode,AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\nFY25,1000,Synthetic cash,100.123456,QAR,SYN-ENTITY,\nFY25,3000,Synthetic equity,-100.123456,QAR,SYN-ENTITY,\nFY26,1000,Synthetic cash,200.123456,QAR,SYN-ENTITY,\nFY26,3000,Synthetic equity,-200.123456,QAR,SYN-ENTITY,\n";
  [Fact]
  [Trait("CaseId", "ANGULAR-TB-UPLOAD")]
  public async Task NativeReviewedUpload_InvalidPeriod_CheckpointRecovery_WorkerValidation_AndRevocation()
  {
    const string multilineName = "Synthetic cash, restricted\nOperating \"reserve\"";
    const string quotedName = "\"Synthetic cash, restricted\nOperating \"\"reserve\"\"\"";
    var reviewedCsv = Csv.Replace("FY25,1000,Synthetic cash,", $"FY25,1000,{quotedName},", StringComparison.Ordinal);
    await using var host = await OwnedHost.StartAsync(caseId: "ANGULAR-TB-UPLOAD"); await Seed(host);
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var pw = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(pw); var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    var route = $"/ui/app/engagements/{host.Fixture.EngagementId}/tb-intake"; await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    var file = page.GetByLabel("Trial balance file", new() { Exact = true }); var table = page.GetByRole(AriaRole.Table, new() { Name = "Periods in the file" }); var import = page.GetByRole(AriaRole.Button, new() { Name = "Import reviewed periods" });
    await file.SetInputFilesAsync(File(reviewedCsv.Replace("-200.123456", "-200.123455"))); await Assertions.Expect(table).ToContainTextAsync("does not balance"); await Assertions.Expect(import).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save upload checkpoint in this tab" }).ClickAsync();
    await file.SetInputFilesAsync(File(reviewedCsv)); await Assertions.Expect(table).ToContainTextAsync("Ready");
    var review = page.GetByLabel("I reviewed this exact file, every period and the current reporting-period identities.", new() { Exact = true }); await review.CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save upload checkpoint in this tab" }).ClickAsync(); await page.ReloadAsync();
    await file.SetInputFilesAsync(File(reviewedCsv)); await page.GetByRole(AriaRole.Button, new() { Name = "Recover upload checkpoint", Exact = true }).ClickAsync(); await Assertions.Expect(review).Not.ToBeCheckedAsync(); await Assertions.Expect(import).ToBeDisabledAsync();
    await review.CheckAsync(); await import.ClickAsync(); await Assertions.Expect(page.GetByText("Every period has a persisted source receipt. Observe validation before mapping.", new() { Exact = true })).ToBeVisibleAsync();
    var validated = false;
    for (var attempt = 0; attempt < 60; attempt++) { await using var db = host.CreateDbContext(); validated = await db.TrialBalanceDatasets.CountAsync(x => x.ValidationStatus == "Accepted") == 2; if (validated) break; await Task.Delay(500); }
    Assert.True(validated, "Owned background worker must validate both source datasets.");
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh period receipts", Exact = true }).ClickAsync(); await Assertions.Expect(table).ToContainTextAsync(new System.Text.RegularExpressions.Regex("accepted", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    Guid sourceId;
    long sourceRevision;
    await using (var db = host.CreateDbContext())
    {
      var period = await db.ClientReportingPeriods.SingleAsync(x => x.ClientId == host.Fixture.ClientId && x.PeriodCode == "FY25");
      var source = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.PeriodId == period.Id);
      sourceId = source.Id;
      sourceRevision = source.Revision;
      var rows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == sourceId)
        .OrderBy(x => x.AccountCode).ToListAsync();
      Assert.Equal(
        new[] { ("1000", multilineName, 100.123456m), ("3000", "Synthetic equity", -100.123456m) },
        rows.Select(x => (x.AccountCode, x.AccountName, x.Amount)));
    }
    await page.GetByRole(AriaRole.Combobox, new() { Name = "Dataset", Exact = true }).SelectOptionAsync(sourceId.ToString("D"));
    var sourceRows = page.GetByRole(AriaRole.Table, new() { Name = "Trial balance source rows", Exact = true });
    var cashRow = sourceRows.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic cash, restricted" });
    await Assertions.Expect(cashRow).ToContainTextAsync("Operating \"reserve\"");
    await Assertions.Expect(cashRow).ToContainTextAsync("100.123456");
    var downloaded = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Export complete source CSV", Exact = true }).ClickAsync());
    Assert.Equal($"auditsphere-tb-{sourceId:D}-r{sourceRevision}.csv", downloaded.SuggestedFilename);
    var downloadPath = System.IO.Path.GetTempFileName(); try { await downloaded.SaveAsAsync(downloadPath); var bytes = await System.IO.File.ReadAllTextAsync(downloadPath); Assert.Contains("dataset_revision", bytes); Assert.Contains(sourceId.ToString("D"), bytes); Assert.Contains($"\"1000\",{quotedName},100.123456,", bytes); } finally { System.IO.File.Delete(downloadPath); }
    await using (var db = host.CreateDbContext()) { Assert.Equal(2, await db.TrialBalanceDatasets.CountAsync()); Assert.Equal(2, await db.DurableOperations.CountAsync(x => x.OperationKind == "ValidateTrialBalance.v1")); (await db.Users.SingleAsync(x => x.Id == host.Fixture.Staff.Id)).SessionEpoch++; await db.SaveChangesAsync(); }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 }); Assert.DoesNotContain("100.123456", await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
  }
  [Fact]
  [Trait("CaseId", "ANGULAR-TB-UNKNOWN")]
  public async Task AcceptedImportWithLostResponse_ReconcilesWithoutDuplicateOrAutomaticRetry()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-TB-UNKNOWN"); await Seed(host);
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" }); using var pw = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(pw); var page = await browser.NewPageAsync(); var writes = 0;
    await page.RouteAsync("**/tb-intake/import", async r => { writes++; var accepted = await r.FetchAsync(); Assert.Equal(200, accepted.Status); await r.AbortAsync("failed"); });
    var route = $"/ui/app/engagements/{host.Fixture.EngagementId}/tb-intake"; await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await page.GetByLabel("Trial balance file", new() { Exact = true }).SetInputFilesAsync(File(Csv)); await Assertions.Expect(page.GetByRole(AriaRole.Table, new() { Name = "Periods in the file" })).ToContainTextAsync("Ready");
    await page.GetByLabel("I reviewed this exact file, every period and the current reporting-period identities.", new() { Exact = true }).CheckAsync(); await page.GetByRole(AriaRole.Button, new() { Name = "Import reviewed periods" }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("No automatic retry");
    await page.GetByRole(AriaRole.Button, new() { Name = "Read persisted upload receipts", Exact = true }).ClickAsync(); await page.GetByRole(AriaRole.Button, new() { Name = "I reviewed the persisted receipts", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Import reviewed periods" })).ToHaveCountAsync(0); Assert.Equal(1, writes); await using var db = host.CreateDbContext(); Assert.Equal(2, await db.TrialBalanceDatasets.CountAsync());
  }
  private static FilePayload File(string csv) => new() { Name = "synthetic-tb.csv", MimeType = "text/csv", Buffer = Encoding.UTF8.GetBytes(csv) };
  private static async Task Seed(OwnedHost host) { await using var db = host.CreateDbContext(); var f = host.Fixture; foreach (var (code, year) in new[] { ("FY25", 2025), ("FY26", 2026) }) db.ClientReportingPeriods.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = code, StartDate = new(year, 1, 1), EndDate = new(year, 12, 31), Currency = "QAR", Basis = "IFRS", Status = "ACTIVE", CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
}
