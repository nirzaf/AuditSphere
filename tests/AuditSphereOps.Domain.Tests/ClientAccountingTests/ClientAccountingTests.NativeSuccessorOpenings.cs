using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  private sealed record NativeSuccessorFixture(NativeBehaviourFixture Native, Guid SourceDatasetId,
    string SourceHash, Guid TargetPeriodId, NativeSuccessorOpeningRequest Request);

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorOpeningRetainsV1AndTwoGenerationsThenRefusesReopenedAncestor()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeSuccessorFixtureAsync(pg);
    var maker = Actor(f.Native.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Native.Scope.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var legacyBefore = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == f.Native.OpeningId);
    var sourceBefore = await NativeTrialBalanceSourceWorkspace.ArtifactAsync(db, maker, f.SourceDatasetId);
    Assert.True(sourceBefore.Succeeded, sourceBefore.Message);
    var preview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, f.Request);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.Equal(new[] { "120", "0", "0" }, preview.Value!.Lines.Select(x => x.Debit));
    Assert.Equal(2, preview.Value.Controls.Count);
    Assert.All(preview.Value.Controls, x => Assert.Equal(("0", "0"), (x.ClosingDebit, x.ClosingCredit)));
    var reviewed = f.Request with { ReviewBasis = preview.Value.ReviewBasis, Reviewed = true };
    var created = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker, reviewed);
    Assert.True(created.Succeeded, created.Message);
    var first = created.Value!;
    Assert.Equal(preview.Value.ProspectiveOpeningManifestHash, first.Opening.ManifestSha256);
    Assert.Null(first.Opening.ApprovedByUserId);
    using (var proof = JsonDocument.Parse(first.ProofJson))
    {
      Assert.Equal("native-opening-successor.v1", proof.RootElement.GetProperty("Version").GetString());
      Assert.Equal(f.SourceDatasetId, proof.RootElement.GetProperty("SourceDatasetId").GetGuid());
      Assert.Equal(1L, proof.RootElement.GetProperty("Facts").GetProperty("TargetRevision").GetInt64());
      Assert.False(proof.RootElement.GetProperty("Facts").TryGetProperty("CarriedPopulation", out _));
    }
    var replay = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker, reviewed);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(first.Opening.Id, replay.Value!.Opening.Id);
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker,
      reviewed with { EvidenceReference = "Changed original intent" })).ErrorCode);
    Assert.Equal(ErrorCodes.GateBlocked, (await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, reviewer,
      f.Native.Scope.ClientA, first.Opening.Id, 1, first.Opening.ManifestSha256)).ErrorCode);
    var approval = new NativeSuccessorOpeningApprovalRequest(Guid.CreateVersion7(), f.Native.Scope.ClientA,
      f.Native.Scope.EngagementA, first.Opening.Id, 1, first.Opening.ManifestSha256, first.ProofHash, true);
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientOperationalOpeningBalanceWorkspace.ApproveSuccessorAsync(db, maker, approval)).ErrorCode);
    Assert.Equal(ErrorCodes.GenerationStale, (await ClientOperationalOpeningBalanceWorkspace.ApproveSuccessorAsync(db, reviewer,
      approval with { ExpectedSuccessorProofHash = new string('a', 64) })).ErrorCode);
    Assert.True((await ClientOperationalOpeningBalanceWorkspace.ApproveSuccessorAsync(db, reviewer, approval)).Succeeded);
    var ledger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, maker, f.Native.Scope.ClientA, f.TargetPeriodId);
    Assert.True(ledger.Succeeded, ledger.Message);
    Assert.Equal("120", ledger.Value!.TrialBalance.OpeningDebit);
    Assert.Equal("0", ledger.Value.TrialBalance.PeriodDebit);
    Assert.Equal("120", ledger.Value.TrialBalance.ClosingDebit);
    var secondSource = await CaptureSuccessorFixtureSourceAsync(pg, f.Native with { PeriodId = f.TargetPeriodId }, 2027);
    // Real period close must preserve the original opening/source revision for the next generation.
    var bridge = await ClientAccountingService.CreateOpeningBalanceBridgeAsync(db, maker,
      new(f.Native.Scope.ClientA, f.TargetPeriodId, f.Native.PeriodId, null, f.SourceHash, 120m, 120m, "Synthetic successor bridge"));
    Assert.True(bridge.Succeeded, bridge.Message);
    Assert.True((await ClientAccountingService.ApproveOpeningBalanceBridgeAsync(db, reviewer, bridge.Value)).Succeeded);
    Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, f.TargetPeriodId, "Synthetic second period independently closed")).Succeeded);
    var stillCurrent = await NativeTrialBalanceSourceWorkspace.ViewAsync(db, maker, secondSource.DatasetId);
    Assert.True(stillCurrent.Succeeded, stillCurrent.Message);
    Assert.True(stillCurrent.Value!.IsCurrent, stillCurrent.Value.CurrentnessBlocker);
    var thirdPeriod = await SeedNativeSuccessorActivePeriodAsync(pg, f.Native, f.TargetPeriodId, 2028);
    var next = new NativeSuccessorOpeningRequest(Guid.CreateVersion7(), f.Native.Scope.ClientA, f.Native.Scope.EngagementA,
      thirdPeriod, 1, f.TargetPeriodId, 2, secondSource.DatasetId, secondSource.SourceIdentityHash, "Synthetic next successor evidence");
    var nextPreview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, next);
    Assert.True(nextPreview.Succeeded, nextPreview.Message);
    var nextOpening = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker,
      next with { ReviewBasis = nextPreview.Value!.ReviewBasis, Reviewed = true });
    Assert.True(nextOpening.Succeeded, nextOpening.Message);
    Assert.True((await ClientOperationalOpeningBalanceWorkspace.ApproveSuccessorAsync(db, reviewer,
      new(Guid.CreateVersion7(), f.Native.Scope.ClientA, f.Native.Scope.EngagementA, nextOpening.Value!.Opening.Id,
        1, nextOpening.Value.Opening.ManifestSha256, nextOpening.Value.ProofHash, true))).Succeeded);
    var thirdLedger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, maker, f.Native.Scope.ClientA, thirdPeriod);
    Assert.True(thirdLedger.Succeeded, thirdLedger.Message);
    Assert.Equal("120", thirdLedger.Value!.TrialBalance.ClosingDebit);
    // Reuse the seal scaffold: retained validations/text bytes are synthetic, while
    // predecessor close, native source acceptance and successor approval above are real commands.
    var packageId = await SeedNativeSealConcurrencyPackageAsync(pg,
      f.Native with { PeriodId = f.TargetPeriodId }, secondSource.DatasetId);
    var packageBefore = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
    var artifactBefore = await db.FinancialPackageArtifacts.AsNoTracking().Where(x => x.FinancialPackageId == packageId)
      .OrderBy(x => x.Id).Select(x => x.ArtifactSha256Hex).ToArrayAsync();
    var readyBefore = await FinancialPackageValidationReadiness.GetAsync(db, maker.FirmId, packageId);
    Assert.True(readyBefore.Ready);
    var sealBefore = await PackageSealService.GetSealStatusAsync(db, maker, packageId);
    Assert.True(sealBefore.Succeeded, sealBefore.Message);
    Assert.True(sealBefore.Value!.CanSeal);
    var legacyAfter = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == f.Native.OpeningId);
    Assert.Equal(legacyBefore.ManifestJson, legacyAfter.ManifestJson);
    Assert.Equal(legacyBefore.ManifestSha256, legacyAfter.ManifestSha256);
    var sourceAfter = await NativeTrialBalanceSourceWorkspace.ArtifactAsync(db, maker, f.SourceDatasetId);
    Assert.True(sourceAfter.Succeeded, sourceAfter.Message);
    Assert.Equal(sourceBefore.Value!.ManifestJson, sourceAfter.Value!.ManifestJson);
    Assert.Equal(sourceBefore.Value.SourceIdentityHash, sourceAfter.Value.SourceIdentityHash);
    Assert.True(sourceAfter.Value.IsCurrent, sourceAfter.Value.CurrentnessBlocker);
    var scopedReopen = await ClientAccountingService.ReopenPeriodAsync(db, Actor(f.Native.Scope.Partner, "Partner"),
      f.Native.PeriodId, "Synthetic ancestor reopen", expectedRevision: 2);
    Assert.Equal(ErrorCodes.ScopeDenied, scopedReopen.ErrorCode); // An engagement grant cannot reopen client books.
    var reopened = await ClientAccountingService.ReopenPeriodAsync(db, Actor(f.Native.Scope.Reviewer, "Partner"),
      f.Native.PeriodId, "Synthetic authorized ancestor reopen", expectedRevision: 2);
    Assert.True(reopened.Succeeded, reopened.Message);
    var historical = await ClientOperationalOpeningBalanceWorkspace.GetSuccessorAsync(db, maker, f.Native.Scope.ClientA, nextOpening.Value.Opening.Id);
    Assert.True(historical.Succeeded, historical.Message);
    Assert.False(historical.Value!.IsCurrent);
    Assert.False((await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, maker, f.Native.Scope.ClientA, thirdPeriod)).Succeeded);
    Assert.False((await NativeTrialBalanceSourceWorkspace.ViewAsync(db, maker, secondSource.DatasetId)).Value!.IsCurrent);
    var staleReadiness = await FinancialPackageValidationReadiness.GetAsync(db, maker.FirmId, packageId);
    Assert.False(staleReadiness.Ready);
    Assert.Contains("NATIVE_SOURCE_CURRENT", staleReadiness.Failed);
    var staleSeal = await PackageSealService.GetSealStatusAsync(db, maker, packageId);
    Assert.True(staleSeal.Succeeded, staleSeal.Message);
    Assert.False(staleSeal.Value!.CanSeal);
    Assert.Equal(ErrorCodes.GateBlocked, (await PackageSealService.SealPackageAsync(db, maker, packageId)).ErrorCode);
    Assert.False(await db.FinancialPackageSeals.AnyAsync(x => x.FinancialPackageId == packageId));
    Assert.Equal(packageBefore.CalculationHash, (await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId)).CalculationHash);
    Assert.Equal(artifactBefore, await db.FinancialPackageArtifacts.AsNoTracking().Where(x => x.FinancialPackageId == packageId)
      .OrderBy(x => x.Id).Select(x => x.ArtifactSha256Hex).ToArrayAsync());
  }

  [Theory]
  [InlineData(false, false, false, "AR and AP")]
  [InlineData(true, true, false, "control balance")]
  [InlineData(true, false, true, "P&L")]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorOpeningRefusesMissingControlsNonzeroControlsAndOffsettingProfitAccounts(
    bool controls, bool nonzeroControl, bool nonzeroProfitAndLoss, string expected)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeSuccessorFixtureAsync(pg, controls, nonzeroControl, nonzeroProfitAndLoss);
    await using var db = new AuditSphereDbContext(pg.Options);
    var maker = Actor(f.Native.Scope.Preparer, "AccountingPreparer");
    var preview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, f.Request);
    Assert.False(preview.Succeeded);
    Assert.Contains(expected, preview.Message!, StringComparison.OrdinalIgnoreCase);
    Assert.Empty(await db.ClientOperationalOpeningSuccessorProofs.ToListAsync());
    Assert.False((await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker,
      f.Request with { ClientId = f.Native.Scope.ClientB })).Succeeded);
  }

  private static async Task<NativeSuccessorFixture> CreateNativeSuccessorFixtureAsync(PgTestSchema pg,
    bool controls = true, bool nonzeroControl = false, bool nonzeroProfitAndLoss = false)
  {
    var native = await CreateNativeBehaviourFixtureAsync(pg, successorAccounts: true, openingControlBalance: nonzeroControl);
    var maker = Actor(native.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(native.Scope.Reviewer, "AccountingReviewer");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      if (controls)
      {
        foreach (var (role, code) in new[] { ("AR", "1100"), ("AP", "2100") })
        {
          var accountId = await db.ClientAccounts.Where(x => x.ChartVersionId == native.ChartId && x.AccountCode == code).Select(x => x.Id).SingleAsync();
          var proposal = await ClientAccountRoleWorkspace.ProposeAsync(db, maker, native.Scope.ClientA,
            new(native.ChartId, accountId, role, new DateOnly(2026, 1, 1), null, "Synthetic successor control"));
          Assert.True(proposal.Succeeded, proposal.Message);
          var approved = await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, native.Scope.ClientA,
            proposal.Value, "APPROVE", "Synthetic independent control review");
          Assert.True(approved.Succeeded, approved.Message);
        }
      }
      if (nonzeroProfitAndLoss)
      {
        var journal = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, maker,
          new(native.Scope.ClientA, native.PeriodId, "OFFSET-PROFIT", "Equal nonzero expense and income",
            new DateOnly(2026, 2, 1), [new("5000", "Expense", 10m, 0m), new("4000", "Income", 0m, 10m)]));
        Assert.True(journal.Succeeded, journal.Message);
        var preview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, maker, native.Scope.ClientA, journal.Value);
        Assert.True((await ClientOperationalLedgerWorkspace.SubmitAsync(db, maker, native.Scope.ClientA, journal.Value,
          1, previewDigest: preview.Value!.Digest)).Succeeded);
        var review = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, native.Scope.ClientA, journal.Value);
        Assert.True((await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, native.Scope.ClientA, journal.Value,
          new(2, "APPROVE", "Synthetic independent offsetting P&L review", review.Value!.Digest, Guid.CreateVersion7()))).Succeeded);
      }
    }
    var source = await CaptureSuccessorFixtureSourceAsync(pg, native, 2026);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, native.PeriodId, "Synthetic exact source closed")).Succeeded);
    var target = await SeedNativeSuccessorActivePeriodAsync(pg, native, native.PeriodId, 2027);
    return new(native, source.DatasetId, source.SourceIdentityHash, target,
      new(Guid.CreateVersion7(), native.Scope.ClientA, native.Scope.EngagementA, target, 1, native.PeriodId, 2,
        source.DatasetId, source.SourceIdentityHash, "Synthetic retained closed native source"));
  }

  private static async Task<NativeTrialBalanceSourceView> CaptureSuccessorFixtureSourceAsync(PgTestSchema pg,
    NativeBehaviourFixture native, int year)
  {
    Guid datasetId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var maker = Actor(native.Scope.Preparer, "AccountingPreparer");
      var request = NativeBehaviourRequest(native) with { AsOfDate = new DateOnly(year, 12, 31) };
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, maker, request);
      Assert.True(preview.Succeeded, preview.Message);
      var sealedSource = await NativeTrialBalanceSourceWorkspace.SealAsync(db, maker,
        request with { ReviewBasis = preview.Value!.ReviewBasis, Reviewed = true });
      Assert.True(sealedSource.Succeeded, sealedSource.Message); datasetId = sealedSource.Value!.DatasetId;
    }
    await ValidateNativeBehaviourSourceAsync(pg, native.Scope.FirmId, datasetId);
    await AcceptNativeBehaviourSourceAsync(pg, native, datasetId);
    await using var verify = new AuditSphereDbContext(pg.Options);
    return (await NativeTrialBalanceSourceWorkspace.ViewAsync(verify, Actor(native.Scope.Preparer, "AccountingPreparer"), datasetId)).Value!;
  }

  private static async Task<Guid> SeedNativeSuccessorActivePeriodAsync(PgTestSchema pg, NativeBehaviourFixture native,
    Guid priorPeriodId, int year)
  {
    // Explicit synthetic pre-existing ACTIVE context. This does not activate a publicly created DRAFT period.
    await using var db = new AuditSphereDbContext(pg.Options);
    var id = Guid.CreateVersion7();
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = id, FirmId = native.Scope.FirmId, ClientId = native.Scope.ClientA, PeriodCode = year.ToString(),
      StartDate = new(year, 1, 1), EndDate = new(year, 12, 31), Basis = "IFRS", Currency = "QAR",
      Status = AccountingWorkflowStates.Active, PriorPeriodId = priorPeriodId, Revision = 1,
      CreatedByUserId = native.Scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(); return id;
  }
}
