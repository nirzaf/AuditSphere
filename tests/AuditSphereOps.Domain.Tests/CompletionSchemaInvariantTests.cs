using System.Text.RegularExpressions;
using Npgsql;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// WU-004: after applying every migration to an owned PostgreSQL database, each trigger the migrations leave in
/// place exists and is enabled, and no seeded single-column key between firm-scoped tables accepts another firm's row.
/// </summary>
[Trait("Profile", "Database")]
[Trait("CompletionUnit", "WU-004")]
public sealed partial class CompletionSchemaInvariantTests(Xunit.Abstractions.ITestOutputHelper output)
{
  [Fact]
  public async Task MigratedSchema_KeepsEveryMigrationTriggerEnabled()
  {
    var expected = ExpectedTriggers();
    Assert.True(expected.Count > 50, $"Only {expected.Count} triggers parsed from migrations; the probe would be vacuous.");
    await using var pg = await PgTestSchema.CreateAsync();
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    var actual = new Dictionary<string, char>(StringComparer.Ordinal);
    await using (var command = new NpgsqlCommand("SELECT tgname, tgenabled FROM pg_trigger WHERE NOT tgisinternal", connection))
    await using (var reader = await command.ExecuteReaderAsync())
      while (await reader.ReadAsync()) actual[reader.GetString(0)] = reader.GetChar(1);

    var missing = expected.Where(name => !actual.ContainsKey(name)).ToList();
    var disabled = expected.Where(name => actual.TryGetValue(name, out var state) && state is not ('O' or 'A')).ToList();
    output.WriteLine($"Triggers expected from migrations: {expected.Count}; present: {expected.Count - missing.Count}; disabled: {disabled.Count}");
    Assert.True(missing.Count == 0, "Missing: " + string.Join(", ", missing));
    Assert.True(disabled.Count == 0, "Disabled: " + string.Join(", ", disabled));
  }

  [Fact]
  public async Task MigratedSchema_ForeignKeysBetweenFirmScopedTablesCarryTheFirm()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    const string sql = """
      SELECT c.conname, child.relname, parent.relname,
             CASE WHEN cardinality(c.conkey) = 1 THEN (SELECT attname FROM pg_attribute WHERE attrelid = c.conrelid AND attnum = c.conkey[1]) END,
             CASE WHEN cardinality(c.confkey) = 1 THEN (SELECT attname FROM pg_attribute WHERE attrelid = c.confrelid AND attnum = c.confkey[1]) END,
             EXISTS (SELECT 1 FROM unnest(c.conkey) k JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k
                     WHERE a.attname = 'firm_id') AS carries_firm
      FROM pg_constraint c
      JOIN pg_class child ON child.oid = c.conrelid
      JOIN pg_class parent ON parent.oid = c.confrelid
      JOIN pg_namespace n ON n.oid = child.relnamespace
      WHERE c.contype = 'f' AND n.nspname = current_schema()
        AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.conrelid AND a.attname = 'firm_id' AND NOT a.attisdropped)
        AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.confrelid AND a.attname = 'firm_id' AND NOT a.attisdropped)
        -- A single-column key is scoped when another key on the same columns to the same parent also binds firm_id.
        AND NOT EXISTS (SELECT 1 FROM pg_constraint o JOIN pg_attribute fa ON fa.attrelid = o.conrelid AND fa.attname = 'firm_id'
                        WHERE o.contype = 'f' AND o.oid <> c.oid AND o.conrelid = c.conrelid AND o.confrelid = c.confrelid
                          AND c.conkey <@ o.conkey AND fa.attnum = ANY (o.conkey))
      ORDER BY child.relname, c.conname
      """;
    var scoped = 0;
    var unscoped = new List<string>();
    var probes = new List<(string Child, string Column, string Parent, string Reference)>();
    await using (var command = new NpgsqlCommand(sql, connection))
    await using (var reader = await command.ExecuteReaderAsync())
      while (await reader.ReadAsync())
      {
        scoped++;
        if (reader.GetBoolean(5)) continue;
        unscoped.Add($"{reader.GetString(1)} -> {reader.GetString(2)} ({reader.GetString(0)})");
        if (!reader.IsDBNull(3) && !reader.IsDBNull(4))
          probes.Add((reader.GetString(1), reader.GetString(3), reader.GetString(2), reader.GetString(4)));
      }
    output.WriteLine($"Foreign keys between firm-scoped tables: {scoped}; without firm_id: {unscoped.Count}");
    Assert.True(scoped > 20, $"Only {scoped} firm-scoped foreign keys found; the probe would be vacuous.");

    // Probe each single-column key: copy a seeded child row, repoint it at another firm's parent and observe
    // whether the database refuses it. Every probe is rolled back; nothing is committed.
    await PbcSeed.SeedAsync(pg);
    await PbcSeed.SeedAsync(pg);
    var accepted = new List<string>();
    await using var transaction = await connection.BeginTransactionAsync();
    await new NpgsqlCommand("SET CONSTRAINTS ALL IMMEDIATE", connection, transaction).ExecuteNonQueryAsync();
    foreach (var (child, column, parent, reference) in probes)
    {
      await new NpgsqlCommand("SAVEPOINT probe", connection, transaction).ExecuteNonQueryAsync();
      var probe = $"""
        INSERT INTO "{child}"
        SELECT (jsonb_populate_record(NULL::"{child}", to_jsonb(c) || jsonb_build_object(
          '{column}', (SELECT p."{reference}" FROM "{parent}" p WHERE p.firm_id <> c.firm_id LIMIT 1), 'id', gen_random_uuid()))).*
        FROM "{child}" c
        WHERE c.firm_id IS NOT NULL AND EXISTS (SELECT 1 FROM "{parent}" p WHERE p.firm_id <> c.firm_id)
        LIMIT 1
        """;
      string outcome;
      try
      {
        var rows = await new NpgsqlCommand(probe, connection, transaction).ExecuteNonQueryAsync();
        outcome = rows == 0 ? "NOT_PROBED" : "ACCEPTED_CROSS_FIRM";
        if (rows > 0) accepted.Add($"{child}.{column} -> {parent}");
      }
      catch (PostgresException exception) { outcome = $"REFUSED {exception.SqlState} {exception.MessageText}"; }
      await new NpgsqlCommand("ROLLBACK TO SAVEPOINT probe", connection, transaction).ExecuteNonQueryAsync();
      output.WriteLine($"PROBE {child}.{column} -> {parent}: {outcome}");
    }
    await transaction.RollbackAsync();
    output.WriteLine($"Single-column scoped-key probes: {probes.Count}; accepted cross-firm: {accepted.Count}");
    Assert.True(accepted.Count == 0, "Cross-firm references accepted: " + string.Join(", ", accepted));
  }

  [Fact]
  public async Task StaffCostRates_RejectForeignFirmUser()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();

    var sameFirmId = Guid.NewGuid();
    var crossFirmId = Guid.NewGuid();
    string crossFirmOutcome;
    await using (var transaction = await connection.BeginTransactionAsync())
    {
      async Task<int> InsertRateAsync(Guid id, Guid firmId, Guid userId, Guid recordedByUserId)
      {
        await using var command = new NpgsqlCommand("""
          INSERT INTO staff_cost_rates
            (id, firm_id, user_id, hourly_cost, currency, effective_from, recorded_by_user_id, recorded_at)
          VALUES
            (@id, @firm_id, @user_id, @hourly_cost, @currency, @effective_from, @recorded_by_user_id, @recorded_at)
          """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("firm_id", firmId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("hourly_cost", 200m);
        command.Parameters.AddWithValue("currency", "QAR");
        command.Parameters.AddWithValue("effective_from", new DateOnly(2026, 1, 1));
        command.Parameters.AddWithValue("recorded_by_user_id", recordedByUserId);
        command.Parameters.AddWithValue("recorded_at", DateTimeOffset.UtcNow);
        return await command.ExecuteNonQueryAsync();
      }

      var sameFirmRows = await InsertRateAsync(sameFirmId, own.FirmId, own.Staff.Id, own.Admin.Id);
      output.WriteLine($"SAME_FIRM_CONTROL: inserted={sameFirmRows}");
      Assert.Equal(1, sameFirmRows);

      try
      {
        var rows = await InsertRateAsync(crossFirmId, own.FirmId, foreign.Staff.Id, own.Admin.Id);
        crossFirmOutcome = $"ACCEPTED rows={rows}";
      }
      catch (PostgresException exception)
      {
        crossFirmOutcome = $"REFUSED SqlState={exception.SqlState} Constraint={exception.ConstraintName ?? "(none)"}";
      }
      output.WriteLine($"CROSS_FIRM_USER: {crossFirmOutcome}");
      await transaction.RollbackAsync();
    }

    await using var rollbackCheck = new NpgsqlCommand(
      "SELECT count(*) FROM staff_cost_rates WHERE id IN (@same_firm_id, @cross_firm_id)", connection);
    rollbackCheck.Parameters.AddWithValue("same_firm_id", sameFirmId);
    rollbackCheck.Parameters.AddWithValue("cross_firm_id", crossFirmId);
    Assert.Equal(0L, await rollbackCheck.ExecuteScalarAsync());
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_staff_cost_rate_user_scope", crossFirmOutcome);
  }

  [Theory]
  [InlineData("ENGAGEMENT")]
  [InlineData("USER")]
  [InlineData("ROLE_GRANT")]
  [InlineData("CLIENT")]
  [InlineData("OTHER_USER_GRANT")]
  public async Task StaffingReferences_RefuseForeignFirmParents(string parent)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var staffGrant = await db.RoleGrants.SingleAsync(x => x.FirmId == own.FirmId &&
      x.UserId == own.Staff.Id && x.Role == "Staff");
    var ownAdminGrant = PbcSeed.Grant(own.FirmId, own.Admin, "Staff", own.ClientId, own.EngagementId);
    var foreignAdminGrant = PbcSeed.Grant(foreign.FirmId, foreign.Admin, "Staff", foreign.ClientId, foreign.EngagementId);
    db.RoleGrants.AddRange(ownAdminGrant, foreignAdminGrant);
    db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId, ClientId = own.ClientId,
      EngagementId = own.EngagementId, UserId = own.Staff.Id, RoleGrantId = staffGrant.Id,
      StaffingLevel = StaffingLevels.StaffAssociate, AssignedByUserId = own.Admin.Id,
      AssignedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(); // All-owned control is valid; candidate uses a different local user.

    var candidate = new EngagementStaffAssignment
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId,
      ClientId = parent == "CLIENT" ? foreign.ClientId : own.ClientId,
      EngagementId = parent == "ENGAGEMENT" ? foreign.EngagementId : own.EngagementId,
      UserId = parent == "USER" ? foreign.Admin.Id : own.Admin.Id,
      RoleGrantId = parent == "ROLE_GRANT" ? foreignAdminGrant.Id :
        parent == "OTHER_USER_GRANT" ? staffGrant.Id : ownAdminGrant.Id,
      StaffingLevel = StaffingLevels.StaffAssociate, AssignedByUserId = own.Admin.Id,
      AssignedAt = DateTimeOffset.UtcNow
    };
    db.EngagementStaffAssignments.Add(candidate);
    var failure = await Record.ExceptionAsync(() => db.SaveChangesAsync());
    if (failure is null) output.WriteLine($"ACCEPTED_FOREIGN_STAFFING_PARENT:{parent}");
    var rejected = Assert.IsType<DbUpdateException>(failure);
    var postgres = Assert.IsType<PostgresException>(rejected.InnerException);
    Assert.Equal("23503", postgres.SqlState);
    var expectedConstraints = parent switch
    {
      "ENGAGEMENT" or "CLIENT" => new[] { "fk_staff_assignment_engagement_scope" },
      "USER" => new[] { "fk_staff_assignment_user_scope", "fk_staff_assignment_grant_scope" },
      _ => new[] { "fk_staff_assignment_grant_scope" }
    };
    Assert.Contains(postgres.ConstraintName, expectedConstraints);
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.False(await proof.EngagementStaffAssignments.AnyAsync(x => x.Id == candidate.Id));
    Assert.Equal(1, await proof.EngagementStaffAssignments.CountAsync(x => x.FirmId == own.FirmId));
  }

  [Theory]
  [InlineData("PROFILE", "staff_profiles", "fk_staff_profile_user_scope")]
  [InlineData("CERTIFICATION", "staff_certifications", "fk_staff_certification_user_scope")]
  [InlineData("AVAILABILITY", "staff_availabilities", "fk_staff_availability_user_scope")]
  [InlineData("ALLOCATION_USER", "staff_allocations", "fk_staff_allocation_user_scope")]
  [InlineData("ALLOCATION_ENGAGEMENT", "staff_allocations", "fk_staff_allocation_engagement_scope")]
  [InlineData("ALLOCATION_CLIENT", "staff_allocations", "fk_staff_allocation_engagement_scope")]
  public async Task ResourceRecords_RefuseMismatchedScopedParents(string kind, string table, string constraint)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    var sunday = new DateOnly(2026, 10, 25);
    object Row(Guid id, bool invalid)
    {
      var user = invalid && kind is not ("ALLOCATION_ENGAGEMENT" or "ALLOCATION_CLIENT")
        ? foreign.Admin.Id : invalid ? own.Admin.Id : own.Staff.Id;
      return kind switch
      {
        "PROFILE" => new StaffProfile { Id = id, FirmId = own.FirmId, UserId = user,
          Department = "AUDIT", WeeklyCapacityMinutes = 2400, TargetUtilizationPercent = 75,
          UpdatedAt = now, UpdatedByUserId = own.Admin.Id },
        "CERTIFICATION" => new StaffCertification { Id = id, FirmId = own.FirmId, UserId = user,
          Name = "SYNTHETIC CPA", RecordedAt = now, RecordedByUserId = own.Admin.Id },
        "AVAILABILITY" => new StaffAvailability { Id = id, FirmId = own.FirmId, UserId = user,
          StartDate = sunday, EndDate = sunday, MinutesPerDay = 480,
          RecordedAt = now, RecordedByUserId = own.Admin.Id },
        _ => new StaffAllocation { Id = id, FirmId = own.FirmId, UserId = user,
          ClientId = invalid && kind == "ALLOCATION_CLIENT" ? foreign.ClientId : own.ClientId,
          EngagementId = invalid && kind == "ALLOCATION_ENGAGEMENT" ? foreign.EngagementId : own.EngagementId,
          WeekStart = sunday, PlannedMinutes = 480, UpdatedAt = now, UpdatedByUserId = own.Admin.Id }
      };
    }

    var validId = Guid.NewGuid();
    var invalidId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Add(Row(validId, false));
    await db.SaveChangesAsync(); // Distinct valid subject avoids a unique-index false refusal.
    db.Add(Row(invalidId, true));
    var failure = await Record.ExceptionAsync(() => db.SaveChangesAsync());
    if (failure is null) output.WriteLine($"ACCEPTED_FOREIGN_RESOURCE_PARENT:{kind}");
    var rejected = Assert.IsType<DbUpdateException>(failure);
    var postgres = Assert.IsType<PostgresException>(rejected.InnerException);
    Assert.Equal("23503", postgres.SqlState);
    Assert.Equal(constraint, postgres.ConstraintName);

    // Table identifiers are fixed InlineData above, never caller input.
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE id = @id", connection);
    count.Parameters.AddWithValue("id", invalidId);
    Assert.Equal(0L, await count.ExecuteScalarAsync());
    count.Parameters["id"].Value = validId;
    Assert.Equal(1L, await count.ExecuteScalarAsync());
  }

  private static HashSet<string> ExpectedTriggers()
  {
    var root = AppContext.BaseDirectory;
    for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
      if (File.Exists(Path.Combine(dir.FullName, "AuditSphereOps.slnx"))) { root = dir.FullName; break; }
    var migrations = Directory.GetFiles(Path.Combine(root, "src", "AuditSphereOps.Infrastructure", "Persistence", "Migrations"), "*.cs")
      .Where(file => !file.EndsWith(".Designer.cs") && Regex.IsMatch(Path.GetFileName(file), @"^\d{14}_")).Order(StringComparer.Ordinal);
    var live = new HashSet<string>(StringComparer.Ordinal);
    foreach (var file in migrations)
    {
      // Only the Up direction defines the schema the application runs on.
      var text = File.ReadAllText(file);
      var down = text.IndexOf("void Down(", StringComparison.Ordinal);
      foreach (Match operation in TriggerOperation().Matches(down < 0 ? text : text[..down]))
        if (operation.Groups[1].Value.StartsWith("DROP", StringComparison.OrdinalIgnoreCase)) live.Remove(operation.Groups[2].Value);
        else live.Add(operation.Groups[2].Value);
    }
    return live;
  }

  [GeneratedRegex(@"\b(CREATE\s+(?:OR\s+REPLACE\s+)?(?:CONSTRAINT\s+)?TRIGGER|DROP\s+TRIGGER(?:\s+IF\s+EXISTS)?)\s+""?(\w+)""?", RegexOptions.IgnoreCase)]
  private static partial Regex TriggerOperation();
}
