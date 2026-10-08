using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditPlanningCompletionIsolationTests
{
  [Fact(DisplayName = "Client-scoped opening-balance commands deny sibling engagement identifiers")]
  public async Task SiblingEngagementIds_AreDeniedForOpeningBalanceRecordAndReview()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var clientBUserId = Guid.NewGuid();
    var clientBActor = new ActorContext(clientBUserId, clientB.FirmId, 1, ["Partner"]);

    Guid verificationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingEngagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      siblingEngagement.ProfessionalWorkBlocked = false;
      db.Users.Add(new AppUser
      {
        Id = clientBUserId,
        FirmId = clientB.FirmId,
        Subject = "opening-balance-client-b-" + clientBUserId.ToString("N"),
        TenantId = "tenant-planning",
        Email = "opening-balance-client-b@example.test",
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

      var created = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, clientBActor,
        NewVerification(clientB.EngagementId, "Client B prior-year audited financial statements."));
      Assert.True(created.Succeeded, created.Message);
      verificationId = created.Value!.VerificationId;

      var siblingRecord = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, clientA.Actor,
        NewVerification(clientB.EngagementId, "Unauthorized replacement using sibling engagement ID."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingRecord.ErrorCode);

      var siblingReview = await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(db, clientA.Actor,
        verificationId, "Unauthorized sibling review.");
      Assert.Equal(ErrorCodes.ScopeDenied, siblingReview.ErrorCode);

      var siblingSummary = await AuditPlanningCompletionService.GetAuditPlanningSummaryAsync(db, clientA.Actor,
        clientB.EngagementId);
      Assert.Equal(ErrorCodes.ScopeDenied, siblingSummary.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var verification = await db.OpeningBalanceVerifications.AsNoTracking().SingleAsync(x => x.Id == verificationId);
      Assert.Equal(clientB.ClientId, verification.ClientId);
      Assert.Equal(clientB.EngagementId, verification.EngagementId);
      Assert.Equal("Client B prior-year audited financial statements.", verification.PriorReference);
      Assert.Equal(OpeningBalanceVerificationConclusions.Agreed, verification.Conclusion);
      Assert.Equal(clientBUserId, verification.RecordedByUserId);
      Assert.Null(verification.ReviewedByUserId);
      Assert.Equal(1, verification.Revision);
      Assert.Equal(1, await db.OpeningBalanceVerifications.CountAsync(x => x.EngagementId == clientB.EngagementId));
    }
  }

  private static OpeningBalanceVerificationRequest NewVerification(Guid engagementId, string priorReference) => new(
    engagementId, null, null, priorReference, new DateOnly(2026, 1, 1), "QAR", 1000m, 1000m, true,
    "Opening balances agree to the prior-year audited financial statements.",
    ["prior-year-audited-fs", "ledger-opening-tb"]);
}
