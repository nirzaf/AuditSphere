using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  [Theory]
  [InlineData("DECLINED")]
  [InlineData("DEFERRED")]
  [InlineData("ACCEPTED_WITH_CONDITIONS")]
  public async Task PortalCurrentRisk_AdverseDecisionCannotReuseOriginalAcceptedLetter(string decision)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedSupportedDeliverablesAsync(pg, refreshPortalRisk: false);
    await using var db = new AuditSphereDbContext(pg.Options);
    var letter = await db.CommercialDocuments.AsNoTracking()
      .SingleAsync(x => x.FirmId == w.FirmId && x.Kind == "ENGAGEMENT_LETTER");
    Assert.False(await ClientPortalService.CommercialOnboardingClearedAsync(db, w.FirmId, w.ClientId, w.EngagementId, default));
    var preparer = w.A("partner2", "Partner");
    var checklist = await AcceptanceChecklistService.GetAsync(db, preparer, w.ClientId);
    Assert.True(checklist.Succeeded, checklist.Message);
    foreach (var item in checklist.Value!.Items)
    {
      var answer = item.Question.AdverseAnswer == "YES" ? "No" : "Yes";
      var saved = await AcceptanceChecklistService.RecordAnswerAsync(db, preparer, w.ClientId,
        item.Question.Code, answer, item.Question.RequiresEvidence ? "synthetic-portal-risk-review" : null,
        expectedGeneration: checklist.Value.Generation, expectedRevision: item.Answer?.Revision ?? 0);
      Assert.True(saved.Succeeded, saved.Message);
    }
    var recorded = await AcceptanceDecisionService.RecordAsync(db, w.A("partner", "Partner"),
      new(w.ClientId, w.EngagementId, "FinancialStatementAudit", decision,
        "Independent current risk decision.", decision == "ACCEPTED_WITH_CONDITIONS" ? "Resolve the recorded condition." : null,
        checklist.Value.Generation));
    Assert.True(recorded.Succeeded, recorded.Message);
    Assert.False(await ClientPortalService.CommercialOnboardingClearedAsync(db, w.FirmId, w.ClientId, w.EngagementId, default));
    var retained = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == letter.Id);
    Assert.Equal(letter.Bytes, retained.Bytes);
    Assert.Equal(letter.AcceptanceDecisionId, retained.AcceptanceDecisionId);
  }

  [Fact]
  public async Task PortalCurrentRisk_ExactScopeAndStandingCommercialApprovalRemainRequired()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedSupportedDeliverablesAsync(pg, requireCommercialReview: true);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.True(await ClientPortalService.CommercialOnboardingClearedAsync(db, w.FirmId, w.ClientId, w.EngagementId, default));
    Assert.False(await ClientPortalService.CommercialOnboardingClearedAsync(db, w.FirmId, w.ClientId, Guid.NewGuid(), default));
    var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.EngagementId == w.EngagementId);
    var approval = await db.QuotationApprovals.AsNoTracking().FirstAsync(x => x.QuotationVersionId == agreement.QuotationVersionId);
    var revoked = await QuotationService.RevokeApprovalAsync(db, w.A("administrator", "Administrator"), approval.Id,
      "Independent withdrawal of the commercial approval.");
    Assert.True(revoked.Succeeded, revoked.Message);
    Assert.False(await ClientPortalService.CommercialOnboardingClearedAsync(db, w.FirmId, w.ClientId, w.EngagementId, default));
  }
  [Theory]
  [InlineData("payment")]
  [InlineData("grant")]
  public async Task PortalCurrentRisk_UploadRechecksPaidAdvanceAfterLockAndCurrentAuthority(string change)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedSupportedDeliverablesAsync(pg);
    var client = w.A("client", "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg,
      new(w.FirmId, w.ClientId, w.EngagementId, w.U["associate"], w.U["manager"], w.U["client"], w.U["administrator"]),
      w.A("associate", "Staff"), client);
    await using var db = new AuditSphereDbContext(pg.Options);
    var revision = await db.PbcRequests.Where(x => x.Id == requestId).Select(x => x.Revision).SingleAsync();
    var operationCount = await db.DurableOperations.CountAsync();
    var letter = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.Kind == "ENGAGEMENT_LETTER");
    var uploadRequest = new StartPbcUploadRequest(requestId, "ledger.csv", "text/csv", 10, new string('a', 64));
    if (change == "grant")
    {
      var grant = await db.RoleGrants.AsNoTracking().SingleAsync(x => x.UserId == client.UserId && x.Role == "ClientUser" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, w.A("administrator", "Administrator"),
        new(grant.Id, Reason: "Withdraw this client's exact portal authority."));
      Assert.True(revoked.Succeeded, revoked.Message);
      var refused = await PbcService.StartUploadAsync(db, client, uploadRequest);
      Assert.Equal(ErrorCodes.GenerationStale, refused.ErrorCode);
      var currentUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == client.UserId);
      Assert.True(currentUser.SessionEpoch > client.SessionEpoch);
      var currentActor = client with { SessionEpoch = currentUser.SessionEpoch };
      Assert.Equal(ErrorCodes.ScopeDenied, (await PbcService.StartUploadAsync(db, currentActor, uploadRequest)).ErrorCode);
    }
    else
    {
      var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.EngagementId == w.EngagementId);
      var advance = await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.AgreementId == agreement.Id && x.Kind == FeeMilestoneKinds.Advance);
      var allocation = await db.ReceiptAllocations.AsNoTracking().SingleAsync(x => x.InvoiceId == advance.InvoiceId && x.ReceiptId == advance.ReceiptId);
      var requested = await ReceiptAllocationReversalService.SubmitAsync(db, w.A("finance-maker", "FinanceManager"), allocation.Id,
        new(1m, "PORTAL-ADVANCE-CORRECTION", "Independently reviewed allocation correction.", 0, true));
      Assert.True(requested.Succeeded, requested.Message);
      await using var reviewing = new AuditSphereDbContext(pg.Options);
      await using var transaction = await reviewing.Database.BeginTransactionAsync();
      var reviewed = await ReceiptAllocationReversalService.ReviewAsync(reviewing, w.A("finance-reviewer", "FinanceReviewer"), requested.Value,
        new(true, "Approved correction of allocated amount.", true));
      Assert.True(reviewed.Succeeded, reviewed.Message);
      await using var uploading = new AuditSphereDbContext(pg.Options);
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
      var upload = PbcService.StartUploadAsync(uploading, client, uploadRequest, timeout.Token);
      var committed = false;
      try
      {
        while (await db.Database.SqlQueryRaw<int>(
          "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() " +
          "AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE '%firm_safety_states%'")
          .SingleAsync(timeout.Token) == 0)
          await Task.Delay(25, timeout.Token);
        Assert.False(upload.IsCompleted);
        await transaction.CommitAsync(timeout.Token);
        committed = true;
        var refused = await upload.WaitAsync(timeout.Token);
        Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
      }
      finally
      {
        if (!committed) await transaction.RollbackAsync();
      }
      Assert.False(await FeeAgreementService.AdvanceCurrentlyPaidAsync(db, w.FirmId, agreement.Id));
      Assert.Equal(w.InputGeneration, await db.ClientSafetyStates.AsNoTracking()
        .Where(x => x.Id == w.ClientId).Select(x => x.InputGeneration).SingleAsync());
      var retainedAdvance = await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.Id == advance.Id);
      Assert.Equal((advance.State, advance.ReceiptId, advance.PaidAt),
        (retainedAdvance.State, retainedAdvance.ReceiptId, retainedAdvance.PaidAt));
    }
    Assert.Empty(await db.PbcUploadIntents.ToListAsync());
    Assert.Empty(await db.PbcUploadChunks.ToListAsync());
    Assert.Equal(operationCount, await db.DurableOperations.CountAsync());
    Assert.Equal(revision, await db.PbcRequests.Where(x => x.Id == requestId).Select(x => x.Revision).SingleAsync());
    var retainedLetter = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == letter.Id);
    Assert.Equal(letter.Bytes, retainedLetter.Bytes);
    Assert.Equal(letter.AcceptanceDecisionId, retainedLetter.AcceptanceDecisionId);
  }

}
