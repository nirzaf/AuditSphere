using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditFindingCommandIsolationTests
{
  [Fact(DisplayName = "Client-scoped finding commands deny sibling engagement identifiers")]
  public async Task SiblingEngagementIds_AreDeniedForFindingCreateResponseAndDesignation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var clientBUserId = Guid.NewGuid();
    var clientBActor = new ActorContext(clientBUserId, clientB.FirmId, 1, ["Partner"]);

    Guid findingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingEngagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      siblingEngagement.ProfessionalWorkBlocked = false;
      db.Users.Add(new AppUser
      {
        Id = clientBUserId,
        FirmId = clientB.FirmId,
        Subject = "audit-finding-client-b-" + clientBUserId.ToString("N"),
        TenantId = "tenant-planning",
        Email = "audit-finding-client-b@example.test",
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

      var created = await AuditPlanningService.CreateFindingAsync(db, clientBActor,
        NewFinding(clientB.EngagementId, "Client B control deficiency."));
      Assert.True(created.Succeeded, created.Message);
      findingId = created.Value!.FindingId;

      var siblingCreate = await AuditPlanningService.CreateFindingAsync(db, clientA.Actor,
        NewFinding(clientB.EngagementId, "Unauthorized sibling finding."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingCreate.ErrorCode);

      var siblingResponse = await AuditPlanningService.RecordFindingResponseAsync(db, clientA.Actor,
        new RecordFindingResponseRequest(findingId, "Unauthorized sibling response.", true));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingResponse.ErrorCode);

      var siblingDesignation = await AuditPlanningService.DesignateManagementLetterFindingAsync(db, clientA.Actor,
        new RecordManagementLetterDesignationRequest(findingId, true,
          "Unauthorized recommendation for the client-facing letter."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingDesignation.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var finding = await db.Findings.AsNoTracking().SingleAsync(x => x.Id == findingId);
      Assert.Equal(clientB.ClientId, finding.ClientId);
      Assert.Equal(clientB.EngagementId, finding.EngagementId);
      Assert.Equal("Client B control deficiency.", finding.ImpactDescription);
      Assert.Equal(FindingStatuses.Open, finding.Status);
      Assert.Null(finding.ManagementResponse);
      Assert.Null(finding.LetterDesignatedAt);
      Assert.Null(finding.LetterDesignatedByUserId);
      Assert.Null(finding.LetterRecommendation);
      Assert.Equal(clientBUserId, finding.ActorId);
      Assert.Equal(1, await db.Findings.CountAsync(x => x.EngagementId == clientB.EngagementId));
    }
  }

  private static CreateFindingRequest NewFinding(Guid engagementId, string impact) => new(
    engagementId, "Control deficiency", impact, Corrected: false, MonetaryAmount: 250m, ManagementResponse: null);
}
