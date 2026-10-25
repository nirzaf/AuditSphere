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
  public const string ClientAction = "CLIENT_ACTION";
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
  // Zero denotes a pre-lineage row from before submission-bound revisions were recorded.
  public long RequestRevision { get; set; }
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

/// <summary>
/// Immutable record of the exact provider-verified upload version accepted by the assigned
/// reviewer for a PBC request. Later resubmissions remain separate upload intents.
/// </summary>
public sealed class PbcAcceptanceReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcRequestId { get; set; }
  public long AcceptedRequestRevision { get; set; }
  public long SourceRequestRevision { get; set; }
  public Guid ReviewerUserId { get; set; }
  public DateTimeOffset AcceptedAt { get; set; }
  public Guid PbcUploadIntentId { get; set; }
  public long UploadIntentRevision { get; set; }
  public Guid UploaderUserId { get; set; }
  public string FileName { get; set; } = string.Empty;
  public long ByteCount { get; set; }
  public string ContentSha256Hex { get; set; } = string.Empty;
  public Guid TransferOperationId { get; set; }
  public string ProviderIdentity { get; set; } = string.Empty;
  public string ProviderReceiptDigest { get; set; } = string.Empty;
  public DateTimeOffset ProviderRegisteredAt { get; set; }
  public string SourceDigest { get; set; } = string.Empty;
}

/// <summary>
/// Append-only association of one provider-verified PBC upload with the scoped document reference
/// and exact-version snapshot of its selected-site item. It is technical registration evidence,
/// not a review decision, and never changes the upload revision an acceptance receipt pins.
/// </summary>
public sealed class PbcDocumentRegistration
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcUploadIntentId { get; set; }
  public Guid TransferOperationId { get; set; }
  public Guid DocumentReferenceId { get; set; }
  public Guid DocumentSnapshotId { get; set; }
  public string ProviderIdentity { get; set; } = string.Empty;
  public string BindingSha256Hex { get; set; } = string.Empty;
  public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>Where a registered PBC upload's bytes are read from. No row means the pre-cutover local read.</summary>
public static class PbcReadSources
{
  public const string LocalStaged = "LOCAL_STAGED";
  public const string SelectedSite = "SELECTED_SITE";
}

/// <summary>Whether the temporary local staging copy of a registered upload is still retained.</summary>
public static class PbcLocalCopyStates
{
  public const string Retained = "RETAINED";
  public const string Released = "RELEASED";
}

public static class PbcStorageEventKinds
{
  public const string RemoteReadActivated = "REMOTE_READ_ACTIVATED";
  public const string RemoteReadReverted = "REMOTE_READ_REVERTED";
  public const string LocalCopyReleased = "LOCAL_COPY_RELEASED";
}

/// <summary>
/// Current storage disposition of one registered PBC upload, separate from its business state and from
/// the revisions an acceptance receipt pins. Every revision has exactly one append-only event.
/// </summary>
public sealed class PbcStorageDisposition
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcUploadIntentId { get; set; }
  public Guid PbcDocumentRegistrationId { get; set; }
  public string ReadSource { get; set; } = PbcReadSources.LocalStaged;
  public string LocalCopy { get; set; } = PbcLocalCopyStates.Retained;
  public long Revision { get; set; } = 1;
  public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Append-only transition evidence for a <see cref="PbcStorageDisposition"/> revision.</summary>
public sealed class PbcStorageEvent
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcStorageDispositionId { get; set; }
  public long Revision { get; set; }
  public string Kind { get; set; } = string.Empty;
  public string ReadSource { get; set; } = string.Empty;
  public string LocalCopy { get; set; } = string.Empty;
  public Guid? ActorUserId { get; set; }
  public Guid? OperationId { get; set; }
  public string Reason { get; set; } = string.Empty;
  public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// One administrator's exact reviewed storage command, tied to the real immutable effect it produced: the storage
/// event of an activation, revert or release, or the registration of a backfill. Never matched from current history.
/// </summary>
public sealed class PbcStorageCommandReceipt
{
  public const int MaximumInputLength = 4000;
  public const int MaximumPreviewLength = 100000;
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcUploadIntentId { get; set; }
  public Guid? PbcStorageEventId { get; set; }
  public Guid? PbcDocumentRegistrationId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public string InputJson { get; set; } = string.Empty;
  public string PreviewJson { get; set; } = string.Empty;
  public string OutcomeJson { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
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
  // Present only for idempotent client replies; legacy and staff messages remain null.
  public Guid? ClientRequestId { get; set; }
  public string? ClientRequestSha256 { get; set; }
  public DateTimeOffset? DeliveredAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Append-only idempotency receipt for a first-party PBC reminder action. The source event is the persisted PBC
/// request; action keys identify a due window and never alter the request's authoritative state.
/// </summary>
public sealed class PbcReminderReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcRequestId { get; set; }
  public Guid RuleRevisionId { get; set; }
  public string ActionKey { get; set; } = string.Empty;
  public Guid CommunicationId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
