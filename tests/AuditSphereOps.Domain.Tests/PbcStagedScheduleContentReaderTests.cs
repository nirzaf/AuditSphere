using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

public sealed class PbcStagedScheduleContentReaderTests
{
  [Fact]
  public async Task VerifiedChunksReturnTheExactCombinedBytesAndPropagateCancellation()
  {
    var root = Path.Combine(Path.GetTempPath(), "schedule-reader-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var first = Path.Combine(root, "first");
      var second = Path.Combine(root, "second");
      var bytes = Encoding.UTF8.GetBytes("AccountCode,SignedAmount\r\n001100,100.123456\r\n");
      await File.WriteAllBytesAsync(first, bytes[..17]);
      await File.WriteAllBytesAsync(second, bytes[17..]);
      var download = new PbcDownload("listing.csv", "text/csv", bytes.Length, [first, second]);
      var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
      var reader = new PbcStagedScheduleContentReader(root + Path.DirectorySeparatorChar);
      var result = await reader.ReadVerifiedAsync(download, hash, bytes.Length, CancellationToken.None);
      Assert.True(result.Succeeded, result.ErrorCode);
      Assert.Equal(bytes, result.Value);
      using var cancelled = new CancellationTokenSource();
      cancelled.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        reader.ReadVerifiedAsync(download, hash, bytes.Length, cancelled.Token));
    }
    finally { Directory.Delete(root, true); }
  }

  [Theory]
  [InlineData("hash")]
  [InlineData("uppercase-hash")]
  [InlineData("count")]
  [InlineData("outside")]
  [InlineData("prefix-sibling")]
  [InlineData("relative")]
  [InlineData("missing")]
  [InlineData("nul")]
  [InlineData("oversize-declared")]
  [InlineData("oversize-file")]
  [InlineData("truncated")]
  [InlineData("changed")]
  [InlineData("directory")]
  [InlineData("file-symlink")]
  [InlineData("directory-symlink")]
  public async Task UnverifiableOrUncontainedContentFailsWithoutExposingPaths(string scenario)
  {
    var sandbox = Path.Combine(Path.GetTempPath(), "schedule-reader-" + Guid.NewGuid().ToString("N"));
    var root = Path.Combine(sandbox, "root");
    Directory.CreateDirectory(root);
    try
    {
      var bytes = Encoding.UTF8.GetBytes("001100,100.123456\n");
      var path = Path.Combine(root, "chunk");
      await File.WriteAllBytesAsync(path, bytes);
      var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
      long count = bytes.Length;
      switch (scenario)
      {
        case "hash": hash = new string('0', 64); break;
        case "uppercase-hash": hash = new string('A', 64); break;
        case "count": count++; break;
        case "outside": path = Path.Combine(sandbox, "outside"); await File.WriteAllBytesAsync(path, bytes); break;
        case "prefix-sibling":
          Directory.CreateDirectory(root + "-other");
          path = Path.Combine(root + "-other", "chunk");
          await File.WriteAllBytesAsync(path, bytes); break;
        case "relative": path = "chunk"; break;
        case "missing": File.Delete(path); break;
        case "nul": path += "\0"; break;
        case "oversize-declared": count = AuditScheduleCsvProfile.MaxBytes + 1L; break;
        case "oversize-file":
          using (var file = new FileStream(path, FileMode.Open, FileAccess.Write))
            file.SetLength(AuditScheduleCsvProfile.MaxBytes + 1L);
          break;
        case "truncated": await File.WriteAllBytesAsync(path, bytes[..^1]); break;
        case "changed":
          var changed = (byte[])bytes.Clone(); changed[0] = (byte)'9';
          await File.WriteAllBytesAsync(path, changed); break;
        case "directory": path = root; break;
        case "file-symlink":
          var link = Path.Combine(root, "link"); File.CreateSymbolicLink(link, path); path = link; break;
        case "directory-symlink":
          var directory = Path.Combine(root, "link"); Directory.CreateSymbolicLink(directory, root);
          path = Path.Combine(directory, "chunk"); break;
      }
      var result = await new PbcStagedScheduleContentReader(root).ReadVerifiedAsync(
        new PbcDownload("listing.csv", "text/csv", count, [path]), hash, count, CancellationToken.None);
      Assert.False(result.Succeeded);
      Assert.Null(result.Value);
      Assert.Contains(result.ErrorCode, new[] { ErrorCodes.Operations.Invalid, ErrorCodes.GateBlocked });
      Assert.DoesNotContain(sandbox, result.Message ?? "");
      Assert.DoesNotContain("001100", result.Message ?? "");
    }
    finally { Directory.Delete(sandbox, true); }
  }
}
