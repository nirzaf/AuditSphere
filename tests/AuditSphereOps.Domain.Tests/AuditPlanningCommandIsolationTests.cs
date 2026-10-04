using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditPlanningCommandIsolationTests
{
  [Fact(DisplayName = "Client-scoped planning commands deny sibling engagement identifiers")]
  public async Task SiblingEngagementIds_AreDeniedAcrossPlanningCommands()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var clientBUserId = Guid.NewGuid();
    var clientBActor = new ActorContext(clientBUserId, clientB.FirmId, 1, ["Partner"]);

    Guid materialityId;
    Guid workpaperId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingEngagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      siblingEngagement.ProfessionalWorkBlocked = false;
      db.Users.Add(new AppUser
      {
        Id = clientBUserId,
        FirmId = clientB.FirmId,
        Subject = "audit-planning-client-b-" + clientBUserId.ToString("N"),
        TenantId = "tenant-planning",
        Email = "audit-planning-client-b@example.test",
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

      var materiality = await AuditPlanningService.CreateMaterialityAssessmentAsync(db, clientBActor,
        NewMateriality(clientB.EngagementId));
      Assert.True(materiality.Succeeded, materiality.Message);
      materialityId = materiality.Value!.AssessmentId;

      var workpaper = await AuditPlanningService.CreateWorkpaperAsync(db, clientBActor,
        NewWorkpaper(clientB.EngagementId));
      Assert.True(workpaper.Succeeded, workpaper.Message);
      workpaperId = workpaper.Value!.WorkpaperId;

      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditPlanningService.CreateMaterialityAssessmentAsync(db, clientA.Actor,
          NewMateriality(clientB.EngagementId))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, clientA.Actor, materialityId)).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditPlanningService.CreateAuditRiskAsync(db, clientA.Actor,
          new CreateAuditRiskRequest(clientB.EngagementId, "Revenue", "Occurrence",
            "Revenue may be recognized before delivery.", "Complex contracts.",
            SignificanceDecisions.Significant, "No effective control.", "Test contract evidence."))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditPlanningService.CreatePopulationVersionAsync(db, clientA.Actor,
          new CreatePopulationRequest(clientB.EngagementId, "Trade receivables", "Existence",
            "AR-EXPORT-CLIENT-B", "All posted receivables at year end", 10, 1250m, "QAR", null))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditPlanningService.CreateWorkpaperAsync(db, clientA.Actor,
          NewWorkpaper(clientB.EngagementId))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditPlanningService.SubmitWorkpaperAsync(db, clientA.Actor,
          new SubmitWorkpaperRequest(workpaperId, 1, "Unauthorized work performed.",
            "Unauthorized conclusion."))).ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var materiality = await db.MaterialityAssessments.AsNoTracking().SingleAsync(x => x.Id == materialityId);
      var workpaper = await db.Workpapers.AsNoTracking().SingleAsync(x => x.Id == workpaperId);
      Assert.Equal(clientB.ClientId, materiality.ClientId);
      Assert.Equal(clientB.EngagementId, materiality.EngagementId);
      Assert.Equal(MaterialityStatuses.Draft, materiality.Status);
      Assert.Equal(clientBUserId, materiality.ActorId);
      Assert.Equal(0, await db.MaterialityApprovals.CountAsync(x => x.MaterialityAssessmentId == materialityId));
      Assert.Equal(clientB.ClientId, workpaper.ClientId);
      Assert.Equal(clientB.EngagementId, workpaper.EngagementId);
      Assert.Equal(WorkpaperStatuses.Working, workpaper.Status);
      Assert.Equal(1, workpaper.Revision);
      Assert.Equal(clientBUserId, workpaper.ActorId);
      Assert.Equal(0, await db.WorkpaperSubmissions.CountAsync(x => x.WorkpaperId == workpaperId));
      Assert.Equal(0, await db.AuditRisks.CountAsync(x => x.EngagementId == clientB.EngagementId));
      Assert.Equal(0, await db.PopulationVersions.CountAsync(x => x.EngagementId == clientB.EngagementId));
      Assert.Equal(1, await db.MaterialityAssessments.CountAsync(x => x.EngagementId == clientB.EngagementId));
      Assert.Equal(1, await db.Workpapers.CountAsync(x => x.EngagementId == clientB.EngagementId));
    }
  }

  private static CreateMaterialityRequest NewMateriality(Guid engagementId) => new(
    engagementId, "Total assets", "AFS-v1", "Stable benchmark", 1_000_000m, 0.05m,
    50_000m, 37_500m, 2_500m, null);

  private static CreateWorkpaperRequest NewWorkpaper(Guid engagementId) => new(
    engagementId, "CB-02", "Bank Reconciliation", "Verify year-end bank reconciliation",
    "CASH-2025-v3", null, "Obtain bank confirmations and agree to TB");
}
