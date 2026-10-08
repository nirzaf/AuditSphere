using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditProgramCommandIsolationTests
{
  [Fact(DisplayName = "Client-scoped audit program commands cannot mutate sibling client records by identifier")]
  public async Task SiblingClientIds_AreDeniedForAdoptionApplicabilitySubmissionAndReview()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var clientBUserId = Guid.NewGuid();
    var clientBActor = new ActorContext(clientBUserId, clientB.FirmId, 1, ["Partner"]);

    Guid programVersionId;
    Guid procedureId;
    Guid resultId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingEngagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      siblingEngagement.ProfessionalWorkBlocked = false;
      db.Users.Add(new AppUser
      {
        Id = clientBUserId,
        FirmId = clientB.FirmId,
        Subject = "audit-program-client-b-" + clientBUserId.ToString("N"),
        TenantId = "tenant-planning",
        Email = "client-b-partner@example.test",
        DisplayName = "Client B Partner",
        UserKind = "Staff",
        SessionEpoch = 1,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(),
        FirmId = clientB.FirmId,
        UserId = clientBUserId,
        Role = "Partner",
        ClientId = clientB.ClientId,
        EngagementId = clientB.EngagementId,
        GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = clientA.Actor.UserId
      });
      await db.SaveChangesAsync();

      var published = await AuditProgramService.PublishAsync(db, clientA.Actor,
        new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(published.Succeeded, published.Message);
      programVersionId = published.Value!.ProgramVersionId;

      var adopted = await AuditProgramService.AdoptAsync(db, clientBActor,
        new AdoptAuditProgramRequest(clientB.EngagementId, programVersionId));
      Assert.True(adopted.Succeeded, adopted.Message);
      var procedure = await db.AuditProcedures.SingleAsync(x =>
        x.FirmId == clientB.FirmId && x.EngagementId == clientB.EngagementId &&
        x.SourceProcedureId == "AWP-08-01");
      procedureId = procedure.Id;

      var decision = await AuditProgramService.DecideApplicabilityAsync(db, clientBActor,
        new DecideProcedureApplicabilityRequest(procedureId, AuditApplicabilityStatuses.Applicable, null));
      Assert.True(decision.Succeeded, decision.Message);

      // Legitimate planning approval precedes substantive execution (STE-REM-04): the sibling part B
      // partner prepares the basis; an independent firm-wide partner approves materiality.
      var basisApproverId = Guid.NewGuid();
      db.Users.Add(new AppUser
      {
        Id = basisApproverId, FirmId = clientB.FirmId, Subject = "basis-approver-" + basisApproverId.ToString("N"),
        TenantId = "tenant-planning", Email = "basis-approver@example.test", DisplayName = "Planning Basis Approver",
        UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = clientB.FirmId, UserId = basisApproverId, Role = "Partner",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = clientBUserId
      });
      await db.SaveChangesAsync();
      await PlanningBasisSeed.EstablishAsync(db, clientB.FirmId, clientB.ClientId, clientB.EngagementId, clientBActor,
        new ActorContext(basisApproverId, clientB.FirmId, 1, ["Partner"]));

      var submitted = await AuditProgramService.SubmitResultAsync(db, clientBActor,
        new SubmitProcedureResultRequest(procedureId, 1, "Agreed the bank listing to the ledger.",
          "{\"result\":\"PASS\"}", ["source:bank-list-1"], "No exception noted."));
      Assert.True(submitted.Succeeded, submitted.Message);
      resultId = submitted.Value!.AuditProcedureResultId;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingAdoption = await AuditProgramService.AdoptAsync(db, clientA.Actor,
        new AdoptAuditProgramRequest(clientB.EngagementId, programVersionId));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingAdoption.ErrorCode);

      var siblingDecision = await AuditProgramService.DecideApplicabilityAsync(db, clientA.Actor,
        new DecideProcedureApplicabilityRequest(procedureId,
          AuditApplicabilityStatuses.NotApplicablePendingReview, "Attempted sibling mutation."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingDecision.ErrorCode);

      var siblingSubmission = await AuditProgramService.SubmitResultAsync(db, clientA.Actor,
        new SubmitProcedureResultRequest(procedureId, 1, "Attempted sibling work.",
          "{\"result\":\"FAIL\"}", ["source:guessed"], "Attempted sibling conclusion."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingSubmission.ErrorCode);

      var siblingReview = await AuditProgramService.ReviewResultAsync(db, clientA.Actor,
        new ReviewProcedureResultRequest(resultId, AuditProcedureReviewDecisions.ChangesRequired,
          "Attempted sibling review."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingReview.ErrorCode);

      var persistedProcedure = await db.AuditProcedures.AsNoTracking().SingleAsync(x => x.Id == procedureId);
      var persistedResult = await db.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.Id == resultId);
      Assert.Equal(AuditApplicabilityStatuses.Applicable, persistedProcedure.ApplicabilityStatus);
      Assert.Equal(AuditProcedureStatuses.Submitted, persistedProcedure.Status);
      Assert.Equal(1, persistedProcedure.CurrentResultRevision);
      Assert.Equal(AuditProcedureResultStatuses.Submitted, persistedResult.Status);
      Assert.Equal(clientBUserId, persistedResult.PreparedByUserId);
      Assert.Equal(0, await db.AuditProcedureReviews.CountAsync(x => x.AuditProcedureResultId == resultId));
      Assert.Equal(1, await db.AuditProcedureResults.CountAsync(x => x.AuditProcedureId == procedureId));
      Assert.Equal(1, await db.Workpapers.CountAsync(x => x.ProcedureId == procedureId));
    }
  }
}
