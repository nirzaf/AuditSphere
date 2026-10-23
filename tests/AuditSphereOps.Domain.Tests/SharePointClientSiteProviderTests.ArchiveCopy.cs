using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class SharePointClientSiteProviderTests
{
  private const string ArchiveLibraryListId = "11111111-2222-3333-4444-555555555555";

  private sealed record ArchiveItem(string Id, string Name, string DriveId, string? ParentId,
    bool IsFolder, byte[] Content, string VersionId, string ContentTag,
    bool HasUniqueRoleAssignments = false, int StaffRoleTypeKind = 2);
  private sealed record UploadSession(string ParentId, string Name);

  private sealed class ArchiveRemote : HttpMessageHandler
  {
    private readonly ArchiveCopyProviderTarget target;
    private readonly Dictionary<string, ArchiveItem> items = new(StringComparer.Ordinal);
    private readonly Dictionary<(string ItemId, string VersionId), byte[]> versions = [];
    private readonly Dictionary<string, int> listItemIds = new(StringComparer.Ordinal);
    private readonly Dictionary<(string ParentId, string Name), string> children = [];
    private readonly Dictionary<string, UploadSession> sessions = new(StringComparer.Ordinal);
    private readonly string staffObjectId;
    private int nextItem = 1;
    private int nextListItemId = 7;
    public bool StaffWrite { get; set; }
    public bool LoseAfterNextUpload { get; set; }
    public int CreatedFileCount { get; private set; }
    public int CreatedFolderCount { get; private set; }
    public int CreatedEmptyFileCount { get; private set; }
    public int SourceVersionContentRequests { get; private set; }
    public bool EmptyFileCreateWasNoOverwrite { get; private set; }
    public bool EmptyFileCreateHadDigest { get; private set; }
    public string? EmptyFileCreatePath { get; private set; }
    public int RootListItemLookups { get; private set; }

    public ArchiveRemote(ArchiveCopyProviderTarget target, byte[] snapshotBytes, string staffObjectId,
      byte[]? currentSourceBytes = null, string currentSourceVersionId = "source-version-7")
    {
      this.target = target;
      this.staffObjectId = staffObjectId;
      Add(new("archive-library-root", "Documents", target.DriveId, null, true, [], "", "", true, 2));
      Add(new("source-library-root", "Source", "source-drive", null, true, [], "", ""));
      Add(new("source-item", "evidence.pdf", "source-drive", "source-library-root", false,
        (currentSourceBytes ?? snapshotBytes).ToArray(), currentSourceVersionId, "source-ctag-7"));
      AddVersion("source-item", "source-version-7", snapshotBytes);
    }

    private void Add(ArchiveItem item)
    {
      items.Add(item.Id, item);
      listItemIds.Add(item.Id, nextListItemId++);
      if (item.ParentId is not null) children[(item.ParentId, item.Name)] = item.Id;
      if (item.VersionId.Length != 0) AddVersion(item.Id, item.VersionId, item.Content);
    }

    public void AddVersion(string itemId, string versionId, byte[] content) =>
      versions[(itemId, versionId)] = content.ToArray();

    public string AddUniqueWriteFolder(string parentId, string name)
    {
      var item = new ArchiveItem("archive-item-" + nextItem++, name, target.DriveId, parentId,
        true, [], "", "", true, 3);
      Add(item);
      return item.Id;
    }

    public string AddUniqueWriteFile(string parentId, string name, byte[] content)
    {
      var item = new ArchiveItem("archive-item-" + nextItem++, name, target.DriveId, parentId,
        false, content.ToArray(), "existing-version-1", "existing-ctag-1", true, 3);
      Add(item);
      return item.Id;
    }

    public int DirectStaffRoleTypeKind(string itemId) => items[itemId].StaffRoleTypeKind;

    public void CompleteUpload(string sessionId, byte[] content)
    {
      var session = sessions[sessionId];
      var item = new ArchiveItem("archive-item-" + nextItem++, session.Name, target.DriveId,
        session.ParentId, false, content.ToArray(), "archive-version-1", "archive-ctag-1");
      Add(item);
      CreatedFileCount++;
    }

    public byte[] ReadVersion(string itemId, string versionId)
    {
      return versions.TryGetValue((itemId, versionId), out var content)
        ? content.ToArray()
        : throw new InvalidOperationException("Unexpected archive test version.");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Assert.NotNull(request.Headers.Authorization);
      var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
      if (request.RequestUri.Host == "graph.microsoft.com")
        return await GraphAsync(request, path, ct);
      return SharePoint(request, path);
    }

    private async Task<HttpResponseMessage> GraphAsync(HttpRequestMessage request, string path, CancellationToken ct)
    {
      if (path.StartsWith("/v1.0/users/", StringComparison.Ordinal))
      {
        var objectId = path.Split('/').Last();
        return Json(new { id = objectId, userPrincipalName = "archive-staff@example.test", accountEnabled = true, userType = "Member" });
      }
      if (path.StartsWith("/v1.0/sites/", StringComparison.Ordinal) && path.EndsWith("/drives", StringComparison.Ordinal))
        return Json(new { value = new[]
        {
          new { id = target.DriveId, driveType = "documentLibrary" },
          new { id = "source-drive", driveType = "documentLibrary" }
        } });
      if (path.StartsWith("/v1.0/sites/", StringComparison.Ordinal))
        return Json(new { id = target.SiteId, webUrl = target.SiteUrl });

      if (path.Contains("/createUploadSession", StringComparison.Ordinal))
      {
        var (parentId, name) = ParseChildPath(path);
        var sessionId = Guid.NewGuid().ToString("N");
        sessions.Add(sessionId, new(parentId, name));
        return Json(new { uploadUrl = $"https://upload.sharepoint.com/sessions/{sessionId}" });
      }
      if (path.EndsWith("/children", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
      {
        var parentId = path.Split("/items/", StringSplitOptions.None)[1].Split("/children", StringSplitOptions.None)[0];
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var name = body.RootElement.GetProperty("name").GetString()!;
        var folder = body.RootElement.TryGetProperty("folder", out _);
        var item = new ArchiveItem("archive-item-" + nextItem++, name, target.DriveId, parentId,
          folder, [], "", "archive-ctag-" + nextItem);
        Add(item);
        if (folder) CreatedFolderCount++;
        return Json(ToGraph(item), HttpStatusCode.Created);
      }
      if (path.StartsWith("/v1.0/drives/", StringComparison.Ordinal))
        return await GraphDriveAsync(request, path, ct);
      throw new InvalidOperationException("Unmapped archive-copy Graph endpoint: " + path);
    }

    private async Task<HttpResponseMessage> GraphDriveAsync(HttpRequestMessage request, string path, CancellationToken ct)
    {
      var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
      var driveId = segments[2];
      if (segments.Length == 3)
      {
        var siteParts = target.SiteId.Split(',');
        return Json(new
        {
          id = driveId,
          driveType = "documentLibrary",
          sharepointIds = new { listId = ArchiveLibraryListId, siteId = siteParts[1], webId = siteParts[2] }
        });
      }
      if (segments.Length == 4 && segments[3] == "root")
        return Json(ToGraph(items[driveId == target.DriveId ? target.LibraryRootItemId : "source-library-root"]));

      var versionsIndex = Array.IndexOf(segments, "versions");
      if (versionsIndex >= 0)
      {
        var itemId = segments[versionsIndex - 1];
        if (versionsIndex == segments.Length - 1)
        {
          var item = items[itemId];
          return Json(new { value = new[] { new { id = item.VersionId, size = item.Content.LongLength } } });
        }
        var versionId = segments[versionsIndex + 1];
        if (versionsIndex + 2 == segments.Length && segments[^1] != "content")
        {
          return versions.TryGetValue((itemId, versionId), out var versionBytes)
            ? Json(new { id = versionId, size = versionBytes.LongLength })
            : Json(new { }, HttpStatusCode.NotFound);
        }
        if (segments[^1] == "content")
        {
          SourceVersionContentRequests++;
          var redirect = new HttpResponseMessage(HttpStatusCode.Found)
          {
            RequestMessage = request
          };
          redirect.Headers.Location = new Uri($"https://download.sharepoint.com/archive-copy/{Uri.EscapeDataString(itemId)}/{Uri.EscapeDataString(versionId)}");
          return redirect;
        }
      }

      if (path.Contains("/items/", StringComparison.Ordinal) && path.Contains(":/", StringComparison.Ordinal))
      {
        if (request.Method == HttpMethod.Get)
        {
          var (parentId, name) = ParseChildPath(path);
          return children.TryGetValue((parentId, name), out var childId)
            ? Json(ToGraph(items[childId])) : Json(new { }, HttpStatusCode.NotFound);
        }
      }
      if (path.EndsWith("/listItem/fields", StringComparison.Ordinal))
        return Json(new { Title = target.OwnershipMarker });
      if (path.EndsWith("/listItem", StringComparison.Ordinal))
      {
        var itemId = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[4];
        if (itemId == target.LibraryRootItemId) RootListItemLookups++;
        return Json(new
        {
          id = "list-item-" + itemId,
          sharepointIds = new
          {
            listId = ArchiveLibraryListId,
            listItemId = listItemIds[itemId].ToString(System.Globalization.CultureInfo.InvariantCulture)
          }
        });
      }
      if (path.EndsWith("/root", StringComparison.Ordinal))
        return Json(ToGraph(items[driveId == target.DriveId ? target.LibraryRootItemId : "source-library-root"]));
      if (path.Contains("/items/", StringComparison.Ordinal))
      {
        var itemId = segments[4];
        return items.TryGetValue(itemId, out var item) ? Json(ToGraph(item)) : Json(new { }, HttpStatusCode.NotFound);
      }
      throw new InvalidOperationException("Unmapped archive-copy drive endpoint: " + path);
    }

    private HttpResponseMessage SharePoint(HttpRequestMessage request, string path)
    {
      if (path.EndsWith("/_api/contextinfo", StringComparison.Ordinal))
        return Json(new { FormDigestValue = "synthetic-request-digest" });
      if (path.EndsWith("/RootFolder", StringComparison.Ordinal))
        return Json(new { ServerRelativeUrl = "/sites/shared-archive/Shared Documents" });
      if (path.Contains("/lists(guid'", StringComparison.Ordinal) && path.EndsWith("/roleassignments", StringComparison.Ordinal) &&
          !path.Contains("/items(", StringComparison.Ordinal))
        return Json(new { value = new[] { new
        {
          Member = new { Id = int.Parse(target.StaffGroupId), PrincipalType = 8 },
          RoleDefinitionBindings = new[] { new { RoleTypeKind = StaffWrite ? 3 : 2 } }
        } } });
      if (path.EndsWith("/roleassignments", StringComparison.Ordinal))
      {
        var itemId = ParseListItemId(path);
        var item = items.Values.Single(candidate => listItemIds[candidate.Id] == itemId);
        var role = item.StaffRoleTypeKind;
        return Json(new { value = new[] { new
        {
          Member = new { Id = int.Parse(target.StaffGroupId), PrincipalType = 8 },
          RoleDefinitionBindings = new[] { new { RoleTypeKind = role } }
        } } });
      }
      if (path.Contains("/sitegroups/getbyid(", StringComparison.Ordinal) && path.EndsWith("/users", StringComparison.Ordinal))
        return Json(new { value = new[] { new { Id = 101 } } });
      if (path.Contains("/sitegroups/getbyid(", StringComparison.Ordinal))
      {
        var id = int.Parse(path.Split("getbyid(", StringSplitOptions.None)[1].Split(')')[0]);
        return Json(new { Id = id, Title = $"AuditSphere Archive Staff {target.ClientId:N}", Description = target.OwnershipMarker });
      }
      if (path.Contains("/siteusers/getbyid(", StringComparison.Ordinal))
        return Json(new { Id = 101, AadObjectId = new { NameId = staffObjectId } });
      if (path.Contains("/lists(guid'", StringComparison.Ordinal) && path.Contains("/items(", StringComparison.Ordinal))
      {
        var itemId = ParseListItemId(path);
        var item = items.Values.Single(candidate => listItemIds[candidate.Id] == itemId);
        return Json(new
        {
          Id = itemId,
          HasUniqueRoleAssignments = item.HasUniqueRoleAssignments,
          FileRef = ServerRelativePath(item),
          FileSystemObjectType = item.IsFolder ? 1 : 0
        });
      }
      if (path.Contains("/lists(guid'", StringComparison.Ordinal))
        return Json(new { Id = ArchiveLibraryListId, Description = target.OwnershipMarker, HasUniqueRoleAssignments = true });
      if (path.Contains("/GetFolderByServerRelativePath(decodedUrl='") &&
          path.Contains("/Files/AddUsingPath(decodedurl='") )
      {
        const string folderPrefix = "GetFolderByServerRelativePath(decodedUrl='";
        var folderStart = path.IndexOf(folderPrefix, StringComparison.Ordinal) + folderPrefix.Length;
        var folderEnd = path.IndexOf("')/Files/AddUsingPath(decodedurl='", folderStart, StringComparison.Ordinal);
        var parentPath = path[folderStart..folderEnd].Replace("''", "'", StringComparison.Ordinal);
        var parent = items.Values.Single(candidate => candidate.IsFolder &&
          string.Equals(ServerRelativePath(candidate), parentPath, StringComparison.Ordinal));
        const string namePrefix = "/Files/AddUsingPath(decodedurl='";
        var nameStart = path.IndexOf(namePrefix, folderEnd, StringComparison.Ordinal) + namePrefix.Length;
        var nameEnd = path.IndexOf("',overwrite=false)", nameStart, StringComparison.Ordinal);
        var name = path[nameStart..nameEnd].Replace("''", "'", StringComparison.Ordinal);
        var body = request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        EmptyFileCreateWasNoOverwrite = path.EndsWith(",overwrite=false)", StringComparison.Ordinal);
        EmptyFileCreateHadDigest = request.Headers.Contains("X-RequestDigest");
        EmptyFileCreatePath = path;
        if (body.Length != 0 || !EmptyFileCreateWasNoOverwrite)
          throw new InvalidOperationException("Zero-byte ResourcePath create must use the empty body and overwrite=false.");
        if (children.ContainsKey((parent.Id, name))) return new(HttpStatusCode.Conflict);
        var created = new ArchiveItem("archive-item-" + nextItem++, name, target.DriveId, parent.Id,
          false, [], "archive-empty-version-1", "archive-empty-ctag-1");
        Add(created);
        CreatedFileCount++;
        CreatedEmptyFileCount++;
        return Json(ToGraph(created), HttpStatusCode.Created);
      }
      if (path.Contains("/items(", StringComparison.Ordinal))
      {
        var itemId = ParseListItemId(path);
        var item = items.Values.Single(candidate => listItemIds[candidate.Id] == itemId);
        return Json(new { Id = itemId, HasUniqueRoleAssignments = item.HasUniqueRoleAssignments });
      }
      throw new InvalidOperationException("Unmapped archive-copy SharePoint endpoint: " + path);
    }

    private static int ParseListItemId(string path)
    {
      var start = path.IndexOf("/items(", StringComparison.Ordinal) + "/items(".Length;
      var end = path.IndexOf(')', start);
      return int.Parse(path.AsSpan(start, end - start), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string ServerRelativePath(ArchiveItem item) => item.ParentId is null
      ? "/sites/shared-archive/Shared Documents"
      : ServerRelativePath(items[item.ParentId]) + "/" + item.Name;

    private static (string ParentId, string Name) ParseChildPath(string path)
    {
      var remainder = path.Split("/items/", StringSplitOptions.None)[1];
      var delimiter = remainder.IndexOf(":/", StringComparison.Ordinal);
      var parentId = remainder[..delimiter];
      var name = remainder[(delimiter + 2)..];
      var nextDelimiter = name.IndexOf(":/", StringComparison.Ordinal);
      if (nextDelimiter >= 0) name = name[..nextDelimiter];
      return (parentId, name.TrimEnd(':'));
    }

    private object ToGraph(ArchiveItem item) => new
    {
      id = item.Id, name = item.Name, size = item.Content.LongLength,
      folder = item.IsFolder ? new { } : null,
      file = item.IsFolder ? null : new { },
      parentReference = item.ParentId is null ? null : new { id = item.ParentId, driveId = item.DriveId },
      cTag = item.ContentTag
    };
  }

  private sealed class ArchiveTransfer(ArchiveRemote remote) : HttpMessageHandler
  {
    public int RequestsWithAuthorization { get; private set; }
    public int DownloadRequests { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      if (request.Headers.Authorization is not null) RequestsWithAuthorization++;
      var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
      if (request.Method == HttpMethod.Put)
      {
        var sessionId = segments[^1];
        var content = await request.Content!.ReadAsByteArrayAsync(ct);
        remote.CompleteUpload(sessionId, content);
        if (remote.LoseAfterNextUpload)
        {
          remote.LoseAfterNextUpload = false;
          throw new HttpRequestException("Synthetic upload response loss after remote acceptance.");
        }
        return new(HttpStatusCode.Created);
      }
      if (request.Method == HttpMethod.Get && segments.Length >= 2)
      {
        DownloadRequests++;
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(remote.ReadVersion(segments[^2], segments[^1])) };
      }
      throw new InvalidOperationException("Unmapped preauthenticated archive-copy request.");
    }
  }

  [Trait("CompletionUnit", "WU-023")]
  [Fact]
  public async Task ArchiveCopyProvider_ReadsExactVersionAndCopiesIntoClientOwnedSeparateLibrary()
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var tenantId = Tenant;
    var siteWebId = Guid.NewGuid();
    var siteCollectionId = Guid.NewGuid();
    var siteId = $"{Host},{siteCollectionId:D},{siteWebId:D}";
    var siteUrl = $"https://{Host}/sites/shared-archive";
    var libraryRootId = "archive-library-root";
    var marker = $"AuditSphere:{firmId:D}:{clientId:D}";
    const string staffGroupId = "42";
    var staffObjectId = Guid.NewGuid().ToString("D");
    var expectedPermissions = ArchiveCopyPermissionEncoding.Encode(
      ArchiveCopyPermissionEncoding.ClientStaffRead(staffGroupId));
    var target = new ArchiveCopyProviderTarget(firmId, clientId, null, Guid.NewGuid(), Guid.NewGuid(), 1,
      new string('a', 64), marker, tenantId, siteId, "archive-drive", libraryRootId, staffGroupId,
      ArchiveCopyPermissionEncoding.StaffDigest([staffObjectId]), expectedPermissions.Sha256,
      siteUrl, libraryRootId);
    var snapshotBytes = Encoding.UTF8.GetBytes("exact archived source version bytes");
    byte[] currentSourceBytes = [.. snapshotBytes, .. Encoding.UTF8.GetBytes(" changed")];
    var remote = new ArchiveRemote(target, snapshotBytes, staffObjectId,
      currentSourceBytes, "source-version-8");
    var transfer = new ArchiveTransfer(remote);
    using var graphAndSharePointHttp = new HttpClient(remote);
    using var tokenHttp = new HttpClient(new TokenHandler(readerId));
    using var transferTransport = new GraphPreauthenticatedTransport(transfer);
    using var provider = new SharePointClientSiteProvider(graphAndSharePointHttp, tokenHttp, options,
      new GraphCapabilityTokenSource(tokenHttp, new(true, Tenant, readerId, options.CertificatePath,
        options.PrivateKeyPath, "User.Read.All")), transferTransport);

    var sourceGuardCalls = 0;
    var snapshotRequest = new ArchiveCopySnapshotReadRequest(firmId, clientId, Guid.NewGuid(), Guid.NewGuid(),
      tenantId, siteId, "source-drive", "source-item", "source-version-7",
      Hashing.Sha256Hex(snapshotBytes), snapshotBytes.LongLength, _ =>
      {
        sourceGuardCalls++;
        return Task.CompletedTask;
      });
    var snapshot = await provider.ReadExactSnapshotAsync(snapshotRequest, CancellationToken.None);
    Assert.Equal("source-version-7", snapshot.VersionId);
    Assert.Equal(snapshotBytes, snapshot.Content);
    Assert.Equal(3, sourceGuardCalls); // operation entry, Graph content request, preauthenticated redirect

    var sourceRequestsBeforeDirectGuard = remote.SourceVersionContentRequests;
    var downloadsBeforeDirectGuard = transfer.DownloadRequests;
    var directGuardCalls = 0;
    var directGuardDenied = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      provider.ReadExactSnapshotAsync(snapshotRequest with
      {
        RecheckAuthorization = _ =>
        {
          directGuardCalls++;
          return directGuardCalls == 2
            ? Task.FromException(new OperationBlockedException("archive-copy-test-guard-denied", authorization: true))
            : Task.CompletedTask;
        }
      }, CancellationToken.None));
    Assert.Equal("archive-copy-test-guard-denied", directGuardDenied.Code);
    Assert.Equal(sourceRequestsBeforeDirectGuard, remote.SourceVersionContentRequests);
    Assert.Equal(downloadsBeforeDirectGuard, transfer.DownloadRequests);

    var sourceRequestsBeforeRedirectGuard = remote.SourceVersionContentRequests;
    var downloadsBeforeRedirectGuard = transfer.DownloadRequests;
    var redirectGuardCalls = 0;
    var redirectGuardDenied = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      provider.ReadExactSnapshotAsync(snapshotRequest with
      {
        RecheckAuthorization = _ =>
        {
          redirectGuardCalls++;
          return redirectGuardCalls == 3
            ? Task.FromException(new OperationBlockedException("archive-copy-test-guard-denied", authorization: true))
            : Task.CompletedTask;
        }
      }, CancellationToken.None));
    Assert.Equal("archive-copy-test-guard-denied", redirectGuardDenied.Code);
    Assert.Equal(sourceRequestsBeforeRedirectGuard + 1, remote.SourceVersionContentRequests);
    Assert.Equal(downloadsBeforeRedirectGuard, transfer.DownloadRequests);

    var wrongVersionBytes = Enumerable.Repeat((byte)'x', snapshotBytes.Length).ToArray();
    remote.AddVersion("source-item", "source-version-wrong", wrongVersionBytes);
    var wrongVersion = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      provider.ReadExactSnapshotAsync(new(firmId, clientId, Guid.NewGuid(), Guid.NewGuid(),
        tenantId, siteId, "source-drive", "source-item", "source-version-wrong",
        Hashing.Sha256Hex(snapshotBytes), snapshotBytes.LongLength, _ => Task.CompletedTask), CancellationToken.None));
    Assert.Equal("archive-copy-source-bytes-mismatch", wrongVersion.Code);

    var permissions = await provider.ReadTargetPermissionsAsync(new(target, _ => Task.CompletedTask), CancellationToken.None);
    Assert.True(permissions.HasUniquePermissionScope);
    Assert.Equal("READ", Assert.Single(permissions.Grants).Role);
    Assert.Equal(target.AuthorizedStaffSha256, permissions.ObservedStaffSha256);

    var content = Encoding.UTF8.GetBytes("immutable bundle part bytes");
    remote.LoseAfterNextUpload = true;
    var fileRequest = new ArchiveCopyFileWriteRequest(target, Guid.NewGuid(),
      Hashing.Sha256Hex(Encoding.UTF8.GetBytes("source-key")), "bundle/part.pdf",
      Hashing.Sha256Hex(content), content, _ => Task.CompletedTask);
    var firstWrite = await provider.CreateOrReadExactFileAsync(fileRequest, CancellationToken.None);
    var retryWrite = await provider.CreateOrReadExactFileAsync(fileRequest, CancellationToken.None);
    Assert.Equal(content, firstWrite.Content);
    Assert.Equal(firstWrite.DestinationItemId, retryWrite.DestinationItemId);
    Assert.Equal(firstWrite.DestinationVersion, retryWrite.DestinationVersion);
    Assert.Equal(1, remote.CreatedFileCount);
    Assert.Equal(1, remote.CreatedFolderCount);
    Assert.Equal(0, transfer.RequestsWithAuthorization);

    var emptyBytes = Array.Empty<byte>();
    var emptyFile = await provider.CreateOrReadExactFileAsync(fileRequest with
    {
      SourceKeySha256 = Hashing.Sha256Hex(Encoding.UTF8.GetBytes("zero-byte-manifest-entry")),
      RelativePath = "bundle/zero%25%23%27byte.pdf",
      ExpectedSha256 = Hashing.Sha256Hex(emptyBytes),
      Content = emptyBytes
    }, CancellationToken.None);
    Assert.Empty(emptyFile.Content);
    Assert.Equal(Hashing.Sha256Hex(emptyBytes), Hashing.Sha256Hex(emptyFile.Content));
    Assert.Equal(1, remote.CreatedEmptyFileCount);
    Assert.True(remote.EmptyFileCreateWasNoOverwrite);
    Assert.True(remote.EmptyFileCreateHadDigest);
    Assert.Contains("GetFolderByServerRelativePath(decodedUrl=", remote.EmptyFileCreatePath, StringComparison.Ordinal);
    Assert.Contains("/Files/AddUsingPath(decodedurl='zero%#''byte.pdf',overwrite=false)",
      remote.EmptyFileCreatePath, StringComparison.Ordinal);

    var uniqueWriteFolderId = remote.AddUniqueWriteFolder(libraryRootId, "restricted-child");
    Assert.Equal(3, remote.DirectStaffRoleTypeKind(uniqueWriteFolderId));
    var beforeUniqueFolderAttempt = remote.CreatedFileCount;
    var uniqueFolderWrite = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      provider.CreateOrReadExactFileAsync(fileRequest with
      {
        RelativePath = "restricted-child/denied.pdf",
        SourceKeySha256 = Hashing.Sha256Hex(Encoding.UTF8.GetBytes("restricted-child-write"))
      }, CancellationToken.None));
    Assert.Equal("archive-copy-descendant-permission-scope-unique", uniqueFolderWrite.Code);
    Assert.Equal(beforeUniqueFolderAttempt, remote.CreatedFileCount);

    var uniqueWriteFileId = remote.AddUniqueWriteFile(libraryRootId, "matching-existing.pdf", content);
    Assert.Equal(3, remote.DirectStaffRoleTypeKind(uniqueWriteFileId));
    var beforeUniqueFileReadback = remote.CreatedFileCount;
    var uniqueFileWrite = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      provider.CreateOrReadExactFileAsync(fileRequest with
      {
        RelativePath = "matching-existing.pdf",
        SourceKeySha256 = Hashing.Sha256Hex(Encoding.UTF8.GetBytes("matching-existing-write"))
      }, CancellationToken.None));
    Assert.Equal("archive-copy-descendant-permission-scope-unique", uniqueFileWrite.Code);
    Assert.Equal(beforeUniqueFileReadback, remote.CreatedFileCount);

    var crossClientId = Guid.NewGuid();
    var crossClient = target with
    {
      ClientId = crossClientId,
      TargetRevisionId = Guid.NewGuid(),
      OwnershipMarker = $"AuditSphere:{firmId:D}:{crossClientId:D}"
    };
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      provider.ReadTargetPermissionsAsync(new(crossClient, _ => Task.CompletedTask), CancellationToken.None));

    remote.StaffWrite = true;
    var beforeStaffWriteAttempt = remote.CreatedFileCount;
    var writeGrant = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      provider.CreateOrReadExactFileAsync(fileRequest with
      {
        RelativePath = "blocked/write.pdf",
        SourceKeySha256 = Hashing.Sha256Hex(Encoding.UTF8.GetBytes("staff-write-denial"))
      }, CancellationToken.None));
    Assert.Equal("archive-copy-target-permission-preflight-failed", writeGrant.Code);
    Assert.Equal(beforeStaffWriteAttempt, remote.CreatedFileCount);
    Assert.Equal(0, remote.RootListItemLookups);
  }
}
