using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>Server-resolved exact registered selected-site version of one PBC upload; never browser input.</summary>
public sealed record PbcRemoteSource(Guid FirmId, Guid ClientId, Guid EngagementId, Guid UploadIntentId,
  Guid RegistrationId, Guid SnapshotId, string DriveId, string ItemId, string VersionId, string Sha256Hex,
  long ByteCount, string BindingSha256Hex);

/// <summary>
/// Verified bytes of one exact registered version, held in a private temporary spool. Disposal closes and
/// removes the spool; the content is never a permanent local copy.
/// </summary>
public sealed class PbcVerifiedContent(Stream content, long length) : IAsyncDisposable
{
  public Stream Content { get; } = content;
  public long Length { get; } = length;
  public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// Reads a registered PBC version from its selected-site binding. Implementations resolve the target only from
/// current local records, fetch the exact version into a bounded private spool and release it only after the
/// complete SHA-256 and byte count match; they never substitute another version, a similarly named file or a
/// local copy.
/// </summary>
public interface IPbcRemoteContentReader
{
  Task<CommandResult<PbcVerifiedContent>> ReadVerifiedAsync(PbcRemoteSource source, CancellationToken ct);
}

public static partial class PbcService
{
  /// <summary>
  /// The read disposition of an upload: no source for the pre-cutover local read, the exact registered remote
  /// source once remote reads are activated, or a failure when that registration no longer matches the upload.
  /// </summary>
  internal static async Task<(PbcRemoteSource? Source, string? Error)> ResolveReadSourceAsync(
    IAuditSphereDbContext db, PbcUploadIntent intent, CancellationToken ct)
  {
    var disposition = await db.PbcStorageDispositions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == intent.FirmId && x.PbcUploadIntentId == intent.Id, ct);
    if (disposition is null || disposition.ReadSource == PbcReadSources.LocalStaged) return (null, null);
    var source = await (from registration in db.PbcDocumentRegistrations.AsNoTracking()
      join snapshot in db.DocumentSnapshots.AsNoTracking()
        on new { registration.FirmId, Id = registration.DocumentSnapshotId } equals new { snapshot.FirmId, snapshot.Id }
      where registration.FirmId == intent.FirmId && registration.Id == disposition.PbcDocumentRegistrationId &&
        registration.PbcUploadIntentId == intent.Id && registration.ClientId == intent.ClientId &&
        registration.EngagementId == intent.EngagementId
      select new PbcRemoteSource(registration.FirmId, registration.ClientId, registration.EngagementId,
        registration.PbcUploadIntentId, registration.Id, snapshot.Id, snapshot.DriveId, snapshot.ItemId,
        snapshot.VersionId, snapshot.Sha256Hex, snapshot.ByteCount, registration.BindingSha256Hex)).SingleOrDefaultAsync(ct);
    return source is not null && intent.State == PbcUploadStates.Received &&
      string.Equals(source.Sha256Hex, intent.FinalSha256Hex, StringComparison.Ordinal) &&
      source.ByteCount == intent.DeclaredByteCount
        ? (source, null)
        : (null, "pbc.source-registration-conflict");
  }
}
