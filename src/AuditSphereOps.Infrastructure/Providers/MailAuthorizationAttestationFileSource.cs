using AuditSphereOps.Application.Microsoft365;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>Paths are deployment configuration only; they are never accepted from a request or signed payload.</summary>
public sealed record MailAuthorizationAttestationFileSourceOptions(
  string CanonicalPayloadPath,
  string DetachedCmsPath)
{
  public void Validate()
  {
    if (!Path.IsPathFullyQualified(CanonicalPayloadPath) || !Path.IsPathFullyQualified(DetachedCmsPath) ||
        string.Equals(Path.GetFullPath(CanonicalPayloadPath), Path.GetFullPath(DetachedCmsPath),
          OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
        !HasBoundedFile(CanonicalPayloadPath, MailAuthorizationAttestationCanonicalizer.MaximumPayloadBytes) ||
        !HasBoundedFile(DetachedCmsPath, MailAuthorizationAttestationCanonicalizer.MaximumCmsBytes))
      throw new InvalidOperationException("Mail authorization evidence file configuration is invalid.");
  }

  private static bool HasBoundedFile(string path, int maximumBytes)
  {
    try
    {
      var file = new FileInfo(path);
      return file.Exists && file.Length is > 0 && file.Length <= maximumBytes;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
      return false;
    }
  }
}

/// <summary>Reads bounded public signed evidence without caching or writing it.</summary>
public sealed class MailAuthorizationAttestationFileSource(
  MailAuthorizationAttestationFileSourceOptions options) : IMailAuthorizationAttestationSource
{
  public async Task<MailAuthorizationAttestationBundle> ReadAsync(CancellationToken ct)
  {
    try
    {
      options.Validate();
      var payload = await ReadBoundedAsync(options.CanonicalPayloadPath,
        MailAuthorizationAttestationCanonicalizer.MaximumPayloadBytes, ct);
      var cms = await ReadBoundedAsync(options.DetachedCmsPath,
        MailAuthorizationAttestationCanonicalizer.MaximumCmsBytes, ct);
      if (payload.Length < 2 || cms.Length == 0)
        throw new InvalidDataException();
      return new MailAuthorizationAttestationBundle(payload, cms);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
        InvalidOperationException or NotSupportedException)
    {
      throw new IOException("mail-authorization-evidence-source-unavailable");
    }
  }

  private static async Task<byte[]> ReadBoundedAsync(string path, int maximumBytes, CancellationToken ct)
  {
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
      bufferSize: 81920, useAsync: true);
    if (stream.Length is < 1 || stream.Length > maximumBytes)
      throw new InvalidDataException();
    var bytes = new byte[checked((int)stream.Length)];
    await stream.ReadExactlyAsync(bytes, ct);
    if (stream.ReadByte() != -1)
      throw new InvalidDataException();
    return bytes;
  }
}
