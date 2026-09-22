// Client PBC requests and upload receipts (§15, §43.5). Bytes stay behind the
// trusted transfer boundary; these rows record scope, intent and provenance.
namespace AuditSphereOps.Domain.Documents;

public static class PbcStates
{
  public const string Draft = "DRAFT";
  public const string Sent = "SENT";
  public const string Acknowledged = "ACKNOWLEDGED";
  public const string PartiallyReceived = "PARTIALLY_RECEIVED";
  public const string Received = "RECEIVED";
  public const string UnderReview = "UNDER_REVIEW";
  public const string Accepted = "ACCEPTED";
  public const string Closed = "CLOSED";
  public const string ClarificationRequired = "CLARIFICATION_REQUIRED";
  public const string Resubmitted = "RESUBMITTED";
}

public static class PbcUploadStates
{
  public const string Started = "STARTED";
  public const string Chunking = "CHUNKING";
  // STAGED: every declared byte was durably staged and its combined digest verified at the
  // trusted completion boundary; a durable provider transfer operation is queued and the
  // PBC request is not RECEIVED until that transfer completes or reconciles (§43.5).
  public const string Staged = "STAGED";
  public const string Received = "RECEIVED";
  public const string Failed = "FAILED";
  public const string Expired = "EXPIRED";
}

public static class PbcCommunicationKinds
{
  public const string Request = "REQUEST";
  public const string StaffMessage = "STAFF_MESSAGE";
  public const string ClientMessage = "CLIENT_MESSAGE";
  public const string Email = "EMAIL";
}

public static class PbcDeliveryStates
{
  public const string Queued = "QUEUED";
  public const string Sent = "SENT";
  public const string Failed = "FAILED";
  public const string Uncertain = "UNCERTAIN";
}

public sealed class PbcRequest
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string Objective { get; set; } = string.Empty;
  public string EntityScope { get; set; } = string.Empty;
  public string PeriodStart { get; set; } = string.Empty;
  public string PeriodEnd { get; set; } = string.Empty;
  public string Area { get; set; } = string.Empty;
  public string RequestedFormat { get; set; } = string.Empty;
  public string ControlTotals { get; set; } = string.Empty;
  public Guid ClientOwnerUserId { get; set; }
  public Guid FirmOwnerUserId { get; set; }
  public Guid ReviewerUserId { get; set; }
  public string DueDate { get; set; } = string.Empty;
  public string Confidentiality { get; set; } = string.Empty;
  public string AcceptanceCriteria { get; set; } = string.Empty;
  public string State { get; set; } = PbcStates.Draft;
  public long Revision { get; set; } = 1;
  public string? ClarificationReason { get; set; }
  public Guid? AcceptedByUserId { get; set; }
  public DateTimeOffset? AcceptedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PbcUploadIntent
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcRequestId { get; set; }
  public Guid UploaderUserId { get; set; }
  public string FileName { get; set; } = string.Empty;
  public string ContentType { get; set; } = string.Empty;
  public long DeclaredByteCount { get; set; }
  public string DeclaredSha256Hex { get; set; } = string.Empty;
  public string CapabilityHash { get; set; } = string.Empty;
  public long ReceivedByteCount { get; set; }
  public string State { get; set; } = PbcUploadStates.Started;
  public DateTimeOffset ExpiresAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? CompletedAt { get; set; }
  public string? FinalSha256Hex { get; set; }
  public string? FailureReason { get; set; }
  // Provider handoff evidence (§43.5 items 6–7): the durable transfer operation and the
  // verified provider registration are recorded before the intent is RECEIVED.
  public Guid? TransferOperationId { get; set; }
  public string? ProviderReceiptDigest { get; set; }
  public DateTimeOffset? ProviderRegisteredAt { get; set; }
  public long Revision { get; set; } = 1;
}

public sealed class PbcUploadChunk
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcUploadIntentId { get; set; }
  public int ChunkIndex { get; set; }
  public long Offset { get; set; }
  public int ByteCount { get; set; }
  public string Sha256Hex { get; set; } = string.Empty;
  public string? StagedPath { get; set; }
  public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>Append-only, client-visible communication for one request thread.</summary>
public sealed class PbcCommunication
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcRequestId { get; set; }
  public Guid AuthorUserId { get; set; }
  public string Kind { get; set; } = string.Empty;
  public string Body { get; set; } = string.Empty;
  public string? RecipientEmail { get; set; }
  public string? Subject { get; set; }
  public string? PortalUrl { get; set; }
  public string? DeliveryState { get; set; }
  public string? ProviderCorrelationId { get; set; }
  public DateTimeOffset? DeliveredAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
