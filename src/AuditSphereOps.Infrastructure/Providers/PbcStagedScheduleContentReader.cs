using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>Local staging only. Acceptance, scope and source-currentness belong to the caller.</summary>
public sealed class PbcStagedScheduleContentReader(string stagingRoot) : IAuditScheduleSourceContentReader
{
  public async Task<CommandResult<byte[]>> ReadVerifiedAsync(PbcDownload download, string expectedSha256,
    long expectedByteCount, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (download is null || expectedByteCount is < 1 or > AuditScheduleCsvProfile.MaxBytes ||
        download.ByteCount != expectedByteCount || expectedSha256 is null || expectedSha256.Length != 64 ||
        expectedSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
        download.ChunkPaths is null || download.ChunkPaths.Count == 0 ||
        download.ChunkPaths.Count > expectedByteCount)
      return CommandResult<byte[]>.Fail(ErrorCodes.Operations.Invalid, "The source content identity is invalid.");

    try
    {
      if (!Path.IsPathFullyQualified(stagingRoot)) return Unavailable();
      var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
      var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      if (!Directory.Exists(root) || IsLink(new DirectoryInfo(root))) return Unavailable();
      var paths = new string[download.ChunkPaths.Count];
      long total = 0;
      for (var i = 0; i < paths.Length; i++)
      {
        ct.ThrowIfCancellationRequested();
        var path = download.ChunkPaths[i];
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return Unavailable();
        path = Path.GetFullPath(path);
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !IsRegularDescendant(root, path))
          return Unavailable();
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > expectedByteCount - total) return Unavailable();
        total += length;
        paths[i] = path;
      }
      if (total != expectedByteCount) return Unavailable();

      // Allocate only after both the declaration and complete recorded file lengths fit the cap.
      var bytes = new byte[checked((int)expectedByteCount)];
      var offset = 0;
      var extra = new byte[1];
      foreach (var path in paths)
      {
        if (!IsRegularDescendant(root, path)) return Unavailable();
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
          64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (!input.CanSeek || input.Length <= 0 || input.Length > expectedByteCount - offset)
          return Unavailable();
        var length = input.Length;
        long readFromFile = 0;
        while (readFromFile < length)
        {
          ct.ThrowIfCancellationRequested();
          var count = await input.ReadAsync(bytes.AsMemory(offset,
            (int)Math.Min(length - readFromFile, bytes.Length - offset)), ct);
          if (count == 0) return Unavailable();
          offset += count;
          readFromFile += count;
          if (offset > AuditScheduleCsvProfile.MaxBytes || offset > expectedByteCount) return Unavailable();
        }
        if (await input.ReadAsync(extra.AsMemory(), ct) != 0 || input.Length != length ||
            !IsRegularDescendant(root, path)) return Unavailable();
      }
      ct.ThrowIfCancellationRequested();
      if (offset != expectedByteCount || Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedSha256)
        return Unavailable();
      return CommandResult<byte[]>.Ok(bytes);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
      NotSupportedException or System.Security.SecurityException)
    {
      ct.ThrowIfCancellationRequested();
      return Unavailable();
    }
  }

  private static bool IsLink(FileSystemInfo item) =>
    item.LinkTarget is not null || (item.Attributes & FileAttributes.ReparsePoint) != 0;

  private static bool IsRegularDescendant(string root, string path)
  {
    var file = new FileInfo(path);
    if (!file.Exists || IsLink(file) ||
        (file.Attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0 || file.Length <= 0)
      return false;
    var parent = file.Directory;
    while (parent is not null && !string.Equals(parent.FullName, root, StringComparison.Ordinal))
    {
      if (!parent.Exists || IsLink(parent)) return false;
      parent = parent.Parent;
    }
    return parent is not null && parent.Exists && !IsLink(parent);
  }

  private static CommandResult<byte[]> Unavailable() =>
    CommandResult<byte[]>.Fail(ErrorCodes.GateBlocked, "The verified source content is unavailable.");
}
