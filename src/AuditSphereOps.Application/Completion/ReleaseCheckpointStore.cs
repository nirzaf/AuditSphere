using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Completion;

/// <summary>
/// Local, append-only, content-addressed release checkpoint store outside the webroot (§24.4, §25.7).
/// Writes release evidence to disk, re-reads and compares to verify integrity.
/// A second write for the same manifest digest with different bytes conflicts.
/// </summary>
public sealed class LocalAppendOnlyCheckpointStore(string rootDirectory) : IReleaseCheckpointStore
{
  public LocalAppendOnlyCheckpointStore() : this(DefaultRootDirectory) { }

  public static string DefaultRootDirectory =>
    Path.Combine(Path.GetTempPath(), "AuditSphereOps", "release-checkpoints");

  public string RootDirectory { get; } = rootDirectory;

  public async Task<CheckpointReceipt> WriteAsync(string manifestDigest, byte[] manifestBytes, CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(manifestDigest);
    ArgumentNullException.ThrowIfNull(manifestBytes);

    if (manifestDigest.Length != 64 || !IsHexString(manifestDigest))
      throw new ArgumentException("Manifest digest must be a 64-character lowercase hex string.", nameof(manifestDigest));

    var computedDigest = Hashing.Sha256Hex(manifestBytes);
    if (!string.Equals(computedDigest, manifestDigest, StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException("Manifest bytes SHA256 does not match declared digest.", nameof(manifestBytes));

    Directory.CreateDirectory(RootDirectory);
    var targetPath = Path.Combine(RootDirectory, $"{manifestDigest.ToLowerInvariant()}.checkpoint");

    if (File.Exists(targetPath))
    {
      var existingBytes = await File.ReadAllBytesAsync(targetPath, ct);
      var existingDigest = Hashing.Sha256Hex(existingBytes);
      if (!string.Equals(existingDigest, manifestDigest, StringComparison.OrdinalIgnoreCase) ||
          !existingBytes.AsSpan().SequenceEqual(manifestBytes))
      {
        throw new InvalidOperationException("Content-addressed checkpoint conflict: existing bytes do not match.");
      }
      return new CheckpointReceipt(targetPath, existingDigest, existingBytes.LongLength);
    }

    var tempPath = Path.Combine(RootDirectory, $"{manifestDigest.ToLowerInvariant()}.{Guid.NewGuid():N}.tmp");
    try
    {
      await File.WriteAllBytesAsync(tempPath, manifestBytes, ct);
      try
      {
        File.Move(tempPath, targetPath);
      }
      catch (IOException) when (File.Exists(targetPath))
      {
        // Race condition: another writer finished first. Compare bytes.
        var existingBytes = await File.ReadAllBytesAsync(targetPath, ct);
        var existingDigest = Hashing.Sha256Hex(existingBytes);
        if (!string.Equals(existingDigest, manifestDigest, StringComparison.OrdinalIgnoreCase) ||
            !existingBytes.AsSpan().SequenceEqual(manifestBytes))
        {
          throw new InvalidOperationException("Content-addressed checkpoint conflict: existing bytes do not match.");
        }
        return new CheckpointReceipt(targetPath, existingDigest, existingBytes.LongLength);
      }

      // Re-read and verify from disk to guarantee durability and read-back integrity
      var verifiedBytes = await File.ReadAllBytesAsync(targetPath, ct);
      var verifiedDigest = Hashing.Sha256Hex(verifiedBytes);
      if (!string.Equals(verifiedDigest, manifestDigest, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Read-back verification failed for written checkpoint.");

      return new CheckpointReceipt(targetPath, verifiedDigest, verifiedBytes.LongLength);
    }
    finally
    {
      if (File.Exists(tempPath))
      {
        try { File.Delete(tempPath); } catch { }
      }
    }
  }

  public async Task<CheckpointReceipt?> VerifyAsync(string reference, string expectedDigest, CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(reference);
    if (!File.Exists(reference))
      return null;

    var bytes = await File.ReadAllBytesAsync(reference, ct);
    var digest = Hashing.Sha256Hex(bytes);
    return new CheckpointReceipt(reference, digest, bytes.LongLength);
  }

  public async Task<CheckpointReceipt?> ProbeAsync(string manifestDigest, CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(manifestDigest);
    var targetPath = Path.Combine(RootDirectory, $"{manifestDigest.ToLowerInvariant()}.checkpoint");
    if (!File.Exists(targetPath))
      return null;

    var bytes = await File.ReadAllBytesAsync(targetPath, ct);
    var digest = Hashing.Sha256Hex(bytes);
    return new CheckpointReceipt(targetPath, digest, bytes.LongLength);
  }

  private static bool IsHexString(string value) =>
    value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
}
