using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.E2E.Tests;

internal static class FinancialPackageFixture
{
  internal sealed record PackageSeed(Guid PackageId, Dictionary<string, (byte[] Bytes, string Sha256)> Expected,
    Guid MappingId, Guid PlanId, Guid PeriodId, Guid SourceLineId)
  {
    public void Deconstruct(out Guid packageId, out Dictionary<string, (byte[] Bytes, string Sha256)> expected)
    { packageId = PackageId; expected = Expected; }
  }

  internal static async Task<PackageSeed> CreatePackageAsync(
    OwnedHost host, bool completeForReview = false)
  {
    var firmId = host.Fixture.FirmId;
    var clientId = host.Fixture.ClientId;
    var engagementId = host.Fixture.EngagementId;
    var preparer = PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer");
    var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer");
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var taxonomyId = Guid.NewGuid();
    var sourceBatchId = Guid.NewGuid();
    var sourceTransactionId = Guid.NewGuid();
    var sourceLineId = Guid.NewGuid();
    var investmentSourceId = Guid.NewGuid();
    var capitalSourceId = Guid.NewGuid();
    var sourceHash = sourceBatchId.ToString("N") + sourceBatchId.ToString("N");
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(firmId, host.Fixture.Staff, "AccountingPreparer", clientId, engagementId),
        PbcSeed.Grant(firmId, host.Fixture.Reviewer, "AccountingReviewer", clientId, engagementId));
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "FY2026-E2E",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingBooks.Add(new ClientReportingBook
      {
        Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId,
        Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
        Status = AccountingWorkflowStates.Active, CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
      {
        Id = taxonomyId, FirmId = firmId, Code = "tax-e2e-v1", Framework = "IFRS", Name = "Synthetic E2E taxonomy",
        Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
        CreatedByUserId = host.Fixture.Staff.Id, ApprovedByUserId = host.Fixture.Reviewer.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ReportingTaxonomyNodes.AddRange(
        new ReportingTaxonomyNode
        {
          Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "CASH", Name = "Cash",
          StatementSection = "ASSETS", DisplaySign = "SIGNED", NormalBalance = "DEBIT", IsPosting = true,
          Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
        },
        new ReportingTaxonomyNode
        {
          Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "REVENUE", Name = "Revenue",
          StatementSection = "INCOME", DisplaySign = "SIGNED", NormalBalance = "CREDIT", IsPosting = true,
          Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
        });
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY", SourceKind = "Raw", Revision = 1,
        Currency = "QAR", Balanced = true, ValidationStatus = "Accepted", ControlTotal = 0m,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = host.Fixture.Staff.Id
      });
      db.TrialBalanceRows.AddRange(
        new TrialBalanceRow
        {
          Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash",
          Amount = 180_000m, Currency = "QAR", Entity = "SYNTHETIC"
        },
        new TrialBalanceRow
        {
          Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000", AccountName = "Revenue",
          Amount = -180_000m, Currency = "QAR", Entity = "SYNTHETIC"
        });
      db.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = sourceBatchId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        PeriodId = periodId, BookId = bookId, SourceKind = AccountingSourceKinds.GeneralLedger,
        ProfileVersion = "cash-flow-e2e-v1", ParserVersion = "synthetic-e2e-v1", RawFileSha256Hex = sourceHash,
        NormalizedDatasetDigest = sourceHash, LegalEntityKey = "SYNTHETIC-CASH-FLOW", Currency = "QAR",
        RowCount = completeForReview ? 6 : 2, Status = "LOADING", ReceiptReference = "Synthetic accepted cash-flow ledger",
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
      {
        Id = sourceTransactionId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        ImportBatchId = sourceBatchId, StableJournalId = "SYN-CF-J-E2E", DocumentNumber = "SYN-CF-DOC-E2E",
        PostingDate = new DateOnly(2026, 6, 30), Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
      });
      // A balanced cash sale: the cash leg is debited, and the credited revenue line is the noncash counterpart a movement draws on.
      db.GeneralLedgerLines.Add(new GeneralLedgerLine
      {
        Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        ImportBatchId = sourceBatchId, TransactionId = sourceTransactionId, StableLineId = "SYN-CF-L-E2E-CASH",
        AccountCode = "1000", Debit = 175_000m, Credit = 0m, FunctionalAmount = 175_000m,
        OriginalCurrency = "QAR", OriginalAmount = 175_000m, CreatedAt = DateTimeOffset.UtcNow
      });
      db.GeneralLedgerLines.Add(new GeneralLedgerLine
      {
        Id = sourceLineId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        ImportBatchId = sourceBatchId, TransactionId = sourceTransactionId, StableLineId = "SYN-CF-L-E2E",
        // Ledger sign is debit-positive, so the credited revenue carries -175,000. The movement projects it as +175,000 of cash.
        AccountCode = "4000", Debit = 0m, Credit = 175_000m, FunctionalAmount = -175_000m,
        OriginalCurrency = "QAR", OriginalAmount = -175_000m, CreatedAt = DateTimeOffset.UtcNow
      });
      if (completeForReview)
      {
        // Balanced investment purchase and capital subscription offset in cash,
        // while preserving the 175,000 revenue/profit and closing cash fixture.
        foreach (var (id, account, amount, key) in new[] {
          (investmentSourceId, "1500", 100m, "INVESTMENT"),
          (capitalSourceId, "3000", -100m, "CAPITAL") })
        {
          var transactionId = Guid.NewGuid();
          db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction {
            Id = transactionId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
            ImportBatchId = sourceBatchId, StableJournalId = "SYN-" + key, DocumentNumber = "SYN-" + key,
            PostingDate = new DateOnly(2026, 6, 30), Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow });
          foreach (var (lineId, code, signed) in new[] { (id, account, amount), (Guid.NewGuid(), "1000", -amount) })
            db.GeneralLedgerLines.Add(new GeneralLedgerLine {
              Id = lineId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
              ImportBatchId = sourceBatchId, TransactionId = transactionId, StableLineId = "SYN-" + key + "-" + code,
              AccountCode = code, Debit = Math.Max(signed, 0m), Credit = Math.Max(-signed, 0m), FunctionalAmount = signed,
              OriginalCurrency = "QAR", OriginalAmount = signed, CreatedAt = DateTimeOffset.UtcNow });
        }
        foreach (var (account, code, section) in new[] { ("1500", "INVESTMENT", "ASSETS"), ("3000", "SHARE_CAPITAL", "EQUITY"), ("3900", "OCI_ZERO", "OCI") })
        {
          db.ReportingTaxonomyNodes.Add(new ReportingTaxonomyNode
          {
            Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = code, Name = "Synthetic review balance " + code,
            StatementSection = section, DisplaySign = "SIGNED", NormalBalance = "CREDIT", IsPosting = true,
            Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
          });
          db.TrialBalanceRows.Add(new TrialBalanceRow
          {
            Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = account, AccountName = "Synthetic review balance " + code,
            Amount = account == "1500" ? 100m : account == "3000" ? -100m : 0m, Currency = "QAR", Entity = "SYNTHETIC"
          });
        }
      }
      await db.SaveChangesAsync();
    }

    await using (var db = host.CreateDbContext())
    {
      await db.SourceImportBatches.Where(x => x.Id == sourceBatchId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SEALED"));
      var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer"),
        new(clientId, engagementId, AccountingSourceKinds.GeneralLedger, null, sourceBatchId,
          "Synthetic independent cash-flow source acceptance"));
      Assert.True(accepted.Succeeded, accepted.Message);
    }

    Guid mappingId;
    await using (var db = host.CreateDbContext())
    {
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(datasetId, "tax-e2e-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Synthetic cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Synthetic revenue mapping"),
          ..(completeForReview ? new MappingAllocationInput[] {
            new("1500", "INVESTMENT", "ASSETS", 1m, "Synthetic investment purchase"),
            new("3000", "SHARE_CAPITAL", "EQUITY", 1m, "Synthetic capital subscription"),
            new("3900", "OCI_ZERO", "OCI", 1m, "Synthetic zero OCI") } : [])
        ]));
      Assert.True(mapping.Succeeded, mapping.Message);
      mappingId = mapping.Value;
      var approved = await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1);
      Assert.True(approved.Succeeded, approved.Message);
    }

    Guid planId;
    await using (var db = host.CreateDbContext())
    {
      var journal = await AdjustmentJournalService.CreateDraftAsync(db, preparer, datasetId, "AJ-E2E-001",
        [("1000", 0m, 5_000m), ("4000", 5_000m, 0m)]);
      Assert.True(journal.Succeeded, journal.Message);
      var posted = await AdjustmentJournalService.PostAsync(db, reviewer, journal.Value!);
      Assert.True(posted.Succeeded, posted.Message);
      var reconciled = await SourceReconciliationService.ResolveAsync(db, reviewer, datasetId,
        "AJ-E2E-001", 1, ReflectionStates.NotReflected, "Synthetic source-ledger review: adjustment not posted in client books.");
      Assert.True(reconciled.Succeeded, reconciled.Message);
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, datasetId,
        [new PlanLineInput("AJ-E2E-001", 1)]);
      Assert.True(plan.Succeeded, plan.Message);
      planId = plan.Value;
      var finalized = await AdjustmentPlanService.FinalizeAsync(db, preparer, planId);
      Assert.True(finalized.Succeeded, finalized.Message);
    }

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "e2e-template-v1",
      new FinancialSupplementaryInformation(0m, 175_000m,
        [new CashFlowLineInput("OPERATING", "Synthetic cash collections", 175_000m, MovementDate: new DateOnly(2026, 6, 30),
          ActivityCode: "CASH_COLLECTIONS", SourceReferences: ["synthetic-ledger-e2e"], ClassificationRationale: "Synthetic collections agreed to source.",
          SourceAllocations: [new CashFlowSourceAllocationInput(sourceLineId, -175_000m)]),
          ..(completeForReview ? new CashFlowLineInput[] {
            new("INVESTING", "Synthetic investment purchase", -100m, MovementDate: new DateOnly(2026, 6, 30), ActivityCode: "INVESTMENT_PURCHASE", SourceReferences: ["SYN-INVESTMENT"], ClassificationRationale: "Synthetic investment purchase agreed to accepted ledger.", SourceAllocations: [new(investmentSourceId, 100m)]),
            new("FINANCING", "Synthetic capital subscription", 100m, MovementDate: new DateOnly(2026, 6, 30), ActivityCode: "CAPITAL_SUBSCRIPTION", SourceReferences: ["SYN-CAPITAL"], ClassificationRationale: "Synthetic capital subscription agreed to accepted ledger.", SourceAllocations: [new(capitalSourceId, -100m)]) } : [])],
        [new DisclosureInput("E2E_POLICY", "Synthetic test policy note."),
         new DisclosureInput("E2E_OTHER", string.Empty, NotApplicable: true, Rationale: "No other synthetic items.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 175_000m, 0m, 0m, 0m, 175_000m, "e2e-equity"),
          ..(completeForReview ? new EquityLineInput[] { new("SHARE_CAPITAL", "Share capital", 0m, 0m, 0m, 100m, 0m, 100m, "SYN-CAPITAL") } : [])],
        NoteLines: [new NoteLineInput("E2E_NOTE", "CASH", 175_000m, "e2e-note")],
        // Every fixture reviewed by this helper uses a QAR-only cash population, so record the assessed-zero state
        // explicitly instead of relying on a missing FX assessment to mean no effect.
        FxCashAssessment: new FxCashAssessmentInput(FxCashAssessmentStates.AssessedZero, "synthetic-functional-currency-cash-population",
          "Synthetic cash is QAR-only; no foreign-currency cash effect exists."),
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["synthetic-opening-cash-e2e"], ClosingCashEvidenceReferences: ["synthetic-closing-cash-e2e"]));
    Guid packageId;
    var expected = new Dictionary<string, (byte[] Bytes, string Sha256)>(StringComparer.Ordinal);
    await using (var db = host.CreateDbContext())
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
      packageId = built.Value.PackageId;
      var sourceReviewer = PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer");
      var reviewState = await FinancialStatementService.GetCashFlowSourceReviewAsync(db, sourceReviewer, packageId);
      Assert.True(reviewState.Succeeded, reviewState.Message);
      var sourceReview = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, sourceReviewer, packageId, "APPROVED",
        "Synthetic independent cash-flow source review.", Guid.NewGuid(), reviewState.Value!.LinkDigest, reviewState.Value.LatestReviewId);
      Assert.True(sourceReview.Succeeded, sourceReview.Message);
      var canonical = await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId);
      Assert.True(canonical.Succeeded, canonical.Message);
      foreach (var version in new[]
      {
        FinancialPackageArtifactVersions.Workbook,
        FinancialPackageArtifactVersions.Word,
        FinancialPackageArtifactVersions.Pdf
      })
      {
        var rendered = await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, preparer, packageId, version);
        Assert.True(rendered.Succeeded, rendered.Message);
        expected.Add(version, (rendered.Value!.ArtifactBytes, rendered.Value.ArtifactSha256Hex));
      }
    }
    return new PackageSeed(packageId, expected, mappingId, planId, periodId, sourceLineId);
  }
  internal static async Task<FinancialStatementComposition> SelectReviewedCompositionAsync(OwnedHost host, Guid packageId)
  {
    await using var db = host.CreateDbContext();
    // Layout authoring is client-scoped; an engagement-only grant cannot authorize it.
    db.RoleGrants.AddRange(
      PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "AccountingPreparer", host.Fixture.ClientId),
      PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "AccountingReviewer", host.Fixture.ClientId));
    await db.SaveChangesAsync();
    var preparer = PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer");
    var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer");
    var layout = await StatementLayoutService.SaveLayoutDraftAsync(db, preparer,
      new StatementLayoutDraftRequest(host.Fixture.ClientId, "IFRS", "Synthetic artifact parity layout",
      [
        new StatementLineDefinition("H_FP", "Assets", StatementLayoutLineKinds.Heading, StatementLayoutSections.FinancialPosition, 1),
        new StatementLineDefinition("CASH", "Cash", StatementLayoutLineKinds.MappedTaxonomyBalance, StatementLayoutSections.FinancialPosition, 2, TaxonomyNodeCode: "CASH"),
        new StatementLineDefinition("INV", "Investments", StatementLayoutLineKinds.MappedTaxonomyBalance, StatementLayoutSections.FinancialPosition, 3, TaxonomyNodeCode: "INVESTMENT"),
        new StatementLineDefinition("SC", "Share capital", StatementLayoutLineKinds.MappedTaxonomyBalance, StatementLayoutSections.FinancialPosition, 4, DisplaySign: StatementLayoutDisplaySigns.Inverted, TaxonomyNodeCode: "SHARE_CAPITAL"),
        new StatementLineDefinition("H_PL", "Revenue", StatementLayoutLineKinds.Heading, StatementLayoutSections.ProfitOrLoss, 1),
        new StatementLineDefinition("REV", "Revenue", StatementLayoutLineKinds.MappedTaxonomyBalance, StatementLayoutSections.ProfitOrLoss, 2,
          DisplaySign: StatementLayoutDisplaySigns.Inverted, TaxonomyNodeCode: "REVENUE")
      ]));
    Assert.True(layout.Succeeded, layout.Message);
    var validation = await StatementLayoutService.ValidateLayoutAsync(db, preparer, layout.Value!.LayoutVersionId);
    Assert.True(validation.Succeeded, validation.Message);
    Assert.True(validation.Value!.IsValid);
    var publication = await StatementLayoutService.PublishLayoutAsync(db, reviewer, layout.Value.LayoutVersionId, "Synthetic independent layout publication");
    Assert.True(publication.Succeeded, publication.Message);
    var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, packageId, layout.Value.LayoutVersionId);
    Assert.True(pin.Succeeded, pin.Message);
    var render = await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, pin.Value!.RevisionId,
      FinancialStatementPresentationRenderer.PdfFormat);
    Assert.True(render.Succeeded, render.Message);
    var content = await StatementContentReviewCall.RecordAsync(db, reviewer, packageId, pin.Value.RevisionId);
    Assert.True(content.Succeeded, content.Message);
    var candidate = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, packageId, pin.Value.RevisionId);
    Assert.True(candidate.Succeeded, candidate.Message);
    var composition = await FinancialStatementService.SelectCompositionAsync(db, preparer, packageId, candidate.Value!.RevisionId);
    Assert.True(composition.Succeeded, composition.Message);
    return await db.FinancialStatementCompositions.AsNoTracking().SingleAsync(x => x.Id == composition.Value!.Id);
  }

}
