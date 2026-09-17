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
      db.PracticeClients.Add(new PracticeClient { Id = client, FirmId = firm, LegalName = "Migration fixture" });
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
