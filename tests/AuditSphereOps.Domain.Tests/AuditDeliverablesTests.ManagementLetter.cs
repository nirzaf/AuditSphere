using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// R09 / GAP-02: the management letter is a selective client report. Only findings explicitly designated
/// by an authorized Manager/Partner with a complete recommendation are rendered; internal-only findings
 /// never appear; an incomplete designation blocks generation; and changing a designated matter
/// invalidates the already-generated letter through the facts digest.
/// </summary>
[Trait("Profile", "Database")]
public sealed partial class AuditDeliverablesTests
{
  [Fact]
  public async Task ManagementLetterRendersOnlyDesignatedCompleteMatters_AndStalesOnTheirChange()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var manager = w.A("manager", "Manager");
    var recommendation = "Introduce a documented approval workflow before the next cycle.";

    await using var db = new AuditSphereDbContext(pg.Options);
    var findingId = await db.Findings.AsNoTracking().Select(x => x.Id).SingleAsync();
    db.Findings.Add(new Finding { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, ActorId = w.U["senior"].Id,
      FindingType = "Internal-only observation", ImpactDescription = "Housekeeping matter not intended for the client letter", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();

    // Designation is an authorized decision with a required, bounded recommendation.
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditPlanningService.DesignateManagementLetterFindingAsync(db, w.A("senior", "Senior"),
      new RecordManagementLetterDesignationRequest(findingId, true, recommendation))).ErrorCode);
    Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await AuditPlanningService.DesignateManagementLetterFindingAsync(db, manager,
      new RecordManagementLetterDesignationRequest(findingId, true, "   "))).ErrorCode);
    var designation = await AuditPlanningService.DesignateManagementLetterFindingAsync(db, manager,
      new RecordManagementLetterDesignationRequest(findingId, true, recommendation));
    Assert.True(designation.Succeeded, designation.Message);
    Assert.Equal(manager.UserId, (await db.Findings.AsNoTracking().SingleAsync(x => x.Id == findingId)).LetterDesignatedByUserId);

    // The letter renders the designated matter exactly once with its recommendation; the internal-only finding never appears.
    var letter = (await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.ManagementLetter)).Value!.DeliverableId!.Value;
    var text = DocumentText((await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == letter)).Content);
    Assert.Contains(recommendation, text);
    Assert.Contains("Credit notes approved without review", text);
    Assert.DoesNotContain("Housekeeping matter not intended for the client letter", text);
    Assert.Equal(1, text.Split("Credit notes approved without review").Length - 1);

    // A designated matter that lacks its recommendation blocks generation instead of rendering a gap.
    await db.Findings.Where(x => x.Id == findingId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LetterRecommendation, (string?)null));
    var blocked = await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.ManagementLetter);
    Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
    await db.Findings.Where(x => x.Id == findingId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LetterRecommendation, recommendation));

    // Changing a designated matter's content invalidates the already-generated letter through the facts digest.
    var generated = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == letter);
    Assert.True(await AuditDeliverableService.IsCurrentAsync(db, manager, generated));
    await db.Findings.Where(x => x.Id == findingId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ManagementResponse, "Workflow live since February."));
    Assert.False(await AuditDeliverableService.IsCurrentAsync(db, manager, generated));

    // Un-designating returns the finding to internal-only and stales the letter the same way.
    Assert.True((await AuditPlanningService.DesignateManagementLetterFindingAsync(db, manager,
      new RecordManagementLetterDesignationRequest(findingId, false, null))).Succeeded);
    var unDesignated = await db.Findings.AsNoTracking().SingleAsync(x => x.Id == findingId);
    Assert.Null(unDesignated.LetterDesignatedAt);
    Assert.Null(unDesignated.LetterRecommendation);
  }
}
