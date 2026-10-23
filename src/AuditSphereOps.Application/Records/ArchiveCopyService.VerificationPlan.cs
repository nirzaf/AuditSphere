using System.Data;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Records;

/// <summary>
/// Exact schema consumed by Verify-ArchiveCopies.ps1. This contains pinned identities and
/// expected byte metadata only; it never contains source bytes, observed provider results or credentials.
/// </summary>
public sealed record ArchiveCopyVerificationPlan(
  int SchemaVersion,
  Guid OperationId,
  Guid FirmId,
  string TenantId,
  Guid ClientId,
  Guid EngagementId,
  Guid ArchiveId,
  Guid ManifestId,
  long ManifestVersion,
  string ManifestSha256,
  Guid RecordsProfileId,
  long RecordsProfileVersion,
  Guid ReleaseId,
  Guid ReleasedArtifactId,
  Guid CheckpointId,
  Guid? ClientSharePointSiteId,
  Guid TargetRevisionId,
  long TargetRevision,
  string TargetConfigurationSha256,
  string AuthorizedStaffSha256,
  string DestinationTenantId,
  string DestinationSiteId,
  string DestinationDriveId,
  string DestinationLibraryRootItemId,
  string StaffGroupId,
  string ExpectedPermissionsSha256,
  IReadOnlyList<ArchiveCopyVerificationPlanFile> Files);

public sealed record ArchiveCopyVerificationPlanFile(
  string SourceKind,
  string SourceKeySha256,
  Guid? SourceId,
  Guid? ManifestEntryId,
  string SourceVersion,
  string SourceSha256,
  long SourceByteCount,
  string RelativePath);

public static partial class ArchiveCopyService
{
  private const int MaximumVerificationPlanFiles = 1000;

  public static async Task<CommandResult<ArchiveCopyVerificationPlan>> GetVerificationPlanAsync(
    IRecordsArchiveCopyDbContext db,
    IReleaseCheckpointStore checkpointStore,
    ActorContext actor,
    Guid operationId,
    CancellationToken ct = default)
  {
    if (operationId == Guid.Empty)
      return CommandResult<ArchiveCopyVerificationPlan>.Fail(
        "archive-copy-verification-plan.invalid", "Select an exact archive-copy operation.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<ArchiveCopyVerificationPlan>.Fail(ErrorCodes.GateBlocked,
        "Archive-copy verification plans require an independent read snapshot.");

    ArchiveCopyOperationPayload payload;
    ArchiveCopyVerificationPlan pinnedPlan;
    string pinnedCheckpointReference;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var operation = await db.DurableOperations.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == operationId && x.FirmId == actor.FirmId && x.OperationKind == ArchiveCopyHandler.Kind, ct);
      if (operation is null)
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      if (!TryDecodePayload(operation, out var decodedPayload))
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          "archive-copy-verification-plan.invalid", "Operation evidence is unavailable.");
      payload = decodedPayload!;

      if (payload.FirmId != operation.FirmId || payload.ClientId != operation.ClientId ||
          payload.EngagementId != operation.EngagementId)
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

      var authorization = await AuthorizeArchiveAsync(db, actor, payload.ClientId, payload.EngagementId, ct);
      if (!authorization.Succeeded)
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(authorization.ErrorCode!, authorization.Message!);

      ArchiveCopySourceSet sources;
      IReadOnlyList<ArchiveCopyVerificationPlanFile> files;
      try
      {
        await ArchiveCopySourceGuard.LockRowsAsync(db, actor.FirmId, payload, ct);
        sources = await ArchiveCopySourceGuard.ResolveAsync(db, payload, includeArtifactBytes: false, ct);
        files = ArchiveCopyHandler.ExpectedFilesForVerification(sources, payload);
      }
      catch (OperationBlockedException exception)
      {
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          exception.Authorization ? ErrorCodes.ScopeDenied : exception.Code,
          exception.Authorization ? "Access denied." : "The approved archive sources or current target are unavailable.");
      }

      if (files.Count is < 2 or > MaximumVerificationPlanFiles)
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          "archive-copy-verification-plan.bounds", "The exact source file set is outside verifier limits.");
      var checkpointFiles = GetPinnedCheckpointFiles(files, payload.CheckpointId);
      if (checkpointFiles.Length != 1 || checkpointFiles[0].SourceByteCount != -1 ||
          files.Any(x => x.SourceByteCount < 0 && x.SourceKind != ArchiveCopySourceKinds.CheckpointManifest))
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          "archive-copy-verification-plan.invalid", "An exact source byte count is unavailable.");

      pinnedPlan = CreateVerificationPlan(operation, payload, sources, files);
      pinnedCheckpointReference = sources.Checkpoint.StoredReference;
      await snapshot.CommitAsync(ct);
    }

    var preReadArchive = await db.Archives.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.Id == payload.ArchiveId)
      .Select(x => new { x.ClientId, x.EngagementId }).SingleOrDefaultAsync(ct);
    if (preReadArchive is null || preReadArchive.ClientId != payload.ClientId ||
        preReadArchive.EngagementId != payload.EngagementId)
      return CommandResult<ArchiveCopyVerificationPlan>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var preReadAuthorization = await AuthorizeArchiveAsync(db, actor, payload.ClientId, payload.EngagementId, ct);
    if (!preReadAuthorization.Succeeded)
      return CommandResult<ArchiveCopyVerificationPlan>.Fail(preReadAuthorization.ErrorCode!, preReadAuthorization.Message!);

    CheckpointContent? checkpoint;
    try
    {
      // Checkpoint storage may be external. The first database snapshot has been committed and disposed here.
      checkpoint = await checkpointStore.ReadVerifiedAsync(pinnedCheckpointReference,
        payload.CheckpointReadBackSha256, ct);
    }
    catch (OperationBlockedException)
    {
      return CommandResult<ArchiveCopyVerificationPlan>.Fail(
        "archive-copy-checkpoint-unavailable", "The exact pinned checkpoint bytes are unavailable.");
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
    {
      return CommandResult<ArchiveCopyVerificationPlan>.Fail(
        "archive-copy-checkpoint-unavailable", "The exact pinned checkpoint bytes are unavailable.");
    }

    if (checkpoint is null || checkpoint.Receipt is null || checkpoint.Bytes is null ||
        !string.Equals(checkpoint.Receipt.Reference, pinnedCheckpointReference, StringComparison.Ordinal) ||
        !string.Equals(checkpoint.Receipt.ContentSha256Hex, payload.CheckpointReadBackSha256, StringComparison.Ordinal) ||
        checkpoint.Receipt.ByteCount != checkpoint.Bytes.LongLength ||
        !string.Equals(Hashing.Sha256Hex(checkpoint.Bytes), payload.CheckpointReadBackSha256, StringComparison.Ordinal))
      return CommandResult<ArchiveCopyVerificationPlan>.Fail(
        "archive-copy-checkpoint-content-mismatch", "The exact pinned checkpoint bytes did not match their approved digest.");

    ArchiveCopyVerificationPlan verifiedPlan;
    await using (var refreshedSnapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var operation = await db.DurableOperations.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == operationId && x.FirmId == actor.FirmId && x.OperationKind == ArchiveCopyHandler.Kind, ct);
      if (operation is null)
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      if (!TryDecodePayload(operation, out var decodedPayload))
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          "archive-copy-verification-plan.invalid", "Operation evidence is unavailable.");
      var refreshedPayload = decodedPayload!;
      if (refreshedPayload.FirmId != operation.FirmId || refreshedPayload.ClientId != operation.ClientId ||
          refreshedPayload.EngagementId != operation.EngagementId || refreshedPayload != payload)
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

      var refreshedAuthorization = await AuthorizeArchiveAsync(db, actor, payload.ClientId, payload.EngagementId, ct);
      if (!refreshedAuthorization.Succeeded)
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(refreshedAuthorization.ErrorCode!, refreshedAuthorization.Message!);

      ArchiveCopySourceSet refreshedSources;
      IReadOnlyList<ArchiveCopyVerificationPlanFile> refreshedFiles;
      try
      {
        await ArchiveCopySourceGuard.LockRowsAsync(db, actor.FirmId, refreshedPayload, ct);
        refreshedSources = await ArchiveCopySourceGuard.ResolveAsync(db, refreshedPayload, includeArtifactBytes: false, ct);
        refreshedFiles = ArchiveCopyHandler.ExpectedFilesForVerification(refreshedSources, refreshedPayload);
      }
      catch (OperationBlockedException exception)
      {
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          exception.Authorization ? ErrorCodes.ScopeDenied : exception.Code,
          exception.Authorization ? "Access denied." : "The approved archive sources or current target are unavailable.");
      }

      if (refreshedFiles.Count is < 2 or > MaximumVerificationPlanFiles)
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          "archive-copy-verification-plan.bounds", "The exact source file set is outside verifier limits.");
      if (!string.Equals(refreshedSources.Checkpoint.StoredReference, pinnedCheckpointReference, StringComparison.Ordinal))
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          "archive-copy-verification-plan.source-changed", "The pinned checkpoint reference changed during verification-plan export.");

      var refreshedPinnedPlan = CreateVerificationPlan(operation, refreshedPayload, refreshedSources, refreshedFiles);
      if (!SamePinnedSources(pinnedPlan, refreshedPinnedPlan))
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          "archive-copy-verification-plan.source-changed", "The approved archive sources or target changed during verification-plan export.");

      var refreshedCheckpointFiles = GetPinnedCheckpointFiles(refreshedFiles, payload.CheckpointId);
      if (refreshedCheckpointFiles.Length != 1 || refreshedCheckpointFiles[0].SourceByteCount != -1 ||
          refreshedFiles.Any(x => x.SourceByteCount < 0 && x.SourceKind != ArchiveCopySourceKinds.CheckpointManifest))
        return CommandResult<ArchiveCopyVerificationPlan>.Fail(
          "archive-copy-verification-plan.invalid", "An exact source byte count is unavailable.");

      verifiedPlan = refreshedPinnedPlan with
      {
        Files = refreshedFiles.Select(file => file.SourceKind == ArchiveCopySourceKinds.CheckpointManifest
          ? file with { SourceByteCount = checkpoint.Bytes.LongLength }
          : file).ToArray()
      };
      await refreshedSnapshot.CommitAsync(ct);
    }

    var currentArchive = await db.Archives.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.Id == payload.ArchiveId)
      .Select(x => new { x.ClientId, x.EngagementId }).SingleOrDefaultAsync(ct);
    if (currentArchive is null || currentArchive.ClientId != payload.ClientId ||
        currentArchive.EngagementId != payload.EngagementId)
      return CommandResult<ArchiveCopyVerificationPlan>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var currentAuthorization = await AuthorizeArchiveAsync(db, actor, payload.ClientId, payload.EngagementId, ct);
    if (!currentAuthorization.Succeeded)
      return CommandResult<ArchiveCopyVerificationPlan>.Fail(currentAuthorization.ErrorCode!, currentAuthorization.Message!);

    return CommandResult<ArchiveCopyVerificationPlan>.Ok(verifiedPlan);
  }

  private static bool TryDecodePayload(DurableOperation operation, out ArchiveCopyOperationPayload? payload)
  {
    payload = null;
    try
    {
      var request = new OperationRequest(operation.FirmId, operation.ClientId, operation.EngagementId,
        operation.OperationKind, operation.TargetId, operation.ExpectedRevision, operation.IdempotencyKey,
        operation.PayloadJson, operation.OriginatorId);
      var normalizedPayload = ArchiveCopyPayloadRules.Normalize(request);
      if (!OperationEncoding.Matches(operation, QueueOnlyHandler)) return false;
      payload = JsonSerializer.Deserialize<ArchiveCopyOperationPayload>(normalizedPayload);
      return payload is not null;
    }
    catch (Exception exception) when (exception is JsonException or OperationBlockedException or NotSupportedException)
    {
      return false;
    }
  }

  private static ArchiveCopyVerificationPlanFile[] GetPinnedCheckpointFiles(
    IReadOnlyList<ArchiveCopyVerificationPlanFile> files, Guid checkpointId) => files.Where(x =>
      x.SourceKind == ArchiveCopySourceKinds.CheckpointManifest && x.SourceId == checkpointId &&
      x.ManifestEntryId is null).ToArray();

  private static ArchiveCopyVerificationPlan CreateVerificationPlan(DurableOperation operation,
    ArchiveCopyOperationPayload payload, ArchiveCopySourceSet sources,
    IReadOnlyList<ArchiveCopyVerificationPlanFile> files)
  {
    var target = sources.Target;
    return new ArchiveCopyVerificationPlan(
      1, operation.Id, payload.FirmId, target.TenantId, payload.ClientId, payload.EngagementId,
      payload.ArchiveId, payload.ManifestId, payload.ManifestVersion, payload.ManifestSha256,
      payload.RecordsProfileId, payload.RecordsProfileVersion, payload.ReleaseId,
      payload.ReleasedArtifactId, payload.CheckpointId, target.ClientSharePointSiteId,
      target.Id, target.Revision, target.ConfigurationSha256, target.AuthorizedStaffSha256,
      target.TenantId, target.SiteId, target.DriveId, target.LibraryRootItemId,
      target.StaffGroupId, target.ExpectedPermissionsSha256, files);
  }

  private static bool SamePinnedSources(ArchiveCopyVerificationPlan before, ArchiveCopyVerificationPlan after) =>
    (before with { Files = after.Files }) == after && before.Files.SequenceEqual(after.Files);
}
