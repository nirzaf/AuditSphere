using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Records;

/// <summary>Durable SharePoint secondary-copy protocol, hosted only by the existing client-sites worker group.</summary>
public sealed class ArchiveCopyHandler(
  IAuditSphereDbContextFactory factory,
  IReleaseCheckpointStore checkpointStore,
  IClientSharePointArchiveCopyProvider provider) : IOperationHandler
{
  public const string Kind = "CopyReviewedArchiveToSecondarySharePoint.v1";
  public const string Group = "client-sites";
  private const int MaximumResultIdentityLength = 1_048_576;
  private static readonly string[] CustodianRoles = ["Administrator", "Partner", "RecordsCustodian"];

  public OperationDefinition Definition { get; } = new(Kind, OperationMode.LIVE, OperationAuthority.LIVE_PROVIDER, Group: Group);

  public string NormalizePayload(OperationRequest request) => ArchiveCopyPayloadRules.Normalize(request);

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation operation, CancellationToken ct)
  {
    var recordsDb = RequireRecordsCapability(db);
    var payload = ParsePayload(operation);
    if (operation.OperationKind != Kind || operation.ExecutionGroup != Group || operation.TargetId != payload.ArchiveId ||
        operation.FirmId != payload.FirmId || operation.ClientId != payload.ClientId ||
        operation.EngagementId != payload.EngagementId || operation.ExpectedRevision != payload.ManifestVersion ||
        operation.OriginatorId != payload.OriginatorId)
      throw new OperationBlockedException("archive-copy-operation-binding-invalid", authorization: true);

    await AuthorizeOriginatorAsync(recordsDb, payload, ct);
    await ArchiveCopySourceGuard.LockRowsAsync(recordsDb, operation.FirmId, payload, ct);
    _ = await ArchiveCopySourceGuard.ResolveAsync(recordsDb, payload, includeArtifactBytes: false, ct);
  }

  public Task<OperationResult> ExecuteEffectAsync(DurableOperation operation, CancellationToken ct) => RunAsync(operation, ct);

  // Reconciliation uses the same deterministic provider key. The provider contract is atomic
  // create-if-absent and exact-readback, so an unknown prior outcome can never overwrite bytes.
  public Task<OperationResult> ReconcileAsync(DurableOperation operation, CancellationToken ct) => RunAsync(operation, ct);

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation operation,
    OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    if (verifiedRemoteResult is null ||
        verifiedRemoteResult.Identity is null || verifiedRemoteResult.Identity.Length > MaximumResultIdentityLength ||
        !string.Equals(verifiedRemoteResult.Digest, Hashing.Sha256Hex(Encoding.UTF8.GetBytes(verifiedRemoteResult.Identity)), StringComparison.Ordinal))
      throw new OperationBlockedException("archive-copy-result-invalid");

    var payload = ParsePayload(operation);
    ValidateResultStructure(verifiedRemoteResult.Identity, payload);

    ArchiveCopyOperationResult result;
    try
    {
      result = JsonSerializer.Deserialize<ArchiveCopyOperationResult>(verifiedRemoteResult.Identity)
        ?? throw new JsonException();
    }
    catch (JsonException)
    {
      throw new OperationBlockedException("archive-copy-result-invalid");
    }

    ValidateResultIdentity(operation, payload, result);
    var recordsDb = RequireRecordsCapability(db);
    await AuthorizeOriginatorAsync(recordsDb, payload, ct);
    await ArchiveCopySourceGuard.LockRowsAsync(recordsDb, operation.FirmId, payload, ct);
    var sources = await ArchiveCopySourceGuard.ResolveAsync(recordsDb, payload, includeArtifactBytes: false, ct);
    var expected = ExpectedFiles(sources, payload);
    ValidateRemoteResult(result, expected, sources.Target);

    foreach (var file in result.Files)
    {
      var existing = await recordsDb.ArchiveCopyFileReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == operation.FirmId && x.OperationId == operation.Id && x.AttemptToken == operation.AttemptToken &&
        x.SourceKeySha256 == file.SourceKeySha256, ct);
      if (existing is not null)
      {
        if (existing.ClientId != payload.ClientId || existing.TargetRevisionId != payload.TargetRevisionId ||
            existing.TargetConfigurationSha256 != payload.TargetConfigurationSha256 ||
            existing.SourceKind != file.SourceKind || existing.SourceVersion != file.SourceVersion ||
            existing.SourceSha256 != file.SourceSha256 || existing.SourceByteCount != file.SourceByteCount ||
            existing.RelativePath != file.RelativePath || existing.DestinationTenantId != file.DestinationTenantId ||
            existing.DestinationSiteId != file.DestinationSiteId || existing.DestinationDriveId != file.DestinationDriveId ||
            existing.DestinationLibraryRootItemId != file.DestinationLibraryRootItemId ||
            existing.HasExactTargetBinding != file.HasExactTargetBinding ||
            existing.ReadBackSha256 != file.ReadBackSha256 || existing.ReadBackByteCount != file.ReadBackByteCount ||
            existing.DestinationItemId != file.DestinationItemId || existing.DestinationVersion != file.DestinationVersion)
          throw new OperationBlockedException("archive-copy-receipt-idempotency-conflict");
        continue;
      }
      recordsDb.ArchiveCopyFileReceipts.Add(ArchiveCopyFileReceipt.Create(
        Guid.CreateVersion7(), payload.FirmId, payload.ClientId, payload.EngagementId,
        operation.Id, operation.AttemptToken, payload.ArchiveId, payload.ManifestId,
        payload.ManifestVersion, payload.ManifestSha256, payload.ReleaseId, payload.CheckpointId,
        payload.TargetRevisionId, payload.TargetRevision, payload.TargetConfigurationSha256,
        file.SourceKind, file.SourceKeySha256, file.SourceId, file.ManifestEntryId,
        file.SourceVersion, file.SourceSha256, file.SourceByteCount, file.RelativePath,
        file.DestinationTenantId, file.DestinationSiteId, file.DestinationDriveId, file.DestinationLibraryRootItemId,
        file.HasExactTargetBinding, file.DestinationItemId, file.DestinationVersion, file.ReadBackSha256,
        file.ReadBackByteCount, file.ReadBackAt));
    }

    var existingPermission = await recordsDb.ArchiveCopyPermissionObservations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == operation.FirmId && x.OperationId == operation.Id && x.AttemptToken == operation.AttemptToken, ct);
    if (existingPermission is null)
    {
      recordsDb.ArchiveCopyPermissionObservations.Add(ArchiveCopyPermissionObservation.Create(
        Guid.CreateVersion7(), payload.FirmId, payload.ClientId, payload.EngagementId,
        operation.Id, operation.AttemptToken, payload.ArchiveId, payload.ManifestId,
        payload.ManifestVersion, payload.ManifestSha256, payload.ReleaseId, payload.CheckpointId,
        payload.TargetRevisionId, payload.TargetRevision, payload.TargetConfigurationSha256,
        payload.AuthorizedStaffSha256, sources.Target.StaffGroupId,
        result.Permissions.ExpectedPermissionsJson, result.Permissions.ExpectedPermissionsSha256,
        result.Permissions.ObservedPermissionsJson, result.Permissions.ObservedPermissionsSha256,
        result.Permissions.HasUniquePermissionScope,
        result.Permissions.HasExactTargetBinding, result.Permissions.ObservedStaffSha256,
        result.Permissions.ObservedTenantId,
        result.Permissions.ObservedSiteId, result.Permissions.ObservedDriveId,
        result.Permissions.ObservedLibraryRootItemId,
        result.Permissions.ObservedAt));
    }
    else if (existingPermission.AuthorizedStaffSha256 != payload.AuthorizedStaffSha256 ||
        existingPermission.StaffGroupId != sources.Target.StaffGroupId ||
        existingPermission.ExpectedPermissionsJson != result.Permissions.ExpectedPermissionsJson ||
        existingPermission.ExpectedPermissionsSha256 != result.Permissions.ExpectedPermissionsSha256 ||
        existingPermission.ObservedPermissionsSha256 != result.Permissions.ObservedPermissionsSha256 ||
        existingPermission.ObservedPermissionsJson != result.Permissions.ObservedPermissionsJson ||
        existingPermission.HasUniquePermissionScope != result.Permissions.HasUniquePermissionScope ||
        existingPermission.HasExactTargetBinding != result.Permissions.HasExactTargetBinding ||
        existingPermission.ObservedStaffSha256 != result.Permissions.ObservedStaffSha256 ||
        existingPermission.ObservedTenantId != result.Permissions.ObservedTenantId ||
        existingPermission.ObservedSiteId != result.Permissions.ObservedSiteId ||
        existingPermission.ObservedDriveId != result.Permissions.ObservedDriveId ||
        existingPermission.ObservedLibraryRootItemId != result.Permissions.ObservedLibraryRootItemId ||
        existingPermission.ComparisonOutcome != result.Permissions.ComparisonOutcome)
      throw new OperationBlockedException("archive-copy-permission-idempotency-conflict");

    // Operation completion records protocol readback only. It does not set Purview state,
    // RecordsAction.Observed fields, Archive status, WORM, or local custody claims.
    return verifiedRemoteResult;
  }

  private async Task<OperationResult> RunAsync(DurableOperation operation, CancellationToken ct)
  {
    var payload = ParsePayload(operation);
    ArchiveCopySourceSet sources;
    await using (var db = await factory.CreateAsync(ct))
    {
      var recordsDb = RequireRecordsCapability(db);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      if (!await ClientSharePointSites.LockIntentScopeAsync(db, operation.FirmId, payload.ClientId, ct))
        throw new OperationBlockedException("archive-copy-scope-unavailable", authorization: true);
      await LockTargetAsync(db, operation, ct);
      await EnsureAttemptOwnsLeaseAsync(db, operation, ct);
      sources = await ArchiveCopySourceGuard.ResolveAsync(recordsDb, payload, includeArtifactBytes: true, ct);
      await tx.CommitAsync(ct);
    }

    var checkpointContent = await checkpointStore.ReadVerifiedAsync(sources.Checkpoint.StoredReference,
      payload.CheckpointManifestSha256, ct);
    if (checkpointContent is null || checkpointContent.Receipt.ContentSha256Hex != payload.CheckpointManifestSha256 ||
        Hashing.Sha256Hex(checkpointContent.Bytes) != payload.CheckpointManifestSha256)
      throw new OperationBlockedException("archive-copy-checkpoint-bytes-unavailable");

    if (sources.ReleasedArtifactBytes is not { } releasedBytes ||
        releasedBytes.LongLength != payload.ReleasedArtifactByteCount ||
        Hashing.Sha256Hex(releasedBytes) != payload.ReleasedArtifactSha256)
      throw new OperationBlockedException("archive-copy-released-artifact-bytes-unavailable");

    var target = ToProviderTarget(sources.Target, sources.ClientSite);
    var plan = BuildFilePlan(sources, payload, checkpointContent.Bytes, releasedBytes);
    var files = new List<ArchiveCopyFileResult>(plan.Count);
    async Task Recheck(CancellationToken token) => await RecheckAuthorityAsync(operation, payload, token);

    var permissionPreflight = await ReadPermissionResultAsync(target, sources.Target, Recheck, ct);
    if (permissionPreflight.ComparisonOutcome != ArchiveCopyVerificationOutcomes.Match)
      throw new OperationBlockedException("archive-copy-target-permission-preflight-failed", authorization: true);

    foreach (var source in plan)
    {
      await Recheck(ct);
      var sourceBytes = source.Content;
      if (source.SnapshotRequest is { } snapshotRequest)
      {
        var snapshot = await provider.ReadExactSnapshotAsync(snapshotRequest with { RecheckAuthorization = Recheck }, ct);
        if (snapshot is null || snapshot.Content is null || snapshot.VersionId != snapshotRequest.VersionId)
          throw new OperationBlockedException("archive-copy-snapshot-readback-unavailable");
        sourceBytes = snapshot.Content;
      }

      var sourceDigest = Hashing.Sha256Hex(sourceBytes);
      if (sourceBytes.LongLength != source.ExpectedByteCount || sourceDigest != source.ExpectedSha256)
        throw new OperationBlockedException("archive-copy-source-bytes-mismatch");

      await Recheck(ct);
      var write = await provider.CreateOrReadExactFileAsync(new ArchiveCopyFileWriteRequest(
        target, operation.Id, source.SourceKeySha256, source.RelativePath,
        source.ExpectedSha256, sourceBytes, Recheck), ct);
      if (write is null || write.Content is null || write.ReadBackAt == default ||
          string.IsNullOrWhiteSpace(write.TenantId) || string.IsNullOrWhiteSpace(write.SiteId) ||
          string.IsNullOrWhiteSpace(write.DriveId) || string.IsNullOrWhiteSpace(write.LibraryRootItemId) ||
          string.IsNullOrWhiteSpace(write.RelativePath) ||
          string.IsNullOrWhiteSpace(write.DestinationItemId) || string.IsNullOrWhiteSpace(write.DestinationVersion))
        throw new OperationBlockedException("archive-copy-destination-readback-unavailable");
      var readBackDigest = Hashing.Sha256Hex(write.Content);
      var exactTargetBinding = string.Equals(write.TenantId, target.TenantId, StringComparison.OrdinalIgnoreCase) &&
        write.SiteId == target.SiteId && write.DriveId == target.DriveId &&
        write.LibraryRootItemId == target.LibraryRootItemId && write.RelativePath == source.RelativePath;
      files.Add(new ArchiveCopyFileResult(source.SourceKind, source.SourceKeySha256, source.SourceId,
        source.ManifestEntryId, source.SourceVersion, source.ExpectedSha256, source.ExpectedByteCount,
        write.RelativePath, write.TenantId, write.SiteId, write.DriveId, write.LibraryRootItemId,
        exactTargetBinding, write.DestinationItemId, write.DestinationVersion, readBackDigest,
        write.Content.LongLength, FileOutcome(source.ExpectedSha256, source.ExpectedByteCount,
          readBackDigest, write.Content.LongLength, exactTargetBinding), write.ReadBackAt.ToUniversalTime()));
      await Recheck(ct);
    }

    await Recheck(ct);
    var permissionResult = await ReadPermissionResultAsync(target, sources.Target, Recheck, ct);
    var allMatch = files.All(x => x.VerificationOutcome == ArchiveCopyVerificationOutcomes.Match) &&
      permissionResult.ComparisonOutcome == ArchiveCopyVerificationOutcomes.Match;
    await Recheck(ct);

    var result = new ArchiveCopyOperationResult(operation.Id, operation.AttemptToken, payload.ArchiveId, payload.ClientId,
      payload.ManifestId, payload.ManifestVersion, payload.ManifestSha256, payload.RecordsProfileId,
      payload.RecordsProfileVersion, payload.ReleaseId, payload.CheckpointId, payload.ClientSharePointSiteId,
      payload.TargetRevisionId, payload.TargetRevision, payload.TargetConfigurationSha256, payload.AuthorizedStaffSha256,
      allMatch ? ArchiveCopyVerificationOutcomes.Match : ArchiveCopyVerificationOutcomes.Mismatch,
      files, permissionResult);
    var identity = JsonSerializer.Serialize(result);
    return new OperationResult(identity, Hashing.Sha256Hex(Encoding.UTF8.GetBytes(identity)));
  }

  private async Task<ArchiveCopyPermissionResult> ReadPermissionResultAsync(
    ArchiveCopyProviderTarget target, ArchiveCopyTargetRevision persistedTarget,
    Func<CancellationToken, Task> recheck, CancellationToken ct)
  {
    await recheck(ct);
    var readback = await provider.ReadTargetPermissionsAsync(new ArchiveCopyPermissionReadRequest(target, recheck), ct);
    if (readback is null || readback.Grants is null || !IsSupportedEvidenceTime(readback.ReadBackAt))
      throw new OperationBlockedException("archive-copy-permission-readback-unavailable");
    var observed = ArchiveCopyPermissionEncoding.CanonicalReadback(readback.Grants);
    var expected = ArchiveCopyPermissionEncoding.Encode(
      ArchiveCopyPermissionEncoding.Decode(persistedTarget.ExpectedPermissionsJson));
    var exactTarget = string.Equals(readback.TenantId, target.TenantId, StringComparison.OrdinalIgnoreCase) &&
      readback.SiteId == target.SiteId && readback.DriveId == target.DriveId &&
      readback.LibraryRootItemId == target.LibraryRootItemId;
    var exactStaffRead = ArchiveCopyPermissionEncoding.IsExactClientStaffReadGrant(
      observed.Grants, persistedTarget.StaffGroupId);
    var outcome = readback.HasUniquePermissionScope && exactTarget && exactStaffRead &&
      readback.ObservedStaffSha256 == target.AuthorizedStaffSha256 && expected.Sha256 == observed.Sha256
      ? ArchiveCopyVerificationOutcomes.Match : ArchiveCopyVerificationOutcomes.Mismatch;
    if (!ArchiveCopyPayloadRules.IsSha256(readback.ObservedStaffSha256))
      throw new OperationBlockedException("archive-copy-permission-readback-invalid");
    return new ArchiveCopyPermissionResult(expected.Json, expected.Sha256, observed.Json, observed.Sha256,
      readback.HasUniquePermissionScope, exactTarget, readback.ObservedStaffSha256, readback.TenantId,
      readback.SiteId, readback.DriveId, readback.LibraryRootItemId, outcome, readback.ReadBackAt.ToUniversalTime());
  }

  private async Task RecheckAuthorityAsync(DurableOperation operation, ArchiveCopyOperationPayload payload, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    var recordsDb = RequireRecordsCapability(db);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await ClientSharePointSites.LockIntentScopeAsync(db, operation.FirmId, payload.ClientId, ct))
      throw new OperationBlockedException("archive-copy-scope-unavailable", authorization: true);
    await AuthorizeOriginatorAsync(recordsDb, payload, ct);
    await ArchiveCopySourceGuard.LockRowsAsync(recordsDb, operation.FirmId, payload, ct);
    _ = await ArchiveCopySourceGuard.ResolveAsync(recordsDb, payload, includeArtifactBytes: false, ct);
    await EnsureAttemptOwnsLeaseAsync(db, operation, ct);
    await tx.CommitAsync(ct);
  }

  private static async Task EnsureAttemptOwnsLeaseAsync(IAuditSphereDbContext db, DurableOperation operation, CancellationToken ct)
  {
    var expectedStatus = operation.IsReconciliation ? OperationState.VERIFYING : OperationState.REMOTE_STARTED;
    if (operation.Status != expectedStatus || operation.LeaseOwner is null || operation.LeaseExpiresAt is null)
      throw new OperationOwnershipLostException();
    var owned = await db.DurableOperations.AsNoTracking().AnyAsync(x => x.Id == operation.Id &&
      x.FirmId == operation.FirmId && x.OperationKind == Kind && x.RequestDigest == operation.RequestDigest &&
      x.AttemptToken == operation.AttemptToken && x.LeaseOwner == operation.LeaseOwner &&
      x.Status == expectedStatus && x.IsReconciliation == operation.IsReconciliation &&
      x.LeaseExpiresAt > DateTimeOffset.UtcNow, ct);
    if (!owned) throw new OperationOwnershipLostException();
  }

  private static async Task AuthorizeOriginatorAsync(IRecordsArchiveCopyDbContext db,
    ArchiveCopyOperationPayload payload, CancellationToken ct)
  {
    var actor = new ActorContext(payload.OriginatorId, payload.FirmId, payload.OriginatorSessionEpoch, []);
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(payload.FirmId, payload.ClientId, payload.EngagementId,
        RequiredRoles: CustodianRoles, InternalOnly: true), ct);
    if (!authorization.Succeeded)
      throw new OperationBlockedException("archive-copy-originator-authority-revoked", authorization: true);
  }

  private static ArchiveCopyOperationPayload ParsePayload(DurableOperation operation)
  {
    try
    {
      var payload = JsonSerializer.Deserialize<ArchiveCopyOperationPayload>(operation.PayloadJson)
        ?? throw new JsonException();
      var request = new OperationRequest(operation.FirmId, operation.ClientId, operation.EngagementId,
        operation.OperationKind, operation.TargetId, operation.ExpectedRevision, operation.IdempotencyKey,
        operation.PayloadJson, operation.OriginatorId);
      _ = ArchiveCopyPayloadRules.Normalize(request);
      return payload;
    }
    catch (Exception exception) when (exception is JsonException or NotSupportedException)
    {
      throw new OperationBlockedException("archive-copy-request-invalid");
    }
  }

  private static IRecordsArchiveCopyDbContext RequireRecordsCapability(IAuditSphereDbContext db) =>
    db as IRecordsArchiveCopyDbContext ?? throw new OperationBlockedException("records-archive-copy-capability-unavailable");

  private static void ValidateResultIdentity(DurableOperation operation, ArchiveCopyOperationPayload payload,
    ArchiveCopyOperationResult result)
  {
    if (result.OperationId != operation.Id || result.AttemptToken != operation.AttemptToken ||
        result.ArchiveId != payload.ArchiveId || result.ClientId != payload.ClientId || result.ManifestId != payload.ManifestId ||
        result.ManifestVersion != payload.ManifestVersion || result.ManifestSha256 != payload.ManifestSha256 ||
        result.RecordsProfileId != payload.RecordsProfileId || result.RecordsProfileVersion != payload.RecordsProfileVersion ||
        result.ReleaseId != payload.ReleaseId || result.CheckpointId != payload.CheckpointId ||
        result.ClientSharePointSiteId != payload.ClientSharePointSiteId ||
        result.TargetRevisionId != payload.TargetRevisionId || result.TargetRevision != payload.TargetRevision ||
        result.TargetConfigurationSha256 != payload.TargetConfigurationSha256 ||
        result.AuthorizedStaffSha256 != payload.AuthorizedStaffSha256)
      throw new OperationBlockedException("archive-copy-result-scope-mismatch", authorization: true);
  }

  private static IReadOnlyList<PlannedFile> ExpectedFiles(ArchiveCopySourceSet sources, ArchiveCopyOperationPayload payload)
  {
    var basePath = $"archives/{payload.ArchiveId:N}/profiles/{payload.RecordsProfileId:N}-v{payload.RecordsProfileVersion}-{payload.ProfileSha256}/manifests/{payload.ManifestVersion:D}-{payload.ManifestSha256}/releases/{payload.ReleaseId:N}/targets/{payload.TargetRevision:D}-{payload.TargetRevisionId:N}";
    var files = new List<PlannedFile>
    {
      PlannedFile.ForBytes(ArchiveCopySourceKinds.CheckpointManifest, payload.CheckpointId, null,
        payload.CheckpointReadBackSha256, payload.CheckpointReadBackSha256, -1,
        $"{basePath}/release-evidence/checkpoint-{payload.CheckpointId:N}.json", []),
      PlannedFile.ForBytes(ArchiveCopySourceKinds.ReleasedArtifact, payload.ReleasedArtifactId, null,
        payload.ReleasedArtifactVersion, payload.ReleasedArtifactSha256, payload.ReleasedArtifactByteCount,
        $"{basePath}/release-evidence/pinned-artifact-{payload.ReleasedArtifactId:N}-{Uri.EscapeDataString(payload.ReleasedArtifactVersion)}", [])
    };

    foreach (var entry in sources.Entries.OrderBy(x => x.Ordinal))
    {
      var key = Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
      {
        SourceKind = ArchiveCopySourceKinds.ReviewedManifestEntry, payload.ArchiveId,
        payload.ManifestId, payload.ManifestVersion, payload.ManifestSha256,
        entry.Id, entry.Ordinal, entry.EntryKind, ManifestEntrySourceKind = entry.SourceKind, entry.SourceId,
        entry.RelativeName, entry.ContentHash, entry.ByteCount, entry.Required, entry.MetadataJson,
        payload.RecordsProfileId, payload.RecordsProfileVersion, payload.ProfileSha256,
        payload.ReleaseId, payload.CheckpointId, payload.TargetRevisionId, payload.TargetConfigurationSha256
      }));
      var relative = $"{basePath}/manifest-entries/{entry.Ordinal:D6}/{EncodeManifestPath(entry.RelativeName)}";
      string version;
      byte[] bytes;
      if (entry.EntryKind == "STRUCTURED_EXPORT")
      {
        version = sources.StructuredExport.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        bytes = Encoding.UTF8.GetBytes(sources.StructuredExport.PayloadJson);
      }
      else if (entry.EntryKind == "DOCUMENT_SNAPSHOT")
      {
        var snapshot = sources.DocumentSnapshots[entry.Id].Snapshot;
        version = snapshot.VersionId;
        bytes = [];
      }
      else if (sources.PersistedByteSources.TryGetValue(entry.Id, out var persisted))
      {
        version = persisted.Version;
        bytes = persisted.Content;
      }
      else
        throw new OperationBlockedException("archive-copy-manifest-entry-unretrievable");
      files.Add(new PlannedFile(ArchiveCopySourceKinds.ReviewedManifestEntry, key, entry.SourceId,
        entry.Id, version, entry.ContentHash, entry.ByteCount, relative, bytes,
        entry.EntryKind == "DOCUMENT_SNAPSHOT" ? ToSnapshotReadRequest(sources.DocumentSnapshots[entry.Id], sources.Target) : null));
    }
    return files;
  }

  internal static IReadOnlyList<ArchiveCopyVerificationPlanFile> ExpectedFilesForVerification(
    ArchiveCopySourceSet sources, ArchiveCopyOperationPayload payload) =>
    ExpectedFiles(sources, payload).Select(file => new ArchiveCopyVerificationPlanFile(
      file.SourceKind, file.SourceKeySha256, file.SourceId, file.ManifestEntryId, file.SourceVersion,
      file.ExpectedSha256, file.ExpectedByteCount, file.RelativePath)).ToArray();

  private static List<PlannedFile> BuildFilePlan(ArchiveCopySourceSet sources, ArchiveCopyOperationPayload payload,
    byte[] checkpointBytes, byte[] releasedArtifactBytes)
  {
    var files = ExpectedFiles(sources, payload).ToList();
    files[0] = files[0] with { Content = checkpointBytes, ExpectedByteCount = checkpointBytes.LongLength };
    files[1] = files[1] with { Content = releasedArtifactBytes };
    return files;
  }

  private static ArchiveCopySnapshotReadRequest ToSnapshotReadRequest(
    ArchiveCopyDocumentSnapshotSource source, ArchiveCopyTargetRevision target)
  {
    if (!string.Equals(source.Binding.TenantId, target.TenantId, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("archive-copy-source-tenant-mismatch", authorization: true);
    return new ArchiveCopySnapshotReadRequest(source.Snapshot.FirmId, source.Snapshot.ClientId,
      source.Snapshot.EngagementId, source.Snapshot.Id, source.Binding.TenantId, source.Binding.SiteId,
      source.Snapshot.DriveId, source.Snapshot.ItemId, source.Snapshot.VersionId,
      source.Snapshot.Sha256Hex, source.Snapshot.ByteCount);
  }

  private static ArchiveCopyProviderTarget ToProviderTarget(ArchiveCopyTargetRevision target, ClientSharePointSite? clientSite)
  {
    if (target.ClientSharePointSiteId is not null && clientSite is null)
      throw new OperationBlockedException("archive-copy-client-site-or-library-stale", authorization: true);
    if (target.UsesSharedSelectedResource &&
        (target.SelectedResourceSiteUrl is null || target.SelectedResourceRootFolderId is null))
      throw new OperationBlockedException("archive-copy-selected-resource-invalid", authorization: true);
    var marker = clientSite?.OwnershipMarker ?? $"AuditSphere:{target.FirmId:D}:{target.ClientId:D}";
    return new(target.FirmId, target.ClientId, target.ClientSharePointSiteId, target.ConnectionRevisionId,
      target.Id, target.Revision, target.ConfigurationSha256, marker, target.TenantId, target.SiteId,
      target.DriveId, target.LibraryRootItemId, target.StaffGroupId, target.AuthorizedStaffSha256,
      target.ExpectedPermissionsSha256, clientSite?.RequestedUrl ?? target.SelectedResourceSiteUrl ?? "",
      target.SelectedResourceRootFolderId);
  }

  private static string EncodeManifestPath(string path)
  {
    if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\') ||
        path.Any(char.IsControl))
      throw new OperationBlockedException("archive-copy-manifest-path-invalid");
    var segments = path.Split('/');
    if (segments.Length is < 1 or > 32 || segments.Any(x => x.Length is 0 or > 255 || x is "." or ".." ||
          x.Any(c => c is ':' or '*' or '?' or '<' or '>' or '|' or '"')))
      throw new OperationBlockedException("archive-copy-manifest-path-invalid");
    return string.Join('/', segments.Select(Uri.EscapeDataString));
  }

  private static string FileOutcome(string sourceHash, long sourceLength, string readbackHash, long readbackLength,
    bool exactTargetBinding = true) =>
    exactTargetBinding && sourceHash == readbackHash && sourceLength == readbackLength
      ? ArchiveCopyVerificationOutcomes.Match : ArchiveCopyVerificationOutcomes.Mismatch;

  private static void ValidateRemoteResult(ArchiveCopyOperationResult result,
    IReadOnlyList<PlannedFile> expected, ArchiveCopyTargetRevision target)
  {
    var expectedByKey = expected.ToDictionary(x => x.SourceKeySha256, StringComparer.Ordinal);
    if (result.Files.Count != expectedByKey.Count || result.Files.Select(x => x.SourceKeySha256).Distinct(StringComparer.Ordinal).Count() != expectedByKey.Count)
      throw new OperationBlockedException("archive-copy-result-file-set-mismatch");
    foreach (var file in result.Files)
    {
      if (!expectedByKey.TryGetValue(file.SourceKeySha256, out var source) ||
          file.SourceKind != source.SourceKind || file.SourceId != source.SourceId ||
          file.ManifestEntryId != source.ManifestEntryId || file.SourceVersion != source.SourceVersion ||
          file.SourceSha256 != source.ExpectedSha256 ||
          file.SourceByteCount < 0 ||
          (source.SourceKind != ArchiveCopySourceKinds.CheckpointManifest && file.SourceByteCount != source.ExpectedByteCount) ||
          file.RelativePath != source.RelativePath ||
          !string.Equals(file.DestinationTenantId, target.TenantId, StringComparison.OrdinalIgnoreCase) ||
          file.DestinationSiteId != target.SiteId || file.DestinationDriveId != target.DriveId ||
          file.DestinationLibraryRootItemId != target.LibraryRootItemId ||
          !file.HasExactTargetBinding ||
          string.IsNullOrWhiteSpace(file.DestinationItemId) ||
          string.IsNullOrWhiteSpace(file.DestinationVersion) || !ArchiveCopyPayloadRules.IsSha256(file.ReadBackSha256) ||
          file.ReadBackByteCount < 0 || !IsSupportedEvidenceTime(file.ReadBackAt) ||
          file.VerificationOutcome != FileOutcome(file.SourceSha256, file.SourceByteCount, file.ReadBackSha256, file.ReadBackByteCount))
        throw new OperationBlockedException("archive-copy-result-file-binding-mismatch");
    }

    var expectedPermissions = ArchiveCopyPermissionEncoding.Encode(
      ArchiveCopyPermissionEncoding.Decode(target.ExpectedPermissionsJson));
    var observedPermissions = ArchiveCopyPermissionEncoding.CanonicalReadback(
      ArchiveCopyPermissionEncoding.Decode(result.Permissions.ObservedPermissionsJson));
    var exactPermissionTarget = string.Equals(result.Permissions.ObservedTenantId, target.TenantId, StringComparison.OrdinalIgnoreCase) &&
      result.Permissions.ObservedSiteId == target.SiteId && result.Permissions.ObservedDriveId == target.DriveId &&
      result.Permissions.ObservedLibraryRootItemId == target.LibraryRootItemId;
    var exactStaffReadGrant = ArchiveCopyPermissionEncoding.IsExactClientStaffReadGrant(
      observedPermissions.Grants, target.StaffGroupId);
    var permissionOutcome = result.Permissions.HasUniquePermissionScope && exactPermissionTarget && exactStaffReadGrant &&
      result.Permissions.ObservedStaffSha256 == target.AuthorizedStaffSha256 &&
      expectedPermissions.Sha256 == observedPermissions.Sha256
        ? ArchiveCopyVerificationOutcomes.Match : ArchiveCopyVerificationOutcomes.Mismatch;
    if (result.Permissions.ExpectedPermissionsJson != expectedPermissions.Json ||
        result.Permissions.ExpectedPermissionsSha256 != expectedPermissions.Sha256 ||
        result.Permissions.ObservedPermissionsJson != observedPermissions.Json ||
        result.Permissions.ObservedPermissionsSha256 != observedPermissions.Sha256 ||
        result.Permissions.HasExactTargetBinding != exactPermissionTarget ||
        !ArchiveCopyPayloadRules.IsSha256(result.Permissions.ObservedStaffSha256) ||
        !IsSupportedEvidenceTime(result.Permissions.ObservedAt) ||
        result.Permissions.ComparisonOutcome != permissionOutcome)
      throw new OperationBlockedException("archive-copy-result-permissions-mismatch");

    var expectedOutcome = result.Files.All(x => x.VerificationOutcome == ArchiveCopyVerificationOutcomes.Match) &&
      result.Permissions.ComparisonOutcome == ArchiveCopyVerificationOutcomes.Match
        ? ArchiveCopyVerificationOutcomes.Match : ArchiveCopyVerificationOutcomes.Mismatch;
    if (result.ProtocolOutcome != expectedOutcome)
      throw new OperationBlockedException("archive-copy-result-outcome-mismatch");
  }

  private static void ValidateResultStructure(string identity, ArchiveCopyOperationPayload payload)
  {
    try
    {
      using var document = JsonDocument.Parse(identity, new JsonDocumentOptions { MaxDepth = 16 });
      var root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object ||
          !root.TryGetProperty(nameof(ArchiveCopyOperationResult.Files), out var files) ||
          files.ValueKind != JsonValueKind.Array ||
          files.GetArrayLength() != checked(payload.ManifestEntryCount + 2) ||
          !root.TryGetProperty(nameof(ArchiveCopyOperationResult.Permissions), out var permissions) ||
          permissions.ValueKind != JsonValueKind.Object)
        throw new OperationBlockedException("archive-copy-result-invalid");

      foreach (var file in files.EnumerateArray())
      {
        if (file.ValueKind != JsonValueKind.Object ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.SourceKind), 40) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.SourceKeySha256), 64) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.SourceVersion), 500) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.SourceSha256), 64) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.RelativePath), 2000) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.DestinationTenantId), 200) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.DestinationSiteId), 2000) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.DestinationDriveId), 2000) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.DestinationLibraryRootItemId), 2000) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.DestinationItemId), 500) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.DestinationVersion), 500) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.ReadBackSha256), 64) ||
            !HasBoundedString(file, nameof(ArchiveCopyFileResult.VerificationOutcome), 20) ||
            !HasSupportedTimestamp(file, nameof(ArchiveCopyFileResult.ReadBackAt)))
          throw new OperationBlockedException("archive-copy-result-invalid");
      }

      if (!HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ExpectedPermissionsJson),
            ArchiveCopyPermissionEncoding.MaximumPermissionGrants * 256) ||
          !HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ExpectedPermissionsSha256), 64) ||
          !HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ObservedPermissionsJson),
            ArchiveCopyPermissionEncoding.MaximumPermissionGrants * 256) ||
          !HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ObservedPermissionsSha256), 64) ||
          !HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ObservedStaffSha256), 64) ||
          !HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ObservedTenantId), 200) ||
          !HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ObservedSiteId), 2000) ||
          !HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ObservedDriveId), 2000) ||
          !HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ObservedLibraryRootItemId), 2000) ||
          !HasBoundedString(permissions, nameof(ArchiveCopyPermissionResult.ComparisonOutcome), 20) ||
          !HasSupportedTimestamp(permissions, nameof(ArchiveCopyPermissionResult.ObservedAt)))
        throw new OperationBlockedException("archive-copy-result-invalid");
    }
    catch (Exception exception) when (exception is JsonException or OverflowException)
    {
      throw new OperationBlockedException("archive-copy-result-invalid");
    }
  }

  private static bool HasBoundedString(JsonElement value, string propertyName, int maximumLength)
  {
    if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
      return false;
    var text = property.GetString();
    return text is { Length: > 0 } && text.Length <= maximumLength;
  }

  private static bool HasSupportedTimestamp(JsonElement value, string propertyName) =>
    value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String &&
    property.TryGetDateTimeOffset(out var timestamp) && IsSupportedEvidenceTime(timestamp);

  private static bool IsSupportedEvidenceTime(DateTimeOffset timestamp) =>
    timestamp != default && timestamp <= DateTimeOffset.UtcNow.AddSeconds(5);

  private sealed record PlannedFile(
    string SourceKind,
    string SourceKeySha256,
    Guid? SourceId,
    Guid? ManifestEntryId,
    string SourceVersion,
    string ExpectedSha256,
    long ExpectedByteCount,
    string RelativePath,
    byte[] Content,
    ArchiveCopySnapshotReadRequest? SnapshotRequest)
  {
    public static PlannedFile ForBytes(string kind, Guid? sourceId, Guid? entryId, string version,
      string sha256, long byteCount, string relativePath, byte[] bytes)
    {
      var key = Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
      {
        kind, sourceId, entryId, version, sha256, byteCount, relativePath
      }));
      return new PlannedFile(kind, key, sourceId, entryId, version, sha256, byteCount, relativePath, bytes, null);
    }
  }
}
