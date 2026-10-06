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

  [Fact]
  public async Task CounterpartyAmendmentRequiresIndependentCurrentRevisionAndPreservesOriginal()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid partyId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Synthetic mandate", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('f', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow });
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "AccountingReviewer"));
      await db.SaveChangesAsync();
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, reviewer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE", ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
      var party = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, preparer, scope.ClientA,
        new("Example", "Example", "BOTH", "Original address", "QA", "", "", "", "", "", ""));
      Assert.True(party.Succeeded, party.Message); partyId = party.Value;
    }
    var request = new CounterpartyAmendmentRequest(1, "Example amended", "New address", "", "New contact", "Net 30", "Contact update");
    Guid amendmentId, competingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var proposal = await ClientBookkeepingCounterpartyWorkspace.ProposeAmendmentAsync(db, preparer, scope.ClientA, partyId, request);
      Assert.True(proposal.Succeeded, proposal.Message); amendmentId = proposal.Value;
      var competing = await ClientBookkeepingCounterpartyWorkspace.ProposeAmendmentAsync(db, preparer, scope.ClientA, partyId, request with { Address = "Competing address" });
      Assert.True(competing.Succeeded, competing.Message); competingId = competing.Value;
      var history = await ClientBookkeepingCounterpartyWorkspace.HistoryAsync(db, reviewer, scope.ClientA, partyId);
      Assert.True(history.Succeeded); Assert.Equal("Original address", history.Value!.Current.Address); Assert.Equal("1", history.Value.Current.Revision);
      Assert.Equal(2, history.Value.Amendments.Count); Assert.All(history.Value.Amendments, x => Assert.Null(x.Decision));
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.ReviewAmendmentAsync(db, preparer, scope.ClientA, partyId, amendmentId, 2, "APPROVE", "Self review")).Succeeded);
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.ReviewAmendmentAsync(db, reviewer, scope.ClientB, partyId, amendmentId, 2, "APPROVE", "Wrong client")).Succeeded);
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.ReviewAmendmentAsync(db, reviewer, scope.ClientA, partyId, amendmentId, 3, "APPROVE", "Wrong revision")).Succeeded);
      Assert.True((await ClientBookkeepingCounterpartyWorkspace.ReviewAmendmentAsync(db, reviewer, scope.ClientA, partyId, amendmentId, 2, "APPROVE", "Independently checked address")).Succeeded);
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.ReviewAmendmentAsync(db, reviewer, scope.ClientA, partyId, competingId, 2, "APPROVE", "Stale competing proposal")).Succeeded);
      Assert.True((await ClientBookkeepingCounterpartyWorkspace.ReviewAmendmentAsync(db, reviewer, scope.ClientA, partyId, competingId, 2, "REJECT", "Superseded by approved proposal")).Succeeded);
      Assert.False((await ClientBookkeepingCounterpartyWorkspace.ProposeAmendmentAsync(db, preparer, scope.ClientA, partyId, request)).Succeeded);
      var approvedHistory = await ClientBookkeepingCounterpartyWorkspace.HistoryAsync(db, preparer, scope.ClientA, partyId);
      Assert.True(approvedHistory.Succeeded); Assert.Equal("New address", approvedHistory.Value!.Current.Address); Assert.Equal("2", approvedHistory.Value.Current.Revision);
      Assert.Equal(amendmentId, approvedHistory.Value.Current.EffectiveAmendmentId);
      Assert.Equal("Original address", (await db.ClientBookkeepingCounterparties.AsNoTracking().SingleAsync(x => x.Id == partyId)).Address);
      var listed = await ClientBookkeepingCounterpartyWorkspace.ListAsync(db, preparer, scope.ClientA);
      Assert.Equal("New address", Assert.Single(listed.Value!.Counterparties).Address);
    }
    foreach (var sql in new[] { "UPDATE client_counterparty_amendments SET address='Changed' WHERE id={0}", "DELETE FROM client_counterparty_amendments WHERE id={0}", "UPDATE client_counterparty_amendment_decisions SET reason='Changed' WHERE amendment_id={0}" })
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, amendmentId));
      Assert.Equal("23514", error.SqlState);
    }
  }
}
