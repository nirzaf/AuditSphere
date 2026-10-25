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
    var parsed = PbcSelectedSiteIdentity.Parse(identity) ?? throw new OperationBlockedException("provider-receipt-conflict");
    var (target, location) = await TargetAsync(scope, ct);
    if (parsed.DriveId != target.DriveId) throw new OperationBlockedException("provider-receipt-conflict");
    var item = await drive.GetItemAsync(location, parsed.ItemId, ct);
    if (item is null) return null;
    if (item.IsFolder || item.ParentId != target.RootFolderId || item.Name != await FileNameAsync(scope, ct))
      throw new OperationBlockedException("provider-receipt-conflict");
    return await ReadBackAsync(location, target, item.ItemId, parsed.VersionId, await DeclaredBytesAsync(scope, ct), ct);
  }

  /// <summary>
  /// Hashes the exact version: the current one after upload or probe, or the recorded one on verify, using the
  /// drive's version-fenced read so bytes read across a change are never relabelled as the observed version.
  /// </summary>
  private async Task<PbcProviderReceipt> ReadBackAsync(SelectedSiteLocation location, PbcRepositoryTarget target,
    string itemId, string? versionId, long maxBytes, CancellationToken ct)
  {
    var (version, sha, size) = await drive!.ReadExactVersionAsync(location, itemId, versionId, maxBytes, null, ct);
    return new(PbcSelectedSiteIdentity.Format(target.DriveId, itemId, version), sha, size);
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
  public static string StoredName(Guid uploadIntentId, string fileName) =>
    PbcSelectedSiteIdentity.StoredName(uploadIntentId, fileName);
}
