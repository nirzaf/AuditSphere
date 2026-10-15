using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

/// <summary>
/// One outbound communication the firm already recorded for an engagement. Status is the recorded fact only: a queued or
/// dispatched send is not delivery, an uncertain outcome stays unknown, and a routing record with no provider receipt is shown
/// as recorded. The message body is never projected.
/// </summary>
public sealed record EngagementCommunicationEntry(
  Guid Id, string Source, string Kind, string Subject, string Recipient, string Status,
  DateTimeOffset RecordedAtUtc, DateTimeOffset? DeliveredAtUtc, string SourceLink);

public sealed record EngagementCommunicationTimeline(
  Guid EngagementId, int TotalCount, IReadOnlyList<EngagementCommunicationEntry> Entries);

public static class EngagementCommunicationStatuses
{
  public const string Queued = "QUEUED";
  public const string Dispatched = "DISPATCHED";
  public const string ProviderAccepted = "PROVIDER_ACCEPTED";
  public const string Delivered = "DELIVERED";
  public const string Failed = "FAILED";
  public const string Unknown = "UNKNOWN";
  /// <summary>A dispatch routing record exists; it carries no provider outcome.</summary>
  public const string Recorded = "RECORDED";
}

/// <summary>
/// Read-only engagement communication timeline (PM-07, first slice). It gathers the outbound mail the platform already
/// records: PBC request emails, routed document dispatches, and commercial notifications tied to this engagement through its
/// fee agreement, its fee milestones or its audit deliverables. It changes nothing and is not evidence that a recipient read
/// a message.
/// </summary>
public static partial class EngagementCommunicationTimelineQuery
{
  public const int MaxEntries = 200;
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager"];

  public static async Task<CommandResult<EngagementCommunicationTimeline>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == engagementId)
      .Select(x => new { x.Id, x.PracticeClientId }).SingleOrDefaultAsync(ct);
    // An unknown engagement and a denied one are indistinguishable to the caller.
    if (engagement is null) return CommandResult<EngagementCommunicationTimeline>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, engagement.PracticeClientId, engagementId, ct);
    if (!auth.Succeeded) return CommandResult<EngagementCommunicationTimeline>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var firmId = actor.FirmId;
    var entries = new List<EngagementCommunicationEntry>();

    var pbc = await db.PbcCommunications.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.EngagementId == engagementId && x.Kind == PbcCommunicationKinds.Email)
      .OrderByDescending(x => x.CreatedAt).Take(MaxEntries)
      .Select(x => new { x.Id, x.Subject, x.RecipientEmail, x.DeliveryState, x.CreatedAt, x.DeliveredAt }).ToListAsync(ct);
    entries.AddRange(pbc.Select(x => new EngagementCommunicationEntry(x.Id, "PBC", "PBC request email", x.Subject ?? string.Empty,
      x.RecipientEmail ?? string.Empty, PbcStatus(x.DeliveryState), x.CreatedAt, x.DeliveredAt, $"/app/engagements/{engagementId}/pbc")));

    var routed = await db.CorrespondenceDispatchRecords.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.DispatchedAt).Take(MaxEntries)
      .Select(x => new { x.Id, x.DocumentType, x.DocumentReference, x.RecipientName, x.RecipientEmail, x.DispatchedAt }).ToListAsync(ct);
    entries.AddRange(routed.Select(x => new EngagementCommunicationEntry(x.Id, "CORRESPONDENCE", x.DocumentType, x.DocumentReference,
      $"{x.RecipientName} <{x.RecipientEmail}>", EngagementCommunicationStatuses.Recorded, x.DispatchedAt, null,
      $"/app/clients/{engagement.PracticeClientId}")));

    // Commercial notifications carry no engagement id. They belong to this engagement only through a record that does.
    var agreements = await db.EngagementFeeAgreements.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .Select(x => new { x.Id, x.ProposalId }).ToListAsync(ct);
    var agreementIds = agreements.Select(x => x.Id).ToArray();
    var proposalIds = agreements.Select(x => (Guid?)x.ProposalId).ToArray();
    var milestoneIds = agreementIds.Length == 0 ? [] : await db.FeeMilestones.AsNoTracking()
      .Where(x => x.FirmId == firmId && agreementIds.Contains(x.AgreementId)).Select(x => (Guid?)x.Id).ToArrayAsync(ct);
    var commercial = await db.CommercialNotifications.AsNoTracking()
      .Where(x => x.FirmId == firmId && (
        (x.ProposalId != null && proposalIds.Contains(x.ProposalId)) ||
        (x.FeeMilestoneId != null && milestoneIds.Contains(x.FeeMilestoneId)) ||
        (x.DeliverableId != null && db.AuditDeliverables.Any(d => d.FirmId == firmId && d.Id == x.DeliverableId && d.EngagementId == engagementId))))
      .OrderByDescending(x => x.CreatedAt).Take(MaxEntries)
      .Select(x => new { x.Id, x.Kind, x.Subject, x.Recipient, x.DeliveryState, x.CreatedAt, x.DeliveredAt, x.DeliverableId }).ToListAsync(ct);
    entries.AddRange(commercial.Select(x => new EngagementCommunicationEntry(x.Id, "COMMERCIAL", x.Kind, x.Subject, x.Recipient,
      CommercialStatus(x.DeliveryState), x.CreatedAt, x.DeliveredAt,
      x.DeliverableId is null ? $"/app/clients/{engagement.PracticeClientId}" : $"/app/engagements/{engagementId}/completion")));

    var total = entries.Count;
    var newest = entries.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).Take(MaxEntries).ToList();

    // The grant may have been revoked while the rows were read; never return them to a caller who has lost access.
    auth = await AuthorizeAsync(db, actor, engagement.PracticeClientId, engagementId, ct);
    return auth.Succeeded
      ? CommandResult<EngagementCommunicationTimeline>.Ok(new(engagementId, total, newest))
      : CommandResult<EngagementCommunicationTimeline>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }

  private static string PbcStatus(string? state) => state switch
  {
    PbcDeliveryStates.Queued => EngagementCommunicationStatuses.Queued,
    // A PBC send is recorded when the mail provider accepts it. That is not proof of delivery to the inbox.
    PbcDeliveryStates.Sent => EngagementCommunicationStatuses.ProviderAccepted,
    PbcDeliveryStates.Failed => EngagementCommunicationStatuses.Failed,
    _ => EngagementCommunicationStatuses.Unknown,
  };

  private static string CommercialStatus(string state) => state switch
  {
    CommercialDeliveryStates.Queued => EngagementCommunicationStatuses.Queued,
    CommercialDeliveryStates.Dispatched => EngagementCommunicationStatuses.Dispatched,
    CommercialDeliveryStates.ProviderAccepted => EngagementCommunicationStatuses.ProviderAccepted,
    CommercialDeliveryStates.Delivered => EngagementCommunicationStatuses.Delivered,
    CommercialDeliveryStates.Rejected => EngagementCommunicationStatuses.Failed,
    _ => EngagementCommunicationStatuses.Unknown,
  };

  private static Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: clientId, EngagementId: engagementId, RequiredRoles: Roles, InternalOnly: true), ct);
}
