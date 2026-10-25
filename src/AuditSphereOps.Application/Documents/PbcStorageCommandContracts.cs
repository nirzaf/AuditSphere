namespace AuditSphereOps.Application.Documents;

public static class PbcStorageCommandActions
{
  public const string ActivateRemoteRead = "ACTIVATE_REMOTE_READ";
  public const string RevertToLocalRead = "REVERT_TO_LOCAL_READ";
  public const string ReleaseLocalCopy = "RELEASE_LOCAL_COPY";
  public const string BackfillRegistration = "BACKFILL_REGISTRATION";
}

/// <summary>One upload and one action; the expected registration, revision and hashes come from the reviewed state.</summary>
public sealed record PbcStorageCommandInput(string Action, Guid UploadIntentId, string Reason);

public sealed record PbcStorageCommandRequest(
  Guid RequestId, PbcStorageCommandInput Input, string? ReviewBasis = null,
  string? ExpectedRequestHash = null, bool Reviewed = false);

/// <summary>Server-projected storage facts for one upload. Revisions and sizes are exact strings.</summary>
public sealed record PbcStorageSnapshot(
  Guid UploadIntentId, Guid ClientId, Guid EngagementId, string FileName, string UploadState, string? RequestState,
  string Sha256Hex, string ByteCount, Guid? RegistrationId, string? ProviderIdentity, string? VersionId,
  string ReadSource, string LocalCopy, string DispositionRevision, bool CleanupEnabled, string CleanupDisposition,
  string CleanupPathClass, string CleanupEffect, string ChunkCount, string BackfillDisposition);

/// <summary>Capabilities are server decisions; the caller never infers an allowed action.</summary>
public sealed record PbcStorageCommandState(
  PbcStorageSnapshot Snapshot, string ReviewBasis, bool CanActivateRemoteRead, bool CanRevertToLocalRead,
  bool CanReleaseLocalCopy, bool CanBackfillRegistration, IReadOnlyList<string> ActivateBlockers,
  IReadOnlyList<string> RevertBlockers, IReadOnlyList<string> ReleaseBlockers, IReadOnlyList<string> BackfillBlockers);

public sealed record PbcStorageCommandPreview(
  Guid RequestId, string RequestHash, string ReviewBasis, PbcStorageCommandInput CanonicalInput,
  PbcStorageSnapshot Before, bool CanProceed, IReadOnlyList<string> Blockers, string Summary);

/// <summary>The retained effect: the new disposition for read-source changes and releases, the registration for a backfill.</summary>
public sealed record PbcStorageCommandOutcome(
  string? ReadSource, string? LocalCopy, string? DispositionRevision, Guid? StorageEventId, Guid? RegistrationId);

/// <summary>Staged files removed by this call. Reported per call, never retained; a replay retries remaining deletes.</summary>
public sealed record PbcStorageLocalCopyDeletion(int DeletedChunks, int AlreadyAbsentChunks, string? FailureCode);

public sealed record PbcStorageCommandReceiptView(
  Guid Id, Guid UploadIntentId, Guid ActorId, string ActorEpoch, Guid RequestId, string RequestHash, string ReviewBasis,
  string Action, DateTimeOffset CreatedAt, PbcStorageCommandPreview Preview, PbcStorageCommandOutcome Outcome,
  PbcStorageLocalCopyDeletion? LocalCopyDeletion);

public sealed record PbcStorageCommandLookup
{
  public bool Found { get; }
  public PbcStorageCommandReceiptView? Receipt { get; }

  public PbcStorageCommandLookup(bool found, PbcStorageCommandReceiptView? receipt)
  {
    if (found != (receipt is not null)) throw new ArgumentException("Found must exactly match receipt presence.");
    Found = found;
    Receipt = receipt;
  }
}
