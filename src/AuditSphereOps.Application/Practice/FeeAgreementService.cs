using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

internal sealed record PaymentRecipientSnapshot(string Email, Guid? ContactId, Guid? RoutingId, string AuthorityBasis);

public sealed record FeeAgreementView(
  EngagementFeeAgreement Agreement,
  IReadOnlyList<FeeMilestoneView> Milestones,
  bool ReleaseRecorded);

public sealed record FeeMilestoneView(
  FeeMilestone Milestone, string? InvoiceNumber, string? InvoiceStatus, decimal Allocated, decimal Outstanding);

public sealed record AdvancePaymentOutcome(
  Guid ReceiptId, decimal Allocated, decimal Outstanding, bool MilestonePaid, Guid? ReceiptDocumentId, bool EmailQueued, string Message,
  Guid? VoucherDocumentId = null, bool VoucherEmailQueued = false);

/// <summary>
/// The agreed-fee cycle: one agreement per accepted proposal, an advance (default 50%) invoiced and recorded manually
/// (no payment gateway), an official receipt with one queued email when the advance is fully paid, and the remaining
/// balance invoiced once, only after the engagement's final release exists. Every step is idempotent and reuses the
/// existing billing commands, so invoice review, posting and receipt allocation controls are unchanged.
/// </summary>
public static partial class FeeAgreementService
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
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
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
    if (proposal.Status != CrmStates.ProposalAccepted || proposal.SentAt is null || proposal.ResponseAt is null || proposal.PracticeClientId is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A fee agreement needs an accepted proposal that has been converted to a client.");
    var quotation = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (quotation is null || quotation.Status != QuotationStates.Approved || quotation.Fee != proposal.Fee)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The agreed fee must come from an approved quotation that matches the accepted proposal.");
    // The agreement must carry the exact accepted commercial terms (STE 4.1.3): a dispatched offer binds one
    // quotation revision, and a later approved revision needs a new dispatch and fresh acceptance.
    var dispatch = await db.CommercialNotifications.AsNoTracking().SingleOrDefaultAsync(
      x => x.FirmId == actor.FirmId && x.ProposalId == proposalId && x.Kind == CommercialNotificationKinds.Proposal, ct);
    if (dispatch is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "A recorded dispatch of the approved offer is required before creating the fee agreement.");
    if (dispatch.QuotationVersionId != quotation.Id)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "The client accepted a different quotation revision than the current one; use a newly approved commercial revision with fresh acceptance before creating the fee agreement.");
    // The agreement carries the accepted terms, so the recorded acceptance and dispatch must cite one exact offer identity.
    var response = await db.ProposalResponseRecords.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId, ct);
    var offer = dispatch.DocumentId is { } documentId
      ? await db.CommercialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == documentId, ct)
      : null;
    if (!ProposalResponseEvidence.MatchesAcceptedResponse(proposal, dispatch, response, quotation, offer))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale,
        "A matching immutable client response to the exact current rendered offer is required before creating the fee agreement.");

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
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return auth;
    var agreement = await db.EngagementFeeAgreements.SingleOrDefaultAsync(x => x.Id == agreementId && x.FirmId == actor.FirmId, ct);
    if (agreement is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var agreementEntry = db.EngagementFeeAgreements.Attach(agreement);
    await agreementEntry.ReloadAsync(ct);
    if (agreementEntry.State == EntityState.Detached || agreement.FirmId != actor.FirmId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (agreement.EngagementId == engagementId) return CommandResult.Ok();
    if (agreement.EngagementId.HasValue) return CommandResult.Fail(ErrorCodes.ProtectedState, "The agreement is already linked to an engagement.");
    var belongs = await db.Engagements.AsNoTracking().AnyAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId && x.PracticeClientId == agreement.PracticeClientId, ct);
    if (!belongs) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (await db.EngagementFeeAgreements.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.EngagementId == engagementId && x.Id != agreement.Id, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Another fee agreement is already linked to this engagement.");
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
  /// The balance is drafted only after advance payment and an issued financial-package release on the linked
  /// engagement. Repeating the request never creates a second invoice.
  /// </summary>
  public static async Task<CommandResult<Guid>> IssueBalanceInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, CancellationToken ct = default) =>
    await IssueInvoiceAsync(db, actor, agreementId, FeeMilestoneKinds.Balance, ct);

  internal static async Task<bool> HasFinancialPackageReleaseAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid? engagementId, CancellationToken ct) =>
    engagementId.HasValue && await db.Releases.AsNoTracking().AnyAsync(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId.Value &&
      db.ReleaseCandidates.Any(c => c.FirmId == firmId && c.Id == x.ReleaseCandidateId &&
        c.TargetKind == ReleaseTargetKinds.FinancialPackage), ct);

  private static async Task<CommandResult<Guid>> IssueInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, string kind, CancellationToken ct)
  {
    var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == agreementId && x.FirmId == actor.FirmId, ct);
    if (agreement is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, agreement.PracticeClientId, RequiredRoles: ["FinanceManager", "FinanceReviewer"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    // Ambient callers must take the exclusive firm fence before client/target locks, as the invoice worker does.
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");

    agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == agreementId && x.FirmId == actor.FirmId, ct);
    if (agreement is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, agreement.PracticeClientId, RequiredRoles: ["FinanceManager", "FinanceReviewer"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var milestone = await db.FeeMilestones.FromSqlInterpolated($"SELECT * FROM fee_milestones WHERE firm_id = {actor.FirmId} AND agreement_id = {agreementId} AND kind = {kind} FOR UPDATE")
      .SingleAsync(ct);
    await db.FeeMilestones.Attach(milestone).ReloadAsync(ct);
    if (milestone.InvoiceId.HasValue)
    {
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(milestone.InvoiceId.Value);
    }

    if (kind == FeeMilestoneKinds.Balance)
    {
      var advance = await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Advance, ct);
      if (!await IsMilestoneCurrentlyPaidAsync(db, advance, ct))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The advance must be fully paid before the balance is invoiced.");
      if (!await HasFinancialPackageReleaseAsync(db, actor.FirmId, agreement.PracticeClientId, agreement.EngagementId, ct))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The balance invoice draft requires an issued financial-package release on the linked engagement.");
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
      // UUIDv7 timestamp prefixes repeat across agreements; retain the full stable source identity.
      var number = $"AGR-{agreement.Id.ToString("N").ToUpperInvariant()}-{(kind == FeeMilestoneKinds.Advance ? "ADV" : "BAL")}";
      var draft = await BillingService.CreateInvoiceDraftAsync(db, actor, new(accountId, number,
        [new InvoiceLineRequest(description, 1m, milestone.Amount, MilestoneSourceKind, milestone.Id, 1)]), ct);
      if (!draft.Succeeded) return CommandResult<Guid>.Fail(draft.ErrorCode!, draft.Message!);
      invoiceId = draft.Value;
    }
    milestone.InvoiceId = invoiceId;
    milestone.State = FeeMilestoneStates.Invoiced;
    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(invoiceId);
  }

  /// <summary>
  /// Records a manual advance payment (no gateway), allocates it to the advance invoice and, when the advance is fully
  /// paid, produces the official receipt document and queues one email. Partial payments are recorded and leave the
  /// outstanding amount; a repeated payment reference never creates a second receipt, document or email.
  /// </summary>
  public static Task<CommandResult<AdvancePaymentOutcome>> RecordAdvancePaymentAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, decimal amount, string paymentReference,
    DateTimeOffset? receivedAt = null, CancellationToken ct = default)
    => RecordMilestonePaymentAsync(db, actor, agreementId, FeeMilestoneKinds.Advance, amount, paymentReference, receivedAt, ct);

  /// <summary>
  /// Records a manual final balance payment against the balance invoice, producing the official settlement receipt.
  /// </summary>
  public static Task<CommandResult<AdvancePaymentOutcome>> RecordBalancePaymentAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, decimal amount, string paymentReference,
    DateTimeOffset? receivedAt = null, CancellationToken ct = default)
    => RecordMilestonePaymentAsync(db, actor, agreementId, FeeMilestoneKinds.Balance, amount, paymentReference, receivedAt, ct);

  private static async Task<CommandResult<AdvancePaymentOutcome>> RecordMilestonePaymentAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid agreementId, string kind, decimal amount, string paymentReference,
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

    // The source identity is shared by finance actors; actor-private generic command replay is not its fence.
    // Ambient owners must acquire the firm fence before any client/target locks and roll back on failure.
    await using var paymentTx = db.Database.CurrentTransaction is null
      ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<AdvancePaymentOutcome>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == agreementId && x.FirmId == actor.FirmId, ct);
    if (agreement is null) return CommandResult<AdvancePaymentOutcome>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, agreement.PracticeClientId, RequiredRoles: ["FinanceManager", "FinanceReviewer"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<AdvancePaymentOutcome>.Fail(auth.ErrorCode!, auth.Message!);
    var milestone = await db.FeeMilestones.FromSqlInterpolated($"SELECT * FROM fee_milestones WHERE firm_id = {actor.FirmId} AND agreement_id = {agreementId} AND kind = {kind} FOR UPDATE")
      .SingleAsync(ct);
    await db.FeeMilestones.Attach(milestone).ReloadAsync(ct);
    if (milestone.InvoiceId is null)
      return CommandResult<AdvancePaymentOutcome>.Fail(ErrorCodes.GateBlocked, $"Issue the {kind.ToLowerInvariant()} invoice first.");
    var invoice = await (from i in db.Invoices.AsNoTracking()
      join account in db.BillingAccounts.AsNoTracking() on new { i.FirmId, Id = i.BillingAccountId } equals new { account.FirmId, account.Id }
      where i.FirmId == actor.FirmId && i.Id == milestone.InvoiceId && account.PracticeClientId == agreement.PracticeClientId
      select i).SingleOrDefaultAsync(ct);
    if (invoice is null) return CommandResult<AdvancePaymentOutcome>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (invoice.Status is not (BillingStates.InvoicePosted or BillingStates.InvoiceSent))
      return CommandResult<AdvancePaymentOutcome>.Fail(ErrorCodes.GateBlocked, $"The {kind.ToLowerInvariant()} invoice must be approved and posted before a payment is recorded.");

    var prefix = kind == FeeMilestoneKinds.Advance ? "ADV" : "BAL";
    var storedReference = $"{prefix}-{agreement.Id.ToString("N").ToUpperInvariant()}-{reference}";
    var legacyReference = $"{prefix}-{agreement.Id.ToString("N")[..8].ToUpperInvariant()}-{reference}";
    var recovered = await ResolveMilestoneReceiptAsync(db, actor, agreement, milestone, invoice,
      storedReference, legacyReference, ct);
    if (!recovered.Succeeded) return CommandResult<AdvancePaymentOutcome>.Fail(recovered.ErrorCode!, recovered.Message!);
    var receipt = recovered.Value;
    if (receipt is not null) storedReference = receipt.Reference;
    if (receipt is not null && (receipt.Amount != amount || (receivedAt.HasValue && receipt.ReceivedAt != receivedAt)))
      return CommandResult<AdvancePaymentOutcome>.Fail("fee.reference-conflict", "This payment reference already records different payment details.");
    if (receipt is null)
    {
      if (amount <= 0 || MoneyPolicy.Normalize(amount) != amount)
        return CommandResult<AdvancePaymentOutcome>.Fail("fee.invalid", "The amount must be positive.");
      var outstandingBefore = await OutstandingAsync(db, actor.FirmId, invoice, ct);
      if (amount > outstandingBefore)
        return CommandResult<AdvancePaymentOutcome>.Fail("fee.over-payment", $"The amount exceeds the outstanding {kind.ToLowerInvariant()} ({outstandingBefore.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}).");
      var receiptRequestId = BillingCommandWorkspace.DeriveRequestId(
        $"fee-milestone:receipt:{agreement.Id:N}:{kind}:{storedReference}");
      var recorded = await BillingService.RecordReceiptAsync(db, actor,
        new(invoice.BillingAccountId, amount, storedReference, receivedAt, receiptRequestId), ct);
      if (!recorded.Succeeded) return CommandResult<AdvancePaymentOutcome>.Fail(recorded.ErrorCode!, recorded.Message!);
      receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == recorded.Value && x.FirmId == actor.FirmId, ct);
    }
    var allocatedToInvoice = await db.ReceiptAllocations.Where(x => x.FirmId == actor.FirmId && x.ReceiptId == receipt.Id && x.InvoiceId == invoice.Id)
      .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
    var reversedFromInvoice = await (from reversal in db.ReceiptAllocationReversals.AsNoTracking()
      join allocation in db.ReceiptAllocations.AsNoTracking()
        on new { reversal.FirmId, reversal.ReceiptAllocationId } equals new { allocation.FirmId, ReceiptAllocationId = allocation.Id }
      where reversal.FirmId == actor.FirmId && allocation.ReceiptId == receipt.Id && allocation.InvoiceId == invoice.Id &&
        reversal.Status == ReceiptAllocationReversalStates.Approved
      select (decimal?)reversal.Amount).SumAsync(ct) ?? 0m;
    allocatedToInvoice = MoneyPolicy.Normalize(Math.Max(0m, allocatedToInvoice - reversedFromInvoice));
    if (allocatedToInvoice < receipt.Amount)
    {
      var allocationRequestId = BillingCommandWorkspace.DeriveRequestId(
        $"fee-milestone:allocation:{agreement.Id:N}:{kind}:{storedReference}:{receipt.Id:N}:{invoice.Id:N}:reversed:{BillingCommandWorkspace.Decimal(reversedFromInvoice)}");
      var allocation = await BillingService.AllocateReceiptAsync(db, actor,
        new(receipt.Id, invoice.Id, receipt.Amount - allocatedToInvoice, allocationRequestId), ct);
      if (!allocation.Succeeded) return CommandResult<AdvancePaymentOutcome>.Fail(allocation.ErrorCode!, allocation.Message!);
    }

    // Receipt, allocation and settlement retain the same firm fence; a reversal cannot interleave the cash projection.
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, agreement.PracticeClientId, RequiredRoles: ["FinanceManager", "FinanceReviewer"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<AdvancePaymentOutcome>.Fail(auth.ErrorCode!, auth.Message!);
    var currentMilestone = await db.FeeMilestones.AsNoTracking()
      .SingleAsync(x => x.Id == milestone.Id && x.FirmId == actor.FirmId, ct);
    milestone.State = currentMilestone.State;
    milestone.ReceiptId = currentMilestone.ReceiptId;
    milestone.PaidAt = currentMilestone.PaidAt;
    // Credits cannot replace the mandatory cash advance. Preserve the existing
    // credit-adjusted settlement policy for the final balance milestone.
    var netCash = await BillingService.GetNetAppliedToInvoiceAsync(db, actor.FirmId, invoice.Id, ct);
    var outstanding = kind == FeeMilestoneKinds.Advance
      ? MoneyPolicy.Normalize(Math.Max(0m, milestone.Amount - netCash))
      : await OutstandingAsync(db, actor.FirmId, invoice, ct);
    var allocated = kind == FeeMilestoneKinds.Advance ? netCash : invoice.Total - outstanding;
    // Every actual payment receives its own voucher and one dispatch intent, partial or not. A missing profile or recipient keeps the
    // payment and leaves the voucher recoverable: recording the same reference again completes it without a second receipt.
    var voucher = await EnsurePaymentVoucherAsync(db, actor, agreement, milestone, invoice, receipt, ct);
    if (outstanding > 0)
    {
      if (paymentTx is not null) await paymentTx.CommitAsync(ct);
      return CommandResult<AdvancePaymentOutcome>.Ok(new(receipt.Id, allocated, outstanding, false, null, false,
        $"Partial {kind.ToLowerInvariant()} recorded; {outstanding.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} remains outstanding. {voucher.Message}",
        voucher.DocumentId, voucher.EmailQueued));
    }

    var tracked = await db.FeeMilestones.SingleAsync(x => x.Id == milestone.Id && x.FirmId == actor.FirmId, ct);
    if (tracked.State != FeeMilestoneStates.Paid)
    {
      tracked.State = FeeMilestoneStates.Paid;
      tracked.ReceiptId = receipt.Id;
      tracked.PaidAt = receipt.ReceivedAt;
      await db.SaveChangesAsync(ct);
    }
    if (kind == FeeMilestoneKinds.Advance)
    {
      await AuditSphereOps.Application.Documents.ClientPortalService.RefreshCommercialIntentAsync(db, actor.FirmId, agreement.PracticeClientId, ct);
    }
    var settled = await EnsureReceiptDocumentAndEmailAsync(db, actor, agreement, tracked, invoice, receipt, ct);
    if (paymentTx is not null) await paymentTx.CommitAsync(ct);
    return settled.Succeeded
      ? CommandResult<AdvancePaymentOutcome>.Ok(settled.Value! with { VoucherDocumentId = voucher.DocumentId, VoucherEmailQueued = voucher.EmailQueued })
      : settled;
  }

  private static async Task<CommandResult<AdvancePaymentOutcome>> EnsureReceiptDocumentAndEmailAsync(
    IAuditSphereDbContext db, ActorContext actor, EngagementFeeAgreement agreement, FeeMilestone milestone,
    Invoice invoice, Receipt receipt, CancellationToken ct)
  {
    var profile = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (profile is null)
      return CommandResult<AdvancePaymentOutcome>.Ok(new(receipt.Id, invoice.Total, 0m, true, null, false,
        $"{milestone.Kind} recorded as paid. Configure the firm commercial profile to generate the official receipt."));
    var client = await db.PracticeClients.AsNoTracking().SingleAsync(x => x.Id == agreement.PracticeClientId && x.FirmId == actor.FirmId, ct);

    var document = await db.CommercialDocuments.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.FeeMilestoneId == milestone.Id && x.Kind == CommercialDocumentKinds.PaymentReceipt, ct);
    if (document is null)
    {
      // UUIDv7 prefixes contain a timestamp, not a unique milestone identity. Keep the complete
      // retained identity for new references; the existing-document branch above never reissues history.
      var receiptNumber = $"RCT-{milestone.Id.ToString("N").ToUpperInvariant()}";
      var balance = agreement.AgreedFee - invoice.Total;
      var isAdvance = milestone.Kind == FeeMilestoneKinds.Advance;
      // The settlement receipt identifies every constituent payment with its own amount, date and reference. It never presents the last
      // payment as if it carried the whole milestone; each payment also has its own voucher.
      var payments = await InvoicePaymentsAsync(db, actor.FirmId, invoice, ct);
      var paymentRows = payments.Select((p, index) => new[]
      {
        $"Payment {index + 1} received on {p.ReceivedAt.UtcDateTime.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)} (reference {p.Reference})",
        $"{p.Allocated.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}"
      }).ToList();
      var detailRows = isAdvance
        ? new List<string[]>
        {
          new[] { "Applied to invoice", invoice.InvoiceNumber },
          new[] { "Agreed engagement fee", $"{agreement.AgreedFee.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}" },
          new[] { $"Advance ({agreement.AdvancePercent:0.##}%)", $"{invoice.Total.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} — paid" },
          new[] { "Balance due on delivery of the final report", $"{balance.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}" },
        }
        : new List<string[]>
        {
          new[] { "Applied to invoice", invoice.InvoiceNumber },
          new[] { "Agreed engagement fee", $"{agreement.AgreedFee.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}" },
          new[] { "Milestone settlement", $"Final balance ({invoice.Total.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}) — paid in full" },
          new[] { "Total outstanding balance", $"0.00 {agreement.Currency}" },
        };

      detailRows.InsertRange(0, paymentRows);
      var model = new CommercialDocumentModel(profile, "Official payment receipt", receiptNumber,
        DateOnly.FromDateTime(receipt.ReceivedAt.UtcDateTime), client.LegalName,
        [
          new("Payment received", [$"We acknowledge settlement of {invoice.Total.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} from {client.LegalName}, received in {payments.Count} payment(s) listed below. Each payment has its own official voucher."],
            new(["Item", "Detail"], detailRows.ToArray(), [1]))
        ], false);
      var bytes = CommercialDocumentRenderer.RenderDocx(model);
      document = new CommercialDocument
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProposalId = agreement.ProposalId, QuotationVersionId = agreement.QuotationVersionId,
        FeeMilestoneId = milestone.Id, Kind = CommercialDocumentKinds.PaymentReceipt, TemplateVersion = CommercialDocumentRenderer.SettlementReceiptTemplate,
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
          $"{milestone.Kind} paid and receipt generated. No client contact email is on file, so no email was queued."));

      var isAdvance = milestone.Kind == FeeMilestoneKinds.Advance;
      var subject = $"Payment receipt {document.FileName.Replace("Receipt-", string.Empty).Replace(".docx", string.Empty)} — {profile.LegalName}";
      var body = isAdvance
        ? $"Dear {client.LegalName},\n\nWe confirm that your advance of {invoice.Total.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} " +
          $"against invoice {invoice.InvoiceNumber} is now paid in full. The remaining balance is invoiced on delivery of the final report.\n\n" +
          $"Your official receipt voucher is available for authenticated download at /api/commercial/documents/{document.Id}/download and is retained on your engagement record.\n\n{profile.LegalName}"
        : $"Dear {client.LegalName},\n\nWe confirm that your final balance of {invoice.Total.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} " +
          $"against invoice {invoice.InvoiceNumber} is now paid in full. The engagement fee is now settled in full.\n\n" +
          $"Your official receipt voucher is available for authenticated download at /api/commercial/documents/{document.Id}/download and is retained on your engagement record.\n\n{profile.LegalName}";

      db.CommercialNotifications.Add(new CommercialNotification
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Kind = CommercialNotificationKinds.Receipt,
        FeeMilestoneId = milestone.Id, PracticeClientId = agreement.PracticeClientId, DocumentId = document.Id,
        OfferSha256 = document.Sha256Hex, Recipient = recipient.Email,
        RecipientContactId = recipient.ContactId, RecipientRoutingId = recipient.RoutingId, RecipientAuthorityBasis = recipient.AuthorityBasis,
        Subject = subject,
        Body = body,
        CreatedAt = DateTimeOffset.UtcNow
      });
      try { await db.SaveChangesAsync(ct); }
      catch (DbUpdateException) { /* a concurrent retry queued it first: the unique index guarantees exactly one */ }
    }
    return CommandResult<AdvancePaymentOutcome>.Ok(new(receipt.Id, invoice.Total, 0m, true, document.Id, true,
      $"{milestone.Kind} paid: official receipt generated and email queued for delivery."));
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
      var outstanding = invoice is null ? milestone.Amount : milestone.Kind == FeeMilestoneKinds.Advance
        ? MoneyPolicy.Normalize(Math.Max(0m, milestone.Amount - await BillingService.GetNetAppliedToInvoiceAsync(db, actor.FirmId, invoice.Id, ct)))
        : await OutstandingAsync(db, actor.FirmId, invoice, ct);
      // This untracked read projection reflects current settlement while the persisted
      // PAID milestone and original settlement receipt remain immutable history.
      if (milestone.State == FeeMilestoneStates.Paid && !await IsMilestoneCurrentlyPaidAsync(db, milestone, ct))
        milestone.State = FeeMilestoneStates.Invoiced;
      views.Add(new(milestone, invoice?.InvoiceNumber, invoice?.Status, milestone.Amount - outstanding, outstanding));
    }
    var released = await HasFinancialPackageReleaseAsync(db, actor.FirmId, agreement.PracticeClientId, agreement.EngagementId, ct);
    return CommandResult<FeeAgreementView?>.Ok(new(agreement, views.OrderBy(x => x.Milestone.Kind == FeeMilestoneKinds.Advance ? 0 : 1).ToList(), released));
  }

  private static async Task<decimal> OutstandingAsync(IAuditSphereDbContext db, Guid firmId, Invoice invoice, CancellationToken ct)
  {
    var allocated = await BillingService.GetNetAppliedToInvoiceAsync(db, firmId, invoice.Id, ct);
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

  internal static async Task<PaymentRecipientSnapshot?> ResolveRecipientAsync(IAuditSphereDbContext db, Guid firmId, EngagementFeeAgreement agreement, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var asOf = DateOnly.FromDateTime(now.UtcDateTime);
    // 1. Check purpose-specific Finance routing first
    var financeContact = await (
      from r in db.ClientContactRoutings.AsNoTracking()
      where r.FirmId == firmId && r.PracticeClientId == agreement.PracticeClientId && r.Purpose == CorrespondencePurposes.Finance && r.RevokedAt == null
      where (r.EffectiveFrom == null || r.EffectiveFrom <= asOf) &&
            (r.EffectiveTo == null || r.EffectiveTo >= asOf)
      join c in db.ClientContacts.AsNoTracking() on r.ClientContactId equals c.Id
      where c.FirmId == firmId && c.PracticeClientId == agreement.PracticeClientId &&
            c.IsActive && !string.IsNullOrWhiteSpace(c.Email) &&
            (c.ValidFrom == null || c.ValidFrom <= now) &&
            (c.ValidTo == null || c.ValidTo >= now)
      orderby r.IsPrimaryForPurpose descending, r.CreatedAt descending
      select new PaymentRecipientSnapshot(c.Email, c.Id, r.Id, PaymentRecipientAuthorityBases.FinanceRouting)
    ).FirstOrDefaultAsync(ct);

    if (financeContact is not null) return financeContact;

    var contact = await db.ClientContacts.AsNoTracking().Where(x => x.FirmId == firmId && x.PracticeClientId == agreement.PracticeClientId && x.Primary && x.IsActive &&
      (x.ValidFrom == null || x.ValidFrom <= now) && (x.ValidTo == null || x.ValidTo >= now)).Select(x => new PaymentRecipientSnapshot(x.Email, x.Id, null, PaymentRecipientAuthorityBases.ClientPrimaryContact)).FirstOrDefaultAsync(ct);
    if (contact is not null && !string.IsNullOrWhiteSpace(contact.Email)) return contact;
    var proposal = await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == agreement.ProposalId && x.FirmId == firmId, ct);
    var lead = await (from o in db.Opportunities.AsNoTracking() join l in db.Leads.AsNoTracking() on o.LeadId equals l.Id
                      where o.Id == proposal.OpportunityId && o.FirmId == firmId && l.FirmId == firmId select l.PrimaryContactEmail).FirstOrDefaultAsync(ct);
    return string.IsNullOrWhiteSpace(lead) ? null
      : new(lead, null, null, PaymentRecipientAuthorityBases.ProspectPrimaryContact);
  }

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);
}
