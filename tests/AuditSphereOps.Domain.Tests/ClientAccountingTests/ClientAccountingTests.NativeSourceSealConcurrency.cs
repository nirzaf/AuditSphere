using System.Data.Common;
using System.Text;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSourcePackageSealCannotPublishAfterConcurrentPostingCommitted()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeBehaviourFixtureAsync(pg);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Scope.Reviewer, "AccountingReviewer");
    Guid sourceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = NativeBehaviourRequest(f);
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(preview.Succeeded, preview.Message);
      var source = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer,
        request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
      Assert.True(source.Succeeded, source.Message);
      sourceId = source.Value!.DatasetId;
    }
    await ValidateNativeBehaviourSourceAsync(pg, f.Scope.FirmId, sourceId);
    await AcceptNativeBehaviourSourceAsync(pg, f, sourceId);

    // This is a seal concurrency scaffold, as in PackageSealTests: its package
    // validations are prevalidated fixture rows and its four artifacts are text
    // bytes, not rendered PDF/DOCX/XLSX or a professional financial-report approval.
    // All native source evidence, validation, acceptance and postings use commands.
    var packageId = await SeedNativeSealConcurrencyPackageAsync(pg, f, sourceId);
    Guid journalId;
    string postingPreview;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var draft = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, preparer,
        new(f.Scope.ClientA, f.PeriodId, "DURING-NATIVE-PACKAGE-SEAL", "Synthetic concurrent native posting",
          new DateOnly(2026, 1, 16),
          [new("1000", "Equipment", 25m, 0m), new("2000", "Loan", 0m, 25m)]));
      Assert.True(draft.Succeeded, draft.Message);
      journalId = draft.Value;
      var preview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, f.Scope.ClientA, journalId);
      Assert.True(preview.Succeeded, preview.Message);
      var submit = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, f.Scope.ClientA,
        journalId, 1, previewDigest: preview.Value!.Digest);
      Assert.True(submit.Succeeded, submit.Message);
      var review = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, f.Scope.ClientA, journalId);
      Assert.True(review.Succeeded, review.Message);
      postingPreview = review.Value!.Digest;
      var source = await NativeTrialBalanceSourceWorkspace.ViewAsync(db, reviewer, sourceId);
      Assert.True(source.Succeeded, source.Message);
      Assert.True(source.Value!.IsCurrent);
      var ready = await PackageSealService.GetSealStatusAsync(db, preparer, packageId);
      Assert.True(ready.Succeeded, ready.Message);
      Assert.True(ready.Value!.CanSeal);
      Assert.False(ready.Value.IsSealed);
      Assert.Empty(ready.Value.MissingRequiredFormats);
      Assert.False(await db.FinancialPackageSeals.AnyAsync(x => x.FinancialPackageId == packageId));
    }

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var pause = new PauseNativeSealExistingLookup();
    await using var sealDb = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var postDb = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    Task<CommandResult<PackageSealValue>>? sealTask = null;
    Task<CommandResult>? postTask = null;
    try
    {
      // Keep backend identities stable while observing the real PostgreSQL blocker.
      await sealDb.Database.OpenConnectionAsync(timeout.Token);
      await postDb.Database.OpenConnectionAsync(timeout.Token);
      var sealPid = ((NpgsqlConnection)sealDb.Database.GetDbConnection()).ProcessID;
      var postPid = ((NpgsqlConnection)postDb.Database.GetDbConnection()).ProcessID;
      sealTask = PackageSealService.SealPackageAsync(sealDb, preparer, packageId, timeout.Token);
      await pause.Reached.Task.WaitAsync(timeout.Token);
      postTask = ClientOperationalLedgerWorkspace.ReviewAndPostAsync(postDb, reviewer, f.Scope.ClientA, journalId,
        new(2, "APPROVE", "Synthetic independent concurrency review", postingPreview, Guid.CreateVersion7()), timeout.Token);

      var blockedBySeal = false;
      var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
      while (!postTask.IsCompleted && !blockedBySeal && DateTimeOffset.UtcNow < deadline)
      {
        blockedBySeal = await observer.Database.SqlQuery<bool>(
          $"SELECT {sealPid} = ANY(pg_blocking_pids({postPid})) AS \"Value\"").SingleAsync(timeout.Token);
        if (!postTask.IsCompleted && !blockedBySeal) await Task.Delay(20, timeout.Token);
      }
      Assert.True(blockedBySeal || postTask.IsCompleted,
        "Observe an actual PostgreSQL dependency on A or a completed official posting before releasing A.");
      var postingCommittedBeforeResume = postTask.IsCompleted;
      if (postingCommittedBeforeResume)
      {
        var posted = await postTask;
        Assert.True(posted.Succeeded, posted.Message);
        Assert.True(await observer.ClientOperationalJournals.AsNoTracking().AnyAsync(x =>
          x.Id == journalId && x.Status == "POSTED", timeout.Token));
        Assert.True(await observer.ClientOperationalPostingReceipts.AsNoTracking().AnyAsync(x =>
          x.JournalId == journalId && x.ActorUserId == f.Scope.Reviewer.Id, timeout.Token));
      }

      pause.Release.TrySetResult(true);
      var sealOutcome = await sealTask.WaitAsync(timeout.Token);
      var postOutcome = await postTask.WaitAsync(timeout.Token);
      Assert.True(postOutcome.Succeeded, postOutcome.Message);
      var sealCount = await observer.FinancialPackageSeals.CountAsync(x => x.FinancialPackageId == packageId, timeout.Token);
      if (postingCommittedBeforeResume)
      {
        Assert.False(sealOutcome.Succeeded,
          "A new package seal must not succeed after an official native posting committed while sealing was paused after readiness.");
        Assert.Equal(0, sealCount);
      }
      else
      {
        // Serialization is equally correct: A may seal the current source, then B
        // posts and makes that retained historical seal's source stale afterwards.
        Assert.True(blockedBySeal);
        Assert.True(sealOutcome.Succeeded, sealOutcome.Message);
        Assert.Equal(1, sealCount);
      }
      var retained = await NativeTrialBalanceSourceWorkspace.ViewAsync(observer, reviewer, sourceId, timeout.Token);
      Assert.True(retained.Succeeded, retained.Message);
      Assert.False(retained.Value!.IsCurrent);
    }
    finally
    {
      pause.Release.TrySetResult(true);
      await timeout.CancelAsync();
      if (sealTask is not null)
        try { await sealTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
      if (postTask is not null)
        try { await postTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
    }
  }

  private sealed class PauseNativeSealExistingLookup : DbCommandInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int paused;

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      // The existing-seal SELECT follows readiness in SealPackageAsync. Status
      // fixture checks use a different context with no interceptor attached.
      if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("FROM financial_package_seals", StringComparison.Ordinal) &&
          Interlocked.Exchange(ref paused, 1) == 0)
      {
        Reached.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }

  private static async Task<Guid> SeedNativeSealConcurrencyPackageAsync(PgTestSchema pg, NativeBehaviourFixture f, Guid sourceId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var source = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == sourceId);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == f.PeriodId);
    var generation = await db.ClientSafetyStates.Where(x => x.Id == f.Scope.ClientA).Select(x => x.InputGeneration).SingleAsync();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var now = DateTimeOffset.UtcNow;
    var hash = Hashing.Sha256Hex("Synthetic native package seal scaffold|" + source.Sha256Hex);
    var start = period.StartDate.ToString("yyyy-MM-dd");
    var end = period.EndDate.ToString("yyyy-MM-dd");
    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = source.FirmId, ClientId = source.ClientId, EngagementId = source.EngagementId,
      BaseDatasetId = source.Id, Status = "Finalized", ResultHash = hash,
      CreatedByUserId = f.Scope.Preparer.Id, CreatedAt = now
    });
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = source.FirmId, ClientId = source.ClientId, EngagementId = source.EngagementId,
      DatasetId = source.Id, Version = 1, Generation = generation, ClientChartVersionId = f.ChartId,
      TaxonomyVersion = "native-source-test", PeriodStart = start, PeriodEnd = end, Status = AccountingPackageStates.MappingApproved,
      CreatedByUserId = f.Scope.Preparer.Id, ApprovedByUserId = f.Scope.Reviewer.Id, ApprovedAt = now, CreatedAt = now
    });
    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = source.FirmId, ClientId = source.ClientId, EngagementId = source.EngagementId,
      BaseDatasetId = source.Id, AdjustmentPlanId = planId, Currency = source.Currency, ResultHash = hash,
      CreatedByUserId = f.Scope.Preparer.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = source.FirmId, ClientId = source.ClientId, EngagementId = source.EngagementId,
      AdjustedDatasetId = adjustedId, MappingVersionId = mappingId, AdjustmentPlanId = planId,
      PeriodId = source.PeriodId, BookId = source.BookId, Basis = source.Basis, Currency = source.Currency,
      Framework = "IFRS", PeriodStart = start, PeriodEnd = end, TaxonomyVersion = "native-source-test",
      TemplateVersion = "seal-concurrency-fixture", CalculationEngineVersion = "test-scaffold",
      CalculationHash = hash, Revision = 1, Generation = generation,
      Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    foreach (var code in FinancialPackageValidationReadiness.RequiredCodes)
      db.FinancialPackageValidations.Add(new FinancialPackageValidation
      {
        Id = Guid.CreateVersion7(), FirmId = source.FirmId, FinancialPackageId = packageId,
        Code = code, Passed = true, Detail = "Prevalidated seal race fixture only; no report build or professional acceptance.", CreatedAt = now
      });
    foreach (var version in PackageSealService.RequiredFormats)
    {
      var bytes = Encoding.UTF8.GetBytes($"Synthetic non-rendered seal concurrency artifact|{version}|{hash}");
      db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
      {
        Id = Guid.CreateVersion7(), FirmId = source.FirmId, ClientId = source.ClientId, EngagementId = source.EngagementId,
        FinancialPackageId = packageId, PackageRevision = 1, PackageGeneration = generation, PackageHash = hash,
        ArtifactVersion = version, FrameworkVersion = "IFRS", TemplateVersion = "seal-concurrency-fixture",
        ArtifactSha256Hex = Hashing.Sha256Hex(bytes), ArtifactBytes = bytes,
        CreatedByUserId = f.Scope.Preparer.Id, CreatedAt = now
      });
    }
    await db.SaveChangesAsync();
    return packageId;
  }
}
