using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class LedgerTests
{
  private sealed record Fixture(
    Guid FirmId,
    Guid ClientId,
    AppUser Manager,
    AppUser Reviewer,
    AppUser Partner,
    ActorContext ManagerActor,
    ActorContext ReviewerActor,
    ActorContext PartnerActor);

  [Fact]
  public async Task LedgerWorkflow_IsBalancedIdempotentAndImmutable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid periodId, debitAccountId, creditAccountId, journalId, postingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      debitAccountId = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
        new CreateFirmAccountRequest("1100", "Receivables", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
      creditAccountId = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
        new CreateFirmAccountRequest("4100", "Revenue", LedgerStates.AccountRevenue, LedgerStates.Credit))).Value;
      periodId = (await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor,
        new CreateFirmPeriodRequest("2026-09"))).Value;
      var draftRequest = new CreateFirmJournalDraftRequest(periodId, "J-100", "INVOICE", "INV-100", 1, "REVENUE", "QAR", [
        new FirmJournalLineRequest(debitAccountId, "Receivable", 100, 0),
        new FirmJournalLineRequest(creditAccountId, "Revenue", 0, 100)
      ]);
      journalId = (await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor, draftRequest)).Value;
      var retryDraft = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor, draftRequest);
      Assert.True(retryDraft.Succeeded, retryDraft.Message);
      Assert.Equal(journalId, retryDraft.Value);
      var changedDraft = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor,
        draftRequest with { Lines = [
          new FirmJournalLineRequest(debitAccountId, "Receivable", 101, 0),
          new FirmJournalLineRequest(creditAccountId, "Revenue", 0, 101)
        ] });
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedDraft.ErrorCode);
      var submitted = await LedgerService.SubmitFirmJournalAsync(db, fixture.ManagerActor, journalId);
      Assert.True(submitted.Succeeded, submitted.Message);
      Assert.Equal(LedgerStates.JournalReviewRequired, (await db.FirmJournals.AsNoTracking().SingleAsync(x => x.Id == journalId)).Status);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, fixture.ReviewerActor, journalId)).Succeeded);
      Assert.Equal(LedgerStates.JournalApproved, (await db.FirmJournals.AsNoTracking().SingleAsync(x => x.Id == journalId)).Status);
      var posted = await LedgerService.PostFirmJournalAsync(db, fixture.ManagerActor, journalId);
      Assert.True(posted.Succeeded, posted.Message);
      postingId = posted.Value;
      var retry = await LedgerService.PostFirmJournalAsync(db, fixture.ManagerActor, journalId);
      Assert.True(retry.Succeeded);
      Assert.Equal(postingId, retry.Value);
      var duplicateSource = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor,
        new CreateFirmJournalDraftRequest(periodId, "J-100-DUP", "INVOICE", "INV-100", 1, "REVENUE", "QAR", [
          new FirmJournalLineRequest(debitAccountId, "Receivable", 100, 0),
          new FirmJournalLineRequest(creditAccountId, "Revenue", 0, 100)
        ]));
      Assert.False(duplicateSource.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicateSource.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(2, await db.FirmPostingLines.CountAsync(x => x.PostingId == postingId));
      var persistedJournal = await db.FirmJournals.SingleAsync(x => x.Id == journalId);
      Assert.Equal(LedgerStates.JournalPosted, persistedJournal.Status);
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE firm_postings SET currency = 'USD' WHERE id = {postingId} AND firm_id = {fixture.FirmId}"));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM firm_posting_lines WHERE posting_id = {postingId} AND firm_id = {fixture.FirmId}"));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE firm_journals SET source_key = 'tampered' WHERE id = {journalId} AND firm_id = {fixture.FirmId}"));

      var reversal = await LedgerService.ReverseFirmPostingAsync(db, fixture.ReviewerActor,
        new ReverseFirmPostingRequest(postingId, periodId, "Corrected invoice"));
      Assert.True(reversal.Succeeded);
      var reversalRetry = await LedgerService.ReverseFirmPostingAsync(db, fixture.ReviewerActor,
        new ReverseFirmPostingRequest(postingId, periodId, "Corrected invoice"));
      Assert.True(reversalRetry.Succeeded);
      Assert.Equal(reversal.Value, reversalRetry.Value);
      var reversedLines = await db.FirmPostingLines.Where(x => x.PostingId == reversal.Value).ToListAsync();
      Assert.Equal(100m, reversedLines.Sum(x => x.Credit));
      Assert.Equal(100m, reversedLines.Sum(x => x.Debit));
    }
  }

  [Fact]
  public async Task FirmAccountAndPeriodSetup_IsIdempotentByNaturalKeyAndRejectsConflictingAccountDefinition()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var accountRequest = new CreateFirmAccountRequest("1000", "Operating bank", LedgerStates.AccountAsset,
      LedgerStates.Debit, PostingAllowed: true);
    var firstAccount = await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor, accountRequest);
    var retriedAccount = await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor, accountRequest);
    Assert.True(firstAccount.Succeeded, firstAccount.Message);
    Assert.True(retriedAccount.Succeeded, retriedAccount.Message);
    Assert.Equal(firstAccount.Value, retriedAccount.Value);

    var changedAccount = await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      accountRequest with { Name = "Different bank definition" });
    Assert.Equal(ErrorCodes.IdempotencyConflict, changedAccount.ErrorCode);

    var periodRequest = new CreateFirmPeriodRequest("2026-12");
    var firstPeriod = await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor, periodRequest);
    var retriedPeriod = await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor, periodRequest);
    Assert.True(firstPeriod.Succeeded, firstPeriod.Message);
    Assert.True(retriedPeriod.Succeeded, retriedPeriod.Message);
    Assert.Equal(firstPeriod.Value, retriedPeriod.Value);
    Assert.Equal(1, await db.FirmAccounts.CountAsync(x => x.FirmId == fixture.FirmId && x.Code == "1000"));
    Assert.Equal(1, await db.FirmPeriods.CountAsync(x => x.FirmId == fixture.FirmId && x.PeriodCode == "2026-12"));
  }

  [Fact]
  public async Task FinanceCapabilityProjection_ExcludesExpiredRoleGrants()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var now = DateTimeOffset.UtcNow;
    var expiredAt = now.AddMinutes(-1);

    var managerGrant = await db.RoleGrants.SingleAsync(x =>
      x.FirmId == fixture.FirmId && x.UserId == fixture.Manager.Id && x.Role == "FinanceManager");
    managerGrant.GrantedAt = now.AddMinutes(-2);
    managerGrant.ExpiresAt = expiredAt;
    db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Manager, "FinanceReviewer"));

    var reviewerGrant = await db.RoleGrants.SingleAsync(x =>
      x.FirmId == fixture.FirmId && x.UserId == fixture.Reviewer.Id && x.Role == "FinanceReviewer");
    reviewerGrant.GrantedAt = now.AddMinutes(-2);
    reviewerGrant.ExpiresAt = expiredAt;
    db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Reviewer, "FinanceManager"));
    await db.SaveChangesAsync();

    var managerView = await FirmFinanceQuery.GetAsync(db, fixture.ManagerActor);
    Assert.True(managerView.Succeeded, managerView.Message);
    Assert.False(managerView.Value!.CanCreateSetup);
    Assert.False(managerView.Value.CanCreateJournals);
    Assert.True(managerView.Value.CanClosePeriod);
    Assert.True(managerView.Value.CanReviewJournals);
    Assert.False(managerView.Value.CanPostJournals);

    var reviewerView = await FirmFinanceQuery.GetAsync(db, fixture.ReviewerActor);
    Assert.True(reviewerView.Succeeded, reviewerView.Message);
    Assert.True(reviewerView.Value!.CanCreateSetup);
    Assert.True(reviewerView.Value.CanCreateJournals);
    Assert.False(reviewerView.Value.CanClosePeriod);
    Assert.False(reviewerView.Value.CanReviewJournals);
    Assert.True(reviewerView.Value.CanPostJournals);
  }

  [Fact]
  public async Task FinanceReviewerCanReviewButCannotAuthorOrSubmitFirmJournal()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var cash = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("ROLE-CASH", "Cash", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
    var equity = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("ROLE-EQUITY", "Opening equity", LedgerStates.AccountEquity, LedgerStates.Credit))).Value;
    var period = (await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor,
      new CreateFirmPeriodRequest("2026-12"))).Value;

    var reviewerProjection = await FirmFinanceQuery.GetAsync(db, fixture.ReviewerActor);
    Assert.True(reviewerProjection.Succeeded, reviewerProjection.Message);
    Assert.False(reviewerProjection.Value!.CanCreateJournals);
    Assert.True(reviewerProjection.Value.CanReviewJournals);
    Assert.False(reviewerProjection.Value.CanPostJournals);

    var request = new CreateFirmJournalDraftRequest(period, "ROLE-BOUNDARY", "MANUAL", "ROLE-BOUNDARY-01", 1,
      "OPENING_BALANCE", "QAR", [
        new FirmJournalLineRequest(cash, "Opening cash", 500, 0),
        new FirmJournalLineRequest(equity, "Opening equity", 0, 500)
      ], "opening-source.txt", "text/plain", Encoding.UTF8.GetBytes("approved opening source"));
    var created = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor, request);
    Assert.True(created.Succeeded, created.Message);
    var createDenied = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ReviewerActor,
      request with { JournalNumber = "REVIEWER-CANNOT-CREATE" });
    Assert.Equal(ErrorCodes.ScopeDenied, createDenied.ErrorCode);
    Assert.True((await LedgerService.SubmitFirmJournalAsync(db, fixture.ManagerActor, created.Value)).Succeeded);
    var submitDenied = await LedgerService.SubmitFirmJournalAsync(db, fixture.ReviewerActor, created.Value);
    Assert.Equal(ErrorCodes.ScopeDenied, submitDenied.ErrorCode);
    Assert.True((await LedgerService.ApproveFirmJournalAsync(db, fixture.ReviewerActor, created.Value)).Succeeded);
  }

  [Fact]
  public async Task OpeningBalanceBindsExactEvidenceAndPreventsEvidenceMutation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var cash = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("EVIDENCE-CASH", "Opening cash", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
    var equity = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("EVIDENCE-EQUITY", "Opening equity", LedgerStates.AccountEquity, LedgerStates.Credit))).Value;
    var period = (await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor,
      new CreateFirmPeriodRequest("2026-12"))).Value;
    var lines = new[] {
      new FirmJournalLineRequest(cash, "Opening cash", 500, 0),
      new FirmJournalLineRequest(equity, "Opening equity", 0, 500)
    };
    var request = new CreateFirmJournalDraftRequest(period, "EVIDENCE-BOUND", "MANUAL", "OPENING-SOURCE-01", 1,
      "OPENING_BALANCE", "QAR", lines);
    var missingEvidence = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor, request);
    Assert.Equal("ledger.invalid", missingEvidence.ErrorCode);

    var content = Encoding.UTF8.GetBytes("opening schedule v1; reviewed 2026-12-01");
    var supported = request with
    {
      SupportingEvidenceFileName = "opening-schedule.txt",
      SupportingEvidenceContentType = "text/plain",
      SupportingEvidenceContent = content
    };
    var created = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor, supported);
    Assert.True(created.Succeeded, created.Message);
    var retry = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor, supported);
    Assert.Equal(created.Value, retry.Value);
    var changedEvidence = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor,
      supported with { SupportingEvidenceContent = Encoding.UTF8.GetBytes("different opening schedule") });
    Assert.Equal(ErrorCodes.IdempotencyConflict, changedEvidence.ErrorCode);

    var journal = await db.FirmJournals.AsNoTracking().SingleAsync(x => x.Id == created.Value);
    Assert.Equal("opening-schedule.txt", journal.SupportingEvidenceFileName);
    Assert.Equal(Hashing.Sha256Hex(content), journal.SupportingEvidenceSha256);
    Assert.Equal(fixture.Manager.Id, journal.SupportingEvidenceUploadedByUserId);
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE firm_journals SET supporting_evidence_content = {Encoding.UTF8.GetBytes("tampered")} WHERE firm_id = {fixture.FirmId} AND id = {journal.Id}"));
  }

  [Fact]
  public async Task PartnerDrawingJournal_OnlyDebitsEquityAndCreditsAssets()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var cash = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("DRAW-CASH", "Partner cash", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
    var drawings = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("DRAW-EQUITY", "Partner drawings", LedgerStates.AccountEquity, LedgerStates.Debit))).Value;
    var expense = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("DRAW-EXPENSE", "Invalid drawing expense", LedgerStates.AccountExpense, LedgerStates.Debit))).Value;
    var period = (await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor,
      new CreateFirmPeriodRequest("2026-12"))).Value;

    var invalid = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor,
      new CreateFirmJournalDraftRequest(period, "DRAW-INVALID", "MANUAL", "PARTNER-DRAW-INVALID", 1,
        "PARTNER_DRAWING", "QAR", [
          new FirmJournalLineRequest(expense, "Partner drawing", 25, 0),
          new FirmJournalLineRequest(cash, "Cash withdrawal", 0, 25)
        ], "partner-draw-approval.txt", "text/plain", "approved draw request"u8.ToArray()));
    Assert.Equal("ledger.drawing-accounts-invalid", invalid.ErrorCode);

    var valid = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor,
      new CreateFirmJournalDraftRequest(period, "DRAW-VALID", "MANUAL", "PARTNER-DRAW-VALID", 1,
        "PARTNER_DRAWING", "QAR", [
          new FirmJournalLineRequest(drawings, "Partner drawing", 25, 0),
          new FirmJournalLineRequest(cash, "Cash withdrawal", 0, 25)
        ], "partner-draw-approval.txt", "text/plain", "approved draw request"u8.ToArray()));
    Assert.True(valid.Succeeded, valid.Message);
  }

  [Fact]
  public async Task UnbalancedJournal_CannotBeSubmittedOrPostedAndDatabaseRejectsDirectPosting()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid periodId, debitAccountId, creditAccountId, journalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      debitAccountId = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
        new CreateFirmAccountRequest("1200", "Unbalanced debit", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
      creditAccountId = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
        new CreateFirmAccountRequest("4200", "Unbalanced credit", LedgerStates.AccountRevenue, LedgerStates.Credit))).Value;
      periodId = (await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor,
        new CreateFirmPeriodRequest("2026-10"))).Value;
      journalId = (await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor,
        new CreateFirmJournalDraftRequest(periodId, "J-101", "MANUAL", "MANUAL-101", 1, "MANUAL", "QAR", [
          new FirmJournalLineRequest(debitAccountId, "Debit", 100, 0),
          new FirmJournalLineRequest(creditAccountId, "Credit", 0, 99)
        ]))).Value;
      var submit = await LedgerService.SubmitFirmJournalAsync(db, fixture.ManagerActor, journalId);
      Assert.False(submit.Succeeded);
      Assert.Equal("ledger.unbalanced", submit.ErrorCode);
      Assert.Equal(LedgerStates.JournalDraft,
        (await db.FirmJournals.AsNoTracking().SingleAsync(x => x.Id == journalId)).Status);
      var approve = await LedgerService.ApproveFirmJournalAsync(db, fixture.ReviewerActor, journalId);
      Assert.False(approve.Succeeded);
      var rejected = await LedgerService.PostFirmJournalAsync(db, fixture.ManagerActor, journalId);
      Assert.False(rejected.Succeeded);
    }

    await using var direct = new AuditSphereDbContext(pg.Options);
    var directJournal = new FirmJournal
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, PeriodId = periodId,
      JournalNumber = "J-DIRECT", SourceKind = "DIRECT", SourceKey = "DIRECT-1",
      SourceRevision = 1, PostingPurpose = "MANUAL", Currency = "QAR",
      Status = LedgerStates.JournalPosted, CreatedByUserId = fixture.Manager.Id,
      ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
      PostedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    };
    direct.FirmJournals.Add(directJournal);
    await direct.SaveChangesAsync();
    await using var tx = await direct.Database.BeginTransactionAsync();
    var directPosting = new FirmPosting
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, PeriodId = periodId,
      JournalId = directJournal.Id, Currency = "QAR", PostedByUserId = fixture.Manager.Id,
      PostedAt = DateTimeOffset.UtcNow
    };
    direct.FirmPostings.Add(directPosting);
    direct.FirmPostingLines.Add(new FirmPostingLine
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, PostingId = directPosting.Id,
      FirmAccountId = debitAccountId, Debit = 1, Credit = 0
    });
    await direct.SaveChangesAsync();
    var error = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
    Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
  }

  [Fact]
  public async Task PeriodCloseAndPostingShareThePeriodGuard()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid periodId, journalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var debit = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
        new CreateFirmAccountRequest("1300", "Race debit", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
      var credit = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
        new CreateFirmAccountRequest("4300", "Race credit", LedgerStates.AccountRevenue, LedgerStates.Credit))).Value;
      periodId = (await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor,
        new CreateFirmPeriodRequest("2026-11"))).Value;
      journalId = (await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor,
        new CreateFirmJournalDraftRequest(periodId, "J-102", "MANUAL", "MANUAL-102", 1, "MANUAL", "QAR", [
          new FirmJournalLineRequest(debit, "Debit", 75, 0),
          new FirmJournalLineRequest(credit, "Credit", 0, 75)
        ]))).Value;
      Assert.True((await LedgerService.SubmitFirmJournalAsync(db, fixture.ManagerActor, journalId)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, fixture.ReviewerActor, journalId)).Succeeded);
    }

    var closeTask = Task.Run(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await LedgerService.CloseFiscalPeriodAsync(db, fixture.ReviewerActor, periodId, "November close");
    });
    var postTask = Task.Run(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await LedgerService.PostFirmJournalAsync(db, fixture.ManagerActor, journalId);
    });
    await Task.WhenAll(closeTask, postTask);
    var closeResult = await closeTask;
    var postResult = await postTask;

    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var period = await verify.FirmPeriods.SingleAsync(x => x.Id == periodId);
      var journal = await verify.FirmJournals.SingleAsync(x => x.Id == journalId);
      Assert.False(period.Status == LedgerStates.PeriodClosed && journal.Status != LedgerStates.JournalPosted);
      if (period.Status == LedgerStates.PeriodClosed)
        Assert.True(postResult.Succeeded);
      else
        Assert.True(postResult.Succeeded || !closeResult.Succeeded);
    }
  }

  [Fact]
  public async Task FinanceRoleAndPostingAccountScopeAreEnforced()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var denied = await LedgerService.CreateFirmAccountAsync(db, fixture.PartnerActor,
      new CreateFirmAccountRequest("9999", "Denied", LedgerStates.AccountAsset, LedgerStates.Debit));
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

    var scopedManager = User(fixture.FirmId, "ScopedFinanceManager");
    db.Users.Add(scopedManager);
    db.RoleGrants.Add(Grant(fixture.FirmId, scopedManager, "FinanceManager", fixture.ClientId));
    await db.SaveChangesAsync();
    var scopedDenied = await LedgerService.CreateFirmAccountAsync(db, Actor(scopedManager, "FinanceManager"),
      new CreateFirmAccountRequest("9988", "Scoped must not manage firm ledger", LedgerStates.AccountAsset, LedgerStates.Debit));
    Assert.False(scopedDenied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, scopedDenied.ErrorCode);

    var blocked = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("9900", "Control", LedgerStates.AccountAsset, LedgerStates.Debit, false))).Value;
    var period = (await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor,
      new CreateFirmPeriodRequest("2026-12"))).Value;
    var credit = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("4900", "Control revenue", LedgerStates.AccountRevenue, LedgerStates.Credit))).Value;
    var journal = await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor,
      new CreateFirmJournalDraftRequest(period, "J-103", "MANUAL", "MANUAL-103", 1, "MANUAL", "QAR", [
        new FirmJournalLineRequest(blocked, "Blocked", 1, 0),
        new FirmJournalLineRequest(credit, "Credit", 0, 1)
      ]));
    Assert.False(journal.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, journal.ErrorCode);
  }

  [Fact]
  public async Task FiscalPeriodReopen_TransitionsClosedPeriodToOpenWithIncrementedRevisionAndAuditTrail()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var periodId = (await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor,
      new CreateFirmPeriodRequest("2026-05"))).Value;

    var close = await LedgerService.CloseFiscalPeriodAsync(db, fixture.ReviewerActor, periodId, "Month end close");
    Assert.True(close.Succeeded, close.Message);

    var closedPeriod = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
    Assert.Equal(LedgerStates.PeriodClosed, closedPeriod.Status);
    Assert.NotNull(closedPeriod.ClosedAt);
    Assert.Equal(2, closedPeriod.Revision);

    var partnerDenied = await LedgerService.RequestPeriodReopenAsync(db, fixture.PartnerActor, periodId, "Reopen attempt");
    Assert.Equal(ErrorCodes.ScopeDenied, partnerDenied.ErrorCode);

    var invalidReason = await LedgerService.RequestPeriodReopenAsync(db, fixture.ManagerActor, periodId, "");
    Assert.Equal("ledger.invalid", invalidReason.ErrorCode);

    var reopen = await LedgerService.RequestPeriodReopenAsync(db, fixture.ManagerActor, periodId, "Adjusting transaction required");
    Assert.True(reopen.Succeeded, reopen.Message);

    var reopenedPeriod = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
    Assert.Equal(LedgerStates.PeriodOpen, reopenedPeriod.Status);
    Assert.Null(reopenedPeriod.ClosedAt);
    Assert.Equal(3, reopenedPeriod.Revision);

    var decisions = await db.PeriodCloseDecisions.AsNoTracking().Where(x => x.PeriodId == periodId).OrderBy(x => x.DecidedAt).ToListAsync();
    Assert.Equal(2, decisions.Count);
    Assert.Equal("CLOSE", decisions[0].DecisionKind);
    Assert.Equal("Month end close", decisions[0].Reason);
    Assert.Equal("REOPEN", decisions[1].DecisionKind);
    Assert.Equal("Adjusting transaction required", decisions[1].Reason);
    Assert.Equal(fixture.Manager.Id, decisions[1].DecidedByUserId);

    var idempotentReopen = await LedgerService.RequestPeriodReopenAsync(db, fixture.ManagerActor, periodId, "Duplicate reopen");
    Assert.True(idempotentReopen.Succeeded);
  }

  [Fact]
  public async Task PartnerDrawingJournal_EndToEndPostingAndReversal_PreservesBalanceAndImmutableAudit()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var cash = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("DRAW-BANK", "Firm Bank Account", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
    var equityDrawings = (await LedgerService.CreateFirmAccountAsync(db, fixture.ManagerActor,
      new CreateFirmAccountRequest("DRAW-PARTNER", "Partner Capital Drawings", LedgerStates.AccountEquity, LedgerStates.Debit))).Value;
    var periodId = (await LedgerService.CreateFirmPeriodAsync(db, fixture.ManagerActor,
      new CreateFirmPeriodRequest("2026-06"))).Value;

    var evidence = Encoding.UTF8.GetBytes("Partner drawing resolution approved 2026-06-15");
    var draftRequest = new CreateFirmJournalDraftRequest(periodId, "JRN-DRAW-001", "MANUAL", "DRAW-RES-2026-06", 1,
      "PARTNER_DRAWING", "QAR", [
        new FirmJournalLineRequest(equityDrawings, "Partner draw distribution", 5000m, 0m),
        new FirmJournalLineRequest(cash, "Bank payout", 0m, 5000m)
      ], "partner-draw-resolution.txt", "text/plain", evidence);

    var journalId = (await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor, draftRequest)).Value;

    var submit = await LedgerService.SubmitFirmJournalAsync(db, fixture.ManagerActor, journalId);
    Assert.True(submit.Succeeded, submit.Message);

    var selfApprove = await LedgerService.ApproveFirmJournalAsync(db, fixture.ManagerActor, journalId);
    Assert.False(selfApprove.Succeeded);

    var approve = await LedgerService.ApproveFirmJournalAsync(db, fixture.ReviewerActor, journalId);
    Assert.True(approve.Succeeded, approve.Message);

    var reviewerPost = await LedgerService.PostFirmJournalAsync(db, fixture.ReviewerActor, journalId);
    Assert.Equal(ErrorCodes.ScopeDenied, reviewerPost.ErrorCode);

    var post = await LedgerService.PostFirmJournalAsync(db, fixture.ManagerActor, journalId);
    Assert.True(post.Succeeded, post.Message);
    var postingId = post.Value;

    var posting = await db.FirmPostings.AsNoTracking().SingleAsync(x => x.Id == postingId);
    Assert.Equal("QAR", posting.Currency);
    Assert.Equal(fixture.Manager.Id, posting.PostedByUserId);

    var lines = await db.FirmPostingLines.AsNoTracking().Where(x => x.PostingId == postingId).ToListAsync();
    Assert.Equal(5000m, lines.Single(x => x.FirmAccountId == equityDrawings).Debit);
    Assert.Equal(5000m, lines.Single(x => x.FirmAccountId == cash).Credit);

    var reversal = await LedgerService.ReverseFirmPostingAsync(db, fixture.ReviewerActor,
      new ReverseFirmPostingRequest(postingId, periodId, "Cancelled drawing resolution"));
    Assert.True(reversal.Succeeded, reversal.Message);

    var revPosting = await db.FirmPostings.AsNoTracking().SingleAsync(x => x.Id == reversal.Value);
    Assert.Equal(postingId, revPosting.ReversalOfPostingId);

    var revLines = await db.FirmPostingLines.AsNoTracking().Where(x => x.PostingId == reversal.Value).ToListAsync();
    Assert.Equal(5000m, revLines.Single(x => x.FirmAccountId == equityDrawings).Credit);
    Assert.Equal(5000m, revLines.Single(x => x.FirmAccountId == cash).Debit);

    var retryReversal = await LedgerService.ReverseFirmPostingAsync(db, fixture.ReviewerActor,
      new ReverseFirmPostingRequest(postingId, periodId, "Cancelled drawing resolution"));
    Assert.True(retryReversal.Succeeded);
    Assert.Equal(reversal.Value, retryReversal.Value);
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "Ledger Client", CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    var manager = User(firmId, "FinanceManager");
    var reviewer = User(firmId, "FinanceReviewer");
    var partner = User(firmId, "Partner");
    db.Users.AddRange(manager, reviewer, partner);
    db.FirmFinanceProfiles.Add(new FirmFinanceProfile
    {
      Id = Guid.NewGuid(), FirmId = firmId, FunctionalCurrency = "QAR",
      ProfileKind = BillingStates.TestProfile, Approved = true,
      ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    db.RoleGrants.AddRange(
      Grant(firmId, manager, "FinanceManager"),
      Grant(firmId, reviewer, "FinanceReviewer"),
      Grant(firmId, partner, "Partner"));
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, manager, reviewer, partner,
      Actor(manager, "FinanceManager"), Actor(reviewer, "FinanceReviewer"), Actor(partner, "Partner"));
  }

  private static AppUser User(Guid firmId, string label) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = $"sub-{label}-{Guid.NewGuid():N}",
    TenantId = "tenant-test", Email = $"{label.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
    DisplayName = label, UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role, Guid? clientId = null) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role, ClientId = clientId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, params string[] roles) =>
    new(user.Id, user.FirmId, user.SessionEpoch, roles);
}
