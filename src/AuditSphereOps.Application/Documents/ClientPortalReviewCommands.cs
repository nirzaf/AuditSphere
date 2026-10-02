using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>Client review writes share final-release locks; the underlying deliverable service owns its evidence and version checks.</summary>
public static class ClientPortalReviewCommands
{
  public static async Task<CommandResult<Guid>> CommentAsync(IAuditSphereDbContext db, ActorContext actor, Guid reviewId, string body, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var gate = await RequireWritableReviewAsync(db, actor, reviewId, ct);
    if (!gate.Succeeded) return CommandResult<Guid>.Fail(gate.ErrorCode!, gate.Message!);
    var result = await AuditDeliverableService.CommentAsync(db, actor, reviewId, body, ct);
    if (result.Succeeded) await tx.CommitAsync(ct);
    return result;
  }
  public static async Task<CommandResult> AcknowledgeAsync(IAuditSphereDbContext db, ActorContext actor, Guid reviewId, string sha256, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var gate = await RequireWritableReviewAsync(db, actor, reviewId, ct);
    if (!gate.Succeeded) return gate;
    var result = await AuditDeliverableService.AcknowledgeAsync(db, actor, reviewId, sha256, ct);
    if (result.Succeeded) await tx.CommitAsync(ct);
    return result;
  }
  private static async Task<CommandResult> RequireWritableReviewAsync(IAuditSphereDbContext db, ActorContext actor, Guid reviewId, CancellationToken ct)
  {
    if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" && !x.Disabled, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Review unavailable.");
    var review = await db.ClientDeliverableReviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reviewId && x.FirmId == actor.FirmId, ct);
    if (review is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Review unavailable.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, review.ClientId, review.EngagementId, ["ClientUser"]), ct);
    if (!auth.Succeeded) return auth;
    _ = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    _ = await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE id = {review.ClientId} AND firm_id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    _ = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE id = {review.EngagementId} AND firm_id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    return await ClientPortalService.RequireUploadWindowAsync(db, actor, review.EngagementId, ct);
  }
}
