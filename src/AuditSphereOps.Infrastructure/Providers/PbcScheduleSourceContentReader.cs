using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// Accepted schedule source bytes by recorded read disposition (SP-04): the exact registered SharePoint version
/// once remote reads are activated, otherwise the pre-cutover staged chunks. There is no fallback between them.
/// Acceptance, scope and source-currentness remain the caller's responsibility.
/// </summary>
public sealed class PbcScheduleSourceContentReader(PbcStagedScheduleContentReader staged, IPbcRemoteContentReader remote)
  : IAuditScheduleSourceContentReader
{
  public async Task<CommandResult<byte[]>> ReadVerifiedAsync(PbcDownload download, string expectedSha256,
    long expectedByteCount, CancellationToken ct)
  {
    if (download?.Remote is not { } source)
      return await staged.ReadVerifiedAsync(download!, expectedSha256, expectedByteCount, ct);
    if (expectedByteCount is < 1 or > AuditScheduleCsvProfile.MaxBytes || download.ByteCount != expectedByteCount ||
        source.ByteCount != expectedByteCount || !string.Equals(source.Sha256Hex, expectedSha256, StringComparison.Ordinal) ||
        download.ChunkPaths.Count != 0)
      return CommandResult<byte[]>.Fail(ErrorCodes.Operations.Invalid, "The source content identity is invalid.");

    var read = await remote.ReadVerifiedAsync(source, ct);
    if (!read.Succeeded) return CommandResult<byte[]>.Fail(read.ErrorCode!, read.Message!);
    await using var content = read.Value!;
    if (content.Length != expectedByteCount) return Unavailable();
    var bytes = new byte[checked((int)expectedByteCount)];
    var offset = 0;
    while (offset < bytes.Length)
    {
      var count = await content.Content.ReadAsync(bytes.AsMemory(offset), ct);
      if (count == 0) return Unavailable();
      offset += count;
    }
    if (await content.Content.ReadAsync(new byte[1], ct) != 0 ||
        Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedSha256)
      return Unavailable();
    return CommandResult<byte[]>.Ok(bytes);
  }

  private static CommandResult<byte[]> Unavailable() =>
    CommandResult<byte[]>.Fail(ErrorCodes.GateBlocked, "The verified source content is unavailable.");
}
