// Commercial calculation and documents: versioned quotations, a configurable approval matrix, branded documents,
// and the agreed-fee 50% advance / 50% balance cycle. Amounts are decimal; documents are immutable once generated.
namespace AuditSphereOps.Domain.Practice;

public static class QuotationStates
{
  public const string Draft = "DRAFT";
  public const string PendingApproval = "PENDING_APPROVAL";
  public const string Approved = "APPROVED";
  public const string Superseded = "SUPERSEDED";
}

/// <summary>Durable delivery-receipt states for commercial email (STE 4.1.2): a queued offer, an in-flight send
/// attempt, a provider-verified receipt, explicit delivery evidence, a provider refusal and an unknown outcome are
/// all distinct and never conflated.</summary>
public static class CommercialDeliveryStates
{
  public const string Queued = "QUEUED";
  public const string Dispatched = "DISPATCHED";
  public const string ProviderAccepted = "PROVIDER_ACCEPTED";
  public const string Delivered = "DELIVERED";
  public const string Rejected = "REJECTED";
  public const string Unknown = "UNKNOWN";
  /// <summary>True once the mail provider's receipt is verified: the send is terminal and never repeated.</summary>
  public static bool IsProviderReceipt(string state) => state is ProviderAccepted or Delivered;
}

/// <summary>
/// Firm-approved pricing basis and limits (STE 4.1.2): versioned rows authored by a rule administrator and approved
/// by a distinct one. The latest approved row for a currency bounds new quotations and sets their validity window;
/// it never alters historical documents.
/// </summary>
public sealed class FirmPricingPolicy
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public long Version { get; set; } = 1;
  public string Currency { get; set; } = string.Empty;
  public decimal? MinimumFee { get; set; }
  public decimal? MaximumFee { get; set; }
  public decimal? MaxDiscountPercent { get; set; }
  public int ValidityDays { get; set; } = 30;
  public string Status { get; set; } = FirmPricingPolicyStates.Draft;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

public static class FirmPricingPolicyStates
{
  public const string Draft = "DRAFT";
  public const string PendingApproval = "PENDING_APPROVAL";
  public const string Approved = "APPROVED";
}

public static class CommercialRuleKinds
{
  /// <summary>Applies when the discount percentage is strictly above the threshold.</summary>
  public const string DiscountOver = "DISCOUNT_OVER_PERCENT";
  /// <summary>Applies whenever the quotation carries non-standard contractual terms.</summary>
  public const string NonStandardTerms = "NON_STANDARD_TERMS";
}

public static class CommercialDocumentKinds
{
  public const string Quotation = "QUOTATION";
  public const string ComprehensiveProposal = "COMPREHENSIVE_PROPOSAL";
  public const string EngagementLetter = "ENGAGEMENT_LETTER";
  public const string PaymentReceipt = "PAYMENT_RECEIPT";
}

public static class FeeMilestoneKinds
{
  public const string Advance = "ADVANCE";
  public const string Balance = "BALANCE";
}

public static class FeeMilestoneStates
{
  public const string Planned = "PLANNED";
  public const string Invoiced = "INVOICED";
  public const string Paid = "PAID";
}

/// <summary>One immutable calculated quotation for a proposal; recalculation creates a new revision.</summary>
public sealed class QuotationVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ProposalId { get; set; }
  public long Revision { get; set; } = 1;
  public string Currency { get; set; } = string.Empty;
  /// <summary>Canonical JSON of hour lines with the approved rate-card version each line used.</summary>
  public string LinesJson { get; set; } = "[]";
  public decimal ComplexityFactor { get; set; } = 1m;
  public decimal RiskPremiumPercent { get; set; }
  public decimal DiscountPercent { get; set; }
  public bool NonStandardTerms { get; set; }
  public string? NonStandardTermsNote { get; set; }
  public decimal BaseAmount { get; set; }
  public decimal ComplexityAmount { get; set; }
  public decimal RiskPremiumAmount { get; set; }
  public decimal DiscountAmount { get; set; }
  public decimal Fee { get; set; }
  public string InputHash { get; set; } = string.Empty;
  public string Status { get; set; } = QuotationStates.Draft;
  /// <summary>When the approved pricing policy defines a validity window, the offer expires at this instant; an expired offer cannot be dispatched or accepted.</summary>
  public DateTimeOffset? ValidUntil { get; set; }
  /// <summary>Canonical JSON of the approvals this version requires (rule, rule version, role).</summary>
  public string RequiredApprovalsJson { get; set; } = "[]";
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

/// <summary>Configurable approval matrix row. Rules are versioned and never edited in place.</summary>
public sealed class CommercialApprovalRule
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Kind { get; set; } = CommercialRuleKinds.DiscountOver;
  public long Version { get; set; } = 1;
  public decimal? ThresholdPercent { get; set; }
  public string RequiredRole { get; set; } = string.Empty;
  public bool Active { get; set; } = true;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class QuotationApproval
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid QuotationVersionId { get; set; }
  public string RuleKey { get; set; } = string.Empty;
  public string RequiredRole { get; set; } = string.Empty;
  public Guid ApprovedByUserId { get; set; }
  public string Reason { get; set; } = string.Empty;
  public DateTimeOffset ApprovedAt { get; set; }
}

/// <summary>
/// A required approval stands until this append-only record withdraws it. Approval rows themselves are never
/// edited — commercial approval history is append-only — so revocation is a distinct record that a distinct
/// role-holder signs, and the historical approval evidence is preserved unchanged.
/// </summary>
public sealed class QuotationApprovalRevocation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid QuotationApprovalId { get; set; }
  public string Reason { get; set; } = string.Empty;
  public Guid RevokedByUserId { get; set; }
  public DateTimeOffset RevokedAt { get; set; }
}

/// <summary>Firm letterhead used by generated commercial documents. Versioned; latest row applies.</summary>
public sealed class FirmCommercialProfile
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public long Version { get; set; } = 1;
  public string LegalName { get; set; } = string.Empty;
  public string Address { get; set; } = string.Empty;
  public string ContactEmail { get; set; } = string.Empty;
  public string ContactPhone { get; set; } = string.Empty;
  public string AccentColorHex { get; set; } = "#2B6CB0";
  public string ClosingText { get; set; } = string.Empty;
  public string FirmHistoryAndRegistrations { get; set; } = string.Empty;
  public string IndustryCredentials { get; set; } = string.Empty;
  public string AuditMethodology { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A generated, immutable commercial document with its template version and content hash.</summary>
public sealed class CommercialDocument
{
  public Guid? AcceptanceDecisionId { get; set; }
  public Guid? SignatureSpecimenId { get; set; }
  public Guid? FirmSealSpecimenId { get; set; }
  public DateTimeOffset? CommercialAcceptedAt { get; set; }
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid? ProposalId { get; set; }
  public Guid? QuotationVersionId { get; set; }
  public Guid? FeeMilestoneId { get; set; }
  public string Kind { get; set; } = CommercialDocumentKinds.Quotation;
  public string TemplateVersion { get; set; } = string.Empty;
  public long ProfileVersion { get; set; }
  public string FileName { get; set; } = string.Empty;
  public string ContentType { get; set; } = string.Empty;
  public byte[] Bytes { get; set; } = [];
  public string Sha256Hex { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The agreed fee for an accepted proposal, split into an advance and a balance milestone.</summary>
public sealed class EngagementFeeAgreement
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ProposalId { get; set; }
  public Guid QuotationVersionId { get; set; }
  public Guid PracticeClientId { get; set; }
  public Guid? EngagementId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal AgreedFee { get; set; }
  public decimal AdvancePercent { get; set; } = 50m;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FeeMilestone
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid AgreementId { get; set; }
  public string Kind { get; set; } = FeeMilestoneKinds.Advance;
  public decimal Amount { get; set; }
  public string State { get; set; } = FeeMilestoneStates.Planned;
  public Guid? InvoiceId { get; set; }
  public Guid? ReceiptId { get; set; }
  public DateTimeOffset? PaidAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Queued email owned by the commercial workflow; delivered by the isolated mail worker.</summary>
public sealed class CommercialNotification
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Kind { get; set; } = CommercialNotificationKinds.Receipt;
  public Guid? FeeMilestoneId { get; set; }
  public Guid? ProposalId { get; set; }
  public Guid? PracticeClientId { get; set; }
  /// <summary>The approved quotation revision the dispatched offer was built from; downstream letters and fee agreements must use it.</summary>
  public Guid? QuotationVersionId { get; set; }
  public Guid? DocumentId { get; set; }
  /// <summary>Exact identity of the dispatched offer (generated artifact hash, or the reviewed offer digest when no document exists).</summary>
  public string? OfferSha256 { get; set; }
  public string Recipient { get; set; } = string.Empty;
  public string Subject { get; set; } = string.Empty;
  public string Body { get; set; } = string.Empty;
  public string DeliveryState { get; set; } = "QUEUED";
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? DeliveredAt { get; set; }
  /// <summary>The exact holding letter dispatched by this notification (STE 3.3); one dispatch per letter identity.</summary>
  public Guid? DeliverableId { get; set; }
  /// <summary>Digest of the exact outstanding critical set; one dispatch per blocker set, regardless of regenerated letter identities.</summary>
  public string? DispatchKey { get; set; }
}

public static class CommercialNotificationKinds
{
  public const string Receipt = "RECEIPT";
  public const string Proposal = "PROPOSAL";
  public const string HoldingLetter = "HOLDING_LETTER";
}
