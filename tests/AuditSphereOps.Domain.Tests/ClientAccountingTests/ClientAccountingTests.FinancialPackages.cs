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
  public async Task FinancialPackageReviews_AreStageBoundAndImmutable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var partner = Actor(scope.Partner, "Partner");
    Guid packageId, managementDecisionId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      packageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "review");
      await db.SaveChangesAsync();

      var management = await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "signed-management-approval", "Management approval received offline."));
      Assert.True(management.Succeeded, management.Message);
      managementDecisionId = management.Value;
      var managementRecord = await db.FinancialPackageReviewDecisions.SingleAsync(x => x.Id == management.Value);
      var renderedArtifact = await db.FinancialPackageArtifacts.SingleAsync(x => x.Id == managementRecord.FinancialPackageArtifactId);
      Assert.Equal(renderedArtifact.ArtifactSha256Hex, managementRecord.ArtifactSha256Hex);
      Assert.Equal(FinancialPackageArtifactVersions.Text, managementRecord.ArtifactVersion);

      var accounting = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "accounting-review-session", "Tie-outs and validations reviewed."));
      Assert.True(accounting.Succeeded, accounting.Message);

      var current = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, packageId, requirePartner: false);
      Assert.True(current.Succeeded, current.Message);
      var missingPartner = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, packageId, requirePartner: true);
      Assert.False(missingPartner.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, missingPartner.ErrorCode);

      var queue = await FinancialPackageReviewService.GetStaffQueueAsync(db, partner);
      Assert.True(queue.Succeeded, queue.Message);
      var queuedPackage = Assert.Single(queue.Value!);
      Assert.Equal(packageId, queuedPackage.PackageId);
      Assert.Equal(FinancialPackageReviewStages.PartnerApproval, queuedPackage.NextAction);

      var duplicate = await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "signed-management-approval", "Management approval received offline."));
      Assert.False(duplicate.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicate.ErrorCode);

      var partnerApproval = await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "partner-review-session", "Partner approval recorded."));
      Assert.True(partnerApproval.Succeeded, partnerApproval.Message);
      var complete = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, packageId, requirePartner: true);
      Assert.True(complete.Succeeded, complete.Message);
      var emptyQueue = await FinancialPackageReviewService.GetStaffQueueAsync(db, partner);
      Assert.True(emptyQueue.Succeeded, emptyQueue.Message);
      Assert.Empty(emptyQueue.Value!);

      var reviews = await FinancialPackageReviewService.GetAsync(db, partner, packageId);
      Assert.True(reviews.Succeeded, reviews.Message);
      Assert.Equal(3, reviews.Value!.Count);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var mutation = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE financial_package_review_decisions SET comment = {"tampered"} WHERE id = {managementDecisionId}"));
    Assert.Equal("55000", mutation.SqlState);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task FinancialPackageReviewQueue_FiltersScopeBeforeApplyingPageLimit()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var scopedReviewer = User(scope.FirmId, "scoped-queue-reviewer");
    var actor = Actor(scopedReviewer, "AccountingReviewer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(scopedReviewer);
      db.RoleGrants.Add(Grant(scope.FirmId, scopedReviewer, "AccountingReviewer", scope.ClientA));

      for (var i = 0; i < 101; i++)
      {
        var id = await AddPackageAsync(db, scope, scope.ClientB, scope.EngagementB, 100m, "CASH", $"queue-hidden-{i}");
        db.FinancialPackages.Local.Single(x => x.Id == id).CreatedAt = DateTimeOffset.UtcNow.AddSeconds(i + 2);
      }

      var visibleId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "queue-visible");
      db.FinancialPackages.Local.Single(x => x.Id == visibleId).CreatedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync();
    }

    await using var query = new AuditSphereDbContext(pg.Options);
    var queue = await FinancialPackageReviewService.GetStaffQueueAsync(query, actor);
    Assert.True(queue.Succeeded, queue.Message);
    var item = Assert.Single(queue.Value!);
    Assert.Equal(scope.ClientA, await query.FinancialPackages.Where(x => x.Id == item.PackageId).Select(x => x.ClientId).SingleAsync());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task FinancialPackageRelease_BindsCandidateToCurrentPackageReviews()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var partner = Actor(scope.Partner, "Partner");
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("financial-package-release-manifest");
    var manifest = Hashing.Sha256Hex(manifestBytes);
    Guid packageId, candidateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      packageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "release");
      await db.SaveChangesAsync();

      Assert.True((await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "management-package-approval", "Management approved the exact package."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "accounting-package-review", "Accounting review completed."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "partner-package-review", "Partner approval completed."))).Succeeded);

      var approval = await ApprovalService.CreateAsync(db, partner,
        new CreateApprovalRequest(ReleaseTargetKinds.FinancialPackage, packageId, 1, 1, 1, manifest));
      Assert.True(approval.Succeeded, approval.Message);

      var candidate = await ReleaseService.CreateCandidateAsync(db, partner,
        new CreateReleaseCandidateRequest(approval.Value, ReleaseTargetKinds.FinancialPackage,
          packageId, 1, 1, 1, manifest));
      Assert.True(candidate.Succeeded, candidate.Message);
      candidateId = candidate.Value;

      var checkpointStore = new LocalAppendOnlyCheckpointStore(
        Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N")));
      var checkpoint = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, checkpointStore, partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "financial-package-release-001", manifest, manifestBytes));
      Assert.True(checkpoint.Succeeded, checkpoint.Message);

      var issued = await ReleaseService.IssueAsync(db, partner,
        new IssueReleaseRequest(candidateId, 1, manifest, "financial-package-release-001"));
      Assert.True(issued.Succeeded, $"{issued.ErrorCode}: {issued.Message}");
      Assert.Equal(packageId, await db.Releases.Where(x => x.Id == issued.Value).Select(x => x.PackageId).SingleAsync());
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var candidateRow = await verify.ReleaseCandidates.SingleAsync(x => x.Id == candidateId);
    Assert.Equal(ReleaseTargetKinds.FinancialPackage, candidateRow.TargetKind);
    Assert.Equal(ReleaseStates.Issued, candidateRow.Status);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ClientPackageView_IsScopedAndSupportsSignedInManagementDecision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var clientUser = User(scope.FirmId, "ClientApprover");
    clientUser.UserKind = "Client";
    Guid packageA, packageB;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      packageA = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "client-view-a");
      packageB = await AddPackageAsync(db, scope, scope.ClientB, scope.EngagementB, 200m, "CASH", "client-view-b");
      db.Users.Add(clientUser);
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, UserId = clientUser.Id,
        Role = "ClientUser", ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Preparer.Id
      });
      await db.SaveChangesAsync();
    }

    var actor = Actor(clientUser, "ClientUser");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var view = await FinancialPackageReviewService.GetClientViewAsync(db, actor, packageA);
      Assert.True(view.Succeeded, view.Message);
      Assert.Equal(packageA, view.Value!.PackageId);
      Assert.Equal("PENDING", view.Value.ManagementDecision);
      Assert.Contains(view.Value.StatementTotals, x => x.StatementSection == "STATEMENT" && x.Amount == 100m);

      var decision = await FinancialPackageReviewService.RecordAsync(db, actor,
        new FinancialPackageReviewRequest(packageA, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "client-portal-ack-1", "Management reviewed the supplied package."));
      Assert.True(decision.Succeeded, decision.Message);

      var approved = await FinancialPackageReviewService.GetClientViewAsync(db, actor, packageA);
      Assert.True(approved.Succeeded, approved.Message);
      Assert.Equal(FinancialPackageReviewDecisions.Approved, approved.Value!.ManagementDecision);

      var internalReviews = await FinancialPackageReviewService.GetAsync(db, actor, packageA);
      Assert.False(internalReviews.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, internalReviews.ErrorCode);
      Assert.Empty(await db.ReleaseCandidates.Where(x => x.TargetId == packageA).ToListAsync());
      Assert.Empty(await db.Releases.Where(x => x.PackageId == packageA).ToListAsync());

      var otherClient = await FinancialPackageReviewService.GetClientViewAsync(db, actor, packageB);
      Assert.False(otherClient.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, otherClient.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == clientUser.Id &&
        x.Role == "ClientUser" && x.RevokedAt == null);
      grant.RevokedAt = DateTimeOffset.UtcNow;
      var currentUser = await db.Users.SingleAsync(x => x.Id == clientUser.Id);
      currentUser.SessionEpoch++;
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staleDecision = await FinancialPackageReviewService.RecordAsync(db, actor,
        new FinancialPackageReviewRequest(packageA, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.ChangesRequired, FinancialPackageReviewEvidenceModes.SignedIn,
          "stale-client-session", "Must not persist after grant revocation."));
      Assert.False(staleDecision.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleDecision.ErrorCode);
      Assert.Single(await db.FinancialPackageReviewDecisions.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageA).ToListAsync());
    }
  }
}
