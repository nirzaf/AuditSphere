using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
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
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-STE-FIELDWORK-01");
    var f = host.Fixture;
    var senior = PbcSeed.User(f.FirmId, "Staff"); senior.DisplayName = "Sam Senior";
    var reviewer = PbcSeed.User(f.FirmId, "Staff");
    var now = DateTimeOffset.UtcNow;
    Guid revenueProcedure;
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(senior, reviewer);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, senior, "Senior", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, senior, "Staff", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, senior, "AccountingPreparer", f.ClientId));
      await db.Engagements.Where(x => x.Id == f.EngagementId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ServiceRoute, "FinancialStatementAudit").SetProperty(x => x.PeriodEnd, "2026-12-31"));
      await db.SaveChangesAsync();
      var preparer = new ActorContext(senior.Id, f.FirmId, senior.SessionEpoch, ["AccountingPreparer"]);
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer, new ClientAccountingProfileRequest(f.ClientId, "QA", "QAR", 1, 1, "LEDGER", "L-1"))).Succeeded);
      var fy25 = (await ClientAccountingService.CreatePeriodAsync(db, preparer, new ReportingPeriodRequest(f.ClientId, "FY2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.CreatePeriodAsync(db, preparer, new ReportingPeriodRequest(f.ClientId, "FY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR", fy25))).Succeeded);

      var datasetId = Guid.NewGuid();
      var mappingId = Guid.NewGuid();
      var taxonomyId = Guid.NewGuid();
      var accounts = new (string Code, decimal Amount, string Dest, string Section, string? Area)[]
        { ("1000", 800m, "CASH", "ASSETS", "Cash"), ("3000", -300m, "EQUITY", "EQUITY", null), ("4000", -1500m, "REVENUE", "INCOME", "Revenue"), ("5000", 1000m, "EXPENSES", "EXPENSE", null) };
      db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
      {
        Id = taxonomyId, FirmId = f.FirmId, Code = "tax-v1", Framework = "IFRS", Name = "Synthetic statement taxonomy",
        Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1), CreatedByUserId = reviewer.Id,
        ApprovedByUserId = reviewer.Id, ApprovedAt = now, CreatedAt = now
      });
      db.ReportingTaxonomyNodes.AddRange(accounts.Select(a => new ReportingTaxonomyNode
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, TaxonomyVersionId = taxonomyId, Code = a.Dest, Name = a.Dest,
        StatementSection = a.Section, DisplaySign = "SIGNED", NormalBalance = "DEBIT", IsPosting = true,
        Applicability = "ALL", CreatedAt = now
      }));
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

    var uploadBytes = Encoding.UTF8.GetBytes("Synthetic client sales listing evidence for REV-01.");
    var stagedUpload = await PbcSeed.StageUploadAsync(host.Database, f,
      PbcSeed.Actor(f.Client, "ClientUser"), host.RequestId, uploadBytes, "client-sales-listing.txt", "text/plain");
    var origin = await host.StartApiForIdentityAsync(senior,
      new Dictionary<string, string> { ["Storage__PbcStagingRoot"] = stagedUpload.StagingRoot });
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

    var pbcPath = $"/app/engagements/{f.EngagementId:D}/pbc";
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(pbcPath)}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Prepared-by-client requests", Exact = true })).ToBeVisibleAsync();
    var stagedRequest = page.Locator("section.panel").Filter(new() { HasText = "Bank statements" });
    await stagedRequest.GetByRole(AriaRole.Button,
      new() { Name = "Complete staged transfer", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText(
      "Staged bytes verified; a durable provider transfer is queued. The request is not received until that transfer completes.",
      new() { Exact = true })).ToBeVisibleAsync();
    try
    {
      await host.StartGeneralWorkerAsync();
      await host.WaitForReceivedAsync(stagedUpload.UploadIntentId);
    }
    finally
    {
      PbcSeed.DeleteDirectory(stagedUpload.StagingRoot);
    }

    // Multi-period upload.
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/engagements/{f.EngagementId:D}/tb-intake")}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Multi-period upload" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    var csv = "PeriodCode,AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\nFY2025,1000,Cash,500,QAR,E1,\nFY2025,4000,Sales,-500,QAR,E1,\nFY2026,1000,Cash,700,QAR,E1,\nFY2026,4000,Sales,-700,QAR,E1,\n";
    await page.GetByLabel("Trial balance file").SetInputFilesAsync(new FilePayload { Name = "two-periods.csv", MimeType = "text/csv", Buffer = System.Text.Encoding.UTF8.GetBytes(csv) });
    var periods = page.Locator("[aria-label='Periods in the file']");
    await Assertions.Expect(periods).ToContainTextAsync("FY2025");
    await Assertions.Expect(periods).ToContainTextAsync("FY2026");
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this exact file, every period and the current reporting-period identities.", Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Import reviewed periods" }).ClickAsync();
    await Assertions.Expect(page.GetByText("Each period has a persisted source receipt. Background validation is separate.", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });

    // Statements drill-down into the revenue procedure.
    await page.GotoAsync($"{origin}/app/engagements/{f.EngagementId:D}/statements");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Financial statements", Exact = true }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Profit or loss", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Inspect line REVENUE INCOME", Exact = true }).ClickAsync();
    var procedureControl = page.GetByRole(AriaRole.Button, new() { Name = "Inspect procedure REV-01", Exact = true });
    await procedureControl.WaitForAsync(new() { Timeout = 15000 });
    await procedureControl.ClickAsync();
    var link = page.GetByRole(AriaRole.Link, new() { Name = "Open controlled fieldwork", Exact = true });
    await Assertions.Expect(link).ToBeVisibleAsync();
    Assert.EndsWith($"/app/engagements/{f.EngagementId:D}/audit-fieldwork#procedure-{revenueProcedure}", await link.GetAttributeAsync("href"));

    // Fieldwork tools.
    await page.GotoAsync($"{origin}/app/engagements/{f.EngagementId:D}/audit-fieldwork");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Fieldwork tools" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    await page.Locator("select[name='sp']").SelectOptionAsync(new SelectOptionValue { Label = "REV-01 · Test revenue cut-off" });
    await page.Locator("select[name='ss']").SelectOptionAsync(new SelectOptionValue { Label = "SALES_LISTING · 20 rows (QAR)" });
    await page.GetByLabel("Interval").FillAsync("5000");
    var samplingRationale = page.Locator("input[name='sr']");
    await samplingRationale.FillAsync("MUS over the sales listing");
    await samplingRationale.PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview selection", Exact = true }).ClickAsync();
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact selection preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync("of 20 rows selected");
    await preview.GetByRole(AriaRole.Button, new() { Name = "Record this exact sample", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator(".command-result").Last).ToContainTextAsync("of 20 selected rows");
    await Assertions.Expect(page.Locator("[aria-label='Sampling calculation log']")).ToContainTextAsync("Matches");

    await page.Locator("select[name='sm']").SelectOptionAsync("SYSTEMATIC");
    await page.Locator("select[name='sp']").SelectOptionAsync(new SelectOptionValue { Label = "SMP-01 · Select systematic random transactions" });
    await page.GetByLabel("Sample size").FillAsync("5");
    await page.GetByLabel("Seed", new() { Exact = true }).FillAsync("42");
    await samplingRationale.FillAsync("Systematic random five from the approved sales listing");
    await samplingRationale.PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview selection", Exact = true }).ClickAsync();
    var systematicPreview = page.GetByRole(AriaRole.Region, new() { Name = "Exact selection preview", Exact = true });
    await systematicPreview.GetByRole(AriaRole.Button, new() { Name = "Record this exact sample", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator(".command-result").Last).ToContainTextAsync("5 of 20 selected rows");
    await Assertions.Expect(page.Locator("[aria-label='Sampling calculation log']")).ToContainTextAsync("Systematic random");

    await page.GetByRole(AriaRole.Tab, new() { Name = "Client evidence" }).ClickAsync();
    await page.Locator("select[name='ep']").SelectOptionAsync(revenueProcedure.ToString());
    await page.Locator("select[name='eu']").SelectOptionAsync(stagedUpload.UploadIntentId.ToString());
    await page.Locator("input[name='en']").FillAsync("Evidence supporting REV-01");
    await page.GetByRole(AriaRole.Button, new() { Name = "Link evidence", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Evidence linked.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("[aria-label='Linked client evidence']"))
      .ToContainTextAsync("client-sales-listing.txt");

    await page.GetByRole(AriaRole.Tab, new() { Name = "Physical files" }).ClickAsync();
    await page.GetByLabel("File index").FillAsync("X-1");
    await page.GetByLabel("Box").FillAsync("Box 3");
    await page.GetByLabel("Description").FillAsync("Signed stock count sheets");
    var physicalLocation = page.GetByLabel("Location");
    await physicalLocation.FillAsync("Client warehouse");
    await physicalLocation.PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Register file" }).ClickAsync();
    await Assertions.Expect(page.Locator(".command-result").Last).ToContainTextAsync("Registered X-1.");
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Fieldwork tools" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    await page.GetByRole(AriaRole.Tab, new() { Name = "Physical files" }).ClickAsync();
    var physicalFile = page.Locator("[data-file-index='X-1']");
    await Assertions.Expect(physicalFile).ToContainTextAsync("Box 3");
    await physicalFile.GetByLabel("Procedure for X-1", new() { Exact = true }).SelectOptionAsync(revenueProcedure.ToString());
    await physicalFile.GetByRole(AriaRole.Button, new() { Name = "Link X-1", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Physical file linked.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(physicalFile).ToContainTextAsync("Test revenue cut-off");
    await physicalFile.GetByLabel("New location for X-1", new() { Exact = true }).FillAsync("Secure audit archive");
    var movePhysicalFile = physicalFile.GetByRole(AriaRole.Button, new() { Name = "Move X-1", Exact = true });
    await Assertions.Expect(movePhysicalFile).ToBeEnabledAsync();
    await movePhysicalFile.ClickAsync();
    await Assertions.Expect(page.GetByText("Movement recorded.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(physicalFile).ToContainTextAsync("Secure audit archive");

    await page.GetByRole(AriaRole.Tab, new() { Name = "Ad hoc steps" }).ClickAsync();
    await page.GetByLabel("Title").FillAsync("Inspect unusual credit note");
    var adHocPanel = page.GetByRole(AriaRole.Tabpanel, new() { Name = "Ad hoc steps" });
    await adHocPanel.GetByRole(AriaRole.Textbox, new() { Name = "Step", Exact = true }).FillAsync("Inspect the December credit note and its approval.");
    var adhocReason = adHocPanel.GetByRole(AriaRole.Textbox, new() { Name = "Why it is needed", Exact = true });
    await adhocReason.FillAsync("Unusual related-party credit note");
    await adhocReason.PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Insert step" }).ClickAsync();
    await Assertions.Expect(page.Locator(".command-result").Last).ToContainTextAsync("Ad hoc step inserted");

    Guid resultId;
    await using (var db = host.CreateDbContext())
    {
      var planningPartner = PbcSeed.User(f.FirmId, "Staff"); planningPartner.DisplayName = "Planning Partner";
      db.Users.Add(planningPartner);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, planningPartner, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
      await PlanningBasisSeed.ApproveMaterialityAsync(db, f.FirmId, f.ClientId, f.EngagementId,
        PbcSeed.Actor(senior, "Senior"), PbcSeed.Actor(planningPartner, "Partner"));
      var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.Id == f.ClientId)
        .Select(x => x.InputGeneration).SingleAsync();
      var submitted = await AuditProgramService.SubmitResultAsync(db, PbcSeed.Actor(senior, "Senior"),
        new SubmitProcedureResultRequest(revenueProcedure, Math.Max(1, generation),
          "Agreed invoice-001 to the reconciled source schedule and inspected the customer record.",
          "{}", ["SYNTHETIC-CLIENT-SALES-LISTING"], "Agreed with no exception."));
      Assert.True(submitted.Succeeded, submitted.Message);
      resultId = submitted.Value!.AuditProcedureResultId;

      var reviewerActor = PbcSeed.Actor(f.Reviewer, "Reviewer");
      var blockedSampling = await AuditFieldworkService.RunSamplingAsync(db, reviewerActor,
        new RunSamplingRequest(f.EngagementId, revenueProcedure, Guid.NewGuid(), AuditSamplingMethods.MonetaryUnit,
          100m, null, null, null, "Reviewer role must not gain planning access."));
      Assert.False(blockedSampling.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, blockedSampling.ErrorCode);
    }

    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    var reviewerPage = await (await browser.NewContextAsync()).NewPageAsync();
    var fieldworkPath = $"/app/engagements/{f.EngagementId:D}/audit-fieldwork";
    await reviewerPage.GotoAsync($"{reviewerOrigin}/auth/sign-in?returnUrl={Uri.EscapeDataString(fieldworkPath)}");
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading, new() { Name = "Procedure review", Exact = true })).ToBeVisibleAsync();
    await reviewerPage.GetByRole(AriaRole.Tab, new() { Name = "Review notes" }).ClickAsync();
    foreach (var hiddenTab in new[] { "Client evidence", "Physical files", "Ad hoc steps" })
      await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Tab, new() { Name = hiddenTab, Exact = true })).ToHaveCountAsync(0);
    await reviewerPage.GetByRole(AriaRole.Tab, new() { Name = "Sampling", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Preview selection", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Link, new() { Name = "Confirmation dashboard", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading, new() { Name = "Versioned audit program", Exact = true })).ToHaveCountAsync(0);
    var reviewerProjection = await reviewerPage.EvaluateAsync<string>("""
      async path => {
        const response = await fetch(path, { credentials: 'same-origin' });
        return `${response.status}|${await response.text()}`;
      }
      """, $"/api/ui/engagements/{f.EngagementId:D}/fieldwork");
    Assert.StartsWith("200|", reviewerProjection, StringComparison.Ordinal);
    Assert.Contains("\"canManageFieldwork\":false", reviewerProjection, StringComparison.Ordinal);
    Assert.Contains("\"canViewReviewNotes\":true", reviewerProjection, StringComparison.Ordinal);
    Assert.Contains("\"schedules\":[]", reviewerProjection, StringComparison.Ordinal);
    Assert.Contains("\"evidenceCandidates\":[]", reviewerProjection, StringComparison.Ordinal);
    Assert.Contains("\"physicalItems\":[]", reviewerProjection, StringComparison.Ordinal);
    await reviewerPage.GetByRole(AriaRole.Tab, new() { Name = "Review notes", Exact = true }).ClickAsync();
    var reviewerNotes = reviewerPage.GetByRole(AriaRole.Tabpanel, new() { Name = "Review notes" });
    await reviewerNotes.GetByRole(AriaRole.Combobox).SelectOptionAsync(revenueProcedure.ToString());
    var submittedResult = reviewerNotes.GetByLabel("Current submitted result", new() { Exact = true });
    await Assertions.Expect(submittedResult).ToContainTextAsync("Agreed invoice-001");
    await reviewerNotes.GetByLabel("Quoted text", new() { Exact = true }).FillAsync("reconciled source schedule");
    await reviewerNotes.GetByLabel("Note", new() { Exact = true }).FillAsync("Retain the matched invoice reference.");
    await reviewerNotes.GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("Note added.", new() { Exact = true })).ToBeVisibleAsync();
    var note = reviewerNotes.Locator("[data-note='reconciled source schedule']");
    await Assertions.Expect(note).ToContainTextAsync("Open");

    var preparerPage = await (await browser.NewContextAsync()).NewPageAsync();
    await preparerPage.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(fieldworkPath)}");
    await preparerPage.GetByRole(AriaRole.Tab, new() { Name = "Review notes" }).ClickAsync();
    var preparerNotes = preparerPage.GetByRole(AriaRole.Tabpanel, new() { Name = "Review notes" });
    await preparerNotes.GetByRole(AriaRole.Combobox).SelectOptionAsync(revenueProcedure.ToString());
    var preparerNote = preparerNotes.Locator("[data-note='reconciled source schedule']");
    await preparerNotes.GetByLabel("Reply to note on reconciled source schedule", new() { Exact = true })
      .FillAsync("The matched invoice is recorded in the source schedule.");
    await preparerNote.GetByRole(AriaRole.Button, new() { Name = "Respond", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText("Response added.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(preparerNote).ToContainTextAsync("response: The matched invoice is recorded");

    await reviewerPage.ReloadAsync();
    await reviewerPage.GetByRole(AriaRole.Tab, new() { Name = "Review notes" }).ClickAsync();
    reviewerNotes = reviewerPage.GetByRole(AriaRole.Tabpanel, new() { Name = "Review notes" });
    await reviewerNotes.GetByRole(AriaRole.Combobox).SelectOptionAsync(revenueProcedure.ToString());
    note = reviewerNotes.Locator("[data-note='reconciled source schedule']");
    await note.GetByRole(AriaRole.Button,
      new() { Name = "Resolve note on reconciled source schedule", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("Note resolved.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(note).ToContainTextAsync("Resolved");

    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(2, await db.AuditSamplingRuns.CountAsync(x => x.EngagementId == f.EngagementId));
      Assert.True(await db.AuditProcedures.AnyAsync(x => x.EngagementId == f.EngagementId && x.SourceProcedureId == "ADHOC-001"));
      Assert.Equal(2, await db.TrialBalanceDatasets.CountAsync(x => x.EngagementId == f.EngagementId && x.PeriodId != null));
      Assert.True(await db.ProcedureEvidenceLinks.AnyAsync(x => x.ProcedureId == revenueProcedure &&
        x.PbcUploadIntentId == stagedUpload.UploadIntentId && x.ContentSha256 == stagedUpload.DeclaredSha256Hex));
      var noteRecord = await db.ProcedureReviewNotes.AsNoTracking().SingleAsync(x => x.ResultId == resultId);
      Assert.Equal("reconciled source schedule", noteRecord.Excerpt);
      var noteEvents = await db.ProcedureReviewNoteEvents.AsNoTracking().Where(x => x.NoteId == noteRecord.Id)
        .OrderBy(x => x.CreatedAt).Select(x => x.Kind).ToListAsync();
      Assert.Equal(["RESPONSE", "RESOLVED"], noteEvents);
      var persistedPhysicalFile = await db.PhysicalEvidenceItems.AsNoTracking().SingleAsync(x =>
        x.EngagementId == f.EngagementId && x.FileIndex == "X-1");
      Assert.Equal("Secure audit archive", persistedPhysicalFile.CurrentLocation);

      var staleReviewer = new ActorContext(f.Reviewer.Id, f.FirmId, f.Reviewer.SessionEpoch + 1, ["Reviewer"]);
      var staleWorkspace = await AuditFieldworkWorkspaceQuery.GetAsync(db, staleReviewer, f.EngagementId);
      Assert.False(staleWorkspace.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleWorkspace.ErrorCode);
    }
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal) || x.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase));
  }
}
