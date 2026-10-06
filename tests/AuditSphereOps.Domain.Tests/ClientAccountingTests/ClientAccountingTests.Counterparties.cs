using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  public async Task CounterpartiesAreClientScopedOptionalTaxAndImmutable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var actor = Actor(scope.Preparer, "AccountingPreparer");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      foreach (var clientId in new[] { scope.ClientA, scope.ClientB })
      {
        db.AcceptanceDecisions.Add(new AcceptanceDecision {
          Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = clientId,
          ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
          Rationale = "Synthetic authorized bookkeeping", EvaluationTemplateVersion = "TEST-1",
          EvaluationSnapshotDigest = new string('f', 64), DecidedByUserId = scope.Reviewer.Id,
          DecidedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var profile = await ClientAccountingService.CreateProfileAsync(db, Actor(scope.Reviewer, "AccountingReviewer"),
          new ClientAccountingProfileRequest(clientId, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE", ClientAccountingSourceModes.NativeBookkeeping));
        Assert.True(profile.Succeeded, profile.Message);
      }
    }
    var request = new ClientCounterpartyCreateRequest("Example Trading", "Example", "BOTH", "", "QA", "", "", "", "QAR", "LEGACY", "001");
    Guid partyId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, actor, scope.ClientA, request);
      Assert.True(created.Succeeded, created.Message); partyId = created.Value;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, actor, scope.ClientA, request with { LegalName = "Other" })).Succeeded);
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, actor, scope.ClientA, request with { ExternalSystem = "", ExternalReference = "", LegalName = "example trading" })).Succeeded);
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, actor, scope.ClientA, request with { LegalName = "Euro", DefaultCurrency = "EUR" })).Succeeded);
      Assert.True((await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, actor, scope.ClientB, request)).Succeeded);
      foreach (var role in new[] { "CUSTOMER", "SUPPLIER", "BOTH" })
      {
        var listed = await ClientBookkeepingCounterpartyWorkspace.ListAsync(db, actor, scope.ClientA, role);
        Assert.True(listed.Succeeded, listed.Message); Assert.Equal(1, listed.Value!.Total);
        var row = Assert.Single(listed.Value.Counterparties); Assert.Equal(partyId, row.Id); Assert.Equal("", row.TaxIdentifier);
      }
      db.AcceptanceDecisions.Add(new AcceptanceDecision {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Declined", Generation = 2,
        Rationale = "Synthetic service withdrawal", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('e', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, actor, scope.ClientA, request with { LegalName = "New", ExternalReference = "002" })).Succeeded);
      var history = await ClientBookkeepingCounterpartyWorkspace.ListAsync(db, actor, scope.ClientA);
      Assert.True(history.Succeeded); Assert.False(history.Value!.BookkeepingActive); Assert.Single(history.Value.Counterparties);
      var grants = await db.RoleGrants.Where(x => x.UserId == scope.Preparer.Id).ToListAsync();
      foreach (var grant in grants) { grant.ClientId = scope.ClientA; }
      await db.SaveChangesAsync();
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.ListAsync(db, actor, scope.ClientB)).Succeeded);
    }
    foreach (var sql in new[] { "UPDATE client_bookkeeping_counterparties SET display_name='Changed' WHERE id={0}", "DELETE FROM client_bookkeeping_counterparties WHERE id={0}" })
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, partyId));
      Assert.Equal("23514", error.SqlState);
    }
  }
}
