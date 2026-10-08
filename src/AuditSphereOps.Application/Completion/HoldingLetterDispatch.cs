using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

/// <summary>
/// STE 3.3 automatic Holding Letter dispatch for an outstanding critical confirmation. The recipient is resolved only from an
/// active COMPLETION contact routing: a missing routing blocks dispatch with an actionable message and never selects an
/// arbitrary address. Dispatch queues one notification per exact letter identity, so an unchanged blocker set cannot send
/// twice. A queued notification is reported as queued; delivery is recorded separately by the mail worker.
/// </summary>
internal static class HoldingLetterDispatch
{
  public const string NotDispatched = "NOT_DISPATCHED";

  public static async Task<(string State, string Message)> QueueAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId,
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
        "Holding letter not sent: no active Completion contact routing is configured for this client. Configure the client-management recipient in CRM routing, then generate the report again to dispatch.");

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
