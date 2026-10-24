using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  public void NativeGeneralLedgerKeepsLegacyExternalCompletenessSourceIdentitySelection()
  {
    var legacySha = new string('a', 64);
    var normalized = new string('b', 64);
    var external = new TrialBalanceDataset { SourceKind = "Raw", Sha256Hex = legacySha, NormalizedDatasetDigest = "" };
    Assert.Equal(legacySha, GeneralLedgerCompletenessWorkspace.SourceDigest(external));
    external.NormalizedDatasetDigest = "legacy-incomplete-digest";
    Assert.Equal(legacySha, GeneralLedgerCompletenessWorkspace.SourceDigest(external));
    external.NormalizedDatasetDigest = normalized;
    Assert.Equal(normalized, GeneralLedgerCompletenessWorkspace.SourceDigest(external));
    var native = new TrialBalanceDataset { SourceKind = "Native", Sha256Hex = legacySha, NormalizedDatasetDigest = normalized };
    Assert.Equal(legacySha, GeneralLedgerCompletenessWorkspace.SourceDigest(native));
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeGeneralLedgerPreservesOriginalSourceFactsAndRequiresIndependentSelectionAndRealOpening()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeGlFixtureAsync(pg);
    var source = await CaptureNativeGlTrialBalanceAsync(pg, f, verifyPrerequisites: true);
    var request = NativeGlRequest(f, source);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Scope.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var preview = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(db, preparer, request);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.Equal("20", preview.Value!.TotalDebit);
    Assert.Equal("20", preview.Value.TotalCredit);
    Assert.Equal("1", preview.Value.PostingSequence);
    Assert.Equal(f.OpeningId, preview.Value.NativeOpening.OpeningId);
    var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == f.OpeningId);
    Assert.Equal(opening.ManifestSha256, preview.Value.NativeOpening.ManifestHash);
    var originalLines = await db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.JournalId == f.JournalId).ToListAsync();
    Assert.Equal(2, preview.Value.Rows.Count);
    foreach (var row in preview.Value.Rows)
    {
      var original = Assert.Single(originalLines, x => x.Id == row.LineId);
      Assert.Equal(f.JournalId, row.JournalId);
      Assert.Equal(original.ClientAccountId, row.ClientAccountId);
      Assert.Equal(f.Scope.Preparer.Id.ToString("D"), row.SourceUser);
      Assert.Equal("AUDITSPHERE_NATIVE", row.SourceSystem);
      Assert.Equal("QAR", row.Currency);
      Assert.Null(row.IsManual); Assert.Null(row.IsYearEnd);
    }
    Assert.Empty(await db.SourceImportBatches.ToListAsync());
    Assert.False((await NativeGeneralLedgerSourceWorkspace.SealAsync(db, preparer, request)).Succeeded);
    var reviewed = request with { Reviewed = true, ReviewBasis = preview.Value.ReviewBasis };
    var sealedSource = await NativeGeneralLedgerSourceWorkspace.SealAsync(db, preparer, reviewed);
    Assert.True(sealedSource.Succeeded, sealedSource.Message);
    var id = sealedSource.Value!.ImportBatchId;
    var batch = await db.SourceImportBatches.AsNoTracking().SingleAsync(x => x.Id == id);
    Assert.Equal("NATIVE_LEDGER", batch.OriginKind);
    Assert.Equal("", batch.RawFileSha256Hex);
    Assert.Null(batch.BookId);
    Assert.Equal(0, batch.ExpectedChunkCount);
    Assert.Equal(0, batch.AcceptedChunkCount);
    Assert.Equal("native-posted-gl.v1", batch.ProfileVersion);
    Assert.Equal("native-gl-export.v1", batch.ParserVersion);
    Assert.Empty(await db.GeneralLedgerImportChunks.ToListAsync());
    var evidence = await db.NativeGeneralLedgerSources.AsNoTracking().SingleAsync(x => x.ImportBatchId == id);
    Assert.Equal(source.SourceIdentityHash, evidence.NativeTrialBalanceSourceHash);
    Assert.Equal(source.ManifestHash, evidence.NativeTrialBalanceManifestHash);
    Assert.Equal(batch.NormalizedDatasetDigest, evidence.SourceIdentityHash);
    Assert.Equal(Hashing.Sha256Hex(evidence.GeneratedRowsJson), evidence.GeneratedRowsHash);
    Assert.Equal(JsonSerializer.Serialize(preview.Value.Rows), evidence.GeneratedRowsJson);
    using var artifact = JsonDocument.Parse(evidence.GeneratedRowsJson);
    Assert.Equal(JsonValueKind.Array, artifact.RootElement.ValueKind);
    Assert.Equal(2, artifact.RootElement.GetArrayLength());
    using var manifest = JsonDocument.Parse(evidence.ManifestJson);
    var tbManifest = manifest.RootElement.GetProperty("NativeTrialBalanceManifestJson").GetString()!;
    using var pinnedTb = JsonDocument.Parse(tbManifest);
    using var membership = JsonDocument.Parse(pinnedTb.RootElement.GetProperty("Pins").GetProperty("MembershipJson").GetString()!);
    Assert.Contains(membership.RootElement.GetProperty("Journals").EnumerateArray(), x => x.GetProperty("Id").GetGuid() == f.JournalId);
    var snapshot = await db.ClientOperationalJournalSnapshots.AsNoTracking().SingleAsync(x => x.JournalId == f.JournalId && x.JournalRevision == 2);
    Assert.Contains(membership.RootElement.GetProperty("Submissions").EnumerateArray(), x =>
      x.GetProperty("Id").GetGuid() == snapshot.Id && x.GetProperty("SnapshotHash").GetString() == Hashing.Sha256Hex(snapshot.SnapshotJson));
    var transaction = await db.GeneralLedgerTransactions.AsNoTracking().SingleAsync(x => x.ImportBatchId == id);
    Assert.NotEqual(f.JournalId, transaction.Id);
    Assert.Equal(f.JournalId.ToString("D"), transaction.StableJournalId);
    Assert.Null(transaction.DocumentDate); Assert.Null(transaction.ServiceDate);
    Assert.Null(transaction.IsManual); Assert.Null(transaction.IsYearEnd);
    var projected = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.ImportBatchId == id).ToListAsync();
    Assert.All(projected, l =>
    {
      Assert.DoesNotContain(originalLines, x => x.Id == l.Id);
      Assert.Contains(originalLines, x => x.Id.ToString("D") == l.StableLineId && x.Debit == l.Debit && x.Credit == l.Credit);
      Assert.Equal(l.Debit - l.Credit, l.FunctionalAmount);
      Assert.Equal(l.FunctionalAmount, l.OriginalAmount);
      Assert.Null(l.IsManual); Assert.Null(l.IsYearEnd);
    });
    var notSelected = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(db, preparer,
      f.Scope.ClientA, f.Scope.EngagementA, f.PeriodId);
    Assert.True(notSelected.Succeeded, notSelected.Message); Assert.Null(notSelected.Value);
    var self = await SourceAcceptanceWorkspace.GetGeneralLedgerAsync(db, preparer, id);
    Assert.True(self.Succeeded, self.Message); Assert.False(self.Value!.CanAccept);
    var acceptance = await SourceAcceptanceWorkspace.GetGeneralLedgerAsync(db, reviewer, id);
    Assert.True(acceptance.Succeeded, acceptance.Message); Assert.True(acceptance.Value!.CanAccept, acceptance.Value.Blocker);
    var accepted = await SourceAcceptanceWorkspace.AcceptGeneralLedgerAsync(db, reviewer, id,
      acceptance.Value.Revision, "Synthetic independent native GL selection", true);
    Assert.True(accepted.Succeeded, accepted.Message);
    var picker = await FinancialScheduleSourceQuery.GetAsync(db, preparer, f.Scope.EngagementA, f.PeriodId);
    Assert.True(picker.Succeeded, picker.Message);
    Assert.Equal(projected.Select(x => x.Id).Order(), picker.Value!.Lines.Select(x => x.LineId).Order());
    var bridge = await AccountingAnalysisService.CreateGeneralLedgerCompletenessBridgeAsync(db, preparer,
      new(f.Scope.ClientA, f.Scope.EngagementA, f.PeriodId, null, source.DatasetId, id, "Synthetic native opening plus posted movement"));
    Assert.True(bridge.Succeeded, bridge.Message);
    var retainedBridge = await db.GeneralLedgerCompletenessBridges.AsNoTracking().SingleAsync(x => x.Id == bridge.Value);
    Assert.Null(retainedBridge.OpeningTrialBalanceDatasetId);
    Assert.Equal(opening.ManifestSha256, retainedBridge.OpeningTrialBalanceHash);
    Assert.Equal(source.SourceIdentityHash, retainedBridge.TrialBalanceHash);
    Assert.False(retainedBridge.IncompleteExtract);
    Assert.Equal(0m, retainedBridge.AbsoluteResidual);
    Assert.Equal(0m, retainedBridge.OpeningMovementResidual);
    var review = await GeneralLedgerCompletenessWorkspace.ReviewAsync(db, reviewer, bridge.Value);
    Assert.True(review.Succeeded, review.Message);
    Assert.Equal(f.OpeningId, review.Value!.Context.NativeOpening!.OpeningId);
    Assert.True(review.Value.CanApprove, review.Value.ApprovalBlocker);
    var approved = await AccountingAnalysisService.ReviewGeneralLedgerCompletenessAsync(db, reviewer, bridge.Value, true,
      reviewRevision: review.Value.Revision, reviewed: true);
    Assert.True(approved.Succeeded, approved.Message);
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeGeneralLedgerExactRetryConflictsWithChangedIntentAndRefusesSiblingScopeOrWithdrawnMandate()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeGlFixtureAsync(pg);
    var source = await CaptureNativeGlTrialBalanceAsync(pg, f);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    var request = NativeGlRequest(f, source);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preview = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(preview.Succeeded, preview.Message);
      var repeatedPreview = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(repeatedPreview.Succeeded, repeatedPreview.Message);
      Assert.Equal(preview.Value!.SourceIdentityHash, repeatedPreview.Value!.SourceIdentityHash);
      Assert.Equal(preview.Value.GeneratedRowsHash, repeatedPreview.Value.GeneratedRowsHash);
      request = request with { Reviewed = true, ReviewBasis = preview.Value.ReviewBasis };
      var first = await NativeGeneralLedgerSourceWorkspace.SealAsync(db, preparer, request);
      Assert.True(first.Succeeded, first.Message);
      var retry = await NativeGeneralLedgerSourceWorkspace.SealAsync(db, preparer, request);
      Assert.True(retry.Succeeded, retry.Message); Assert.Equal(first.Value!.ImportBatchId, retry.Value!.ImportBatchId);
      var conflict = await NativeGeneralLedgerSourceWorkspace.SealAsync(db, preparer, request with { EvidenceReference = "Changed intent" });
      Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
      var duplicateRequest = request with { RequestId = Guid.CreateVersion7(), Reviewed = false, ReviewBasis = null };
      var duplicatePreview = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(db, preparer, duplicateRequest);
      Assert.True(duplicatePreview.Succeeded, duplicatePreview.Message);
      Assert.Equal(preview.Value.SourceIdentityHash, duplicatePreview.Value!.SourceIdentityHash);
      var duplicate = await NativeGeneralLedgerSourceWorkspace.SealAsync(db, preparer,
        duplicateRequest with { Reviewed = true, ReviewBasis = duplicatePreview.Value.ReviewBasis });
      Assert.Equal(ErrorCodes.Accounting.ImportDuplicate, duplicate.ErrorCode);
      var sibling = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(db, preparer,
        duplicateRequest with { EngagementId = f.Scope.EngagementB });
      Assert.False(sibling.Succeeded);
      Assert.Single(await db.NativeGeneralLedgerSources.ToListAsync());
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = f.Scope.FirmId, PracticeClientId = f.Scope.ClientA, ServiceRoute = "BOOKKEEPING",
        Decision = "Declined", Generation = 2, Rationale = "Synthetic withdrawn mandate", EvaluationTemplateVersion = "TEST-NATIVE-GL",
        EvaluationSnapshotDigest = new string('b', 64), DecidedByUserId = f.Scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      Assert.False((await NativeGeneralLedgerSourceWorkspace.SealAsync(db, preparer, request)).Succeeded);
      var retained = await db.NativeGeneralLedgerSources.AsNoTracking().SingleAsync();
      var historical = await NativeGeneralLedgerSourceWorkspace.ArtifactAsync(db, preparer, retained.ImportBatchId);
      Assert.True(historical.Succeeded, historical.Message); Assert.False(historical.Value!.IsCurrent);
      Assert.Equal(retained.GeneratedRowsJson, historical.Value.GeneratedRowsJson);
    }
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeGeneralLedgerUnknownMetadataRefusesCounterpartyAndRiskAnalysisWhilePostingFactsRemainReadable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeGlFixtureAsync(pg);
    var tb = await CaptureNativeGlTrialBalanceAsync(pg, f);
    var gl = await SealNativeGlAsync(pg, f, tb);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var refused = await GeneralLedgerQuery.GetLinesAsync(db, preparer, gl.ImportBatchId, new(Counterparty: "Unknown party"));
    Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
    var readable = await GeneralLedgerQuery.GetLinesAsync(db, preparer, gl.ImportBatchId,
      new(PostedFrom: new DateOnly(2026, 1, 1), PostedTo: new DateOnly(2026, 12, 31)));
    Assert.True(readable.Succeeded, readable.Message); Assert.Equal(2, readable.Value!.TotalCount);
    var drilldown = await GeneralLedgerQuery.GetJournalDrillDownAsync(db, preparer, gl.ImportBatchId, f.JournalId.ToString("D"));
    Assert.True(drilldown.Succeeded, drilldown.Message); Assert.Null(drilldown.Value!.IsManual); Assert.Null(drilldown.Value.IsYearEnd);
    var page = await AccountingAnalysisService.GetGeneralLedgerPageAsync(db, preparer, gl.ImportBatchId);
    Assert.True(page.Succeeded, page.Message);
    Assert.Contains("DocumentDate", page.Value!.UnavailableMetadata);
    Assert.Contains("ServiceDate", page.Value.UnavailableMetadata);
    Assert.Contains("PartyIdentifier", page.Value.UnavailableMetadata);
    Assert.All(page.Value.Rows, row => { Assert.Null(row.DocumentDate); Assert.Null(row.ServiceDate); });
    var risk = await AccountingAnalysisService.AnalyzeJournalRiskAsync(db, preparer,
      new(f.Scope.ClientA, f.Scope.EngagementA, gl.ImportBatchId, new DateOnly(2026, 12, 31), 10m));
    Assert.Equal(ErrorCodes.GateBlocked, risk.ErrorCode);
    Assert.Contains("Unknown", risk.Message!);
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeGeneralLedgerAcceptedCashFlowPackageStalesAfterOffsettingBackdatedPostingsWithoutChangingClosingTotals()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeGlFixtureAsync(pg);
    var tb = await CaptureNativeGlTrialBalanceAsync(pg, f);
    var gl = await SealNativeGlAsync(pg, f, tb);
    // Both independently accepted entity contexts remain current; period/currency alone cannot establish an exact source pair.
    var otherTb = await CaptureNativeGlTrialBalanceAsync(pg, f, legalEntityKey: "NATIVE-OTHER-ENTITY");
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Scope.Reviewer, "AccountingReviewer");
    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await AcceptNativeGlAsync(db, f, gl.ImportBatchId);
      var otherMapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new(otherTb.DatasetId, "native-gl-test", "2026-01-01", "2026-12-31",
          [new("1000", "CASH", "ASSETS", 1m, "Other synthetic native cash"),
           new("2000", "BORROWINGS", "LIABILITIES", 1m, "Other synthetic financing"),
           new("3000", "CAPITAL", "EQUITY", 1m, "Other synthetic opening capital")], f.ChartId));
      Assert.True(otherMapping.Succeeded, otherMapping.Message);
      var otherVersion = await db.MappingVersions.Where(x => x.Id == otherMapping.Value).Select(x => x.Version).SingleAsync();
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, otherMapping.Value, otherVersion)).Succeeded);
      var otherPlan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, otherTb.DatasetId, []);
      Assert.True(otherPlan.Succeeded, otherPlan.Message);
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, otherPlan.Value)).Succeeded);
      await using (var mismatchDb = new AuditSphereDbContext(pg.Options))
      {
        foreach (var id in new[] { tb.DatasetId, otherTb.DatasetId })
        {
          var currentTb = await mismatchDb.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == id);
          var reporting = await NativeTrialBalanceSourceWorkspace.CheckReportingAsync(mismatchDb, currentTb);
          Assert.True(reporting.Succeeded, reporting.Message);
        }
        var selected = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(mismatchDb, preparer,
          f.Scope.ClientA, f.Scope.EngagementA, f.PeriodId);
        Assert.True(selected.Succeeded, selected.Message); Assert.True(selected.Value!.IsCurrent);
        var originalLoan = await mismatchDb.GeneralLedgerLines.AsNoTracking().SingleAsync(x => x.ImportBatchId == gl.ImportBatchId && x.AccountCode == "2000");
        var mismatch = await FinancialStatementService.BuildFinancialPackageAsync(mismatchDb, preparer,
          new BuildFinancialPackageRequest(otherPlan.Value, otherMapping.Value, "IFRS", "2026-01-01", "2026-12-31", "native-gl-wrong-tb-pair")
          { SupplementaryInformation = NativeGlCashInformation(f, otherTb, originalLoan.Id) });
        Assert.False(mismatch.Succeeded);
        Assert.Equal(ErrorCodes.GateBlocked, mismatch.ErrorCode);
        Assert.Contains("exact native TB dataset", mismatch.Message!);
      }
      Assert.False(await db.FinancialPackages.AnyAsync(x => x.MappingVersionId == otherMapping.Value));
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new(tb.DatasetId, "native-gl-test", "2026-01-01", "2026-12-31",
          [new("1000", "CASH", "ASSETS", 1m, "Synthetic native cash"),
           new("2000", "BORROWINGS", "LIABILITIES", 1m, "Synthetic native financing"),
           new("3000", "CAPITAL", "EQUITY", 1m, "Synthetic native opening capital")], f.ChartId));
      Assert.True(mapping.Succeeded, mapping.Message);
      var version = await db.MappingVersions.Where(x => x.Id == mapping.Value).Select(x => x.Version).SingleAsync();
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mapping.Value, version)).Succeeded);
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, tb.DatasetId, []);
      Assert.True(plan.Succeeded, plan.Message);
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, plan.Value)).Succeeded);
      var loan = await db.GeneralLedgerLines.AsNoTracking().SingleAsync(x => x.ImportBatchId == gl.ImportBatchId && x.AccountCode == "2000");
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        new BuildFinancialPackageRequest(plan.Value, mapping.Value, "IFRS", "2026-01-01", "2026-12-31", "native-gl-source-test")
        {
          SupplementaryInformation = NativeGlCashInformation(f, tb, loan.Id)
        });
      Assert.True(built.Succeeded, built.Message); packageId = built.Value!.PackageId;
      Assert.True(await db.FinancialPackageValidations.AnyAsync(x => x.FinancialPackageId == packageId && x.Code == "CASH_FLOW_SOURCE_LINEAGE" && x.Passed));
      Assert.False((await CashFlowSourceReviewCall.RecordAsync(db, preparer, packageId, "APPROVED", null)).Succeeded);
      var independent = await CashFlowSourceReviewCall.RecordAsync(db, reviewer, packageId, "APPROVED", null);
      Assert.True(independent.Succeeded, independent.Message);
      var reviewed = await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId);
      Assert.True(reviewed.Succeeded, reviewed.Message); Assert.True(reviewed.Value!.Approved);
      var readiness = await FinancialPackageValidationReadiness.GetAsync(db, f.Scope.FirmId, packageId);
      Assert.DoesNotContain("NATIVE_GL_SOURCE_CURRENT", readiness.Failed);
    }
    await PostNativeGlJournalAsync(pg, f, "BACKDATED-ADD", 7m, false);
    await PostNativeGlJournalAsync(pg, f, "BACKDATED-OFFSET", 7m, true);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var live = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, preparer, f.Scope.ClientA, f.PeriodId);
      Assert.True(live.Succeeded, live.Message);
      Assert.Equal("120", live.Value!.TrialBalance.ClosingDebit);
      Assert.Equal("120", live.Value.TrialBalance.ClosingCredit);
      var historical = await NativeGeneralLedgerSourceWorkspace.ArtifactAsync(db, preparer, gl.ImportBatchId);
      Assert.True(historical.Succeeded, historical.Message); Assert.False(historical.Value!.IsCurrent);
      Assert.Equal(gl.SourceIdentityHash, historical.Value.SourceIdentityHash);
      var selected = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(db, preparer, f.Scope.ClientA, f.Scope.EngagementA, f.PeriodId);
      Assert.True(selected.Succeeded, selected.Message); Assert.Equal(gl.ImportBatchId, selected.Value!.ImportBatchId); Assert.False(selected.Value.IsCurrent);
      Assert.False((await FinancialScheduleSourceQuery.GetAsync(db, preparer, f.Scope.EngagementA, f.PeriodId)).Succeeded);
      var review = await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId);
      Assert.True(review.Succeeded, review.Message); Assert.False(review.Value!.Approved);
      var readiness = await FinancialPackageValidationReadiness.GetAsync(db, f.Scope.FirmId, packageId);
      Assert.False(readiness.Ready);
      Assert.Contains("NATIVE_GL_SOURCE_CURRENT", readiness.Failed);
    }
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeGeneralLedgerRefusesOpeningOnlyAndActivityOnlySourcesWithoutInventingAnOpeningJournal()
  {
    await using (var pg = await PgTestSchema.CreateAsync())
    {
      var f = await CreateNativeGlFixtureAsync(pg, withPosting: false);
      var tb = await CaptureNativeGlTrialBalanceAsync(pg, f);
      await using var db = new AuditSphereDbContext(pg.Options);
      var preview = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(db, Actor(f.Scope.Preparer, "AccountingPreparer"), NativeGlRequest(f, tb));
      Assert.Equal(ErrorCodes.GateBlocked, preview.ErrorCode);
      Assert.Contains("nonempty", preview.Message!);
      Assert.Empty(await db.SourceImportBatches.ToListAsync());
      Assert.Empty(await db.GeneralLedgerTransactions.ToListAsync());
    }
    await using (var pg = await PgTestSchema.CreateAsync())
    {
      var f = await CreateNativeBehaviourFixtureAsync(pg, withOpening: false);
      await using var db = new AuditSphereDbContext(pg.Options);
      var actor = Actor(f.Scope.Preparer, "AccountingPreparer");
      var request = NativeBehaviourRequest(f) with { AllowActivityOnly = true };
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, actor, request);
      Assert.True(preview.Succeeded, preview.Message);
      var tb = await NativeTrialBalanceSourceWorkspace.SealAsync(db, actor,
        request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
      Assert.True(tb.Succeeded, tb.Message);
      var gl = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(db, actor, NativeGlRequest(f, tb.Value!));
      Assert.Equal(ErrorCodes.GateBlocked, gl.ErrorCode);
      Assert.Contains("opening", gl.Message!, StringComparison.OrdinalIgnoreCase);
      Assert.Empty(await db.SourceImportBatches.ToListAsync());
      Assert.Empty(await db.NativeGeneralLedgerSources.ToListAsync());
    }
  }

  private static FinancialSupplementaryInformation NativeGlCashInformation(NativeBehaviourFixture f,
    NativeTrialBalanceSourceView tb, Guid loanLineId) =>
new FinancialSupplementaryInformation(100m, 120m,
            [new("FINANCING", "Synthetic actual loan proceeds", 20m, MovementDate: new DateOnly(2026, 1, 15),
              ActivityCode: "LOAN_PROCEEDS", SourceReferences: [f.JournalId.ToString("D")], ClassificationRationale: "Synthetic exact posted loan source",
              SourceAllocations: [new(loanLineId, -20m)])],
            [new("CASH_POLICY", "Synthetic functional currency cash"), new("OCI_POLICY", "No OCI in synthetic period")],
            [new("CAPITAL", "Capital", 100m, 0m, 0m, 0m, 0m, 100m, f.OpeningId!.Value.ToString("D"))],
            CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: [f.OpeningId.Value.ToString("D")],
            ClosingCashEvidenceReferences: [tb.SourceIdentityHash],
            FxCashAssessment: new(FxCashAssessmentStates.AssessedZero, tb.SourceIdentityHash, "Synthetic functional-currency-only source"));

  private static NativeGeneralLedgerSourceRequest NativeGlRequest(NativeBehaviourFixture f, NativeTrialBalanceSourceView tb) =>
    new(Guid.CreateVersion7(), f.Scope.ClientA, f.Scope.EngagementA, tb.DatasetId, tb.SourceIdentityHash, true, "Synthetic native GL source evidence");

  private static async Task<NativeTrialBalanceSourceView> CaptureNativeGlTrialBalanceAsync(PgTestSchema pg, NativeBehaviourFixture f, bool verifyPrerequisites = false, string? legalEntityKey = null)
  {
    NativeTrialBalanceSourceView source;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var actor = Actor(f.Scope.Preparer, "AccountingPreparer");
      var request = NativeBehaviourRequest(f);
      if (legalEntityKey is not null) request = request with { LegalEntityKey = legalEntityKey };
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, actor, request);
      Assert.True(preview.Succeeded, preview.Message);
      var sealedSource = await NativeTrialBalanceSourceWorkspace.SealAsync(db, actor, request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
      Assert.True(sealedSource.Succeeded, sealedSource.Message); source = sealedSource.Value!;
    }
    if (verifyPrerequisites)
    {
      await using var pendingDb = new AuditSphereDbContext(pg.Options);
      var pending = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(pendingDb,
        Actor(f.Scope.Preparer, "AccountingPreparer"), NativeGlRequest(f, source));
      Assert.Equal(ErrorCodes.GateBlocked, pending.ErrorCode);
      Assert.Contains("technical validation", pending.Message!);
      Assert.Empty(await pendingDb.SourceImportBatches.ToListAsync());
    }
    await ValidateNativeBehaviourSourceAsync(pg, f.Scope.FirmId, source.DatasetId);
    if (verifyPrerequisites)
    {
      await using var unselectedDb = new AuditSphereDbContext(pg.Options);
      var unselected = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(unselectedDb,
        Actor(f.Scope.Preparer, "AccountingPreparer"), NativeGlRequest(f, source));
      Assert.Equal(ErrorCodes.GateBlocked, unselected.ErrorCode);
      Assert.Contains("Independently accept", unselected.Message!);
      Assert.Empty(await unselectedDb.SourceImportBatches.ToListAsync());
    }
    await AcceptNativeBehaviourSourceAsync(pg, f, source.DatasetId);
    return source;
  }

  private static async Task<NativeGeneralLedgerSourceView> SealNativeGlAsync(PgTestSchema pg, NativeBehaviourFixture f, NativeTrialBalanceSourceView tb)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var actor = Actor(f.Scope.Preparer, "AccountingPreparer"); var request = NativeGlRequest(f, tb);
    var preview = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(db, actor, request);
    Assert.True(preview.Succeeded, preview.Message);
    var result = await NativeGeneralLedgerSourceWorkspace.SealAsync(db, actor, request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
    Assert.True(result.Succeeded, result.Message); return result.Value!;
  }

  private static async Task AcceptNativeGlAsync(AuditSphereDbContext db, NativeBehaviourFixture f, Guid id)
  {
    var actor = Actor(f.Scope.Reviewer, "AccountingReviewer");
    var review = await SourceAcceptanceWorkspace.GetGeneralLedgerAsync(db, actor, id);
    Assert.True(review.Succeeded, review.Message); Assert.True(review.Value!.CanAccept, review.Value.Blocker);
    var result = await SourceAcceptanceWorkspace.AcceptGeneralLedgerAsync(db, actor, id, review.Value.Revision, "Synthetic independent native GL acceptance", true);
    Assert.True(result.Succeeded, result.Message);
  }

  private static async Task<NativeBehaviourFixture> CreateNativeGlFixtureAsync(PgTestSchema pg, bool withPosting = true)
  {
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer"); var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid periodId, chartId, openingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA, ServiceRoute = "BOOKKEEPING", Decision = "Accepted",
        Generation = 1, Rationale = "Synthetic native GL fixture, not live approval", EvaluationTemplateVersion = "TEST-NATIVE-GL",
        EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "AccountingReviewer", scope.ClientA, scope.EngagementA));
      await db.SaveChangesAsync();
      var profile = await ClientAccountingService.CreateProfileAsync(db, reviewer,
        new(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE-GL-TEST", ClientAccountingSourceModes.NativeBookkeeping));
      Assert.True(profile.Succeeded, profile.Message);
      var period = await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "IFRS", "QAR"));
      Assert.True(period.Succeeded, period.Message); periodId = period.Value;
      await db.ClientReportingPeriods.Where(x => x.Id == periodId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AccountingWorkflowStates.Active));
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "AUDITSPHERE", new DateOnly(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message); chartId = chart.Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, chartId,
        [new("cash", "1000", "Cash", "ASSET", "DEBIT", true), new("loan", "2000", "Loan", "LIABILITY", "CREDIT", true),
         new("capital", "3000", "Capital", "EQUITY", "CREDIT", true)])).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId)).Succeeded);
      var created = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, preparer,
        new(scope.ClientA, periodId, 1, new DateOnly(2026, 1, 1), "QAR", "Synthetic actual opening cash and capital",
          new string('c', 64), [new("1000", 100m, 0m), new("3000", 0m, 100m)]));
      Assert.True(created.Succeeded, created.Message); openingId = created.Value;
      var view = await ClientOperationalOpeningBalanceWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId);
      Assert.True(view.Succeeded, view.Message);
      Assert.True((await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, reviewer, scope.ClientA, openingId, 1, view.Value!.ManifestSha256)).Succeeded);
      var taxId = Guid.CreateVersion7();
      db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
      {
        Id = taxId, FirmId = scope.FirmId, Code = "native-gl-test", Framework = "IFRS", Name = "Synthetic native GL source test taxonomy",
        Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1), CreatedByUserId = scope.Preparer.Id,
        ApprovedByUserId = scope.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      foreach (var (code, section) in new[] { ("CASH", "ASSETS"), ("BORROWINGS", "LIABILITIES"), ("CAPITAL", "EQUITY") })
        db.ReportingTaxonomyNodes.Add(new ReportingTaxonomyNode
        {
          Id = Guid.CreateVersion7(), FirmId = scope.FirmId, TaxonomyVersionId = taxId, Code = code, Name = code,
          StatementSection = section, DisplaySign = "SIGNED", NormalBalance = section == "ASSETS" ? "DEBIT" : "CREDIT",
          IsPosting = true, Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }
    var fixture = new NativeBehaviourFixture(scope, periodId, chartId, openingId, Guid.Empty);
    return withPosting ? fixture with { JournalId = await PostNativeGlJournalAsync(pg, fixture, "NATIVE-LOAN", 20m, false) } : fixture;
  }

  private static async Task<Guid> PostNativeGlJournalAsync(PgTestSchema pg, NativeBehaviourFixture f, string number, decimal amount, bool offset)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer"); var reviewer = Actor(f.Scope.Reviewer, "AccountingReviewer");
    var created = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, preparer,
      new(f.Scope.ClientA, f.PeriodId, number, "Synthetic source-backed cash and loan movement", new DateOnly(2026, 1, 15),
        [new("1000", "Cash", offset ? 0m : amount, offset ? amount : 0m), new("2000", "Loan", offset ? amount : 0m, offset ? 0m : amount)]));
    Assert.True(created.Succeeded, created.Message);
    var preview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, f.Scope.ClientA, created.Value);
    Assert.True(preview.Succeeded, preview.Message);
    var submitted = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, f.Scope.ClientA, created.Value, 1, previewDigest: preview.Value!.Digest);
    Assert.True(submitted.Succeeded, submitted.Message);
    var review = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, f.Scope.ClientA, created.Value);
    Assert.True(review.Succeeded, review.Message);
    var posted = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, f.Scope.ClientA, created.Value,
      new(2, "APPROVE", "Synthetic independent native GL posting", review.Value!.Digest, Guid.CreateVersion7()));
    Assert.True(posted.Succeeded, posted.Message); return created.Value;
  }
}
