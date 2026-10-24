using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorPackageSealSerializesAncestorReopenOrRefusesStalePublication()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeSuccessorFixtureAsync(pg);
    var maker = Actor(f.Native.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Native.Scope.Reviewer, "AccountingReviewer");
    // The existing fixture reviewer has client-book authority; its engagement-only
    // Partner actor does not. Use the same official reopen actor as the two-generation test.
    var reopenActor = Actor(f.Native.Scope.Reviewer, "Partner");
    Guid openingId;
    string openingHash;
    string proofHash;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, f.Request);
      Assert.True(preview.Succeeded, preview.Message);
      var created = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker,
        f.Request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
      Assert.True(created.Succeeded, created.Message);
      openingId = created.Value!.Opening.Id;
      openingHash = created.Value.Opening.ManifestSha256;
      proofHash = created.Value.ProofHash;
      var approved = await ClientOperationalOpeningBalanceWorkspace.ApproveSuccessorAsync(db, reviewer,
        new(Guid.CreateVersion7(), f.Native.Scope.ClientA, f.Native.Scope.EngagementA, openingId,
          1, openingHash, proofHash, true));
      Assert.True(approved.Succeeded, approved.Message);
    }
    var descendant = f.Native with { PeriodId = f.TargetPeriodId, OpeningId = openingId };
    var source = await CaptureSuccessorFixtureSourceAsync(pg, descendant, 2027);
    // Only package validation/artifact scaffolding is synthetic. Opening approval,
    // native source validation/acceptance and ancestor reopening use actual commands.
    // The scaffold does not represent rendered reports or professional report approval.
    var packageId = await SeedNativeSealConcurrencyPackageAsync(pg, descendant, source.DatasetId);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var status = await PackageSealService.GetSealStatusAsync(db, maker, packageId);
      Assert.True(status.Succeeded, status.Message);
      Assert.True(status.Value!.CanSeal);
      Assert.False(status.Value.IsSealed);
      Assert.Empty(status.Value.MissingRequiredFormats);
      Assert.False(await db.FinancialPackageSeals.AnyAsync(x => x.FinancialPackageId == packageId));
    }

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var pause = new PauseNativeSealExistingLookup();
    await using var sealDb = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var reopenDb = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    Task<CommandResult<PackageSealValue>>? sealTask = null;
    Task<CommandResult>? reopenTask = null;
    try
    {
      await sealDb.Database.OpenConnectionAsync(timeout.Token);
      await reopenDb.Database.OpenConnectionAsync(timeout.Token);
      var sealPid = ((NpgsqlConnection)sealDb.Database.GetDbConnection()).ProcessID;
      var reopenPid = ((NpgsqlConnection)reopenDb.Database.GetDbConnection()).ProcessID;
      sealTask = PackageSealService.SealPackageAsync(sealDb, maker, packageId, timeout.Token);
      // This existing interceptor stops after source readiness but before seal publication.
      await pause.Reached.Task.WaitAsync(timeout.Token);
      reopenTask = ClientAccountingService.ReopenPeriodAsync(reopenDb, reopenActor, f.Native.PeriodId,
        "Synthetic concurrent ancestor reopen", timeout.Token, expectedRevision: 2,
        expectedClientId: f.Native.Scope.ClientA);

      var blockedBySeal = false;
      var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
      while (!reopenTask.IsCompleted && !blockedBySeal && DateTimeOffset.UtcNow < deadline)
      {
        blockedBySeal = await observer.Database.SqlQuery<bool>(
          $"SELECT {sealPid} = ANY(pg_blocking_pids({reopenPid})) AS \"Value\"").SingleAsync(timeout.Token);
        // Bounded lock-state polling, not a sleep intended to guess which command won.
        if (!reopenTask.IsCompleted && !blockedBySeal) await Task.Delay(20, timeout.Token);
      }
      Assert.True(blockedBySeal || reopenTask.IsCompleted,
        "Observe the actual ancestor-reopen blocker or a completed official reopen before releasing seal publication.");
      var reopenedBeforeResume = reopenTask.IsCompleted;
      if (reopenedBeforeResume)
      {
        var reopened = await reopenTask;
        Assert.True(reopened.Succeeded, reopened.Message);
        Assert.True(await observer.ClientReportingPeriods.AsNoTracking().AnyAsync(x => x.Id == f.Native.PeriodId &&
          x.Status == "DRAFT" && x.Revision == 3 && x.ClosedByUserId == null && x.ClosedAt == null, timeout.Token));
        Assert.True(await observer.ClientPeriodAmendments.AsNoTracking().AnyAsync(x => x.PeriodId == f.Native.PeriodId &&
          x.PreviousRevision == 2 && x.AmendmentRevision == 3 && x.CreatedByUserId == reopenActor.UserId, timeout.Token));
      }

      pause.Release.TrySetResult(true);
      var sealedResult = await sealTask.WaitAsync(timeout.Token);
      var reopenedResult = await reopenTask.WaitAsync(timeout.Token);
      Assert.True(reopenedResult.Succeeded, reopenedResult.Message);
      var seals = await observer.FinancialPackageSeals.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId).ToArrayAsync(timeout.Token);
      if (reopenedBeforeResume)
      {
        Assert.False(sealedResult.Succeeded,
          "No new descendant seal may publish after its official ancestor reopen committed while sealing was paused.");
        Assert.Empty(seals);
      }
      else
      {
        // Serialization is valid: retain the seal that preceded reopening, then deny current readiness.
        Assert.True(blockedBySeal);
        Assert.True(sealedResult.Succeeded, sealedResult.Message);
        Assert.Single(seals);
        Assert.Equal(sealedResult.Value!.SealId, seals[0].Id);
      }
      var readiness = await FinancialPackageValidationReadiness.GetAsync(observer, maker.FirmId, packageId, timeout.Token);
      Assert.False(readiness.Ready);
      Assert.Contains("NATIVE_SOURCE_CURRENT", readiness.Failed);
      var statusAfter = await PackageSealService.GetSealStatusAsync(observer, maker, packageId, timeout.Token);
      Assert.True(statusAfter.Succeeded, statusAfter.Message);
      Assert.False(statusAfter.Value!.CanSeal);
      Assert.Equal(!reopenedBeforeResume, statusAfter.Value.IsSealed);
      var sourceAfter = await NativeTrialBalanceSourceWorkspace.ViewAsync(observer, maker, source.DatasetId, timeout.Token);
      Assert.True(sourceAfter.Succeeded, sourceAfter.Message);
      Assert.False(sourceAfter.Value!.IsCurrent);
      var history = await ClientOperationalOpeningBalanceWorkspace.GetSuccessorAsync(observer, maker,
        f.Native.Scope.ClientA, openingId, timeout.Token);
      Assert.True(history.Succeeded, history.Message);
      Assert.False(history.Value!.IsCurrent);
      Assert.Equal(openingHash, history.Value.Opening.ManifestSha256);
      Assert.Equal(proofHash, history.Value.ProofHash);
      var staleSeal = await PackageSealService.SealPackageAsync(observer, maker, packageId, timeout.Token);
      Assert.False(staleSeal.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, staleSeal.ErrorCode);
      Assert.Equal(seals.Length, await observer.FinancialPackageSeals.CountAsync(x => x.FinancialPackageId == packageId, timeout.Token));
    }
    finally
    {
      pause.Release.TrySetResult(true);
      await timeout.CancelAsync();
      if (sealTask is not null)
        try { await sealTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
      if (reopenTask is not null)
        try { await reopenTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
    }
  }
}
