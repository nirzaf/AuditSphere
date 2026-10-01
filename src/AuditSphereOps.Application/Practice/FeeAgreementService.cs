using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record FeeAgreementView(
  EngagementFeeAgreement Agreement,
  IReadOnlyList<FeeMilestoneView> Milestones,
  bool ReleaseRecorded);

public sealed record FeeMilestoneView(
  FeeMilestone Milestone, string? InvoiceNumber, string? InvoiceStatus, decimal Allocated, decimal Outstanding);

public sealed record AdvancePaymentOutcome(
  Guid ReceiptId, decimal Allocated, decimal Outstanding, bool MilestonePaid, Guid? ReceiptDocumentId, bool EmailQueued, string Message);

/// <summary>
/// The agreed-fee cycle: one agreement per accepted proposal, an advance (default 50%) invoiced and recorded manually
/// (no payment gateway), an official receipt with one queued email when the advance is fully paid, and the remaining
/// balance invoiced once, only after the engagement's final release exists. Every step is idempotent and reuses the
/// existing billing commands, so invoice review, posting and receipt allocation controls are unchanged.
/// </summary>
public static class FeeAgreementService
{
  public const string MilestoneSourceKind = "FEE_MILESTONE";
  public const decimal DefaultAdvancePercent = 50m;
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];

  public static async Task<CommandResult<Guid>> CreateAgreementAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var result = await EnsureReviewedAgreementWithinTransactionAsync(db, actor, proposalId, ct);
    if (result.Succeeded) await tx.CommitAsync(ct);
    return result;
  }

  // The caller has authorized commercial access and holds the firm guard in its local transaction.
  internal static async Task<CommandResult<Guid>> EnsureReviewedAgreementWithinTransactionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct)
  {
    var existing = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId, ct);
    if (existing is not null) return CommandResult<Guid>.Ok(existing.Id);
    var proposal = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (proposal.Status != CrmStates.ProposalAccepted || proposal.PracticeClientId is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A fee agreement needs an accepted proposal that has been converted to a client.");
    var quotation = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (quotation is null || quotation.Status != QuotationStates.Approved || quotation.Fee != proposal.Fee)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The agreed fee must come from an approved quotation that matches the accepted proposal.");

    var advance = MoneyPolicy.Normalize(proposal.Fee * DefaultAdvancePercent / 100m, QuotationCalculator.CurrencyScale);
    if (advance <= 0m || proposal.Fee - advance <= 0m)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The agreed fee must support positive advance and balance milestones.");
    var agreement = new EngagementFeeAgreement
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProposalId = proposal.Id, QuotationVersionId = quotation.Id,
      PracticeClientId = proposal.PracticeClientId.Value, Currency = proposal.Currency.ToUpperInvariant(), AgreedFee = proposal.Fee,
      AdvancePercent = DefaultAdvancePercent, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.EngagementFeeAgreements.Add(agreement);
    db.FeeMilestones.AddRange(
      new FeeMilestone { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, AgreementId = agreement.Id, Kind = FeeMilestoneKinds.Advance, Amount = advance, CreatedAt = agreement.CreatedAt },
      new FeeMilestone { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, AgreementId = agreement.Id, Kind = FeeMilestoneKinds.Balance, Amount = agreement.AgreedFee - advance, CreatedAt = agreement.CreatedAt });
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(agreement.Id);
  }

  /// <summary>Links the accepted engagement so the final release can trigger the balance invoice.</summary>
  public static async Task<CommandResult> LinkEngagementAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var agreement = await db.EngagementFeeAgreements.SingleOrDefaultAsync(x => x.Id == agreementId && x.FirmId == actor.FirmId, ct);
    if (agreement is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (agreement.EngagementId == engagementId) return CommandResult.Ok();
    if (agreement.EngagementId.HasValue) return CommandResult.Fail(ErrorCodes.ProtectedState, "The agreement is already linked to an engagement.");
    var belongs = await db.Engagements.AsNoTracking().AnyAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId && x.PracticeClientId == agreement.PracticeClientId, ct);
    if (!belongs) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    agreement.EngagementId = engagementId;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>Creates the milestone's invoice draft through the normal billing review path. Idempotent.</summary>
  public static async Task<CommandResult<Guid>> IssueAdvanceInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, CancellationToken ct = default) =>
    await IssueInvoiceAsync(db, actor, agreementId, FeeMilestoneKinds.Advance, ct);

  /// <summary>
  /// The balance is invoiced only after advance payment and only once the linked engagement has an issued release
  /// (delivery and sign-off of the final report). Repeating the request never creates a second invoice.
  /// </summary>
  public static async Task<CommandResult<Guid>> IssueBalanceInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, CancellationToken ct = default) =>
    await IssueInvoiceAsync(db, actor, agreementId, FeeMilestoneKinds.Balance, ct);

  private static async Task<CommandResult<Guid>> IssueInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, string kind, CancellationToken ct)
  {
    var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == agreementId && x.FirmId == actor.FirmId, ct);
    if (agreement is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, agreement.PracticeClientId, RequiredRoles: ["FinanceManager", "FinanceReviewer"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var milestone = await db.FeeMilestones.SingleAsync(x => x.FirmId == actor.FirmId && x.AgreementId == agreementId && x.Kind == kind, ct);
    if (milestone.InvoiceId.HasValue) return CommandResult<Guid>.Ok(milestone.InvoiceId.Value);

    if (kind == FeeMilestoneKinds.Balance)
    {
      var advance = await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Advance, ct);
      if (advance.State != FeeMilestoneStates.Paid)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The advance must be fully paid before the balance is invoiced.");
      if (agreement.EngagementId is null || !await db.Releases.AsNoTracking().AnyAsync(x =>
            x.FirmId == actor.FirmId && x.EngagementId == agreement.EngagementId && x.ClientId == agreement.PracticeClientId, ct))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The balance is invoiced after the final report is delivered and signed off (an issued release on the linked engagement).");
    }

    // A crash between the invoice draft and the milestone update leaves an invoice with a source allocation: relink it.
    var linked = await ExistingInvoiceAsync(db, actor.FirmId, milestone.Id, ct);
    Guid invoiceId;
    if (linked is not null) invoiceId = linked.Value;
    else
    {
      var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.PracticeClientId == agreement.PracticeClientId, ct);
      Guid accountId;
      if (account is null)
      {
        var created = await BillingService.CreateBillingAccountAsync(db, actor, new(agreement.PracticeClientId, agreement.Currency), ct);
        if (!created.Succeeded) return CommandResult<Guid>.Fail(created.ErrorCode!, created.Message!);
        accountId = created.Value;
      }
      else if (!string.Equals(account.Currency, agreement.Currency, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The client billing account currency differs from the agreed fee currency.");
      else accountId = account.Id;

      var percent = kind == FeeMilestoneKinds.Advance ? agreement.AdvancePercent : 100m - agreement.AdvancePercent;
      var description = kind == FeeMilestoneKinds.Advance
        ? $"Advance fee ({agreement.AdvancePercent:0.##}% of agreed fee {agreement.AgreedFee.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency})"
        : $"Balance fee ({percent:0.##}% of agreed fee {agreement.AgreedFee.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency})";
      var number = $"AGR-{agreement.Id.ToString("N")[..8].ToUpperInvariant()}-{(kind == FeeMilestoneKinds.Advance ? "ADV" : "BAL")}";
      var draft = await BillingService.CreateInvoiceDraftAsync(db, actor, new(accountId, number,
        [new InvoiceLineRequest(description, 1m, milestone.Amount, MilestoneSourceKind, milestone.Id, 1)]), ct);
      if (!draft.Succeeded) return CommandResult<Guid>.Fail(draft.ErrorCode!, draft.Message!);
      invoiceId = draft.Value;
    }
    milestone.InvoiceId = invoiceId;
    milestone.State = FeeMilestoneStates.Invoiced;
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(invoiceId);
  }

  /// <summary>
  /// Records a manual advance payment (no gateway), allocates it to the advance invoice and, when the advance is fully
  /// paid, produces the official receipt document and queues one email. Partial payments are recorded and leave the
  /// outstanding amount; a repeated payment reference never creates a second receipt, document or email.
  /// </summary>
  public static async Task<CommandResult<AdvancePaymentOutcome>> RecordAdvancePaymentAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, decimal amount, string paymentReference,
    DateTimeOffset? receivedAt = null, CancellationToken ct = default)
  {
    var reference = (paymentReference ?? string.Empty).Trim();
    if (reference.Length is < 1 or > 120)
      return CommandResult<AdvancePaymentOutcome>.Fail("fee.invalid", "Enter the bank or transfer reference (up to 120 characters).");
    var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == agreementId && x.FirmId == actor.FirmId, ct);
    if (agreement is null) return CommandResult<AdvancePaymentOutcome>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, agreement.PracticeClientId, RequiredRoles: ["FinanceManager", "FinanceReviewer"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<AdvancePaymentOutcome>.Fail(auth.ErrorCode!, auth.Message!);
    var milestone = await db.FeeMilestones.SingleAsync(x => x.FirmId == actor.FirmId && x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Advance, ct);
    if (milestone.InvoiceId is null)
      return CommandResult<AdvancePaymentOutcome>.Fail(ErrorCodes.GateBlocked, "Issue the advance invoice first.");
    var invoice = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == milestone.InvoiceId && x.FirmId == actor.FirmId, ct);
    if (invoice.Status is not (BillingStates.InvoicePosted or BillingStates.InvoiceSent))
      return CommandResult<AdvancePaymentOutcome>.Fail(ErrorCodes.GateBlocked, "The advance invoice must be approved and posted before a payment is recorded.");

    var storedReference = $"ADV-{agreement.Id.ToString("N")[..8].ToUpperInvariant()}-{reference}";
    var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.BillingAccountId == invoice.BillingAccountId && x.Reference == storedReference, ct);
    if (receipt is null)
    {
      if (amount <= 0 || MoneyPolicy.Normalize(amount) != amount)
        return CommandResult<AdvancePaymentOutcome>.Fail("fee.invalid", "The amount must be positive.");
      var outstandingBefore = await OutstandingAsync(db, actor.FirmId, invoice, ct);
      if (amount > outstandingBefore)
        return CommandResult<AdvancePaymentOutcome>.Fail("fee.over-payment", $"The amount exceeds the outstanding advance ({outstandingBefore.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}).");
      var recorded = await BillingService.RecordReceiptAsync(db, actor, new(invoice.BillingAccountId, amount, storedReference, receivedAt), ct);
      if (!recorded.Succeeded) return CommandResult<AdvancePaymentOutcome>.Fail(recorded.ErrorCode!, recorded.Message!);
      receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == recorded.Value && x.FirmId == actor.FirmId, ct);
    }
    var allocatedToInvoice = await db.ReceiptAllocations.Where(x => x.FirmId == actor.FirmId && x.ReceiptId == receipt.Id && x.InvoiceId == invoice.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
    if (allocatedToInvoice < receipt.Amount)
    {
      var allocation = await BillingService.AllocateReceiptAsync(db, actor, new(receipt.Id, invoice.Id, receipt.Amount - allocatedToInvoice), ct);
      if (!allocation.Succeeded) return CommandResult<AdvancePaymentOutcome>.Fail(allocation.ErrorCode!, allocation.Message!);
    }

    var outstanding = await OutstandingAsync(db, actor.FirmId, invoice, ct);
    var allocated = invoice.Total - outstanding;
    if (outstanding > 0)
      return CommandResult<AdvancePaymentOutcome>.Ok(new(receipt.Id, allocated, outstanding, false, null, false,
        $"Partial advance recorded; {outstanding.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} remains outstanding."));

    var tracked = await db.FeeMilestones.SingleAsync(x => x.Id == milestone.Id && x.FirmId == actor.FirmId, ct);
    if (tracked.State != FeeMilestoneStates.Paid)
    {
      tracked.State = FeeMilestoneStates.Paid;
      tracked.ReceiptId = receipt.Id;
      tracked.PaidAt = receipt.ReceivedAt;
      await db.SaveChangesAsync(ct);
    }
    await AuditSphereOps.Application.Documents.ClientPortalService.RefreshCommercialIntentAsync(db, actor.FirmId, agreement.PracticeClientId, ct);
    return await EnsureReceiptDocumentAndEmailAsync(db, actor, agreement, tracked, invoice, receipt, ct);
  }

  private static async Task<CommandResult<AdvancePaymentOutcome>> EnsureReceiptDocumentAndEmailAsync(
    IAuditSphereDbContext db, ActorContext actor, EngagementFeeAgreement agreement, FeeMilestone milestone,
    Invoice invoice, Receipt receipt, CancellationToken ct)
  {
    var profile = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (profile is null)
      return CommandResult<AdvancePaymentOutcome>.Ok(new(receipt.Id, invoice.Total, 0m, true, null, false,
        "Advance recorded as paid. Configure the firm commercial profile to generate the official receipt."));
    var client = await db.PracticeClients.AsNoTracking().SingleAsync(x => x.Id == agreement.PracticeClientId && x.FirmId == actor.FirmId, ct);

    var document = await db.CommercialDocuments.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.FeeMilestoneId == milestone.Id && x.Kind == CommercialDocumentKinds.PaymentReceipt, ct);
    if (document is null)
    {
      var receiptNumber = $"RCT-{milestone.Id.ToString("N")[..8].ToUpperInvariant()}";
      var balance = agreement.AgreedFee - invoice.Total;
      var model = new CommercialDocumentModel(profile, "Official payment receipt", receiptNumber,
        DateOnly.FromDateTime(receipt.ReceivedAt.UtcDateTime), client.LegalName,
        [
          new("Payment received", [$"We acknowledge receipt of {invoice.Total.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} from {client.LegalName}."],
            new(["Item", "Detail"],
            [
              ["Received on", receipt.ReceivedAt.UtcDateTime.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)],
              ["Payment reference", receipt.Reference],
              ["Applied to invoice", invoice.InvoiceNumber],
              ["Agreed engagement fee", $"{agreement.AgreedFee.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}"],
              [$"Advance ({agreement.AdvancePercent:0.##}%)", $"{invoice.Total.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} — paid"],
              ["Balance due on delivery of the final report", $"{balance.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}"],
            ], [1]))
        ], false);
      var bytes = CommercialDocumentRenderer.RenderDocx(model);
      document = new CommercialDocument
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProposalId = agreement.ProposalId, QuotationVersionId = agreement.QuotationVersionId,
        FeeMilestoneId = milestone.Id, Kind = CommercialDocumentKinds.PaymentReceipt, TemplateVersion = CommercialDocumentRenderer.ReceiptTemplate,
        ProfileVersion = profile.Version, FileName = $"Receipt-{receiptNumber}.docx", ContentType = CommercialDocumentRenderer.DocxContentType,
        Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      };
      db.CommercialDocuments.Add(document);
      await db.SaveChangesAsync(ct);
    }

    var queued = await db.CommercialNotifications.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.FeeMilestoneId == milestone.Id, ct);
    if (!queued)
    {
      var recipient = await ResolveRecipientAsync(db, actor.FirmId, agreement, ct);
      if (recipient is null)
        return CommandResult<AdvancePaymentOutcome>.Ok(new(receipt.Id, invoice.Total, 0m, true, document.Id, false,
          "Advance paid and receipt generated. No client contact email is on file, so no email was queued."));
      db.CommercialNotifications.Add(new CommercialNotification
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, FeeMilestoneId = milestone.Id, DocumentId = document.Id, Recipient = recipient,
        Subject = $"Payment receipt {document.FileName.Replace("Receipt-", string.Empty).Replace(".docx", string.Empty)} — {profile.LegalName}",
        Body = $"Dear {client.LegalName},\n\nWe confirm receipt of your advance payment of {invoice.Total.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} " +
               $"(reference {receipt.Reference}) against invoice {invoice.InvoiceNumber}. The remaining balance is invoiced on delivery of the final report.\n\n" +
               $"The official receipt is retained on your engagement record.\n\n{profile.LegalName}",
        CreatedAt = DateTimeOffset.UtcNow
      });
      try { await db.SaveChangesAsync(ct); }
      catch (DbUpdateException) { /* a concurrent retry queued it first: the unique index guarantees exactly one */ }
    }
    return CommandResult<AdvancePaymentOutcome>.Ok(new(receipt.Id, invoice.Total, 0m, true, document.Id, true, "Advance paid: official receipt generated and email queued for delivery."));
  }

  public static async Task<CommandResult<FeeAgreementView?>> GetForProposalAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return CommandResult<FeeAgreementView?>.Fail(auth.ErrorCode!, auth.Message!);
    var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId, ct);
    if (agreement is null) return CommandResult<FeeAgreementView?>.Ok(null);
    var milestones = await db.FeeMilestones.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.AgreementId == agreement.Id).OrderBy(x => x.Kind).ToListAsync(ct);
    var views = new List<FeeMilestoneView>();
    foreach (var milestone in milestones)
    {
      Invoice? invoice = milestone.InvoiceId.HasValue
        ? await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == milestone.InvoiceId && x.FirmId == actor.FirmId, ct) : null;
      var outstanding = invoice is null ? milestone.Amount : await OutstandingAsync(db, actor.FirmId, invoice, ct);
      views.Add(new(milestone, invoice?.InvoiceNumber, invoice?.Status, milestone.Amount - outstanding, outstanding));
    }
    var released = agreement.EngagementId.HasValue && await db.Releases.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == agreement.EngagementId, ct);
    return CommandResult<FeeAgreementView?>.Ok(new(agreement, views.OrderBy(x => x.Milestone.Kind == FeeMilestoneKinds.Advance ? 0 : 1).ToList(), released));
  }

  private static async Task<decimal> OutstandingAsync(IAuditSphereDbContext db, Guid firmId, Invoice invoice, CancellationToken ct)
  {
    var allocated = await db.ReceiptAllocations.Where(x => x.FirmId == firmId && x.InvoiceId == invoice.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
    var credited = await db.CreditNotes.Where(x => x.FirmId == firmId && x.InvoiceId == invoice.Id && x.Status == BillingStates.CreditIssued).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
    return MoneyPolicy.Normalize(invoice.Total - allocated - credited);
  }

  private static async Task<Guid?> ExistingInvoiceAsync(IAuditSphereDbContext db, Guid firmId, Guid milestoneId, CancellationToken ct)
  {
    var line = await (from allocation in db.BillingSourceAllocations.AsNoTracking()
                      join invoiceLine in db.InvoiceLines.AsNoTracking() on allocation.InvoiceLineId equals invoiceLine.Id
                      where allocation.FirmId == firmId && allocation.SourceKind == MilestoneSourceKind && allocation.SourceId == milestoneId
                      select (Guid?)invoiceLine.InvoiceId).FirstOrDefaultAsync(ct);
    return line;
  }

  private static async Task<string?> ResolveRecipientAsync(IAuditSphereDbContext db, Guid firmId, EngagementFeeAgreement agreement, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var contact = await db.ClientContacts.AsNoTracking().Where(x => x.FirmId == firmId && x.PracticeClientId == agreement.PracticeClientId && x.Primary &&
      (x.ValidFrom == null || x.ValidFrom <= now) && (x.ValidTo == null || x.ValidTo >= now)).Select(x => x.Email).FirstOrDefaultAsync(ct);
    if (!string.IsNullOrWhiteSpace(contact)) return contact;
    var proposal = await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == agreement.ProposalId && x.FirmId == firmId, ct);
    var lead = await (from o in db.Opportunities.AsNoTracking() join l in db.Leads.AsNoTracking() on o.LeadId equals l.Id
                      where o.Id == proposal.OpportunityId && o.FirmId == firmId select l.PrimaryContactEmail).FirstOrDefaultAsync(ct);
    return string.IsNullOrWhiteSpace(lead) ? null : lead;
  }

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);
}
