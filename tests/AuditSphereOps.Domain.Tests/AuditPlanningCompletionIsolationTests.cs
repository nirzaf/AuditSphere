using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static AuditSphereOps.Domain.Tests.AuditPlanningCompletionTests;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditPlanningCompletionIsolationTests
{
  [Fact(DisplayName = "Client-scoped opening-balance commands deny sibling engagement identifiers")]
  public async Task SiblingEngagementIds_AreDeniedForOpeningBalanceRecordAndReview()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var scope = world.Primary;
    var request = Request(scope.EngagementId);
    var created = await Prepare(pg, scope.Preparer, request);
    Assert.True(created.Succeeded, created.Message); var id = created.Value!.VerificationId;
    var sibling = await Prepare(pg, world.Sibling.Preparer, Request(world.Sibling.EngagementId));
    Assert.True(sibling.Succeeded, sibling.Message);
    var staffDetail = await Read(pg, world.Sibling.Preparer, world.Sibling.EngagementId, sibling.Value!.VerificationId);
    Assert.Equal(world.Sibling.Preparer.UserId, staffDetail.RecordedByUserId);
    Assert.Equal(Request(world.Sibling.EngagementId).EvidenceReferences, staffDetail.EvidenceReferences);
    Assert.False(staffDetail.CurrentIndependentReview);
    Assert.Equal(ErrorCodes.ScopeDenied, (await Review(pg, world.Sibling.Preparer,
      new(world.Sibling.EngagementId, sibling.Value.VerificationId, 1, null))).ErrorCode);
    await using (var detailDb = new AuditSphereDbContext(pg.Options))
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditPlanningCompletionService.GetOpeningBalanceVerificationAsync(detailDb,
        world.Sibling.Preparer, scope.EngagementId, id)).ErrorCode);

    Guid primaryProcedure; Guid siblingProcedure; Guid foreignPeriod;
    var otherClient = await SupportedAuditCompletionWorldBuilder.CreateAdditionalClientAsync(pg, world);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var partnerId = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == world.Administrator.FirmId &&
        x.Role == "Partner" && x.ClientId == null && x.RevokedAt == null).Select(x => x.UserId).SingleAsync();
      var partner = await db.Users.AsNoTracking().SingleAsync(x => x.Id == partnerId);
      var draft = await AuditProgramService.PrepareControlledAuditProgramAsync(db, world.Administrator, new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(draft.Succeeded, draft.Message);
      var published = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db,
        new ActorContext(partner.Id, partner.FirmId, partner.SessionEpoch, ["Partner"]),
        new(draft.Value!.ProgramVersionId, draft.Value.ContentDigest!, "Synthetic R06 controlled program review."));
      Assert.True(published.Succeeded, published.Message);
      foreach (var target in new[] { world.Primary, world.Sibling })
        Assert.True((await AuditProgramService.AdoptAsync(db, target.Preparer,
          new(target.EngagementId, published.Value!.ProgramVersionId, AuditProgramCatalog.SourceHash, published.Value.ContentDigest))).Succeeded);
      primaryProcedure = await db.AuditProcedures.Where(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-01-01").Select(x => x.Id).SingleAsync();
      siblingProcedure = await db.AuditProcedures.Where(x => x.EngagementId == world.Sibling.EngagementId && x.SourceProcedureId == "AWP-01-01").Select(x => x.Id).SingleAsync();
      var period = await ClientAccountingService.CreatePeriodAsync(db, world.Administrator,
        new(otherClient, "FY2024", new(2024, 1, 1), new(2024, 12, 31), "IFRS", "QAR"));
      Assert.True(period.Succeeded, period.Message); foreignPeriod = period.Value;
      var blocked = await EngagementLifecycleService.CreateDraftAsync(db,
        new ActorContext(partner.Id, partner.FirmId, partner.SessionEpoch, ["Partner"]),
        new(scope.ClientId, "FinancialStatementAudit", "2028-01-01", "2028-12-31", "FinancialStatementAudit"));
      Assert.True(blocked.Succeeded, blocked.Message);
      Assert.Equal(ErrorCodes.GateBlocked, (await Prepare(pg, world.Administrator, Request(blocked.Value))).ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, (await Review(pg, world.Administrator, new(blocked.Value, id, 1, null))).ErrorCode);
    }
    Assert.Equal(ErrorCodes.ScopeDenied, (await Prepare(pg, scope.Preparer, request with { ExpectedRevision = 1, ExpectedVerificationId = id, ProcedureId = siblingProcedure })).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await Prepare(pg, scope.Preparer, request with { ExpectedRevision = 1, ExpectedVerificationId = id, PriorPeriodId = foreignPeriod })).ErrorCode);
    // An optional same-engagement procedure is accepted without inventing a submitted-result prerequisite.
    var linked = await Prepare(pg, scope.Preparer, request with { AsOfDate = new(2026, 1, 2), ProcedureId = primaryProcedure });
    Assert.True(linked.Succeeded, linked.Message);
    Assert.Equal(primaryProcedure, (await Read(pg, scope.Preparer, scope.EngagementId, linked.Value!.VerificationId)).ProcedureId);
    var original = await Read(pg, scope.Preparer, scope.EngagementId, id);
    Assert.Equal(ErrorCodes.ScopeDenied, (await Prepare(pg, world.Sibling.Preparer, request)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await Review(pg, world.Sibling.Preparer, new(scope.EngagementId, id, 1, null))).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await Review(pg, world.Reviewer, new(scope.EngagementId, sibling.Value!.VerificationId, 1, null))).ErrorCode);
    Assert.Equal(ErrorCodes.StaleRevision, (await Prepare(pg, scope.Preparer, request with { ExpectedRevision = 1, ExpectedVerificationId = sibling.Value.VerificationId })).ErrorCode);
    foreach (var invalid in new[] { request with { ProcedureId = Guid.NewGuid() }, request with { PriorPeriodId = Guid.NewGuid() } })
      Assert.Equal(ErrorCodes.ScopeDenied, (await Prepare(pg, scope.Preparer, invalid with { ExpectedRevision = 1, ExpectedVerificationId = id })).ErrorCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditPlanningCompletionService.GetOpeningBalanceVerificationAsync(db, scope.Preparer, scope.EngagementId, sibling.Value.VerificationId)).ErrorCode);
      await db.OpeningBalanceVerifications.SingleAsync(x => x.Id == id);
      Assert.Equal(ErrorCodes.StaleRevision, (await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(db, world.Reviewer, new(scope.EngagementId, id, 1, null))).ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await using var transaction = await db.Database.BeginTransactionAsync();
      Assert.Equal(ErrorCodes.StaleRevision, (await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, scope.Preparer,
        request with { ExpectedRevision = 1, ExpectedVerificationId = id })).ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var stagedDelete = await db.OpeningBalanceVerifications.SingleAsync(x => x.Id == id);
      db.OpeningBalanceVerifications.Remove(stagedDelete);
      Assert.Empty(db.OpeningBalanceVerifications.Local);
      Assert.Equal(ErrorCodes.StaleRevision, (await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, scope.Preparer,
        request with { AsOfDate = new(2026, 1, 3) })).ErrorCode);
      Assert.Equal(ErrorCodes.StaleRevision, (await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(db, world.Reviewer,
        new(scope.EngagementId, id, 1, null))).ErrorCode);
    }
    using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      Assert.Equal(ErrorCodes.StaleRevision, (await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, scope.Preparer,
        request with { ExpectedRevision = 1, ExpectedVerificationId = id })).ErrorCode);
      Assert.Equal(ErrorCodes.StaleRevision, (await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(db, world.Reviewer,
        new(scope.EngagementId, id, 1, null))).ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var grant = await db.RoleGrants.AsNoTracking().SingleAsync(x => x.UserId == world.Reviewer.UserId && x.EngagementId == scope.EngagementId && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, world.Administrator, new(grant.Id, Reason: "Synthetic R06 revocation."));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    Assert.False((await Review(pg, world.Reviewer, new(scope.EngagementId, id, 1, null))).Succeeded);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var epoch = await db.Users.AsNoTracking().Where(x => x.Id == world.Reviewer.UserId).Select(x => x.SessionEpoch).SingleAsync();
      Assert.Equal(ErrorCodes.ScopeDenied, (await Review(pg, world.Reviewer with { SessionEpoch = epoch }, new(scope.EngagementId, id, 1, null))).ErrorCode);
    }
    var retained = await Read(pg, scope.Preparer, scope.EngagementId, id);
    Assert.Equal(original.Revision, retained.Revision); Assert.Equal(original.OpeningSignedTotal, retained.OpeningSignedTotal);
    Assert.Equal(original.PriorReference, retained.PriorReference); Assert.Equal(original.EvidenceReferences, retained.EvidenceReferences);
    Assert.False(retained.CurrentIndependentReview); Assert.Null(retained.ReviewedByUserId);
  }
}
