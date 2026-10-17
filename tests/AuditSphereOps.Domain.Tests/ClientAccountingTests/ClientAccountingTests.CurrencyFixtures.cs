using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  // Complete integration packages are built by the product; no validation rows or approved
  // component statuses are invented. Zero OCI is an explicit source row and layout line.
  private static async Task<Guid> BuildCurrencyPackageAsync(AuditSphereDbContext db, Scope scope,
    Guid clientId, Guid engagementId, IReadOnlyList<(string Destination, string Section, decimal Amount)> requested,
    string suffix, string currency = "USD")
  {
    var firmId = scope.FirmId;
    var preparer = scope.Preparer;
    var reviewer = scope.Reviewer;
    var datasetId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var taxonomyCode = "tax-v1";
    var lines = requested.Concat(new[] { (Destination: "ZERO_OCI", Section: "OCI", Amount: 0m) }).ToList();
    string Account(int index) => lines[index].Destination == "CASH" ? "1000" : (3000 + index).ToString();
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "FY2026-" + suffix,
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
      Basis = "STATUTORY", Currency = currency, Status = AccountingWorkflowStates.Active,
      CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientReportingBooks.Add(new ClientReportingBook
    {
      Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId,
      Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = currency,
      Status = AccountingWorkflowStates.Active, CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    var existingTaxonomy = await db.ReportingTaxonomyVersions.SingleOrDefaultAsync(x => x.FirmId == firmId && x.Code == taxonomyCode);
    var taxonomyId = existingTaxonomy?.Id ?? Guid.NewGuid();
    if (existingTaxonomy is null) db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
    {
      Id = taxonomyId, FirmId = firmId, Code = taxonomyCode, Framework = "IFRS", Name = "Candidate taxonomy",
      Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
      CreatedByUserId = preparer.Id, ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
      CreatedAt = DateTimeOffset.UtcNow
    });
    foreach (var line in lines)
      if (!await db.ReportingTaxonomyNodes.AnyAsync(x => x.TaxonomyVersionId == taxonomyId && x.Code == line.Destination))
      db.ReportingTaxonomyNodes.Add(new ReportingTaxonomyNode
      {
        Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId,
        Code = line.Destination, Name = line.Destination, StatementSection = line.Section,
        DisplaySign = "SIGNED", NormalBalance = line.Section is "ASSETS" or "EXPENSE" ? "DEBIT" : "CREDIT",
        IsPosting = true, Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
      });
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      PeriodId = periodId, BookId = bookId, Basis = "STATUTORY",
      SourceKind = "Raw", Revision = 1, Currency = currency, Balanced = true,
      ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.Id
    });
    TrialBalanceRow Row(string account, string name, decimal amount) => new()
    {
      Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = account, AccountName = name, Amount = amount, Currency = currency, Entity = "TEST"
    };
    for (var i = 0; i < lines.Count; i++)
      db.TrialBalanceRows.Add(Row(Account(i), lines[i].Destination, lines[i].Amount));

    await db.SaveChangesAsync();
    var preparerActor = Actor(preparer, "AccountingPreparer");
    var reviewerActor = Actor(reviewer, "AccountingReviewer");
    var cash = lines.Single(x => x.Destination == "CASH").Amount;
    var sourceId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    var digest = Hashing.Sha256Hex("currency-gl-" + sourceId);
    db.SourceImportBatches.Add(new SourceImportBatch
    {
      Id = sourceId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      PeriodId = periodId, BookId = bookId, SourceKind = "GL", ProfileVersion = "currency-test-v1",
      ParserVersion = "currency-test-v1", RawFileSha256Hex = digest, NormalizedDatasetDigest = digest,
      LegalEntityKey = clientId.ToString("D"), Currency = currency, RowCount = 2 * lines.Count(x => x.Destination != "CASH" && x.Amount != 0m), Status = "LOADING",
      ReceiptReference = suffix, CreatedByUserId = preparer.Id, CreatedAt = now
    });
    var cashFlows = new List<CashFlowLineInput>();
    var cashEvents = lines.Select((line, i) => (Account: Account(i), line.Destination, line.Amount,
      Section: line.Section is "EQUITY" or "LIABILITIES" ? "FINANCING" : line.Section == "ASSETS" ? "INVESTING" : "OPERATING"))
      .Where(x => x.Destination != "CASH" && x.Amount != 0m).ToList();
    foreach (var section in new[] { "OPERATING", "INVESTING", "FINANCING" })
      if (!cashEvents.Any(x => x.Section == section))
      {
        // A purchase/subsequent refund, operating advance/repayment, or funding/repayment.
        // Both legs carry cash evidence and net to zero in the period TB.
        cashEvents.Add(("ZERO-" + section, section + " advance", 5m, section));
        cashEvents.Add(("ZERO-" + section, section + " repayment", -5m, section));
      }
    var batch = db.SourceImportBatches.Local.Single(x => x.Id == sourceId);
    batch.RowCount = 2 * cashEvents.Count;
    for (var i = 0; i < cashEvents.Count; i++)
    {
      var line = cashEvents[i];
      var transactionId = Guid.NewGuid();
      var counterpartId = Guid.NewGuid();
      db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
      {
        Id = transactionId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        ImportBatchId = sourceId, StableJournalId = "CASH-" + suffix + "-" + i,
        PostingDate = new DateOnly(2026, 6, 30), Currency = currency, CreatedAt = now
      });
      foreach (var (id, account, amount) in new[] { (Guid.NewGuid(), "1000", -line.Amount), (counterpartId, line.Account, line.Amount) })
        db.GeneralLedgerLines.Add(new GeneralLedgerLine
        {
          Id = id, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
          ImportBatchId = sourceId, TransactionId = transactionId, StableLineId = account + "-" + suffix + "-" + i,
          AccountCode = account, Debit = Math.Max(amount, 0m), Credit = Math.Max(-amount, 0m),
          FunctionalAmount = amount, OriginalAmount = amount, OriginalCurrency = currency, CreatedAt = now
        });
      cashFlows.Add(new CashFlowLineInput(line.Section, line.Destination + " cash movement", -line.Amount,
        MovementDate: new DateOnly(2026, 6, 30), ActivityCode: "SOURCE_CASH_MOVEMENT",
        SourceReferences: ["journal-" + suffix + "-" + i],
        ClassificationRationale: "Cash counterpart of the source " + line.Section.ToLowerInvariant() + " movement.",
        SourceAllocations: [new CashFlowSourceAllocationInput(counterpartId, line.Amount)]));
    }
    await db.SaveChangesAsync();
    await db.SourceImportBatches.Where(x => x.Id == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SEALED"));
    var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewerActor,
      new(clientId, engagementId, AccountingSourceKinds.GeneralLedger, null, sourceId, "Reviewed cash movements " + suffix));
    Assert.True(accepted.Succeeded, accepted.Message);
    var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparerActor,
      new CreateMappingVersionRequest(datasetId, taxonomyCode, "2026-01-01", "2026-12-31",
        lines.Select((line, i) => new MappingAllocationInput(Account(i), line.Destination, line.Section, 1m,
          "Source-backed currency fixture mapping")).ToList()));
    Assert.True(mapping.Succeeded, mapping.Message);
    var mappingVersion = await db.MappingVersions.Where(x => x.Id == mapping.Value)
      .Select(x => x.Version).SingleAsync();
    var approvedMapping = await FinancialStatementService.ApproveMappingAsync(db, reviewerActor, mapping.Value, mappingVersion);
    Assert.True(approvedMapping.Succeeded, approvedMapping.Message);
    var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparerActor, datasetId, []);
    Assert.True(plan.Succeeded, plan.Message);
    var finalized = await AdjustmentPlanService.FinalizeAsync(db, preparerActor, plan.Value);
    Assert.True(finalized.Succeeded, finalized.Message);
    var profit = -lines.Where(x => x.Section is "INCOME" or "EXPENSE").Sum(x => x.Amount);
    var capital = -lines.Where(x => x.Section == "EQUITY").Sum(x => x.Amount);
    var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparerActor,
      new BuildFinancialPackageRequest(plan.Value, mapping.Value, "IFRS", "2026-01-01", "2026-12-31", "currency-" + suffix)
      {
        SupplementaryInformation = new FinancialSupplementaryInformation(0m, cash,
          cashFlows,
          [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
           new DisclosureInput("OCI_POLICY", "No OCI transactions occurred during the period.")],
          [new EquityLineInput("CAPITAL", "Capital", 0m, 0m, 0m, capital, 0m, capital, "capital-" + suffix),
           new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, profit, 0m, 0m, 0m, profit, "profit-" + suffix)],
          FxCashAssessment: new FxCashAssessmentInput(FxCashAssessmentStates.AssessedZero, "functional-currency-cash-population",
            "All cash is denominated in functional currency; no entity-level exchange exposure."),
          CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-" + suffix],
          ClosingCashEvidenceReferences: ["closing-" + suffix])
      });
    Assert.True(built.Succeeded, built.Message);
    Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
    var builtLines = await db.FinancialPackageLines.AsNoTracking().Where(x => x.FinancialPackageId == built.Value.PackageId).ToListAsync();
    Assert.Equal(lines.Count, builtLines.Count);
    foreach (var expected in lines)
    {
      var actual = Assert.Single(builtLines, line => line.DestinationCode == expected.Destination);
      Assert.Equal(expected.Amount, actual.Amount);
      Assert.Equal(expected.Section, actual.StatementSection);
      Assert.Equal(currency, actual.Currency);
    }
    var sourceReview = await CashFlowSourceReviewCall.RecordAsync(db, reviewerActor, built.Value.PackageId, "APPROVED", null);
    Assert.True(sourceReview.Succeeded, sourceReview.Message);
    return built.Value.PackageId;
  }

  private static async Task<Guid> ReviewCurrencyCompositionAsync(AuditSphereDbContext db, Scope scope, Guid packageId)
  {
    var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
    var balances = await db.FinancialPackageLines.AsNoTracking().Where(x => x.FinancialPackageId == packageId).ToListAsync();
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var definitions = balances.Select((line, i) => new StatementLineDefinition("L" + i, line.DestinationCode,
      StatementLayoutLineKinds.MappedTaxonomyBalance,
      line.StatementSection == "OCI" ? StatementLayoutSections.OtherComprehensiveIncome :
      line.StatementSection is "INCOME" or "EXPENSE" ? StatementLayoutSections.ProfitOrLoss : StatementLayoutSections.FinancialPosition,
      i + 1, TaxonomyNodeCode: line.DestinationCode,
      DisplaySign: line.StatementSection is "EQUITY" or "LIABILITIES" or "INCOME" or "OCI"
        ? StatementLayoutDisplaySigns.Inverted : StatementLayoutDisplaySigns.Signed)).ToList();
    var layout = await StatementLayoutService.SaveLayoutDraftAsync(db, preparer,
      new StatementLayoutDraftRequest(package.ClientId, "IFRS", "Currency fixture " + packageId, definitions));
    Assert.True(layout.Succeeded, layout.Message);
    var validation = await StatementLayoutService.ValidateLayoutAsync(db, preparer, layout.Value!.LayoutVersionId);
    Assert.True(validation.Succeeded, validation.Message);
    Assert.True(validation.Value!.IsValid, string.Join("; ", validation.Value.Errors.Select(x => x.Message)));
    var published = await StatementLayoutService.PublishLayoutAsync(db, reviewer, layout.Value.LayoutVersionId, "Reviewed currency layout");
    Assert.True(published.Succeeded, published.Message);
    var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, packageId, layout.Value.LayoutVersionId);
    Assert.True(pin.Succeeded, pin.Message);
    var rendered = await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, pin.Value!.RevisionId,
      FinancialStatementPresentationRenderer.PdfFormat);
    Assert.True(rendered.Succeeded, rendered.Message);
    var content = await StatementContentReviewCall.RecordAsync(db, reviewer, packageId, pin.Value.RevisionId);
    Assert.True(content.Succeeded, content.Message);
    var candidate = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, packageId, pin.Value.RevisionId);
    Assert.True(candidate.Succeeded, candidate.Message);
    var composition = await FinancialStatementService.SelectCompositionAsync(db, preparer, packageId, candidate.Value!.RevisionId);
    Assert.True(composition.Succeeded, composition.Message);
    foreach (var (actor, stage, mode) in new[]
    {
      (preparer, FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewEvidenceModes.Offline),
      (reviewer, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewEvidenceModes.SignedIn),
      (Actor(scope.Partner, "Partner"), FinancialPackageReviewStages.PartnerApproval, FinancialPackageReviewEvidenceModes.SignedIn)
    })
    {
      var decision = await FinancialPackageReviewService.RecordAsync(db, actor,
        new(packageId, stage, FinancialPackageReviewDecisions.Approved, mode, "currency-" + stage,
          "Reviewed exact currency composition.", candidate.Value.RevisionId, null,
          composition.Value!.Id, composition.Value.ManifestDigest));
      Assert.True(decision.Succeeded, $"{stage}: {decision.Message}");
    }
    Assert.True((await FinancialPackageReviewService.RequireCurrentAsync(db, Actor(scope.Partner, "Partner"), packageId, true)).Succeeded);
    var decisions = await db.FinancialPackageReviewDecisions.AsNoTracking().Where(x => x.FinancialPackageId == packageId).ToListAsync();
    Assert.Equal(3, decisions.Count);
    Assert.All(decisions, decision =>
    {
      Assert.Equal(candidate.Value.RevisionId, decision.PresentationRevisionId);
      Assert.Equal(composition.Value!.Id, decision.PresentationCompositionId);
      Assert.Equal(composition.Value.ManifestDigest, decision.CompositionManifestDigest);
    });
    return composition.Value!.Id;
  }

  private static async Task<string> CurrencyPackageSourceManifestAsync(AuditSphereDbContext db, Guid[] packageIds)
  {
    var packages = await db.FinancialPackages.AsNoTracking().Where(x => packageIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
    var lines = await db.FinancialPackageLines.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId)).OrderBy(x => x.Id).ToListAsync();
    var artifacts = await db.FinancialPackageArtifacts.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId)).OrderBy(x => x.Id).ToListAsync();
    return System.Text.Json.JsonSerializer.Serialize(new { packages, lines, artifacts });
  }

  // Raw, deliberately unreviewable input for service gate-denial tests only.
  private static async Task<Guid> AddSparseCurrencyPackageAsync(AuditSphereDbContext db, Scope scope, Guid clientId, Guid engagementId,
    IReadOnlyList<(string Destination, string Section, decimal Amount)> lines,
    string? suffix = null, string currency = "USD")
  {
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var digest = Hashing.Sha256Hex($"package-{clientId:D}-{suffix ?? "multiline"}");
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, SourceKind = "Raw",
      Revision = 1, LegalEntityKey = clientId.ToString("D"), Currency = currency, RawFileSha256Hex = digest,
      NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true, ValidationStatus = "Accepted",
      ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m, ImportedAt = now, ImportedByUserId = scope.Preparer.Id
    });
    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      Status = "Finalized", ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, DatasetId = datasetId,
      Version = 1, Generation = 1, TaxonomyVersion = "tax-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      Status = AccountingPackageStates.MappingApproved, CreatedByUserId = scope.Preparer.Id, ApprovedByUserId = scope.Reviewer.Id,
      ApprovedAt = now, CreatedAt = now
    });
    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      AdjustmentPlanId = planId, Currency = currency, ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, AdjustedDatasetId = adjustedId,
      MappingVersionId = mappingId, AdjustmentPlanId = planId, Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      TaxonomyVersion = "tax-v1", TemplateVersion = "template-v1", CalculationEngineVersion = "test-engine",
      CalculationHash = digest, Currency = currency, Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    int accountIndex = 1000;
    foreach (var (destination, section, amount) in lines)
    {
      db.FinancialPackageLines.Add(new FinancialPackageLine
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, FinancialPackageId = packageId,
        SourceAccountCode = (accountIndex += 10).ToString(), DestinationCode = destination, StatementSection = section,
        Amount = amount, Fraction = 1m, Currency = currency, AdjustedSnapshotId = adjustedId, CreatedAt = now
      });
    }
    var artifactBytes = System.Text.Encoding.UTF8.GetBytes($"package-artifact|{packageId:D}|{digest}");
    db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId,
      FinancialPackageId = packageId, PackageRevision = 1, PackageGeneration = 1, PackageHash = digest,
      ArtifactVersion = FinancialPackageArtifactVersions.Text, FrameworkVersion = "IFRS", TemplateVersion = "template-v1",
      ArtifactSha256Hex = Hashing.Sha256Hex(artifactBytes), ArtifactBytes = artifactBytes,
      CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    return packageId;
  }

}
