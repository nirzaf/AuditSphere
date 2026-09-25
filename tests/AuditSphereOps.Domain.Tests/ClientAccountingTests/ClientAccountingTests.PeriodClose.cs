using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task ClosedPeriodRestatement_PreservesIssuedPackagesAndRequiresIndependentApproval()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid periodId, originalPackageId, revisedPackageId, restatementId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Issued package")).Succeeded);
      originalPackageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "original");
      revisedPackageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 90m, "CASH", "revised");
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "AccountingReviewer"));
      await db.SaveChangesAsync();

      var created = await ClientAccountingService.CreatePeriodRestatementAsync(db, preparer,
        new CreatePeriodRestatementRequest(scope.ClientA, periodId, originalPackageId, revisedPackageId,
          "IAS 8", "Prior-period error identified", "restatement-evidence",
          PeriodRestatementChangeTypes.RestatedError, "FY2026"));
      Assert.True(created.Succeeded, created.Message);

      var selfApproval = await ClientAccountingService.ApprovePeriodRestatementAsync(db, preparer, created.Value);
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, selfApproval.ErrorCode);

      var approved = await ClientAccountingService.ApprovePeriodRestatementAsync(db, reviewer, created.Value);
      Assert.True(approved.Succeeded, approved.Message);
      var restatement = await db.ClientPeriodRestatements.SingleAsync(x => x.Id == created.Value);
      restatementId = restatement.Id;
      Assert.Equal(AccountingWorkflowStates.Approved, restatement.Status);
      Assert.Equal(scope.Reviewer.Id, restatement.ApprovedByUserId);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2, await verify.FinancialPackages.CountAsync(x => x.ClientId == scope.ClientA));
    Assert.Equal(AccountingPackageStates.PackageValidated,
      await verify.FinancialPackages.Where(x => x.Id == originalPackageId).Select(x => x.Status).SingleAsync());
    Assert.Equal(AccountingPackageStates.PackageValidated,
      await verify.FinancialPackages.Where(x => x.Id == revisedPackageId).Select(x => x.Status).SingleAsync());
    var tampered = "tampered";
    var mutation = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_period_restatements SET reason = {tampered} WHERE id = {restatementId}"));
    Assert.Equal("55000", mutation.SqlState);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task PeriodReopen_RecordsImmutableRevisionLineage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var partner = Actor(scope.Partner, "Partner");
    Guid periodId, amendmentId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Initial close")).Succeeded);
      Assert.True((await ClientAccountingService.ReopenPeriodAsync(db, partner, periodId, "Correct the approved opening bridge")).Succeeded);

      var period = await db.ClientReportingPeriods.SingleAsync(x => x.Id == periodId);
      Assert.Equal(AccountingWorkflowStates.Draft, period.Status);
      Assert.Equal(3, period.Revision);
      var first = await db.ClientPeriodAmendments.SingleAsync(x => x.PeriodId == periodId);
      Assert.Equal(2, first.PreviousRevision);
      Assert.Equal(3, first.AmendmentRevision);
      Assert.Equal("Correct the approved opening bridge", first.Reason);
      amendmentId = first.Id;

      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Corrected close")).Succeeded);
      Assert.True((await ClientAccountingService.ReopenPeriodAsync(db, partner, periodId, "Record the final correction")).Succeeded);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var amendments = await verify.ClientPeriodAmendments.Where(x => x.PeriodId == periodId)
      .OrderBy(x => x.AmendmentRevision).ToListAsync();
    Assert.Equal(2, amendments.Count);
    Assert.Equal((2L, 3L), (amendments[0].PreviousRevision, amendments[0].AmendmentRevision));
    Assert.Equal((4L, 5L), (amendments[1].PreviousRevision, amendments[1].AmendmentRevision));
    var mutation = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE client_period_amendments SET reason = {"tampered"} WHERE id = {amendmentId}"));
    Assert.Equal("55000", mutation.SqlState);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task PeriodClose_RequiresCurrentReviewsForMatchingFinancialPackages()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var partner = Actor(scope.Partner, "Partner");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      var packageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "period-close-gate");
      await db.SaveChangesAsync();

      var blocked = await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Package approvals pending");
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);

      Assert.True((await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "management-close-gate", "Management approved the exact package."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "accounting-close-gate", "Accounting review completed."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "partner-close-gate", "Partner approval completed."))).Succeeded);

      var closed = await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "All package approvals complete");
      Assert.True(closed.Succeeded, closed.Message);
      Assert.Equal(AccountingWorkflowStates.Closed,
        await db.ClientReportingPeriods.Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync());
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task PeriodRollForward_CopiesDraftBooksAndRequiresOpeningEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid priorPeriodId, nextPeriodId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      priorPeriodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, priorPeriodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded);
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, priorPeriodId, "Prior period issued")).Succeeded);

      var rolled = await ClientAccountingService.RollForwardPeriodAsync(db, preparer,
        new RollForwardPeriodRequest(scope.ClientA, priorPeriodId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
          "STATUTORY", "QAR", new string('f', 64), 100m, 100m, "signed-prior-closing"));
      Assert.True(rolled.Succeeded, rolled.Message);
      nextPeriodId = rolled.Value;

      var next = await db.ClientReportingPeriods.SingleAsync(x => x.Id == nextPeriodId);
      Assert.Equal(priorPeriodId, next.PriorPeriodId);
      Assert.Equal(AccountingWorkflowStates.Draft, next.Status);
      Assert.Equal(1, next.Revision);
      var book = await db.ClientReportingBooks.SingleAsync(x => x.PeriodId == nextPeriodId);
      Assert.Equal("STAT", book.Code);
      Assert.Equal(AccountingWorkflowStates.Draft, book.Status);
      var bridge = await db.OpeningBalanceBridges.SingleAsync(x => x.CurrentPeriodId == nextPeriodId);
      Assert.Equal("RECONCILED", bridge.Status);
      Assert.Null(bridge.ApprovedByUserId);

      var duplicate = await ClientAccountingService.RollForwardPeriodAsync(db, preparer,
        new RollForwardPeriodRequest(scope.ClientA, priorPeriodId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
          "STATUTORY", "QAR", new string('f', 64), 100m, 100m, "signed-prior-closing"));
      Assert.False(duplicate.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicate.ErrorCode);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await verify.ClientReportingPeriods.CountAsync(x => x.PriorPeriodId == priorPeriodId));
    Assert.Equal(1, await verify.OpeningBalanceBridges.CountAsync(x => x.CurrentPeriodId == nextPeriodId));
  }
}
