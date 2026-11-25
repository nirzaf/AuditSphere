using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task PostingSnapshotMigrationBackfillsExistingPostedJournals()
  {
    await using var pg = await PgTestSchema.CreateAsync("20261007154932_ClientManualSettlementOriginConstraints");
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var legacyProfileId = Guid.CreateVersion7();
    var legacyAcceptanceDecisionId = Guid.CreateVersion7();
    var legacyAcceptanceAt = DateTimeOffset.UtcNow;
    Guid periodId;
    Guid chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // This fixture deliberately stops before later acceptance columns were introduced;
      // seed its historical shape instead of asking the current EF model to write future fields.
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO acceptance_decisions
          (id,firm_id,practice_client_id,engagement_id,decision,service_route,generation,decided_by_user_id,decided_at,
           conditions,evaluation_snapshot_digest,evaluation_template_version,rationale,path,prior_decision_id)
        VALUES ({legacyAcceptanceDecisionId},{scope.FirmId},{scope.ClientA},{(Guid?)null},{"Accepted"},{"BOOKKEEPING"},{1L},
          {scope.Reviewer.Id},{legacyAcceptanceAt},{(string?)null},{new string('a', 64)},{"TEST-1"},{"Approved migration fixture"},
          {"NEW_CLIENT"},{(Guid?)null})
        """);
      // This is a pre-history schema: current profile commands also write the later immutable
      // revision table. Seed the actual legacy head without inventing historical snapshots.
      db.ClientAccountingProfiles.Add(new ClientAccountingProfile
      {
        Id = legacyProfileId, FirmId = scope.FirmId, ClientId = scope.ClientA,
        Jurisdiction = "QA", FunctionalCurrency = "QAR", FiscalYearStartMonth = 1,
        FiscalYearStartDay = 1, SourceSystem = "AUDITSPHERE", SourceSystemIdentifier = "MIGRATION-FIXTURE",
        SourceMode = ClientAccountingSourceModes.NativeBookkeeping,
        CreatedByUserId = reviewer.UserId, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var period = await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "IFRS", "QAR"));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "AUDITSPHERE", new DateOnly(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message);
      chartId = chart.Value;
      var accounts = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
        new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
        new("expense", "6000", "Office expense", "EXPENSE", "DEBIT", true)
      ]);
      Assert.True(accounts.Succeeded, accounts.Message);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId)).Succeeded);

    Guid journalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.ClientId == scope.ClientA)
        .OrderBy(x => x.AccountCode).Select(x => new { x.Id, x.AccountCode, x.AccountName }).ToListAsync();
      var cash = accounts.Single(x => x.AccountCode == "1000");
      var expense = accounts.Single(x => x.AccountCode == "6000");
      journalId = Guid.CreateVersion7();
      var commandId = Guid.CreateVersion7();
      var now = DateTimeOffset.UtcNow;
      await using var transaction = await db.Database.BeginTransactionAsync();
      // Seed a legitimate pre-upgrade posted row without EF's current model column,
      // which does not exist until the migration under test runs.
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_operational_journals
          (id,firm_id,client_id,period_id,journal_number,description,posting_date,currency,status,revision,created_by_user_id,created_at)
        VALUES ({journalId},{scope.FirmId},{scope.ClientA},{periodId},{"J-LEGACY-SNAPSHOT"},{"Posted before snapshot migration"},
          {new DateOnly(2026, 1, 15)},{"QAR"},{"DRAFT"},{1L},{scope.Preparer.Id},{now})
        """);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_operational_journal_lines
          (id,firm_id,client_id,journal_id,line_number,client_account_id,account_code,account_name,description,debit,credit)
        VALUES ({Guid.CreateVersion7()},{scope.FirmId},{scope.ClientA},{journalId},{1},{expense.Id},{expense.AccountCode},{expense.AccountName},{"Expense"},{40m},{0m}),
          ({Guid.CreateVersion7()},{scope.FirmId},{scope.ClientA},{journalId},{2},{cash.Id},{cash.AccountCode},{cash.AccountName},{"Cash"},{0m},{40m})
        """);
      await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_operational_journals SET status='SUBMITTED',revision=2,submitted_at={now} WHERE id={journalId}");
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_operational_journal_decisions
          (id,firm_id,client_id,journal_id,journal_revision,decision,reason,actor_user_id,created_at)
        VALUES ({Guid.CreateVersion7()},{scope.FirmId},{scope.ClientA},{journalId},{2L},{"APPROVE"},{"Reviewed before snapshot migration"},{scope.Reviewer.Id},{now})
        """);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE client_operational_journals
        SET status='POSTED',revision=3,posted_by_user_id={scope.Reviewer.Id},posted_at={now}
        WHERE id={journalId}
        """);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO client_operational_posting_receipts
          (id,firm_id,client_id,command_id,journal_id,actor_user_id,submitted_revision,posted_revision,intent_hash,preview_digest,recorded_at)
        VALUES ({Guid.CreateVersion7()},{scope.FirmId},{scope.ClientA},{commandId},{journalId},{scope.Reviewer.Id},{2L},{3L},
          {new string('a', 64)},{new string('b', 64)},{now})
        """);
      await transaction.CommitAsync();
      Assert.Equal(0, await db.Database.SqlQuery<int>($"""
        SELECT count(*)::int AS "Value" FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='client_operational_journals' AND column_name='posting_sequence'
        """).SingleAsync());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.GetService<IMigrator>().MigrateAsync();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var retainedAcceptance = await db.AcceptanceDecisions.AsNoTracking()
        .SingleAsync(x => x.Id == legacyAcceptanceDecisionId);
      Assert.Equal(("Accepted", "BOOKKEEPING", "NEW_CLIENT", (string?)null),
        (retainedAcceptance.Decision, retainedAcceptance.ServiceRoute, retainedAcceptance.Path, retainedAcceptance.OpportunityBasisSha256));
      var journal = await db.ClientOperationalJournals.AsNoTracking().SingleAsync(x => x.Id == journalId);
      Assert.Equal("POSTED", journal.Status);
      Assert.True(journal.PostingSequence > 0);
      Assert.False(await db.ClientAccountingProfileRevisionRecords.AnyAsync(x => x.ProfileId == legacyProfileId));
      var ledger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId, pageSize: 1);
      Assert.True(ledger.Succeeded, ledger.Message);
      Assert.Equal(journal.PostingSequence!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), ledger.Value!.PostingSnapshotThrough);
      Assert.Equal(2, ledger.Value.TotalEntries);
    }
  }
}
