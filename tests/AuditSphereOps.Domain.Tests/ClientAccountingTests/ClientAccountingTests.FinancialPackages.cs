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
using AuditSphereOps.Domain.Reviews;
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
  public async Task FinancialPackageReviews_AreStageBoundAndImmutable_AndReleaseRequiresSelectedComposition()
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
      Assert.False(complete.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, complete.ErrorCode);
      Assert.Contains("three-format composition", complete.Message!, StringComparison.Ordinal);
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
    var source = await StatementCandidatePromotionTests.SeedPresentableAsync(pg);
    var partner = Actor(source.Partner, "Partner");
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("financial-package-release-manifest");
    var manifest = Hashing.Sha256Hex(manifestBytes);
    await using var db = new AuditSphereDbContext(pg.Options);
    var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == source.PackageId);
    var generation = await db.ClientSafetyStates.Where(x => x.FirmId == source.FirmId && x.Id == source.ClientId)
      .Select(x => x.InputGeneration).SingleAsync();
    var policy = await db.FirmSafetyStates.Where(x => x.Id == source.FirmId).Select(x => x.PolicyGeneration).SingleAsync();
    var unreviewed = await ApprovalService.CreateAsync(db, partner,
      new CreateApprovalRequest(ReleaseTargetKinds.FinancialPackage, source.PackageId, package.Revision, generation, policy, manifest));
    Assert.False(unreviewed.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unreviewed.ErrorCode);

    var composition = await SelectReviewedCompositionAsync(db, source);
    var approval = await ApprovalService.CreateAsync(db, partner,
      new CreateApprovalRequest(ReleaseTargetKinds.FinancialPackage, source.PackageId, package.Revision, generation, policy, manifest));
    Assert.True(approval.Succeeded, approval.Message);

    // A client-wide generation can advance for another reporting period without changing this
    // package. Keep the global generation as the command's concurrency token, while approval
    // applicability remains bound to the immutable package generation.
    await db.ClientSafetyStates.Where(x => x.FirmId == source.FirmId && x.Id == source.ClientId)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    generation++;
    var stillCurrent = await ApprovalService.EvaluateAsync(db, partner, approval.Value);
    Assert.True(stillCurrent.Succeeded, stillCurrent.Message);
    Assert.Equal(ApprovalStates.Current, stillCurrent.Value!.Status);

    var candidate = await ReleaseService.CreateCandidateAsync(db, partner,
      new CreateReleaseCandidateRequest(approval.Value, ReleaseTargetKinds.FinancialPackage,
        source.PackageId, package.Revision, generation, policy, manifest));
    Assert.True(candidate.Succeeded, candidate.Message);
    var missingCheckpoint = await ReleaseService.IssueAsync(db, partner,
      new IssueReleaseRequest(candidate.Value, 1, manifest, "financial-package-release-001"));
    Assert.False(missingCheckpoint.Succeeded);

    var root = Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N"));
    try
    {
      var checkpoint = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db,
        new LocalAppendOnlyCheckpointStore(root), partner,
        new RecordReleaseCheckpointRequest(candidate.Value, 1, "financial-package-release-001", manifest, manifestBytes));
      Assert.True(checkpoint.Succeeded, checkpoint.Message);
      await db.ClientSafetyStates.Where(x => x.FirmId == source.FirmId && x.Id == source.ClientId)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
      var issued = await ReleaseService.IssueAsync(db, partner,
        new IssueReleaseRequest(candidate.Value, 1, manifest, "financial-package-release-001"));
      Assert.True(issued.Succeeded, issued.Message);
      var pinned = await db.Releases.AsNoTracking().SingleAsync(x => x.Id == issued.Value);
      Assert.Equal(source.PackageId, pinned.PackageId);
      Assert.Equal(composition.Id, pinned.PinnedCompositionId);
      Assert.Equal(composition.ManifestDigest, pinned.PinnedCompositionManifestDigest);
      Assert.Equal(composition.PdfArtifactId, pinned.PinnedArtifactId);
      Assert.Equal(composition.PdfSha256Hex, pinned.PinnedArtifactSha256Hex);
      Assert.Equal(ReleaseStates.Issued,
        await db.ReleaseCandidates.Where(x => x.Id == candidate.Value).Select(x => x.Status).SingleAsync());
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
  }

  private static async Task<FinancialStatementComposition> SelectReviewedCompositionAsync(
    AuditSphereDbContext db, StatementCandidatePromotionTests.Seeded source)
  {
    var preparer = Actor(source.Preparer, "AccountingPreparer");
    var reviewer = Actor(source.Reviewer, "AccountingReviewer");
    var partner = Actor(source.Partner, "Partner");
    var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, source.PackageId, source.LayoutId);
    Assert.True(pin.Succeeded, pin.Message);
    var rendered = await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer,
      pin.Value!.RevisionId, FinancialStatementPresentationRenderer.PdfFormat);
    Assert.True(rendered.Succeeded, rendered.Message);
    var content = await StatementContentReviewCall.RecordAsync(db, reviewer, source.PackageId, pin.Value.RevisionId);
    Assert.True(content.Succeeded, content.Message);
    var candidate = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer,
      source.PackageId, pin.Value.RevisionId);
    Assert.True(candidate.Succeeded, candidate.Message);
    var selected = await FinancialStatementService.SelectCompositionAsync(db, preparer,
      source.PackageId, candidate.Value!.RevisionId);
    Assert.True(selected.Succeeded, selected.Message);
    foreach (var (actor, stage, mode) in new[]
    {
      (preparer, FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewEvidenceModes.Offline),
      (reviewer, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewEvidenceModes.SignedIn),
      (partner, FinancialPackageReviewStages.PartnerApproval, FinancialPackageReviewEvidenceModes.SignedIn)
    })
    {
      var decision = await FinancialPackageReviewService.RecordAsync(db, actor,
        new(source.PackageId, stage, FinancialPackageReviewDecisions.Approved, mode,
          "composition-review-" + stage, "Reviewed exact test composition.", candidate.Value.RevisionId, null,
          selected.Value!.Id, selected.Value.ManifestDigest));
      Assert.True(decision.Succeeded, decision.Message);
    }
    return selected.Value!;
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
    var decisionRequestId = Guid.CreateVersion7();
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
          "client-portal-ack-1", "Management reviewed the supplied package.", RequestId: decisionRequestId,
          ExpectedLatestDecisionId: null));
      Assert.True(decision.Succeeded, decision.Message);

      var receipt = await FinancialPackageReviewService.GetClientDecisionReceiptAsync(db, actor, packageA, decisionRequestId);
      Assert.True(receipt.Succeeded, receipt.Message);
      Assert.True(receipt.Value!.Found);
      Assert.Equal(decision.Value, receipt.Value.DecisionId);
      Assert.Equal(FinancialPackageReviewDecisions.Approved, receipt.Value.Decision);
      Assert.Equal((await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageA)).CalculationHash, receipt.Value.PackageHash);
      Assert.NotNull(receipt.Value.DecidedAt);
      var absent = await FinancialPackageReviewService.GetClientDecisionReceiptAsync(db, actor, packageA, Guid.CreateVersion7());
      Assert.True(absent.Succeeded, absent.Message);
      Assert.False(absent.Value!.Found);

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
      var foreignReceipt = await FinancialPackageReviewService.GetClientDecisionReceiptAsync(db, actor, packageB, decisionRequestId);
      Assert.False(foreignReceipt.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, foreignReceipt.ErrorCode);
      var staffLookup = await FinancialPackageReviewService.GetClientDecisionReceiptAsync(db, Actor(scope.Partner, "Partner"), packageA, decisionRequestId);
      Assert.False(staffLookup.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, staffLookup.ErrorCode);
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
      var staleReceipt = await FinancialPackageReviewService.GetClientDecisionReceiptAsync(db, actor, packageA, decisionRequestId);
      Assert.False(staleReceipt.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleReceipt.ErrorCode);
      Assert.Single(await db.FinancialPackageReviewDecisions.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageA).ToListAsync());
    }
  }
}
