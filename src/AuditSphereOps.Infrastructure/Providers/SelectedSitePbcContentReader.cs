using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// Exact-version PBC reads from the registered selected-site binding (SP-03). The target comes only from the
/// registration's pinned binding and current local readiness records; the bytes stream into a private
/// delete-on-close spool and are released only after the complete SHA-256 and byte count match the registered
/// snapshot. A historical version uses the version-content endpoint; the current version is fenced by equal
/// before/after version and content-tag observations. No other version, file or local copy is ever substituted.
/// </summary>
public sealed class SelectedSitePbcContentReader(PbcRepositoryBindingResolver resolver, GraphSelectedSiteDrive drive,
  string scratchRoot) : IPbcRemoteContentReader, IPbcRegistrationVerifier
{
  /// <summary>Hashes a not-yet-registered exact version (SP-06 backfill) without retaining its bytes.</summary>
  public async Task<CommandResult> VerifyUnregisteredAsync(PbcRemoteSource candidate, CancellationToken ct)
  {
    if (candidate is null || candidate.ByteCount is < 0 or > MaxBytes || !IsSha256(candidate.Sha256Hex) ||
        string.IsNullOrEmpty(candidate.ItemId) || string.IsNullOrEmpty(candidate.VersionId))
      return CommandResult.Fail(ErrorCodes.Operations.Invalid, "The candidate source identity is invalid.");
    try
    {
      var target = await resolver.ResolveUnregisteredReadAsync(candidate, ct);
      if (target.DriveId != candidate.DriveId) return CommandResult.Fail(ErrorCodes.GateBlocked, "The candidate binding is unavailable.");
      var location = new SelectedSiteLocation(target.TenantId, target.SiteId, target.DriveId, target.RuntimeCredentialReference);
      var (version, sha, size) = await drive.ReadExactVersionAsync(location, candidate.ItemId, candidate.VersionId,
        candidate.ByteCount, null, ct);
      return version == candidate.VersionId && size == candidate.ByteCount && string.Equals(sha, candidate.Sha256Hex, StringComparison.Ordinal)
        ? CommandResult.Ok()
        : CommandResult.Fail("pbc.source-integrity", "The SharePoint version does not match the retained transfer receipt.");
    }
    catch (OperationBlockedException blocked) when (blocked.Code == "selected-site-content-too-large")
    {
      return CommandResult.Fail("pbc.source-integrity", "The SharePoint version does not match the retained transfer receipt.");
    }
    catch (Exception error) when (error is OperationBlockedException or SafeRetryException or IOException or
      HttpRequestException or UnauthorizedAccessException || error is TaskCanceledException && !ct.IsCancellationRequested)
    {
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The SharePoint version is temporarily unavailable; retry later.");
    }
  }

  // Matches the PBC upload limit; an exact registered version can never be larger.
  private const long MaxBytes = 250L * 1024 * 1024;

  public async Task<CommandResult<PbcVerifiedContent>> ReadVerifiedAsync(PbcRemoteSource source, CancellationToken ct)
  {
    if (source is null || source.ByteCount is < 0 or > MaxBytes || !IsSha256(source.Sha256Hex) ||
        string.IsNullOrEmpty(source.ItemId) || string.IsNullOrEmpty(source.VersionId))
      return CommandResult<PbcVerifiedContent>.Fail(ErrorCodes.Operations.Invalid, "The registered source identity is invalid.");
    if (!Path.IsPathFullyQualified(scratchRoot)) return Unavailable();
    FileStream? spool = null;
    try
    {
      var target = await resolver.ResolveReadAsync(source, ct);
      if (target.DriveId != source.DriveId) return Unavailable();
      Directory.CreateDirectory(scratchRoot);
      spool = new FileStream(Path.Combine(scratchRoot, $"pbc-read-{Guid.NewGuid():N}.spool"), FileMode.CreateNew,
        FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
      var location = new SelectedSiteLocation(target.TenantId, target.SiteId, target.DriveId, target.RuntimeCredentialReference);
      var (version, sha, size) = await drive.ReadExactVersionAsync(location, source.ItemId, source.VersionId,
        source.ByteCount, spool, ct);
      await spool.FlushAsync(ct);
      if (version != source.VersionId || size != source.ByteCount || spool.Length != size ||
          !string.Equals(sha, source.Sha256Hex, StringComparison.Ordinal))
        return CommandResult<PbcVerifiedContent>.Fail("pbc.source-integrity",
          "The SharePoint version does not match its registered hash and size; no content was released.");
      spool.Position = 0;
      var content = new PbcVerifiedContent(spool, size);
      spool = null;
      return CommandResult<PbcVerifiedContent>.Ok(content);
    }
    catch (OperationBlockedException blocked) when (blocked.Code == "selected-site-content-too-large")
    {
      return CommandResult<PbcVerifiedContent>.Fail("pbc.source-integrity",
        "The SharePoint version does not match its registered hash and size; no content was released.");
    }
    catch (Exception error) when (error is OperationBlockedException or SafeRetryException or IOException or
      HttpRequestException or UnauthorizedAccessException || error is TaskCanceledException && !ct.IsCancellationRequested)
    {
      return Unavailable();
    }
    finally
    {
      if (spool is not null) await spool.DisposeAsync();
    }
  }

  private static CommandResult<PbcVerifiedContent> Unavailable() =>
    CommandResult<PbcVerifiedContent>.Fail(ErrorCodes.GateBlocked,
      "The registered SharePoint version is temporarily unavailable; retry later.");

  private static bool IsSha256(string? value) => value is { Length: 64 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
