using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditPlanningService
{
  // Existing in-process callers retain an idempotent compatibility signature. The observed revision is captured
  // before locking, so a concurrent disposition cannot be silently overwritten. HTTP callers must supply a revision.
  public static Task<CommandResult> SetReviewPointDispositionAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid reviewPointId, bool cleared, CancellationToken ct = default) =>
    SetReviewPointDispositionCoreAsync(db, actor, reviewPointId, cleared, null, false, ct);

  public static Task<CommandResult> SetReviewPointDispositionAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid reviewPointId, bool cleared, long expectedDispositionRevision, CancellationToken ct = default) =>
    SetReviewPointDispositionCoreAsync(db, actor, reviewPointId, cleared, expectedDispositionRevision, false, ct);

  internal static async Task<CommandResult> SetReviewPointDispositionCoreAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid id, bool cleared, long? expectedRevision, bool publishNoOp, CancellationToken ct)
  {
    if (expectedRevision is <= 0) return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid,"Provide the exact disposition revision.");
    var snapshot = await db.ReviewPoints.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == id, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    var initial=await ReviewPointCommandWorkspace.AuthorizeAsync(db,actor,snapshot,ct);
    if(!initial.Succeeded)return initial;
    var expected = expectedRevision ?? snapshot.DispositionRevision;
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var publication = await AuditPlanningCommandWorkspace.LockAsync(db,actor,snapshot.EngagementId,true,ct);
    if (!publication.Succeeded) return publication;
    var scope = await LockedEngagementAsync(db,actor,snapshot.EngagementId,ct,snapshot.ClientId,requireProfessionalWork:false);
    if (scope.Denied is not null) return CommandResult.Fail(scope.Denied,scope.Message);
    var auth = await ReviewPointCommandWorkspace.AuthorizeAsync(db,actor,snapshot,ct);
    if (!auth.Succeeded) return auth;
    var current = await db.ReviewPoints.FromSqlInterpolated($"SELECT * FROM review_points WHERE firm_id={actor.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (current is null || current.ClientId != snapshot.ClientId || current.EngagementId != snapshot.EngagementId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    if (current.DispositionRevision != expected) return CommandResult.Fail(ErrorCodes.StaleRevision,"The review point disposition changed; refresh before deciding again.");
    // Same-value compatibility retries are true no-ops, including frozen archives; they cannot reopen or overwrite anything.
    if (!publishNoOp && current.Cleared == cleared)
    { if(tx is not null)await tx.CommitAsync(ct);return CommandResult.Ok(); }
    if (current.DispositionRevision == long.MaxValue) return CommandResult.Fail(ErrorCodes.ProtectedState,"The disposition revision limit has been reached.");
    var writable=await FileFreezeService.RequireWritableAsync(db,actor,current.EngagementId,"change review point disposition",ct);
    if(!writable.Succeeded)return writable;
    var actorId=actor.UserId.ToString();var epoch=actor.SessionEpoch.ToString(CultureInfo.InvariantCulture);var eventId=Guid.CreateVersion7().ToString();
    var action=cleared?ReviewPointCommandActions.Clear:ReviewPointCommandActions.Reopen;
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('audit.review_point_actor_id',{actorId},true), set_config('audit.review_point_actor_epoch',{epoch},true), set_config('audit.review_point_action',{action},true), set_config('audit.review_point_event_id',{eventId},true)",ct);
    var next=expected+1;
    var count=await db.ReviewPoints.Where(x=>x.Id==id && x.FirmId==actor.FirmId && x.ClientId==current.ClientId && x.EngagementId==current.EngagementId && x.DispositionRevision==expected)
      .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Cleared,cleared).SetProperty(x=>x.DispositionRevision,next),ct);
    if(count!=1)return CommandResult.Fail(ErrorCodes.StaleRevision,"The review point disposition changed before publication.");
    await db.SaveChangesAsync(ct);
    auth=await ReviewPointCommandWorkspace.AuthorizeAsync(db,actor,current,ct);if(!auth.Succeeded)return auth;
    if(tx is not null)await tx.CommitAsync(ct);return CommandResult.Ok();
  }
}
