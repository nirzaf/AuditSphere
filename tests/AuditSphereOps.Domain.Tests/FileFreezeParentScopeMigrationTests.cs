using System.Security.Cryptography;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
[Trait("CompletionUnit", "WU-004")]
public sealed class FileFreezeParentScopeMigrationTests
{
  private const string BaselineMigration = "20261101041254_ClientDeliverableReviewScope";
  private const string ScopeMigration = "20261101061145_FileFreezeParentScope";
  private static readonly string[] ScopeConstraints =
  [
    "fk_engagement_file_freeze_engagement_scope",
    "fk_engagement_file_freeze_deliverable_scope",
    "fk_file_freeze_amendment_freeze_scope",
    "fk_file_freeze_early_lock_parent_scope"
  ];
  private static readonly string[] ScopeKeys =
  [
    "ak_engagement_file_freeze_firm_engagement_id",
    "ak_engagement_file_freeze_scope_report_id"
  ];
  private static readonly string[] FreezeTables =
  ["engagement_file_freezes", "file_freeze_amendments", "file_freeze_early_locks"];

  [Fact]
  public async Task Migration_RoundTripsRowsAndPreservesPriorCatalogAndHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    var deliverable = await AddDeliverableAsync(
      pg, own.FirmId, own.ClientId, own.EngagementId, own.Staff.Id, 1, 0x71);
    var freezeId = Guid.NewGuid();
    await InsertFreezeAsync(pg, freezeId, own.FirmId, own.ClientId, own.EngagementId, deliverable.Id, now);
    await InsertAmendmentAsync(pg, Guid.NewGuid(), own.FirmId, own.EngagementId, freezeId, own.Staff.Id, now);
    await InsertEarlyLockAsync(
      pg, Guid.NewGuid(), own.FirmId, own.ClientId, own.EngagementId, freezeId, deliverable.Id, own.Staff.Id, now);

    await using var db = new AuditSphereDbContext(pg.Options);
    var migrator = db.GetService<IMigrator>();
    var currentHistory = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    var baselineIndex = Array.IndexOf(currentHistory, BaselineMigration);
    var scopeIndex = Array.IndexOf(currentHistory, ScopeMigration);
    Assert.True(baselineIndex >= 0, $"Baseline migration {BaselineMigration} is absent.");
    Assert.True(scopeIndex > baselineIndex, $"Scope migration {ScopeMigration} must follow its baseline.");
    var currentCatalog = await ReadCatalogAsync(pg);
    var retainedRows = await ReadRowsAsync(pg);

    await migrator.MigrateAsync(BaselineMigration);
    var baselineHistory = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    Assert.Equal(currentHistory[..(baselineIndex + 1)], baselineHistory);
    var baselineCatalog = await ReadCatalogAsync(pg);
    AssertScopeObjectsAbsent(baselineCatalog);
    Assert.Equal(retainedRows, await ReadRowsAsync(pg));

    await migrator.MigrateAsync();
    Assert.Equal(currentHistory, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
    Assert.Equal(currentCatalog, await ReadCatalogAsync(pg));
    Assert.Equal(retainedRows, await ReadRowsAsync(pg));
    AssertOnlyParentKeyIndexesAdded(baselineCatalog, currentCatalog);

    await migrator.MigrateAsync(BaselineMigration);
    Assert.Equal(baselineHistory, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
    Assert.Equal(baselineCatalog, await ReadCatalogAsync(pg));
    Assert.Equal(retainedRows, await ReadRowsAsync(pg));
    await migrator.MigrateAsync();
    Assert.Equal(currentHistory, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
    Assert.Equal(currentCatalog, await ReadCatalogAsync(pg));
    Assert.Equal(retainedRows, await ReadRowsAsync(pg));
  }

  [Theory]
  [InlineData("freeze-engagement", "fk_engagement_file_freeze_engagement_scope")]
  [InlineData("freeze-deliverable", "fk_engagement_file_freeze_deliverable_scope")]
  [InlineData("amendment", "fk_file_freeze_amendment_freeze_scope")]
  [InlineData("early-lock", "fk_file_freeze_early_lock_parent_scope")]
  public async Task Migration_RefusesEachInvalidRetainedLinkAtomically(string probe, string expectedConstraint)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using (var migrationDb = new AuditSphereDbContext(pg.Options))
      await migrationDb.GetService<IMigrator>().MigrateAsync(BaselineMigration);

    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;

    switch (probe)
    {
      case "freeze-engagement":
      {
        var report = await AddDeliverableAsync(
          pg, own.FirmId, own.ClientId, foreign.EngagementId, own.Staff.Id, 1, 0x21);
        await InsertFreezeAsync(
          pg, Guid.NewGuid(), own.FirmId, own.ClientId, foreign.EngagementId, report.Id, now);
        break;
      }
      case "freeze-deliverable":
      {
        var (otherClientId, otherEngagementId) = await AddOtherClientAsync(pg, own.FirmId, now);
        var report = await AddDeliverableAsync(
          pg, own.FirmId, otherClientId, otherEngagementId, own.Staff.Id, 1, 0x31);
        await InsertFreezeAsync(
          pg, Guid.NewGuid(), own.FirmId, own.ClientId, own.EngagementId, report.Id, now);
        break;
      }
      case "amendment":
      {
        var ownReport = await AddDeliverableAsync(
          pg, own.FirmId, own.ClientId, own.EngagementId, own.Staff.Id, 1, 0x41);
        var foreignReport = await AddDeliverableAsync(
          pg, foreign.FirmId, foreign.ClientId, foreign.EngagementId, foreign.Staff.Id, 1, 0x51);
        var ownFreeze = Guid.NewGuid();
        var foreignFreeze = Guid.NewGuid();
        await InsertFreezeAsync(pg, ownFreeze, own.FirmId, own.ClientId, own.EngagementId, ownReport.Id, now);
        await InsertFreezeAsync(
          pg, foreignFreeze, foreign.FirmId, foreign.ClientId, foreign.EngagementId, foreignReport.Id, now);
        await InsertAmendmentAsync(
          pg, Guid.NewGuid(), own.FirmId, own.EngagementId, foreignFreeze, own.Staff.Id, now);
        break;
      }
      case "early-lock":
      {
        var originalReport = await AddDeliverableAsync(
          pg, own.FirmId, own.ClientId, own.EngagementId, own.Staff.Id, 1, 0x61);
        var otherReport = await AddDeliverableAsync(
          pg, own.FirmId, own.ClientId, own.EngagementId, own.Staff.Id, 2, 0x71);
        var freezeId = Guid.NewGuid();
        await InsertFreezeAsync(pg, freezeId, own.FirmId, own.ClientId, own.EngagementId, originalReport.Id, now);
        await InsertEarlyLockAsync(
          pg, Guid.NewGuid(), own.FirmId, own.ClientId, own.EngagementId, freezeId, otherReport.Id, own.Staff.Id, now);
        break;
      }
      default:
        throw new ArgumentOutOfRangeException(nameof(probe), probe, "Unknown file-freeze scope probe.");
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var migrationIds = verify.Database.GetMigrations().ToArray();
    var migrationIndex = Array.IndexOf(migrationIds, ScopeMigration);
    Assert.True(migrationIndex > 0, $"Could not locate {ScopeMigration} in the current migration list.");
    var expectedHistory = migrationIds[..migrationIndex];
    Assert.Equal(expectedHistory, (await verify.Database.GetAppliedMigrationsAsync()).ToArray());
    var oldCatalog = await ReadCatalogAsync(pg);
    AssertScopeObjectsAbsent(oldCatalog);
    var legacyRows = await ReadRowsAsync(pg);

    var refused = await Assert.ThrowsAsync<PostgresException>(() => verify.GetService<IMigrator>().MigrateAsync());
    Assert.Equal("23503", refused.SqlState);
    Assert.Equal(expectedConstraint, refused.ConstraintName);
    Assert.Equal(expectedHistory, (await verify.Database.GetAppliedMigrationsAsync()).ToArray());
    Assert.Equal(oldCatalog, await ReadCatalogAsync(pg));
    Assert.Equal(legacyRows, await ReadRowsAsync(pg));
  }

  private static async Task<AuditDeliverable> AddDeliverableAsync(
    ITestPostgresDatabase pg,
    Guid firmId,
    Guid clientId,
    Guid engagementId,
    Guid createdByUserId,
    int version,
    byte marker)
  {
    var content = new byte[] { marker, (byte)(marker + 1), (byte)(marker + 2) };
    var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    var deliverable = new AuditDeliverable
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      Kind = DeliverableKinds.ManagementLetter, Version = version, TemplateVersion = "STRUCTURAL-TEST",
      InputDigest = digest, InputSummaryJson = "{}", FileName = "structural-only.txt",
      ContentType = "text/plain", Content = content, ContentSha256 = digest,
      CreatedByUserId = createdByUserId, CreatedAt = DateTimeOffset.UtcNow
    };
    await using var db = new AuditSphereDbContext(pg.Options);
    db.AuditDeliverables.Add(deliverable);
    await db.SaveChangesAsync();
    return deliverable;
  }

  private static async Task<(Guid ClientId, Guid EngagementId)> AddOtherClientAsync(
    ITestPostgresDatabase pg, Guid firmId, DateTimeOffset now)
  {
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "SYNTHETIC OTHER CLIENT", CreatedAt = now
    });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
    });
    await db.SaveChangesAsync();
    return (clientId, engagementId);
  }

  private static async Task InsertFreezeAsync(
    ITestPostgresDatabase pg,
    Guid id,
    Guid firmId,
    Guid clientId,
    Guid engagementId,
    Guid reportDeliverableId,
    DateTimeOffset now)
  {
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
      INSERT INTO engagement_file_freezes
        (id, firm_id, client_id, engagement_id, report_deliverable_id, report_signed_at, due_at,
         state, revision, frozen_at, external_read_only, updated_at)
      VALUES
        (@id, @firm, @client, @engagement, @deliverable, @signed, @due,
         'SCHEDULED', 1, NULL, 'NOT_REQUESTED', @updated)
      """, connection);
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("firm", firmId);
    command.Parameters.AddWithValue("client", clientId);
    command.Parameters.AddWithValue("engagement", engagementId);
    command.Parameters.AddWithValue("deliverable", reportDeliverableId);
    command.Parameters.AddWithValue("signed", now);
    command.Parameters.AddWithValue("due", now.AddDays(60));
    command.Parameters.AddWithValue("updated", now);
    await command.ExecuteNonQueryAsync();
  }

  private static async Task InsertAmendmentAsync(
    ITestPostgresDatabase pg,
    Guid id,
    Guid firmId,
    Guid engagementId,
    Guid freezeId,
    Guid requestedByUserId,
    DateTimeOffset now)
  {
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
      INSERT INTO file_freeze_amendments
        (id, firm_id, freeze_id, engagement_id, reason, requested_by_user_id, requested_at,
         approved_by_user_id, opened_at, closed_by_user_id, closed_at)
      VALUES
        (@id, @firm, @freeze, @engagement, 'synthetic migration scope fixture', @actor, @requested,
         NULL, NULL, NULL, NULL)
      """, connection);
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("firm", firmId);
    command.Parameters.AddWithValue("freeze", freezeId);
    command.Parameters.AddWithValue("engagement", engagementId);
    command.Parameters.AddWithValue("actor", requestedByUserId);
    command.Parameters.AddWithValue("requested", now);
    await command.ExecuteNonQueryAsync();
  }

  private static async Task InsertEarlyLockAsync(
    ITestPostgresDatabase pg,
    Guid id,
    Guid firmId,
    Guid clientId,
    Guid engagementId,
    Guid freezeId,
    Guid reportDeliverableId,
    Guid lockedByUserId,
    DateTimeOffset now)
  {
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
      INSERT INTO file_freeze_early_locks
        (id, firm_id, client_id, engagement_id, freeze_id, report_deliverable_id, freeze_revision,
         release_id, archive_readiness_digest, rationale, locked_by_user_id, locked_at)
      VALUES
        (@id, @firm, @client, @engagement, @freeze, @deliverable, 1,
         @release, @digest, 'synthetic migration scope fixture', @actor, @locked)
      """, connection);
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("firm", firmId);
    command.Parameters.AddWithValue("client", clientId);
    command.Parameters.AddWithValue("engagement", engagementId);
    command.Parameters.AddWithValue("freeze", freezeId);
    command.Parameters.AddWithValue("deliverable", reportDeliverableId);
    command.Parameters.AddWithValue("release", Guid.NewGuid());
    command.Parameters.AddWithValue("digest", new string('c', 64));
    command.Parameters.AddWithValue("actor", lockedByUserId);
    command.Parameters.AddWithValue("locked", now);
    await command.ExecuteNonQueryAsync();
  }

  private static async Task<string> ReadRowsAsync(ITestPostgresDatabase pg)
  {
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
      SELECT jsonb_build_object(
        'freezes', COALESCE((SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM engagement_file_freezes t), '[]'::jsonb),
        'amendments', COALESCE((SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM file_freeze_amendments t), '[]'::jsonb),
        'earlyLocks', COALESCE((SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM file_freeze_early_locks t), '[]'::jsonb)
      )::text
      """, connection);
    return (string)(await command.ExecuteScalarAsync())!;
  }

  private static async Task<string> ReadCatalogAsync(ITestPostgresDatabase pg)
  {
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
      SELECT jsonb_build_object(
        'constraints', COALESCE((
          SELECT jsonb_agg(jsonb_build_array(r.relname, c.conname, c.contype::text,
            pg_get_constraintdef(c.oid, true), c.convalidated, c.condeferrable)
            ORDER BY r.relname, c.conname)
          FROM pg_constraint c JOIN pg_class r ON r.oid=c.conrelid
          WHERE c.connamespace=current_schema()::regnamespace AND r.relname=ANY(@tables)
        ), '[]'::jsonb),
        'indexes', COALESCE((
          SELECT jsonb_agg(jsonb_build_array(tablename, indexname, indexdef) ORDER BY tablename, indexname)
          FROM pg_indexes WHERE schemaname=current_schema() AND tablename=ANY(@tables)
        ), '[]'::jsonb)
      )::text
      """, connection);
    command.Parameters.AddWithValue("tables", FreezeTables);
    return (string)(await command.ExecuteScalarAsync())!;
  }

  private static void AssertScopeObjectsAbsent(string catalog)
  {
    using var document = System.Text.Json.JsonDocument.Parse(catalog);
    var constraints = document.RootElement.GetProperty("constraints").EnumerateArray()
      .Select(row => row[1].GetString()!).ToHashSet(StringComparer.Ordinal);
    var indexes = document.RootElement.GetProperty("indexes").EnumerateArray()
      .Select(row => row[1].GetString()!).ToHashSet(StringComparer.Ordinal);
    foreach (var name in ScopeConstraints) Assert.DoesNotContain(name, constraints);
    foreach (var name in ScopeKeys)
    {
      Assert.DoesNotContain(name, constraints);
      Assert.DoesNotContain(name, indexes);
    }
  }

  private static void AssertOnlyParentKeyIndexesAdded(string baselineCatalog, string currentCatalog)
  {
    using var baseline = System.Text.Json.JsonDocument.Parse(baselineCatalog);
    using var current = System.Text.Json.JsonDocument.Parse(currentCatalog);
    var oldIndexes = baseline.RootElement.GetProperty("indexes").EnumerateArray()
      .Select(row => row[1].GetString()!).ToHashSet(StringComparer.Ordinal);
    var currentIndexes = current.RootElement.GetProperty("indexes").EnumerateArray()
      .Select(row => row[1].GetString()!).ToHashSet(StringComparer.Ordinal);
    Assert.Equal(ScopeKeys.OrderBy(name => name, StringComparer.Ordinal),
      currentIndexes.Except(oldIndexes, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal));
    Assert.True(oldIndexes.IsSubsetOf(currentIndexes), "A pre-existing file-freeze index was removed or renamed.");

    var constraints = current.RootElement.GetProperty("constraints").EnumerateArray()
      .Where(row => ScopeConstraints.Contains(row[1].GetString()!, StringComparer.Ordinal) ||
                    ScopeKeys.Contains(row[1].GetString()!, StringComparer.Ordinal))
      .ToArray();
    Assert.Equal(ScopeConstraints.Length + ScopeKeys.Length, constraints.Length);
    foreach (var row in constraints) Assert.True(row[4].GetBoolean());
    foreach (var name in ScopeConstraints)
    {
      var row = Assert.Single(constraints, item => item[1].GetString() == name);
      Assert.Equal("f", row[2].GetString());
      Assert.Contains("ON DELETE RESTRICT", row[3].GetString());
      Assert.False(row[5].GetBoolean());
    }
    foreach (var name in ScopeKeys)
    {
      var row = Assert.Single(constraints, item => item[1].GetString() == name);
      Assert.Equal("u", row[2].GetString());
      Assert.False(row[5].GetBoolean());
    }
  }
}
