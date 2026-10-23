using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>Exact, read-only observations and conflict-fail copies through the existing client-sites identity.</summary>
public sealed partial class SharePointClientSiteProvider
{
  private const string ArchiveGraph = "https://graph.microsoft.com/v1.0";
  private const long MaximumArchiveCopyBytes = 250L * 1024 * 1024;
  private const int ArchiveUploadFragmentBytes = 10 * 1024 * 1024;

  private sealed record ArchiveTargetContext(
    ArchiveCopyProviderTarget Target, string GraphToken, string SharePointToken,
    string SiteUrl, string SiteId, string DriveId, string RootItemId,
    Guid LibraryListId, string LibraryRootServerRelativeUrl);

  private sealed record ArchiveDriveItem(
    string Id, string Name, string? ParentId, string? ParentDriveId, long Size,
    bool IsFolder, string? ContentTag);

  private sealed record ArchiveVersion(string Id, long Size);
  private sealed record ArchiveFilePath(string ParentId, string Name, string ParentServerRelativePath);

  public async Task<ArchiveCopySnapshotContent> ReadExactSnapshotAsync(
    ArchiveCopySnapshotReadRequest request, CancellationToken ct)
  {
    ValidateSnapshotRequest(request);
    return await InArchiveCopyScopeAsync(StableCorrelation(
      $"archive-copy-source-v1:{request.ClientId:N}:{request.SnapshotId:N}:{request.VersionId}"),
      request.RecheckAuthorization, async token =>
      {
        if (!string.Equals(request.TenantId, options.TenantId, StringComparison.OrdinalIgnoreCase))
          throw ArchiveBlocked("archive-copy-source-tenant-mismatch", authorization: true);
        var graph = (await graphTokens.GetAsync(request.TenantId, token)).AccessToken;
        var site = await GraphGetAsync($"{ArchiveGraph}/sites/{GraphId(request.SiteId)}?$select=id,webUrl", graph, token);
        if (!string.Equals(Text(site, "id"), request.SiteId, StringComparison.Ordinal))
          throw ArchiveBlocked("archive-copy-source-site-mismatch", authorization: true);
        var siteUrl = ExactSiteUrl(Text(site, "webUrl"));
        var drives = await CollectionAsync($"{ArchiveGraph}/sites/{GraphId(request.SiteId)}/drives?$select=id,driveType", graph, token);
        if (drives.Count(x => string.Equals(Text(x, "id"), request.DriveId, StringComparison.Ordinal) &&
                              string.Equals(Text(x, "driveType"), "documentLibrary", StringComparison.OrdinalIgnoreCase)) != 1)
          throw ArchiveBlocked("archive-copy-source-drive-mismatch", authorization: true);

        var item = await GetDriveItemAsync(graph, request.DriveId, request.ItemId, token);
        if (item.Id != request.ItemId || item.IsFolder || item.ParentDriveId != request.DriveId)
          throw ArchiveBlocked("archive-copy-source-item-mismatch", authorization: true);
        var version = await GetExactVersionAsync(graph, request.DriveId, request.ItemId, request.VersionId, token);
        if (version.Id != request.VersionId || version.Size != request.ExpectedByteCount)
          throw ArchiveBlocked("archive-copy-source-version-mismatch", authorization: true);

        var bytes = await ReadVersionBytesAsync(request.TenantId, graph, request.DriveId,
          request.ItemId, request.VersionId, request.ExpectedByteCount, token);
        if (bytes.LongLength != request.ExpectedByteCount ||
            !string.Equals(Hashing.Sha256Hex(bytes), request.ExpectedSha256, StringComparison.Ordinal))
          throw ArchiveBlocked("archive-copy-source-bytes-mismatch");
        _ = siteUrl;
        return new ArchiveCopySnapshotContent(request.VersionId, bytes);
      }, ct);
  }

  public async Task<ArchiveCopyFileReadback> CreateOrReadExactFileAsync(
    ArchiveCopyFileWriteRequest request, CancellationToken ct)
  {
    ValidateFileRequest(request);
    return await InArchiveCopyScopeAsync(StableCorrelation(request.IdempotencyKey),
      request.RecheckAuthorization, async token =>
      {
        var context = await ResolveArchiveTargetAsync(request.Target, token);
        await RequireExactStaffReadAsync(context, token);

        var path = await EnsureArchivePathAsync(context, request.RelativePath, token);
        await RequireExactStaffReadAsync(context, token);
        await RequireInheritedArchivePathAsync(context, request.RelativePath, path.ParentId, token);
        var existing = await FindChildAsync(context, path.ParentId, path.Name, token);
        if (existing is not null)
          return await ReadExactDestinationAsync(context, existing, path.ParentId, path.Name,
            request.RelativePath, request.ExpectedSha256, request.Content.LongLength, token);

        try
        {
          if (request.Content.Length == 0)
            await CreateEmptyFileAsync(context, path.ParentServerRelativePath, path.Name, token);
          else
            await UploadNewFileAsync(context, path.ParentId, path.Name, request.Content, token);
        }
        catch (Exception exception) when (exception is HttpRequestException or SafeRetryException or OperationBlockedException)
        {
          // An accepted write with a lost response is reconciled only by the deterministic path.
          var reconciled = await FindChildAsync(context, path.ParentId, path.Name, token);
          if (reconciled is null) throw;
          return await ReadExactDestinationAsync(context, reconciled, path.ParentId, path.Name,
            request.RelativePath, request.ExpectedSha256, request.Content.LongLength, token);
        }

        var created = await FindChildAsync(context, path.ParentId, path.Name, token)
          ?? throw new HttpRequestException("archive-copy-file-outcome-unconfirmed");
        return await ReadExactDestinationAsync(context, created, path.ParentId, path.Name,
          request.RelativePath, request.ExpectedSha256, request.Content.LongLength, token);
      }, ct);
  }

  public async Task<ArchiveCopyPermissionReadback> ReadTargetPermissionsAsync(
    ArchiveCopyPermissionReadRequest request, CancellationToken ct)
  {
    if (request is null) throw ArchiveBlocked("archive-copy-permission-request-invalid");
    ValidateArchiveTarget(request.Target);
    return await InArchiveCopyScopeAsync(StableCorrelation(
      $"archive-copy-permissions-v1:{request.Target.ClientId:N}:{request.Target.TargetRevisionId:N}"),
      request.RecheckAuthorization, async token =>
      {
        var context = await ResolveArchiveTargetAsync(request.Target, token);
        return await ReadTargetPermissionsCoreAsync(context, token);
      }, ct);
  }

  private async Task<T> InArchiveCopyScopeAsync<T>(Guid correlation,
    Func<CancellationToken, Task>? recheck, Func<CancellationToken, Task<T>> action, CancellationToken ct)
  {
    var priorCorrelation = clientCorrelation.Value;
    var priorGuard = authorizationGuard.Value;
    var priorProgress = effectProgress.Value;
    clientCorrelation.Value = correlation;
    authorizationGuard.Value = recheck;
    effectProgress.Value = new();
    try
    {
      if (recheck is null) throw ArchiveBlocked("client-site-authorization-guard-required", authorization: true);
      await recheck(ct);
      return await action(ct);
    }
    finally
    {
      clientCorrelation.Value = priorCorrelation;
      authorizationGuard.Value = priorGuard;
      effectProgress.Value = priorProgress;
    }
  }

  private async Task<ArchiveTargetContext> ResolveArchiveTargetAsync(
    ArchiveCopyProviderTarget target, CancellationToken ct)
  {
    ValidateArchiveTarget(target);
    var graph = (await graphTokens.GetAsync(target.TenantId, ct)).AccessToken;
    var sharePoint = await sharePointTokens.GetAsync(ct);
    var site = await GraphGetAsync($"{ArchiveGraph}/sites/{GraphId(target.SiteId)}?$select=id,webUrl", graph, ct);
    var siteId = Text(site, "id");
    var siteUrl = ExactSiteUrl(Text(site, "webUrl"));
    if (!string.Equals(siteId, target.SiteId, StringComparison.Ordinal) ||
        !SameSiteUrl(siteUrl, target.SiteUrl) || !SiteIdMatchesHost(siteId))
      throw ArchiveBlocked("archive-copy-target-site-binding-mismatch", authorization: true);

    var drives = await CollectionAsync($"{ArchiveGraph}/sites/{GraphId(target.SiteId)}/drives?$select=id,driveType", graph, ct);
    var matchingDrive = drives.Where(x => string.Equals(Text(x, "id"), target.DriveId, StringComparison.Ordinal)).ToArray();
    if (matchingDrive.Length != 1 || !string.Equals(Text(matchingDrive[0], "driveType"), "documentLibrary", StringComparison.OrdinalIgnoreCase))
      throw ArchiveBlocked("archive-copy-target-drive-binding-mismatch", authorization: true);

    var drive = await GraphGetAsync(
      $"{ArchiveGraph}/drives/{GraphId(target.DriveId)}?$select=id,driveType,sharepointIds", graph, ct);
    if (!string.Equals(Text(drive, "id"), target.DriveId, StringComparison.Ordinal) ||
        !string.Equals(Text(drive, "driveType"), "documentLibrary", StringComparison.OrdinalIgnoreCase) ||
        !drive.TryGetProperty("sharepointIds", out var sharePointIds) ||
        !Guid.TryParse(Text(sharePointIds, "listId"), out var libraryListId))
      throw ArchiveBlocked("archive-copy-target-library-list-binding-invalid", authorization: true);
    var siteParts = siteId.Split(',');
    if (siteParts.Length != 3 || !Guid.TryParse(siteParts[1], out var expectedSiteCollectionId) ||
        !Guid.TryParse(siteParts[2], out var expectedWebId) ||
        !Guid.TryParse(Text(sharePointIds, "siteId"), out var actualSiteCollectionId) ||
        !Guid.TryParse(Text(sharePointIds, "webId"), out var actualWebId) ||
        actualSiteCollectionId != expectedSiteCollectionId || actualWebId != expectedWebId)
      throw ArchiveBlocked("archive-copy-target-library-site-binding-invalid", authorization: true);

    var driveRoot = await GraphGetAsync($"{ArchiveGraph}/drives/{GraphId(target.DriveId)}/root?$select=id,name,folder,file,parentReference", graph, ct);
    var root = ParseDriveItem(driveRoot);
    if (!root.IsFolder || root.Id != target.LibraryRootItemId || root.Id.Length == 0 ||
        (target.UsesSharedSelectedResource && target.SelectedResourceRootFolderId != root.Id))
      throw ArchiveBlocked("archive-copy-target-library-root-invalid", authorization: true);

    var rootFolder = await CallAsync(HttpMethod.Get,
      $"{siteUrl}/_api/web/lists(guid'{libraryListId:D}')/RootFolder?$select=ServerRelativeUrl",
      sharePoint, null, ct);
    var rootPath = Text(rootFolder.Json, "ServerRelativeUrl");
    if (!IsBounded(rootPath, 2000) || !rootPath.StartsWith("/", StringComparison.Ordinal))
      throw ArchiveBlocked("archive-copy-target-library-path-invalid", authorization: true);
    return new ArchiveTargetContext(target, graph, sharePoint, siteUrl, siteId,
      target.DriveId, target.LibraryRootItemId, libraryListId, rootPath);
  }

  private void ValidateArchiveTarget(ArchiveCopyProviderTarget target)
  {
    if (target is null) throw ArchiveBlocked("archive-copy-target-invalid", authorization: true);
    var sharedBinding = target.ClientSharePointSiteId is null;
    if (target.FirmId == Guid.Empty || target.ClientId == Guid.Empty ||
        target.ConnectionRevisionId == Guid.Empty || target.TargetRevisionId == Guid.Empty || target.TargetRevision < 1 ||
        !string.Equals(target.TenantId, options.TenantId, StringComparison.OrdinalIgnoreCase) ||
        target.ClientSharePointSiteId == Guid.Empty || !SiteIdMatchesHost(target.SiteId) ||
        !IsBounded(target.DriveId, 2000) || !IsBounded(target.LibraryRootItemId, 2000) ||
        !IsBounded(target.StaffGroupId, 20) || !int.TryParse(target.StaffGroupId, NumberStyles.None, CultureInfo.InvariantCulture, out var groupId) || groupId <= 0 ||
        !IsSha256(target.ConfigurationSha256) || !IsSha256(target.AuthorizedStaffSha256) ||
        !string.Equals(target.OwnershipMarker, $"AuditSphere:{target.FirmId:D}:{target.ClientId:D}", StringComparison.Ordinal) ||
        !IsExactProviderSiteUrl(target.SiteUrl) ||
        (target.UsesSharedSelectedResource && (!IsBounded(target.SelectedResourceRootFolderId, 2000) ||
          target.SelectedResourceRootFolderId != target.LibraryRootItemId)) ||
        sharedBinding != (target.SelectedResourceRootFolderId is not null) ||
        (!target.UsesSharedSelectedResource && sharedBinding))
      throw ArchiveBlocked("archive-copy-target-invalid", authorization: true);

    var expected = ArchiveCopyPermissionEncoding.Encode(
      ArchiveCopyPermissionEncoding.ClientStaffRead(target.StaffGroupId));
    if (target.ExpectedPermissionsSha256 != expected.Sha256 || groupId.ToString(CultureInfo.InvariantCulture) != target.StaffGroupId)
      throw ArchiveBlocked("archive-copy-target-permission-policy-invalid", authorization: true);
  }

  private async Task<ArchiveCopyPermissionReadback> ReadTargetPermissionsCoreAsync(
    ArchiveTargetContext context, CancellationToken ct)
  {
    var listUrl = $"{context.SiteUrl}/_api/web/lists(guid'{context.LibraryListId:D}')";
    var list = await CallAsync(HttpMethod.Get, listUrl + "?$select=Id,Description,HasUniqueRoleAssignments",
      context.SharePointToken, null, ct);
    if (!Guid.TryParse(Text(list.Json, "Id"), out var actualListId) || actualListId != context.LibraryListId ||
        !string.Equals(Text(list.Json, "Description"), context.Target.OwnershipMarker, StringComparison.Ordinal) ||
        !list.Json.TryGetProperty("HasUniqueRoleAssignments", out var unique) ||
        unique.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
      throw ArchiveBlocked("archive-copy-target-library-proof-invalid", authorization: true);
    var uniqueScope = unique.ValueKind == JsonValueKind.True;
    var assignments = await CollectionAsync(listUrl + "/roleassignments?$select=PrincipalId,Member/Id,Member/PrincipalType,RoleDefinitionBindings/RoleTypeKind&$expand=Member,RoleDefinitionBindings",
      context.SharePointToken, ct);
    var grants = new List<ArchiveCopyPermissionGrant>();
    foreach (var assignment in assignments)
    {
      if (!assignment.TryGetProperty("Member", out var member) ||
          !member.TryGetProperty("Id", out var idValue) || !idValue.TryGetInt32(out var principalId) || principalId < 1 ||
          !member.TryGetProperty("PrincipalType", out var principalTypeValue) || !principalTypeValue.TryGetInt32(out var principalType))
        throw ArchiveBlocked("archive-copy-permission-principal-invalid", authorization: true);
      var (principalKind, canonicalPrincipalId) = await CanonicalPrincipalAsync(
        context, principalId, principalType, ct);
      if (!assignment.TryGetProperty("RoleDefinitionBindings", out var bindings) || bindings.ValueKind != JsonValueKind.Array)
        throw ArchiveBlocked("archive-copy-permission-roles-invalid", authorization: true);
      foreach (var binding in bindings.EnumerateArray())
      {
        if (!binding.TryGetProperty("RoleTypeKind", out var roleTypeValue) || !roleTypeValue.TryGetInt32(out var roleType))
          throw ArchiveBlocked("archive-copy-permission-role-invalid", authorization: true);
        var role = roleType switch
        {
          2 => "READ",
          3 or 4 => "WRITE",
          5 => "FULL_CONTROL",
          _ => throw ArchiveBlocked("archive-copy-permission-role-unsupported", authorization: true)
        };
        grants.Add(new ArchiveCopyPermissionGrant(principalKind, canonicalPrincipalId, role));
      }
    }

    var staffGroupId = int.Parse(context.Target.StaffGroupId, CultureInfo.InvariantCulture);
    await VerifyStaffGroupAsync(context, staffGroupId, ct);
    var observedStaff = await ReadStaffDigestAsync(context, staffGroupId, ct);
    var canonical = ArchiveCopyPermissionEncoding.CanonicalReadback(grants);
    return new ArchiveCopyPermissionReadback(canonical.Grants, uniqueScope, observedStaff,
      context.Target.TenantId, context.SiteId, context.DriveId, context.RootItemId, DateTimeOffset.UtcNow);
  }

  private async Task<(string Kind, string Id)> CanonicalPrincipalAsync(
    ArchiveTargetContext context, int principalId, int principalType, CancellationToken ct)
  {
    if (principalType == 8)
      return ("SHAREPOINT_GROUP", principalId.ToString(CultureInfo.InvariantCulture));
    if (principalType != 1)
      throw ArchiveBlocked("archive-copy-permission-principal-unsupported", authorization: true);
    var user = await CallAsync(HttpMethod.Get,
      $"{context.SiteUrl}/_api/web/siteusers/getbyid({principalId})?$select=Id,AadObjectId",
      context.SharePointToken, null, ct);
    if (!user.Json.TryGetProperty("AadObjectId", out var aadObjectId))
      throw ArchiveBlocked("archive-copy-permission-user-identity-unavailable", authorization: true);
    var raw = aadObjectId.ValueKind == JsonValueKind.String ? aadObjectId.GetString() :
      aadObjectId.ValueKind == JsonValueKind.Object && aadObjectId.TryGetProperty("NameId", out var nameId) ? nameId.GetString() : null;
    if (!Guid.TryParse(raw, out var objectId))
      throw ArchiveBlocked("archive-copy-permission-user-identity-invalid", authorization: true);
    return ("USER", objectId.ToString("D"));
  }

  private async Task VerifyStaffGroupAsync(ArchiveTargetContext context, int groupId, CancellationToken ct)
  {
    var group = await CallAsync(HttpMethod.Get,
      $"{context.SiteUrl}/_api/web/sitegroups/getbyid({groupId})?$select=Id,Title,Description",
      context.SharePointToken, null, ct);
    var expectedTitle = context.Target.UsesSharedSelectedResource
      ? $"AuditSphere Archive Staff {context.Target.ClientId:N}"
      : "AuditSphere Assigned Staff";
    if (!group.Json.TryGetProperty("Id", out var actualId) || !actualId.TryGetInt32(out var parsedId) || parsedId != groupId ||
        !string.Equals(Text(group.Json, "Title"), expectedTitle, StringComparison.Ordinal) ||
        !string.Equals(Text(group.Json, "Description"), context.Target.OwnershipMarker, StringComparison.Ordinal))
      throw ArchiveBlocked("archive-copy-client-staff-group-marker-mismatch", authorization: true);
  }

  private async Task<string> ReadStaffDigestAsync(ArchiveTargetContext context, int groupId, CancellationToken ct)
  {
    var groupUsers = await CollectionAsync(
      $"{context.SiteUrl}/_api/web/sitegroups/getbyid({groupId})/users?$select=Id",
      context.SharePointToken, ct);
    if (groupUsers.Count is 0 or > 200) throw ArchiveBlocked("archive-copy-client-staff-group-members-invalid", authorization: true);
    var objectIds = new List<string>(groupUsers.Count);
    var seenPrincipalIds = new HashSet<int>();
    var directoryToken = (await directoryTokens.GetAsync(context.Target.TenantId, ct)).AccessToken;
    foreach (var groupUser in groupUsers)
    {
      if (!groupUser.TryGetProperty("Id", out var rawId) || !rawId.TryGetInt32(out var principalId) ||
          principalId < 1 || !seenPrincipalIds.Add(principalId))
        throw ArchiveBlocked("archive-copy-client-staff-group-members-invalid", authorization: true);
      var principal = await CallAsync(HttpMethod.Get,
        $"{context.SiteUrl}/_api/web/siteusers/getbyid({principalId})?$select=Id,AadObjectId",
        context.SharePointToken, null, ct);
      if (!principal.Json.TryGetProperty("AadObjectId", out var identity))
        throw ArchiveBlocked("archive-copy-client-staff-object-id-unavailable", authorization: true);
      var rawObjectId = identity.ValueKind == JsonValueKind.String ? identity.GetString() :
        identity.ValueKind == JsonValueKind.Object && identity.TryGetProperty("NameId", out var nameId) ? nameId.GetString() : null;
      if (!Guid.TryParse(rawObjectId, out var objectId))
        throw ArchiveBlocked("archive-copy-client-staff-object-id-invalid", authorization: true);
      var canonical = objectId.ToString("D");
      if (await DirectoryIdentityAsync(canonical, directoryToken, ct) is null)
        throw ArchiveBlocked("archive-copy-client-staff-member-inactive", authorization: true);
      objectIds.Add(canonical);
    }
    return ArchiveCopyPermissionEncoding.StaffDigest(objectIds);
  }

  private async Task RequireExactStaffReadAsync(ArchiveTargetContext context, CancellationToken ct)
  {
    var readback = await ReadTargetPermissionsCoreAsync(context, ct);
    var expected = ArchiveCopyPermissionEncoding.Encode(
      ArchiveCopyPermissionEncoding.ClientStaffRead(context.Target.StaffGroupId));
    var observed = ArchiveCopyPermissionEncoding.Encode(readback.Grants);
    if (!readback.HasUniquePermissionScope || readback.ObservedStaffSha256 != context.Target.AuthorizedStaffSha256 ||
        expected.Sha256 != observed.Sha256 ||
        !ArchiveCopyPermissionEncoding.IsExactClientStaffReadGrant(readback.Grants, context.Target.StaffGroupId))
      throw ArchiveBlocked("archive-copy-target-permission-preflight-failed", authorization: true);
  }

  private async Task<ArchiveFilePath> EnsureArchivePathAsync(
    ArchiveTargetContext context, string relativePath, CancellationToken ct)
  {
    var segments = DecodeArchiveRelativePath(relativePath);
    var parentId = context.RootItemId;
    for (var index = 0; index < segments.Length - 1; index++)
      parentId = (await EnsureArchiveFolderAsync(context, parentId, segments[index], ct)).Id;
    var parentPath = context.LibraryRootServerRelativeUrl.TrimEnd('/');
    if (segments.Length > 1)
      parentPath = (parentPath == "/" ? "" : parentPath) + "/" + string.Join('/', segments[..^1]);
    if (parentPath.Length == 0) parentPath = "/";
    return new ArchiveFilePath(parentId, segments[^1], parentPath);
  }

  private async Task<ArchiveDriveItem> EnsureArchiveFolderAsync(
    ArchiveTargetContext context, string parentId, string name, CancellationToken ct)
  {
    var existing = await FindChildAsync(context, parentId, name, ct);
    if (existing is not null)
    {
      if (!existing.IsFolder) throw ArchiveBlocked("archive-copy-path-file-collision", authorization: true);
      await RequireInheritedTargetPermissionsAsync(context, existing.Id, ct);
      return existing;
    }
    try
    {
      var created = await CallAsync(HttpMethod.Post,
        $"{ArchiveGraph}/drives/{GraphId(context.DriveId)}/items/{GraphId(parentId)}/children",
        context.GraphToken, new Dictionary<string, object>
        {
          ["name"] = name,
          ["folder"] = new Dictionary<string, object>(),
          ["@microsoft.graph.conflictBehavior"] = "fail"
        }, ct);
      var item = ParseDriveItem(created.Json);
      if (!item.IsFolder || item.Name != name || item.ParentId != parentId || item.ParentDriveId != context.DriveId)
        throw ArchiveBlocked("archive-copy-created-folder-mismatch", authorization: true);
      await RequireInheritedTargetPermissionsAsync(context, item.Id, ct);
      return item;
    }
    catch (Exception exception) when (exception is HttpRequestException or SafeRetryException)
    {
      var reconciled = await FindChildAsync(context, parentId, name, ct);
      if (reconciled is { IsFolder: true } && reconciled.ParentDriveId == context.DriveId && reconciled.ParentId == parentId)
      {
        await RequireInheritedTargetPermissionsAsync(context, reconciled.Id, ct);
        return reconciled;
      }
      throw;
    }
  }

  private async Task<ArchiveDriveItem?> FindChildAsync(
    ArchiveTargetContext context, string parentId, string name, CancellationToken ct)
  {
    var url = $"{ArchiveGraph}/drives/{GraphId(context.DriveId)}/items/{GraphId(parentId)}:/{Uri.EscapeDataString(name)}:?$select=id,name,size,folder,file,parentReference,cTag";
    var reply = await CallAsync(HttpMethod.Get, url, context.GraphToken, null, ct, allowMissing: true);
    if (reply.Missing) return null;
    var item = ParseDriveItem(reply.Json);
    if (item.Name != name || item.ParentId != parentId || item.ParentDriveId != context.DriveId)
      throw ArchiveBlocked("archive-copy-destination-path-mismatch", authorization: true);
    return item;
  }

  private async Task<ArchiveCopyFileReadback> ReadExactDestinationAsync(
    ArchiveTargetContext context, ArchiveDriveItem item, string parentId, string name,
    string relativePath, string expectedSha256, long expectedByteCount, CancellationToken ct)
  {
    if (item.IsFolder || item.Id.Length == 0 || item.Name != name || item.ParentId != parentId ||
        item.ParentDriveId != context.DriveId || item.Size != expectedByteCount ||
        string.IsNullOrWhiteSpace(item.ContentTag))
      throw ArchiveBlocked("archive-copy-existing-item-binding-mismatch", authorization: true);
    await RequireExactStaffReadAsync(context, ct);
    await RequireInheritedArchivePathAsync(context, relativePath, parentId, ct);
    await RequireInheritedTargetPermissionsAsync(context, item.Id, ct);
    var version = await GetCurrentVersionAsync(context.GraphToken, context.DriveId, item.Id, ct);
    if (version.Size != expectedByteCount)
      throw ArchiveBlocked("archive-copy-destination-version-size-mismatch");
    var bytes = await ReadVersionBytesAsync(context.Target.TenantId, context.GraphToken,
      context.DriveId, item.Id, version.Id, expectedByteCount, ct);
    var afterItem = await GetDriveItemAsync(context.GraphToken, context.DriveId, item.Id, ct);
    var afterVersion = await GetCurrentVersionAsync(context.GraphToken, context.DriveId, item.Id, ct);
    var digest = Hashing.Sha256Hex(bytes);
    if (afterItem.Id != item.Id || afterItem.Name != name || afterItem.ParentId != parentId ||
        afterItem.ParentDriveId != context.DriveId || afterItem.Size != expectedByteCount ||
        afterItem.ContentTag != item.ContentTag || afterVersion.Id != version.Id ||
        bytes.LongLength != expectedByteCount || !string.Equals(digest, expectedSha256, StringComparison.Ordinal))
      throw ArchiveBlocked("archive-copy-destination-readback-mismatch");
    await RequireExactStaffReadAsync(context, ct);
    await RequireInheritedArchivePathAsync(context, relativePath, parentId, ct);
    await RequireInheritedTargetPermissionsAsync(context, item.Id, ct);
    return new ArchiveCopyFileReadback(context.Target.TenantId, context.SiteId, context.DriveId,
      context.RootItemId, relativePath,
      item.Id, version.Id, bytes, DateTimeOffset.UtcNow);
  }

  private async Task RequireInheritedArchivePathAsync(
    ArchiveTargetContext context, string relativePath, string expectedParentId, CancellationToken ct)
  {
    var segments = DecodeArchiveRelativePath(relativePath);
    var parentId = context.RootItemId;
    for (var index = 0; index < segments.Length - 1; index++)
    {
      var folder = await FindChildAsync(context, parentId, segments[index], ct);
      if (folder is not { IsFolder: true } || folder.ParentId != parentId || folder.ParentDriveId != context.DriveId)
        throw ArchiveBlocked("archive-copy-descendant-path-changed", authorization: true);
      await RequireInheritedTargetPermissionsAsync(context, folder.Id, ct);
      parentId = folder.Id;
    }
    if (parentId != expectedParentId)
      throw ArchiveBlocked("archive-copy-descendant-path-changed", authorization: true);
  }

  private async Task RequireInheritedTargetPermissionsAsync(
    ArchiveTargetContext context, string itemId, CancellationToken ct)
  {
    var listItem = await GraphGetAsync(
      $"{ArchiveGraph}/drives/{GraphId(context.DriveId)}/items/{GraphId(itemId)}/listItem?$select=id,sharepointIds",
      context.GraphToken, ct);
    if (!listItem.TryGetProperty("sharepointIds", out var ids) ||
        !Guid.TryParse(Text(ids, "listId"), out var listId) ||
        listId != context.LibraryListId ||
        !int.TryParse(Text(ids, "listItemId"), NumberStyles.None, CultureInfo.InvariantCulture, out var listItemId) || listItemId < 1)
      throw ArchiveBlocked("archive-copy-descendant-permission-scope-unavailable", authorization: true);
    var itemUrl = $"{context.SiteUrl}/_api/web/lists(guid'{listId:D}')/items({listItemId})";
    var item = await CallAsync(HttpMethod.Get, itemUrl + "?$select=Id,HasUniqueRoleAssignments",
      context.SharePointToken, null, ct);
    if (!item.Json.TryGetProperty("HasUniqueRoleAssignments", out var uniqueScope) ||
        uniqueScope.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
      throw ArchiveBlocked("archive-copy-descendant-permission-scope-unavailable", authorization: true);
    if (uniqueScope.ValueKind == JsonValueKind.True)
      throw ArchiveBlocked("archive-copy-descendant-permission-scope-unique", authorization: true);
  }

  private async Task CreateEmptyFileAsync(
    ArchiveTargetContext context, string parentPath, string name, CancellationToken ct)
  {
    var digest = await GetSharePointRequestDigestAsync(context, ct);
    var parentLiteral = EncodeResourcePathParameter(parentPath);
    var nameLiteral = EncodeResourcePathParameter(name);
    var url = $"{context.SiteUrl}/_api/web/GetFolderByServerRelativePath(decodedUrl='{parentLiteral}')/Files/AddUsingPath(decodedurl='{nameLiteral}',overwrite=false)";
    await RecheckArchiveCopyAsync(ct);
    using var request = new HttpRequestMessage(HttpMethod.Post, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.SharePointToken);
    request.Headers.Accept.ParseAdd("application/json;odata=nometadata");
    request.Headers.Add("client-request-id", (clientCorrelation.Value ?? Guid.NewGuid()).ToString("D"));
    request.Headers.Add("X-RequestDigest", digest);
    request.Content = new ByteArrayContent([]);
    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    effectProgress.Value!.Dispatched = true;
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
      throw new OperationBlockedException("client-site-consent-or-authority-denied", authorization: true);
    if (!response.IsSuccessStatusCode)
      throw new HttpRequestException("archive-copy-empty-file-create-outcome-unconfirmed");
  }

  private static string[] DecodeArchiveRelativePath(string relativePath)
  {
    if (!IsBounded(relativePath, 2000) || relativePath.StartsWith("/", StringComparison.Ordinal) ||
        relativePath.Contains('\\') || relativePath.Any(char.IsControl))
      throw ArchiveBlocked("archive-copy-manifest-path-invalid", authorization: true);
    var encodedSegments = relativePath.Split('/');
    if (encodedSegments.Length is < 1 or > 32 || encodedSegments.Any(x => x.Length == 0))
      throw ArchiveBlocked("archive-copy-manifest-path-invalid", authorization: true);
    var segments = new string[encodedSegments.Length];
    for (var index = 0; index < encodedSegments.Length; index++)
    {
      var decoded = Uri.UnescapeDataString(encodedSegments[index]);
      if (decoded.Length is 0 or > 255 || decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\') ||
          decoded.Any(c => c is ':' or '*' or '?' or '<' or '>' or '|' or '"') ||
          decoded.Any(char.IsControl) || !string.Equals(Uri.EscapeDataString(decoded), encodedSegments[index], StringComparison.Ordinal))
        throw ArchiveBlocked("archive-copy-manifest-path-invalid", authorization: true);
      segments[index] = decoded;
    }
    return segments;
  }

  private static string EncodeResourcePathParameter(string decodedUrl) =>
    Uri.EscapeDataString(decodedUrl.Replace("'", "''", StringComparison.Ordinal))
      .Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);

  private async Task<string> GetSharePointRequestDigestAsync(ArchiveTargetContext context, CancellationToken ct)
  {
    await RecheckArchiveCopyAsync(ct);
    using var request = new HttpRequestMessage(HttpMethod.Post, context.SiteUrl + "/_api/contextinfo")
    {
      Content = new ByteArrayContent([])
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.SharePointToken);
    request.Headers.Accept.ParseAdd("application/json;odata=nometadata");
    request.Headers.Add("client-request-id", (clientCorrelation.Value ?? Guid.NewGuid()).ToString("D"));
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
      throw new OperationBlockedException("client-site-consent-or-authority-denied", authorization: true);
    if (!response.IsSuccessStatusCode)
      throw new HttpRequestException("archive-copy-request-digest-unavailable");
    await response.Content.LoadIntoBufferAsync(64 * 1024, ct);
    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
    if (bytes.Length is 0 or > 64 * 1024)
      throw ArchiveBlocked("archive-copy-request-digest-invalid", authorization: true);
    using var json = JsonDocument.Parse(bytes);
    var root = json.RootElement;
    if (root.TryGetProperty("d", out var d)) root = d;
    if (root.TryGetProperty("GetContextWebInformation", out var contextInfo)) root = contextInfo;
    var digest = Text(root, "FormDigestValue");
    if (!IsBounded(digest, 4096))
      throw ArchiveBlocked("archive-copy-request-digest-invalid", authorization: true);
    return digest;
  }

  private async Task UploadNewFileAsync(
    ArchiveTargetContext context, string parentId, string name, byte[] content, CancellationToken ct)
  {
    var session = await CallAsync(HttpMethod.Post,
      $"{ArchiveGraph}/drives/{GraphId(context.DriveId)}/items/{GraphId(parentId)}:/{Uri.EscapeDataString(name)}:/createUploadSession",
      context.GraphToken, new Dictionary<string, object>
      {
        ["item"] = new Dictionary<string, object>
        {
          ["@microsoft.graph.conflictBehavior"] = "fail"
        }
      }, ct);
    var uploadUrl = Text(session.Json, "uploadUrl");
    if (!Uri.TryCreate(uploadUrl, UriKind.Absolute, out var capabilityUrl))
      throw ArchiveBlocked("archive-copy-upload-session-invalid", authorization: true);

    for (long start = 0; start < content.LongLength;)
    {
      var length = (int)Math.Min(ArchiveUploadFragmentBytes, content.LongLength - start);
      var end = start + length - 1;
      await RecheckArchiveCopyAsync(ct);
      effectProgress.Value!.Dispatched = true;
      using var chunk = new ByteArrayContent(content.AsSpan(checked((int)start), length).ToArray());
      chunk.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
      chunk.Headers.ContentRange = new ContentRangeHeaderValue(start, end, content.LongLength);
      using var response = await transferTransport.SendAsync(capabilityUrl, HttpMethod.Put, chunk, ct);
      var final = end + 1 == content.LongLength;
      if (final ? response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created) : response.StatusCode != HttpStatusCode.Accepted)
        throw ArchiveBlocked("archive-copy-upload-session-outcome-unconfirmed");
      start = end + 1;
    }
  }

  private async Task<ArchiveDriveItem> GetDriveItemAsync(
    string graphToken, string driveId, string itemId, CancellationToken ct)
  {
    var reply = await GraphGetAsync(
      $"{ArchiveGraph}/drives/{GraphId(driveId)}/items/{GraphId(itemId)}?$select=id,name,size,folder,file,parentReference,cTag",
      graphToken, ct);
    return ParseDriveItem(reply);
  }

  private async Task<ArchiveVersion> GetExactVersionAsync(
    string graphToken, string driveId, string itemId, string versionId, CancellationToken ct)
  {
    var reply = await GraphGetAsync(
      $"{ArchiveGraph}/drives/{GraphId(driveId)}/items/{GraphId(itemId)}/versions/{Uri.EscapeDataString(versionId)}?$select=id,size",
      graphToken, ct);
    var id = Text(reply, "id");
    if (!reply.TryGetProperty("size", out var size) || !size.TryGetInt64(out var byteCount) || byteCount < 0)
      throw ArchiveBlocked("archive-copy-version-metadata-invalid", authorization: true);
    return new ArchiveVersion(id, byteCount);
  }

  private async Task<ArchiveVersion> GetCurrentVersionAsync(
    string graphToken, string driveId, string itemId, CancellationToken ct)
  {
    var reply = await GraphGetAsync(
      $"{ArchiveGraph}/drives/{GraphId(driveId)}/items/{GraphId(itemId)}/versions?$select=id,size&$top=1",
      graphToken, ct);
    if (!reply.TryGetProperty("value", out var versions) || versions.ValueKind != JsonValueKind.Array || versions.GetArrayLength() != 1)
      throw ArchiveBlocked("archive-copy-current-version-unavailable");
    var version = versions[0];
    var id = Text(version, "id");
    if (!version.TryGetProperty("size", out var size) || !size.TryGetInt64(out var byteCount) || byteCount < 0)
      throw ArchiveBlocked("archive-copy-current-version-metadata-invalid", authorization: true);
    return new ArchiveVersion(id, byteCount);
  }

  private async Task<byte[]> ReadVersionBytesAsync(
    string tenantId, string graphToken, string driveId, string itemId, string versionId,
    long expectedByteCount, CancellationToken ct)
  {
    if (!string.Equals(tenantId, options.TenantId, StringComparison.OrdinalIgnoreCase) ||
        expectedByteCount < 0 || expectedByteCount > MaximumArchiveCopyBytes)
      throw ArchiveBlocked("archive-copy-content-size-invalid");
    var url = $"{ArchiveGraph}/drives/{GraphId(driveId)}/items/{GraphId(itemId)}/versions/{Uri.EscapeDataString(versionId)}/content";
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
    request.Headers.Accept.ParseAdd("application/octet-stream");
    request.Headers.Add("client-request-id", (clientCorrelation.Value ?? Guid.NewGuid()).ToString("D"));
    await RecheckArchiveCopyAsync(ct);
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
    {
      var location = response.Headers.Location ?? throw ArchiveBlocked("archive-copy-download-location-missing");
      if (!location.IsAbsoluteUri) location = new Uri(new Uri(url), location);
      await RecheckArchiveCopyAsync(ct);
      using var download = await transferTransport.SendAsync(location, HttpMethod.Get, null, ct);
      if (download.StatusCode != HttpStatusCode.OK)
        throw new HttpRequestException("archive-copy-content-read-unconfirmed");
      return await ReadBoundedContentAsync(download, expectedByteCount, ct);
    }
    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
      throw ArchiveBlocked("archive-copy-content-authority-denied", authorization: true);
    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
      throw new SafeRetryException(response.Headers.RetryAfter?.Delta);
    if (response.StatusCode != HttpStatusCode.OK)
      throw new HttpRequestException("archive-copy-content-read-unconfirmed");
    return await ReadBoundedContentAsync(response, expectedByteCount, ct);
  }

  private static async Task<byte[]> ReadBoundedContentAsync(
    HttpResponseMessage response, long expectedByteCount, CancellationToken ct)
  {
    if (response.Content.Headers.ContentLength is { } length && length != expectedByteCount)
      throw ArchiveBlocked("archive-copy-content-length-mismatch");
    await using var source = await response.Content.ReadAsStreamAsync(ct);
    using var destination = new MemoryStream(expectedByteCount > 0 ? checked((int)Math.Min(expectedByteCount, 4 * 1024 * 1024)) : 0);
    var buffer = new byte[64 * 1024];
    long total = 0;
    while (true)
    {
      var read = await source.ReadAsync(buffer, ct);
      if (read == 0) break;
      total += read;
      if (total > expectedByteCount || total > MaximumArchiveCopyBytes)
        throw ArchiveBlocked("archive-copy-content-exceeds-expected-size");
      destination.Write(buffer, 0, read);
    }
    if (total != expectedByteCount) throw ArchiveBlocked("archive-copy-content-length-mismatch");
    return destination.ToArray();
  }

  private async Task<JsonElement> GraphGetAsync(string url, string token, CancellationToken ct) =>
    (await CallAsync(HttpMethod.Get, url, token, null, ct)).Json;

  private async Task RecheckArchiveCopyAsync(CancellationToken ct)
  {
    var recheck = authorizationGuard.Value ?? throw ArchiveBlocked("client-site-authorization-guard-required", authorization: true);
    await recheck(ct);
  }

  private static ArchiveDriveItem ParseDriveItem(JsonElement item)
  {
    var id = Text(item, "id");
    var name = Text(item, "name");
    var isFolder = item.TryGetProperty("folder", out var folder) && folder.ValueKind == JsonValueKind.Object;
    var hasParent = item.TryGetProperty("parentReference", out var parent) && parent.ValueKind == JsonValueKind.Object;
    var parentId = hasParent ? Text(parent, "id") : null;
    var parentDriveId = hasParent ? Text(parent, "driveId") : null;
    var size = item.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var parsedSize) ? parsedSize : 0;
    return new ArchiveDriveItem(id, name, parentId, parentDriveId, size, isFolder, TextOrNull(item, "cTag"));
  }

  private static string Text(JsonElement json, string name) => TextOrNull(json, name) ?? "";

  private static string? TextOrNull(JsonElement json, string name) =>
    json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
      ? value.GetString() : null;

  private static string GraphId(string value)
  {
    if (!IsBounded(value, 2000)) throw ArchiveBlocked("archive-copy-provider-identifier-invalid", authorization: true);
    return Uri.EscapeDataString(value);
  }

  private bool SiteIdMatchesHost(string? siteId)
  {
    if (string.IsNullOrWhiteSpace(siteId) || siteId.Length > 2000) return false;
    var parts = siteId.Split(',');
    return parts.Length == 3 && string.Equals(parts[0], options.SiteHost, StringComparison.OrdinalIgnoreCase) &&
      Guid.TryParse(parts[1], out _) && Guid.TryParse(parts[2], out _);
  }

  private string ExactSiteUrl(string? value)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length > 2000 ||
        !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
        !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
        !string.Equals(uri.IdnHost, options.SiteHost, StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath == "/")
      throw ArchiveBlocked("archive-copy-provider-site-url-invalid", authorization: true);
    return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
  }

  private bool IsExactProviderSiteUrl(string? value)
  {
    if (string.IsNullOrWhiteSpace(value)) return false;
    try { return SameSiteUrl(ExactSiteUrl(value), value); }
    catch (OperationBlockedException) { return false; }
  }

  private static bool SameSiteUrl(string left, string right) =>
    string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

  private static bool IsBounded(string? value, int maxLength) =>
    value is { Length: > 0 } && value.Length <= maxLength && !value.Any(char.IsControl) && value == value.Trim();

  private static bool IsSha256(string? value) => value is { Length: 64 } &&
    value.All(char.IsAsciiHexDigit) && value == value.ToLowerInvariant();

  private static Guid StableCorrelation(string value)
  {
    var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return new Guid(digest.AsSpan(0, 16));
  }

  private static OperationBlockedException ArchiveBlocked(string code, bool authorization = false) =>
    new(code, authorization);

  private void ValidateSnapshotRequest(ArchiveCopySnapshotReadRequest request)
  {
    if (request is null || request.FirmId == Guid.Empty || request.ClientId == Guid.Empty ||
        request.EngagementId == Guid.Empty || request.SnapshotId == Guid.Empty ||
        !string.Equals(request.TenantId, options.TenantId, StringComparison.OrdinalIgnoreCase) ||
        !SiteIdMatchesHost(request.SiteId) || !IsBounded(request.DriveId, 2000) ||
        !IsBounded(request.ItemId, 2000) || !IsBounded(request.VersionId, 500) ||
        !IsSha256(request.ExpectedSha256) || request.ExpectedByteCount < 0 ||
        request.ExpectedByteCount > MaximumArchiveCopyBytes)
      throw ArchiveBlocked("archive-copy-snapshot-request-invalid", authorization: true);
  }

  private void ValidateFileRequest(ArchiveCopyFileWriteRequest request)
  {
    if (request is null || request.Target is null || request.OperationId == Guid.Empty ||
        !IsSha256(request.SourceKeySha256) || !IsSha256(request.ExpectedSha256) || request.Content is null ||
        request.Content.LongLength > MaximumArchiveCopyBytes ||
        !string.Equals(Hashing.Sha256Hex(request.Content), request.ExpectedSha256, StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(request.RelativePath))
      throw ArchiveBlocked("archive-copy-file-request-invalid");
    _ = DecodeArchiveRelativePath(request.RelativePath);
    ValidateArchiveTarget(request.Target);
  }

  private static Guid StableCorrelation(string value, string namespaceName) =>
    StableCorrelation(namespaceName + ":" + value);
}
