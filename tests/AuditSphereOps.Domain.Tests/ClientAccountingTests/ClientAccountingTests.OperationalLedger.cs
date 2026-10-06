using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var submitted = await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, journalId, 1);
      Assert.True(submitted.Succeeded, submitted.Message);
      var selfApproval = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, preparer, scope.ClientA, journalId,
        new ClientOperationalJournalDecisionRequest(2, "APPROVE", "Reviewed"));
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, selfApproval.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var posted = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, journalId,
        new ClientOperationalJournalDecisionRequest(2, "APPROVE", "Balanced and supported"));
      Assert.True(posted.Succeeded, posted.Message);
      var replay = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, journalId,
        new ClientOperationalJournalDecisionRequest(2, "APPROVE", "Balanced and supported"));
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(1, await db.ClientOperationalJournalDecisions.CountAsync(x => x.FirmId == scope.FirmId && x.JournalId == journalId));
      var journal = await db.ClientOperationalJournals.SingleAsync(x => x.FirmId == scope.FirmId && x.Id == journalId);
      Assert.Equal("POSTED", journal.Status);
      Assert.Equal(scope.Reviewer.Id, journal.PostedByUserId);
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
