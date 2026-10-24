using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditFieldwork")]
public sealed class AngularAuditFieldworkParityJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true",
    ["AngularUi__CanonicalRoutes"] = "true"
  };

  [Fact]
  [Trait("CaseId", "AS-BANK-INSPECTION-01")]
  public async Task BankInspectionShowsRetainedSourceEvidenceWithoutChangingReview()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-BANK-INSPECTION-01");
    var f = host.Fixture;
    var sourceHash = Hashing.Sha256Hex("synthetic retained bank GL fixture");
    var statementHash = Hashing.Sha256Hex("synthetic retained statement fixture");
    var batchId = Guid.NewGuid();
    var sourcePeriodId = Guid.NewGuid();
    var transactionId = Guid.NewGuid();
    Guid bankId;
    await using (var db = host.CreateDbContext())
    {
      // Stored synthetic GL fixture only: this test exercises inspection, not file ingestion
      // or a real professional source acceptance. All schedule/reconciliation commands are real.
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = sourcePeriodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "BANK-INSPECTION-FY26",
        StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), Basis = "IFRS", Currency = "QAR",
        CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = batchId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PeriodId = sourcePeriodId, SourceKind = "GL", ProfileVersion = "gl-v1", ParserVersion = "parser-v1",
        RawFileSha256Hex = sourceHash, NormalizedDatasetDigest = sourceHash, LegalEntityKey = "bank-inspection-001", Currency = "QAR",
        RowCount = 1, ExpectedChunkCount = 1, ExpectedTransactionCount = 1, ExpectedLineCount = 1,
        AcceptedChunkCount = 1, AcceptedTransactionCount = 1, AcceptedLineCount = 1, Status = "SEALED",
        ReceiptReference = "BANK-INSPECTION-GL", CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
      {
        Id = transactionId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ImportBatchId = batchId, StableJournalId = "BANK-INSPECTION-J1", DocumentNumber = "BANK-INSPECTION-J1",
        PostingDate = new(2026, 9, 30), SourceUser = "synthetic", SourceSystem = "LEDGER", Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
      });
      db.GeneralLedgerLines.Add(new GeneralLedgerLine
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ImportBatchId = batchId, TransactionId = transactionId, StableLineId = "BANK-INSPECTION-L1", AccountCode = "1100",
        Debit = 100.123456m, Credit = 0m, OriginalCurrency = "QAR", OriginalAmount = 100.123456m,
        FunctionalAmount = 100.123456m, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var preparer = PbcSeed.Actor(f.Staff, "Staff");
      var reviewer = PbcSeed.Actor(f.Reviewer, "Reviewer");
      var ledger = await AuditFieldworkService.CreateScheduleAsync(db, preparer, new CreateScheduleRequest(
        f.EngagementId, "BANK_LEDGER", "bank-inspection-001", "BANK-INSPECTION-GL", new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30), "QAR", "debits positive; credits negative", sourceHash, 100.123456m,
        [new("bank-ledger-row", 1, "1100", "Synthetic retained ledger", 100.123456m, "QAR", null, new DateOnly(2026, 9, 30), null, null, "{}")], batchId));
      var statement = await AuditFieldworkService.CreateScheduleAsync(db, preparer, new CreateScheduleRequest(
        f.EngagementId, "BANK_STATEMENT", "bank-inspection-001", "BANK-INSPECTION-STATEMENT", new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30), "QAR", "debits positive; credits negative", statementHash, 90.123456m,
        [new("bank-statement-row", 1, "1100", "Synthetic retained statement", 90.123456m, "QAR", null, new DateOnly(2026, 9, 30), null, null, "{}")]));
      Assert.True(ledger.Succeeded, ledger.Message);
      Assert.True(statement.Succeeded, statement.Message);
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer, new(ledger.Value!.ScheduleId, "Synthetic independent source review.", true))).Succeeded);
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer, new(statement.Value!.ScheduleId, "Synthetic independent statement review.", true))).Succeeded);
      var bank = await AuditFieldworkService.CreateBankReconciliationAsync(db, preparer, new(
        f.EngagementId, null, ledger.Value.ScheduleId, statement.Value.ScheduleId, new(2026, 9, 30),
        [new("ledger", "LEDGER", 100.123456m, "Ledger", "BANK-INSPECTION-GL", "ledger-evidence", ledger.Value.ScheduleId),
         new("statement", "STATEMENT", 90.123456m, "Statement", "BANK-INSPECTION-STATEMENT", "statement-evidence", statement.Value.ScheduleId),
         new("timing", "TIMING", -10m, "Outstanding synthetic cheque", "BANK-INSPECTION-STATEMENT", "bank-timing-evidence", statement.Value.ScheduleId)]));
      Assert.True(bank.Succeeded, bank.Message);
      bankId = bank.Value!.BankReconciliationId;
    }
    var origin = await host.StartApiForIdentityAsync(f.Reviewer, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var inspectionPath = $"/app/engagements/{f.EngagementId:D}/audit-fieldwork";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(inspectionPath));
    var entry = page.GetByRole(AriaRole.Button, new() { Name = "Inspect bank reconciliations (1 needing approval)", Exact = true });
    await entry.FocusAsync(); await page.Keyboard.PressAsync("Enter");
    var region = page.GetByRole(AriaRole.Region, new() { Name = "Bank reconciliation evidence inspection", Exact = true });
    await Assertions.Expect(region.GetByRole(AriaRole.Heading, new() { Name = "Bank reconciliation evidence inspection", Exact = true })).ToBeFocusedAsync();
    await region.GetByRole(AriaRole.Button, new() { Name = "Inspect bank reconciliation " + bankId.ToString("D"), Exact = true }).PressAsync("Enter");
    await Assertions.Expect(region.GetByText(sourceHash, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(region.GetByText(statementHash, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(region.GetByText("bank-timing-evidence", new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(region.GetByText("100.123456", new() { Exact = true }).First).ToBeVisibleAsync();
    await region.GetByRole(AriaRole.Button, new() { Name = "Close bank inspection", Exact = true }).PressAsync("Enter");
    await Assertions.Expect(entry).ToBeFocusedAsync();
    await using (var db = host.CreateDbContext())
    {
      var retained = await db.AuditBankReconciliations.AsNoTracking().SingleAsync(x => x.Id == bankId);
      Assert.Equal(AuditBankReconciliationStatuses.Reconciled, retained.Status);
      Assert.Null(retained.ReviewedByUserId); Assert.Null(retained.ReviewedAt); Assert.Null(retained.Conclusion);
      Assert.Equal(3, await db.AuditBankReconciliationItems.CountAsync(x => x.BankReconciliationId == bankId));
      // A separate explicit synthetic reviewer action starts the source-linked phase;
      // inspection itself did not approve or modify the Bank record above.
      var review = await AuditFieldworkService.ReviewBankReconciliationAsync(db, PbcSeed.Actor(f.Reviewer, "Reviewer"),
        new(bankId, AuditBankReconciliationStatuses.Approved, "Synthetic independent Bank source review for confirmation."));
      Assert.True(review.Succeeded, review.Message);
    }
    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    await using var staffContext = await browser.NewContextAsync();
    var preparation = await staffContext.NewPageAsync();
    preparation.PageError += (_, error) => errors.Add(error);
    var confirmationPath = $"/app/engagements/{f.EngagementId:D}/confirmations";
    await preparation.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(confirmationPath));
    await preparation.GetByText("Prepare confirmation", new() { Exact = true }).ClickAsync();
    var createForm = preparation.Locator("details").Filter(new() { Has = preparation.GetByText("Prepare confirmation", new() { Exact = true }) });
    await createForm.GetByLabel("Confirmation area").SelectOptionAsync("CASH_BANK");
    await createForm.GetByRole(AriaRole.Button, new() { Name = "Load approved Bank source candidates", Exact = true }).ClickAsync();
    await createForm.GetByRole(AriaRole.Button, new() { Name = "Select this exact Bank source", Exact = true }).PressAsync("Enter");
    await Assertions.Expect(createForm.GetByLabel("Booked amount", new() { Exact = true })).ToHaveValueAsync("100.123456");
    await Assertions.Expect(createForm.GetByLabel("Booked amount", new() { Exact = true })).ToHaveAttributeAsync("readonly", "");
    await Assertions.Expect(createForm.GetByLabel("Currency", new() { Exact = true })).ToHaveValueAsync("QAR");
    await createForm.GetByLabel("Source record", new() { Exact = true }).FillAsync("bank-source-account-explicit");
    await createForm.GetByLabel("Confirmation date", new() { Exact = true }).FillAsync("2026-10-02");
    await createForm.GetByLabel("Respondent", new() { Exact = true }).FillAsync("Synthetic Bank respondent");
    await createForm.GetByLabel("Validated contact source", new() { Exact = true }).FillAsync("Explicit synthetic account/contact mapping.");
    var submit = createForm.GetByRole(AriaRole.Button, new() { Name = "Prepare reviewed confirmation", Exact = true });
    await Assertions.Expect(submit).ToBeDisabledAsync();
    await createForm.GetByRole(AriaRole.Checkbox).CheckAsync();
    await submit.ClickAsync();
    var retainedProof = preparation.GetByRole(AriaRole.Region, new() { Name = "Retained booked-source proof", Exact = true });
    // The new case is inspected through its register action; source creation is not a
    // professional approval or actual external dispatch.
    await preparation.GetByRole(AriaRole.Button, new() { Name = "Review confirmation for Synthetic Bank respondent", Exact = true }).ClickAsync();
    await Assertions.Expect(retainedProof.GetByText(bankId.ToString("D"), new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(retainedProof.GetByText(sourceHash, new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var created = await db.AuditConfirmationCases.AsNoTracking().SingleAsync(x => x.SourceRecordId == "bank-source-account-explicit");
      Assert.Equal(100.123456m, created.BookedAmount);
      Assert.Equal(new DateOnly(2026, 10, 2), created.ConfirmationDate);
      Assert.Equal(AuditConfirmationStatuses.Draft, created.Status);
      var source = await db.AuditConfirmationBookedSources.AsNoTracking().SingleAsync(x => x.ConfirmationCaseId == created.Id);
      Assert.Equal(bankId, source.BankReconciliationId);
      Assert.Equal(Hashing.Sha256Hex(source.SnapshotJson), source.SnapshotSha256);
      Assert.Equal(f.Staff.Id, source.CreatedByUserId);
    }
    await OwnedBrowserFixtureHandoff.WaitAsync(staffOrigin, confirmationPath, "typed-bank-confirmation");
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-009-ANGULAR-AGGREGATE-DIFFERENCES-01")]
  public async Task AggregateDifferenceConclusionRetainsHumanDecisionAndExactSourceSnapshot()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-009-ANGULAR-AGGREGATE-DIFFERENCES-01");
    var f = host.Fixture;
    const string conclusion = "Human assessment: evaluate unadjusted QAR amounts and qualitative factors; reporting impact remains subject to partner judgment.";
    const string differenceDescription = "Synthetic unadjusted cut-off difference";
    var differenceId = Guid.NewGuid();

    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId, f.EngagementId));
      var publication = await AuditProgramTestHelpers.PublishAsync(db, PbcSeed.Actor(f.Staff, "Partner"),
        new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(publication.Succeeded, publication.Message);
      await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId,
        PbcSeed.Actor(f.Staff, "Partner"), PbcSeed.Actor(f.Reviewer, "Partner"));
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = differenceId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        AccountArea = "Revenue", DifferenceType = "KNOWN", Description = differenceDescription,
        Amount = 12_000m, Currency = "QAR", MaterialityReference = "AFS-v1",
        QualitativeConcerns = "Synthetic only", Status = AuditDifferenceStatuses.Evaluated,
        CreatedByUserId = f.Staff.Id, EvaluatedByUserId = f.Reviewer.Id,
        Evaluation = "Synthetic individual evaluation", CreatedAt = DateTimeOffset.UtcNow,
        EvaluatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var path = $"/app/engagements/{f.EngagementId:D}/audit-fieldwork";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    var aggregate = page.GetByRole(AriaRole.Region,
      new() { Name = "Aggregate differences and reporting assessment", Exact = true });
    await Assertions.Expect(aggregate).ToContainTextAsync("12,000.00");
    await Assertions.Expect(aggregate).ToContainTextAsync("QAR");
    await aggregate.GetByLabel("Required: professional aggregate conclusion and reporting impact")
      .FillAsync(conclusion);
    await aggregate.GetByRole(AriaRole.Button, new() { Name = "Record aggregate conclusion", Exact = true }).ClickAsync();
    await Assertions.Expect(aggregate.GetByText(conclusion, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(aggregate.GetByText(AuditAreaAssessmentStatuses.Submitted, new() { Exact = true }))
      .ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var saved = await db.AuditAreaAssessments.AsNoTracking().SingleAsync(x =>
        x.FirmId == f.FirmId && x.ClientId == f.ClientId && x.EngagementId == f.EngagementId &&
        x.AreaCode == AuditAreaCodes.AuditDifferences &&
        x.AssessmentKind == AuditAreaAssessmentKinds.AggregateDifferences);
      Assert.Equal(conclusion, saved.Conclusion);
      Assert.Equal(AuditAreaAssessmentStatuses.Submitted, saved.Status);
      Assert.Equal("audit-differences-aggregate.v1", saved.MethodologyReference);
      var separator = saved.InputSnapshotJson.IndexOf(':');
      Assert.True(separator > 0);
      using var snapshot = JsonDocument.Parse(saved.InputSnapshotJson[(separator + 1)..]);
      Assert.Equal("audit-difference-aggregate.v1", snapshot.RootElement.GetProperty("Version").GetString());
      var sourceDifference = Assert.Single(snapshot.RootElement.GetProperty("Differences").EnumerateArray());
      Assert.Equal(differenceId, sourceDifference.GetProperty("Id").GetGuid());
      Assert.Equal(differenceDescription, sourceDifference.GetProperty("Description").GetString());
      Assert.Equal(12_000m, sourceDifference.GetProperty("Amount").GetDecimal());
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-04-ANGULAR")]
  public async Task AdoptedProgramReviewedFieldworkAndFrozenWorkpaperSurviveReload()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "PROP-E2E-04-ANGULAR");
    var f = host.Fixture;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
      var publication = await AuditProgramTestHelpers.PublishAsync(db, PbcSeed.Actor(f.Staff, "Partner"),
        new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(publication.Succeeded, publication.Message);
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var fieldworkPath = $"/app/engagements/{f.EngagementId:D}/audit-fieldwork";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(fieldworkPath));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Controlled audit fieldwork", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Adopt exact published version 2026.1", Exact = true }).ClickAsync();
    var program = page.GetByRole(AriaRole.Region, new() { Name = "Versioned audit program", Exact = true });
    await Assertions.Expect(program).ToContainTextAsync("AUDIT-WORKING-PROCESS v2026.1");
    await Assertions.Expect(program.GetByText("165", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Showing 9 of 165 authorized procedures", new() { Exact = true })).ToBeVisibleAsync();

    var section = page.Locator("select[name='section']");
    await section.SelectOptionAsync(new SelectOptionValue { Label = "All sections" });
    await Assertions.Expect(page.GetByText("Showing 165 of 165 authorized procedures", new() { Exact = true })).ToBeVisibleAsync();
    await section.SelectOptionAsync(new SelectOptionValue { Index = 1 });
    await Assertions.Expect(page.GetByText("Showing 9 of 165 authorized procedures", new() { Exact = true })).ToBeVisibleAsync();

    await page.GotoAsync(origin + "/app/audit/library");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Library versions", Exact = true })).ToBeVisibleAsync();
    var refreshLibrary = page.GetByRole(AriaRole.Button, new() { Name = "Refresh library", Exact = true });
    await page.GetByRole(AriaRole.Link, new() { Name = "Back to portfolio" }).FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(refreshLibrary).ToBeFocusedAsync();
    await Assertions.Expect(refreshLibrary).ToHaveCSSAsync("outline-style", "solid");

    await page.GotoAsync(origin + fieldworkPath);
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1", new() { Exact = true })).ToBeVisibleAsync();
    Guid procedureId;
    string sourceProcedureId;
    await using (var db = host.CreateDbContext())
    {
      var procedure = await db.AuditProcedures.AsNoTracking().Where(x => x.EngagementId == f.EngagementId)
        .OrderBy(x => x.SourceSectionNumber).ThenBy(x => x.SourceProcedureId).FirstAsync();
      procedureId = procedure.Id;
      sourceProcedureId = procedure.SourceProcedureId;
    }
    var procedureRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = sourceProcedureId });
    await procedureRow.GetByRole(AriaRole.Button, new() { Name = "Applicable", Exact = true }).ClickAsync();
    await Assertions.Expect(procedureRow).ToContainTextAsync("APPLICABLE");

    Guid scheduleId;
    Guid selectionId;
    Guid itemTestId;
    Guid findingId;
    Guid workpaperId;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Reviewer");
    const string workPerformed = "Agreed ROW-001 to the synthetic source schedule.";
    const string workpaperConclusion = "The selected item agrees; the separately recorded finding remains open.";
    await using (var db = host.CreateDbContext())
    {
      await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId, staff, PbcSeed.Actor(f.Reviewer, "Partner"));
      var sourceHash = Hashing.Sha256Hex("synthetic-audit-source-schedule");
      var schedule = await AuditFieldworkService.CreateScheduleAsync(db, staff, new CreateScheduleRequest(
        f.EngagementId, "SYNTHETIC_GL", "TEST-ENTITY", "E2E-SOURCE-001", null,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "DEBIT_MINUS_CREDIT", sourceHash, 125m,
        [new ScheduleRowInput("ROW-001", 1, "1000", "Synthetic cash item", 125m, "QAR",
          new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 30), null, null, "{}") ]));
      Assert.True(schedule.Succeeded, schedule.Message);
      scheduleId = schedule.Value!.ScheduleId;
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
        new ReviewScheduleRequest(scheduleId, "Complete synthetic source schedule", true))).Succeeded);

      var selection = await AuditFieldworkService.CreateSelectionAsync(db, staff, new CreateSelectionRequest(
        f.EngagementId, procedureId, scheduleId, null, "TARGETED", "Synthetic high-value selection",
        [new SelectionItemInput("ROW-001", 125m, "QAR", "Synthetic test item")]));
      Assert.True(selection.Succeeded, selection.Message);
      selectionId = selection.Value!.SelectionId;
      Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
        new ReviewSelectionRequest(selectionId, AuditSelectionStatuses.Reviewed, "Independent selection review."))).Succeeded);

      var selectedItemId = await db.AuditSelectionItems.AsNoTracking().Where(x => x.SelectionId == selectionId)
        .Select(x => x.Id).SingleAsync();
      var itemTest = await AuditFieldworkService.RecordItemTestAsync(db, staff, new RecordItemTestRequest(
        selectedItemId, "Agreed synthetic row to source evidence.", ["synthetic-evidence-001"],
        AuditItemTestResults.Pass, null, null, null));
      Assert.True(itemTest.Succeeded, itemTest.Message);
      itemTestId = itemTest.Value!.AuditItemTestId;
      Assert.True((await AuditFieldworkService.ReviewItemTestAsync(db, reviewer,
        new ReviewItemTestRequest(itemTestId, AuditItemTestReviewDecisions.Reviewed, "Independent result review."))).Succeeded);

      var finding = await AuditPlanningService.CreateFindingAsync(db, staff, new CreateFindingRequest(
        f.EngagementId, "Synthetic cut-off exception", "Synthetic test exception retained for review.",
        false, 125m, null));
      Assert.True(finding.Succeeded, finding.Message);
      findingId = finding.Value!.FindingId;
      var workpaper = await AuditPlanningService.CreateWorkpaperAsync(db, staff, new CreateWorkpaperRequest(
        f.EngagementId, "E2E-04", "Synthetic source test", "Test scoped source evidence",
        "E2E-TEMPLATE-v1", procedureId, "Inspect selected source row and resolve exception."));
      Assert.True(workpaper.Succeeded, workpaper.Message);
      workpaperId = workpaper.Value!.WorkpaperId;
      var draft = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(), workPerformed, workpaperConclusion));
      Assert.True(draft.Succeeded, draft.Message);
    }

    var workpaperPath = $"/app/audit/workpapers/{workpaperId:D}";
    await page.GotoAsync(origin + workpaperPath);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Workpaper: Synthetic source test", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByLabel("Work performed", new() { Exact = true })).ToHaveValueAsync(workPerformed);
    await Assertions.Expect(page.GetByLabel("Conclusion", new() { Exact = true })).ToHaveValueAsync(workpaperConclusion);

    await using (var verifyDraft = host.CreateDbContext())
    {
      var saved = await verifyDraft.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal(workPerformed, saved.WorkPerformed);
      Assert.Equal(workpaperConclusion, saved.Conclusion);
    }

    await page.EvaluateAsync("() => localStorage.clear()");
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByLabel("Work performed", new() { Exact = true })).ToHaveValueAsync(workPerformed);
    await Assertions.Expect(page.GetByLabel("Conclusion", new() { Exact = true })).ToHaveValueAsync(workpaperConclusion);
    await page.GetByRole(AriaRole.Button, new() { Name = "Submit for review", Exact = true }).ClickAsync();
    var workpaperReview = page.GetByRole(AriaRole.Dialog);
    await Assertions.Expect(workpaperReview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed workpaper action", Exact = true })).ToBeDisabledAsync();
    await workpaperReview.GetByLabel("I reviewed this exact workpaper, acknowledged draft and proposed effect.", new() { Exact = true }).CheckAsync();
    await workpaperReview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed workpaper action", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The submission was recorded.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("1 frozen submissions", new() { Exact = false })).ToBeVisibleAsync();

    await page.GotoAsync(origin + $"/app/findings/{findingId:D}");
    await Assertions.Expect(page.GetByText("Synthetic cut-off exception", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Synthetic test exception retained for review.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var verify = host.CreateDbContext())
    {
      Assert.Equal(AuditScheduleStatuses.Approved,
        await verify.AuditSchedules.Where(x => x.Id == scheduleId).Select(x => x.Status).SingleAsync());
      Assert.Equal(AuditSelectionStatuses.Reviewed,
        await verify.AuditSelections.Where(x => x.Id == selectionId).Select(x => x.Status).SingleAsync());
      Assert.Single(await verify.AuditItemTestReviews.Where(x => x.AuditItemTestId == itemTestId).ToListAsync());
      Assert.Single(await verify.WorkpaperSubmissions.Where(x => x.WorkpaperId == workpaperId).ToListAsync());
      Assert.Equal(AuditApplicabilityStatuses.Applicable,
        await verify.AuditProcedures.Where(x => x.Id == procedureId).Select(x => x.ApplicabilityStatus).SingleAsync());
    }
    await page.GotoAsync(origin + fieldworkPath);
    await page.GetByRole(AriaRole.Tab, new() { Name = "Sampling", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Combobox, new() { Name = "Procedure", Exact = true }).SelectOptionAsync(procedureId.ToString("D"));
    await page.GetByRole(AriaRole.Combobox, new() { Name = "Approved schedule", Exact = true }).SelectOptionAsync(scheduleId.ToString("D"));
    await page.GetByLabel("Interval", new() { Exact = true }).FillAsync("125");
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Synthetic accessibility preview only");
    var previewTrigger = page.GetByRole(AriaRole.Button, new() { Name = "Preview selection", Exact = true });
    await previewTrigger.PressAsync("Enter");
    var previewHeading = page.GetByRole(AriaRole.Heading, new() { Name = "Exact selection preview", Exact = true });
    await Assertions.Expect(previewHeading).ToBeFocusedAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Table, new() { Name = "Exact selected sampling rows", Exact = true })).ToContainTextAsync("ROW-001");
    foreach (var returnToOrigin in new[] { false, true })
    {
      var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      await page.RouteAsync("**/sampling/preview", async route =>
      {
        reached.TrySetResult();
        await release.Task;
        await route.ContinueAsync();
      });
      try
      {
        var rationale = page.GetByLabel("Rationale", new() { Exact = true });
        var delayedPreview = rationale.PressAsync("Enter");
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var portfolio = page.GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb", Exact = true })
          .GetByRole(AriaRole.Link, new() { Name = "Portfolio", Exact = true });
        if (returnToOrigin)
        {
          await page.Keyboard.PressAsync("Tab");
          await Assertions.Expect(rationale).Not.ToBeFocusedAsync();
          await page.Keyboard.PressAsync("Shift+Tab");
          await Assertions.Expect(rationale).ToBeFocusedAsync();
        }
        else await portfolio.FocusAsync();
        release.TrySetResult();
        await delayedPreview;
        await Assertions.Expect(previewTrigger).ToBeEnabledAsync();
        await Assertions.Expect(previewHeading).ToBeVisibleAsync();
        await Assertions.Expect(returnToOrigin ? rationale : portfolio).ToBeFocusedAsync();
      }
      finally
      {
        release.TrySetResult();
        await page.UnrouteAsync("**/sampling/preview");
      }
    }
    var stateReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var stateRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await page.RouteAsync("**/commands/state", async route =>
    {
      stateReached.TrySetResult();
      await stateRelease.Task;
      await route.ContinueAsync();
    });
    var procedurePanel = page.GetByRole(AriaRole.Region, new() { Name = "Procedure execution and review", Exact = true });
    try
    {
      await procedureRow.GetByRole(AriaRole.Button, new() { Name = "Open procedure", Exact = true }).PressAsync("Enter");
      await stateReached.Task.WaitAsync(TimeSpan.FromSeconds(20));
      await Assertions.Expect(procedurePanel.GetByRole(AriaRole.Status)).ToHaveTextAsync("Loading exact procedure state…");
      await Assertions.Expect(procedurePanel.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    }
    finally
    {
      stateRelease.TrySetResult();
      await page.UnrouteAsync("**/commands/state");
    }
    var procedureWork = procedurePanel.GetByLabel("Work performed", new() { Exact = true });
    await procedureWork.FillAsync("Synthetic accessibility preview only; no procedure result is recorded.");
    await procedurePanel.GetByLabel("Conclusion", new() { Exact = true }).FillAsync("Synthetic keyboard inspection.");
    await procedurePanel.GetByLabel("Manual reference", new() { Exact = true }).FillAsync("SYN-A11Y-PREVIEW");
    await procedurePanel.GetByLabel("What was observed", new() { Exact = true }).FillAsync("Disposable synthetic fixture only.");
    var procedurePreview = procedurePanel.GetByRole(AriaRole.Button, new() { Name = "Preview exact submission", Exact = true });
    await procedurePreview.PressAsync("Enter");
    await Assertions.Expect(procedurePanel.GetByRole(AriaRole.Heading, new() { Name = "Exact command preview", Exact = true })).ToBeFocusedAsync();
    await Assertions.Expect(procedurePanel.GetByRole(AriaRole.Button, new() { Name = "Record exact outcome", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(procedurePanel.GetByRole(AriaRole.Checkbox)).Not.ToBeCheckedAsync();

    var previewReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var previewRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await page.RouteAsync("**/commands/preview", async route =>
    {
      previewReached.TrySetResult();
      await previewRelease.Task;
      await route.ContinueAsync();
    });
    try
    {
      await procedureWork.FillAsync("Updated synthetic preview only.");
      await procedurePreview.PressAsync("Enter");
      await previewReached.Task.WaitAsync(TimeSpan.FromSeconds(20));
      var portfolio = page.GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb", Exact = true })
        .GetByRole(AriaRole.Link, new() { Name = "Portfolio", Exact = true });
      await portfolio.FocusAsync();
      previewRelease.TrySetResult();
      await Assertions.Expect(procedurePanel.GetByRole(AriaRole.Heading, new() { Name = "Exact command preview", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(portfolio).ToBeFocusedAsync();
    }
    finally
    {
      previewRelease.TrySetResult();
      await page.UnrouteAsync("**/commands/preview");
    }
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, fieldworkPath, "fieldwork-populated-accessibility");
    Assert.Empty(errors);
  }
}
