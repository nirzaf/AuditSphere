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
    Guid periodId;
    Guid chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Approved migration fixture", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var profile = await ClientAccountingService.CreateProfileAsync(db, reviewer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "MIGRATION-FIXTURE",
          ClientAccountingSourceModes.NativeBookkeeping));
      Assert.True(profile.Succeeded, profile.Message);
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
      var journal = await db.ClientOperationalJournals.AsNoTracking().SingleAsync(x => x.Id == journalId);
      Assert.Equal("POSTED", journal.Status);
      Assert.True(journal.PostingSequence > 0);
      var ledger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId, pageSize: 1);
      Assert.True(ledger.Succeeded, ledger.Message);
      Assert.Equal(journal.PostingSequence!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), ledger.Value!.PostingSnapshotThrough);
      Assert.Equal(2, ledger.Value.TotalEntries);
    }
  }
}
