using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Records;

public static class ActivityKinds
{
  public const string Upload = "UPLOAD";
  public const string Edit = "EDIT";
  public const string Comment = "COMMENT";
  public const string SignOff = "SIGN_OFF";
  public const string Denied = "DENIED";
  public const string Freeze = "FREEZE";
}

public sealed record ActivityEvent(DateTimeOffset At, string Kind, string Actor, string Description, Guid EntityId);

public sealed record DocumentLockView(Guid Id, string DocumentKey, string LockedBy, DateTimeOffset LockedAt);

/// <summary>
/// One attributable, time-ordered trail per engagement over the application's own records: uploads, edits
/// (submitted revisions), comments (PBC messages, review notes and client comments), sign-offs (reviews, approvals,
/// clearances, opinions, signatures), freeze events and refused writes. Edits made directly in SharePoint are not
/// observed by the application and are reported as not covered, rather than implied.
/// </summary>
public static class EngagementActivityQuery
{
  public const string ExternalCoverageNote = "Direct SharePoint edits are not observed by AuditSphere; capturing them needs Graph change tracking in an authorized tenant (BLOCKED_EXTERNAL).";

  public static async Task<CommandResult<IReadOnlyList<ActivityEvent>>> GetAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<IReadOnlyList<ActivityEvent>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId,
      ["Manager", "Partner", "Administrator", "Reviewer"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<ActivityEvent>>.Fail(auth.ErrorCode!, auth.Message!);
    var f = actor.FirmId;
    var events = new List<(DateTimeOffset At, string Kind, Guid Actor, string Description, Guid Id)>();
    events.AddRange((await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.CreatedAt, ActivityKinds.Upload, x.UploaderUserId, $"Upload of {x.FileName} ({x.State.ToLowerInvariant()})", x.Id)));
    events.AddRange((await db.PbcCommunications.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.CreatedAt, ActivityKinds.Comment, x.AuthorUserId, $"PBC {x.Kind.ToLowerInvariant().Replace('_', ' ')}", x.Id)));
    events.AddRange((await db.AuditProcedureResults.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.SubmittedAt, ActivityKinds.Edit, x.PreparedByUserId, $"Procedure result revision {x.Revision} submitted", x.Id)));
    events.AddRange((await db.AuditProcedureReviews.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.CreatedAt, ActivityKinds.SignOff, x.ReviewerUserId, $"Procedure review: {x.Decision.ToLowerInvariant().Replace('_', ' ')} (revision {x.ResultRevision})", x.Id)));
    events.AddRange((await db.ProcedureReviewNotes.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.CreatedAt, ActivityKinds.Comment, x.AuthorUserId, $"Review note on “{x.Excerpt}”", x.Id)));
    events.AddRange((await db.MaterialityApprovals.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.ApprovedAt, ActivityKinds.SignOff, x.ApprovedByUserId, "Materiality approved", x.Id)));
    events.AddRange((await db.RiskPartnerClearances.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.ClearedAt, ActivityKinds.SignOff, x.PartnerUserId, "Partner review of a red risk", x.Id)));
    events.AddRange((await db.AuditDeliverables.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId)
        .Select(x => new { x.Id, x.CreatedAt, x.CreatedByUserId, x.Kind, x.Version, x.SignedFromDeliverableId }).ToListAsync(ct))
      .Select(x => (x.CreatedAt, x.SignedFromDeliverableId is null ? ActivityKinds.Edit : ActivityKinds.SignOff, x.CreatedByUserId,
        $"{AuditSphereOps.Domain.Completion.DeliverableKinds.Title(x.Kind)} v{x.Version} {(x.SignedFromDeliverableId is null ? "generated" : "signed")}", x.Id)));
    events.AddRange((await db.PartnerCompletionClearances.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.ClearedAt, ActivityKinds.SignOff, x.PartnerUserId, "Partner completion clearance", x.Id)));
    events.AddRange((await db.AuditOpinionDecisions.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.DecidedAt, ActivityKinds.SignOff, x.DecidedByUserId, $"Opinion recorded: {x.OpinionType.ToLowerInvariant()}", x.Id)));
    var reviewIds = await db.ClientDeliverableReviews.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).Select(x => x.Id).ToListAsync(ct);
    events.AddRange((await db.ClientDeliverableComments.AsNoTracking().Where(x => reviewIds.Contains(x.ReviewId)).ToListAsync(ct))
      .Select(x => (x.CreatedAt, ActivityKinds.Comment, x.AuthorUserId, x.FromClient ? "Client comment on a shared deliverable" : "Auditor comment on a shared deliverable", x.Id)));
    events.AddRange((await db.ClientDeliverableReviews.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId && x.AcknowledgedAt != null).ToListAsync(ct))
      .Select(x => (x.AcknowledgedAt!.Value, ActivityKinds.SignOff, x.AcknowledgedByUserId!.Value, "Client acknowledged a shared deliverable", x.Id)));
    var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == f && x.EngagementId == engagementId, ct);
    if (freeze?.FrozenAt is { } frozenAt) events.Add((frozenAt, ActivityKinds.Freeze, Guid.Empty, "File frozen 60 days after the signed report", freeze.Id));
    events.AddRange((await db.FileFreezeAmendments.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId && x.OpenedAt != null).ToListAsync(ct))
      .Select(x => (x.OpenedAt!.Value, ActivityKinds.Freeze, x.ApprovedByUserId!.Value, $"Amendment opened: {x.Reason}", x.Id)));
    events.AddRange((await db.FrozenAccessAttempts.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId == engagementId).ToListAsync(ct))
      .Select(x => (x.AttemptedAt, ActivityKinds.Denied, x.ActorUserId, $"Refused while frozen: {x.Action}", x.Id)));
    var actorIds = events.Select(x => x.Actor).Distinct().ToArray();
    var names = await db.Users.AsNoTracking().Where(x => actorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    return CommandResult<IReadOnlyList<ActivityEvent>>.Ok(events.OrderByDescending(x => x.At)
      .Select(x => new ActivityEvent(x.At, x.Kind, x.Actor == Guid.Empty ? "System (scheduled)" : names.GetValueOrDefault(x.Actor, "Unknown user"), x.Description, x.Id)).ToList());
  }

  // ── Document locks (OV-02, application side) ─────────────────────────────────────────────────────

  public static async Task<CommandResult<Guid>> LockAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string documentKey, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(documentKey) || documentKey.Length > 200) return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "A document is required.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId,
      ["Staff", "Senior", "Manager", "Partner", "Administrator"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var writable = await FileFreezeService.RequireWritableAsync(db, actor, engagementId, $"lock {documentKey.Trim()}", ct);
    if (!writable.Succeeded) return CommandResult<Guid>.Fail(writable.ErrorCode!, writable.Message!);
    var existing = await db.DocumentLocks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.DocumentKey == documentKey.Trim() && x.ReleasedAt == null, ct);
    if (existing is not null)
      return existing.LockedByUserId == actor.UserId ? CommandResult<Guid>.Ok(existing.Id)
        : CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Another user holds the lock on this document.");
    var @lock = new DocumentLock { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, EngagementId = engagementId, DocumentKey = documentKey.Trim(), LockedByUserId = actor.UserId, LockedAt = DateTimeOffset.UtcNow };
    db.DocumentLocks.Add(@lock);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Another user holds the lock on this document."); }
    return CommandResult<Guid>.Ok(@lock.Id);
  }

  /// <summary>The holder releases; a Manager or Partner can break another user's lock.</summary>
  public static async Task<CommandResult> UnlockAsync(IAuditSphereDbContext db, ActorContext actor, Guid lockId, CancellationToken ct = default)
  {
    var @lock = await db.DocumentLocks.SingleOrDefaultAsync(x => x.Id == lockId && x.FirmId == actor.FirmId, ct);
    if (@lock is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == @lock.EngagementId, ct);
    var roles = @lock.LockedByUserId == actor.UserId ? new[] { "Staff", "Senior", "Manager", "Partner", "Administrator" } : ["Manager", "Partner", "Administrator"];
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, @lock.EngagementId, roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    if (@lock.ReleasedAt is not null) return CommandResult.Ok();
    @lock.ReleasedAt = DateTimeOffset.UtcNow;
    @lock.ReleasedByUserId = actor.UserId;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<IReadOnlyList<DocumentLockView>> LocksAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default) =>
    await db.DocumentLocks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.ReleasedAt == null)
      .Join(db.Users.AsNoTracking(), l => l.LockedByUserId, u => u.Id, (l, u) => new DocumentLockView(l.Id, l.DocumentKey, u.DisplayName, l.LockedAt)).ToListAsync(ct);
}
