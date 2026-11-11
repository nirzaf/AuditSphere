using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditPlanningCompletionTests
{
  internal static OpeningBalanceVerificationRequest Request(Guid engagementId) => new(engagementId, null, null,
    "FY2025 audited FS", new(2026, 1, 1), "QAR", 1000.123456m, 1000.123456m, true,
    "Opening balances agree to the prior-year audited statements.", ["prior-year-audited-fs", "ledger-opening-tb"], 0);

  internal static async Task<CommandResult<OpeningBalanceVerificationValue>> Prepare(PgTestSchema pg, ActorContext actor, OpeningBalanceVerificationRequest request)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, actor, request);
  }
  internal static async Task<CommandResult> Review(PgTestSchema pg, ActorContext actor, OpeningBalanceReviewRequest request)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(db, actor, request);
  }
  internal static async Task<OpeningBalanceVerificationDetail> Read(PgTestSchema pg, ActorContext actor, Guid engagementId, Guid id)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await AuditPlanningCompletionService.GetOpeningBalanceVerificationAsync(db, actor, engagementId, id);
    Assert.True(result.Succeeded, result.Message); return result.Value!;
  }

  [Fact(DisplayName = "Opening-balance verification derives its conclusion and blocks unexplained differences")]
  public async Task OpeningBalanceVerification_DerivesConclusionAndBlocksUnresolved()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var scope = world.Primary;
    var request = Request(scope.EngagementId);
    Guid periodId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var period = await ClientAccountingService.CreatePeriodAsync(db, world.Administrator,
        new(scope.ClientId, "FY2025", new(2025, 1, 1), new(2025, 12, 31), "IFRS", "QAR"));
      Assert.True(period.Succeeded, period.Message); periodId = period.Value;
    }
    request = request with { PriorPeriodId = periodId };
    Assert.False((await Prepare(pg, scope.Preparer, request with { EvidenceReferences = [] })).Succeeded);
    var agreed = await Prepare(pg, scope.Preparer, request);
    Assert.True(agreed.Succeeded, agreed.Message);
    var id = agreed.Value!.VerificationId;
    var initial = await Read(pg, scope.Preparer, scope.EngagementId, id);
    Assert.Equal(OpeningBalanceVerificationConclusions.Agreed, initial.Conclusion);
    Assert.Equal(0m, initial.DifferenceAmount); Assert.Equal(1000.123456m, initial.OpeningSignedTotal);
    Assert.Equal(initial.OpeningSignedTotal, initial.AgreedSignedTotal);
    Assert.Equal(periodId, initial.PriorPeriodId); Assert.Null(initial.ProcedureId);
    Assert.Equal(request.EvidenceReferences, initial.EvidenceReferences);
    Assert.True(initial.ComparisonResolved); Assert.False(initial.CurrentIndependentReview);
    Assert.Null(initial.ReviewedByUserId); Assert.Null(initial.ReviewedBasisRevision);
    Assert.Equal(ErrorCodes.ScopeDenied, (await Review(pg, scope.Preparer, new(scope.EngagementId, id, 1, "Self"))).ErrorCode);
    Assert.True((await Review(pg, world.Reviewer, new(scope.EngagementId, id, 1, "Agreed to prior-year file."))).Succeeded);
    var reviewed = await Read(pg, scope.Preparer, scope.EngagementId, id);
    Assert.Equal(2, reviewed.Revision); Assert.Equal(1, reviewed.ReviewedBasisRevision);
    Assert.Equal(world.Reviewer.UserId, reviewed.ReviewedByUserId); Assert.NotNull(reviewed.ReviewedAt);
    Assert.Equal("Agreed to prior-year file.", reviewed.ReviewComment); Assert.True(reviewed.CurrentIndependentReview);
    Assert.Equal(ErrorCodes.GateBlocked, (await Review(pg, world.Reviewer, new(scope.EngagementId, id, 2, "Replacement review"))).ErrorCode);
    var unchangedReview = await Read(pg, scope.Preparer, scope.EngagementId, id);
    Assert.Equal(reviewed.Revision, unchangedReview.Revision); Assert.Equal(reviewed.ReviewedByUserId, unchangedReview.ReviewedByUserId);
    Assert.Equal(reviewed.ReviewComment, unchangedReview.ReviewComment); Assert.Equal(reviewed.ReviewedAt, unchangedReview.ReviewedAt);
    Assert.Equal(ErrorCodes.StaleRevision, (await Prepare(pg, scope.Preparer, request)).ErrorCode);
    Assert.Equal(ErrorCodes.StaleRevision, (await Prepare(pg, scope.Preparer, request with { ExpectedRevision = 1, ExpectedVerificationId = id })).ErrorCode);
    Assert.Equal(ErrorCodes.StaleRevision, (await Review(pg, world.Reviewer, new(scope.EngagementId, id, 1, null))).ErrorCode);
    var unresolved = await Prepare(pg, scope.Preparer, request with { ExpectedRevision = 2, ExpectedVerificationId = id, AgreedSignedTotal = 975.123456m });
    Assert.True(unresolved.Succeeded, unresolved.Message);
    Assert.Equal(25m, unresolved.Value!.DifferenceAmount);
    Assert.Equal(OpeningBalanceVerificationConclusions.DifferencesUnresolved, unresolved.Value.Conclusion);
    Assert.Equal(ErrorCodes.GateBlocked, (await Review(pg, world.Reviewer, new(scope.EngagementId, id, 3, "Reviewed."))).ErrorCode);
    var amended = await Read(pg, scope.Preparer, scope.EngagementId, id);
    Assert.False(amended.CurrentIndependentReview); Assert.Null(amended.ReviewedByUserId); Assert.Null(amended.ReviewedAt);
    Assert.Equal(1, amended.ReviewedBasisRevision); Assert.Equal(reviewed.ReviewComment, amended.ReviewComment);
    var resolved = await Prepare(pg, scope.Preparer, request with { ExpectedRevision = 3, ExpectedVerificationId = id });
    Assert.True(resolved.Succeeded, resolved.Message); Assert.Equal(4, resolved.Value!.Revision);
    Assert.Equal(OpeningBalanceVerificationConclusions.Agreed, resolved.Value.Conclusion);
    // Preserve the existing zero-difference/policy-inconsistent classification, not an invented policy gate.
    var inconsistent = await Prepare(pg, scope.Preparer, request with { ExpectedRevision = 4, ExpectedVerificationId = id, AccountingPoliciesConsistent = false });
    Assert.True(inconsistent.Succeeded, inconsistent.Message);
    Assert.Equal(OpeningBalanceVerificationConclusions.DifferencesResolved, inconsistent.Value!.Conclusion);
    Assert.True((await Review(pg, world.Reviewer, new(scope.EngagementId, id, 5, null))).Succeeded);
    Assert.True((await Read(pg, scope.Preparer, scope.EngagementId, id)).CurrentIndependentReview);
    await using (var migrationDb = new AuditSphereDbContext(pg.Options))
    {
      var migrations = migrationDb.Database.GetMigrations().ToArray();
      var index = Array.FindIndex(migrations, x => x.EndsWith("_OpeningBalanceReviewedBasis", StringComparison.Ordinal));
      Assert.True(index > 0);
      var failure = await Record.ExceptionAsync(() => migrationDb.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
      Assert.NotNull(failure);
      while (failure is not PostgresException && failure.InnerException is not null) failure = failure.InnerException;
      var postgres = Assert.IsType<PostgresException>(failure);
      Assert.Equal("55000", postgres.SqlState); Assert.Contains("retained opening-balance", postgres.MessageText);
      Assert.Contains(migrations[index], await migrationDb.Database.GetAppliedMigrationsAsync());
      Assert.True((await Read(pg, scope.Preparer, scope.EngagementId, id)).CurrentIndependentReview);
    }
    await using var summaryDb = new AuditSphereDbContext(pg.Options);
    var materiality = await AuditPlanningService.CreateMaterialityAssessmentAsync(summaryDb, scope.Preparer,
      new(scope.EngagementId, "Revenue", "2026-Q1", "Revenue benchmark", 1000m, 0.01m, 10m, 5m, 0.5m, null));
    Assert.True(materiality.Succeeded, materiality.Message);
    var summary = await AuditPlanningCompletionService.GetAuditPlanningSummaryAsync(summaryDb, scope.Preparer, scope.EngagementId);
    Assert.True(summary.Succeeded, summary.Message); Assert.NotNull(summary.Value!.LatestMaterialityId);
    Assert.Equal(10m, summary.Value.OverallMateriality); Assert.Equal("Revenue", summary.Value.MaterialityBenchmarkSource);
    Assert.Equal(OpeningBalanceVerificationConclusions.DifferencesResolved, summary.Value.OpeningBalanceConclusion);
    Assert.Equal(id, summary.Value.OpeningBalanceVerificationId); Assert.True(summary.Value.OpeningBalancesResolved);
    Assert.Equal(0, summary.Value.RiskCount); Assert.Equal(1, await summaryDb.OpeningBalanceVerifications.CountAsync());
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditPlanningCompletionService.GetAuditPlanningSummaryAsync(summaryDb, scope.Preparer, world.Sibling.EngagementId)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditPlanningCompletionService.GetAuditPlanningSummaryAsync(summaryDb, scope.Preparer, Guid.NewGuid())).ErrorCode);
  }
}
