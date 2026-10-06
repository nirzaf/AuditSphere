using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeJournalRequiresBalancedDraftIndependentApprovalAndPostsOnce()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    string reviewDigest = "";
    Guid periodId;
    Guid chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Approved test bookkeeping service", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('f', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var profile = await ClientAccountingService.CreateProfileAsync(db, reviewer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE-1",
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
    {
      var publication = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId);
      Assert.True(publication.Succeeded, publication.Message);
    }

    Guid journalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, preparer,
        new ClientOperationalJournalCreateRequest(scope.ClientA, periodId, "J-001", "Record office expense",
          new DateOnly(2026, 1, 15), [
            new("6000", "Monthly rent", 125m, 0m), new("1000", "Cash", 0m, 125m)
          ]));
      Assert.True(created.Succeeded, created.Message);
      journalId = created.Value;
      var view = await ClientOperationalLedgerWorkspace.GetAsync(db, preparer, scope.ClientA, journalId);
      Assert.True(view.Succeeded, view.Message);
      Assert.Equal("DRAFT", view.Value!.Status);
      Assert.Equal("125.000000", view.Value.Lines[0].Debit);
      await db.ClientOperationalJournalLines.Where(x => x.JournalId == journalId && x.Debit > 0)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Debit, 126m));
      await AssertNativeRuntimeSqlDeniedAsync(pg, journalId, scope.Reviewer.Id,
        ["UPDATE client_operational_journals SET status='SUBMITTED', revision=2, submitted_at=now() WHERE id=@journal"]);
      Assert.Equal("DRAFT", (await db.ClientOperationalJournals.AsNoTracking().SingleAsync(x => x.Id == journalId)).Status);
      await db.ClientOperationalJournalLines.Where(x => x.JournalId == journalId && x.Debit > 0)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Debit, 125m));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, journalId);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.Equal("125.000000", preview.Value!.TotalDebit);
      Assert.Equal(64, preview.Value.Digest.Length);
      Assert.Equal(preview.Value.Digest, (await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, journalId)).Value!.Digest);
      Assert.Empty(await db.ClientOperationalJournalDecisions.ToListAsync());
      var missingPreview = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, journalId, 1);
      Assert.False(missingPreview.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, missingPreview.ErrorCode);
      await db.ClientOperationalJournals.Where(x => x.Id == journalId).ExecuteUpdateAsync(x => x.SetProperty(j => j.Description, "Changed draft intent"));
      db.ChangeTracker.Clear();
      var stalePreview = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, journalId, 1, previewDigest: preview.Value.Digest);
      Assert.False(stalePreview.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stalePreview.ErrorCode);
      db.ChangeTracker.Clear();
      var refreshed = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, journalId);
      Assert.True(refreshed.Succeeded, refreshed.Message);
      Assert.NotEqual(preview.Value.Digest, refreshed.Value!.Digest);
      var submitted = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, journalId, 1, previewDigest: refreshed.Value.Digest);

      Assert.True(submitted.Succeeded, submitted.Message);
      var reviewPreview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, scope.ClientA, journalId);
      Assert.True(reviewPreview.Succeeded, reviewPreview.Message);
      reviewDigest = reviewPreview.Value!.Digest;
      Assert.NotEqual(refreshed.Value.Digest, reviewDigest);
      var staleApproval = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, journalId,
        new(2, "APPROVE", "Old draft preview", refreshed.Value.Digest));
      Assert.False(staleApproval.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleApproval.ErrorCode);
      var selfApproval = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, preparer, scope.ClientA, journalId,
        new ClientOperationalJournalDecisionRequest(2, "APPROVE", "Reviewed"));
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, selfApproval.ErrorCode);
      var readiness = await PeriodCloseReadinessQuery.GetReadinessAsync(db, reviewer, periodId);
      Assert.True(readiness.Succeeded, readiness.Message);
      Assert.Contains(readiness.Value!.Blockers, x => x.Code == "native-journals.unposted");
      var prematureClose = await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Premature close");
      Assert.False(prematureClose.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, prematureClose.ErrorCode);
      await AssertNativeRuntimeSqlDeniedAsync(pg, journalId, scope.Reviewer.Id,
      [
        "UPDATE client_operational_journals SET status='POSTED', revision=revision+1, posted_by_user_id=@reviewer, posted_at=now() WHERE id=@journal",
        "UPDATE client_operational_journal_lines SET debit=126 WHERE journal_id=@journal AND debit>0",
        "UPDATE client_operational_journals SET revision=revision+1 WHERE id=@journal",
        "UPDATE client_operational_journals SET status='RETURNED', revision=revision+1 WHERE id=@journal",
        "UPDATE client_reporting_periods SET status='CLOSED' WHERE id=(SELECT period_id FROM client_operational_journals WHERE id=@journal)"
      ]);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.False((await ClientOperationalLedgerWorkspace.ReturnAsync(db, preparer, scope.ClientA, journalId, 2, "Self return")).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.ReturnAsync(db, reviewer, scope.ClientA, journalId, 2, " ")).Succeeded);
      var returned = await ClientOperationalLedgerWorkspace.ReturnAsync(db, reviewer, scope.ClientA, journalId, 2, "Clarify expense evidence");
      Assert.True(returned.Succeeded, returned.Message);
      var history = await ClientOperationalLedgerWorkspace.GetAsync(db, preparer, scope.ClientA, journalId);
      Assert.Equal("RETURNED", history.Value!.Status);
      Assert.Equal("Clarify expense evidence", Assert.Single(history.Value.Decisions!).Reason);
      Assert.Equal("2", history.Value.Decisions![0].Revision);
      Assert.Equal(scope.Reviewer.Id, history.Value.Decisions[0].ActorUserId);
      Assert.Equal("125.000000", history.Value.Lines[0].Debit);
      await AssertNativeRuntimeSqlDeniedAsync(pg, journalId, scope.Reviewer.Id,
      [
        "UPDATE client_operational_journals SET description='Overwritten return history' WHERE id=@journal",
        "UPDATE client_operational_journal_lines SET description='Overwritten reviewed line' WHERE journal_id=@journal",
        "UPDATE client_operational_journal_decisions SET reason='Overwritten reason' WHERE journal_id=@journal"
      ]);
      var fresh = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, journalId);
      Assert.True(fresh.Succeeded, fresh.Message);
      var resubmitted = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, journalId, 3, previewDigest: fresh.Value!.Digest);
      Assert.True(resubmitted.Succeeded, resubmitted.Message);
      var review = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, scope.ClientA, journalId);
      Assert.True(review.Succeeded, review.Message);
      reviewDigest = review.Value!.Digest;
    }

    async Task<CommandResult> PostOnce()
    {
      await using var concurrent = new AuditSphereDbContext(pg.Options);
      return await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(concurrent, reviewer, scope.ClientA, journalId,
        new(4, "APPROVE", "Balanced and supported", reviewDigest));
    }
    async Task<CommandResult> CloseConcurrently()
    {
      await using var concurrent = new AuditSphereDbContext(pg.Options);
      return await ClientAccountingService.ClosePeriodAsync(concurrent, reviewer, periodId, "Concurrent native close");
    }
    var concurrentOutcomes = await Task.WhenAll(PostOnce(), PostOnce(), CloseConcurrently());
    Assert.True(concurrentOutcomes[0].Succeeded, concurrentOutcomes[0].Message);
    Assert.True(concurrentOutcomes[1].Succeeded, concurrentOutcomes[1].Message);
    if (!concurrentOutcomes[2].Succeeded) Assert.Equal(ErrorCodes.GateBlocked, concurrentOutcomes[2].ErrorCode);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var posted = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, journalId,
        new ClientOperationalJournalDecisionRequest(4, "APPROVE", "Balanced and supported", reviewDigest));
      Assert.True(posted.Succeeded, posted.Message);
      var replay = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, journalId,
        new ClientOperationalJournalDecisionRequest(4, "APPROVE", "Balanced and supported", reviewDigest));
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(2, await db.ClientOperationalJournalDecisions.CountAsync(x => x.FirmId == scope.FirmId && x.JournalId == journalId));
      var journal = await db.ClientOperationalJournals.SingleAsync(x => x.FirmId == scope.FirmId && x.Id == journalId);
      Assert.Equal("POSTED", journal.Status);
      Assert.Equal(scope.Reviewer.Id, journal.PostedByUserId);
      var ledger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId);
      Assert.True(ledger.Succeeded, ledger.Message);
      Assert.Equal(2, ledger.Value!.TotalEntries);
      Assert.Equal("125.000000", ledger.Value.Accounts.Single(x => x.AccountCode == "6000").DebitMovement);
      Assert.Equal("-125.000000", ledger.Value.Accounts.Single(x => x.AccountCode == "1000").NetMovement);
      await AssertNativeRuntimeSqlDeniedAsync(pg, journalId, scope.Reviewer.Id,
      [
        "UPDATE client_operational_journals SET description='Changed' WHERE id=@journal",
        "DELETE FROM client_operational_journals WHERE id=@journal",
        "UPDATE client_operational_journal_lines SET debit=126 WHERE journal_id=@journal AND debit>0",
        "DELETE FROM client_operational_journal_lines WHERE journal_id=@journal",
        "INSERT INTO client_operational_journal_lines SELECT gen_random_uuid(),firm_id,client_id,journal_id,3,client_account_id,account_code,account_name,description,debit,credit FROM client_operational_journal_lines WHERE journal_id=@journal LIMIT 1",
        "UPDATE client_operational_journal_decisions SET reason='Changed' WHERE journal_id=@journal",
        "DELETE FROM client_operational_journal_decisions WHERE journal_id=@journal"
      ]);
      if ((await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId)).Status != AccountingWorkflowStates.Closed)
      {
        var close = await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "All native journals independently posted");
        Assert.True(close.Succeeded, close.Message);
      }
      var afterClose = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, preparer,
        new(scope.ClientA, periodId, "J-CLOSED", "Invalid closed-period journal", new DateOnly(2026, 1, 15),
          [new("6000", "Expense", 10m, 0m), new("1000", "Cash", 0m, 10m)]));
      Assert.False(afterClose.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, afterClose.ErrorCode);
    }
  }

  private static async Task AssertNativeRuntimeSqlDeniedAsync(PgTestSchema pg, Guid journalId, Guid reviewerId,
    IReadOnlyList<string> statements)
  {
    // The role has application DML only, with no table ownership, schema DDL or trigger controls.
    var role = "native_writer_" + Guid.NewGuid().ToString("N");
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using (var setup = new NpgsqlCommand($"""
      CREATE ROLE {role} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
      GRANT USAGE ON SCHEMA {pg.Schema} TO {role};
      GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {pg.Schema} TO {role};
      SET ROLE {role};
      """, connection)) await setup.ExecuteNonQueryAsync();
    try
    {
      await using (var identity = new NpgsqlCommand("SELECT current_user", connection))
        Assert.Equal(role, await identity.ExecuteScalarAsync());
      foreach (var sql in statements)
      {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("journal", journalId);
        command.Parameters.AddWithValue("reviewer", reviewerId);
        var error = await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync());
        if (error is not PostgresException)
        {
          await using var observed = new NpgsqlCommand("SELECT status || ':' || revision::text FROM client_operational_journals WHERE id=@journal", connection);
          observed.Parameters.AddWithValue("journal", journalId);
          var state = await observed.ExecuteScalarAsync();
          Assert.Fail($"Expected database guard rejection for: {sql}; actual: {error?.GetType().Name ?? "no error"}; state: {state}");
        }
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)error!).SqlState);
      }
    }
    finally
    {
      await using var cleanup = new NpgsqlCommand($"RESET ROLE; DROP OWNED BY {role}; DROP ROLE {role};", connection);
      await cleanup.ExecuteNonQueryAsync();
    }
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeJournalRejectsUnbalancedAmountsAndSiblingClientReads()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, preparer,
      new ClientOperationalJournalCreateRequest(scope.ClientA, Guid.NewGuid(), "J-BAD", "Bad balance",
        new DateOnly(2026, 1, 15), [new("1000", "Debit", 10m, 0m), new("4000", "Credit", 0m, 9m)]));
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, result.ErrorCode);

    var denied = await ClientOperationalLedgerWorkspace.GetAsync(db, preparer, scope.ClientB, Guid.NewGuid());
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
  }
}
