using System.Data.Common;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// F-03 and F-04. A signing candidate is created only when every presentation check passes, the professional content checks through an
/// independent review of the exact draft. Formal decisions follow the latest effective predecessor, recover their original request and
/// refuse stale input; release requires the whole management, accounting and Partner chain to be current on one composition.
/// </summary>
[Trait("Profile", "Database")]
public sealed class StatementFinalReadinessAndDecisionChainTests
{
  private sealed class PauseAfterContentReviewRead : DbCommandInterceptor
  {
    private int paused;
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("financial_statement_content_reviews", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref paused, 1, 0) == 0)
      {
        Reached.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CompletionUnit", "WU-011")]
  public async Task ContentReviewRead_RechecksReadAuthorityAfterReviewFetch(bool retainReadGrant)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seeded = await StatementCandidatePromotionTests.SeedPresentableAsync(pg);
    var preparer = StatementCandidatePromotionTests.Actor(seeded.Preparer, "AccountingPreparer");
    var reviewer = StatementCandidatePromotionTests.Actor(seeded.Reviewer, "AccountingReviewer");
    Guid draftId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var pin = await FinancialStatementService.PinPresentationAsync(setup, preparer, seeded.PackageId, seeded.LayoutId);
      Assert.True(pin.Succeeded, pin.Message);
      draftId = pin.Value!.RevisionId;
      Assert.True((await StatementContentReviewCall.RecordAsync(setup, reviewer, seeded.PackageId, draftId,
        "RETURNED", "Synthetic review retained for the authority read.")).Succeeded);
      if (retainReadGrant)
      {
        setup.RoleGrants.Add(new RoleGrant
        {
          Id = Guid.CreateVersion7(), FirmId = seeded.FirmId, UserId = seeded.Reviewer.Id, Role = "AccountingPreparer",
          GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = seeded.Partner.Id
        });
        await setup.SaveChangesAsync();
      }
    }

    var pause = new PauseAfterContentReviewRead();
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options;
    await using var delayed = new AuditSphereDbContext(options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var pending = FinancialStatementService.GetStatementContentReviewAsync(delayed, reviewer, seeded.PackageId, draftId, timeout.Token);
    try
    {
      await pause.Reached.Task.WaitAsync(timeout.Token);
      await using (var writer = new AuditSphereDbContext(pg.Options))
      {
        var revoked = await writer.RoleGrants.Where(x => x.FirmId == seeded.FirmId &&
          x.UserId == seeded.Reviewer.Id && x.Role == "AccountingReviewer" && x.RevokedAt == null)
          .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token);
        Assert.Equal(1, revoked);
      }
      pause.Release.TrySetResult(true);
      var result = await pending;
      Assert.Equal(retainReadGrant, result.Succeeded);
      if (retainReadGrant)
      {
        Assert.False(result.Value!.CanReview);
        Assert.Single(result.Value.Reviews);
      }
      else
      {
        Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
        Assert.Null(result.Value);
      }
    }
    finally
    {
      pause.Release.TrySetResult(true);
      await pending.WaitAsync(timeout.Token);
    }
  }

  [Fact]
  [Trait("CompletionUnit", "WU-011")]
  public async Task ContentReviewRead_PreservesHistoricalApprovalButDemotesReturnedSource()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var s = await StatementCandidatePromotionTests.SeedPresentableAsync(pg);
    var preparer = StatementCandidatePromotionTests.Actor(s.Preparer, "AccountingPreparer");
    var reviewer = StatementCandidatePromotionTests.Actor(s.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, s.PackageId, s.LayoutId);
    Assert.True(pin.Succeeded, pin.Message);
    var draftId = pin.Value!.RevisionId;
    var review = await StatementContentReviewCall.RecordAsync(db, reviewer, s.PackageId, draftId);
    Assert.True(review.Succeeded, review.Message);
    var before = await FinancialStatementService.GetStatementContentReviewAsync(db, reviewer, s.PackageId, draftId);
    Assert.True(before.Succeeded, before.Message);
    Assert.True(before.Value!.Approved);
    Assert.True(before.Value.SourceIsCurrent);
    Assert.True(before.Value.CanReview);

    // Use the real review workflow: returning the linked source invalidates currentness,
    // without rewriting the independent historical content decision or draft bytes.
    var returned = await CashFlowSourceReviewCall.RecordAsync(db, reviewer, s.PackageId, "RETURNED",
      "Synthetic reviewer requires correction of the retained source allocation.");
    Assert.True(returned.Succeeded, returned.Message);
    var after = await FinancialStatementService.GetStatementContentReviewAsync(db, reviewer, s.PackageId, draftId);
    Assert.True(after.Succeeded, after.Message);
    Assert.False(after.Value!.SourceIsCurrent);
    Assert.False(after.Value.CanReview);
    Assert.True(after.Value.Approved);
    Assert.Equal(review.Value, after.Value.LatestReviewId);
    Assert.Equal(before.Value.Reviews, after.Value.Reviews);
    Assert.Equal(before.Value.SnapshotSha256Hex, after.Value.SnapshotSha256Hex);
    var refused = await StatementContentReviewCall.RecordAsync(db, reviewer, s.PackageId, draftId);
    Assert.Equal(ErrorCodes.StaleRevision, refused.ErrorCode);
    Assert.Equal(1, await db.FinancialStatementContentReviews.CountAsync(x => x.PresentationRevisionId == draftId));
    Assert.Equal(pin.Value.SnapshotSha256Hex,
      await db.FinancialStatementPresentationRevisions.Where(x => x.Id == draftId).Select(x => x.SnapshotSha256Hex).SingleAsync());
  }

  [Fact]
  public async Task CandidatePromotion_RequiresAnIndependentContentReviewOfTheExactDraft()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var s = await StatementCandidatePromotionTests.SeedPresentableAsync(pg);
    var preparer = StatementCandidatePromotionTests.Actor(s.Preparer, "AccountingPreparer");
    var reviewer = StatementCandidatePromotionTests.Actor(s.Reviewer, "AccountingReviewer");

    await using var db = new AuditSphereDbContext(pg.Options);
    var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, s.PackageId, s.LayoutId);
    Assert.True(pin.Succeeded, pin.Message);
    var draftId = pin.Value!.RevisionId;
    Assert.True((await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, draftId,
      FinancialStatementPresentationRenderer.PdfFormat)).Succeeded);

    // All eleven canonical package validations pass, yet the unreviewed note and equity content keeps the draft out of candidacy.
    var unreviewed = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, draftId);
    Assert.False(unreviewed.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unreviewed.ErrorCode);
    Assert.Contains(FinancialStatementService.NoteContentCheck, unreviewed.Message);

    // The preparer who pinned the draft cannot review its content.
    var self = await StatementContentReviewCall.RecordAsync(db, preparer, s.PackageId, draftId);
    Assert.False(self.Succeeded);

    // A return keeps the draft out of candidacy; a return without a reason is refused.
    Assert.False((await StatementContentReviewCall.RecordAsync(db, reviewer, s.PackageId, draftId, "RETURNED")).Succeeded);
    var returned = await StatementContentReviewCall.RecordAsync(db, reviewer, s.PackageId, draftId, "RETURNED", "The lease note is missing.");
    Assert.True(returned.Succeeded, returned.Message);
    Assert.False((await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, draftId)).Succeeded);

    // A decision against a stale chain is refused; the same intent replays its stored row; a changed payload conflicts.
    var state = (await FinancialStatementService.GetStatementContentReviewAsync(db, reviewer, s.PackageId, draftId)).Value!;
    Assert.Equal(returned.Value, state.LatestReviewId);
    var stale = await FinancialStatementService.RecordStatementContentReviewAsync(db, reviewer, s.PackageId, draftId, "APPROVED", null,
      Guid.NewGuid(), state.SnapshotSha256Hex, expectedPreviousReviewId: null);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    var requestId = Guid.NewGuid();
    var approved = await FinancialStatementService.RecordStatementContentReviewAsync(db, reviewer, s.PackageId, draftId, "APPROVED", null,
      requestId, state.SnapshotSha256Hex, state.LatestReviewId);
    Assert.True(approved.Succeeded, approved.Message);
    var replay = await FinancialStatementService.RecordStatementContentReviewAsync(db, reviewer, s.PackageId, draftId, "APPROVED", null,
      requestId, state.SnapshotSha256Hex, state.LatestReviewId);
    Assert.Equal(approved.Value, replay.Value);
    var conflict = await FinancialStatementService.RecordStatementContentReviewAsync(db, reviewer, s.PackageId, draftId, "APPROVED",
      "Changed payload", requestId, state.SnapshotSha256Hex, state.LatestReviewId);
    Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
    Assert.Equal(2, await db.FinancialStatementContentReviews.CountAsync(x => x.PresentationRevisionId == draftId));

    var promoted = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, draftId);
    Assert.True(promoted.Succeeded, promoted.Message);
    var candidate = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleAsync(x => x.Id == promoted.Value!.RevisionId);
    var snapshot = JsonSerializer.Deserialize<FinancialStatementPresentation>(candidate.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    Assert.All(snapshot.Checks, check => Assert.True(check.Passed, check.Code));
    Assert.Contains(snapshot.Checks, x => x.Code == FinancialStatementService.NoteContentCheck);
    // Equity evidence is labelled as reviewed manual evidence, never as ledger-derived lineage.
    Assert.Contains(snapshot.Checks, x => x.Code == FinancialStatementService.EquityEvidenceReviewCheck);
    Assert.DoesNotContain(snapshot.Checks, x => x.Code == FinancialStatementService.EquitySourceLineageCheck);
    Assert.DoesNotContain(snapshot.Warnings, x => x.Code is "NOTE_CONTENT_PENDING" or "EQUITY_READINESS_PENDING");
    // The draft is unchanged and still records that its content was unreviewed when pinned.
    var draft = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleAsync(x => x.Id == draftId);
    Assert.Equal(FinancialStatementPresentationRevisionStatuses.UnapprovedDraft, draft.Status);
    Assert.False(FinancialStatementService.PresentationChecksAllPass(draft));
  }

  [Fact]
  public async Task FormalDecisions_FollowTheLatestEffectivePredecessor_AndRecoverTheirOriginalRequest()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var s = await StatementCandidatePromotionTests.SeedPresentableAsync(pg);
    var preparer = StatementCandidatePromotionTests.Actor(s.Preparer, "AccountingPreparer");
    var reviewer = StatementCandidatePromotionTests.Actor(s.Reviewer, "AccountingReviewer");
    var partner = StatementCandidatePromotionTests.Actor(s.Partner, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, s.PackageId, s.LayoutId);
    Assert.True(pin.Succeeded, pin.Message);
    Assert.True((await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, pin.Value!.RevisionId,
      FinancialStatementPresentationRenderer.PdfFormat)).Succeeded);
    Assert.True((await StatementContentReviewCall.RecordAsync(db, reviewer, s.PackageId, pin.Value.RevisionId)).Succeeded);
    var candidate = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, pin.Value.RevisionId);
    Assert.True(candidate.Succeeded, candidate.Message);
    var composition = (await FinancialStatementService.SelectCompositionAsync(db, preparer, s.PackageId, candidate.Value!.RevisionId)).Value!;

    FinancialPackageReviewRequest Request(string stage, string decision, string mode, Guid requestId, Guid? expectedLatest, string comment = "") =>
      new(s.PackageId, stage, decision, mode, "evidence-" + stage, comment, candidate.Value.RevisionId, null, composition.Id,
        composition.ManifestDigest, requestId, expectedLatest);
    const string Management = FinancialPackageReviewStages.ManagementApproval;
    const string Accounting = FinancialPackageReviewStages.AccountingReview;
    const string PartnerStage = FinancialPackageReviewStages.PartnerApproval;
    const string Approved = FinancialPackageReviewDecisions.Approved;
    const string Changes = FinancialPackageReviewDecisions.ChangesRequired;
    const string Offline = FinancialPackageReviewEvidenceModes.Offline;
    const string SignedIn = FinancialPackageReviewEvidenceModes.SignedIn;

    // Management approves; a retry of the same intent returns the same row; the same intent with a different payload conflicts.
    var firstRequest = Guid.NewGuid();
    var m1 = await FinancialPackageReviewService.RecordAsync(db, preparer, Request(Management, Approved, Offline, firstRequest, null));
    Assert.True(m1.Succeeded, m1.Message);
    Assert.Equal(m1.Value, (await FinancialPackageReviewService.RecordAsync(db, preparer, Request(Management, Approved, Offline, firstRequest, null))).Value);
    Assert.Equal(ErrorCodes.IdempotencyConflict,
      (await FinancialPackageReviewService.RecordAsync(db, preparer, Request(Management, Approved, Offline, firstRequest, null, "changed"))).ErrorCode);

    // Accounting decides on the chain it saw; a decision recorded since then makes its request stale.
    Assert.Equal(ErrorCodes.StaleRevision,
      (await FinancialPackageReviewService.RecordAsync(db, reviewer, Request(Accounting, Approved, SignedIn, Guid.NewGuid(), null))).ErrorCode);

    // Management returns the composition. Its earlier approval no longer lets accounting approve.
    var m2 = await FinancialPackageReviewService.RecordAsync(db, preparer, Request(Management, Changes, Offline, Guid.NewGuid(), m1.Value, "Fix note 3"));
    Assert.True(m2.Succeeded, m2.Message);
    var blocked = await FinancialPackageReviewService.RecordAsync(db, reviewer, Request(Accounting, Approved, SignedIn, Guid.NewGuid(), m2.Value));
    Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);

    // A genuine re-approval of the same composition is a new decision, not a duplicate of the first approval.
    var m3 = await FinancialPackageReviewService.RecordAsync(db, preparer, Request(Management, Approved, Offline, Guid.NewGuid(), m2.Value));
    Assert.True(m3.Succeeded, m3.Message);
    Assert.Equal(ErrorCodes.IdempotencyConflict,
      (await FinancialPackageReviewService.RecordAsync(db, preparer, Request(Management, Approved, Offline, Guid.NewGuid(), m3.Value))).ErrorCode);
    var a1 = await FinancialPackageReviewService.RecordAsync(db, reviewer, Request(Accounting, Approved, SignedIn, Guid.NewGuid(), m3.Value));
    Assert.True(a1.Succeeded, a1.Message);
    var p1 = await FinancialPackageReviewService.RecordAsync(db, partner, Request(PartnerStage, Approved, SignedIn, Guid.NewGuid(), a1.Value));
    Assert.True(p1.Succeeded, p1.Message);
    Assert.True((await FinancialPackageReviewService.RequireCurrentAsync(db, partner, s.PackageId, requirePartner: true)).Succeeded);

    // Each decision records the chain it followed.
    var stored = await db.FinancialPackageReviewDecisions.AsNoTracking().Where(x => x.FinancialPackageId == s.PackageId)
      .OrderBy(x => x.Sequence).ToListAsync();
    Assert.Equal([m1.Value, m2.Value, m3.Value, a1.Value, p1.Value], stored.Select(x => x.Id));
    Assert.Equal([null, m1.Value, m2.Value, m3.Value, a1.Value], stored.Select(x => x.PreviousDecisionId));

    // A later management return defeats the downstream approvals for release, and a fresh management approval needs them again.
    var m4 = await FinancialPackageReviewService.RecordAsync(db, preparer, Request(Management, Changes, Offline, Guid.NewGuid(), p1.Value, "Late change"));
    Assert.True(m4.Succeeded, m4.Message);
    Assert.False((await FinancialPackageReviewService.RequireCurrentAsync(db, partner, s.PackageId, requirePartner: true)).Succeeded);
    var m5 = await FinancialPackageReviewService.RecordAsync(db, preparer, Request(Management, Approved, Offline, Guid.NewGuid(), m4.Value));
    Assert.True(m5.Succeeded, m5.Message);
    var gate = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, s.PackageId, requirePartner: true);
    Assert.False(gate.Succeeded);
    Assert.Contains("predates", gate.Message);
    var a2 = await FinancialPackageReviewService.RecordAsync(db, reviewer, Request(Accounting, Approved, SignedIn, Guid.NewGuid(), m5.Value));
    Assert.True(a2.Succeeded, a2.Message);
    var p2 = await FinancialPackageReviewService.RecordAsync(db, partner, Request(PartnerStage, Approved, SignedIn, Guid.NewGuid(), a2.Value));
    Assert.True(p2.Succeeded, p2.Message);
    Assert.True((await FinancialPackageReviewService.RequireCurrentAsync(db, partner, s.PackageId, requirePartner: true)).Succeeded);
  }

  [Fact]
  public void EffectiveApprovals_UseRecordingOrderAndRequireEachStageAfterItsPredecessor()
  {
    var sequence = 0L;
    FinancialPackageReviewDecision D(string stage, string decision) => new()
    {
      Id = Guid.CreateVersion7(), Stage = stage, Decision = decision, Sequence = ++sequence,
      // Wall-clock time runs backwards here: the recorded sequence, not the timestamp, decides which decision is latest.
      DecidedAt = DateTimeOffset.UnixEpoch.AddDays(100 - sequence)
    };
    const string Management = FinancialPackageReviewStages.ManagementApproval;
    const string Accounting = FinancialPackageReviewStages.AccountingReview;
    const string PartnerStage = FinancialPackageReviewStages.PartnerApproval;
    const string Approved = FinancialPackageReviewDecisions.Approved;

    var m1 = D(Management, Approved);
    var a1 = D(Accounting, Approved);
    var p1 = D(PartnerStage, Approved);
    var chain = new List<FinancialPackageReviewDecision> { p1, m1, a1 };
    var effective = FinancialPackageReviewService.EffectiveApprovals(chain);
    Assert.Same(m1, effective[Management]);
    Assert.Same(a1, effective[Accounting]);
    Assert.Same(p1, effective[PartnerStage]);

    var returned = D(Management, FinancialPackageReviewDecisions.ChangesRequired);
    chain.Add(returned);
    effective = FinancialPackageReviewService.EffectiveApprovals(chain);
    Assert.All(effective.Values, Assert.Null);
    Assert.False(FinancialPackageReviewService.IsCurrentDuplicate(chain, Management, Approved));
    Assert.True(FinancialPackageReviewService.IsCurrentDuplicate(chain, Management, FinancialPackageReviewDecisions.ChangesRequired));

    var m2 = D(Management, Approved);
    chain.Add(m2);
    effective = FinancialPackageReviewService.EffectiveApprovals(chain);
    Assert.Same(m2, effective[Management]);
    Assert.Null(effective[Accounting]);
    Assert.Null(effective[PartnerStage]);
    Assert.False(FinancialPackageReviewService.IsCurrentDuplicate(chain, Accounting, Approved));
    // A redundant management re-approval while its approval is effective would silently defeat the later stages, so it is a duplicate.
    Assert.True(FinancialPackageReviewService.IsCurrentDuplicate(chain, Management, Approved));
  }
}
