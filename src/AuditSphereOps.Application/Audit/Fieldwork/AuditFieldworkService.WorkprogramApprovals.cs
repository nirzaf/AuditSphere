using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditFieldworkService
{
  /// <summary>
  /// Independent Manager-level approval of every applicable workprogramme of the engagement after
  /// independent per-procedure review. The final automatic Summary Review Memorandum handoff fires
  /// only against a current approval; a Senior's final Green review alone never substitutes for it
  /// (STE 4.3.3, STE-REM-05). Idempotent per the current procedure-review set: approving again with
  /// unchanged facts returns the existing approval.
  /// </summary>
  public static async Task<CommandResult<Guid>> ApproveWorkprogramsAsync(
    IAuditSphereDbContext db, ActorContext actor, ApproveWorkprogramsRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Rationale))
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "A rationale for the Manager workprogramme approval is required.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var rank = await EngagementRankAsync(db, actor, request.EngagementId, ct);
    if (rank < 3)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied,
        "Only an Audit Manager or higher may record the required workprogramme approval.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var writable = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(db, actor, request.EngagementId, "approve workprogrammes", ct);
    if (!writable.Succeeded)
    {
      await tx.CommitAsync(ct); // persist the recorded refused attempt before refusing
      return CommandResult<Guid>.Fail(writable.ErrorCode!, writable.Message!);
    }
    var applicable = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.EngagementId == request.EngagementId && x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable).ToListAsync(ct);
    if (applicable.Count == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "No applicable workprogrammes exist to approve.");
    if (await db.AuditProcedures.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
      x.EngagementId == request.EngagementId && x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable &&
      x.Status != AuditProcedureStatuses.Reviewed, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Every applicable workprogramme must be independently reviewed before Manager approval.");
    var generation = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    var latestReviewAt = await db.AuditProcedureReviews.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Select(x => (DateTimeOffset?)x.CreatedAt).FirstOrDefaultAsync(ct);
    var existing = await db.AuditWorkprogramManagerApprovals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (existing is not null && existing.InputGeneration == generation &&
        (latestReviewAt is null || existing.CreatedAt >= latestReviewAt))
      return CommandResult<Guid>.Ok(existing.Id);
    var approval = new AuditWorkprogramManagerApproval
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      Rationale = request.Rationale.Trim(), ApprovedByUserId = actor.UserId, InputGeneration = generation,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditWorkprogramManagerApprovals.Add(approval);
    await db.SaveChangesAsync(ct);
    // The automatic SRM compilation runs against this fresh current approval (engine reuse, STE-REM-05).
    await AuditSphereOps.Application.Completion.AuditDeliverableService.CompileAfterFinalReviewAsync(db, actor, request.EngagementId, ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(approval.Id);
  }

  private static async Task<int> EngagementRankAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var staffAssignment = await db.EngagementStaffAssignments.AsNoTracking().FirstOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.UserId == actor.UserId && x.RevokedAt == null, ct);
    return staffAssignment is not null ? StaffingLevels.Rank(staffAssignment.StaffingLevel) :
      actor.Roles.Contains("Partner") ? 4 : actor.Roles.Contains("Manager") ? 3 : actor.Roles.Contains("Senior") ? 2 : 1;
  }
}
