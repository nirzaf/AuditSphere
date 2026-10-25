using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>The exact stored provider target for one trusted PBC operation scope.</summary>
public sealed record PbcRepositoryTarget(
  Guid RepositoryBindingId, string TenantId, string SiteId, string DriveId,
  string RootFolderId, string RuntimeCredentialReference);

/// <summary>
/// Resolves a PBC target only from current, matching database records. This is a local
/// readiness fence, not proof that Graph will accept a write or preserve an exact version.
/// </summary>
public sealed class PbcRepositoryBindingResolver(IAuditSphereDbContextFactory factory)
{
  public async Task<PbcRepositoryTarget> ResolveAsync(PbcTransferScope scope, CancellationToken ct)
  {
    if (scope.FirmId == Guid.Empty || scope.ClientId == Guid.Empty ||
        scope.EngagementId == Guid.Empty || scope.UploadIntentId == Guid.Empty)
      throw Block();
    await using var db = await factory.CreateAsync(ct);
    var intent = await db.PbcUploadIntents.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == scope.UploadIntentId && x.FirmId == scope.FirmId &&
      x.ClientId == scope.ClientId && x.EngagementId == scope.EngagementId, ct);
    if (intent is null || intent.State is not (PbcUploadStates.Staged or PbcUploadStates.Received) ||
        !await db.PbcRequests.AsNoTracking().AnyAsync(x =>
          x.Id == intent.PbcRequestId && x.FirmId == scope.FirmId &&
          x.ClientId == scope.ClientId && x.EngagementId == scope.EngagementId &&
          (x.State == PbcStates.PartiallyReceived || x.State == PbcStates.Received), ct))
      throw Block();

    var bindings = await db.RepositoryBindings.AsNoTracking().Where(x =>
      x.FirmId == scope.FirmId && x.ClientId == scope.ClientId &&
      x.EngagementId == scope.EngagementId && x.Classification == "working" &&
      x.DesiredAccess == "read-write" && x.ObservedAccess == "read-write" &&
      x.CapabilityProfile == "selected-site").Take(2).ToListAsync(ct);
    if (bindings.Count != 1) throw Block();
    return await ResolveBindingTargetAsync(db, scope.FirmId, scope.ClientId, bindings[0], ct, "PBC_UPLOAD", "PBC_READBACK");
  }

  /// <summary>
  /// Resolves the read target of a registered PBC version. Read eligibility is separate from transfer eligibility:
  /// accepted and closed sources stay readable, but only through the exact binding the registration pinned, with
  /// its current verified read-back capability, workspace, connection and dedicated-site evidence. Read access
  /// never implies write access or reactivates an upload.
  /// </summary>
  public async Task<PbcRepositoryTarget> ResolveReadAsync(PbcRemoteSource source, CancellationToken ct)
  {
    if (source is null || source.FirmId == Guid.Empty || source.ClientId == Guid.Empty ||
        source.EngagementId == Guid.Empty || source.RegistrationId == Guid.Empty)
      throw Block();
    await using var db = await factory.CreateAsync(ct);
    var registration = await db.PbcDocumentRegistrations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == source.FirmId && x.Id == source.RegistrationId && x.ClientId == source.ClientId &&
      x.EngagementId == source.EngagementId && x.PbcUploadIntentId == source.UploadIntentId &&
      x.DocumentSnapshotId == source.SnapshotId, ct);
    if (registration is null || registration.BindingSha256Hex != source.BindingSha256Hex) throw Block();
    var reference = await db.DocumentReferences.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == source.FirmId && x.Id == registration.DocumentReferenceId &&
      x.ClientId == source.ClientId && x.EngagementId == source.EngagementId, ct);
    var binding = reference is null ? null : await db.RepositoryBindings.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == source.FirmId && x.Id == reference.RepositoryBindingId &&
      x.ClientId == source.ClientId && x.EngagementId == source.EngagementId, ct);
    // A re-pointed or re-tested binding no longer matches the digest the registration pinned.
    if (binding is null || BindingDigest(binding) != registration.BindingSha256Hex || binding.DriveId != source.DriveId ||
        reference!.ItemId != source.ItemId)
      throw Block();
    return await ResolveBindingTargetAsync(db, source.FirmId, source.ClientId, binding, ct, "PBC_READBACK");
  }

  /// <summary>
  /// Resolves the read target of a not-yet-registered backfill candidate: the engagement's single working selected-site
  /// binding, unchanged since the candidate's digest was computed, on the same drive, with no registration yet.
  /// </summary>
  public async Task<PbcRepositoryTarget> ResolveUnregisteredReadAsync(PbcRemoteSource candidate, CancellationToken ct)
  {
    if (candidate is null || candidate.FirmId == Guid.Empty || candidate.ClientId == Guid.Empty ||
        candidate.EngagementId == Guid.Empty || candidate.UploadIntentId == Guid.Empty || candidate.RegistrationId != Guid.Empty)
      throw Block();
    await using var db = await factory.CreateAsync(ct);
    if (await db.PbcDocumentRegistrations.AsNoTracking().AnyAsync(x =>
          x.FirmId == candidate.FirmId && x.PbcUploadIntentId == candidate.UploadIntentId, ct))
      throw Block();
    var bindings = await db.RepositoryBindings.AsNoTracking().Where(x =>
      x.FirmId == candidate.FirmId && x.ClientId == candidate.ClientId && x.EngagementId == candidate.EngagementId &&
      x.Classification == "working" && x.CapabilityProfile == "selected-site").Take(2).ToListAsync(ct);
    if (bindings.Count != 1 || bindings[0].DriveId != candidate.DriveId ||
        BindingDigest(bindings[0]) != candidate.BindingSha256Hex)
      throw Block();
    return await ResolveBindingTargetAsync(db, candidate.FirmId, candidate.ClientId, bindings[0], ct, "PBC_READBACK");
  }

  private static async Task<PbcRepositoryTarget> ResolveBindingTargetAsync(IAuditSphereDbContext db, Guid firmId,
    Guid clientId, RepositoryBinding binding, CancellationToken ct, params string[] operations)
  {
    var capability = await db.IntegrationCapabilities.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.RepositoryBindingId == binding.Id, ct);
    if (capability is null || capability.HealthStatus != "VERIFIED" ||
        capability.TestedAt is null ||
        !HasPermissions(capability.TestedPermissions, BindingDigest(binding), operations))
      throw Block();

    var clientWorkspaces = await db.ClientWorkspaces.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.PracticeClientId == clientId &&
      x.State == ClientWorkspaceStates.Ready && x.TenantId == binding.TenantId &&
      // The binding root is the engagement's PBC folder inside this client's workspace; the tested
      // binding fingerprint (not a root-folder equality) pins that exact folder.
      x.SiteId == binding.SiteId && x.DriveId == binding.DriveId &&
      x.RemoteItemId != null && x.ConnectionRevisionId != null &&
      x.FolderTemplateVersionId != null && x.LastVerifiedAt != null).Take(2).ToListAsync(ct);
    if (clientWorkspaces.Count != 1) throw Block();
    var clientWorkspace = clientWorkspaces[0];
    if (!await db.AcceptanceDecisions.AsNoTracking().AnyAsync(x =>
      x.Id == clientWorkspace.AcceptanceDecisionId && x.FirmId == firmId &&
      x.PracticeClientId == clientId && x.Decision == "Accepted", ct))
      throw Block();
    if (!await db.FolderTemplateVersions.AsNoTracking().AnyAsync(x =>
      x.Id == clientWorkspace.FolderTemplateVersionId && x.FirmId == firmId &&
      x.Purpose == FolderTemplatePurposes.ClientWorkspace && x.ApprovedAt != null, ct))
      throw Block();

    var workspace = await db.FirmWorkspaceConfigurations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.ConnectionRevisionId == clientWorkspace.ConnectionRevisionId &&
      x.FolderTemplateVersionId == clientWorkspace.FolderTemplateVersionId &&
      x.TenantId == binding.TenantId, ct);
    if (workspace is null || workspace.AccessProfile != Microsoft365AccessProfiles.AppMediated)
      throw Block();
    var clientSite = await db.ClientSharePointSites.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId, ct);
    if (clientSite != null)
    {
      if (clientSite.State != "READY" || clientSite.VerifiedAt == null || clientSite.ConnectionRevisionId != workspace.ConnectionRevisionId ||
          clientSite.TenantId != binding.TenantId || clientSite.SiteId != binding.SiteId || clientSite.DriveId != binding.DriveId)
        throw Block();
    }
    else
    {
      var cutover = await db.FirmWorkspaceConfigurations.AsNoTracking().Where(x => x.FirmId == firmId).MinAsync(x => x.ClientSitesRequiredFrom, ct);
      if (cutover != null && await db.PracticeClients.AnyAsync(x => x.FirmId == firmId && x.Id == clientId && x.CreatedAt >= cutover, ct)) throw Block();
      if (workspace.SiteId != binding.SiteId || workspace.DriveId != binding.DriveId) throw Block();
    }
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == workspace.ConnectionRevisionId && x.FirmId == firmId &&
      x.State == Microsoft365RevisionStates.Active && (x.ConsentState == "VERIFIED" || x.ConsentState == "OBSERVED") &&
      x.TenantId == binding.TenantId, ct);
    if (connection is null || string.IsNullOrWhiteSpace(connection.RuntimeCredentialReference))
      throw Block();
    return new(binding.Id, binding.TenantId, binding.SiteId, binding.DriveId,
      binding.RootFolderId, connection.RuntimeCredentialReference);
  }

  /// <summary>Fingerprint the exact tested target, so changing a binding invalidates its capability row.</summary>
  public static string BindingDigest(RepositoryBinding binding) => RepositoryBindingDigest.Compute(binding);

  private static bool HasPermissions(string value, string expectedDigest, params string[] required)
  {
    try
    {
      using var json = JsonDocument.Parse(value);
      if (json.RootElement.ValueKind != JsonValueKind.Object ||
          !json.RootElement.TryGetProperty("bindingSha256", out var digest) ||
          digest.ValueKind != JsonValueKind.String ||
          !string.Equals(digest.GetString(), expectedDigest, StringComparison.Ordinal) ||
          !json.RootElement.TryGetProperty("operations", out var operations) ||
          operations.ValueKind != JsonValueKind.Array) return false;
      var permissions = operations.EnumerateArray()
        .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())
        .ToHashSet(StringComparer.Ordinal);
      return required.All(permissions.Contains);
    }
    catch (JsonException) { return false; }
  }

  private static OperationBlockedException Block() =>
    new("pbc-repository-binding-unverified", authorization: true);
}
