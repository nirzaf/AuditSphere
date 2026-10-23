using System.Text;
using System.Text.Json;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Domain.Records;

public static class ArchiveCopySourceKinds
{
  public const string CheckpointManifest = "CHECKPOINT_MANIFEST";
  public const string ReleasedArtifact = "RELEASED_ARTIFACT";
  public const string ReviewedManifestEntry = "REVIEWED_MANIFEST_ENTRY";
}

public static class ArchiveCopyVerificationOutcomes
{
  public const string Match = "MATCH";
  public const string Mismatch = "MISMATCH";
}

/// <summary>An immutable destination and exact expected permission set for ADR-0009 secondary copies.</summary>
public sealed class ArchiveCopyTargetRevision
{
  private ArchiveCopyTargetRevision() { }

  public Guid Id { get; private set; }
  public Guid FirmId { get; private set; }
  public Guid ClientId { get; private set; }
  public Guid? ClientSharePointSiteId { get; private set; }
  public Guid? SelectedResourceDraftId { get; private set; }
  public long? SelectedResourceDraftRevision { get; private set; }
  public Guid? SelectedResourceVerificationId { get; private set; }
  public string? SelectedResourceSiteUrl { get; private set; }
  public string? SelectedResourceRootFolderId { get; private set; }
  public string? SelectedResourceEvidenceSha256 { get; private set; }
  public string StaffGroupId { get; private set; } = "";
  public string TargetCode { get; private set; } = "";
  public long Revision { get; private set; }
  public Guid ConnectionRevisionId { get; private set; }
  public string TenantId { get; private set; } = "";
  public string SiteId { get; private set; } = "";
  public string DriveId { get; private set; } = "";
  public string LibraryRootItemId { get; private set; } = "";
  public string ExpectedPermissionsJson { get; private set; } = "[]";
  public string ExpectedPermissionsSha256 { get; private set; } = "";
  public string AuthorizedStaffSha256 { get; private set; } = "";
  public string ConfigurationSha256 { get; private set; } = "";
  public Guid CreatedByUserId { get; private set; }
  public DateTimeOffset CreatedAt { get; private set; }

  public bool UsesSharedSelectedResource => ClientSharePointSiteId is null;

  public bool HasValidDigests()
  {
    if (!IsSharePointGroupId(StaffGroupId) || !HasExactStaffReadExpectation(ExpectedPermissionsJson, StaffGroupId) ||
        !string.Equals(Hashing.Sha256Hex(Encoding.UTF8.GetBytes(ExpectedPermissionsJson)), ExpectedPermissionsSha256, StringComparison.Ordinal) ||
        !IsSha256(AuthorizedStaffSha256)) return false;

    var hasSharedBindingFields = SelectedResourceDraftId.HasValue || SelectedResourceDraftRevision.HasValue ||
      SelectedResourceVerificationId.HasValue || SelectedResourceSiteUrl is not null ||
      SelectedResourceRootFolderId is not null || SelectedResourceEvidenceSha256 is not null;
    // The nullable site FK is the immutable variant discriminator. A shared binding may
    // never carry a legacy client-site id, and a legacy binding may never carry any of
    // the selected-resource proof tuple. Keep the original legacy hash shape unchanged.
    var expectedConfiguration = ClientSharePointSiteId is { } clientSiteId && clientSiteId != Guid.Empty && !hasSharedBindingFields
      ? ComputeConfigurationSha256(FirmId, Revision, ConnectionRevisionId, TenantId, SiteId, DriveId,
        LibraryRootItemId, ClientId, clientSiteId, StaffGroupId, ExpectedPermissionsSha256, AuthorizedStaffSha256)
      : ClientSharePointSiteId is null &&
        SelectedResourceDraftId is { } draftId && SelectedResourceDraftRevision is { } draftRevision &&
        SelectedResourceVerificationId is { } verificationId && SelectedResourceSiteUrl is { } siteUrl &&
        SelectedResourceRootFolderId is { } selectedRoot &&
        string.Equals(selectedRoot, LibraryRootItemId, StringComparison.Ordinal) &&
        SelectedResourceEvidenceSha256 is { } evidenceSha256
          ? ComputeSharedConfigurationSha256(FirmId, ClientId, StaffGroupId, Revision, ConnectionRevisionId,
            TenantId, SiteId, DriveId, LibraryRootItemId, draftId, draftRevision, verificationId,
            siteUrl, selectedRoot, evidenceSha256, ExpectedPermissionsSha256, AuthorizedStaffSha256)
          : null;

    return expectedConfiguration is not null && string.Equals(expectedConfiguration, ConfigurationSha256, StringComparison.Ordinal);
  }

  public static ArchiveCopyTargetRevision Create(
    Guid id, Guid firmId, Guid clientId, Guid clientSharePointSiteId, string staffGroupId, long revision, Guid connectionRevisionId,
    string tenantId, string siteId, string driveId, string libraryRootItemId,
    string expectedPermissionsJson, string expectedPermissionsSha256, string authorizedStaffSha256,
    Guid createdByUserId, DateTimeOffset createdAt)
  {
    if (id == Guid.Empty || firmId == Guid.Empty || clientId == Guid.Empty || clientSharePointSiteId == Guid.Empty ||
        !IsSharePointGroupId(staffGroupId) ||
        revision < 1 || connectionRevisionId == Guid.Empty ||
        createdByUserId == Guid.Empty || createdAt == default ||
        string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 200 ||
        string.IsNullOrWhiteSpace(siteId) || siteId.Length > 2000 ||
        string.IsNullOrWhiteSpace(driveId) || driveId.Length > 2000 ||
        string.IsNullOrWhiteSpace(libraryRootItemId) || libraryRootItemId.Length > 2000 ||
        string.IsNullOrWhiteSpace(expectedPermissionsJson) || expectedPermissionsJson.Length > 8192 || !IsSha256(expectedPermissionsSha256) ||
        !IsSha256(authorizedStaffSha256))
      throw new ArgumentException("Archive-copy target revision is incomplete.");

    var tenant = tenantId.Trim();
    var site = siteId.Trim();
    var drive = driveId.Trim();
    var root = libraryRootItemId.Trim();
    var groupId = int.Parse(staffGroupId, System.Globalization.CultureInfo.InvariantCulture)
      .ToString(System.Globalization.CultureInfo.InvariantCulture);
    var permissionHash = expectedPermissionsSha256.ToLowerInvariant();
    if (!HasExactStaffReadExpectation(expectedPermissionsJson, groupId) ||
        !string.Equals(Hashing.Sha256Hex(Encoding.UTF8.GetBytes(expectedPermissionsJson)), permissionHash,
          StringComparison.Ordinal))
      throw new ArgumentException("Archive-copy target must bind the exact client staff group read grant and its digest.", nameof(expectedPermissionsSha256));

    const string targetCode = "SECONDARY_ARCHIVE";
    var configurationHash = ComputeConfigurationSha256(firmId, revision, connectionRevisionId,
      tenant, site, drive, root, clientId, clientSharePointSiteId, groupId, permissionHash, authorizedStaffSha256.ToLowerInvariant());

    return new ArchiveCopyTargetRevision
    {
      Id = id, FirmId = firmId, ClientId = clientId, ClientSharePointSiteId = clientSharePointSiteId,
      StaffGroupId = groupId,
      TargetCode = targetCode, Revision = revision,
      ConnectionRevisionId = connectionRevisionId, TenantId = tenant, SiteId = site,
      DriveId = drive, LibraryRootItemId = root, ExpectedPermissionsJson = expectedPermissionsJson,
      ExpectedPermissionsSha256 = permissionHash, AuthorizedStaffSha256 = authorizedStaffSha256.ToLowerInvariant(),
      ConfigurationSha256 = configurationHash,
      CreatedByUserId = createdByUserId, CreatedAt = createdAt.ToUniversalTime()
    };
  }

  public static ArchiveCopyTargetRevision CreateSharedSelectedResource(
    Guid id, Guid firmId, Guid clientId, string staffGroupId, long revision, Guid connectionRevisionId,
    string tenantId, string siteId, string driveId, string libraryRootItemId,
    Guid selectedResourceDraftId, long selectedResourceDraftRevision, Guid selectedResourceVerificationId,
    string selectedResourceSiteUrl, string selectedResourceRootFolderId, string selectedResourceEvidenceSha256,
    string expectedPermissionsJson, string expectedPermissionsSha256, string authorizedStaffSha256,
    Guid createdByUserId, DateTimeOffset createdAt)
  {
    if (id == Guid.Empty || firmId == Guid.Empty || clientId == Guid.Empty || !IsSharePointGroupId(staffGroupId) ||
        revision < 1 || connectionRevisionId == Guid.Empty || selectedResourceDraftId == Guid.Empty ||
        selectedResourceDraftRevision < 1 || selectedResourceVerificationId == Guid.Empty ||
        createdByUserId == Guid.Empty || createdAt == default ||
        string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 200 ||
        string.IsNullOrWhiteSpace(siteId) || siteId.Length > 2000 ||
        string.IsNullOrWhiteSpace(driveId) || driveId.Length > 2000 ||
        string.IsNullOrWhiteSpace(libraryRootItemId) || libraryRootItemId.Length > 2000 ||
        string.IsNullOrWhiteSpace(selectedResourceSiteUrl) || selectedResourceSiteUrl.Length > 2000 ||
        string.IsNullOrWhiteSpace(selectedResourceRootFolderId) || selectedResourceRootFolderId.Length > 2000 ||
        !string.Equals(libraryRootItemId.Trim(), selectedResourceRootFolderId.Trim(), StringComparison.Ordinal) ||
        !IsExactSharePointSiteUrl(selectedResourceSiteUrl) ||
        !IsSha256(selectedResourceEvidenceSha256) ||
        string.IsNullOrWhiteSpace(expectedPermissionsJson) || expectedPermissionsJson.Length > 8192 ||
        !IsSha256(expectedPermissionsSha256) || !IsSha256(authorizedStaffSha256))
      throw new ArgumentException("Shared archive-copy target revision is incomplete.");

    var tenant = tenantId.Trim();
    var site = siteId.Trim();
    var drive = driveId.Trim();
    var root = libraryRootItemId.Trim();
    var siteUrl = selectedResourceSiteUrl.Trim();
    var selectedRoot = selectedResourceRootFolderId.Trim();
    var groupId = int.Parse(staffGroupId, System.Globalization.CultureInfo.InvariantCulture)
      .ToString(System.Globalization.CultureInfo.InvariantCulture);
    var permissionHash = expectedPermissionsSha256.ToLowerInvariant();
    var evidenceHash = selectedResourceEvidenceSha256.ToLowerInvariant();
    var staffHash = authorizedStaffSha256.ToLowerInvariant();
    if (!HasExactStaffReadExpectation(expectedPermissionsJson, groupId) ||
        !string.Equals(Hashing.Sha256Hex(Encoding.UTF8.GetBytes(expectedPermissionsJson)), permissionHash, StringComparison.Ordinal))
      throw new ArgumentException("Archive-copy target must bind the exact client staff group read grant and its digest.", nameof(expectedPermissionsSha256));

    const string targetCode = "SECONDARY_ARCHIVE";
    var configurationHash = ComputeSharedConfigurationSha256(firmId, clientId, groupId, revision,
      connectionRevisionId, tenant, site, drive, root, selectedResourceDraftId, selectedResourceDraftRevision,
      selectedResourceVerificationId, siteUrl, selectedRoot, evidenceHash, permissionHash, staffHash);
    return new ArchiveCopyTargetRevision
    {
      Id = id, FirmId = firmId, ClientId = clientId, ClientSharePointSiteId = null,
      SelectedResourceDraftId = selectedResourceDraftId,
      SelectedResourceDraftRevision = selectedResourceDraftRevision,
      SelectedResourceVerificationId = selectedResourceVerificationId,
      SelectedResourceSiteUrl = siteUrl,
      SelectedResourceRootFolderId = selectedRoot,
      SelectedResourceEvidenceSha256 = evidenceHash,
      StaffGroupId = groupId, TargetCode = targetCode, Revision = revision,
      ConnectionRevisionId = connectionRevisionId, TenantId = tenant, SiteId = site,
      DriveId = drive, LibraryRootItemId = root, ExpectedPermissionsJson = expectedPermissionsJson,
      ExpectedPermissionsSha256 = permissionHash, AuthorizedStaffSha256 = staffHash,
      ConfigurationSha256 = configurationHash, CreatedByUserId = createdByUserId,
      CreatedAt = createdAt.ToUniversalTime()
    };
  }

  private static string ComputeConfigurationSha256(Guid firmId, long revision, Guid connectionRevisionId,
    string tenantId, string siteId, string driveId, string libraryRootItemId, Guid clientId,
    Guid clientSharePointSiteId, string staffGroupId, string expectedPermissionsSha256, string authorizedStaffSha256) =>
    Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      targetCode = "SECONDARY_ARCHIVE", firmId, clientId, clientSharePointSiteId, staffGroupId, revision, connectionRevisionId,
      tenant = tenantId, site = siteId, drive = driveId, root = libraryRootItemId,
      expectedPermissionsSha256, authorizedStaffSha256
    }));

  private static string ComputeSharedConfigurationSha256(Guid firmId, Guid clientId, string staffGroupId,
    long revision, Guid connectionRevisionId, string tenantId, string siteId, string driveId,
    string libraryRootItemId, Guid selectedResourceDraftId, long selectedResourceDraftRevision,
    Guid selectedResourceVerificationId, string selectedResourceSiteUrl, string selectedResourceRootFolderId,
    string selectedResourceEvidenceSha256, string expectedPermissionsSha256, string authorizedStaffSha256) =>
    Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      targetCode = "SECONDARY_ARCHIVE", bindingKind = "SHARED_SELECTED_RESOURCE", firmId, clientId,
      staffGroupId, revision, connectionRevisionId, tenant = tenantId, site = siteId, drive = driveId,
      root = libraryRootItemId, selectedResourceDraftId, selectedResourceDraftRevision,
      selectedResourceVerificationId, selectedResourceSiteUrl, selectedResourceRootFolderId,
      selectedResourceEvidenceSha256, expectedPermissionsSha256, authorizedStaffSha256
    }));

  private static bool IsSharePointGroupId(string? value) =>
    int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) &&
    id > 0 && string.Equals(id.ToString(System.Globalization.CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

  private static bool HasExactStaffReadExpectation(string? json, string staffGroupId)
  {
    if (string.IsNullOrWhiteSpace(json)) return false;
    var expected = JsonSerializer.Serialize(new[]
    {
      new { PrincipalKind = "SHAREPOINT_GROUP", PrincipalId = staffGroupId, Role = "READ" }
    });
    return string.Equals(json, expected, StringComparison.Ordinal);
  }

  private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

  private static bool IsExactSharePointSiteUrl(string value) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
    uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
    uri.IdnHost.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath != "/" &&
    string.Equals(uri.AbsoluteUri.TrimEnd('/'), value.TrimEnd('/'), StringComparison.Ordinal);
}

/// <summary>Append-only destination readback for one exact source file and operation attempt.</summary>
public sealed class ArchiveCopyFileReceipt
{
  private ArchiveCopyFileReceipt() { }

  public Guid Id { get; private set; }
  public Guid FirmId { get; private set; }
  public Guid ClientId { get; private set; }
  public Guid EngagementId { get; private set; }
  public Guid OperationId { get; private set; }
  public long AttemptToken { get; private set; }
  public Guid ArchiveId { get; private set; }
  public Guid ArchiveManifestId { get; private set; }
  public long ArchiveManifestVersion { get; private set; }
  public string ArchiveManifestSha256 { get; private set; } = "";
  public Guid ReleaseId { get; private set; }
  public Guid CheckpointId { get; private set; }
  public Guid TargetRevisionId { get; private set; }
  public long TargetRevision { get; private set; }
  public string TargetConfigurationSha256 { get; private set; } = "";
  public string SourceKind { get; private set; } = "";
  public string SourceKeySha256 { get; private set; } = "";
  public Guid? SourceId { get; private set; }
  public Guid? ManifestEntryId { get; private set; }
  public string SourceVersion { get; private set; } = "";
  public string SourceSha256 { get; private set; } = "";
  public long SourceByteCount { get; private set; }
  public string RelativePath { get; private set; } = "";
  public string DestinationTenantId { get; private set; } = "";
  public string DestinationSiteId { get; private set; } = "";
  public string DestinationDriveId { get; private set; } = "";
  public string DestinationLibraryRootItemId { get; private set; } = "";
  public bool HasExactTargetBinding { get; private set; }
  public string DestinationItemId { get; private set; } = "";
  public string DestinationVersion { get; private set; } = "";
  public string ReadBackSha256 { get; private set; } = "";
  public long ReadBackByteCount { get; private set; }
  public string VerificationOutcome { get; private set; } = "";
  public DateTimeOffset ReadBackAt { get; private set; }

  public static ArchiveCopyFileReceipt Create(
    Guid id, Guid firmId, Guid clientId, Guid engagementId, Guid operationId, long attemptToken,
    Guid archiveId, Guid archiveManifestId, long archiveManifestVersion, string archiveManifestSha256,
    Guid releaseId, Guid checkpointId, Guid targetRevisionId, long targetRevision, string targetConfigurationSha256,
    string sourceKind, string sourceKeySha256, Guid? sourceId, Guid? manifestEntryId,
    string sourceVersion, string sourceSha256, long sourceByteCount, string relativePath,
    string destinationTenantId, string destinationSiteId, string destinationDriveId, string destinationLibraryRootItemId,
    bool hasExactTargetBinding, string destinationItemId, string destinationVersion, string readBackSha256, long readBackByteCount,
    DateTimeOffset readBackAt)
  {
    if (id == Guid.Empty || firmId == Guid.Empty || clientId == Guid.Empty || engagementId == Guid.Empty ||
        operationId == Guid.Empty || attemptToken < 1 || archiveId == Guid.Empty || archiveManifestId == Guid.Empty ||
        archiveManifestVersion < 1 || releaseId == Guid.Empty || checkpointId == Guid.Empty ||
        targetRevisionId == Guid.Empty || targetRevision < 1 ||
        !IsSha256(archiveManifestSha256) || !IsSha256(targetConfigurationSha256) ||
        !IsSha256(sourceKeySha256) || !IsSha256(sourceSha256) || !IsSha256(readBackSha256) ||
        sourceKind is not (ArchiveCopySourceKinds.CheckpointManifest or ArchiveCopySourceKinds.ReleasedArtifact or ArchiveCopySourceKinds.ReviewedManifestEntry) ||
        (sourceKind == ArchiveCopySourceKinds.ReviewedManifestEntry) != manifestEntryId.HasValue ||
        (sourceKind == ArchiveCopySourceKinds.CheckpointManifest && sourceId != checkpointId) ||
        (sourceKind == ArchiveCopySourceKinds.ReleasedArtifact && sourceId is null) ||
        string.IsNullOrWhiteSpace(sourceVersion) || sourceVersion.Length > 500 || sourceByteCount < 0 ||
        string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 2000 ||
        string.IsNullOrWhiteSpace(destinationTenantId) || destinationTenantId.Length > 200 ||
        string.IsNullOrWhiteSpace(destinationSiteId) || destinationSiteId.Length > 2000 ||
        string.IsNullOrWhiteSpace(destinationDriveId) || destinationDriveId.Length > 2000 ||
        string.IsNullOrWhiteSpace(destinationLibraryRootItemId) || destinationLibraryRootItemId.Length > 2000 ||
        string.IsNullOrWhiteSpace(destinationItemId) || destinationItemId.Length > 500 ||
        string.IsNullOrWhiteSpace(destinationVersion) || destinationVersion.Length > 500 ||
        readBackByteCount < 0 || readBackAt == default)
      throw new ArgumentException("Archive-copy file receipt is incomplete.");

    var outcome = hasExactTargetBinding && sourceByteCount == readBackByteCount &&
      string.Equals(sourceSha256, readBackSha256, StringComparison.OrdinalIgnoreCase)
        ? ArchiveCopyVerificationOutcomes.Match : ArchiveCopyVerificationOutcomes.Mismatch;
    return new ArchiveCopyFileReceipt
    {
      Id = id, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      OperationId = operationId, AttemptToken = attemptToken, ArchiveId = archiveId,
      ArchiveManifestId = archiveManifestId, ArchiveManifestVersion = archiveManifestVersion,
      ArchiveManifestSha256 = archiveManifestSha256.ToLowerInvariant(), ReleaseId = releaseId,
      CheckpointId = checkpointId, TargetRevisionId = targetRevisionId, TargetRevision = targetRevision,
      TargetConfigurationSha256 = targetConfigurationSha256.ToLowerInvariant(), SourceKind = sourceKind,
      SourceKeySha256 = sourceKeySha256.ToLowerInvariant(), SourceId = sourceId, ManifestEntryId = manifestEntryId,
      SourceVersion = sourceVersion, SourceSha256 = sourceSha256.ToLowerInvariant(), SourceByteCount = sourceByteCount,
      RelativePath = relativePath, DestinationTenantId = destinationTenantId, DestinationSiteId = destinationSiteId,
      DestinationDriveId = destinationDriveId, DestinationLibraryRootItemId = destinationLibraryRootItemId,
      HasExactTargetBinding = hasExactTargetBinding,
      DestinationItemId = destinationItemId, DestinationVersion = destinationVersion,
      ReadBackSha256 = readBackSha256.ToLowerInvariant(), ReadBackByteCount = readBackByteCount,
      VerificationOutcome = outcome, ReadBackAt = readBackAt.ToUniversalTime()
    };
  }

  private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}

/// <summary>Append-only exact expected-versus-observed permission-set readback for one operation attempt.</summary>
public sealed class ArchiveCopyPermissionObservation
{
  private ArchiveCopyPermissionObservation() { }

  public Guid Id { get; private set; }
  public Guid FirmId { get; private set; }
  public Guid ClientId { get; private set; }
  public Guid EngagementId { get; private set; }
  public Guid OperationId { get; private set; }
  public long AttemptToken { get; private set; }
  public Guid ArchiveId { get; private set; }
  public Guid ArchiveManifestId { get; private set; }
  public long ArchiveManifestVersion { get; private set; }
  public string ArchiveManifestSha256 { get; private set; } = "";
  public Guid ReleaseId { get; private set; }
  public Guid CheckpointId { get; private set; }
  public Guid TargetRevisionId { get; private set; }
  public long TargetRevision { get; private set; }
  public string TargetConfigurationSha256 { get; private set; } = "";
  public string AuthorizedStaffSha256 { get; private set; } = "";
  public string StaffGroupId { get; private set; } = "";
  public string ExpectedPermissionsJson { get; private set; } = "[]";
  public string ExpectedPermissionsSha256 { get; private set; } = "";
  public string ObservedPermissionsJson { get; private set; } = "[]";
  public string ObservedPermissionsSha256 { get; private set; } = "";
  public bool HasUniquePermissionScope { get; private set; }
  public bool HasExactTargetBinding { get; private set; }
  public string ObservedStaffSha256 { get; private set; } = "";
  public string ObservedTenantId { get; private set; } = "";
  public string ObservedSiteId { get; private set; } = "";
  public string ObservedDriveId { get; private set; } = "";
  public string ObservedLibraryRootItemId { get; private set; } = "";
  public string ComparisonOutcome { get; private set; } = "";
  public DateTimeOffset ObservedAt { get; private set; }

  public static ArchiveCopyPermissionObservation Create(
    Guid id, Guid firmId, Guid clientId, Guid engagementId, Guid operationId, long attemptToken,
    Guid archiveId, Guid archiveManifestId, long archiveManifestVersion, string archiveManifestSha256,
    Guid releaseId, Guid checkpointId, Guid targetRevisionId, long targetRevision, string targetConfigurationSha256,
    string authorizedStaffSha256, string staffGroupId, string expectedPermissionsJson, string expectedPermissionsSha256,
    string observedPermissionsJson, string observedPermissionsSha256, bool hasUniquePermissionScope,
    bool hasExactTargetBinding, string observedStaffSha256, string observedTenantId, string observedSiteId, string observedDriveId,
    string observedLibraryRootItemId,
    DateTimeOffset observedAt)
  {
    if (id == Guid.Empty || firmId == Guid.Empty || clientId == Guid.Empty || engagementId == Guid.Empty ||
        operationId == Guid.Empty || attemptToken < 1 || archiveId == Guid.Empty || archiveManifestId == Guid.Empty ||
        archiveManifestVersion < 1 || releaseId == Guid.Empty || checkpointId == Guid.Empty ||
        targetRevisionId == Guid.Empty || targetRevision < 1 || !IsSharePointGroupId(staffGroupId) || observedAt == default ||
        !IsSha256(archiveManifestSha256) || !IsSha256(targetConfigurationSha256) ||
        !IsSha256(authorizedStaffSha256) ||
        !IsSha256(expectedPermissionsSha256) || !IsSha256(observedPermissionsSha256) || !IsSha256(observedStaffSha256) ||
        !HasExactStaffReadExpectation(expectedPermissionsJson, staffGroupId) ||
        string.IsNullOrWhiteSpace(observedPermissionsJson) || observedPermissionsJson.Length > 8192 ||
        string.IsNullOrWhiteSpace(observedTenantId) || observedTenantId.Length > 200 ||
        string.IsNullOrWhiteSpace(observedSiteId) || observedSiteId.Length > 2000 ||
        string.IsNullOrWhiteSpace(observedDriveId) || observedDriveId.Length > 2000 ||
        string.IsNullOrWhiteSpace(observedLibraryRootItemId) || observedLibraryRootItemId.Length > 2000 ||
        !string.Equals(Hashing.Sha256Hex(Encoding.UTF8.GetBytes(expectedPermissionsJson)), expectedPermissionsSha256, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(Hashing.Sha256Hex(Encoding.UTF8.GetBytes(observedPermissionsJson)), observedPermissionsSha256, StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException("Archive-copy permission observation is incomplete.");

    return new ArchiveCopyPermissionObservation
    {
      Id = id, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      OperationId = operationId, AttemptToken = attemptToken, ArchiveId = archiveId,
      ArchiveManifestId = archiveManifestId, ArchiveManifestVersion = archiveManifestVersion,
      ArchiveManifestSha256 = archiveManifestSha256.ToLowerInvariant(), ReleaseId = releaseId,
      CheckpointId = checkpointId, TargetRevisionId = targetRevisionId, TargetRevision = targetRevision,
      TargetConfigurationSha256 = targetConfigurationSha256.ToLowerInvariant(),
      AuthorizedStaffSha256 = authorizedStaffSha256.ToLowerInvariant(),
      StaffGroupId = staffGroupId,
      ExpectedPermissionsJson = expectedPermissionsJson, ExpectedPermissionsSha256 = expectedPermissionsSha256.ToLowerInvariant(),
      ObservedPermissionsJson = observedPermissionsJson, ObservedPermissionsSha256 = observedPermissionsSha256.ToLowerInvariant(),
      HasUniquePermissionScope = hasUniquePermissionScope,
      HasExactTargetBinding = hasExactTargetBinding,
      ObservedStaffSha256 = observedStaffSha256.ToLowerInvariant(),
      ObservedTenantId = observedTenantId, ObservedSiteId = observedSiteId, ObservedDriveId = observedDriveId,
      ObservedLibraryRootItemId = observedLibraryRootItemId,
      ComparisonOutcome = hasUniquePermissionScope && hasExactTargetBinding &&
        HasExactStaffReadExpectation(observedPermissionsJson, staffGroupId) &&
        string.Equals(authorizedStaffSha256, observedStaffSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expectedPermissionsSha256, observedPermissionsSha256, StringComparison.OrdinalIgnoreCase)
        ? ArchiveCopyVerificationOutcomes.Match : ArchiveCopyVerificationOutcomes.Mismatch,
      ObservedAt = observedAt.ToUniversalTime()
    };
  }

  private static bool HasExactStaffReadExpectation(string? json, string staffGroupId) =>
    string.Equals(json, JsonSerializer.Serialize(new[]
    {
      new { PrincipalKind = "SHAREPOINT_GROUP", PrincipalId = staffGroupId, Role = "READ" }
    }), StringComparison.Ordinal);

  private static bool IsSharePointGroupId(string? value) =>
    int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) &&
    id > 0 && string.Equals(id.ToString(System.Globalization.CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

  private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
