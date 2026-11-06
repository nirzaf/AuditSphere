using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task FinancialPackageBuildInputs_EnumeratesComparativesOnlyWithIndependentPriorEngagementAccess()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var scopedPreparer = User(scope.FirmId, "comparative-preparer");
    var priorEngagementId = Guid.CreateVersion7();
    Guid priorPackageId, currentPackageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(scopedPreparer);
      db.RoleGrants.Add(Grant(scope.FirmId, scopedPreparer, "AccountingPreparer", scope.ClientA, scope.EngagementA));
      db.Engagements.Add(new Engagement
      {
        Id = priorEngagementId, FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      priorPackageId = await AddPackageAsync(db, scope, scope.ClientA, priorEngagementId, 100m, "CASH", "prior-engagement");
      var prior = db.FinancialPackages.Local.Single(x => x.Id == priorPackageId);
      prior.PeriodStart = "2025-01-01";
      prior.PeriodEnd = "2025-12-31";
      currentPackageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 120m, "CASH", "current-engagement");
      db.FinancialPackages.Local.Single(x => x.Id == currentPackageId).ComparativePackageId = priorPackageId;
      await db.SaveChangesAsync();
    }

    var actor = Actor(scopedPreparer, "AccountingPreparer");
    await using (var deniedDb = new AuditSphereDbContext(pg.Options))
    {
      var denied = await FinancialPackageBuildInputsQuery.GetAsync(deniedDb, actor, scope.EngagementA);
      Assert.True(denied.Succeeded, denied.Message);
      Assert.DoesNotContain(denied.Value!.Comparatives, x => x.Id == priorPackageId);
    }

    await using (var grantDb = new AuditSphereDbContext(pg.Options))
    {
      grantDb.RoleGrants.Add(Grant(scope.FirmId, scopedPreparer, "Staff", scope.ClientA, priorEngagementId));
      await grantDb.SaveChangesAsync();
    }
    await using var authorizedDb = new AuditSphereDbContext(pg.Options);
    var authorized = await FinancialPackageBuildInputsQuery.GetAsync(authorizedDb, actor, scope.EngagementA);
    Assert.True(authorized.Succeeded, authorized.Message);
    var option = Assert.Single(authorized.Value!.Comparatives, x => x.Id == priorPackageId);
    Assert.Equal(priorEngagementId, option.EngagementId);

    await using (var packageDb = new AuditSphereDbContext(pg.Options))
    {
      var package = await packageDb.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == currentPackageId);
      Assert.True((await FinancialStatementService.AuthorizeComparativeAccessAsync(packageDb, actor, package, default)).Succeeded);
    }

    await using (var revokeDb = new AuditSphereDbContext(pg.Options))
    {
      var priorGrant = await revokeDb.RoleGrants.SingleAsync(x => x.FirmId == scope.FirmId &&
        x.UserId == scopedPreparer.Id && x.Role == "Staff" && x.EngagementId == priorEngagementId);
      priorGrant.RevokedAt = DateTimeOffset.UtcNow;
      await revokeDb.SaveChangesAsync();
    }
    await using (var revokedDb = new AuditSphereDbContext(pg.Options))
    {
      var package = await revokedDb.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == currentPackageId);
      var revoked = await FinancialStatementService.AuthorizeComparativeAccessAsync(revokedDb, actor, package, default);
      Assert.False(revoked.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, revoked.ErrorCode);
    }
  }

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
    var candidateBinding = await db.ReleaseCandidates.AsNoTracking().SingleAsync(x => x.Id == candidate.Value);
    Assert.Equal(composition.PresentationRevisionId, candidateBinding.PresentationRevisionId);
    Assert.Equal(composition.Id, candidateBinding.CompositionId);
    Assert.Equal(composition.ManifestDigest, candidateBinding.CompositionManifestDigest);
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

      // Candidate A and its checkpoint remain tied to the composition that was approved at preparation.
      // A later approved composition B must not let A issue or be replayed as if it were prepared for B.
      var alternateLayoutId = await CompletionBundleFixture.CreateAlternateStatementLayoutAsync(db,
        Actor(source.Preparer, "AccountingPreparer"), Actor(source.Reviewer, "AccountingReviewer"),
        source.ClientId, source.LayoutId);
      var replacementComposition = await SelectReviewedCompositionAsync(db, source, "-replacement", alternateLayoutId);
      Assert.NotEqual(composition.Id, replacementComposition.Id);
      Assert.NotEqual(composition.ManifestDigest, replacementComposition.ManifestDigest);

      var staleIssue = await ReleaseService.IssueAsync(db, partner,
        new IssueReleaseRequest(candidate.Value, 1, manifest, "financial-package-release-001"));
      Assert.False(staleIssue.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleIssue.ErrorCode);
      Assert.Equal(ReleaseStates.Ready,
        await db.ReleaseCandidates.AsNoTracking().Where(x => x.Id == candidate.Value).Select(x => x.Status).SingleAsync());
      Assert.Empty(await db.Releases.AsNoTracking().Where(x => x.ReleaseCandidateId == candidate.Value).ToListAsync());
      Assert.Empty(await db.DurableOperations.AsNoTracking().Where(x => x.OperationKind == "ReleaseDelivery.v1").ToListAsync());
      var retainedCheckpointA = await db.ReleaseCheckpoints.AsNoTracking()
        .SingleAsync(x => x.ReleaseCandidateId == candidate.Value);
      Assert.Equal("financial-package-release-001", retainedCheckpointA.AuthorizedReleaseKey);
      Assert.Equal(manifest, retainedCheckpointA.ReadBackDigest);

      var stalePreparation = await ReleaseService.CreateCandidateAsync(db, partner,
        new CreateReleaseCandidateRequest(approval.Value, ReleaseTargetKinds.FinancialPackage,
          source.PackageId, package.Revision, generation, policy, manifest));
      Assert.False(stalePreparation.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, stalePreparation.ErrorCode);

      var manifestBytesB = System.Text.Encoding.UTF8.GetBytes("financial-package-release-manifest-B");
      var manifestB = Hashing.Sha256Hex(manifestBytesB);
      var approvalB = await ApprovalService.CreateAsync(db, partner,
        new CreateApprovalRequest(ReleaseTargetKinds.FinancialPackage, source.PackageId, package.Revision,
          generation, policy, manifestB));
      Assert.True(approvalB.Succeeded, approvalB.Message);
      var candidateB = await ReleaseService.CreateCandidateAsync(db, partner,
        new CreateReleaseCandidateRequest(approvalB.Value, ReleaseTargetKinds.FinancialPackage,
          source.PackageId, package.Revision, generation, policy, manifestB));
      Assert.True(candidateB.Succeeded, candidateB.Message);
      var candidateBindingB = await db.ReleaseCandidates.AsNoTracking().SingleAsync(x => x.Id == candidateB.Value);
      Assert.Equal(replacementComposition.PresentationRevisionId, candidateBindingB.PresentationRevisionId);
      Assert.Equal(replacementComposition.Id, candidateBindingB.CompositionId);
      Assert.Equal(replacementComposition.ManifestDigest, candidateBindingB.CompositionManifestDigest);
      var checkpointB = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db,
        new LocalAppendOnlyCheckpointStore(root), partner,
        new RecordReleaseCheckpointRequest(candidateB.Value, 1, "financial-package-release-002", manifestB, manifestBytesB));
      Assert.True(checkpointB.Succeeded, checkpointB.Message);
      var issuedB = await ReleaseService.IssueAsync(db, partner,
        new IssueReleaseRequest(candidateB.Value, 1, manifestB, "financial-package-release-002"));
      Assert.True(issuedB.Succeeded, issuedB.Message);

      var pinned = await db.Releases.AsNoTracking().SingleAsync(x => x.Id == issuedB.Value);
      Assert.Equal(source.PackageId, pinned.PackageId);
      Assert.Equal(replacementComposition.Id, pinned.PinnedCompositionId);
      Assert.Equal(replacementComposition.ManifestDigest, pinned.PinnedCompositionManifestDigest);
      Assert.Equal(replacementComposition.PdfArtifactId, pinned.PinnedArtifactId);
      Assert.Equal(replacementComposition.PdfSha256Hex, pinned.PinnedArtifactSha256Hex);
      Assert.Equal(ReleaseStates.Ready,
        await db.ReleaseCandidates.AsNoTracking().Where(x => x.Id == candidate.Value).Select(x => x.Status).SingleAsync());
      Assert.Equal(ReleaseStates.Issued,
        await db.ReleaseCandidates.Where(x => x.Id == candidateB.Value).Select(x => x.Status).SingleAsync());
      Assert.Single(await db.Releases.AsNoTracking().ToListAsync());
      Assert.Single(await db.DurableOperations.AsNoTracking().Where(x => x.OperationKind == "ReleaseDelivery.v1").ToListAsync());
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
  }


  [Fact]
  [Trait("Profile", "Database")]
  public async Task FinancialPackageRelease_ConcurrentPartnerReviewAndIssueNeverMixCompositions()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var source = await StatementCandidatePromotionTests.SeedPresentableAsync(pg);
    var partner = Actor(source.Partner, "Partner");
    FinancialStatementComposition compositionA;
    await using (var compositionDb = new AuditSphereDbContext(pg.Options))
      compositionA = await SelectReviewedCompositionAsync(compositionDb, source, "-concurrent-A");
    var manifestBytesA = System.Text.Encoding.UTF8.GetBytes("financial-package-concurrent-release-A");
    var manifestA = Hashing.Sha256Hex(manifestBytesA);
    var root = Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N"));
    try
    {
      Guid candidateA;
      FinancialStatementComposition compositionB;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == source.PackageId);
        var generation = await db.ClientSafetyStates.AsNoTracking()
          .Where(x => x.FirmId == source.FirmId && x.Id == source.ClientId)
          .Select(x => x.InputGeneration).SingleAsync();
        var policy = await db.FirmSafetyStates.AsNoTracking()
          .Where(x => x.Id == source.FirmId).Select(x => x.PolicyGeneration).SingleAsync();
        var approvalA = await ApprovalService.CreateAsync(db, partner,
          new CreateApprovalRequest(ReleaseTargetKinds.FinancialPackage, source.PackageId, package.Revision,
            generation, policy, manifestA));
        Assert.True(approvalA.Succeeded, approvalA.Message);
        var preparedA = await ReleaseService.CreateCandidateAsync(db, partner,
          new CreateReleaseCandidateRequest(approvalA.Value, ReleaseTargetKinds.FinancialPackage,
            source.PackageId, package.Revision, generation, policy, manifestA));
        Assert.True(preparedA.Succeeded, preparedA.Message);
        candidateA = preparedA.Value;
        var checkpointA = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db,
          new LocalAppendOnlyCheckpointStore(root), partner,
          new RecordReleaseCheckpointRequest(candidateA, 1, "financial-package-concurrent-A", manifestA, manifestBytesA));
        Assert.True(checkpointA.Succeeded, checkpointA.Message);

        var alternateLayoutId = await CompletionBundleFixture.CreateAlternateStatementLayoutAsync(db,
          Actor(source.Preparer, "AccountingPreparer"), Actor(source.Reviewer, "AccountingReviewer"),
          source.ClientId, source.LayoutId);
        var preparedB = await PrepareCompositionCandidateAsync(db, source, alternateLayoutId);
        compositionB = preparedB.Composition;
        Assert.NotEqual(compositionA.Id, compositionB.Id);
        Assert.NotEqual(compositionA.ManifestDigest, compositionB.ManifestDigest);
        foreach (var (actor, stage, mode) in new[]
        {
          (Actor(source.Preparer, "AccountingPreparer"), FinancialPackageReviewStages.ManagementApproval,
            FinancialPackageReviewEvidenceModes.Offline),
          (Actor(source.Reviewer, "AccountingReviewer"), FinancialPackageReviewStages.AccountingReview,
            FinancialPackageReviewEvidenceModes.SignedIn)
        })
        {
          var decision = await FinancialPackageReviewService.RecordAsync(db, actor,
            new(source.PackageId, stage, FinancialPackageReviewDecisions.Approved, mode,
              "financial-package-concurrent-" + stage, "Reviewed the exact concurrent test composition.",
              preparedB.PresentationRevisionId, null, preparedB.Composition.Id, preparedB.Composition.ManifestDigest));
          Assert.True(decision.Succeeded, decision.Message);
        }
      }

      var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      async Task<CommandResult<Guid>> IssueAAsync()
      {
        await using var issueDb = new AuditSphereDbContext(pg.Options);
        await start.Task;
        return await ReleaseService.IssueAsync(issueDb, partner,
          new IssueReleaseRequest(candidateA, 1, manifestA, "financial-package-concurrent-A"));
      }
      async Task<CommandResult<Guid>> ApprovePartnerBAsync()
      {
        await using var reviewDb = new AuditSphereDbContext(pg.Options);
        await start.Task;
        return await FinancialPackageReviewService.RecordAsync(reviewDb, partner,
          new(source.PackageId, FinancialPackageReviewStages.PartnerApproval,
            FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
            "financial-package-concurrent-partner-B", "Approved the exact concurrent test composition.",
            compositionB.PresentationRevisionId, null, compositionB.Id, compositionB.ManifestDigest));
      }

      var issueTask = IssueAAsync();
      var reviewTask = ApprovePartnerBAsync();
      start.SetResult();
      await Task.WhenAll(issueTask, reviewTask);
      var issue = await issueTask;
      var partnerReview = await reviewTask;
      Assert.True(partnerReview.Succeeded, partnerReview.Message);

      await using var verify = new AuditSphereDbContext(pg.Options);
      var releases = await verify.Releases.AsNoTracking().ToListAsync();
      var deliveryIntents = await verify.DurableOperations.AsNoTracking()
        .Where(x => x.OperationKind == "ReleaseDelivery.v1").ToListAsync();
      if (issue.Succeeded)
      {
        var releaseA = Assert.Single(releases);
        Assert.Equal(candidateA, releaseA.ReleaseCandidateId);
        Assert.Equal(compositionA.Id, releaseA.PinnedCompositionId);
        Assert.Equal(compositionA.ManifestDigest, releaseA.PinnedCompositionManifestDigest);
        Assert.Equal(ReleaseStates.Issued,
          await verify.ReleaseCandidates.Where(x => x.Id == candidateA).Select(x => x.Status).SingleAsync());
        Assert.Single(deliveryIntents);
      }
      else
      {
        Assert.True(issue.ErrorCode is ErrorCodes.GenerationStale or ErrorCodes.GateBlocked,
          $"Concurrent issue returned unexpected {issue.ErrorCode}: {issue.Message}");
        Assert.Empty(releases);
        Assert.Empty(deliveryIntents);
        Assert.Equal(ReleaseStates.Ready,
          await verify.ReleaseCandidates.Where(x => x.Id == candidateA).Select(x => x.Status).SingleAsync());
      }

      Assert.DoesNotContain(releases,
        x => x.PinnedCompositionId == compositionB.Id && x.PinnedCompositionManifestDigest == compositionB.ManifestDigest);
      var retainedPartnerDecision = await verify.FinancialPackageReviewDecisions.AsNoTracking()
        .SingleAsync(x => x.Id == partnerReview.Value);
      Assert.Equal(compositionB.Id, retainedPartnerDecision.PresentationCompositionId);
      Assert.Equal(compositionB.ManifestDigest, retainedPartnerDecision.CompositionManifestDigest);
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
  }



  private static async Task<FinancialStatementComposition> SelectReviewedCompositionAsync(
    AuditSphereDbContext db, StatementCandidatePromotionTests.Seeded source, string suffix = "",
    Guid? layoutVersionId = null)
  {
    var prepared = await PrepareCompositionCandidateAsync(db, source, layoutVersionId);
    var preparer = Actor(source.Preparer, "AccountingPreparer");
    var reviewer = Actor(source.Reviewer, "AccountingReviewer");
    var partner = Actor(source.Partner, "Partner");
    foreach (var (actor, stage, mode) in new[]
    {
      (preparer, FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewEvidenceModes.Offline),
      (reviewer, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewEvidenceModes.SignedIn),
      (partner, FinancialPackageReviewStages.PartnerApproval, FinancialPackageReviewEvidenceModes.SignedIn)
    })
    {
      var decision = await FinancialPackageReviewService.RecordAsync(db, actor,
        new(source.PackageId, stage, FinancialPackageReviewDecisions.Approved, mode,
          "composition-review-" + stage + suffix, "Reviewed exact test composition.", prepared.PresentationRevisionId, null,
          prepared.Composition.Id, prepared.Composition.ManifestDigest));
      Assert.True(decision.Succeeded, decision.Message);
    }
    return prepared.Composition;
  }

  private static async Task<(Guid PresentationRevisionId, FinancialStatementComposition Composition)>
    PrepareCompositionCandidateAsync(AuditSphereDbContext db, StatementCandidatePromotionTests.Seeded source,
      Guid? layoutVersionId = null)
  {
    var preparer = Actor(source.Preparer, "AccountingPreparer");
    var reviewer = Actor(source.Reviewer, "AccountingReviewer");
    var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, source.PackageId,
      layoutVersionId ?? source.LayoutId);
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
    return (candidate.Value.RevisionId, selected.Value!);
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
