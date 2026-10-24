using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorProofCannotBeChangedRemovedOrTruncated()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeSuccessorFixtureAsync(pg);
    var maker = Actor(f.Native.Scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var preview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, f.Request);
    Assert.True(preview.Succeeded, preview.Message);
    var created = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker,
      f.Request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
    Assert.True(created.Succeeded, created.Message);
    var openingId = created.Value!.Opening.Id;
    var proof = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleAsync(x => x.OpeningId == openingId);
    var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE client_operational_opening_successor_proofs SET proof_json='{{}}' WHERE opening_id={openingId}"));
    Assert.Equal("55000", update.SqlState);
    var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM client_operational_opening_successor_proofs WHERE opening_id={openingId}"));
    Assert.Equal("55000", delete.SqlState);
    var truncate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
      "TRUNCATE client_operational_opening_successor_proofs"));
    Assert.Equal("55000", truncate.SqlState);
    var retained = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleAsync(x => x.OpeningId == openingId);
    Assert.Equal(proof.ProofJson, retained.ProofJson);
    Assert.Equal(proof.ProofHash, retained.ProofHash);
    Assert.True((await ClientOperationalOpeningBalanceWorkspace.GetSuccessorAsync(db, maker,
      f.Native.Scope.ClientA, openingId)).Succeeded);
    var migrations = db.Database.GetMigrations().ToArray();
    var previous = migrations.TakeWhile(x => !x.EndsWith("_ClientOpeningSuccessorProof", StringComparison.Ordinal)).Last();
    var wrapper = await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>().MigrateAsync(previous));
    var rollback = Assert.IsType<PostgresException>(wrapper.InnerException);
    Assert.Equal("55000", rollback.SqlState);
    Assert.Contains("Cannot remove retained native successor opening evidence", rollback.MessageText, StringComparison.Ordinal);
    Assert.Equal(proof.ProofJson, (await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking()
      .SingleAsync(x => x.OpeningId == openingId)).ProofJson);
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorEmptyMigrationCanRollBackAndForwardWithoutRewritingLegacyOpenings()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeBehaviourFixtureAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var before = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == f.OpeningId);
    var migrations = db.Database.GetMigrations().ToArray();
    var migration = migrations.Single(x => x.EndsWith("_ClientOpeningSuccessorProof", StringComparison.Ordinal));
    await db.GetService<IMigrator>().MigrateAsync(migrations.TakeWhile(x => x != migration).Last());
    Assert.DoesNotContain(migration, await db.Database.GetAppliedMigrationsAsync());
    await db.GetService<IMigrator>().MigrateAsync(migrations.Last());
    Assert.Empty(await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().ToListAsync());
    var after = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == f.OpeningId);
    Assert.Equal(before.ManifestJson, after.ManifestJson);
    Assert.Equal(before.ManifestSha256, after.ManifestSha256);
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeManualOpeningSourceDoesNotTraverseUnconsumedPeriodHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeBehaviourFixtureAsync(pg);
    var maker = Actor(f.Scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    // Existing independently reviewed manual cutover is the source boundary. Older periods are
    // synthetic retained history, not generated-successor dependencies or a public activation claim.
    Guid? previous = null;
    for (var year = 1993; year <= 2025; year++)
    {
      var id = Guid.CreateVersion7();
      db.ClientReportingPeriods.Add(new()
      {
        Id = id, FirmId = f.Scope.FirmId, ClientId = f.Scope.ClientA, PeriodCode = $"HISTORY-{year}",
        StartDate = new(year, 1, 1), EndDate = new(year, 12, 31), Currency = "QAR", Basis = "IFRS",
        PriorPeriodId = previous, Status = AccountingWorkflowStates.Closed, Revision = 2,
        ClosedByUserId = f.Scope.Reviewer.Id, ClosedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = f.Scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow,
        CloseReason = "Synthetic pre-existing history outside reviewed manual cutover"
      });
      previous = id;
    }
    await db.SaveChangesAsync();
    await db.ClientReportingPeriods.Where(x => x.Id == f.PeriodId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PriorPeriodId, previous));
    var request = NativeBehaviourRequest(f);
    var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, maker, request);
    Assert.True(preview.Succeeded, preview.Message);
    var source = await NativeTrialBalanceSourceWorkspace.SealAsync(db, maker,
      request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
    Assert.True(source.Succeeded, source.Message);
    Assert.Equal("120", preview.Value.TrialBalance.ClosingDebit);
    Assert.True(source.Value!.IsCurrent, source.Value.CurrentnessBlocker);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorProofRejectsSelfHashedMissingSourceFactsAtCommit(bool genuineFactsWithWrongAmounts)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeSuccessorFixtureAsync(pg);
    var maker = Actor(f.Native.Scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var preview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, f.Request);
    Assert.True(preview.Succeeded, preview.Message);
    var source = await NativeTrialBalanceSourceWorkspace.ReadSuccessorSourceAsync(db, maker.FirmId,
      f.Native.Scope.ClientA, f.Native.Scope.EngagementA, f.SourceDatasetId, default);
    Assert.True(source.Succeeded, source.Message);
    var facts = await ClientOperationalOpeningBalanceWorkspace.ResolveSuccessorFactsAsync(db, maker.FirmId,
      f.Native.Scope.ClientA, f.TargetPeriodId, source.Value!, default);
    Assert.True(facts.Succeeded, facts.Message);
    // Real v1 manual bytes stay balanced but must not claim different native source amounts.
    var manual = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, maker,
      new(f.Native.Scope.ClientA, f.TargetPeriodId, 1, new DateOnly(2027, 1, 1), "QAR",
        "Synthetic manual opening for malformed proof boundary", f.SourceHash,
        preview.Value!.Lines.Select(x => new ClientOperationalOpeningBalanceLineInput(x.AccountCode,
          decimal.Parse(x.Debit, CultureInfo.InvariantCulture) +
            (genuineFactsWithWrongAmounts && x.AccountCode == "1000" ? 1m : 0m),
          decimal.Parse(x.Credit, CultureInfo.InvariantCulture) +
            (genuineFactsWithWrongAmounts && x.AccountCode == "3000" ? 1m : 0m))).ToArray()));
    Assert.True(manual.Succeeded, manual.Message);
    var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == manual.Value);
    var json = JsonSerializer.Serialize(new
    {
      Version = "native-opening-successor.v1", OpeningId = opening.Id, OpeningManifestHash = opening.ManifestSha256,
      opening.FirmId, opening.ClientId, EngagementId = f.Native.Scope.EngagementA,
      PriorPeriodId = f.Native.PeriodId, PriorPeriodRevision = 2L,
      SourceDatasetId = f.SourceDatasetId, SourceIdentityHash = f.SourceHash, Facts = genuineFactsWithWrongAmounts ? (object)facts.Value! : new { }
    });
    await using (var tx = await db.Database.BeginTransactionAsync())
    {
      db.ClientOperationalOpeningSuccessorProofs.Add(new()
      {
        OpeningId = opening.Id, FirmId = opening.FirmId, ClientId = opening.ClientId,
        EngagementId = f.Native.Scope.EngagementA, PriorPeriodId = f.Native.PeriodId, PriorPeriodRevision = 2,
        SourceDatasetId = f.SourceDatasetId, SourceIdentityHash = f.SourceHash, ProofJson = json,
        ProofHash = Hashing.Sha256Hex(json), RequestId = Guid.CreateVersion7(), ActorId = maker.UserId,
        ActorEpoch = maker.SessionEpoch, RequestHash = new string('a', 64), CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var refusal = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
      Assert.Equal("23514", refusal.SqlState);
      Assert.Contains(genuineFactsWithWrongAmounts ? "exact native closing rows" : "exact complete native source artifact",
        refusal.MessageText, StringComparison.Ordinal);
    }
    Assert.False(await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().AnyAsync(x => x.OpeningId == opening.Id));
    Assert.Equal(opening.ManifestSha256, (await db.ClientOperationalOpeningBalances.AsNoTracking()
      .SingleAsync(x => x.Id == opening.Id)).ManifestSha256);
  }
}
