using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditDifferenceCommandIsolationTests
{
  [Fact(DisplayName = "Client-scoped difference commands cannot create or mutate sibling-client differences by identifier")]
  public async Task SiblingClientIds_AreDeniedForDifferenceQueriesAndCommands()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var clientBUserId = Guid.NewGuid();
    var clientBActor = new ActorContext(clientBUserId, clientB.FirmId, 1, ["Partner"]);

    Guid differenceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingEngagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      siblingEngagement.ProfessionalWorkBlocked = false;
      db.Users.Add(new AppUser
      {
        Id = clientBUserId,
        FirmId = clientB.FirmId,
        Subject = "audit-difference-client-b-" + clientBUserId.ToString("N"),
        TenantId = "tenant-planning",
        Email = "difference-client-b@example.test",
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

      var created = await AuditFieldworkService.RecordDifferenceAsync(db, clientBActor,
        NewDifference(clientB.EngagementId, "Client B's timing difference."));
      Assert.True(created.Succeeded, created.Message);
      differenceId = created.Value!.AuditDifferenceId;

      var siblingCreate = await AuditFieldworkService.RecordDifferenceAsync(db, clientA.Actor,
        NewDifference(clientB.EngagementId, "Unauthorized sibling difference."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingCreate.ErrorCode);

      var siblingSummary = await AuditFieldworkService.GetDifferenceSummariesAsync(db, clientA.Actor,
        clientB.EngagementId);
      Assert.Equal(ErrorCodes.ScopeDenied, siblingSummary.ErrorCode);

      var siblingEvaluation = await AuditFieldworkService.EvaluateDifferenceAsync(db, clientA.Actor,
        new EvaluateDifferenceRequest(differenceId, false, "Unauthorized evaluation.", "Unauthorized response.", null));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingEvaluation.ErrorCode);

      var siblingCorrection = await AuditFieldworkService.SetDifferenceCorrectionStateAsync(db, clientA.Actor,
        new SetDifferenceCorrectionStateRequest(differenceId, AuditDifferenceCorrectionStates.Rejected,
          "Unauthorized correction decision."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingCorrection.ErrorCode);

      var siblingJournalLink = await AuditFieldworkService.LinkDifferenceToJournalAsync(db, clientA.Actor,
        new LinkDifferenceToJournalRequest(differenceId, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid()));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingJournalLink.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var difference = await db.AuditDifferences.AsNoTracking().SingleAsync(x => x.Id == differenceId);
      Assert.Equal(clientB.ClientId, difference.ClientId);
      Assert.Equal(clientB.EngagementId, difference.EngagementId);
      Assert.Equal("Client B's timing difference.", difference.Description);
      Assert.Equal(AuditDifferenceStatuses.Open, difference.Status);
      Assert.False(difference.Corrected);
      Assert.Null(difference.Evaluation);
      Assert.Null(difference.ManagementResponse);
      Assert.Null(difference.CorrectionState);
      Assert.Null(difference.ProposedJournalId);
      Assert.Equal(clientBUserId, difference.CreatedByUserId);
      Assert.Equal(1, await db.AuditDifferences.CountAsync(x => x.EngagementId == clientB.EngagementId));
    }
  }

  private static RecordDifferenceRequest NewDifference(Guid engagementId, string description) => new(
    engagementId, null, "Cash", "KNOWN", description, -25m, "QAR");
}
