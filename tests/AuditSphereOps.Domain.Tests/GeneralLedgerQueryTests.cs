using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Module 21 read-side contracts: typed GL line filters and the per-journal
/// drill-down are scope-checked, paged, and honest about balance totals.</summary>
public sealed class GeneralLedgerQueryTests
{
  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId, Guid BatchId, AppUser Preparer, AppUser Outsider);

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GlLines_TypedFilters_PagingAndTotals()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var unfiltered = await GeneralLedgerQuery.GetLinesAsync(db, preparer, fixture.BatchId);
      Assert.True(unfiltered.Succeeded, unfiltered.Message);
      Assert.Equal(4, unfiltered.Value!.TotalCount);
      Assert.Equal(150m, unfiltered.Value.TotalDebit);
      Assert.Equal(150m, unfiltered.Value.TotalCredit);
      Assert.True(unfiltered.Value.Balanced);

      var cashOnly = await GeneralLedgerQuery.GetLinesAsync(db, preparer, fixture.BatchId,
        new GeneralLedgerLineFilter(AccountCodePrefix: "1000"));
      Assert.Equal(2, cashOnly.Value!.TotalCount);
      Assert.Equal(150m, cashOnly.Value.TotalDebit);
      Assert.All(cashOnly.Value.Items, x => Assert.Equal("1000", x.AccountCode));

      var lateJournal = await GeneralLedgerQuery.GetLinesAsync(db, preparer, fixture.BatchId,
        new GeneralLedgerLineFilter(PostedFrom: new DateOnly(2026, 9, 1)));
      Assert.Equal(2, lateJournal.Value!.TotalCount);
      Assert.All(lateJournal.Value.Items, x => Assert.Equal("J-002", x.StableJournalId));

      var oneJournal = await GeneralLedgerQuery.GetLinesAsync(db, preparer, fixture.BatchId,
        new GeneralLedgerLineFilter(StableJournalId: "J-001"));
      Assert.Equal(2, oneJournal.Value!.TotalCount);
      Assert.All(oneJournal.Value.Items, x => Assert.Equal("J-001", x.StableJournalId));

      var paged = await GeneralLedgerQuery.GetLinesAsync(db, preparer, fixture.BatchId, page: 2, pageSize: 3);
      Assert.Equal(4, paged.Value!.TotalCount);
      Assert.Single(paged.Value.Items);

      var outsider = Actor(fixture.Outsider, "AccountingPreparer");
      var denied = await GeneralLedgerQuery.GetLinesAsync(db, outsider, fixture.BatchId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task JournalDrillDown_ReturnsAllLinesAndBalanceVerdict()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = await GeneralLedgerQuery.GetJournalDrillDownAsync(db, preparer, fixture.BatchId, "J-001");
      Assert.True(journal.Succeeded, journal.Message);
      Assert.Equal(2, journal.Value!.LineCount);
      Assert.Equal(100m, journal.Value.TotalDebit);
      Assert.Equal(100m, journal.Value.TotalCredit);
      Assert.True(journal.Value.Balanced);
      Assert.Equal(new DateOnly(2026, 6, 30), journal.Value.PostingDate);
      Assert.Contains(journal.Value.Lines, x => x.AccountCode == "1000" && x.Debit == 100m);
      Assert.Contains(journal.Value.Lines, x => x.AccountCode == "4000" && x.Credit == 100m);

      var missing = await GeneralLedgerQuery.GetJournalDrillDownAsync(db, preparer, fixture.BatchId, "J-MISSING");
      Assert.False(missing.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, missing.ErrorCode);

      var invalid = await GeneralLedgerQuery.GetJournalDrillDownAsync(db, preparer, fixture.BatchId, " ");
      Assert.False(invalid.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ImportRejected, invalid.ErrorCode);
    }
  }

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
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    var outsider = User(firmId, "outsider");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "GL QUERY CLIENT", CreatedAt = DateTimeOffset.UtcNow
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
    await db.SaveChangesAsync();

    var preparerActor = Actor(preparer, "AccountingPreparer");
    var reviewerActor = Actor(reviewer, "AccountingReviewer");
    Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparerActor,
      new ClientAccountingProfileRequest(clientId, "QA", "QAR", 1, 1, "LEDGER-GLQ", "GLQ-1"))).Succeeded);
    var periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparerActor,
      new ReportingPeriodRequest(clientId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
        "STATUTORY", "QAR"))).Value;
    var book = await ClientAccountingService.CreateBookAsync(db, preparerActor,
      new ReportingBookRequest(clientId, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"));
    Assert.True(book.Succeeded, book.Message);
    var chartId = (await ClientAccountingService.CreateChartVersionAsync(db, preparerActor, clientId, "LEDGER-GLQ",
      new DateOnly(2026, 1, 1))).Value;
    Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparerActor, chartId, [
      new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
      new("revenue", "4000", "Revenue", "INCOME", "CREDIT", true)
    ])).Succeeded);
    Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewerActor, chartId)).Succeeded);
    Assert.True((await ClientAccountingService.AddDimensionDefinitionsAsync(db, preparerActor, clientId,
      [new("INTERCOMPANY_COUNTERPARTY", "IC-PARTNER", "IC Partner")])).Succeeded);

    var imported = await ClientAccountingService.ImportGeneralLedgerAsync(db, preparerActor,
      new GeneralLedgerImportRequest(clientId, engagementId, periodId, book.Value, "csv-v1", "gl-v1",
        new string('7', 64), "GLQ-ENTITY", "QAR", "gl-query-batch",
        [
          new("J-001", "DOC-1", new DateOnly(2026, 6, 30), null, "user-a", "LEDGER-GLQ", null, false, false,
            [new("J-001-L1", "1000", 100m, 0m, "QAR", 100m, 100m),
             new("J-001-L2", "4000", 0m, 100m, "QAR", -100m, -100m)]),
          new("J-002", "DOC-2", new DateOnly(2026, 9, 30), null, "user-a", "LEDGER-GLQ", null, false, false,
            [new("J-002-L1", "1000", 50m, 0m, "QAR", 50m, 50m, IntercompanyCounterparty: "IC-PARTNER"),
             new("J-002-L2", "4000", 0m, 50m, "QAR", -50m, -50m)])
        ]));
    Assert.True(imported.Succeeded, imported.Message);
    var batchId = imported.Value;

    var batch = await db.SourceImportBatches.AsNoTracking().SingleAsync(x => x.Id == batchId);
    Assert.Equal("SEALED", batch.Status);
    return new Fixture(firmId, clientId, engagementId, batchId, preparer, outsider);
  }
}
