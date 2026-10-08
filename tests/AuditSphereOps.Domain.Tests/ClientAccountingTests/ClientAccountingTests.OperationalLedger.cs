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
    var commandId = Guid.CreateVersion7();
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
        new("expense", "6000", "Office expense", "EXPENSE", "DEBIT", true),
        new("unused", "7000", "Unused expense", "EXPENSE", "DEBIT", true)
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
        new(2, "APPROVE", "Old draft preview", refreshed.Value.Digest, Guid.CreateVersion7()));
      Assert.False(staleApproval.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleApproval.ErrorCode);
      var selfApproval = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, preparer, scope.ClientA, journalId,
        new ClientOperationalJournalDecisionRequest(2, "APPROVE", "Reviewed", CommandId: Guid.CreateVersion7()));
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
      var correction = new ClientOperationalJournalReworkRequest(3, "Corrected office expense", new DateOnly(2026, 1, 15),
        [new("6000", "Corrected expense", 150m, 0m), new("1000", "Cash", 0m, 150m)]);
      Assert.False((await ClientOperationalLedgerWorkspace.ReworkAsync(db, reviewer, scope.ClientA, journalId, correction)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.ReworkAsync(db, preparer, scope.ClientA, journalId, correction with { ExpectedRevision = 2 })).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.ReworkAsync(db, preparer, scope.ClientA, journalId, correction with { PostingDate = new DateOnly(2027, 1, 1) })).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.ReworkAsync(db, preparer, scope.ClientB, journalId, correction)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.ReworkAsync(db, preparer, scope.ClientA, journalId, correction with {
        Lines = [new("6000", "Expense", 151m, 0m), new("1000", "Cash", 0m, 150m)] })).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.ReworkAsync(db, preparer, scope.ClientA, journalId, correction with {
        Lines = [new("FOREIGN", "Expense", 150m, 0m), new("1000", "Cash", 0m, 150m)] })).Succeeded);
      var reworked = await ClientOperationalLedgerWorkspace.ReworkAsync(db, preparer, scope.ClientA, journalId, correction);
      Assert.True(reworked.Succeeded, reworked.Message);
      Assert.False((await ClientOperationalLedgerWorkspace.ReworkAsync(db, preparer, scope.ClientA, journalId, correction)).Succeeded);
      var fresh = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, journalId);
      Assert.True(fresh.Succeeded, fresh.Message);
      var resubmitted = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, journalId, 4, previewDigest: fresh.Value!.Digest);
      Assert.True(resubmitted.Succeeded, resubmitted.Message);
      var review = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, scope.ClientA, journalId);
      Assert.True(review.Succeeded, review.Message);
      reviewDigest = review.Value!.Digest;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.False((await ClientOperationalLedgerWorkspace.GetPostingReceiptAsync(db, reviewer, scope.ClientA, commandId)).Succeeded);
      await db.Database.ExecuteSqlRawAsync("""
        CREATE FUNCTION fail_native_receipt_for_test() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Synthetic receipt failure' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER zz_fail_native_receipt BEFORE INSERT ON client_operational_posting_receipts
          FOR EACH ROW EXECUTE FUNCTION fail_native_receipt_for_test();
        """);
      try
      {
        await using var failed = new AuditSphereDbContext(pg.Options);
        await Assert.ThrowsAsync<DbUpdateException>(() => ClientOperationalLedgerWorkspace.ReviewAndPostAsync(failed, reviewer, scope.ClientA, journalId,
          new(5, "APPROVE", "Balanced and supported", reviewDigest, commandId)));
        Assert.Equal("SUBMITTED", (await db.ClientOperationalJournals.AsNoTracking().SingleAsync(x => x.Id == journalId)).Status);
        Assert.Equal(1, await db.ClientOperationalJournalDecisions.CountAsync(x => x.JournalId == journalId));
        Assert.Empty(await db.ClientOperationalPostingReceipts.ToListAsync());
      }
      finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER zz_fail_native_receipt ON client_operational_posting_receipts; DROP FUNCTION fail_native_receipt_for_test();"); }
    }

    async Task<CommandResult> PostOnce()
    {
      await using var concurrent = new AuditSphereDbContext(pg.Options);
      return await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(concurrent, reviewer, scope.ClientA, journalId,
        new(5, "APPROVE", "Balanced and supported", reviewDigest, commandId));
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
        new ClientOperationalJournalDecisionRequest(5, "APPROVE", "Balanced and supported", reviewDigest, commandId));
      Assert.True(posted.Succeeded, posted.Message);
      var replay = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, journalId,
        new ClientOperationalJournalDecisionRequest(5, "APPROVE", "Balanced and supported", reviewDigest, commandId));
      Assert.True(replay.Succeeded, replay.Message);
      var receipt = await ClientOperationalLedgerWorkspace.GetPostingReceiptAsync(db, reviewer, scope.ClientA, commandId);
      Assert.True(receipt.Succeeded, receipt.Message);
      Assert.Equal(journalId, receipt.Value!.JournalId);
      Assert.Equal("5", receipt.Value.SubmittedRevision);
      Assert.Equal("6", receipt.Value.PostedRevision);
      Assert.Single(await db.ClientOperationalPostingReceipts.Where(x => x.ClientId == scope.ClientA).ToListAsync());
      var conflicting = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, journalId,
        new(5, "APPROVE", "Different review reason", reviewDigest, commandId));
      Assert.Equal(ErrorCodes.IdempotencyConflict, conflicting.ErrorCode);
      Assert.False((await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, journalId,
        new(5, "APPROVE", "Balanced and supported", reviewDigest, Guid.CreateVersion7()))).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.GetPostingReceiptAsync(db, preparer, scope.ClientA, commandId)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.GetPostingReceiptAsync(db, reviewer, scope.ClientB, commandId)).Succeeded);
      Assert.Equal(2, await db.ClientOperationalJournalDecisions.CountAsync(x => x.FirmId == scope.FirmId && x.JournalId == journalId));
      var journal = await db.ClientOperationalJournals.SingleAsync(x => x.FirmId == scope.FirmId && x.Id == journalId);
      Assert.Equal("POSTED", journal.Status);
      Assert.Equal(scope.Reviewer.Id, journal.PostedByUserId);
      var snapshots = await ClientOperationalLedgerWorkspace.GetSnapshotsAsync(db, reviewer, scope.ClientA, journalId);
      Assert.True(snapshots.Succeeded, snapshots.Message);
      Assert.Equal(new[] { "2", "5" }, snapshots.Value!.Select(x => x.Revision));
      Assert.Equal("125.000000", snapshots.Value![0].Lines[0].Debit);
      Assert.Equal("150.000000", snapshots.Value![1].Lines[0].Debit);
      Assert.False((await ClientOperationalLedgerWorkspace.GetSnapshotsAsync(db, reviewer, scope.ClientB, journalId)).Succeeded);
      await AssertNativeRuntimeSqlDeniedAsync(pg, journalId, scope.Reviewer.Id,
      [
        "UPDATE client_operational_posting_receipts SET intent_hash=repeat('a',64) WHERE journal_id=@journal",
        "DELETE FROM client_operational_posting_receipts WHERE journal_id=@journal",
        "UPDATE client_operational_journal_snapshots SET snapshot_json='{}'::jsonb WHERE journal_id=@journal",
        "DELETE FROM client_operational_journal_snapshots WHERE journal_id=@journal",
        "INSERT INTO client_operational_journal_snapshots SELECT gen_random_uuid(),firm_id,client_id,journal_id,99,capture_kind,snapshot_json,captured_at FROM client_operational_journal_snapshots WHERE journal_id=@journal LIMIT 1"
      ]);
      var ledger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId);
      Assert.True(ledger.Succeeded, ledger.Message);
      Assert.Equal(2, ledger.Value!.TotalEntries);
      Assert.Equal("150.000000", ledger.Value.Accounts.Single(x => x.AccountCode == "6000").DebitMovement);
      Assert.Equal("-150.000000", ledger.Value.Accounts.Single(x => x.AccountCode == "1000").NetMovement);
      Assert.Equal(2, ledger.Value.TrialBalance.Rows.Count);
      Assert.Equal("150", ledger.Value.TrialBalance.ClosingDebit);
      Assert.Equal("150", ledger.Value.TrialBalance.ClosingCredit);
      var filteredLedger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId,
        accountCodeFrom: "6000", accountCodeTo: "6000", sourceType: "NATIVE_JOURNAL", reference: "J-001");
      Assert.True(filteredLedger.Succeeded, filteredLedger.Message);
      Assert.Equal(1, filteredLedger.Value!.TotalEntries);
      Assert.Equal(2, filteredLedger.Value.PeriodTotalEntries);
      Assert.Single(filteredLedger.Value.Entries);
      Assert.Equal("6000", filteredLedger.Value.Entries[0].AccountCode);
      Assert.Single(filteredLedger.Value.TrialBalance.Rows);
      Assert.Equal("150", filteredLedger.Value.TrialBalance.ClosingDebit);
      var later = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId,
        pageSize: 1, fromDate: new DateOnly(2026, 2, 1), toDate: new DateOnly(2026, 12, 31), includeZeroAccounts: true);
      Assert.True(later.Succeeded, later.Message);
      Assert.Empty(later.Value!.Entries);
      Assert.Equal(3, later.Value.TrialBalance.Rows.Count);
      Assert.Equal("0", later.Value.TrialBalance.Rows.Single(x => x.AccountCode == "7000").ClosingDebit);
      Assert.Equal("150", later.Value.TrialBalance.OpeningDebit);
      Assert.Equal("0", later.Value.TrialBalance.PeriodDebit);
      Assert.Equal("150", later.Value.TrialBalance.ClosingDebit);
      Assert.False((await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId,
        fromDate: new DateOnly(2025, 1, 1))).Succeeded);
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
      var correction = new ClientOperationalJournalReversalRequest(6, periodId, "J-REV-001", new DateOnly(2026, 1, 15), "Reverse duplicate expense", "SYN-EVIDENCE-CORRECTION-1");
      Assert.False((await ClientOperationalLedgerWorkspace.CreateReversalAsync(db, preparer, scope.ClientB, journalId, correction)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.CreateReversalAsync(db, preparer, scope.ClientA, journalId, correction with { ExpectedRevision = 5 })).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.CreateReversalAsync(db, preparer, scope.ClientA, journalId, correction with { EvidenceReference = "" })).Succeeded);
      var lockedCorrection = await ClientOperationalLedgerWorkspace.CreateReversalAsync(db, preparer, scope.ClientA, journalId, correction);
      Assert.Equal(ErrorCodes.GateBlocked, lockedCorrection.ErrorCode);
      var next = await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), "IFRS", "QAR"));
      Assert.True(next.Succeeded, next.Message);
      correction = correction with { PeriodId = next.Value, PostingDate = new DateOnly(2027, 1, 15) };
      async Task<CommandResult<Guid>> PrepareReversal() {
        await using var concurrent = new AuditSphereDbContext(pg.Options);
        return await ClientOperationalLedgerWorkspace.CreateReversalAsync(concurrent, preparer, scope.ClientA, journalId, correction);
      }
      var prepared = await Task.WhenAll(PrepareReversal(), PrepareReversal());
      Assert.All(prepared, x => Assert.True(x.Succeeded, x.Message));
      Assert.Equal(prepared[0].Value, prepared[1].Value);
      var reversalId = prepared[0].Value;
      Assert.False((await ClientOperationalLedgerWorkspace.CreateReversalAsync(db, preparer, scope.ClientA, journalId,
        correction with { Reason = "Different intent" })).Succeeded);
      var reversal = await ClientOperationalLedgerWorkspace.GetAsync(db, preparer, scope.ClientA, reversalId);
      Assert.Equal("DRAFT", reversal.Value!.Status);
      Assert.Equal(journalId, reversal.Value.ReversalOf!.OriginalJournalId);
      Assert.Equal(correction.EvidenceReference, reversal.Value.ReversalOf.EvidenceReference);
      Assert.Equal("150.000000", reversal.Value.Lines[0].Credit);
      Assert.Equal(reversalId, (await ClientOperationalLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, journalId)).Value!.ReversedBy!.ReversalJournalId);
      await AssertNativeRuntimeSqlDeniedAsync(pg, reversalId, scope.Reviewer.Id,
      [
        "UPDATE client_operational_journal_reversals SET reason='Changed reason' WHERE reversal_journal_id=@journal",
        "DELETE FROM client_operational_journal_reversals WHERE reversal_journal_id=@journal",
        "UPDATE client_operational_journal_lines SET credit=credit+1 WHERE journal_id=@journal AND credit>0",
        "DELETE FROM client_operational_journal_lines WHERE journal_id=@journal"
      ]);
      var reversalEdit = await ClientOperationalLedgerWorkspace.EditDraftAsync(db, preparer, scope.ClientA, reversalId,
        new(1, "Change reversal draft", new DateOnly(2027, 1, 15), [new("6000", "Expense", 1m, 0m), new("1000", "Cash", 0m, 1m)]));
      Assert.False(reversalEdit.Succeeded); Assert.Equal(ErrorCodes.ProtectedState, reversalEdit.ErrorCode);
      var reversalPreview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, reversalId);
      Assert.True(reversalPreview.Succeeded, reversalPreview.Message);
      Assert.True((await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, reversalId, 1, previewDigest: reversalPreview.Value!.Digest)).Succeeded);
      var reviewerPreview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, scope.ClientA, reversalId);
      Assert.False((await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, preparer, scope.ClientA, reversalId,
        new(2, "APPROVE", "Self approval", reviewerPreview.Value!.Digest, Guid.CreateVersion7()))).Succeeded);
      var reversed = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, reversalId,
        new(2, "APPROVE", "Independent full reversal review", reviewerPreview.Value!.Digest, Guid.CreateVersion7()));
      Assert.True(reversed.Succeeded, reversed.Message);
      var nextLedger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, next.Value);
      Assert.Equal("-150.000000", nextLedger.Value!.Accounts.Single(x => x.AccountCode == "6000").NetMovement);
      Assert.All(nextLedger.Value.Entries, x => Assert.Equal(journalId, x.ReversesJournalId));
      Assert.False((await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, next.Value,
        page: 1, pageSize: 1)).Succeeded);
      var firstSnapshotPage = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, next.Value, pageSize: 1);
      Assert.True(firstSnapshotPage.Succeeded, firstSnapshotPage.Message);
      Assert.Equal(2, firstSnapshotPage.Value!.TotalEntries);
      var originalFirstLine = Assert.Single(firstSnapshotPage.Value.Entries);
      Assert.Equal(reversalId, originalFirstLine.JournalId);
      var laterDraft = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, preparer,
        new(scope.ClientA, next.Value, "J-SNAPSHOT-LATER", "Backdated posting after report start", new DateOnly(2027, 1, 10),
          [new("6000", "Later debit", 20m, 0m), new("1000", "Later credit", 0m, 20m)]));
      Assert.True(laterDraft.Succeeded, laterDraft.Message);
      var laterPreview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, laterDraft.Value);
      Assert.True(laterPreview.Succeeded, laterPreview.Message);
      Assert.True((await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, laterDraft.Value, 1,
        previewDigest: laterPreview.Value!.Digest)).Succeeded);
      var laterReview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, scope.ClientA, laterDraft.Value);
      Assert.True(laterReview.Succeeded, laterReview.Message);
      var laterPost = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, laterDraft.Value,
        new(2, "APPROVE", "Verified after initial report snapshot", laterReview.Value!.Digest, Guid.CreateVersion7()));
      Assert.True(laterPost.Succeeded, laterPost.Message);
      var nextSnapshotPage = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, next.Value,
        page: 1, pageSize: 1, postingSnapshotThrough: long.Parse(firstSnapshotPage.Value.PostingSnapshotThrough,
          System.Globalization.CultureInfo.InvariantCulture));
      Assert.True(nextSnapshotPage.Succeeded, nextSnapshotPage.Message);
      Assert.Equal(firstSnapshotPage.Value.PostingSnapshotThrough, nextSnapshotPage.Value!.PostingSnapshotThrough);
      Assert.Equal(2, nextSnapshotPage.Value.TotalEntries);
      Assert.Equal(reversalId, Assert.Single(nextSnapshotPage.Value.Entries).JournalId);
      Assert.NotEqual(originalFirstLine.LineNumber, nextSnapshotPage.Value.Entries[0].LineNumber);
      Assert.Equal(firstSnapshotPage.Value.TrialBalance.ClosingDebit, nextSnapshotPage.Value.TrialBalance.ClosingDebit);
      Assert.Equal(firstSnapshotPage.Value.TrialBalance.ClosingCredit, nextSnapshotPage.Value.TrialBalance.ClosingCredit);
      Assert.Equal(4, (await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, next.Value)).Value!.TotalEntries);
      var originalLedger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId);
      Assert.Equal("150.000000", originalLedger.Value!.Accounts.Single(x => x.AccountCode == "6000").DebitMovement);
      Assert.All(originalLedger.Value.Entries, x => { Assert.Equal(reversalId, x.ReversedByJournalId); Assert.Equal("POSTED", x.ReversedByStatus); });
      var listed = await ClientOperationalLedgerWorkspace.ListAsync(db, reviewer, scope.ClientA, status: "POSTED", pageSize: 1);
      Assert.True(listed.Succeeded, listed.Message);
      Assert.Equal(3, listed.Value!.TotalJournals);
      Assert.Single(listed.Value.Journals);
      var secondPage = await ClientOperationalLedgerWorkspace.ListAsync(db, reviewer, scope.ClientA, status: "POSTED", page: 1, pageSize: 1);
      Assert.NotEqual(listed.Value.Journals[0].Id, Assert.Single(secondPage.Value!.Journals).Id);
      Assert.Single((await ClientOperationalLedgerWorkspace.ListAsync(db, reviewer, scope.ClientA, periodId)).Value!.Journals);
      var sibling = await ClientOperationalLedgerWorkspace.ListAsync(db, preparer, scope.ClientB);
      Assert.True(sibling.Succeeded); Assert.Empty(sibling.Value!.Journals);
      await db.RoleGrants.Where(x => x.UserId == scope.Preparer.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.ClientId, scope.ClientA));
      Assert.False((await ClientOperationalLedgerWorkspace.ListAsync(db, preparer, scope.ClientB)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.ListAsync(db, reviewer, scope.ClientA, Guid.NewGuid())).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.ListAsync(db, reviewer, scope.ClientA, status: "INVALID")).Succeeded);
      db.AcceptanceDecisions.Add(new AcceptanceDecision {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA, ServiceRoute = "BOOKKEEPING",
        Decision = "Declined", Generation = 2, Rationale = "Synthetic service stopped", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('b', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      var retainedList = await ClientOperationalLedgerWorkspace.ListAsync(db, reviewer, scope.ClientA);
      Assert.True(retainedList.Succeeded, retainedList.Message); Assert.False(retainedList.Value!.BookkeepingActive);
      var retainedLedger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId);
      Assert.True(retainedLedger.Succeeded, retainedLedger.Message); Assert.False(retainedLedger.Value!.BookkeepingActive);
      Assert.Equal(originalLedger.Value.TrialBalance.ClosingDebit, retainedLedger.Value.TrialBalance.ClosingDebit);
      Assert.True((await ClientOperationalLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, journalId)).Succeeded);
      Assert.True((await ClientOperationalLedgerWorkspace.GetSnapshotsAsync(db, reviewer, scope.ClientA, journalId)).Succeeded);
      Assert.True((await ClientOperationalLedgerWorkspace.GetPostingReceiptAsync(db, reviewer, scope.ClientA, commandId)).Succeeded);
      var blockedDraft = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, preparer,
        new(scope.ClientA, next.Value, "J-INACTIVE", "Blocked service", new DateOnly(2027, 1, 20), [new("6000", "Expense", 1m, 0m), new("1000", "Cash", 0m, 1m)]));
      Assert.False(blockedDraft.Succeeded); Assert.Equal(ErrorCodes.GateBlocked, blockedDraft.ErrorCode);
      await db.RoleGrants.Where(x => x.UserId == scope.Reviewer.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
      Assert.False((await ClientOperationalLedgerWorkspace.ListAsync(db, reviewer, scope.ClientA)).Succeeded);
      Assert.False((await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, journalId)).Succeeded);

    }
  }


  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeJournalDraftEditingFencesRevisionAndPreviewWithoutChangingSubmittedHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
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
        new("expense", "6000", "Office expense", "EXPENSE", "DEBIT", true),
        new("unused", "7000", "Unused expense", "EXPENSE", "DEBIT", true)
      ]);
      Assert.True(accounts.Succeeded, accounts.Message);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var publication = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId);
      Assert.True(publication.Succeeded, publication.Message);
    }


    Guid draftId;
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      var created = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, preparer,
        new(scope.ClientA, periodId, "J-EDIT", "Original draft", new DateOnly(2026, 1, 15), [new("6000", "Expense", 10m, 0m), new("1000", "Cash", 0m, 10m)]));
      Assert.True(created.Succeeded, created.Message); draftId = created.Value;
    }
    ClientOperationalJournalPreview oldPreview;
    var edit = new ClientOperationalJournalReworkRequest(1, "Edited draft", new DateOnly(2026, 1, 16), [new("6000", "Expense revised", 20m, 0m), new("1000", "Cash revised", 0m, 20m)]);
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      oldPreview = (await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, draftId)).Value!;
      Assert.False((await ClientOperationalLedgerWorkspace.EditDraftAsync(db, reviewer, scope.ClientA, draftId, edit)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.EditDraftAsync(db, preparer, scope.ClientB, draftId, edit)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.EditDraftAsync(db, preparer, scope.ClientA, draftId, edit with { PostingDate = new DateOnly(2027, 1, 1) })).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.EditDraftAsync(db, preparer, scope.ClientA, draftId, edit with { Lines = [new("6000", "Expense", 20m, 0m), new("1000", "Cash", 0m, 19m)] })).Succeeded);
    }
    async Task<CommandResult> Attempt() {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await ClientOperationalLedgerWorkspace.EditDraftAsync(db, preparer, scope.ClientA, draftId, edit);
    }
    var edits = await Task.WhenAll(Attempt(), Attempt());
    Assert.Single(edits, x => x.Succeeded); Assert.Equal(ErrorCodes.StaleRevision, Assert.Single(edits, x => !x.Succeeded).ErrorCode);
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      var saved = (await ClientOperationalLedgerWorkspace.GetAsync(db, preparer, scope.ClientA, draftId)).Value!;
      Assert.Equal("2", saved.Revision); Assert.Equal("DRAFT", saved.Status); Assert.Equal("Edited draft", saved.Description);
      Assert.Equal("J-EDIT", saved.JournalNumber); Assert.Equal(periodId, saved.PeriodId); Assert.Equal("20.000000", saved.Lines[0].Debit);
      Assert.Empty(await db.ClientOperationalJournalSnapshots.Where(x => x.JournalId == draftId).ToListAsync());
      var oldIntent = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, draftId, 2, previewDigest: oldPreview.Digest);
      Assert.False(oldIntent.Succeeded); Assert.Equal(ErrorCodes.StaleRevision, oldIntent.ErrorCode);
      var fresh = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, draftId);
      Assert.NotEqual(oldPreview.Digest, fresh.Value!.Digest);
      Assert.True((await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, draftId, 2, previewDigest: fresh.Value.Digest)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.EditDraftAsync(db, preparer, scope.ClientA, draftId, edit with { ExpectedRevision = 3 })).Succeeded);
      var history = await ClientOperationalLedgerWorkspace.GetSnapshotsAsync(db, reviewer, scope.ClientA, draftId);
      Assert.Equal("20.000000", Assert.Single(history.Value!).Lines[0].Debit);
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

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeOpeningBalanceRequiresExactEvidenceAndIndependentApprovalAndSeparatesMovement()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid periodId, chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Approved native opening-balance test", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('f', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var profile = await ClientAccountingService.CreateProfileAsync(db, reviewer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "OPENING-1",
          ClientAccountingSourceModes.NativeBookkeeping));
      Assert.True(profile.Succeeded, profile.Message);
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "IFRS", "QAR"))).Value;
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "AUDITSPHERE", new DateOnly(2026, 1, 1))).Value;
      var accounts = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
        new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
        new("retained", "3000", "Retained earnings", "EQUITY", "CREDIT", true)
      ]);
      Assert.True(accounts.Succeeded, accounts.Message);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId)).Succeeded);
      var rows = new[] { new ClientOperationalOpeningBalanceLineInput("1000", 100m, 0m),
        new ClientOperationalOpeningBalanceLineInput("3000", 0m, 100m) };
      var stale = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, preparer,
        new(scope.ClientA, periodId, 2, new DateOnly(2026, 1, 1), "QAR", "prior TB 2025-12", new string('a', 64), rows));
      Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
      var unbalanced = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, preparer,
        new(scope.ClientA, periodId, 1, new DateOnly(2026, 1, 1), "QAR", "prior TB 2025-12", new string('a', 64),
          [rows[0], new ClientOperationalOpeningBalanceLineInput("3000", 0m, 99m)]));
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, unbalanced.ErrorCode);
      var created = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, preparer,
        new(scope.ClientA, periodId, 1, new DateOnly(2026, 1, 1), "QAR", "prior TB 2025-12", new string('a', 64), rows));
      Assert.True(created.Succeeded, created.Message);
      var duplicate = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, preparer,
        new(scope.ClientA, periodId, 1, new DateOnly(2026, 1, 1), "QAR", "prior TB 2025-12", new string('a', 64), rows));
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicate.ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, preparer,
        scope.ClientA, created.Value, 1, (await ClientOperationalOpeningBalanceWorkspace.GetAsync(db, preparer, scope.ClientA, periodId)).Value!.ManifestSha256)).ErrorCode);
      var blocked = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, preparer, scope.ClientA, periodId);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      var beforeApproval = (await ClientOperationalOpeningBalanceWorkspace.GetAsync(db, preparer, scope.ClientA, periodId)).Value!;
      Assert.True((await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, reviewer, scope.ClientA, created.Value,
        1, beforeApproval.ManifestSha256)).Succeeded);
      var approvedAgain = await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, reviewer, scope.ClientA, created.Value,
        1, beforeApproval.ManifestSha256);
      Assert.Equal(ErrorCodes.ScopeDenied, approvedAgain.ErrorCode);
      var immutableEvidence = await Record.ExceptionAsync(async () => await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE client_operational_opening_balances SET evidence_reference='tampered' WHERE id={created.Value}"));
      Assert.IsType<PostgresException>(immutableEvidence);
      Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)immutableEvidence!).SqlState);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var ledger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, preparer, scope.ClientA, periodId);
      Assert.True(ledger.Succeeded, ledger.Message);
      Assert.Equal("NATIVE_POSTED_ACTIVITY_WITH_REVIEWED_OPENING", ledger.Value!.TrialBalance.Source);
      Assert.Equal("100", ledger.Value.TrialBalance.OpeningDebit);
      Assert.Equal("100", ledger.Value.TrialBalance.OpeningCredit);
      Assert.Equal("0", ledger.Value.TrialBalance.PeriodDebit);
      Assert.Equal("0", ledger.Value.TrialBalance.PeriodCredit);
    }
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task ReviewedOpeningInvoiceDetailReconcilesToArAndAppearsInAsOfBalances()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid periodId, chartId, arAccountId, partyId, openingItemId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision { Id = Guid.CreateVersion7(), FirmId = scope.FirmId,
        PracticeClientId = scope.ClientA, ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Reviewed client bookkeeping cutover", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('f', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, reviewer,
        new(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "OPENING-AR", ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new(scope.ClientA, "2026", new(2026, 1, 1), new(2026, 12, 31), "IFRS", "QAR"))).Value;
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "AUDITSPHERE", new(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
        new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
        new("ar", "1100", "Receivables", "ASSET", "DEBIT", true),
        new("equity", "3000", "Opening equity", "EQUITY", "CREDIT", true)
      ])).Succeeded);
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId)).Succeeded);
      var account = await db.ClientAccounts.SingleAsync(x => x.ChartVersionId == chartId && x.AccountCode == "1100");
      arAccountId = account.Id;
      var role = await ClientAccountRoleWorkspace.ProposeAsync(db, preparer, scope.ClientA,
        new(chartId, arAccountId, "AR", new(2026, 1, 1), null, "Reviewed opening AR control"));
      Assert.True(role.Succeeded, role.Message);
      Assert.True((await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, scope.ClientA, role.Value, "APPROVE", "Independent AR control review")).Succeeded);
      var party = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, preparer, scope.ClientA,
        new("Opening customer", "Opening customer", "CUSTOMER", "QA address", "QA", "", "", "", "", "TEST-SYSTEM", "OPEN-001"));
      Assert.True(party.Succeeded, party.Message);
      partyId = party.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var lines = new[] { new ClientOperationalOpeningBalanceLineInput("1000", 100m, 0m),
        new ClientOperationalOpeningBalanceLineInput("1100", 125m, 0m),
        new ClientOperationalOpeningBalanceLineInput("3000", 0m, 225m) };
      var request = new ClientOperationalOpeningBalanceRequest(scope.ClientA, periodId, 1, new(2026, 1, 1), "QAR",
        "approved cutover schedule", new string('a', 64), lines,
        [new("AR", partyId, "CUST-OPEN-1", new(2026, 2, 10), "1100", 125m)]);
      var mismatched = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, preparer,
        request with { OpenItems = [new("AR", partyId, "CUST-OPEN-1", new(2026, 2, 10), "1100", 124m)] });
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, mismatched.ErrorCode);
      var created = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, preparer, request);
      Assert.True(created.Succeeded, created.Message);
      var draft = (await ClientOperationalOpeningBalanceWorkspace.GetAsync(db, preparer, scope.ClientA, periodId)).Value!;
      Assert.Single(draft.OpenItems);
      Assert.Equal("125.000000", draft.OpenItems[0].Amount);
      Assert.True((await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, reviewer, scope.ClientA,
        created.Value, 1, draft.ManifestSha256)).Succeeded);
      var balances = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2026, 1, 31));
      Assert.True(balances.Succeeded, balances.Message);
      var openingItem = Assert.Single(balances.Value!);
      openingItemId = openingItem.OpenItemId;
      Assert.Equal("OPENING_AR_INVOICE", openingItem.Kind);
      Assert.Equal("125.000000", openingItem.OpenAmount);
      Assert.Equal(new(2026, 2, 10), openingItem.DueDate);
      var reconciliation = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, reviewer, scope.ClientA,
        periodId, new(2026, 1, 31));
      Assert.True(reconciliation.Succeeded, reconciliation.Message);
      Assert.Equal("REVIEWED_OPENING_DETAIL_INCLUDED", reconciliation.Value!.OpeningDetailStatus);
      Assert.True(reconciliation.Value.Reconciled);
      var ar = Assert.Single(reconciliation.Value.Accounts, x => x.AccountId == arAccountId);
      Assert.Equal("125.000000", ar.LedgerBalance);
      Assert.Equal("125.000000", ar.OpenItemBalance);
      Assert.Equal("0.000000", ar.Difference);

      var settlement = new ClientManualSettlementDraftRequest(Guid.CreateVersion7(), scope.ClientA, periodId, partyId,
        "SALES_RECEIPT", "RCT-OPEN-01", "Record completed customer receipt", new(2026, 2, 11), "1000", 20m,
        "BANK-OPEN-01", "client evidence for completed receipt");
      var settlementPreview = await ClientOperationalLedgerWorkspace.PreviewSettlementAsync(db, preparer, settlement);
      Assert.True(settlementPreview.Succeeded, settlementPreview.Message);
      var settlementDraft = await ClientOperationalLedgerWorkspace.CreateSettlementDraftAsync(db, preparer, settlement, settlementPreview.Value!.Digest);
      Assert.True(settlementDraft.Succeeded, settlementDraft.Message);
      var journalPreview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, settlementDraft.Value!.JournalId);
      Assert.True(journalPreview.Succeeded, journalPreview.Message);
      Assert.True((await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, settlementDraft.Value.JournalId,
        1, previewDigest: journalPreview.Value!.Digest)).Succeeded);
      var reviewerPreview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, scope.ClientA, settlementDraft.Value.JournalId);
      Assert.True(reviewerPreview.Succeeded, reviewerPreview.Message);
      Assert.True((await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, settlementDraft.Value.JournalId,
        new(2, "APPROVE", "Verified the externally completed client receipt", reviewerPreview.Value!.Digest, Guid.CreateVersion7()))).Succeeded);

      var receipt = Assert.Single((await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2026, 2, 11))).Value!,
        x => x.Kind == "SALES_RECEIPT");
      var allocation = new ClientOpenItemAllocationRequest(Guid.CreateVersion7(), "SALES_RECEIPT", receipt.OpenItemId,
        "ALLOCATE", "BANK-OPEN-01-ALLOC", "Apply part of the completed receipt to the reviewed cutover invoice",
        [new(1, "OPENING_AR_INVOICE", openingItemId, 20m)], "");
      var allocationPreview = await ClientOpenItemAllocationWorkflow.PreviewAsync(db, preparer, scope.ClientA, allocation);
      Assert.True(allocationPreview.Succeeded, allocationPreview.Message);
      var allocationSubmission = await ClientOpenItemAllocationWorkflow.SubmitAsync(db, preparer, scope.ClientA,
        allocation with { PreviewDigest = allocationPreview.Value!.Digest });
      Assert.True(allocationSubmission.Succeeded, allocationSubmission.Message);
      var allocationReview = await ClientOpenItemAllocationWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, allocationSubmission.Value!.SubmissionId);
      Assert.True(allocationReview.Succeeded, allocationReview.Message);
      Assert.True((await ClientOpenItemAllocationWorkflow.ReviewAsync(db, reviewer, scope.ClientA, allocationSubmission.Value.SubmissionId,
        Guid.CreateVersion7(), "APPROVE", "Independently reviewed opening-item receipt allocation", allocationReview.Value!.Digest)).Succeeded);
      var settledBalances = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2026, 2, 11));
      Assert.True(settledBalances.Succeeded, settledBalances.Message);
      Assert.Equal("105.000000", settledBalances.Value!.Single(x => x.OpenItemId == openingItemId).OpenAmount);
      Assert.Equal("0.000000", settledBalances.Value!.Single(x => x.OpenItemId == receipt.OpenItemId).OpenAmount);
      var settledReconciliation = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, reviewer, scope.ClientA, periodId, new(2026, 2, 11));
      Assert.True(settledReconciliation.Succeeded, settledReconciliation.Message);
      Assert.True(settledReconciliation.Value!.Reconciled);
      Assert.Equal("105.000000", settledReconciliation.Value.Accounts.Single(x => x.AccountId == arAccountId).LedgerBalance);
      Assert.Equal("105.000000", settledReconciliation.Value.Accounts.Single(x => x.AccountId == arAccountId).OpenItemBalance);
    }
  }
}
