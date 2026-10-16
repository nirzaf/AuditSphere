using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditPlanningService
{
  // Compatibility methods retain signatures and participate in the caller's transaction without committing it.
  public static async Task<CommandResult<FindingResponseResult>> RecordFindingResponseAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordFindingResponseRequest req, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db); ArgumentNullException.ThrowIfNull(req);
    if (string.IsNullOrWhiteSpace(req.ManagementResponse)) return CommandResult<FindingResponseResult>.Fail(ErrorCodes.AuditPlanning.Invalid,
      "The management response must be recorded verbatim before it can be filed.");
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var locked = await LockFindingCommandAsync(db, actor, req.FindingId, FindingCommandActions.RecordResponse, ct);
    if (!locked.Succeeded) return CommandResult<FindingResponseResult>.Fail(locked.ErrorCode!, locked.Message!);
    var target = locked.Value!; var next = target.Revision + 1;
    await PrepareFindingPublicationAsync(db, actor, FindingCommandActions.RecordResponse, ct);
    var status = req.Corrected ? FindingStatuses.Corrected : FindingStatuses.Evaluated;
    var changed = await db.Findings.Where(x => x.Id == target.Id && x.FirmId == actor.FirmId && x.ClientId == target.ClientId &&
      x.EngagementId == target.EngagementId && x.Revision == target.Revision).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.ManagementResponse, req.ManagementResponse.Trim()).SetProperty(x => x.Corrected, req.Corrected)
      .SetProperty(x => x.Status, status).SetProperty(x => x.Revision, next), ct);
    if (changed != 1) return CommandResult<FindingResponseResult>.Fail(ErrorCodes.StaleRevision, "The finding changed before its response was recorded.");
    await db.SaveChangesAsync(ct);
    var auth = await FindingCommandWorkspace.AuthorizeAsync(db, actor, target, FindingCommandActions.RecordResponse, true, ct);
    if (!auth.Succeeded) return CommandResult<FindingResponseResult>.Fail(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<FindingResponseResult>.Ok(new(target.Id, status, req.Corrected));
  }

  public static async Task<CommandResult<ManagementLetterDesignationResult>> DesignateManagementLetterFindingAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordManagementLetterDesignationRequest req, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db); ArgumentNullException.ThrowIfNull(req);
    var recommendation = req.Recommendation?.Trim() ?? "";
    if (req.Designated && recommendation.Length is 0 or > 2000) return CommandResult<ManagementLetterDesignationResult>.Fail(ErrorCodes.AuditPlanning.Invalid,
      "A designation requires the exact recommendation that the client-facing letter will render.");
    var action = req.Designated ? FindingCommandActions.Designate : FindingCommandActions.Undesignate;
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var locked = await LockFindingCommandAsync(db, actor, req.FindingId, action, ct);
    if (!locked.Succeeded) return CommandResult<ManagementLetterDesignationResult>.Fail(locked.ErrorCode!, locked.Message!);
    var target = locked.Value!; var next = target.Revision + 1;
    await PrepareFindingPublicationAsync(db, actor, action, ct);
    DateTimeOffset? designatedAt = req.Designated ? DateTimeOffset.UtcNow : null;
    Guid? designatedBy = req.Designated ? actor.UserId : null;
    string? stored = req.Designated ? recommendation : null;
    var changed = await db.Findings.Where(x => x.Id == target.Id && x.FirmId == actor.FirmId && x.ClientId == target.ClientId &&
      x.EngagementId == target.EngagementId && x.Revision == target.Revision).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.LetterDesignatedAt, designatedAt).SetProperty(x => x.LetterDesignatedByUserId, designatedBy)
      .SetProperty(x => x.LetterRecommendation, stored).SetProperty(x => x.Revision, next), ct);
    if (changed != 1) return CommandResult<ManagementLetterDesignationResult>.Fail(ErrorCodes.StaleRevision, "The finding changed before its designation was recorded.");
    await db.SaveChangesAsync(ct);
    var auth = await FindingCommandWorkspace.AuthorizeAsync(db, actor, target, action, true, ct);
    if (!auth.Succeeded) return CommandResult<ManagementLetterDesignationResult>.Fail(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<ManagementLetterDesignationResult>.Ok(new(target.Id, req.Designated, designatedAt, stored));
  }

  private static async Task<CommandResult<Finding>> LockFindingCommandAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, string action, CancellationToken ct)
  {
    var snapshot = await db.Findings.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == id, ct);
    if (snapshot is null) return CommandResult<Finding>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var initial = await FindingCommandWorkspace.AuthorizeAsync(db, actor, snapshot, action, false, ct);
    if (!initial.Succeeded) return CommandResult<Finding>.Fail(initial.ErrorCode!, initial.Message!);
    var publication = await AuditPlanningCommandWorkspace.LockAsync(db, actor, snapshot.EngagementId, true, ct);
    if (!publication.Succeeded) return CommandResult<Finding>.Fail(publication.ErrorCode!, publication.Message!);
    var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
    if (scope.Denied is not null) return CommandResult<Finding>.Fail(scope.Denied, scope.Message);
    // The role decision is exact and current after the shared role-administration guards and engagement lock.
    var auth = await FindingCommandWorkspace.AuthorizeAsync(db, actor, snapshot, action, true, ct);
    if (!auth.Succeeded) return CommandResult<Finding>.Fail(auth.ErrorCode!, auth.Message!);
    var writable = await FileFreezeService.RequireWritableAsync(db, actor, snapshot.EngagementId, "amend an audit finding", ct);
    if (!writable.Succeeded) return CommandResult<Finding>.Fail(writable.ErrorCode!, writable.Message!);
    var target = await db.Findings.FromSqlInterpolated($"SELECT * FROM findings WHERE firm_id={actor.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (target is null || target.ClientId != snapshot.ClientId || target.EngagementId != snapshot.EngagementId)
      return CommandResult<Finding>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (target.Revision == long.MaxValue) return CommandResult<Finding>.Fail(ErrorCodes.ProtectedState, "The finding revision limit has been reached.");
    return CommandResult<Finding>.Ok(target);
  }
  private static async Task PrepareFindingPublicationAsync(IAuditSphereDbContext db, ActorContext actor, string action, CancellationToken ct)
  {
    var user = actor.UserId.ToString(); var epoch = actor.SessionEpoch.ToString(CultureInfo.InvariantCulture); var eventId = Guid.CreateVersion7().ToString();
    // Transaction-local context is consumed by the database transition trigger; it never accepts a browser actor.
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('audit.finding_actor_id',{user},true), set_config('audit.finding_actor_epoch',{epoch},true), set_config('audit.finding_action',{action},true), set_config('audit.finding_event_id',{eventId},true)", ct);
  }
}
