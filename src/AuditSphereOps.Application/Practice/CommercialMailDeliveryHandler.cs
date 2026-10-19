using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>
/// Delivers queued commercial emails (the official advance-payment receipt, proposal dispatches) through the isolated
/// Acceptance "mail" worker and the same provider adapters as PBC mail. The operation is keyed to the notification, so
/// a verified provider receipt is terminal and never sent twice; refused and unknown outcomes are durable receipt
/// states that only an explicit operator re-arm can re-attempt.
/// </summary>
public sealed class CommercialMailDeliveryHandler(IAuditSphereDbContextFactory factory, IPbcMailSender sender) : IOperationHandler
{
  public const string Kind = "SendCommercialMail.v1";
  public OperationDefinition Definition { get; } = new(Kind, OperationMode.LIVE, OperationAuthority.LIVE_PROVIDER, Group: "mail");

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      using var document = JsonDocument.Parse(request.PayloadJson);
      // The client binding is optional: a pre-conversion proposal dispatch has no client yet, but the
      // notification identity itself keys the operation and the send is firm-scoped.
      if (!document.RootElement.TryGetProperty("notificationId", out var id) || !id.TryGetGuid(out var value) ||
          value != request.TargetId || request.ExpectedRevision != 1)
        throw new OperationBlockedException("invalid-mail-request");
      return JsonSerializer.Serialize(new { notificationId = value.ToString("D") });
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("invalid-mail-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    var mail = await db.CommercialNotifications.FromSqlInterpolated($"""
      SELECT * FROM commercial_notifications WHERE firm_id = {op.FirmId} AND id = {op.TargetId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    // A verified provider receipt is terminal: the send is never repeated. A refused or unknown outcome may be
    // re-attempted only through an explicit operator re-arm, which re-enters this lock through the same path.
    if (mail is null || mail.DeliveryState is CommercialDeliveryStates.ProviderAccepted or CommercialDeliveryStates.Delivered ||
      string.IsNullOrWhiteSpace(mail.Recipient) || string.IsNullOrWhiteSpace(mail.Subject))
      throw new OperationBlockedException("mail-scope-or-state-conflict", authorization: true);
  }

  public async Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct)
  {
    var plan = await PrepareSendAsync(op, ct);
    try
    {
      await sender.SendAsync(plan, ct);
    }
    catch (SafeRetryException)
    {
      throw; // transient pre-provider failure: the attempt stays DISPATCHED and the operation retries
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (OperationBlockedException)
    {
      await MarkDeliveryStateAsync(factory, op.FirmId, op.TargetId, CommercialDeliveryStates.Rejected, ct);
      throw; // the provider refused the send; the email was not accepted
    }
    catch (Exception)
    {
      await MarkDeliveryStateAsync(factory, op.FirmId, op.TargetId, CommercialDeliveryStates.Unknown, ct);
      throw; // the outcome is uncertain: reconciliation — never a blind resend — must resolve it
    }
    return Expected(op);
  }

  private async Task<PbcMailPlan> PrepareSendAsync(DurableOperation op, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var firm = await PracticeCrmService.LockFirmAsync(db, op.FirmId, ct);
    if (firm is null || firm.OperatingMode != "LOCAL_ONLY" || firm.DeploymentEpoch != op.ClaimedEpoch)
      throw new OperationBlockedException("mail-firm-authority-changed", authorization: true);
    var mail = await db.CommercialNotifications.AsNoTracking().SingleOrDefaultAsync(
      x => x.FirmId == op.FirmId && x.Id == op.TargetId, ct);
    if (mail is null || CommercialDeliveryStates.IsProviderReceipt(mail.DeliveryState) ||
        string.IsNullOrWhiteSpace(mail.Recipient) || string.IsNullOrWhiteSpace(mail.Subject))
      throw new OperationBlockedException("mail-scope-or-state-conflict", authorization: true);
    PbcMailAttachment? attachment = null;
    if (mail.DocumentId is { } documentId)
    {
      var document = await db.CommercialDocuments.AsNoTracking()
        .SingleOrDefaultAsync(x => x.FirmId == op.FirmId && x.Id == documentId, ct);
      if (document is null || document.Bytes.Length == 0 || string.IsNullOrWhiteSpace(document.FileName) ||
          string.IsNullOrWhiteSpace(document.ContentType) || string.IsNullOrWhiteSpace(mail.OfferSha256) ||
          !string.Equals(document.Sha256Hex, mail.OfferSha256, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(document.Sha256Hex, Hashing.Sha256Hex(document.Bytes), StringComparison.OrdinalIgnoreCase) ||
          mail.Kind == CommercialNotificationKinds.Proposal &&
          (document.Kind != CommercialDocumentKinds.Quotation || document.ProposalId != mail.ProposalId ||
           document.QuotationVersionId != mail.QuotationVersionId))
        throw new OperationBlockedException("commercial-mail-artifact-mismatch");
      attachment = new(document.FileName, document.ContentType, document.Bytes);
    }
    else if (mail.Kind == CommercialNotificationKinds.Proposal)
    {
      throw new OperationBlockedException("commercial-mail-artifact-missing");
    }
    if (mail.Kind == CommercialNotificationKinds.Proposal &&
        !await ProposalRecipientAuthorityIsCurrentAsync(db, mail, ct))
      throw new OperationBlockedException("mail-recipient-authority-changed", authorization: true);
    if (mail.Kind is CommercialNotificationKinds.Receipt or CommercialNotificationKinds.PaymentVoucher &&
        (op.ClientId != mail.PracticeClientId || !await PaymentRecipientAuthorityIsCurrentAsync(db, mail, ct)))
      throw new OperationBlockedException("mail-recipient-authority-changed", authorization: true);

    // This commit is the dispatch-authorization boundary. Contact/routing writers take the
    // same firm fence. Changes committed before it win; later changes do not retarget this
    // already-authorized attempt. No database transaction or lock spans the provider call.
    var changed = await db.CommercialNotifications.Where(x => x.FirmId == op.FirmId && x.Id == mail.Id &&
      x.DeliveryState == mail.DeliveryState).ExecuteUpdateAsync(
        setters => setters.SetProperty(x => x.DeliveryState, CommercialDeliveryStates.Dispatched), ct);
    if (changed != 1) throw new OperationBlockedException("mail-state-changed", authorization: true);
    await tx.CommitAsync(ct);
    return new(mail.Recipient, mail.Subject, mail.Body, op.CorrelationId, attachment);
  }

  private static async Task<bool> PaymentRecipientAuthorityIsCurrentAsync(
    IAuditSphereDbContext db, CommercialNotification mail, CancellationToken ct)
  {
    if (mail.PracticeClientId is not { } clientId || mail.DocumentId is not { } documentId)
      return false;
    var isSettlementReceipt = mail.Kind == CommercialNotificationKinds.Receipt;
    if (isSettlementReceipt ? !mail.FeeMilestoneId.HasValue : !mail.ReceiptId.HasValue)
      return false;
    var expectedDocumentKind = isSettlementReceipt
      ? CommercialDocumentKinds.PaymentReceipt : CommercialDocumentKinds.PaymentVoucher;

    // Resolve only through the retained artifact's own agreement. A notification's client or
    // payment identity cannot borrow another client's financial correspondence authority.
    var agreement = await (
      from document in db.CommercialDocuments.AsNoTracking()
      join milestone in db.FeeMilestones.AsNoTracking() on document.FeeMilestoneId equals (Guid?)milestone.Id
      join candidate in db.EngagementFeeAgreements.AsNoTracking() on milestone.AgreementId equals candidate.Id
      where document.FirmId == mail.FirmId && document.Id == documentId && document.Kind == expectedDocumentKind &&
            milestone.FirmId == mail.FirmId && candidate.FirmId == mail.FirmId && candidate.PracticeClientId == clientId &&
            document.ProposalId == candidate.ProposalId && document.QuotationVersionId == candidate.QuotationVersionId &&
            (isSettlementReceipt ? document.FeeMilestoneId == mail.FeeMilestoneId : document.ReceiptId == mail.ReceiptId)
      select candidate
    ).SingleOrDefaultAsync(ct);
    if (agreement is null) return false;

    // Keep the existing Finance-route / primary-contact / prospect fallback policy. A changed
    // recipient blocks this immutable notification; it is never silently retargeted or rewritten.
    return EmailsMatch(await FeeAgreementService.ResolveRecipientAsync(db, mail.FirmId, agreement, ct), mail.Recipient);
  }

  private static async Task<bool> ProposalRecipientAuthorityIsCurrentAsync(
    IAuditSphereDbContext db, CommercialNotification mail, CancellationToken ct)
  {
    if (mail.ProposalId is not { } proposalId) return false;

    var proposalScope = await (
      from proposal in db.Proposals.AsNoTracking()
      join opportunity in db.Opportunities.AsNoTracking() on proposal.OpportunityId equals opportunity.Id
      where proposal.FirmId == mail.FirmId && proposal.Id == proposalId && opportunity.FirmId == mail.FirmId
      select new { ProposalClientId = proposal.PracticeClientId, OpportunityClientId = opportunity.PracticeClientId }
    ).SingleOrDefaultAsync(ct);
    if (proposalScope is null) return false;

    if (mail.RecipientAuthorityBasis == ProposalRecipientAuthorityBases.ProspectPrimaryContact)
    {
      if (mail.PracticeClientId.HasValue || proposalScope.ProposalClientId.HasValue ||
          proposalScope.OpportunityClientId.HasValue || mail.RecipientContactId.HasValue || mail.RecipientRoutingId.HasValue)
        return false;

      var currentEmail = await (
        from proposal in db.Proposals.AsNoTracking()
        join opportunity in db.Opportunities.AsNoTracking() on proposal.OpportunityId equals opportunity.Id
        join lead in db.Leads.AsNoTracking() on opportunity.LeadId equals lead.Id
        where proposal.FirmId == mail.FirmId && proposal.Id == proposalId && lead.FirmId == mail.FirmId
        select lead.PrimaryContactEmail
      ).SingleOrDefaultAsync(ct);
      return EmailsMatch(currentEmail, mail.Recipient);
    }

    if (mail.RecipientAuthorityBasis != ProposalRecipientAuthorityBases.ClientCommercialRouting ||
        mail.PracticeClientId is not { } clientId || mail.RecipientContactId is not { } expectedContactId ||
        mail.RecipientRoutingId is not { } expectedRoutingId ||
        (proposalScope.ProposalClientId ?? proposalScope.OpportunityClientId) != clientId)
      return false;

    var now = DateTimeOffset.UtcNow;
    var today = DateOnly.FromDateTime(now.UtcDateTime);
    var routes = await (
      from routing in db.ClientContactRoutings.AsNoTracking()
      join contact in db.ClientContacts.AsNoTracking() on routing.ClientContactId equals contact.Id
      where routing.FirmId == mail.FirmId && routing.PracticeClientId == clientId &&
            routing.Purpose == CorrespondencePurposes.Commercial && routing.RevokedAt == null &&
            (routing.EffectiveFrom == null || routing.EffectiveFrom <= today) &&
            (routing.EffectiveTo == null || routing.EffectiveTo >= today) &&
            contact.FirmId == mail.FirmId && contact.PracticeClientId == clientId && contact.IsActive &&
            (contact.ValidFrom == null || contact.ValidFrom <= now) &&
            (contact.ValidTo == null || contact.ValidTo >= now) && !string.IsNullOrWhiteSpace(contact.Email)
      select new
      {
        RoutingId = routing.Id,
        routing.IsPrimaryForPurpose,
        ContactId = contact.Id,
        Email = contact.Email
      }
    ).ToListAsync(ct);

    var designated = routes.Count switch
    {
      1 => routes[0],
      > 1 when routes.Count(x => x.IsPrimaryForPurpose) == 1 => routes.Single(x => x.IsPrimaryForPurpose),
      _ => null
    };
    return designated is not null && designated.RoutingId == expectedRoutingId &&
      designated.ContactId == expectedContactId && EmailsMatch(designated.Email, mail.Recipient);
  }

  private static bool EmailsMatch(string? current, string? frozen) =>
    !string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(frozen) &&
    System.Net.Mail.MailAddress.TryCreate(current.Trim(), out var currentAddress) &&
    System.Net.Mail.MailAddress.TryCreate(frozen.Trim(), out var frozenAddress) &&
    string.Equals(currentAddress.Address, frozenAddress.Address, StringComparison.OrdinalIgnoreCase);

  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("mail-delivery-outcome-uncertain");

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op, OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    var expected = Expected(op);
    if (verifiedRemoteResult != expected) throw new OperationBlockedException("mail-provider-receipt-conflict");
    var mail = await db.CommercialNotifications.SingleAsync(x => x.FirmId == op.FirmId && x.Id == op.TargetId, ct);
    mail.DeliveryState = CommercialDeliveryStates.ProviderAccepted;
    mail.DeliveredAt = DateTimeOffset.UtcNow;
    return expected;
  }

  private static async Task MarkDeliveryStateAsync(IAuditSphereDbContextFactory factory, Guid firmId, Guid notificationId, string state, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    await db.CommercialNotifications
      .Where(x => x.FirmId == firmId && x.Id == notificationId)
      .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DeliveryState, state), ct);
  }

  private static OperationResult Expected(DurableOperation op) => new(op.CorrelationId.ToString("D"), Hashing.Sha256Hex(op.PayloadJson));
}

public sealed class CommercialMailDiscovery(
  IAuditSphereDbContextFactory factory, IOperationStore store, CommercialMailDeliveryHandler handler, WorkerOptions options) : IPendingOperationDiscovery
{
  public async Task<int> EnqueuePendingAsync(CancellationToken ct)
  {
    await using var read = await factory.CreateAsync(ct);
    // Receipt and proposal dispatches alike carry the owning client when one exists, so the mail
    // worker discovers every queued commercial email without joining through the fee-milestone path;
    // a pre-conversion proposal dispatch legitimately has no client binding yet.
    var pending = await read.CommercialNotifications.AsNoTracking()
      .Where(n => n.FirmId == options.FirmId && n.DeliveryState == "QUEUED" &&
        !read.DurableOperations.Any(o => o.FirmId == n.FirmId && o.TargetId == n.Id && o.OperationKind == CommercialMailDeliveryHandler.Kind))
      .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)
      .Select(n => new { n.Id, ClientId = n.PracticeClientId })
      .Take(25).ToListAsync(ct);
    var count = 0;
    foreach (var mail in pending)
    {
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var result = await store.EnqueueAsync(db, new OperationRequest(options.FirmId, mail.ClientId, null, CommercialMailDeliveryHandler.Kind,
        mail.Id, 1, "commercial-mail:" + mail.Id.ToString("D"), JsonSerializer.Serialize(new { notificationId = mail.Id.ToString("D") })), handler, ct);
      if (!result.Succeeded) continue;
      await tx.CommitAsync(ct);
      count++;
    }
    return count;
  }
}
