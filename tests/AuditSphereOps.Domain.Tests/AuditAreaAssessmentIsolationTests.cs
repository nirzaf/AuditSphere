using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditAreaAssessmentIsolationTests
{
  [Fact(DisplayName = "Client-scoped assessment commands and completion queries deny sibling engagement identifiers")]
  public async Task SiblingEngagementIds_AreDeniedForAssessmentAndCompletion()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var clientBUserId = Guid.NewGuid();
    var clientBActor = new ActorContext(clientBUserId, clientB.FirmId, 1, ["Partner"]);

    Guid assessmentId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingEngagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      siblingEngagement.ProfessionalWorkBlocked = false;
      db.Users.Add(new AppUser
      {
        Id = clientBUserId,
        FirmId = clientB.FirmId,
        Subject = "area-assessment-client-b-" + clientBUserId.ToString("N"),
        TenantId = "tenant-planning",
        Email = "area-assessment-client-b@example.test",
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

      var created = await AuditFieldworkService.RecordAreaAssessmentAsync(db, clientBActor,
        NewAssessment(clientB.EngagementId, "Client B variance assessment."));
      Assert.True(created.Succeeded, created.Message);
      assessmentId = created.Value!.AuditAreaAssessmentId;

      var siblingCreate = await AuditFieldworkService.RecordAreaAssessmentAsync(db, clientA.Actor,
        NewAssessment(clientB.EngagementId, "Unauthorized sibling assessment."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingCreate.ErrorCode);

      var siblingCompletion = await AuditFieldworkService.EvaluateCompletionAsync(db, clientA.Actor,
        clientB.EngagementId);
      Assert.Equal(ErrorCodes.ScopeDenied, siblingCompletion.ErrorCode);

      var siblingReview = await AuditFieldworkService.ReviewAreaAssessmentAsync(db, clientA.Actor,
        new ReviewAreaAssessmentRequest(assessmentId, AuditAreaAssessmentStatuses.Reviewed, null));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingReview.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var assessment = await db.AuditAreaAssessments.AsNoTracking().SingleAsync(x => x.Id == assessmentId);
      Assert.Equal(clientB.ClientId, assessment.ClientId);
      Assert.Equal(clientB.EngagementId, assessment.EngagementId);
      Assert.Equal("Client B variance assessment.", assessment.Conclusion);
      Assert.Equal(AuditAreaAssessmentStatuses.Submitted, assessment.Status);
      Assert.Null(assessment.ReviewedByUserId);
      Assert.Equal(clientBUserId, assessment.CreatedByUserId);
      Assert.Equal(1, await db.AuditAreaAssessments.CountAsync(x => x.EngagementId == clientB.EngagementId));
    }
  }

  private static RecordAreaAssessmentRequest NewAssessment(Guid engagementId, string conclusion) => new(
    engagementId, null, AuditAreaCodes.AnalyticalReview, "MONTHLY_TREND", "analytics-method-v1", "{}",
    100m, 102m, 2m, 2m, "QAR", new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19),
    ["analytics-source-client-b"], conclusion);
}
