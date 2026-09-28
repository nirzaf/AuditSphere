using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// Selected-site SharePoint provider for staged PBC documents (P2). The target is resolved only from
/// the verified repository binding for the trusted operation scope. A receipt is issued only after the
/// exact stored version is read back and its SHA-256 and byte count match the staged plan.
/// The parameterless instance is the fail-closed placeholder used when no live composition is approved.
/// </summary>
public sealed class GraphPbcProviderSink : IPbcProviderSink
{
  private const string Prefix = "graph-drive-item:";
  private readonly PbcRepositoryBindingResolver? resolver;
  private readonly GraphSelectedSiteDrive? drive;
  private readonly IAuditSphereDbContextFactory? factory;

  public GraphPbcProviderSink() { }

  public GraphPbcProviderSink(PbcRepositoryBindingResolver resolver, GraphSelectedSiteDrive drive, IAuditSphereDbContextFactory factory)
  {
    this.resolver = resolver;
    this.drive = drive;
    this.factory = factory;
  }

  private static OperationBlockedException NotApproved() => new("live-provider-not-approved");

  public async Task<PbcProviderReceipt> UploadAsync(PbcTransferPlan plan, CancellationToken ct)
  {
    if (resolver is null || drive is null || factory is null) throw NotApproved();
    var scope = new PbcTransferScope(plan.FirmId, plan.ClientId, plan.EngagementId, plan.UploadIntentId);
    var (target, location) = await TargetAsync(scope, ct);
    var name = await FileNameAsync(scope, ct);
    var item = await drive.UploadAsync(location, target.RootFolderId, name, plan, ct);
    if (item.ParentId != target.RootFolderId || item.Name != name || item.IsFolder)
      throw new OperationBlockedException("provider-receipt-conflict");
    return await ReadBackAsync(location, target, item.ItemId, versionId: null, plan.DeclaredByteCount, ct);
  }

  public async Task<PbcProviderReceipt?> ProbeAsync(PbcTransferScope scope, CancellationToken ct)
  {
    if (resolver is null || drive is null || factory is null) throw NotApproved();
    var (target, location) = await TargetAsync(scope, ct);
    var item = await drive.GetChildAsync(location, target.RootFolderId, await FileNameAsync(scope, ct), ct);
    if (item is null) return null;
    if (item.IsFolder) throw new OperationBlockedException("provider-receipt-conflict");
    return await ReadBackAsync(location, target, item.ItemId, versionId: null, await DeclaredBytesAsync(scope, ct), ct);
  }

  public async Task<PbcProviderReceipt?> VerifyAsync(PbcTransferScope scope, string identity, CancellationToken ct)
  {
    if (resolver is null || drive is null || factory is null) throw NotApproved();
    var parsed = Parse(identity) ?? throw new OperationBlockedException("provider-receipt-conflict");
    var (target, location) = await TargetAsync(scope, ct);
    if (parsed.DriveId != target.DriveId) throw new OperationBlockedException("provider-receipt-conflict");
    var item = await drive.GetItemAsync(location, parsed.ItemId, ct);
    if (item is null) return null;
    if (item.IsFolder || item.ParentId != target.RootFolderId || item.Name != await FileNameAsync(scope, ct))
      throw new OperationBlockedException("provider-receipt-conflict");
    return await ReadBackAsync(location, target, item.ItemId, parsed.VersionId, await DeclaredBytesAsync(scope, ct), ct);
  }

  /// <summary>Hashes the exact version: the current one after upload, or the recorded one on verify.</summary>
  private async Task<PbcProviderReceipt> ReadBackAsync(SelectedSiteLocation location, PbcRepositoryTarget target,
    string itemId, string? versionId, long maxBytes, CancellationToken ct)
  {
    var current = await drive!.CurrentVersionAsync(location, itemId, ct);
    var version = versionId ?? current;
    var (sha, size) = await drive.ContentDigestAsync(location, itemId, version == current ? null : version, maxBytes, ct);
    return new($"{Prefix}{target.DriveId}:{itemId}:{version}", sha, size);
  }

  private async Task<(PbcRepositoryTarget Target, SelectedSiteLocation Location)> TargetAsync(PbcTransferScope scope, CancellationToken ct)
  {
    var target = await resolver!.ResolveAsync(scope, ct);
    return (target, new SelectedSiteLocation(target.TenantId, target.SiteId, target.DriveId, target.RuntimeCredentialReference));
  }

  /// <summary>Deterministic per-intent name, so an unknown outcome is reconciled by exact name.</summary>
  private async Task<string> FileNameAsync(PbcTransferScope scope, CancellationToken ct)
  {
    await using var db = await factory!.CreateAsync(ct);
    var fileName = await db.PbcUploadIntents.AsNoTracking()
      .Where(x => x.Id == scope.UploadIntentId && x.FirmId == scope.FirmId && x.ClientId == scope.ClientId && x.EngagementId == scope.EngagementId)
      .Select(x => x.FileName).SingleOrDefaultAsync(ct) ?? throw new OperationBlockedException("pbc-repository-binding-unverified", authorization: true);
    return StoredName(scope.UploadIntentId, fileName);
  }

  private async Task<long> DeclaredBytesAsync(PbcTransferScope scope, CancellationToken ct)
  {
    await using var db = await factory!.CreateAsync(ct);
    return await db.PbcUploadIntents.AsNoTracking()
      .Where(x => x.Id == scope.UploadIntentId && x.FirmId == scope.FirmId)
      .Select(x => x.DeclaredByteCount).SingleAsync(ct);
  }

  /// <summary>Upload intent ID prefix plus a SharePoint-safe version of the client's file name.</summary>
  public static string StoredName(Guid uploadIntentId, string fileName)
  {
    var invalid = new[] { '"', '*', ':', '<', '>', '?', '/', '\\', '|', '#', '%', '~', '&', '{', '}' };
    var clean = new string(Path.GetFileName(fileName).Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim('.', ' ');
    if (clean.Length > 150) clean = clean[^150..];
    return $"{uploadIntentId:N}-{(clean.Length == 0 ? "document" : clean)}";
  }

  private sealed record ParsedIdentity(string DriveId, string ItemId, string VersionId);

  private static ParsedIdentity? Parse(string identity)
  {
    if (!identity.StartsWith(Prefix, StringComparison.Ordinal)) return null;
    // Drive IDs contain no ':' ("b!…"), item IDs are alphanumeric, version IDs look like "1.0".
    var parts = identity[Prefix.Length..].Split(':');
    return parts.Length == 3 && parts.All(x => x.Length > 0) ? new(parts[0], parts[1], parts[2]) : null;
  }
}
