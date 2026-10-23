using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Records;

public sealed record EnqueueArchiveCopyRequest(Guid ArchiveId, Guid ReleaseId, Guid TargetRevisionId);
public sealed record ArchiveCopyQueued(Guid OperationId, string IdempotencyKey);

public sealed record CreateArchiveCopyTargetRevisionRequest(
  Guid ClientId,
  Guid? ClientSharePointSiteId,
  string ArchiveLibraryDriveId,
  string LibraryRootItemId,
  Guid? SelectedResourceDraftId = null,
  string? StaffGroupId = null);

public sealed record ArchiveCopyTargetRevisionView(
  Guid ClientId, Guid TargetRevisionId, long Revision, string ConfigurationSha256, string ExpectedPermissionsSha256);

/// <summary>
/// Server-built durable payload. It contains only immutable identity and revision evidence;
/// actual bytes and current authority are re-resolved by the worker.
/// </summary>
public sealed record ArchiveCopyOperationPayload(
  Guid FirmId,
  Guid ArchiveId,
  Guid ClientId,
  Guid EngagementId,
  Guid ManifestId,
  long ManifestVersion,
  int ManifestEntryCount,
  string ManifestSha256,
  Guid ManifestReviewedByUserId,
  DateTimeOffset ManifestReviewedAt,
  Guid RecordsProfileId,
  string RecordsProfileCode,
  long RecordsProfileVersion,
  Guid ProfileApprovedByUserId,
  DateTimeOffset ProfileApprovedAt,
  string ProfileSha256,
  Guid ReleaseId,
  Guid ReleaseCandidateId,
  long ReleaseCandidateRevision,
  string ReleaseManifestSha256,
  Guid CheckpointId,
  string CheckpointManifestSha256,
  string CheckpointReadBackSha256,
  DateTimeOffset CheckpointVerifiedAt,
  Guid ReleasedArtifactId,
  string ReleasedArtifactVersion,
  string ReleasedArtifactSha256,
  long ReleasedArtifactByteCount,
  DateTimeOffset ReleaseReleasedAt,
  Guid ReleaseReleasedByUserId,
  Guid TargetRevisionId,
  Guid? ClientSharePointSiteId,
  long TargetRevision,
  string TargetConfigurationSha256,
  string ExpectedPermissionsSha256,
  string AuthorizedStaffSha256,
  Guid OriginatorId,
  long OriginatorSessionEpoch);

public sealed record ArchiveCopyPermissionGrant(string PrincipalKind, string PrincipalId, string Role);

/// <summary>Canonical exact-set encoding for configured and read-back SharePoint permissions.</summary>
public static class ArchiveCopyPermissionEncoding
{
  public const int MaximumPermissionGrants = 32;
  private static readonly HashSet<string> SemanticRoles = new(StringComparer.Ordinal)
    { "READ", "WRITE", "FULL_CONTROL", "OWNER" };

  public static IReadOnlyList<ArchiveCopyPermissionGrant> Canonicalize(IEnumerable<ArchiveCopyPermissionGrant> grants)
  {
    if (grants is null)
      throw new OperationBlockedException("archive-copy-permission-set-invalid", authorization: true);
    var rows = new List<ArchiveCopyPermissionGrant>();
    foreach (var grant in grants)
    {
      if (grant is null || grant.PrincipalKind is null || grant.PrincipalId is null || grant.Role is null)
        throw new OperationBlockedException("archive-copy-permission-grant-invalid", authorization: true);
      if (rows.Count >= MaximumPermissionGrants || grant.PrincipalKind.Length > 40 ||
          grant.PrincipalId.Length > 100 || grant.Role.Length > 40)
        throw new OperationBlockedException("archive-copy-permission-set-invalid", authorization: true);
      var kind = grant.PrincipalKind.Trim().ToUpperInvariant();
      var rawPrincipal = grant.PrincipalId.Trim();
      var principal = kind switch
      {
        "SHAREPOINT_GROUP" when int.TryParse(rawPrincipal, System.Globalization.NumberStyles.None,
          System.Globalization.CultureInfo.InvariantCulture, out var groupId) && groupId > 0 =>
          groupId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "USER" or "APPLICATION" when Guid.TryParse(rawPrincipal, out var objectId) => objectId.ToString("D"),
        _ => throw new OperationBlockedException("archive-copy-permission-principal-invalid", authorization: true)
      };
      var role = grant.Role.Trim().ToUpperInvariant();
      if (!SemanticRoles.Contains(role))
        throw new OperationBlockedException("archive-copy-permission-grant-invalid", authorization: true);
      rows.Add(new ArchiveCopyPermissionGrant(kind, principal, role));
    }

    var canonicalRows = rows.OrderBy(x => x.PrincipalKind, StringComparer.Ordinal)
      .ThenBy(x => x.PrincipalId, StringComparer.Ordinal)
      .ThenBy(x => x.Role, StringComparer.Ordinal)
      .ToArray();

    if (canonicalRows.Distinct().Count() != canonicalRows.Length)
      throw new OperationBlockedException("archive-copy-permission-set-invalid", authorization: true);
    return canonicalRows;
  }

  /// <summary>Expected archive-library ACL is fixed to the exact current client staff group at read level.</summary>
  public static IReadOnlyList<ArchiveCopyPermissionGrant> ClientStaffRead(string staffGroupId) =>
    Canonicalize([new ArchiveCopyPermissionGrant("SHAREPOINT_GROUP", staffGroupId, "READ")]);

  /// <summary>Canonical digest of the authorized client staff Entra object IDs.</summary>
  public static string StaffDigest(IEnumerable<string> objectIds)
  {
    if (objectIds is null) throw new OperationBlockedException("archive-copy-staff-set-invalid", authorization: true);
    var ids = objectIds.Select(x => Guid.TryParse(x, out var id) ? id.ToString("D") :
        throw new OperationBlockedException("archive-copy-staff-set-invalid", authorization: true))
      .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (ids.Length is 0 or > 200)
      throw new OperationBlockedException("archive-copy-staff-set-invalid", authorization: true);
    return Hashing.Sha256Hex(Encoding.UTF8.GetBytes(string.Join("\u001f", ids)));
  }

  public static bool IsExactClientStaffReadGrant(IEnumerable<ArchiveCopyPermissionGrant> grants, string staffGroupId)
  {
    var expected = Encode(ClientStaffRead(staffGroupId));
    var observed = Encode(grants);
    return string.Equals(expected.Sha256, observed.Sha256, StringComparison.Ordinal);
  }

  public static (string Json, string Sha256) Encode(IEnumerable<ArchiveCopyPermissionGrant> grants)
  {
    var canonical = Canonicalize(grants);
    var json = JsonSerializer.Serialize(canonical);
    return (json, Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(canonical)));
  }

  public static (IReadOnlyList<ArchiveCopyPermissionGrant> Grants, string Json, string Sha256) CanonicalReadback(
    IEnumerable<ArchiveCopyPermissionGrant> grants)
  {
    var canonical = Canonicalize(grants);
    var encoded = Encode(canonical);
    return (canonical, encoded.Json, encoded.Sha256);
  }

  public static IReadOnlyList<ArchiveCopyPermissionGrant> Decode(string json)
  {
    if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumPermissionGrants * 256)
      throw new OperationBlockedException("archive-copy-permission-set-invalid", authorization: true);
    ArchiveCopyPermissionGrant[]? grants;
    try { grants = JsonSerializer.Deserialize<ArchiveCopyPermissionGrant[]>(json); }
    catch (JsonException)
    {
      throw new OperationBlockedException("archive-copy-permission-set-invalid", authorization: true);
    }
    if (grants is null)
      throw new OperationBlockedException("archive-copy-permission-set-invalid", authorization: true);
    var canonical = Canonicalize(grants);
    if (!string.Equals(JsonSerializer.Serialize(canonical), json, StringComparison.Ordinal))
      throw new OperationBlockedException("archive-copy-permission-set-noncanonical", authorization: true);
    return canonical;
  }
}

public sealed record ArchiveCopyProviderTarget(
  Guid FirmId,
  Guid ClientId,
  Guid? ClientSharePointSiteId,
  Guid ConnectionRevisionId,
  Guid TargetRevisionId,
  long TargetRevision,
  string ConfigurationSha256,
  string OwnershipMarker,
  string TenantId,
  string SiteId,
  string DriveId,
  string LibraryRootItemId,
  string StaffGroupId,
  string AuthorizedStaffSha256,
  string ExpectedPermissionsSha256,
  string SiteUrl = "",
  string? SelectedResourceRootFolderId = null)
{
  public bool UsesSharedSelectedResource => ClientSharePointSiteId is null && SelectedResourceRootFolderId is not null;
}

public sealed record ArchiveCopySnapshotReadRequest(
  Guid FirmId,
  Guid ClientId,
  Guid EngagementId,
  Guid SnapshotId,
  string TenantId,
  string SiteId,
  string DriveId,
  string ItemId,
  string VersionId,
  string ExpectedSha256,
  long ExpectedByteCount,
  [property: JsonIgnore] Func<CancellationToken, Task>? RecheckAuthorization = null);

public sealed record ArchiveCopySnapshotContent(string VersionId, byte[] Content);

public sealed record ArchiveCopyFileWriteRequest(
  ArchiveCopyProviderTarget Target,
  Guid OperationId,
  string SourceKeySha256,
  string RelativePath,
  string ExpectedSha256,
  byte[] Content,
  [property: JsonIgnore] Func<CancellationToken, Task>? RecheckAuthorization = null)
{
  public string IdempotencyKey =>
    $"archive-copy-file-v1:{Target.ClientId:N}:{Target.TargetRevisionId:N}:{SourceKeySha256}";
}

/// <summary>The adapter returns bytes fetched from the exact destination item/version after the write.</summary>
public sealed record ArchiveCopyFileReadback(
  string TenantId,
  string SiteId,
  string DriveId,
  string LibraryRootItemId,
  string RelativePath,
  string DestinationItemId,
  string DestinationVersion,
  byte[] Content,
  DateTimeOffset ReadBackAt);

public sealed record ArchiveCopyPermissionReadRequest(
  ArchiveCopyProviderTarget Target,
  [property: JsonIgnore] Func<CancellationToken, Task>? RecheckAuthorization = null);

public sealed record ArchiveCopyPermissionReadback(
  IReadOnlyList<ArchiveCopyPermissionGrant> Grants,
  bool HasUniquePermissionScope,
  string ObservedStaffSha256,
  string TenantId,
  string SiteId,
  string DriveId,
  string LibraryRootItemId,
  DateTimeOffset ReadBackAt);

/// <summary>
/// Implemented by the existing client-sites SharePoint provider. It must resolve and verify the
/// ownership marker, exact site, archive drive and library namespace before any effect. Shared
/// namespaces are client-marked folders under an already verified selected-resource root. The
/// library ACL must have a unique scope and exactly the authorized client staff SharePoint group
/// with READ; readback also binds its live member digest to the authorized staff digest. Staff
/// WRITE/FULL_CONTROL or any unbound residual grant cannot match. File writes are create-if-absent
/// and exact-readback; retries use the stable target/source key above and never overwrite. Attempt
/// tokens fence publication only and are not sent to this provider.
/// </summary>
public interface IClientSharePointArchiveCopyProvider
{
  Task<ArchiveCopySnapshotContent> ReadExactSnapshotAsync(ArchiveCopySnapshotReadRequest request, CancellationToken ct);
  Task<ArchiveCopyFileReadback> CreateOrReadExactFileAsync(ArchiveCopyFileWriteRequest request, CancellationToken ct);
  Task<ArchiveCopyPermissionReadback> ReadTargetPermissionsAsync(ArchiveCopyPermissionReadRequest request, CancellationToken ct);
}

public sealed record ArchiveCopyFileResult(
  string SourceKind,
  string SourceKeySha256,
  Guid? SourceId,
  Guid? ManifestEntryId,
  string SourceVersion,
  string SourceSha256,
  long SourceByteCount,
  string RelativePath,
  string DestinationTenantId,
  string DestinationSiteId,
  string DestinationDriveId,
  string DestinationLibraryRootItemId,
  bool HasExactTargetBinding,
  string DestinationItemId,
  string DestinationVersion,
  string ReadBackSha256,
  long ReadBackByteCount,
  string VerificationOutcome,
  DateTimeOffset ReadBackAt);

public sealed record ArchiveCopyPermissionResult(
  string ExpectedPermissionsJson,
  string ExpectedPermissionsSha256,
  string ObservedPermissionsJson,
  string ObservedPermissionsSha256,
  bool HasUniquePermissionScope,
  bool HasExactTargetBinding,
  string ObservedStaffSha256,
  string ObservedTenantId,
  string ObservedSiteId,
  string ObservedDriveId,
  string ObservedLibraryRootItemId,
  string ComparisonOutcome,
  DateTimeOffset ObservedAt);

/// <summary>Durable operation result; MATCH is byte/grant comparison evidence, never Purview OBSERVED or WORM.</summary>
public sealed record ArchiveCopyOperationResult(
  Guid OperationId,
  long AttemptToken,
  Guid ArchiveId,
  Guid ClientId,
  Guid ManifestId,
  long ManifestVersion,
  string ManifestSha256,
  Guid RecordsProfileId,
  long RecordsProfileVersion,
  Guid ReleaseId,
  Guid CheckpointId,
  Guid? ClientSharePointSiteId,
  Guid TargetRevisionId,
  long TargetRevision,
  string TargetConfigurationSha256,
  string AuthorizedStaffSha256,
  string ProtocolOutcome,
  IReadOnlyList<ArchiveCopyFileResult> Files,
  ArchiveCopyPermissionResult Permissions);

public sealed record ArchiveCopyEvidenceReadback(
  Guid OperationId,
  Guid FirmId,
  Guid ClientId,
  Guid EngagementId,
  string OperationStatus,
  string? OperationResultDigest,
  ArchiveCopyOperationResult? Result,
  IReadOnlyList<ArchiveCopyFileReceiptView> FileReceipts,
  IReadOnlyList<ArchiveCopyPermissionObservationView> PermissionObservations);

public sealed record ArchiveCopyFileReceiptView(
  Guid ReceiptId, long AttemptToken, string SourceKind, string SourceKeySha256, Guid? SourceId,
  Guid? ManifestEntryId, string SourceVersion, string SourceSha256, long SourceByteCount,
  string RelativePath, string DestinationTenantId, string DestinationSiteId, string DestinationDriveId,
  string DestinationLibraryRootItemId, bool HasExactTargetBinding, string DestinationItemId, string DestinationVersion,
  string ReadBackSha256, long ReadBackByteCount, string VerificationOutcome, DateTimeOffset ReadBackAt);

public sealed record ArchiveCopyPermissionObservationView(
  Guid ObservationId, long AttemptToken, string ExpectedPermissionsJson, string ExpectedPermissionsSha256,
  string ObservedPermissionsJson, string ObservedPermissionsSha256, bool HasUniquePermissionScope,
  bool HasExactTargetBinding, string ObservedStaffSha256,
  string ObservedTenantId, string ObservedSiteId, string ObservedDriveId, string ObservedLibraryRootItemId,
  string ComparisonOutcome, DateTimeOffset ObservedAt);

public static class ArchiveCopyPayloadRules
{
  public static string Normalize(OperationRequest request)
  {
    try
    {
      if (request is null || string.IsNullOrWhiteSpace(request.PayloadJson))
        throw new OperationBlockedException("archive-copy-request-invalid");
      var payload = JsonSerializer.Deserialize<ArchiveCopyOperationPayload>(request.PayloadJson)
        ?? throw new OperationBlockedException("archive-copy-request-invalid");
      if (payload.FirmId != request.FirmId || payload.ArchiveId != request.TargetId || payload.ManifestVersion != request.ExpectedRevision ||
          payload.ClientId != request.ClientId || payload.EngagementId != request.EngagementId ||
          payload.OriginatorId != request.OriginatorId || payload.OriginatorId == Guid.Empty ||
          payload.OriginatorSessionEpoch < 1 || payload.FirmId == Guid.Empty || payload.ArchiveId == Guid.Empty ||
          payload.ClientId == Guid.Empty || payload.EngagementId == Guid.Empty || payload.ManifestId == Guid.Empty ||
          payload.ManifestVersion < 1 || payload.ManifestEntryCount < 1 || payload.ManifestReviewedByUserId == Guid.Empty ||
          payload.ManifestReviewedAt == default || payload.RecordsProfileId == Guid.Empty ||
          string.IsNullOrWhiteSpace(payload.RecordsProfileCode) || payload.RecordsProfileVersion < 1 ||
          payload.ProfileApprovedByUserId == Guid.Empty || payload.ProfileApprovedAt == default ||
          payload.ReleaseId == Guid.Empty || payload.ReleaseCandidateId == Guid.Empty || payload.ReleaseCandidateRevision < 1 ||
          payload.CheckpointId == Guid.Empty || payload.CheckpointVerifiedAt == default ||
          payload.ReleasedArtifactId == Guid.Empty || string.IsNullOrWhiteSpace(payload.ReleasedArtifactVersion) ||
          payload.ReleasedArtifactByteCount < 0 || payload.ReleaseReleasedAt == default ||
          payload.ReleaseReleasedByUserId == Guid.Empty || payload.TargetRevisionId == Guid.Empty || payload.TargetRevision < 1 ||
          !AllHashes(payload.ManifestSha256, payload.ProfileSha256, payload.ReleaseManifestSha256,
            payload.CheckpointManifestSha256, payload.CheckpointReadBackSha256, payload.ReleasedArtifactSha256,
            payload.TargetConfigurationSha256, payload.ExpectedPermissionsSha256, payload.AuthorizedStaffSha256))
        throw new OperationBlockedException("archive-copy-request-invalid");

      var canonical = JsonSerializer.Serialize(payload);
      if (canonical.Length > 16384) throw new OperationBlockedException("archive-copy-request-invalid");
      return canonical;
    }
    catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentNullException)
    {
      throw new OperationBlockedException("archive-copy-request-invalid");
    }
  }

  public static string IdempotencyKey(ArchiveCopyOperationPayload payload) =>
    "archive-copy-v1:" + Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(payload));

  public static bool IsSha256(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

  private static bool AllHashes(params string[] values) => values.All(value => IsSha256(value) &&
    string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal));
}
