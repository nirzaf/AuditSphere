using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NativeSourceWorker = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  private sealed record NativeBehaviourFixture(Scope Scope, Guid PeriodId, Guid ChartId, Guid? OpeningId, Guid JournalId);

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSourceUsesApprovedOpeningAndPostedJournalThenRequiresValidationAndIndependentAcceptance()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeBehaviourFixtureAsync(pg);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Scope.Reviewer, "AccountingReviewer");
    var request = NativeBehaviourRequest(f);
    NativeTrialBalanceSourceView sealedSource;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.Equal("PERIOD_LEDGER_NO_BOOK", preview.Value!.BookScope);
      Assert.False(preview.Value.ActivityOnly);
      Assert.Equal(3, preview.Value.RowCount);
      Assert.Equal("100", preview.Value.TrialBalance.OpeningDebit);
      Assert.Equal("100", preview.Value.TrialBalance.OpeningCredit);
      Assert.Equal("20", preview.Value.TrialBalance.PeriodDebit);
      Assert.Equal("20", preview.Value.TrialBalance.PeriodCredit);
      Assert.Equal("120", preview.Value.TrialBalance.ClosingDebit);
      Assert.Equal("120", preview.Value.TrialBalance.ClosingCredit);
      Assert.Empty(await db.TrialBalanceDatasets.ToListAsync());
      Assert.Empty(await db.SourceAcceptanceDecisions.ToListAsync());

      var unreviewed = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer, request);
      Assert.False(unreviewed.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, unreviewed.ErrorCode);
      var result = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer,
        request with { ReviewBasis = preview.Value.ReviewBasis, Reviewed = true });
      Assert.True(result.Succeeded, result.Message);
      sealedSource = result.Value!;
      Assert.Equal("Pending", sealedSource.ValidationStatus);
      Assert.True(sealedSource.IsCurrent, sealedSource.CurrentnessBlocker);
      Assert.Equal(AccountingWorkflowStates.Active,
        (await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == f.PeriodId)).Status);
      Assert.Empty(await db.FinancialPackages.ToListAsync()); // No close/package prerequisite for source capture.
      var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == sealedSource.DatasetId);
      Assert.Equal("Native", dataset.SourceKind);
      Assert.Equal("", dataset.RawFileSha256Hex);
      Assert.Null(dataset.BookId);
      Assert.Null(dataset.ImportBatchId);
      Assert.Equal(sealedSource.SourceIdentityHash, dataset.Sha256Hex);
      Assert.Equal("native-period-ledger.v1", dataset.ImportProfileVersion);
      Assert.Equal(TrialBalanceLayouts.DebitCredit, dataset.SourceLayout);
      var rows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == dataset.Id)
        .OrderBy(x => x.AccountCode).ToListAsync();
      Assert.Equal(new[] { 120m, -20m, -100m }, rows.Select(x => x.Amount));
      Assert.All(rows, x => Assert.Equal(x.SourceDebit - x.SourceCredit, x.Amount));
      var pending = await SourceAcceptanceWorkspace.GetAsync(db, reviewer, dataset.Id);
      Assert.True(pending.Succeeded, pending.Message);
      Assert.False(pending.Value!.CanAccept);

      var artifact = await NativeTrialBalanceSourceWorkspace.ArtifactAsync(db, reviewer, dataset.Id);
      Assert.True(artifact.Succeeded, artifact.Message);
      Assert.Equal(Hashing.Sha256Hex(artifact.Value!.GeneratedRowsJson), artifact.Value.GeneratedRowsHash);
      Assert.Equal(Hashing.Sha256Hex(artifact.Value.ManifestJson), artifact.Value.ManifestHash);
      // Recoverable membership, rather than only an opaque digest of a transient query.
      Assert.Contains(f.JournalId.ToString("D"), artifact.Value.ManifestJson);
      Assert.Contains(f.OpeningId!.Value.ToString("D"), artifact.Value.ManifestJson);
      using var manifest = System.Text.Json.JsonDocument.Parse(artifact.Value.ManifestJson);
      var membership = manifest.RootElement.GetProperty("Pins").GetProperty("MembershipJson").GetString()!;
      var submitted = await db.ClientOperationalJournalSnapshots.AsNoTracking().SingleAsync(x => x.JournalId == f.JournalId);
      Assert.Contains(submitted.Id.ToString("D"), membership);
      Assert.Contains(Hashing.Sha256Hex(submitted.SnapshotJson), membership);
    }

    await ValidateNativeBehaviourSourceAsync(pg, f.Scope.FirmId, sealedSource.DatasetId);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var beforeAcceptance = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        await NativeBehaviourMappingAsync(db, f, sealedSource.DatasetId));
      Assert.False(beforeAcceptance.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, beforeAcceptance.ErrorCode);
      Assert.Empty(await db.MappingVersions.ToListAsync());
      var self = await SourceAcceptanceWorkspace.GetAsync(db, preparer, sealedSource.DatasetId);
      Assert.True(self.Succeeded, self.Message);
      Assert.False(self.Value!.CanAccept);
      var denied = await SourceAcceptanceWorkspace.AcceptAsync(db, preparer, sealedSource.DatasetId,
        self.Value.Revision, "Synthetic self-review must be refused", true);
      Assert.False(denied.Succeeded);
      Assert.Empty(await db.SourceAcceptanceDecisions.ToListAsync());
    }
    await AcceptNativeBehaviourSourceAsync(pg, f, sealedSource.DatasetId);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var selected = await AccountingSourceAcceptanceService.GetSelectedTrialBalanceForPeriodAsync(db, reviewer,
        f.Scope.ClientA, f.Scope.EngagementA, f.PeriodId, null, "IFRS", "QAR", request.LegalEntityKey);
      Assert.True(selected.Succeeded, selected.Message);
      Assert.Equal(sealedSource.DatasetId, selected.Value!.TrialBalanceDatasetId);
      Assert.Equal(sealedSource.SourceIdentityHash, selected.Value.SourceIdentityHash);
      Assert.True(selected.Value.IsCurrent, selected.Value.CurrentnessBlocker);
      Assert.Equal(f.Scope.Reviewer.Id, selected.Value.AcceptedByUserId);
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        await NativeBehaviourMappingAsync(db, f, sealedSource.DatasetId));
      Assert.True(mapping.Succeeded, mapping.Message);
      Assert.Equal(sealedSource.DatasetId,
        (await db.MappingVersions.AsNoTracking().SingleAsync(x => x.Id == mapping.Value)).DatasetId);
    }
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSourceExactRetryConflictsWithChangedIntentAndRefusesSiblingScopeOrWithdrawnMandate()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeBehaviourFixtureAsync(pg);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    var request = NativeBehaviourRequest(f);
    NativeTrialBalanceSourceArtifact original;
    Guid datasetId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingUser = User(f.Scope.FirmId, "native-source-sibling");
      db.Users.Add(siblingUser);
      db.RoleGrants.Add(Grant(f.Scope.FirmId, siblingUser, "AccountingPreparer", f.Scope.ClientB, f.Scope.EngagementB));
      await db.SaveChangesAsync();
      var siblingActor = Actor(siblingUser, "AccountingPreparer");
      var siblingDenied = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, siblingActor, request);
      Assert.False(siblingDenied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, siblingDenied.ErrorCode);
      var sibling = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer,
        request with { ClientId = f.Scope.ClientB });
      Assert.False(sibling.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, sibling.ErrorCode);
      var implicitScope = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer,
        request with { PeriodLedgerReviewed = false });
      Assert.False(implicitScope.Succeeded);
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(preview.Succeeded, preview.Message);
      request = request with { ReviewBasis = preview.Value!.ReviewBasis, Reviewed = true };
      var sealedSource = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer, request);
      Assert.True(sealedSource.Succeeded, sealedSource.Message);
      datasetId = sealedSource.Value!.DatasetId;
      var replay = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer, request);
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(datasetId, replay.Value!.DatasetId);
      var conflict = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer,
        request with { EvidenceReference = "Different synthetic capture intent" });
      Assert.False(conflict.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
      var foreignRead = await NativeTrialBalanceSourceWorkspace.ArtifactAsync(db, siblingActor, datasetId);
      Assert.False(foreignRead.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, foreignRead.ErrorCode);
      Assert.Null(foreignRead.Value);
      var newRequest = request with { RequestId = Guid.CreateVersion7(), Reviewed = false, ReviewBasis = null };
      var newPreview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, newRequest);
      Assert.True(newPreview.Succeeded, newPreview.Message);
      var sourceDuplicate = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer,
        newRequest with { Reviewed = true, ReviewBasis = newPreview.Value!.ReviewBasis });
      Assert.False(sourceDuplicate.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ImportDuplicate, sourceDuplicate.ErrorCode);
      Assert.Equal(1, await db.NativeTrialBalanceSources.CountAsync());
      Assert.Equal(1, await db.TrialBalanceDatasets.CountAsync());
      original = (await NativeTrialBalanceSourceWorkspace.ArtifactAsync(db, preparer, datasetId)).Value!;
      Assert.NotNull(original);
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = f.Scope.FirmId, PracticeClientId = f.Scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Declined", Generation = 2,
        Rationale = "Synthetic mandate withdrawal fixture", EvaluationTemplateVersion = "TEST-NATIVE-SOURCE",
        EvaluationSnapshotDigest = new string('b', 64), DecidedByUserId = f.Scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var noMandate = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer,
        request with { RequestId = Guid.CreateVersion7(), ReviewBasis = null, Reviewed = false });
      Assert.False(noMandate.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, noMandate.ErrorCode);
      var retained = await NativeTrialBalanceSourceWorkspace.ArtifactAsync(db, preparer, datasetId);
      Assert.True(retained.Succeeded, retained.Message);
      Assert.False(retained.Value!.IsCurrent);
      Assert.Equal(original.GeneratedRowsJson, retained.Value.GeneratedRowsJson);
      Assert.Equal(original.ManifestJson, retained.Value.ManifestJson);
    }
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSourceBackdatedOffsettingPostingsStaleAcceptedSourceEvenWhenClosingRowsAreUnchanged()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeBehaviourFixtureAsync(pg);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Scope.Reviewer, "AccountingReviewer");
    NativeTrialBalanceSourceView original;
    string originalRowDigest;
    string originalArtifact;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = NativeBehaviourRequest(f);
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(preview.Succeeded, preview.Message);
      var sealedSource = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer,
        request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
      Assert.True(sealedSource.Succeeded, sealedSource.Message);
      original = sealedSource.Value!;
      originalRowDigest = (await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == original.DatasetId)).NormalizedDatasetDigest;
      originalArtifact = (await NativeTrialBalanceSourceWorkspace.ArtifactAsync(db, preparer, original.DatasetId)).Value!.GeneratedRowsJson;
    }
    await ValidateNativeBehaviourSourceAsync(pg, f.Scope.FirmId, original.DatasetId);
    await AcceptNativeBehaviourSourceAsync(pg, f, original.DatasetId);
    await PostNativeBehaviourJournalAsync(pg, f, "LATER-BACKDATED-1", 25m, false);
    await PostNativeBehaviourJournalAsync(pg, f, "LATER-BACKDATED-2", 25m, true);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var stale = await NativeTrialBalanceSourceWorkspace.ViewAsync(db, reviewer, original.DatasetId);
      Assert.True(stale.Succeeded, stale.Message);
      Assert.False(stale.Value!.IsCurrent);
      var selected = await AccountingSourceAcceptanceService.GetSelectedTrialBalanceForPeriodAsync(db, reviewer,
        f.Scope.ClientA, f.Scope.EngagementA, f.PeriodId, null, "IFRS", "QAR", "NATIVE-TEST-ENTITY");
      Assert.True(selected.Succeeded, selected.Message);
      Assert.Equal(original.DatasetId, selected.Value!.TrialBalanceDatasetId);
      Assert.False(selected.Value.IsCurrent);
      var blocked = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        await NativeBehaviourMappingAsync(db, f, original.DatasetId));
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, blocked.ErrorCode);
      var history = await NativeTrialBalanceSourceWorkspace.ArtifactAsync(db, reviewer, original.DatasetId);
      Assert.True(history.Succeeded, history.Message);
      Assert.Equal(originalArtifact, history.Value!.GeneratedRowsJson);
      var request = NativeBehaviourRequest(f);
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.NotEqual(original.SourceIdentityHash, preview.Value!.SourceIdentityHash);
      Assert.Equal("120", preview.Value.TrialBalance.ClosingDebit);
      Assert.Equal("120", preview.Value.TrialBalance.ClosingCredit);
      var successor = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer,
        request with { Reviewed = true, ReviewBasis = preview.Value.ReviewBasis });
      Assert.True(successor.Succeeded, successor.Message);
      Assert.NotEqual(original.DatasetId, successor.Value!.DatasetId);
      var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == successor.Value.DatasetId);
      Assert.Equal(originalRowDigest, dataset.NormalizedDatasetDigest);
      Assert.NotEqual(original.SourceIdentityHash, dataset.Sha256Hex);
      Assert.Equal(2, await db.NativeTrialBalanceSources.CountAsync());
    }
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSourceActivityOnlyNeedsExplicitSelectionAndCannotBecomeAcceptedMappingOrFullReport()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeBehaviourFixtureAsync(pg, withOpening: false);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Scope.Reviewer, "AccountingReviewer");
    Guid datasetId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = NativeBehaviourRequest(f);
      var refused = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
      request = request with { AllowActivityOnly = true };
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.True(preview.Value!.ActivityOnly);
      Assert.Equal("NATIVE_POSTED_PERIOD_ACTIVITY", preview.Value.TrialBalance.Source);
      var sealedSource = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer,
        request with { Reviewed = true, ReviewBasis = preview.Value.ReviewBasis });
      Assert.True(sealedSource.Succeeded, sealedSource.Message);
      datasetId = sealedSource.Value!.DatasetId;
    }
    await ValidateNativeBehaviourSourceAsync(pg, f.Scope.FirmId, datasetId);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var review = await SourceAcceptanceWorkspace.GetAsync(db, reviewer, datasetId);
      Assert.True(review.Succeeded, review.Message);
      Assert.False(review.Value!.CanAccept);
      var accepted = await SourceAcceptanceWorkspace.AcceptAsync(db, reviewer, datasetId,
        review.Value.Revision, "Synthetic incomplete-opening review", true);
      Assert.False(accepted.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, accepted.ErrorCode);
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        await NativeBehaviourMappingAsync(db, f, datasetId));
      Assert.False(mapping.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, mapping.ErrorCode);
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, datasetId, []);
      Assert.False(plan.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, plan.ErrorCode);
      Assert.Empty(await db.SourceAcceptanceDecisions.ToListAsync());
      Assert.Empty(await db.MappingVersions.ToListAsync());
      Assert.Empty(await db.AdjustmentPlans.ToListAsync());
      Assert.Empty(await db.FinancialPackages.ToListAsync());
    }
  }

  private static NativeTrialBalanceSourceRequest NativeBehaviourRequest(NativeBehaviourFixture f) =>
    new(Guid.CreateVersion7(), f.Scope.ClientA, f.Scope.EngagementA, f.PeriodId, 1, new DateOnly(2026, 12, 31),
      "NATIVE-TEST-ENTITY", true, "Synthetic native ledger source evidence");

  private static async Task<NativeBehaviourFixture> CreateNativeBehaviourFixtureAsync(PgTestSchema pg, bool withOpening = true, bool successorAccounts = false, bool openingControlBalance = false)
  {
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid periodId, chartId;
    Guid? openingId = null;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Synthetic native-source mandate fixture, not a real client approval",
        EvaluationTemplateVersion = "TEST-NATIVE-SOURCE", EvaluationSnapshotDigest = new string('a', 64),
        DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      // Give the maker reviewer authority too: self-acceptance must fail on independence,
      // rather than merely because the maker lacks the reviewer role.
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "AccountingReviewer", scope.ClientA, scope.EngagementA));
      await db.SaveChangesAsync();
      var profile = await ClientAccountingService.CreateProfileAsync(db, reviewer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE-SOURCE-TEST",
          ClientAccountingSourceModes.NativeBookkeeping));
      Assert.True(profile.Succeeded, profile.Message);
      var period = await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "IFRS", "QAR"));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
      // The existing intake contract selects an ACTIVE period; no package validation/approval is seeded.
      await db.ClientReportingPeriods.Where(x => x.Id == periodId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AccountingWorkflowStates.Active));
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "AUDITSPHERE", new DateOnly(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message);
      chartId = chart.Value;
      var accounts = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId,
      [
        new("property", "1000", "Equipment", "ASSET", "DEBIT", true),
        new("loan", "2000", "Loan", "LIABILITY", "CREDIT", true),
        new("capital", "3000", "Capital", "EQUITY", "CREDIT", true)
      ]);
      Assert.True(accounts.Succeeded, accounts.Message);
      if (successorAccounts)
      {
        var controls = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId,
        [new("receivable", "1100", "Receivable control", "ASSET", "DEBIT", true),
         new("payable", "2100", "Payable control", "LIABILITY", "CREDIT", true),
         new("income", "4000", "Income", "INCOME", "CREDIT", true),
         new("expense", "5000", "Expense", "EXPENSE", "DEBIT", true)]);
        Assert.True(controls.Succeeded, controls.Message);
      }
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId)).Succeeded);
      if (withOpening)
      {
        var opening = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, preparer,
          new(scope.ClientA, periodId, 1, new DateOnly(2026, 1, 1), "QAR", "Synthetic approved opening source",
            new string('c', 64), openingControlBalance
              ? [new("1000", 100m, 0m), new("1100", 10m, 0m), new("3000", 0m, 110m)]
              : [new("1000", 100m, 0m), new("3000", 0m, 100m)]));
        Assert.True(opening.Succeeded, opening.Message);
        openingId = opening.Value;
        var retained = await ClientOperationalOpeningBalanceWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId);
        Assert.True(retained.Succeeded, retained.Message);
        var approved = await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, reviewer, scope.ClientA,
          opening.Value, 1, retained.Value!.ManifestSha256);
        Assert.True(approved.Succeeded, approved.Message);
      }
      // Static synthetic taxonomy configuration, using the same fixture pattern as CurrencyFixtures.
      var taxonomyId = Guid.CreateVersion7();
      db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
      {
        Id = taxonomyId, FirmId = scope.FirmId, Code = "native-source-test", Framework = "IFRS", Name = "Synthetic source mapping",
        Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
        CreatedByUserId = scope.Preparer.Id, ApprovedByUserId = scope.Reviewer.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      foreach (var (code, section) in new[] { ("PROPERTY", "ASSETS"), ("BORROWINGS", "LIABILITIES"), ("CAPITAL", "EQUITY") })
        db.ReportingTaxonomyNodes.Add(new ReportingTaxonomyNode
        {
          Id = Guid.CreateVersion7(), FirmId = scope.FirmId, TaxonomyVersionId = taxonomyId,
          Code = code, Name = code, StatementSection = section, DisplaySign = "SIGNED",
          NormalBalance = section == "ASSETS" ? "DEBIT" : "CREDIT", IsPosting = true,
          Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }
    var f = new NativeBehaviourFixture(scope, periodId, chartId, openingId, Guid.Empty);
    return f with { JournalId = await PostNativeBehaviourJournalAsync(pg, f, "INITIAL-NATIVE", 20m, false) };
  }

  private static async Task<Guid> PostNativeBehaviourJournalAsync(PgTestSchema pg, NativeBehaviourFixture f,
    string number, decimal amount, bool offset)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Scope.Reviewer, "AccountingReviewer");
    var created = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, preparer,
      new ClientOperationalJournalCreateRequest(f.Scope.ClientA, f.PeriodId, number, "Synthetic supported native movement",
        new DateOnly(2026, 1, 15),
        [new("1000", "Equipment", offset ? 0m : amount, offset ? amount : 0m),
         new("2000", "Loan", offset ? amount : 0m, offset ? 0m : amount)]));
    Assert.True(created.Succeeded, created.Message);
    var preview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, f.Scope.ClientA, created.Value);
    Assert.True(preview.Succeeded, preview.Message);
    var submitted = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, f.Scope.ClientA, created.Value,
      1, previewDigest: preview.Value!.Digest);
    Assert.True(submitted.Succeeded, submitted.Message);
    var submittedPreview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, f.Scope.ClientA, created.Value);
    Assert.True(submittedPreview.Succeeded, submittedPreview.Message);
    var posted = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, f.Scope.ClientA, created.Value,
      new(2, "APPROVE", "Synthetic independent source posting review", submittedPreview.Value!.Digest, Guid.CreateVersion7()));
    Assert.True(posted.Succeeded, posted.Message);
    return created.Value;
  }

  private static async Task ValidateNativeBehaviourSourceAsync(PgTestSchema pg, Guid firmId, Guid datasetId)
  {
    var factory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(firmId, "Test");
    var handler = new TrialBalanceValidationHandler();
    var worker = new NativeSourceWorker(new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new TrialBalanceDiscovery(factory, store, handler, options)], NullLogger<NativeSourceWorker>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    await using var verify = new AuditSphereDbContext(pg.Options);
    var source = await verify.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == datasetId);
    Assert.Equal("Accepted", source.ValidationStatus);
    Assert.True(source.Balanced);
    Assert.Equal(0m, source.ControlTotal);
  }

  private static async Task AcceptNativeBehaviourSourceAsync(PgTestSchema pg, NativeBehaviourFixture f, Guid id)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var reviewer = Actor(f.Scope.Reviewer, "AccountingReviewer");
    var review = await SourceAcceptanceWorkspace.GetAsync(db, reviewer, id);
    Assert.True(review.Succeeded, review.Message);
    Assert.True(review.Value!.CanAccept, review.Value.Blocker);
    var accepted = await SourceAcceptanceWorkspace.AcceptAsync(db, reviewer, id, review.Value.Revision,
      "Synthetic independent native-source acceptance", true);
    Assert.True(accepted.Succeeded, accepted.Message);
  }

  private static async Task<CreateMappingVersionRequest> NativeBehaviourMappingAsync(AuditSphereDbContext db,
    NativeBehaviourFixture f, Guid datasetId)
  {
    var codes = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == datasetId)
      .OrderBy(x => x.AccountCode).Select(x => x.AccountCode).ToListAsync();
    return new(datasetId, "native-source-test", "2026-01-01", "2026-12-31", codes.Select(code =>
      new MappingAllocationInput(code, code == "1000" ? "PROPERTY" : code == "2000" ? "BORROWINGS" : "CAPITAL",
        code == "1000" ? "ASSETS" : code == "2000" ? "LIABILITIES" : "EQUITY", 1m, "Synthetic exact native source allocation")).ToArray(), f.ChartId);
  }
}
