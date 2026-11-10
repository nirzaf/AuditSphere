using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>T073-T075: variance investigation, going-concern assessment and subsequent-event
/// review each derive their conclusion from the recorded evidence and block review while unresolved.</summary>
[Trait("Profile", "Database")]
public sealed class AuditCompletionTests
{
  [Fact(DisplayName = "Variance, going-concern and subsequent-event conclusions follow from evidence")]
  public async Task CompletionRecords_DeriveConclusionsAndBlockUnresolved()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var scope = world.Primary;
    var reviewer = world.Reviewer;
    var siblingPreparer = world.Sibling.Preparer;
    Guid siblingRecordId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Create the sibling evidence through the supported preparer command. Only
      // its exact-engagement grant is present for this actor.
      var siblingRecord = await AuditCompletionService.RecordVarianceInvestigationAsync(db, siblingPreparer,
        new VarianceInvestigationRequest(world.Sibling.EngagementId, null, AuditAreaCodes.Revenue, "2026-Q1",
          100m, 100m, 10m, "QAR", null, ["sibling-tb-extract"]));
      Assert.True(siblingRecord.Succeeded, siblingRecord.Message);
      siblingRecordId = siblingRecord.Value!.InvestigationId;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // --- variance: below threshold is explained; above threshold without an explanation is unexplained.
      var siblingReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "VARIANCE", siblingRecordId, expectedRevision: 1);
      Assert.False(siblingReview.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, siblingReview.ErrorCode);
      Assert.Null(await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
        .Where(x => x.Id == siblingRecordId).Select(x => x.ReviewedByUserId).SingleAsync());

      var belowThreshold = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Revenue, "2026-Q1",
          1000m, 1050m, 100m, "QAR", null, ["tb-extract"]));
      Assert.True(belowThreshold.Succeeded, belowThreshold.Message);
      Assert.False(belowThreshold.Value!.ExceedsThreshold);
      Assert.Equal(VarianceInvestigationConclusions.Explained, belowThreshold.Value.Conclusion);
      Assert.Equal(5m, belowThreshold.Value.DifferencePercent);

      var aboveUnexplained = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Receivables, "2026-Q1",
          1000m, 1400m, 100m, "QAR", null, ["tb-extract"]));
      Assert.True(aboveUnexplained.Succeeded);
      Assert.True(aboveUnexplained.Value!.ExceedsThreshold);
      Assert.Equal(VarianceInvestigationConclusions.Unexplained, aboveUnexplained.Value.Conclusion);
      var missingVarianceBasis = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Receivables, "2026-Q1",
          1000m, 1400m, 100m, "QAR", "Missing caller basis", ["contract-a"]));
      Assert.False(missingVarianceBasis.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, missingVarianceBasis.ErrorCode);
      Assert.Equal(aboveUnexplained.Value.Revision,
        await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
          .Where(x => x.Id == aboveUnexplained.Value.InvestigationId).Select(x => x.Revision).SingleAsync());
      var blockedVarianceReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "VARIANCE", aboveUnexplained.Value.InvestigationId, expectedRevision: aboveUnexplained.Value.Revision);
      Assert.False(blockedVarianceReview.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blockedVarianceReview.ErrorCode);

      // Corroborating the variance clears the block and permits independent review.
      var corroborated = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Receivables, "2026-Q1",
          1000m, 1400m, 100m, "QAR", "Volume growth from the two signed contracts shipped in March.",
          ["contract-a", "contract-b"], ExpectedRevision: aboveUnexplained.Value.Revision));
      Assert.True(corroborated.Succeeded);
      Assert.Equal(VarianceInvestigationConclusions.Corroborated, corroborated.Value!.Conclusion);
      Assert.Equal(2, corroborated.Value.Revision);
      var staleSessionReview = await AuditCompletionService.ReviewCompletionRecordAsync(db,
        reviewer with { SessionEpoch = 0 },
        "VARIANCE", corroborated.Value.InvestigationId, expectedRevision: corroborated.Value.Revision);
      Assert.False(staleSessionReview.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleSessionReview.ErrorCode);
      Assert.Null(await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
        .Where(x => x.Id == corroborated.Value.InvestigationId).Select(x => x.ReviewedByUserId).SingleAsync());
      var staleVarianceReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "VARIANCE", corroborated.Value.InvestigationId, expectedRevision: corroborated.Value.Revision - 1);
      Assert.False(staleVarianceReview.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleVarianceReview.ErrorCode);
      Assert.Null(await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
        .Where(x => x.Id == corroborated.Value.InvestigationId).Select(x => x.ReviewedByUserId).SingleAsync());
      var currentReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "VARIANCE", corroborated.Value.InvestigationId, expectedRevision: corroborated.Value.Revision);
      Assert.True(currentReview.Succeeded, currentReview.Message);
      var reviewedRecord = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
        .SingleAsync(x => x.Id == corroborated.Value.InvestigationId);
      Assert.Equal(corroborated.Value.Revision + 1, reviewedRecord.Revision);
      Assert.Equal(reviewer.UserId, reviewedRecord.ReviewedByUserId);
      Assert.Equal(corroborated.Value.Revision, reviewedRecord.ReviewedRevision);
      Assert.True(AuditFieldworkService.IsCurrentReviewedBasis(reviewedRecord.ReviewedByUserId,
        reviewedRecord.ReviewedAt, reviewedRecord.ReviewedRevision, reviewedRecord.Revision));
      Assert.False(AuditFieldworkService.IsCurrentReviewedBasis(null, reviewedRecord.ReviewedAt,
        reviewedRecord.ReviewedRevision, reviewedRecord.Revision));
      Assert.False(AuditFieldworkService.IsCurrentReviewedBasis(reviewedRecord.ReviewedByUserId, null,
        reviewedRecord.ReviewedRevision, reviewedRecord.Revision));
      Assert.False(AuditFieldworkService.IsCurrentReviewedBasis(reviewedRecord.ReviewedByUserId,
        reviewedRecord.ReviewedAt, reviewedRecord.Revision, reviewedRecord.Revision));
      Assert.True(AuditFieldworkService.IsReviewedBasisCurrent(
        reviewedRecord.ReviewedRevision, reviewedRecord.Revision));
      Assert.False(AuditFieldworkService.IsReviewedBasisCurrent(null, reviewedRecord.Revision));
      Assert.False(AuditFieldworkService.IsReviewedBasisCurrent(
        reviewedRecord.Revision, reviewedRecord.Revision));
      Assert.False(AuditFieldworkService.IsReviewedBasisCurrent(long.MaxValue, long.MinValue));
      var selfReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, scope.Preparer,
        "VARIANCE", corroborated.Value.InvestigationId, expectedRevision: reviewedRecord.Revision);
      Assert.False(selfReview.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, selfReview.ErrorCode);
      var afterSelfReview = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
        .SingleAsync(x => x.Id == corroborated.Value.InvestigationId);
      Assert.Equal((reviewedRecord.Revision, reviewedRecord.ReviewedByUserId,
          reviewedRecord.ReviewedAt, reviewedRecord.ReviewedRevision),
        (afterSelfReview.Revision, afterSelfReview.ReviewedByUserId,
          afterSelfReview.ReviewedAt, afterSelfReview.ReviewedRevision));
      var revisedAfterReview = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Receivables, "2026-Q1",
          1000m, 1200m, 100m, "QAR", "New evidence after independent review.", ["contract-c"],
          ExpectedRevision: reviewedRecord.Revision));
      Assert.True(revisedAfterReview.Succeeded, revisedAfterReview.Message);
      var reopened = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
        .SingleAsync(x => x.Id == corroborated.Value.InvestigationId);
      Assert.Null(reopened.ReviewedByUserId);
      Assert.Null(reopened.ReviewedAt);
      Assert.Null(reopened.ReviewedRevision);

      // --- going concern: no uncertainty needs no disclosure; uncertainty with disclosure is recorded.
      var noUncertainty = await AuditCompletionService.RecordGoingConcernAssessmentAsync(db, scope.Preparer,
        new GoingConcernAssessmentRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 12, 31), "Twelve-month forecast reviewed with no indicators of concern.",
          false, true, "QAR", "Cash-flow forecast supports operations for at least twelve months.",
          ["board-minutes", "cashflow-forecast"]));
      Assert.True(noUncertainty.Succeeded, noUncertainty.Message);
      Assert.Equal(GoingConcernConclusions.NoMaterialUncertainty, noUncertainty.Value!.Conclusion);
      var reviewedGoingConcern = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "GOING_CONCERN", noUncertainty.Value.AssessmentId, expectedRevision: noUncertainty.Value.Revision);
      Assert.True(reviewedGoingConcern.Succeeded, reviewedGoingConcern.Message);
      var goingConcern = await db.GoingConcernAssessments.AsNoTracking()
        .SingleAsync(x => x.Id == noUncertainty.Value.AssessmentId);
      Assert.True(AuditFieldworkService.IsReviewedBasisCurrent(
        goingConcern.ReviewedRevision, goingConcern.Revision));

      var uncertainty = await AuditCompletionService.RecordGoingConcernAssessmentAsync(db, scope.Preparer,
        new GoingConcernAssessmentRequest(scope.EngagementId, null, new DateOnly(2026, 12, 15),
          new DateOnly(2027, 12, 31), "Forecast shows a covenant breach risk within the next twelve months.",
          true, false, "QAR", "Material uncertainty identified; disclosure assessment pending.",
          ["cashflow-forecast"]));
      Assert.True(uncertainty.Succeeded);
      Assert.Equal(GoingConcernConclusions.InadequateDisclosure, uncertainty.Value!.Conclusion);
      var blockedGoingConcern = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "GOING_CONCERN", uncertainty.Value.AssessmentId, expectedRevision: uncertainty.Value.Revision);
      Assert.False(blockedGoingConcern.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blockedGoingConcern.ErrorCode);

      // --- subsequent events: the classification is recorded, never inferred.
      var pendingEvent = await AuditCompletionService.RecordSubsequentEventAsync(db, scope.Preparer,
        new SubsequentEventReviewRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 1, 20), "Major customer entered administration in January 2027.",
          SubsequentEventClassifications.PendingAssessment, false, false, null, null, "QAR", null,
          ["news-article-1"]));
      Assert.True(pendingEvent.Succeeded, pendingEvent.Message);
      Assert.Equal(1, pendingEvent.Value!.PendingEventCount);
      var blockedEventReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "SUBSEQUENT_EVENT", pendingEvent.Value.ReviewId, expectedRevision: pendingEvent.Value.Revision);
      Assert.False(blockedEventReview.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blockedEventReview.ErrorCode);

      // A non-adjusting event with an inconsistent adjustment flag is refused.
      var inconsistent = await AuditCompletionService.RecordSubsequentEventAsync(db, scope.Preparer,
        new SubsequentEventReviewRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 1, 20), "Inconsistent classification", SubsequentEventClassifications.NonAdjusting,
          true, false, null, "Rationale", "QAR", null, ["evidence"]));
      Assert.False(inconsistent.Succeeded);

      // Disclosure without a reference is refused.
      var missingDisclosure = await AuditCompletionService.RecordSubsequentEventAsync(db, scope.Preparer,
        new SubsequentEventReviewRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 1, 20), "Disclosure needed", SubsequentEventClassifications.NonAdjusting,
          false, true, null, "Rationale", "QAR", null, ["evidence"]));
      Assert.False(missingDisclosure.Succeeded);

      var classified = await AuditCompletionService.RecordSubsequentEventAsync(db, scope.Preparer,
        new SubsequentEventReviewRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 1, 20), "Major customer entered administration in January 2027.",
          SubsequentEventClassifications.NonAdjusting, false, true, "Note 27 subsequent events",
          "Conditions arose after the reporting date, so no adjustment is made.", "QAR", 250000m,
          ["news-article-1", "admin-order"]));
      Assert.True(classified.Succeeded);
      Assert.Equal(2, classified.Value!.Revision);
      Assert.Equal(0, classified.Value.PendingEventCount);
      Assert.True((await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "SUBSEQUENT_EVENT", classified.Value.ReviewId, expectedRevision: classified.Value.Revision)).Succeeded);

      var unsupportedKind = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "NOT_A_KIND", classified.Value.ReviewId, expectedRevision: classified.Value.Revision);
      Assert.False(unsupportedKind.Succeeded);

      // AC-3 revoked-grant case: revoke through the supported administration command, refresh
      // the actor's session epoch, and prove denial is due to the absent exact-scope grant.
      var revocationTarget = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.CashBank, "2026-Q2",
          800m, 804m, 10m, "QAR", "Small explained movement", ["cash-reconciliation"]));
      Assert.True(revocationTarget.Succeeded, revocationTarget.Message);
      var exactGrant = await db.RoleGrants.AsNoTracking().SingleAsync(x =>
        x.FirmId == scope.FirmId && x.UserId == reviewer.UserId && x.Role == "Manager" &&
        x.ClientId == scope.ClientId &&
        x.EngagementId == scope.EngagementId && x.RevokedAt == null);
      var revoke = await RoleAdministrationService.RevokeRoleGrantAsync(db, world.Administrator,
        new RevokeRoleGrantRequest(exactGrant.Id, Reason: "Focused R02 revoked-grant regression."));
      Assert.True(revoke.Succeeded, revoke.Message);
      var currentReviewerEpoch = await db.Users.AsNoTracking().Where(x => x.Id == reviewer.UserId)
        .Select(x => x.SessionEpoch).SingleAsync();
      var currentButRevokedReviewer = reviewer with { SessionEpoch = currentReviewerEpoch };
      var revokedGrantReview = await AuditCompletionService.ReviewCompletionRecordAsync(db,
        currentButRevokedReviewer, "VARIANCE", revocationTarget.Value!.InvestigationId,
        expectedRevision: revocationTarget.Value.Revision);
      Assert.False(revokedGrantReview.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, revokedGrantReview.ErrorCode);
      var retainedAfterRevocation = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
        .SingleAsync(x => x.Id == revocationTarget.Value.InvestigationId);
      Assert.Equal(revocationTarget.Value.Revision, retainedAfterRevocation.Revision);
      Assert.Null(retainedAfterRevocation.ReviewedByUserId);
      Assert.Null(retainedAfterRevocation.ReviewedAt);
      Assert.Null(retainedAfterRevocation.ReviewedRevision);
    }
  }

  [Fact(DisplayName = "R02 rejects stale variance preparation and review bases across contexts")]
  public async Task VarianceReviewAndPreparation_RejectStaleTrackedBasisInterleavings()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var scope = world.Primary;
    var reviewer = world.Reviewer;
    var reviewerId = reviewer.UserId;

    // Interleaving 1: preparation reads and tracks revision 1 before an independent review commits.
    Guid preparedId;
    await using (var create = new AuditSphereDbContext(pg.Options))
    {
      var created = await AuditCompletionService.RecordVarianceInvestigationAsync(create, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Revenue, "2026-Q1",
          100m, 105m, 10m, "QAR", "Original basis", ["source-a"]));
      Assert.True(created.Succeeded, created.Message);
      preparedId = created.Value!.InvestigationId;
    }
    await using var preparer = new AuditSphereDbContext(pg.Options);
    var trackedForPreparation = await preparer.AnalyticalReviewVarianceInvestigations
      .SingleAsync(x => x.Id == preparedId);
    Assert.Equal(1, trackedForPreparation.Revision);
    await using (var independentReviewer = new AuditSphereDbContext(pg.Options))
    {
      var reviewed = await AuditCompletionService.ReviewCompletionRecordAsync(independentReviewer, reviewer,
        "VARIANCE", preparedId, expectedRevision: 1);
      Assert.True(reviewed.Succeeded, reviewed.Message);
    }
    var preparedAfterReview = await AuditCompletionService.RecordVarianceInvestigationAsync(preparer, scope.Preparer,
      new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Revenue, "2026-Q1",
        100m, 140m, 10m, "QAR", null, ["source-b"], ExpectedRevision: 1));
    Assert.False(preparedAfterReview.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, preparedAfterReview.ErrorCode);
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var current = await verify.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleAsync(x => x.Id == preparedId);
      Assert.Equal(105m, current.ActualAmount);
      Assert.Equal(2, current.Revision);
      Assert.Equal(reviewerId, current.ReviewedByUserId);
    }

    // Interleaving 2: reviewer context tracks revision 1, a preparer commits an unresolved revision 2
    // through the supported command, then the tracked reviewer submits the old basis.
    Guid staleReviewId;
    await using (var create = new AuditSphereDbContext(pg.Options))
    {
      var created = await AuditCompletionService.RecordVarianceInvestigationAsync(create, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Receivables, "2026-Q2",
          100m, 105m, 10m, "QAR", "Original basis", ["source-c"]));
      Assert.True(created.Succeeded, created.Message);
      staleReviewId = created.Value!.InvestigationId;
    }
    await using var reviewerContext = new AuditSphereDbContext(pg.Options);
    var trackedForReview = await reviewerContext.AnalyticalReviewVarianceInvestigations
      .SingleAsync(x => x.Id == staleReviewId);
    Assert.Equal(1, trackedForReview.Revision);
    await using (var writer = new AuditSphereDbContext(pg.Options))
    {
      var revised = await AuditCompletionService.RecordVarianceInvestigationAsync(writer, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Receivables, "2026-Q2",
          100m, 140m, 10m, "QAR", null, ["source-d"], ExpectedRevision: 1));
      Assert.True(revised.Succeeded, revised.Message);
      Assert.Equal(2, revised.Value!.Revision);
    }
    var staleReview = await AuditCompletionService.ReviewCompletionRecordAsync(reviewerContext, reviewer,
      "VARIANCE", staleReviewId, expectedRevision: 1);
    Assert.False(staleReview.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleReview.ErrorCode);
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var current = await verify.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleAsync(x => x.Id == staleReviewId);
      Assert.Equal(2, current.Revision);
      Assert.Equal(VarianceInvestigationConclusions.Unexplained, current.Conclusion);
      Assert.Null(current.ReviewedByUserId);
    }
  }

  [Fact(DisplayName = "R02 downgrade preserves recorded review-basis evidence")]
  public async Task ReviewedBasisMigration_RefusesDowngradeAfterReviewEvidenceExists()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var scope = world.Primary;
    var reviewer = world.Reviewer;
    Guid recordId;
    long revision;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Preparer,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Revenue, "2026-Q1",
          100m, 105m, 10m, "QAR", "Review basis retained", ["source-a"]));
      Assert.True(created.Succeeded, created.Message);
      recordId = created.Value!.InvestigationId;
      revision = created.Value.Revision;
    }

    await using var writer = new AuditSphereDbContext(pg.Options);
    var reviewed = await AuditCompletionService.ReviewCompletionRecordAsync(writer, reviewer,
      "VARIANCE", recordId, expectedRevision: revision);
    Assert.True(reviewed.Succeeded, reviewed.Message);

    var migrationConnection = new Npgsql.NpgsqlConnection(pg.ConnectionString);
    await using (migrationConnection)
    {
      await migrationConnection.OpenAsync();
      await using var migrationDb = new AuditSphereDbContext(
        new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(migrationConnection).Options);
      await using var migrationTransaction = await migrationDb.Database.BeginTransactionAsync();
      var migrationsAssembly = migrationDb.GetService<IMigrationsAssembly>();
      var migrationType = migrationsAssembly.Migrations.Single(x =>
        x.Key.EndsWith("_R02ReviewedBasisIdentity", StringComparison.Ordinal)).Value;
      var migration = migrationsAssembly.CreateMigration(migrationType,
        migrationDb.Database.ProviderName!);
      var commands = migrationDb.GetService<IMigrationsSqlGenerator>()
        .Generate(migration.DownOperations, migration.TargetModel);
      var sql = string.Join("\n", commands.Select(x => x.CommandText));
      Assert.True(sql.IndexOf("LOCK TABLE", StringComparison.OrdinalIgnoreCase) >= 0);
      Assert.True(sql.IndexOf("LOCK TABLE", StringComparison.OrdinalIgnoreCase) <
        sql.IndexOf("DO $$", StringComparison.Ordinal));

      var downgrade = Task.Run(async () =>
      {
        foreach (var command in commands)
          await migrationDb.Database.ExecuteSqlRawAsync(command.CommandText);
      });
      var refused = await Assert.ThrowsAsync<PostgresException>(async () => await downgrade);
      Assert.Equal(PostgresErrorCodes.RaiseException, refused.SqlState);
      Assert.Equal("R02 downgrade would discard retained completion review-basis evidence.", refused.MessageText);
      await migrationTransaction.RollbackAsync();
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Contains("20261110093821_R02ReviewedBasisIdentity", await verify.Database.GetAppliedMigrationsAsync());
    var retained = await verify.AnalyticalReviewVarianceInvestigations.AsNoTracking()
      .SingleAsync(x => x.Id == recordId);
    Assert.Equal(revision, retained.ReviewedRevision);
    Assert.Equal(revision + 1, retained.Revision);
  }

}
