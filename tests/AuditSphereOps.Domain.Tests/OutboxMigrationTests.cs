using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class OutboxMigrationTests
{
  private const string Previous = "20260917091412_AccountingIntegrity";

  [Fact]
  [Trait("Profile", "Database")]
  public async Task UpgradeFromPreviousSchema_PreservesAccountingAndProvisionsLocalGuards()
  {
    await using var pg = await PgTestSchema.CreateAsync(Previous);
    var firm = Guid.NewGuid(); var client = Guid.NewGuid(); var engagement = Guid.NewGuid(); var dataset = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // This context intentionally targets the pre-CRM schema; seed the unchanged
      // legacy table shape before asking the current model to migrate it forward.
      const string legacyName = "Migration fixture";
      const string legacyStatus = "Prospect";
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO practice_clients (id, firm_id, legal_name, commercial_name, status, billing_account_id, created_at)
        VALUES ({client}, {firm}, {legacyName}, NULL, {legacyStatus}, NULL, statement_timestamp())
        """);
      db.Engagements.Add(new Engagement { Id = engagement, FirmId = firm, PracticeClientId = client });
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset { Id = dataset, FirmId = firm,
        ClientId = client, EngagementId = engagement, Currency = "QAR" });
      db.TrialBalanceRows.Add(new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = dataset,
        AccountCode = "001", Amount = 123.456789m, Currency = "QAR" });
      await db.SaveChangesAsync();
      await db.Database.MigrateAsync();
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(123.456789m, (await verify.TrialBalanceRows.SingleAsync()).Amount);
    Assert.Equal("LOCAL_ONLY", (await verify.FirmSafetyStates.SingleAsync()).OperatingMode);
    Assert.Equal(client, (await verify.ClientSafetyStates.SingleAsync()).Id);
    Assert.Empty(await verify.DurableOperations.ToListAsync());
    Assert.Empty(await verify.Database.GetPendingMigrationsAsync());
    Assert.False(verify.Database.HasPendingModelChanges());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task LegacyOperations_StopMigrationWithoutModifyingHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync(Previous);
    await using var db = new AuditSphereDbContext(pg.Options);
    var id = Guid.NewGuid();
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO durable_operations
        (id, firm_id, operation_kind, payload_json, idempotency_key, request_digest, status, attempt, created_at)
      VALUES ({id}, {Guid.NewGuid()}, 'Legacy', {"{}"}, 'legacy-key', '', 'Leased', 3, statement_timestamp())
      """);
    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
    Assert.Contains("disposition of legacy operations", error.MessageText);
    var attempt = await db.Database.SqlQuery<int>($"SELECT attempt AS \"Value\" FROM durable_operations WHERE id = {id}").SingleAsync();
    Assert.Equal(3, attempt);
    var pending = await db.Database.GetPendingMigrationsAsync();
    Assert.Contains("20260917104422_DurableOutbox", pending);
    // The twelve post-AccountingIntegrity migrations remain unapplied, including
    // the mapping and financial-statement workflow.
    Assert.Equal(12, pending.Count());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task LegacyFirmLedgerRows_StopMigrationWithoutInventingAccountType()
  {
    await using var pg = await PgTestSchema.CreateAsync(Previous);
    await using var db = new AuditSphereDbContext(pg.Options);
    var accountId = Guid.NewGuid();
    var firmId = Guid.NewGuid();
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO firm_accounts (id, firm_id, code, name, normal_side, posting_allowed)
      VALUES ({accountId}, {firmId}, '1000', 'Legacy cash', 'Debit', TRUE)
      """);

    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
    Assert.Contains("explicit legacy disposition", error.MessageText);
    var preserved = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM firm_accounts WHERE id = {accountId}").SingleAsync();
    Assert.Equal(accountId, preserved);
    Assert.Equal(5, (await db.Database.GetPendingMigrationsAsync()).Count());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task LegacyDocumentRows_StopMigrationWithoutInventingScopeOrHash()
  {
    await using var pg = await PgTestSchema.CreateAsync(Previous);
    await using var db = new AuditSphereDbContext(pg.Options);
    var id = Guid.NewGuid();
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO document_references
        (id, firm_id, client_id, engagement_id, provider, drive_id, item_id, path, purpose, created_at)
      VALUES ({id}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'SharePoint', 'drive', 'item', '/legacy', 'Evidence', statement_timestamp())
      """);

    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
    Assert.Contains("ambiguous document reference", error.MessageText);
    var preserved = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM document_references WHERE id = {id}").SingleAsync();
    Assert.Equal(id, preserved);
    Assert.Equal(4, (await db.Database.GetPendingMigrationsAsync()).Count());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task LegacyApprovals_StopMigrationWithoutInventingDecisionApplicability()
  {
    await using var pg = await PgTestSchema.CreateAsync(Previous);
    await using var db = new AuditSphereDbContext(pg.Options);
    var id = Guid.NewGuid();
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO approvals
        (id, firm_id, client_id, engagement_id, target_kind, target_id, target_revision,
         input_generation, policy_generation, manifest_digest, decided_by_user_id, decided_at)
      VALUES ({id}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'WORKPAPER', {Guid.NewGuid()},
              1, 1, 1, {new string('a', 64)}, {Guid.NewGuid()}, statement_timestamp())
      """);

    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
    Assert.Contains("explicit disposition", error.MessageText);
    var preserved = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM approvals WHERE id = {id}").SingleAsync();
    Assert.Equal(id, preserved);
    Assert.Equal(3, (await db.Database.GetPendingMigrationsAsync()).Count());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task LegacyReleases_StopMigrationWithoutInventingCandidateIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync(Previous);
    await using var db = new AuditSphereDbContext(pg.Options);
    var id = Guid.NewGuid();
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO releases
        (id, firm_id, client_id, engagement_id, package_id, package_revision,
         manifest_digest, external_checkpoint, released_at, released_by_user_id)
      VALUES ({id}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
              1, {new string('a', 64)}, TRUE, statement_timestamp(), {Guid.NewGuid()})
      """);

    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
    Assert.Contains("explicit disposition", error.MessageText);
    var preserved = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM releases WHERE id = {id}").SingleAsync();
    Assert.Equal(id, preserved);
    Assert.Equal(2, (await db.Database.GetPendingMigrationsAsync()).Count());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task LegacyFinancialPackages_StopMigrationWithoutInventingCalculationIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync(Previous);
    await using var db = new AuditSphereDbContext(pg.Options);
    var id = Guid.NewGuid();
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO financial_packages
        (id, firm_id, client_id, engagement_id, adjusted_dataset_id, currency,
         revision, generation, status, created_at)
      VALUES ({id}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
              'QAR', 1, 1, 'Legacy', statement_timestamp())
      """);

    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
    Assert.Contains("explicit disposition for existing financial package rows", error.MessageText);
    var preserved = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM financial_packages WHERE id = {id}").SingleAsync();
    Assert.Equal(id, preserved);
    Assert.Single(await db.Database.GetPendingMigrationsAsync());
  }

  [Theory]
  [Trait("Profile", "Unit")]
  [InlineData("Production", false, false)]
  [InlineData("Staging", false, false)]
  [InlineData("Development", true, false)]
  [InlineData("Test", false, false)]
  [InlineData("Test", true, true)]
  public void SimulationAndLiveExecution_AreRejectedOutsideExplicitTestComposition(string environment, bool simulations, bool effects)
  {
    var definition = new OperationDefinition("probe", OperationMode.SIMULATED, OperationAuthority.SIMULATION);
    var options = new WorkerOptions(Guid.NewGuid(), environment, simulations, effects);
    Assert.Throws<InvalidOperationException>(() => options.Validate([definition]));
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void LocalValidation_RequiresFirm_AndNeverEnablesLiveAdapters()
  {
    new WorkerOptions(Guid.NewGuid()).Validate([new("ValidateTrialBalance.v1", OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION)]);
    Assert.Throws<InvalidOperationException>(() => new WorkerOptions(Guid.Empty).Validate([]));
    Assert.Throws<InvalidOperationException>(() => new WorkerOptions(Guid.NewGuid(), "Test", true)
      .Validate([new("live", OperationMode.LIVE, OperationAuthority.SIMULATION)]));
  }
}
