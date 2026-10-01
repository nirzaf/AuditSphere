using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// A senior auditor uploads a two-period trial balance, opens the generated statements and drills from Revenue into
/// its procedure, runs MUS sampling with a logged, reproducible selection, registers a physical file and inserts an
/// ad hoc step — all in the real UI.
/// </summary>
[Trait("Category", "AuditFieldwork")]
public sealed class FieldworkConnectionsJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-STE-FIELDWORK-01")]
  public async Task SeniorUploadsDrillsSamplesIndexesAndInsertsAStep()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-STE-FIELDWORK-01");
    var f = host.Fixture;
    var senior = PbcSeed.User(f.FirmId, "Staff"); senior.DisplayName = "Sam Senior";
    var reviewer = PbcSeed.User(f.FirmId, "Staff");
    var now = DateTimeOffset.UtcNow;
    Guid revenueProcedure;
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(senior, reviewer);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, senior, "Senior", f.ClientId, f.EngagementId), PbcSeed.Grant(f.FirmId, senior, "AccountingPreparer", f.ClientId));
      await db.Engagements.Where(x => x.Id == f.EngagementId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ServiceRoute, "FinancialStatementAudit").SetProperty(x => x.PeriodEnd, "2026-12-31"));
      await db.SaveChangesAsync();
      var preparer = new ActorContext(senior.Id, f.FirmId, senior.SessionEpoch, ["AccountingPreparer"]);
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer, new ClientAccountingProfileRequest(f.ClientId, "QA", "QAR", 1, 1, "LEDGER", "L-1"))).Succeeded);
      var fy25 = (await ClientAccountingService.CreatePeriodAsync(db, preparer, new ReportingPeriodRequest(f.ClientId, "FY2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.CreatePeriodAsync(db, preparer, new ReportingPeriodRequest(f.ClientId, "FY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR", fy25))).Succeeded);

      var datasetId = Guid.NewGuid();
      var mappingId = Guid.NewGuid();
      var accounts = new (string Code, decimal Amount, string Dest, string Section, string? Area)[]
        { ("1000", 800m, "CASH", "ASSETS", "Cash"), ("3000", -300m, "EQUITY", "EQUITY", null), ("4000", -1500m, "REVENUE", "INCOME", "Revenue"), ("5000", 1000m, "EXPENSES", "EXPENSE", null) };
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset { Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, SourceKind = "Raw",
        Currency = "QAR", Balanced = true, ValidationStatus = "Accepted", NormalizedDatasetDigest = Hashing.Sha256Hex("journey"), ImportedAt = now, ImportedByUserId = senior.Id });
      db.TrialBalanceRows.AddRange(accounts.Select(a => new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = a.Code, AccountName = a.Dest, Amount = a.Amount, Currency = "QAR", Entity = "E" }));
      await db.SaveChangesAsync();
      await db.TrialBalanceDatasets.Where(x => x.Id == datasetId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
      db.MappingVersions.Add(new MappingVersion { Id = mappingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, DatasetId = datasetId, TaxonomyVersion = "tax-v1",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved, CreatedByUserId = senior.Id, ApprovedByUserId = reviewer.Id, ApprovedAt = now, CreatedAt = now });
      db.MappingAllocations.AddRange(accounts.Select(a => new MappingAllocation { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        MappingVersionId = mappingId, SourceAccountCode = a.Code, DestinationCode = a.Dest, StatementSection = a.Section, AuditArea = a.Area, Fraction = 1m, Rationale = "m", CreatedAt = now }));
      var versionId = Guid.NewGuid();
      db.AuditProgramVersions.Add(new AuditProgramVersion { Id = versionId, FirmId = f.FirmId, Version = "2026.1", SourceHash = Hashing.Sha256Hex("prog"),
        Status = AuditProgramStatuses.Published, CreatedByUserId = reviewer.Id, ApprovedByUserId = reviewer.Id, CreatedAt = now, ApprovedAt = now });
      await db.SaveChangesAsync();
      var programId = Guid.NewGuid();
      db.EngagementAuditPrograms.Add(new EngagementAuditProgram { Id = programId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ProgramVersionId = versionId, AdoptedByUserId = reviewer.Id, AdoptedAt = now });
      var procedure = new AuditProcedure { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, EngagementProgramId = programId,
        SourceProcedureId = "REV-01", SourceSectionTitle = "Revenue", SourceWording = "Test revenue cut-off", Title = "Test revenue cut-off",
        ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Planned, CreatedAt = now };
      db.AuditProcedures.Add(procedure);
      db.AuditProcedures.Add(new AuditProcedure { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, EngagementProgramId = programId, SourceProcedureId = "SMP-01", SourceSectionTitle = "Sampling",
        SourceWording = "Select systematic random transactions", Title = "Select systematic random transactions",
        ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Planned, CreatedAt = now });
      revenueProcedure = procedure.Id;
      var scheduleId = Guid.NewGuid();
      db.AuditSchedules.Add(new AuditSchedule { Id = scheduleId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ScheduleType = "SALES_LISTING",
        EntityIdentifier = "E", SourceReceiptReference = "PBC-1", Currency = "QAR", SignConvention = "DEBIT_POSITIVE", SourceHash = Hashing.Sha256Hex("s"), RowCount = 20,
        Status = AuditScheduleStatuses.Approved, CreatedByUserId = reviewer.Id, CreatedAt = now });
      db.AuditScheduleRows.AddRange(Enumerable.Range(1, 20).Select(i => new AuditScheduleRow { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ScheduleId = scheduleId, StableRowId = $"INV-{i:000}", SourceLineNumber = i, AccountCode = "4000", Description = $"Invoice {i}",
        SignedAmount = i * 100m, Currency = "QAR", CreatedAt = now }));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(senior);
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

    // Multi-period upload.
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/engagements/{f.EngagementId:D}/tb-intake")}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Multi-period upload" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    var csv = "PeriodCode,AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\nFY2025,1000,Cash,500,QAR,E1,\nFY2025,4000,Sales,-500,QAR,E1,\nFY2026,1000,Cash,700,QAR,E1,\nFY2026,4000,Sales,-700,QAR,E1,\n";
    await page.Locator("#mp-file").SetInputFilesAsync(new FilePayload { Name = "two-periods.csv", MimeType = "text/csv", Buffer = System.Text.Encoding.UTF8.GetBytes(csv) });
    var periods = page.Locator("[aria-label='Periods in the file']");
    await Assertions.Expect(periods).ToContainTextAsync("FY2025");
    await Assertions.Expect(periods).ToContainTextAsync("FY2026");
    await page.GetByRole(AriaRole.Button, new() { Name = "Import 2 period(s)" }).ClickAsync();
    await Assertions.Expect(page.GetByText("Imported FY2025, FY2026 as separate datasets", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 15000 });

    // Statements drill-down into the revenue procedure.
    await page.GotoAsync($"{origin}/app/engagements/{f.EngagementId:D}/statements");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Statement of profit or loss" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "REVENUE" }).ClickAsync();
    var link = page.GetByRole(AriaRole.Link, new() { Name = "REV-01 · Test revenue cut-off" });
    await Assertions.Expect(link).ToBeVisibleAsync();
    Assert.EndsWith($"#procedure-{revenueProcedure}", await link.GetAttributeAsync("href"));

    // Fieldwork tools.
    await page.GotoAsync($"{origin}/app/engagements/{f.EngagementId:D}/audit-fieldwork");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Fieldwork tools" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    await page.Locator("#smp-procedure").SelectOptionAsync(new SelectOptionValue { Label = "REV-01 · Test revenue cut-off" });
    await page.Locator("#smp-schedule").SelectOptionAsync(new SelectOptionValue { Label = "SALES_LISTING · 20 rows (QAR)" });
    await page.Locator("#smp-interval").FillAsync("5000");
    await page.Locator("#smp-rationale").FillAsync("MUS over the sales listing");
    await page.Locator("#smp-rationale").PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Run sampling" }).ClickAsync();
    await Assertions.Expect(page.Locator(".command-result").Last).ToContainTextAsync("of 20 items");
    await Assertions.Expect(page.Locator("[aria-label='Sampling calculation log']")).ToContainTextAsync("Matches");

    await page.Locator("#smp-method").SelectOptionAsync("SYSTEMATIC");
    await page.Locator("#smp-procedure").SelectOptionAsync(new SelectOptionValue { Label = "SMP-01 · Select systematic random transactions" });
    await page.Locator("#smp-size").FillAsync("5");
    await page.Locator("#smp-seed").FillAsync("42");
    await page.Locator("#smp-rationale").FillAsync("Systematic random five from the approved sales listing");
    await page.Locator("#smp-rationale").PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Run sampling" }).ClickAsync();
    await Assertions.Expect(page.Locator(".command-result").Last).ToContainTextAsync("5 of 20 items");
    await Assertions.Expect(page.Locator("[aria-label='Sampling calculation log']")).ToContainTextAsync("SYSTEMATIC");

    await page.GetByRole(AriaRole.Tab, new() { Name = "Physical files" }).ClickAsync();
    await page.Locator("#ph-index").FillAsync("X-1");
    await page.Locator("#ph-box").FillAsync("Box 3");
    await page.Locator("#ph-desc").FillAsync("Signed stock count sheets");
    await page.Locator("#ph-location").FillAsync("Client warehouse");
    await page.Locator("#ph-location").PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Register file" }).ClickAsync();
    await Assertions.Expect(page.Locator("[data-file-index='X-1']")).ToContainTextAsync("Box 3");

    await page.GetByRole(AriaRole.Tab, new() { Name = "Ad hoc steps" }).ClickAsync();
    await page.Locator("#ah-title").FillAsync("Inspect unusual credit note");
    await page.Locator("#ah-wording").FillAsync("Inspect the December credit note and its approval.");
    await page.Locator("#ah-reason").FillAsync("Unusual related-party credit note");
    await page.Locator("#ah-reason").PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Insert step" }).ClickAsync();
    await Assertions.Expect(page.Locator(".command-result").Last).ToContainTextAsync("Ad hoc step inserted");
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(2, await db.AuditSamplingRuns.CountAsync(x => x.EngagementId == f.EngagementId));
      Assert.True(await db.AuditProcedures.AnyAsync(x => x.EngagementId == f.EngagementId && x.SourceProcedureId == "ADHOC-001"));
      Assert.Equal(2, await db.TrialBalanceDatasets.CountAsync(x => x.EngagementId == f.EngagementId && x.PeriodId != null));
    }
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal) || x.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase));
  }
}
