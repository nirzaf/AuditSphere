using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

/// <summary>The holding letter for the engagement's current critical-confirmation blockers and its dispatch state.</summary>
public sealed record HoldingLetterStatus(Guid? DeliverableId, DateTimeOffset? IssuedAt, int OutstandingCount, string State, string Message);

/// <summary>
/// STE 3.3 automatic Holding Letter dispatch for an outstanding critical confirmation. The recipient is resolved only from an
/// active COMPLETION contact routing: a missing routing blocks dispatch with an actionable message and never selects an
/// arbitrary address. Dispatch queues one notification per exact outstanding set, so an unchanged blocker set cannot send
/// twice. A queued notification is reported as queued; delivery is recorded separately by the mail worker.
/// </summary>
public static class HoldingLetterDispatch
{
  public const string NotDispatched = "NOT_DISPATCHED";
  private static readonly string[] DispatchRoles = ["Partner", "Manager", "SeniorManager"];

  /// <summary>Reads the latest holding letter against the current blockers, and its dispatch state when one exists.</summary>
  public static async Task<HoldingLetterStatus> StatusAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct = default)
  {
    var (digest, count) = await AuditDeliverableService.CurrentOutstandingCriticalAsync(db, firmId, engagementId, ct);
    var letter = await db.AuditDeliverables.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.EngagementId == engagementId && x.Kind == DeliverableKinds.HoldingLetter)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Select(x => new { x.Id, x.InputDigest, x.CreatedAt, x.EngagementId })
      .FirstOrDefaultAsync(ct);
    if (letter is null)
      return count == 0
        ? new(null, null, 0, "NONE", "No critical confirmation is outstanding, so no holding letter is needed.")
        : new(null, null, count, "NOT_GENERATED", $"{count} critical confirmation(s) are outstanding. Generate the report to issue the holding letter.");
    if (count == 0)
      return new(letter.Id, letter.CreatedAt, 0, "RESOLVED", "The critical confirmations are returned and evaluated; the holding letter no longer blocks the report.");
    if (!string.Equals(letter.InputDigest, digest, StringComparison.Ordinal))
      return new(letter.Id, letter.CreatedAt, count, "SUPERSEDED",
        "The outstanding confirmations changed after this holding letter. Generate the report again to issue a current holding letter before dispatch.");

    var notification = await db.CommercialNotifications.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.DispatchKey == letter.InputDigest, ct);
    if (notification is not null)
      return notification.DeliveryState switch
      {
        "SENT" => new(letter.Id, letter.CreatedAt, count, "SENT", "The holding letter was delivered to the client-management recipient."),
        "FAILED" => new(letter.Id, letter.CreatedAt, count, "FAILED", "Delivery of the holding letter failed. Resolve the mail provider issue, then dispatch again."),
        _ => new(letter.Id, letter.CreatedAt, count, "QUEUED", "The holding letter is queued for the client-management recipient. Queued is not yet delivered."),
      };

    var clientId = await db.Engagements.AsNoTracking().Where(x => x.Id == engagementId && x.FirmId == firmId)
      .Select(x => x.PracticeClientId).SingleAsync(ct);
    if (await ResolveRecipientAsync(db, firmId, clientId, ct) is null)
      return new(letter.Id, letter.CreatedAt, count, NotDispatched,
        "Holding letter not sent: no active Completion contact routing is configured for this client. Configure the client-management recipient in CRM routing, then dispatch.");
    return new(letter.Id, letter.CreatedAt, count, "READY_TO_DISPATCH", "The holding letter is ready. Dispatch it to the client-management recipient.");
  }

  /// <summary>Explicitly dispatches the current holding letter. Refuses a superseded or resolved set; repeating it never resends.</summary>
  public static async Task<CommandResult<HoldingLetterStatus>> DispatchCurrentAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<HoldingLetterStatus>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, DispatchRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<HoldingLetterStatus>.Fail(auth.ErrorCode!, auth.Message!);

    var status = await StatusAsync(db, actor.FirmId, engagementId, ct);
    if (status.DeliverableId is not { } letterId)
      return CommandResult<HoldingLetterStatus>.Fail(ErrorCodes.GateBlocked, "No holding letter exists. Generate the report first.");
    if (status.State == "SUPERSEDED")
      return CommandResult<HoldingLetterStatus>.Fail(ErrorCodes.GenerationStale, status.Message);
    if (status.State is "NONE" or "RESOLVED")
      return CommandResult<HoldingLetterStatus>.Fail(ErrorCodes.GateBlocked, "No critical confirmation is outstanding, so there is nothing to dispatch.");

    await QueueAsync(db, actor.FirmId, engagementId, letterId, status.OutstandingCount, ct);
    return CommandResult<HoldingLetterStatus>.Ok(await StatusAsync(db, actor.FirmId, engagementId, ct));
  }

  internal static async Task<(string State, string Message)> QueueAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId,
    Guid holdingLetterId, int outstanding, CancellationToken ct)
  {
    // A regenerated letter for the same outstanding set is a new deliverable, so the dispatch key is the blocker-set digest.
    var letter = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == holdingLetterId && x.FirmId == firmId, ct);
    var dispatchKey = letter.InputDigest;
    var existing = await db.CommercialNotifications.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.DispatchKey == dispatchKey, ct);
    if (existing is not null)
      return (existing.DeliveryState, $"The holding letter for this exact outstanding set was already dispatched ({existing.DeliveryState}).");

    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId && x.FirmId == firmId, ct);
    var recipient = await ResolveRecipientAsync(db, firmId, engagement.PracticeClientId, ct);
    if (recipient is null)
      return (NotDispatched,
        "Holding letter not sent: no active Completion contact routing is configured for this client. Configure the client-management recipient in CRM routing, then dispatch.");

    db.CommercialNotifications.Add(new CommercialNotification
    {
      Id = Guid.CreateVersion7(), FirmId = firmId, Kind = CommercialNotificationKinds.HoldingLetter,
      DeliverableId = holdingLetterId, DispatchKey = dispatchKey, PracticeClientId = engagement.PracticeClientId, Recipient = recipient,
      Subject = $"Holding letter: critical confirmations outstanding ({outstanding})",
      Body = $"The Independent Auditor's Report cannot be issued until {outstanding} critical third-party confirmation(s) are received and evaluated.\n\n" +
             $"Holding letter identity: {letter.ContentSha256}",
      DeliveryState = "QUEUED", CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    return ("QUEUED", "The holding letter is queued for the client-management recipient. Queued is not yet delivered.");
  }

  private static async Task<string?> ResolveRecipientAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct)
  {
    var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
    return await (
      from r in db.ClientContactRoutings.AsNoTracking()
      where r.FirmId == firmId && r.PracticeClientId == clientId && r.Purpose == CorrespondencePurposes.Completion && r.RevokedAt == null
      where (r.EffectiveFrom == null || r.EffectiveFrom <= asOf) && (r.EffectiveTo == null || r.EffectiveTo >= asOf)
      join c in db.ClientContacts.AsNoTracking() on r.ClientContactId equals c.Id
      where c.IsActive && !string.IsNullOrWhiteSpace(c.Email)
      orderby r.IsPrimaryForPurpose descending, r.CreatedAt descending
      select c.Email).FirstOrDefaultAsync(ct);
  }
}
