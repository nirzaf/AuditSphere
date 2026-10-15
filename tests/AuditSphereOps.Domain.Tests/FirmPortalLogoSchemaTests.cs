using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class FirmPortalLogoSchemaTests
{
  private const string BeforeLogoMigration = "20261015113704_ClientBundleReceiptAcknowledgements";
  private const string LogoMigration = "20261016000000_FirmPortalLogoFields";
  private const string TestPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAN0lEQVR4nO3QQREAMAgDQYoYvGGtJstUBZ+Ngcvsqb4vFpebcQcIECBAgAABAgQIECBAgACBLzDWWQJ4Rpw0uQAAAABJRU5ErkJggg==";

  [Fact]
  public async Task UpgradePreservesLegacyProfileAndDocumentAndEnforcesLogoPairConstraint()
  {
    await using var pg = await PgTestSchema.CreateAsync(BeforeLogoMigration);
    var firmId = Guid.NewGuid();
    var profileId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var documentId = Guid.NewGuid();
    var documentBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
    var documentHash = Hashing.Sha256Hex(documentBytes);

    await using (var connection = new NpgsqlConnection(pg.ConnectionString))
    {
      await connection.OpenAsync();
      await ExecuteAsync(connection, """
        INSERT INTO firm_commercial_profiles
          (id, firm_id, version, legal_name, address, contact_email, contact_phone, accent_color_hex, closing_text,
           firm_history_and_registrations, industry_credentials, audit_methodology, created_by_user_id, created_at)
        VALUES (@id, @firm, 1, 'Synthetic legacy firm', 'Doha', 'old@example.test', '123', '#2B6CB0', 'closing',
                '', '', '', @user, now())
        """, ("id", profileId), ("firm", firmId), ("user", userId));
      await ExecuteAsync(connection, """
        INSERT INTO commercial_documents
          (id, firm_id, kind, template_version, profile_version, file_name, content_type, bytes, sha256_hex,
           created_by_user_id, created_at)
        VALUES (@id, @firm, 'QUOTATION', 'test-template', 1, 'legacy.docx',
                'application/vnd.openxmlformats-officedocument.wordprocessingml.document', @bytes, @hash, @user, now())
        """, ("id", documentId), ("firm", firmId), ("bytes", documentBytes), ("hash", documentHash), ("user", userId));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.GetService<IMigrator>().MigrateAsync(LogoMigration);

    var png = Convert.FromBase64String(TestPngBase64);
    var pngHash = Hashing.Sha256Hex(png);
    await using (var connection = new NpgsqlConnection(pg.ConnectionString))
    {
      await connection.OpenAsync();
      await using (var profile = new NpgsqlCommand(
        "SELECT legal_name, address, contact_email, logo_png_bytes, logo_sha256_hex FROM firm_commercial_profiles WHERE id = @id", connection))
      {
        profile.Parameters.AddWithValue("id", profileId);
        await using var reader = await profile.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Synthetic legacy firm", reader.GetString(0));
        Assert.Equal("Doha", reader.GetString(1));
        Assert.Equal("old@example.test", reader.GetString(2));
        Assert.True(reader.IsDBNull(3));
        Assert.True(reader.IsDBNull(4));
      }
      await using (var document = new NpgsqlCommand("SELECT sha256_hex FROM commercial_documents WHERE id = @id", connection))
      {
        document.Parameters.AddWithValue("id", documentId);
        Assert.Equal(documentHash, await document.ExecuteScalarAsync());
      }

      await InsertProfileAsync(connection, Guid.NewGuid(), firmId, 2, userId, png, pngHash);
      var invalidPairs = new (byte[]? Bytes, string? Hash)[]
      {
        (png, null),
        ([], pngHash),
        (new byte[131073], new string('a', 64)),
        (png, new string('A', 64)),
        (png, new string('g', 64)),
        (png, new string('a', 63))
      };
      for (var index = 0; index < invalidPairs.Length; index++)
      {
        var invalid = invalidPairs[index];
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertProfileAsync(connection,
          Guid.NewGuid(), firmId, index + 3, userId, invalid.Bytes, invalid.Hash));
        Assert.Equal("23514", exception.SqlState);
        Assert.Equal("ck_firm_profile_portal_logo", exception.ConstraintName);
      }
    }
  }

  [Fact]
  public async Task NullLogoDowngradeRoundTripsAndRetainedLogoBlocksDowngradeWithoutChangingEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync(BeforeLogoMigration);
    var firmId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var legacyId = Guid.NewGuid();
    await using (var connection = new NpgsqlConnection(pg.ConnectionString))
    {
      await connection.OpenAsync();
      await ExecuteAsync(connection, """
        INSERT INTO firm_commercial_profiles
          (id, firm_id, version, legal_name, address, contact_email, contact_phone, accent_color_hex, closing_text,
           firm_history_and_registrations, industry_credentials, audit_methodology, created_by_user_id, created_at)
        VALUES (@id, @firm, 1, 'Synthetic legacy firm', '', '', '', '#2B6CB0', '', '', '', '', @user, now())
        """, ("id", legacyId), ("firm", firmId), ("user", userId));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var migrator = db.GetService<IMigrator>();
      await migrator.MigrateAsync(LogoMigration);
      await migrator.MigrateAsync(BeforeLogoMigration);
      await migrator.MigrateAsync(LogoMigration);
    }

    var png = Convert.FromBase64String(TestPngBase64);
    var hash = Hashing.Sha256Hex(png);
    var retainedId = Guid.NewGuid();
    await using var retainedDb = new AuditSphereDbContext(pg.Options);
    await using (var connection = new NpgsqlConnection(pg.ConnectionString))
    {
      await connection.OpenAsync();
      await InsertProfileAsync(connection, retainedId, firmId, 2, userId, png, hash);
    }

    var refused = await Assert.ThrowsAsync<PostgresException>(() => retainedDb.GetService<IMigrator>().MigrateAsync(BeforeLogoMigration));
    Assert.Equal("P0001", refused.SqlState);
    Assert.Equal("Retained portal logo evidence prevents downgrade.", refused.MessageText);
    Assert.Contains(LogoMigration, await retainedDb.Database.GetAppliedMigrationsAsync());
    var retained = await retainedDb.FirmCommercialProfiles.AsNoTracking().SingleAsync(x => x.Id == retainedId);
    Assert.Equal(png, retained.LogoPngBytes);
    Assert.Equal(hash, retained.LogoSha256Hex);

    var immutableUpdate = await Assert.ThrowsAsync<PostgresException>(() => retainedDb.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE firm_commercial_profiles SET legal_name = 'rewritten' WHERE id = {retainedId}"));
    Assert.Contains("append-only", immutableUpdate.MessageText, StringComparison.OrdinalIgnoreCase);
    var immutableDelete = await Assert.ThrowsAsync<PostgresException>(() => retainedDb.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM firm_commercial_profiles WHERE id = {retainedId}"));
    Assert.Contains("append-only", immutableDelete.MessageText, StringComparison.OrdinalIgnoreCase);

    retainedDb.FirmCommercialProfiles.Add(new()
    {
      Id = Guid.NewGuid(), FirmId = firmId, Version = 3, LegalName = "Synthetic next version", AccentColorHex = "#2B6CB0",
      CreatedByUserId = userId, CreatedAt = DateTimeOffset.UtcNow
    });
    await retainedDb.SaveChangesAsync();
    Assert.Equal(3, await retainedDb.FirmCommercialProfiles.CountAsync(x => x.FirmId == firmId));
  }

  private static async Task InsertProfileAsync(NpgsqlConnection connection, Guid id, Guid firmId, long version,
    Guid userId, byte[]? bytes, string? hash)
  {
    await ExecuteAsync(connection, """
      INSERT INTO firm_commercial_profiles
        (id, firm_id, version, legal_name, address, contact_email, contact_phone, accent_color_hex, closing_text,
         firm_history_and_registrations, industry_credentials, audit_methodology, logo_png_bytes, logo_sha256_hex,
         created_by_user_id, created_at)
      VALUES (@id, @firm, @version, 'Synthetic firm', '', '', '', '#2B6CB0', '', '', '', '', @bytes, @hash, @user, now())
      """, ("id", id), ("firm", firmId), ("version", version), ("bytes", bytes), ("hash", hash), ("user", userId));
  }

  private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
  {
    await using var command = new NpgsqlCommand(sql, connection);
    foreach (var (name, value) in parameters)
    {
      var parameter = new NpgsqlParameter(name, value ?? DBNull.Value);
      if (value is null)
        parameter.NpgsqlDbType = name == "bytes" ? NpgsqlDbType.Bytea : NpgsqlDbType.Text;
      command.Parameters.Add(parameter);
    }
    await command.ExecuteNonQueryAsync();
  }
}
