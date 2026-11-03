using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Records;

public static partial class ArchiveCopyService
{
  private const string CopyNotRequested = "NOT_REQUESTED";
  private const string CopyRequested = "REQUESTED";
  private const string CopyObserved = "OBSERVED";
  private const string CopyBlocked = "BLOCKED_EXTERNAL";

  private const string NotRequestedNote =
    "No SharePoint secondary-copy request is recorded. The AuditSphere release/archive store remains authoritative.";
  private const string RequestedNote =
    "A SharePoint secondary-copy request is recorded, but matching file and permission readbacks are not yet verified.";
  private const string ObservedNote =
    "Persisted file-hash and permission readbacks matched this reviewed manifest at the recorded times. This is a historical observation, not a live permission check or a claim of write-once or administrator-proof immutability. The AuditSphere release/archive store remains authoritative.";
  private const string BlockedNote =
    "Matching persisted file and permission readbacks for the current reviewed manifest are unavailable or do not match. No SharePoint protection or immutability is claimed; the AuditSphere release/archive store remains authoritative.";

  /// <summary>
  /// Projects the latest exact-scope secondary-copy request for records custodians. It never calls SharePoint and never changes RecordsAction or archive state.
  /// </summary>
  public static async Task<ArchiveCopyStatusProjection?> ReadLatestStatusAsync(
    IRecordsArchiveCopyDbContext db, ActorContext actor, Guid archiveId, CancellationToken ct = default)
  {
    var archive = await db.Archives.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == archiveId && x.FirmId == actor.FirmId, ct);
    if (archive is null) return null;

    var authorization = await AuthorizeArchiveAsync(db, actor, archive.ClientId, archive.EngagementId, ct);
    if (!authorization.Succeeded) return null;

    var manifest = await LatestManifestAsync(db, actor.FirmId, archive.Id, ct);
    var operationQuery = db.DurableOperations.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
      x.OperationKind == ArchiveCopyHandler.Kind && x.TargetId == archive.Id);
    var operation = await operationQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    if (operation is null)
      return new(CopyNotRequested, null, null, manifest?.Version, manifest?.ManifestDigest,
        null, null, NotRequestedNote);

    if (!IsCurrentReviewedManifest(manifest) || operation.ExpectedRevision != manifest!.Version)
      return Blocked(operation, manifest);

    var readbackResult = await ReadEvidenceAsync(db, actor, operation.Id, ct);
    if (!readbackResult.Succeeded)
    {
      if (readbackResult.ErrorCode == ErrorCodes.ScopeDenied) return null;
      return Blocked(operation, manifest);
    }

    var readback = readbackResult.Value!;
    if (readback.OperationId != operation.Id || readback.FirmId != actor.FirmId ||
        readback.ClientId != archive.ClientId || readback.EngagementId != archive.EngagementId)
      return Blocked(operation, manifest);

    var latestOperation = await operationQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    var currentArchive = await db.Archives.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == archive.Id && x.FirmId == actor.FirmId, ct);
    var currentManifest = currentArchive is null ? null : await LatestManifestAsync(db, actor.FirmId, archive.Id, ct);
    if (latestOperation is null || latestOperation.Id != operation.Id || currentArchive is null ||
        currentArchive.ClientId != archive.ClientId || currentArchive.EngagementId != archive.EngagementId ||
        !SameManifest(manifest, currentManifest) || latestOperation.Status.ToString() != readback.OperationStatus)
      return Blocked(latestOperation ?? operation, currentManifest ?? manifest);

    if (readback.OperationStatus is nameof(OperationState.PENDING) or nameof(OperationState.CLAIMED))
      return new(CopyRequested, operation.Id, null, manifest.Version, manifest.ManifestDigest,
        null, null, RequestedNote);

    if (readback.OperationStatus != nameof(OperationState.COMPLETED) ||
        readback.Result is not { Files: not null, Permissions: not null } result ||
        result.OperationId != operation.Id || latestOperation.AttemptToken < 1 ||
        result.AttemptToken != latestOperation.AttemptToken)
      return Blocked(operation, manifest);

    var target = await db.ArchiveCopyTargetRevisions.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ClientId == archive.ClientId && x.TargetCode == "SECONDARY_ARCHIVE")
      .OrderByDescending(x => x.Revision).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    if (target is null || !target.HasValidDigests() ||
        !await HasExactObservedReadbacksAsync(db, archive, manifest, target, result, ct))
      return Blocked(operation, manifest, result.TargetRevision);

    var latestFileReadBackAt = result.Files.Max(x => x.ReadBackAt);
    return new(CopyObserved, operation.Id, target.Revision, manifest.Version, manifest.ManifestDigest,
      latestFileReadBackAt, result.Permissions.ObservedAt, ObservedNote);
  }

  private static Task<ArchiveManifest?> LatestManifestAsync(
    IRecordsArchiveCopyDbContext db, Guid firmId, Guid archiveId, CancellationToken ct) =>
    db.ArchiveManifests.AsNoTracking().Where(x => x.FirmId == firmId && x.ArchiveId == archiveId)
      .OrderByDescending(x => x.Version).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);

  private static bool IsCurrentReviewedManifest(ArchiveManifest? manifest) =>
    manifest is { Status: "REVIEWED", CompletenessStatus: "COMPLETE", SupersededByManifestId: null };

  private static bool SameManifest(ArchiveManifest? left, ArchiveManifest? right) =>
    left is not null && right is not null && left.Id == right.Id && left.Version == right.Version &&
    string.Equals(left.ManifestDigest, right.ManifestDigest, StringComparison.OrdinalIgnoreCase) &&
    left.Status == right.Status && left.CompletenessStatus == right.CompletenessStatus &&
    left.SupersededByManifestId == right.SupersededByManifestId;

  private static ArchiveCopyStatusProjection Blocked(
    DurableOperation operation, ArchiveManifest? manifest, long? targetRevision = null) =>
    new(CopyBlocked, operation.Id, targetRevision, manifest?.Version, manifest?.ManifestDigest,
      null, null, BlockedNote);

  private static async Task<bool> HasExactObservedReadbacksAsync(
    IRecordsArchiveCopyDbContext db, Archive archive, ArchiveManifest manifest,
    ArchiveCopyTargetRevision target, ArchiveCopyOperationResult result, CancellationToken ct)
  {
    if (result.OperationId == Guid.Empty || result.ArchiveId != archive.Id || result.ClientId != archive.ClientId ||
        result.ManifestId != manifest.Id || result.ManifestVersion != manifest.Version ||
        !string.Equals(result.ManifestSha256, manifest.ManifestDigest, StringComparison.OrdinalIgnoreCase) ||
        result.TargetRevisionId != target.Id || result.TargetRevision != target.Revision ||
        !string.Equals(result.TargetConfigurationSha256, target.ConfigurationSha256, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(result.AuthorizedStaffSha256, target.AuthorizedStaffSha256, StringComparison.OrdinalIgnoreCase) ||
        result.ProtocolOutcome != ArchiveCopyVerificationOutcomes.Match || result.Files.Count != manifest.EntryCount + 2 ||
        result.Files.Any(x => x.VerificationOutcome != ArchiveCopyVerificationOutcomes.Match ||
          !x.HasExactTargetBinding || x.SourceByteCount != x.ReadBackByteCount ||
          !string.Equals(x.SourceSha256, x.ReadBackSha256, StringComparison.OrdinalIgnoreCase) ||
          x.ReadBackAt == default || string.IsNullOrWhiteSpace(x.DestinationItemId) ||
          string.IsNullOrWhiteSpace(x.DestinationVersion)) ||
        result.Files.Select(x => x.SourceKeySha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Files.Count)
      return false;

    var expectedEntries = await db.ArchiveManifestEntries.AsNoTracking().Where(x =>
        x.FirmId == archive.FirmId && x.ArchiveManifestId == manifest.Id)
      .OrderBy(x => x.Ordinal)
      .Select(x => new
      {
        x.Id, x.Ordinal, x.EntryKind, x.SourceKind, x.SourceId, x.RelativeName,
        x.ContentHash, x.ByteCount, x.Required, x.MetadataJson
      })
      .ToListAsync(ct);
    if (expectedEntries.Count != manifest.EntryCount) return false;
    var entryDigest = JsonSerializer.Serialize(expectedEntries.Select(x => new
    {
      x.Ordinal, x.EntryKind, x.SourceKind, x.SourceId, x.RelativeName, x.ContentHash,
      x.ByteCount, x.Required, x.MetadataJson
    }));
    if (!string.Equals(Hashing.Sha256Hex(entryDigest), manifest.ManifestDigest, StringComparison.OrdinalIgnoreCase))
      return false;

    var manifestFiles = result.Files.Where(x => x.SourceKind == ArchiveCopySourceKinds.ReviewedManifestEntry).ToArray();
    if (manifestFiles.Length != expectedEntries.Count ||
        result.Files.Count(x => x.SourceKind == ArchiveCopySourceKinds.CheckpointManifest) != 1 ||
        result.Files.Count(x => x.SourceKind == ArchiveCopySourceKinds.ReleasedArtifact) != 1 ||
        result.Files.Any(x => x.SourceKind is not (ArchiveCopySourceKinds.ReviewedManifestEntry or
          ArchiveCopySourceKinds.CheckpointManifest or ArchiveCopySourceKinds.ReleasedArtifact)))
      return false;
    foreach (var entry in expectedEntries)
    {
      var file = manifestFiles.SingleOrDefault(x => x.ManifestEntryId == entry.Id);
      if (file is null || !string.Equals(file.SourceSha256, entry.ContentHash, StringComparison.OrdinalIgnoreCase) ||
          file.SourceByteCount != entry.ByteCount) return false;
    }

    var persistedFiles = await db.ArchiveCopyFileReceipts.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
      x.OperationId == result.OperationId && x.AttemptToken == result.AttemptToken).ToListAsync(ct);
    if (persistedFiles.Count != result.Files.Count) return false;
    foreach (var file in result.Files)
    {
      var row = persistedFiles.SingleOrDefault(x =>
        string.Equals(x.SourceKeySha256, file.SourceKeySha256, StringComparison.OrdinalIgnoreCase));
      if (row is null || row.ArchiveId != archive.Id || row.ArchiveManifestId != manifest.Id ||
          row.ArchiveManifestVersion != manifest.Version ||
          !string.Equals(row.ArchiveManifestSha256, manifest.ManifestDigest, StringComparison.OrdinalIgnoreCase) ||
          row.ReleaseId != result.ReleaseId || row.CheckpointId != result.CheckpointId ||
          row.TargetRevisionId != target.Id || row.TargetRevision != target.Revision ||
          !string.Equals(row.TargetConfigurationSha256, target.ConfigurationSha256, StringComparison.OrdinalIgnoreCase) ||
          row.SourceKind != file.SourceKind || row.SourceId != file.SourceId || row.ManifestEntryId != file.ManifestEntryId ||
          row.SourceVersion != file.SourceVersion || row.SourceByteCount != file.SourceByteCount ||
          row.RelativePath != file.RelativePath || row.DestinationTenantId != file.DestinationTenantId ||
          row.DestinationSiteId != file.DestinationSiteId || row.DestinationDriveId != file.DestinationDriveId ||
          row.DestinationLibraryRootItemId != file.DestinationLibraryRootItemId ||
          row.DestinationItemId != file.DestinationItemId || row.DestinationVersion != file.DestinationVersion ||
          !string.Equals(row.SourceSha256, file.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
          row.HasExactTargetBinding != file.HasExactTargetBinding ||
          !string.Equals(row.ReadBackSha256, file.ReadBackSha256, StringComparison.OrdinalIgnoreCase) ||
          row.ReadBackByteCount != file.ReadBackByteCount || row.VerificationOutcome != file.VerificationOutcome ||
          row.ReadBackAt != file.ReadBackAt || !string.Equals(row.DestinationTenantId, target.TenantId, StringComparison.OrdinalIgnoreCase) ||
          file.DestinationSiteId != target.SiteId || file.DestinationDriveId != target.DriveId ||
          file.DestinationLibraryRootItemId != target.LibraryRootItemId ||
          !string.Equals(file.DestinationTenantId, target.TenantId, StringComparison.OrdinalIgnoreCase))
        return false;
    }

    var permissions = result.Permissions;
    var persistedPermissions = await db.ArchiveCopyPermissionObservations.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
      x.OperationId == result.OperationId && x.AttemptToken == result.AttemptToken).ToListAsync(ct);
    if (persistedPermissions.Count != 1) return false;
    var permission = persistedPermissions[0];
    return permissions.HasUniquePermissionScope && permissions.HasExactTargetBinding &&
      permissions.ComparisonOutcome == ArchiveCopyVerificationOutcomes.Match &&
      permissions.ObservedAt != default &&
      string.Equals(permissions.ExpectedPermissionsJson, target.ExpectedPermissionsJson, StringComparison.Ordinal) &&
      string.Equals(permissions.ExpectedPermissionsSha256, target.ExpectedPermissionsSha256, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(permissions.ObservedPermissionsJson, permissions.ExpectedPermissionsJson, StringComparison.Ordinal) &&
      string.Equals(permissions.ObservedPermissionsSha256, permissions.ExpectedPermissionsSha256, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(permissions.ObservedStaffSha256, target.AuthorizedStaffSha256, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(permissions.ObservedTenantId, target.TenantId, StringComparison.OrdinalIgnoreCase) &&
      permissions.ObservedSiteId == target.SiteId && permissions.ObservedDriveId == target.DriveId &&
      permissions.ObservedLibraryRootItemId == target.LibraryRootItemId &&
      permission.ArchiveId == archive.Id && permission.ArchiveManifestId == manifest.Id &&
      permission.ArchiveManifestVersion == manifest.Version &&
      string.Equals(permission.ArchiveManifestSha256, manifest.ManifestDigest, StringComparison.OrdinalIgnoreCase) &&
      permission.TargetRevisionId == target.Id && permission.TargetRevision == target.Revision &&
      string.Equals(permission.TargetConfigurationSha256, target.ConfigurationSha256, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(permission.AuthorizedStaffSha256, target.AuthorizedStaffSha256, StringComparison.OrdinalIgnoreCase) &&
      permission.StaffGroupId == target.StaffGroupId && permission.HasUniquePermissionScope &&
      permission.HasExactTargetBinding && permission.ComparisonOutcome == ArchiveCopyVerificationOutcomes.Match &&
      string.Equals(permission.ExpectedPermissionsJson, permissions.ExpectedPermissionsJson, StringComparison.Ordinal) &&
      string.Equals(permission.ExpectedPermissionsSha256, permissions.ExpectedPermissionsSha256, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(permission.ObservedPermissionsJson, permissions.ObservedPermissionsJson, StringComparison.Ordinal) &&
      string.Equals(permission.ObservedPermissionsSha256, permissions.ObservedPermissionsSha256, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(permission.ObservedStaffSha256, permissions.ObservedStaffSha256, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(permission.ObservedTenantId, permissions.ObservedTenantId, StringComparison.OrdinalIgnoreCase) &&
      permission.ObservedSiteId == permissions.ObservedSiteId && permission.ObservedDriveId == permissions.ObservedDriveId &&
      permission.ObservedLibraryRootItemId == permissions.ObservedLibraryRootItemId &&
      permission.ObservedAt == permissions.ObservedAt;
  }
}
