using System.Data;
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
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<EngagementCommunicationTimeline>.Fail(ErrorCodes.GateBlocked,
        "The communication timeline requires an independent read snapshot.");

    Guid clientId;
    int total;
    List<EngagementCommunicationEntry> newest;
    {
      // Source counts and bounded rows must describe one committed population. Dispose this transaction before the
      // final authorization read so a grant revoked during the snapshot cannot authorize the returned entries.
      await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
      var engagement = await db.Engagements.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Id == engagementId)
        .Select(x => new { x.PracticeClientId }).SingleOrDefaultAsync(ct);
      // An unknown engagement and a denied one are indistinguishable to the caller.
      if (engagement is null) return CommandResult<EngagementCommunicationTimeline>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      clientId = engagement.PracticeClientId;
      var auth = await AuthorizeAsync(db, actor, clientId, engagementId, ct);
      if (!auth.Succeeded) return CommandResult<EngagementCommunicationTimeline>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

      var firmId = actor.FirmId;
      var pbcQuery = db.PbcCommunications.AsNoTracking()
        .Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
          x.Kind == PbcCommunicationKinds.Email);
      var pbcCount = await pbcQuery.CountAsync(ct);
      var pbc = await pbcQuery.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(MaxEntries)
        .Select(x => new { x.Id, x.Subject, x.RecipientEmail, x.DeliveryState, x.CreatedAt }).ToListAsync(ct);

      var routedQuery = db.CorrespondenceDispatchRecords.AsNoTracking()
        .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.EngagementId == engagementId);
      var routedCount = await routedQuery.CountAsync(ct);
      var routed = await routedQuery.OrderByDescending(x => x.DispatchedAt).ThenBy(x => x.Id).Take(MaxEntries)
        .Select(x => new { x.Id, x.DocumentType, x.DocumentReference, x.RecipientName, x.RecipientEmail, x.DispatchedAt }).ToListAsync(ct);

      // Commercial notifications carry no engagement id. Resolve their source relationship in SQL and require its
      // firm, client and engagement identity; optional notification client identity may be absent on prospect mail.
      // Voucher mail binds through its exact retained voucher, receipt, fee milestone and agreement; the unique receipt
      // indexes on notification and voucher prevent ambiguous receipt links.
      var commercialQuery = db.CommercialNotifications.AsNoTracking()
        .Where(x => x.FirmId == firmId && (x.PracticeClientId == null || x.PracticeClientId == clientId) && (
          (x.ProposalId != null && db.EngagementFeeAgreements.Any(a => a.FirmId == firmId &&
            a.PracticeClientId == clientId && a.EngagementId == engagementId && a.ProposalId == x.ProposalId)) ||
          (x.FeeMilestoneId != null && db.FeeMilestones.Any(m => m.FirmId == firmId && m.Id == x.FeeMilestoneId &&
            db.EngagementFeeAgreements.Any(a => a.FirmId == firmId && a.Id == m.AgreementId &&
              a.PracticeClientId == clientId && a.EngagementId == engagementId))) ||
          (x.Kind == CommercialNotificationKinds.PaymentVoucher && x.PracticeClientId == clientId &&
            x.ReceiptId != null && x.DocumentId != null &&
            db.CommercialDocuments.Any(document => document.FirmId == firmId && document.Id == x.DocumentId &&
              document.Kind == CommercialDocumentKinds.PaymentVoucher && document.ReceiptId == x.ReceiptId &&
              document.ProposalId != null && document.QuotationVersionId != null && document.FeeMilestoneId != null &&
              db.FeeMilestones.Any(milestone => milestone.FirmId == firmId && milestone.Id == document.FeeMilestoneId &&
                db.EngagementFeeAgreements.Any(agreement => agreement.FirmId == firmId &&
                  agreement.Id == milestone.AgreementId && agreement.PracticeClientId == clientId &&
                  agreement.EngagementId == engagementId && agreement.ProposalId == document.ProposalId &&
                  agreement.QuotationVersionId == document.QuotationVersionId)))) ||
          (x.DeliverableId != null && db.AuditDeliverables.Any(d => d.FirmId == firmId && d.ClientId == clientId &&
            d.Id == x.DeliverableId && d.EngagementId == engagementId))));
      var commercialCount = await commercialQuery.CountAsync(ct);
      var commercial = await commercialQuery.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(MaxEntries)
        .Select(x => new { x.Id, x.Kind, x.Subject, x.Recipient, x.DeliveryState, x.CreatedAt, x.DeliveredAt, x.DeliverableId })
        .ToListAsync(ct);

      var entries = new List<EngagementCommunicationEntry>(MaxEntries * 3);
      entries.AddRange(pbc.Select(x => new EngagementCommunicationEntry(x.Id, "PBC", "PBC request email", x.Subject ?? string.Empty,
        x.RecipientEmail ?? string.Empty, PbcStatus(x.DeliveryState), x.CreatedAt, null,
        $"/app/engagements/{engagementId}/pbc")));
      entries.AddRange(routed.Select(x => new EngagementCommunicationEntry(x.Id, "CORRESPONDENCE", x.DocumentType, x.DocumentReference,
        $"{x.RecipientName} <{x.RecipientEmail}>", EngagementCommunicationStatuses.Recorded, x.DispatchedAt, null,
        $"/app/clients/{clientId}")));
      entries.AddRange(commercial.Select(x => new EngagementCommunicationEntry(x.Id, "COMMERCIAL", x.Kind, x.Subject, x.Recipient,
        CommercialStatus(x.DeliveryState), x.CreatedAt,
        x.DeliveryState == CommercialDeliveryStates.Delivered ? x.DeliveredAt : null,
        x.DeliverableId is null ? $"/app/clients/{clientId}" : $"/app/engagements/{engagementId}/completion")));

      total = checked(pbcCount + routedCount + commercialCount);
      newest = entries.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id)
        .ThenBy(x => x.Source, StringComparer.Ordinal).Take(MaxEntries).ToList();
      await snapshot.CommitAsync(ct);
    }

    // Recheck the exact firm/client/engagement relationship after the snapshot closes, then check current authority.
    var finalScope = await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == engagementId)
      .Select(x => new
      {
        x.PracticeClientId,
        ClientExists = db.PracticeClients.Any(c => c.FirmId == actor.FirmId && c.Id == x.PracticeClientId)
      }).SingleOrDefaultAsync(ct);
    if (finalScope is null || finalScope.PracticeClientId != clientId || !finalScope.ClientExists)
      return CommandResult<EngagementCommunicationTimeline>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var finalAuth = await AuthorizeAsync(db, actor, clientId, engagementId, ct);
    return finalAuth.Succeeded
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
