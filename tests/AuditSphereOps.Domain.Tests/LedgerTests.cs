using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
      journalId = (await LedgerService.CreateFirmJournalDraftAsync(db, fixture.ManagerActor,
        new CreateFirmJournalDraftRequest(periodId, "J-100", "INVOICE", "INV-100", 1, "REVENUE", "QAR", [
          new FirmJournalLineRequest(debitAccountId, "Receivable", 100, 0),
          new FirmJournalLineRequest(creditAccountId, "Revenue", 0, 100)
        ]))).Value;
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
      Assert.Equal("ledger.duplicate", duplicateSource.ErrorCode);
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
  public async Task UnbalancedPosting_IsRejectedByCommandAndDeferredDatabaseGuard()
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
      Assert.True((await LedgerService.SubmitFirmJournalAsync(db, fixture.ManagerActor, journalId)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, fixture.ReviewerActor, journalId)).Succeeded);
      var rejected = await LedgerService.PostFirmJournalAsync(db, fixture.ManagerActor, journalId);
      Assert.False(rejected.Succeeded);
      Assert.Equal("ledger.unbalanced", rejected.ErrorCode);
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
      Grant(firmId, manager, "FinanceManager", clientId),
      Grant(firmId, reviewer, "FinanceReviewer", clientId),
      Grant(firmId, partner, "Partner", clientId));
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

  private static RoleGrant Grant(Guid firmId, AppUser user, string role, Guid clientId) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role, ClientId = clientId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, params string[] roles) =>
    new(user.Id, user.FirmId, user.SessionEpoch, roles);
}
