using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Module 22 read-side eligibility report: the plan membership is classified
/// with reason codes and a contribution-set digest, mirroring the finalize rules.</summary>
public sealed class AdjustmentEligibilityQueryTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task EligibilityReport_ClassifiesBlockedExcludedAndEligibleJournals()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var first = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-ELIG-1", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, first)).Succeeded);
      var second = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-ELIG-2", [("1000", 5m, 0m), ("4000", 0m, 5m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, second)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-ELIG-1", 1, ReflectionStates.NotReflected, "Not yet in the client ledger.")).Succeeded);
      // AJ-ELIG-2 has no reconciliation record yet: its reflection is Unknown.

      var planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-ELIG-1", 1), new PlanLineInput("AJ-ELIG-2", 1)])).Value;

      var denied = await AdjustmentEligibilityQuery.GetEligibilityAsync(db,
        Actor(fixture.Outsider, "AccountingPreparer"), planId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

      var report = await AdjustmentEligibilityQuery.GetEligibilityAsync(db, preparer, planId);
      Assert.True(report.Succeeded, report.Message);
      Assert.Equal("Draft", report.Value!.PlanStatus);
      Assert.Equal(1, report.Value.EligibleCount);
      Assert.Equal(0, report.Value.ExcludedCount);
      Assert.Equal(1, report.Value.BlockedCount);
      var eligible = report.Value.Journals.Single(x => x.JournalNumber == "AJ-ELIG-1");
      Assert.Equal(AdjustmentEligibilityQuery.Eligible, eligible.Classification);
      Assert.Equal(ReflectionStates.NotReflected, eligible.ReflectionState);
      var blocked = report.Value.Journals.Single(x => x.JournalNumber == "AJ-ELIG-2");
      Assert.Equal(AdjustmentEligibilityQuery.Blocked, blocked.Classification);
      Assert.Equal("reflection.unresolved", blocked.ReasonCode);
      var digestBefore = report.Value.MembershipDigest;
      Assert.Matches("^[0-9a-f]{64}$", digestBefore);

      // A changed decision cannot make the old immutable selection look usable.
      // Finalization refuses this plan until a replacement snapshots that decision.
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-ELIG-2", 1, ReflectionStates.Reflected, "Client ledger already includes this entry.")).Succeeded);
      var after = await AdjustmentEligibilityQuery.GetEligibilityAsync(db, preparer, planId);
      Assert.True(after.Succeeded, after.Message);
      var changed = after.Value!.Journals.Single(x => x.JournalNumber == "AJ-ELIG-2");
      Assert.Equal(AdjustmentEligibilityQuery.Blocked, changed.Classification);
      Assert.Equal("reflection.changed-since-plan", changed.ReasonCode);
      Assert.Equal(1, after.Value.EligibleCount);
      Assert.Equal(0, after.Value.ExcludedCount);
      Assert.Equal(1, after.Value.BlockedCount);
      Assert.NotEqual(digestBefore, after.Value.MembershipDigest);
      var stale = await AdjustmentPlanService.FinalizeAsync(db, preparer, planId);
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
      var replacement = await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-ELIG-1", 1), new PlanLineInput("AJ-ELIG-2", 1)]);
      Assert.True(replacement.Succeeded);
      var current = await AdjustmentEligibilityQuery.GetEligibilityAsync(db, reviewer, replacement.Value);
      Assert.True(current.Succeeded, current.Message);
      Assert.Equal(1, current.Value!.EligibleCount);
      Assert.Equal(1, current.Value.ExcludedCount);
      Assert.Equal(0, current.Value.BlockedCount);
      await db.JournalSourceReconciliations.Where(x => x.LogicalJournalNumber == "AJ-ELIG-1").ExecuteDeleteAsync();
      var missing = await AdjustmentEligibilityQuery.GetEligibilityAsync(db, preparer, replacement.Value);
      Assert.True(missing.Succeeded);
      Assert.Equal(ReflectionStates.Unknown, missing.Value!.Journals.Single(x => x.JournalNumber == "AJ-ELIG-1").ReflectionState);
      Assert.Equal(1, missing.Value.BlockedCount);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AdjustmentSources_RejectForeignDatasetsAndPreserveExactFinancialEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    async Task<(Guid Draft, Guid Plan)> CreateNativeAsync(Fixture fixture)
    {
      var preparer = Actor(fixture.Preparer, "AccountingPreparer");
      var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
      var draft = await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId, "AJ-SCOPE-DRAFT",
        [("1000", 10m, 0m), ("4000", 0m, 10m)]);
      var posted = await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId, "AJ-SCOPE-POSTED",
        [("1000", 5m, 0m), ("4000", 0m, 5m)]);
      Assert.True(draft.Succeeded, draft.Message);
      Assert.True(posted.Succeeded, posted.Message);
      var result = await AdjustmentJournalService.PostAsync(db, reviewer, posted.Value);
      Assert.True(result.Succeeded, result.Message);
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId, [new PlanLineInput("AJ-SCOPE-POSTED", 1)]);
      Assert.True(plan.Succeeded, plan.Message);
      return (draft.Value, plan.Value);
    }
    var ownRecords = await CreateNativeAsync(own);
    await CreateNativeAsync(foreign);
    var actor = Actor(own.Preparer, "AccountingPreparer");
    Assert.Equal(ErrorCodes.ScopeDenied, (await AdjustmentJournalService.CreateDraftAsync(db, actor, foreign.DatasetId,
      "AJ-FOREIGN-DENIED", [("1000", 10m, 0m), ("4000", 0m, 10m)])).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AdjustmentPlanService.CreatePlanAsync(db, actor, foreign.DatasetId,
      [new PlanLineInput("AJ-SCOPE-POSTED", 1)])).ErrorCode);
    Assert.Equal(new[] { 100m, -100m }, await db.TrialBalanceRows.Where(x => x.DatasetId == own.DatasetId)
      .OrderBy(x => x.AccountCode).Select(x => x.Amount).ToArrayAsync());
    async Task<string[]> ReadHeadersAsync() => await db.Database.SqlQueryRaw<string>("""
      SELECT ('journal:' || to_jsonb(j)::text) AS "Value" FROM adjustment_journals j
      UNION ALL SELECT ('plan:' || to_jsonb(p)::text) AS "Value" FROM adjustment_plans p
      UNION ALL SELECT ('dataset:' || to_jsonb(d)::text) AS "Value" FROM trial_balance_datasets d
      ORDER BY "Value"
      """).ToArrayAsync();
    var retained = await ReadHeadersAsync();
    var lines = System.Text.Json.JsonSerializer.Serialize(await db.AdjustmentLines.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
    var planLines = System.Text.Json.JsonSerializer.Serialize(await db.AdjustmentPlanLines.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
    await using var connection = new Npgsql.NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    async Task<int> InsertAsync(bool journal, Guid dataset, Guid client, Guid engagement)
    {
      var table = journal ? "adjustment_journals" : "adjustment_plans";
      await using var command = new Npgsql.NpgsqlCommand($"""
        INSERT INTO {table}
        SELECT (jsonb_populate_record(NULL::{table}, to_jsonb(h) || jsonb_build_object(
          'id', @id, 'base_dataset_id', @dataset, 'client_id', @client, 'engagement_id', @engagement,
          'journal_number', 'AJ-SQL-CONTROL'))).*
        FROM {table} h WHERE h.id = @source
        """, connection, transaction);
      command.Parameters.AddWithValue("id", Guid.NewGuid());
      command.Parameters.AddWithValue("dataset", dataset);
      command.Parameters.AddWithValue("client", client);
      command.Parameters.AddWithValue("engagement", engagement);
      command.Parameters.AddWithValue("source", journal ? ownRecords.Draft : ownRecords.Plan);
      return await command.ExecuteNonQueryAsync();
    }
    await new Npgsql.NpgsqlCommand("SAVEPOINT adjustment_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    Assert.Equal(1, await InsertAsync(true, own.DatasetId, own.ClientId, own.EngagementId));
    Assert.Equal(1, await InsertAsync(false, own.DatasetId, own.ClientId, own.EngagementId));
    await new Npgsql.NpgsqlCommand("ROLLBACK TO SAVEPOINT adjustment_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    var accepted = new List<string>();
    foreach (var (name, journal, dataset, client, engagement) in new[]
    {
      ("journal dataset", true, foreign.DatasetId, own.ClientId, own.EngagementId),
      ("plan dataset", false, foreign.DatasetId, own.ClientId, own.EngagementId),
      ("journal client", true, own.DatasetId, foreign.ClientId, own.EngagementId),
      ("plan client", false, own.DatasetId, foreign.ClientId, own.EngagementId)
    })
    {
      try
      {
        Assert.Equal(1, await InsertAsync(journal, dataset, client, engagement));
        accepted.Add(name);
      }
      catch (Npgsql.PostgresException exception)
      {
        Assert.Contains(exception.SqlState, new[] { "23503", "23514" });
        Assert.False(string.IsNullOrWhiteSpace(exception.ConstraintName));
        Console.WriteLine($"SCOPED_SOURCE_DENIAL {name}: {exception.SqlState}/{exception.ConstraintName}");
      }
      await new Npgsql.NpgsqlCommand("ROLLBACK TO SAVEPOINT adjustment_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    }
    await transaction.RollbackAsync();
    Assert.Equal(retained, await ReadHeadersAsync());
    Assert.Equal(lines, System.Text.Json.JsonSerializer.Serialize(await db.AdjustmentLines.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()));
    Assert.Equal(planLines, System.Text.Json.JsonSerializer.Serialize(await db.AdjustmentPlanLines.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()));
    Assert.True(accepted.Count == 0, "Cross-scope adjustment source references accepted: " + string.Join(", ", accepted));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AdjustmentSourceScopeUpgrade_PreservesFinancialEvidenceAndRefusesInconsistentParents()
  {
    const string baseline = "20261029065958_AdHocProcedureHistoryScope";
    const string scopedSource = "20261029072350_AdjustmentSourceDatasetScope";
    await using var pg = await PgTestSchema.CreateAsync(baseline);
    var own = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    async Task<(Guid Draft, Guid Plan)> CreateNativeAsync(Fixture fixture)
    {
      var preparer = Actor(fixture.Preparer, "AccountingPreparer");
      var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
      var draft = await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId, "AJ-SCOPE-DRAFT",
        [("1000", 10m, 0m), ("4000", 0m, 10m)]);
      var posted = await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId, "AJ-SCOPE-POSTED",
        [("1000", 5m, 0m), ("4000", 0m, 5m)]);
      Assert.True(draft.Succeeded, draft.Message);
      Assert.True(posted.Succeeded, posted.Message);
      var result = await AdjustmentJournalService.PostAsync(db, reviewer, posted.Value);
      Assert.True(result.Succeeded, result.Message);
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId, [new PlanLineInput("AJ-SCOPE-POSTED", 1)]);
      Assert.True(plan.Succeeded, plan.Message);
      return (draft.Value, plan.Value);
    }
    var ownRecords = await CreateNativeAsync(own);
    await CreateNativeAsync(foreign);
    async Task<string[]> ReadHeadersAsync() => await db.Database.SqlQueryRaw<string>("""
      SELECT ('journal:' || to_jsonb(j)::text) AS "Value" FROM adjustment_journals j
      UNION ALL SELECT ('plan:' || to_jsonb(p)::text) AS "Value" FROM adjustment_plans p
      UNION ALL SELECT ('dataset:' || to_jsonb(d)::text) AS "Value" FROM trial_balance_datasets d
      ORDER BY "Value"
      """).ToArrayAsync();
    async Task<string> ReadEvidenceAsync() => System.Text.Json.JsonSerializer.Serialize(new
    {
      Headers = await ReadHeadersAsync(),
      JournalLines = await db.AdjustmentLines.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
      PlanLines = await db.AdjustmentPlanLines.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
    });
    async Task<string[]> ReadIndexesAsync() => await db.Database.SqlQueryRaw<string>("""
      SELECT (tablename || ':' || indexname || ':' || indexdef)::text AS "Value" FROM pg_indexes
      WHERE tablename IN ('adjustment_journals','adjustment_plans') ORDER BY "Value"
      """).ToArrayAsync();
    async Task<string[]> ReadConstraintsAsync() => await db.Database.SqlQueryRaw<string>("""
      SELECT (conname || ':' || pg_get_constraintdef(oid))::text AS "Value" FROM pg_constraint
      WHERE contype = 'f' AND conrelid IN ('adjustment_journals'::regclass,'adjustment_plans'::regclass)
      ORDER BY "Value"
      """).ToArrayAsync();
    var retained = await ReadEvidenceAsync();
    var indexes = await ReadIndexesAsync();
    var constraints = await ReadConstraintsAsync();
    var migrator = db.GetService<IMigrator>();
    await migrator.MigrateAsync(scopedSource);
    Assert.Equal(scopedSource, (await db.Database.GetAppliedMigrationsAsync()).Last());
    Assert.Equal(retained, await ReadEvidenceAsync());
    var upgradedIndexes = await ReadIndexesAsync();
    Assert.All(indexes, index => Assert.Contains(index, upgradedIndexes));
    await migrator.MigrateAsync(baseline);
    Assert.Equal(baseline, (await db.Database.GetAppliedMigrationsAsync()).Last());
    Assert.Equal(retained, await ReadEvidenceAsync());
    Assert.Equal(indexes, await ReadIndexesAsync());
    Assert.Equal(constraints, await ReadConstraintsAsync());
    Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO adjustment_journals
      SELECT (jsonb_populate_record(NULL::adjustment_journals, to_jsonb(j) || jsonb_build_object(
        'id', {Guid.NewGuid()}, 'base_dataset_id', {foreign.DatasetId}, 'journal_number', 'AJ-INCONSISTENT-HISTORY'))).*
      FROM adjustment_journals j WHERE j.id = {ownRecords.Draft}
      """));
    var inconsistent = await ReadEvidenceAsync();
    var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => migrator.MigrateAsync(scopedSource));
    Assert.Equal("23503", exception.SqlState);
    Assert.Equal("fk_adjustment_journal_source_scope", exception.ConstraintName);
    Assert.Equal(baseline, (await db.Database.GetAppliedMigrationsAsync()).Last());
    Assert.Equal(inconsistent, await ReadEvidenceAsync());
    Assert.Equal(indexes, await ReadIndexesAsync());
    Assert.Equal(constraints, await ReadConstraintsAsync());
  }

  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId,
    AppUser Preparer, AppUser Reviewer, AppUser Outsider);

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = null, EngagementId = null, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    var outsider = User(firmId, "outsider");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "ELIGIBILITY REPORT TEST", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new AuditSphereOps.Domain.Completion.FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer, outsider);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"), Grant(firmId, reviewer, "AccountingReviewer"));
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      SourceKind = "Raw", Revision = 1, Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.Id
    });
    db.TrialBalanceRows.AddRange(
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000",
        AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST"
      },
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
        AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST"
      });
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, datasetId, preparer, reviewer, outsider);
  }
}
