using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditConfirmationCommandIsolationTests
{
  [Fact(DisplayName = "Client-scoped confirmation commands cannot create or mutate sibling client records by identifier")]
  public async Task SiblingClientIds_AreDeniedAcrossConfirmationLifecycle()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var clientBPartnerId = Guid.NewGuid();
    var clientBReviewerId = Guid.NewGuid();
    var clientBPartner = new ActorContext(clientBPartnerId, clientB.FirmId, 1, ["Partner"]);
    var clientBReviewer = new ActorContext(clientBReviewerId, clientB.FirmId, 1, ["Reviewer"]);

    Guid confirmationId;
    Guid responseId;
    Guid alternativeId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingEngagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      siblingEngagement.ProfessionalWorkBlocked = false;
      AddScopedStaff(db, clientA.Actor.UserId, clientB, clientBPartnerId, "Partner", "confirmation-client-b-partner");
      AddScopedStaff(db, clientA.Actor.UserId, clientB, clientBReviewerId, "Reviewer", "confirmation-client-b-reviewer");
      await db.SaveChangesAsync();

      var created = await AuditFieldworkService.CreateConfirmationAsync(db, clientBPartner,
        NewConfirmation(clientB.EngagementId));
      Assert.True(created.Succeeded, created.Message);
      confirmationId = created.Value!.ConfirmationCaseId;

      var wrongClientCreate = await AuditFieldworkService.CreateConfirmationAsync(db, clientA.Actor,
        NewConfirmation(clientB.EngagementId));
      Assert.Equal(ErrorCodes.ScopeDenied, wrongClientCreate.ErrorCode);

      var unauthorizedApproval = await AuditFieldworkService.ApproveConfirmationAsync(db, clientA.Actor, confirmationId);
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedApproval.ErrorCode);
      Assert.Equal(AuditConfirmationStatuses.Draft,
        await db.AuditConfirmationCases.Where(x => x.Id == confirmationId).Select(x => x.Status).SingleAsync());

      Assert.True((await AuditFieldworkService.ApproveConfirmationAsync(db, clientBReviewer, confirmationId)).Succeeded);
      var unauthorizedDispatch = await AuditFieldworkService.RecordDispatchEvidenceAsync(db, clientA.Actor,
        new RecordConfirmationDispatchRequest(confirmationId, "attempted-sibling-dispatch"));
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedDispatch.ErrorCode);

      Assert.True((await AuditFieldworkService.RecordDispatchEvidenceAsync(db, clientBPartner,
        new RecordConfirmationDispatchRequest(confirmationId, "provider-dispatch-client-b"))).Succeeded);
      var responseRequest = new RecordConfirmationResponseRequest(confirmationId, "DIRECT", "PORTAL",
        "response-client-b", null, "Authenticated portal response reviewed for authenticity.",
        AuditConfirmationDecisions.AlternativeRequired);
      var unauthorizedResponse = await AuditFieldworkService.RecordConfirmationResponseAsync(db, clientA.Actor, responseRequest);
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedResponse.ErrorCode);

      Assert.True((await AuditFieldworkService.RecordConfirmationResponseAsync(db, clientBPartner, responseRequest)).Succeeded);
      responseId = await db.AuditConfirmationResponses.Where(x => x.ConfirmationCaseId == confirmationId)
        .Select(x => x.Id).SingleAsync();

      var unauthorizedResponseReview = await AuditFieldworkService.ReviewConfirmationResponseAsync(db, clientA.Actor,
        new ReviewConfirmationResponseRequest(responseId));
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedResponseReview.ErrorCode);

      var alternativeRequest = new RecordAlternativeProcedureRequest(confirmationId,
        "Inspect subsequent cleared transactions.", ["bank-statement-subsequent-client-b"],
        "Subsequent clearing supports the recorded balance.");
      var unauthorizedAlternative = await AuditFieldworkService.RecordAlternativeProcedureAsync(db, clientA.Actor,
        alternativeRequest);
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedAlternative.ErrorCode);

      Assert.True((await AuditFieldworkService.RecordAlternativeProcedureAsync(db, clientBPartner,
        alternativeRequest)).Succeeded);
      alternativeId = await db.AuditAlternativeProcedures.Where(x => x.ConfirmationCaseId == confirmationId)
        .Select(x => x.Id).SingleAsync();

      var unauthorizedAlternativeReview = await AuditFieldworkService.ReviewAlternativeProcedureAsync(db, clientA.Actor,
        new ReviewAlternativeProcedureRequest(alternativeId, "Attempted sibling review."));
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedAlternativeReview.ErrorCode);

      var unauthorizedClose = await AuditFieldworkService.CloseConfirmationAsync(db, clientA.Actor,
        new CloseConfirmationRequest(confirmationId, "Attempted sibling closure."));
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedClose.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var confirmation = await db.AuditConfirmationCases.AsNoTracking().SingleAsync(x => x.Id == confirmationId);
      var response = await db.AuditConfirmationResponses.AsNoTracking().SingleAsync(x => x.Id == responseId);
      var alternative = await db.AuditAlternativeProcedures.AsNoTracking().SingleAsync(x => x.Id == alternativeId);
      Assert.Equal(clientB.ClientId, confirmation.ClientId);
      Assert.Equal(clientB.EngagementId, confirmation.EngagementId);
      Assert.Equal(AuditConfirmationStatuses.AlternativeRequired, confirmation.Status);
      Assert.Equal("provider-dispatch-client-b", confirmation.DispatchReference);
      Assert.Equal(clientBPartnerId, response.CreatedByUserId);
      Assert.Null(response.ReviewedByUserId);
      Assert.Equal(AuditAlternativeStatuses.Submitted, alternative.Status);
      Assert.Equal(clientBPartnerId, alternative.CreatedByUserId);
      Assert.Equal(0, await db.AuditConfirmationClosures.CountAsync(x => x.ConfirmationCaseId == confirmationId));
      Assert.Equal(1, await db.AuditConfirmationCases.CountAsync(x => x.EngagementId == clientB.EngagementId));
      Assert.Equal(1, await db.AuditConfirmationResponses.CountAsync(x => x.ConfirmationCaseId == confirmationId));
      Assert.Equal(1, await db.AuditAlternativeProcedures.CountAsync(x => x.ConfirmationCaseId == confirmationId));
    }
  }

  private static CreateConfirmationRequest NewConfirmation(Guid engagementId) => new(
    engagementId, null, AuditAreaCodes.CashBank, "bank-client-b", 50m, "QAR",
    new DateOnly(2026, 9, 19), "Bank relationship manager", "Client master contact list reviewed by partner.");

  private static void AddScopedStaff(
    AuditSphereDbContext db, Guid grantedBy, PlanningScope scope, Guid userId, string role, string subject)
  {
    db.Users.Add(new AppUser
    {
      Id = userId,
      FirmId = scope.FirmId,
      Subject = subject + "-" + userId.ToString("N"),
      TenantId = "tenant-planning",
      Email = subject + "@example.test",
      DisplayName = subject,
      UserKind = "Staff",
      SessionEpoch = 1,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(),
      FirmId = scope.FirmId,
      UserId = userId,
      Role = role,
      ClientId = scope.ClientId,
      EngagementId = scope.EngagementId,
      GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = grantedBy
    });
  }
}
