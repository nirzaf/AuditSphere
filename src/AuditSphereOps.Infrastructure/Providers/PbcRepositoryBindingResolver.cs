using System.Text.Json;
using System.Security.Cryptography;
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
    var binding = bindings[0];

    var capability = await db.IntegrationCapabilities.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == scope.FirmId && x.RepositoryBindingId == binding.Id, ct);
    if (capability is null || capability.HealthStatus != "VERIFIED" ||
        capability.TestedAt is null ||
        !HasPermissions(capability.TestedPermissions, BindingDigest(binding),
          "PBC_UPLOAD", "PBC_READBACK"))
      throw Block();

    var clientWorkspaces = await db.ClientWorkspaces.AsNoTracking().Where(x =>
      x.FirmId == scope.FirmId && x.PracticeClientId == scope.ClientId &&
      x.State == ClientWorkspaceStates.Ready && x.TenantId == binding.TenantId &&
      x.SiteId == binding.SiteId && x.DriveId == binding.DriveId &&
      x.RootFolderId == binding.RootFolderId && x.ConnectionRevisionId != null &&
      x.FolderTemplateVersionId != null && x.LastVerifiedAt != null).Take(2).ToListAsync(ct);
    if (clientWorkspaces.Count != 1) throw Block();
    var clientWorkspace = clientWorkspaces[0];
    if (!await db.AcceptanceDecisions.AsNoTracking().AnyAsync(x =>
      x.Id == clientWorkspace.AcceptanceDecisionId && x.FirmId == scope.FirmId &&
      x.PracticeClientId == scope.ClientId && x.Decision == "Accepted", ct))
      throw Block();
    if (!await db.FolderTemplateVersions.AsNoTracking().AnyAsync(x =>
      x.Id == clientWorkspace.FolderTemplateVersionId && x.FirmId == scope.FirmId &&
      x.Purpose == FolderTemplatePurposes.ClientWorkspace && x.ApprovedAt != null, ct))
      throw Block();

    var workspace = await db.FirmWorkspaceConfigurations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == scope.FirmId && x.ConnectionRevisionId == clientWorkspace.ConnectionRevisionId &&
      x.FolderTemplateVersionId == clientWorkspace.FolderTemplateVersionId &&
      x.TenantId == binding.TenantId && x.SiteId == binding.SiteId &&
      x.DriveId == binding.DriveId, ct);
    if (workspace is null || workspace.AccessProfile != Microsoft365AccessProfiles.AppMediated)
      throw Block();
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == workspace.ConnectionRevisionId && x.FirmId == scope.FirmId &&
      x.State == Microsoft365RevisionStates.Active && (x.ConsentState == "VERIFIED" || x.ConsentState == "OBSERVED") &&
      x.TenantId == binding.TenantId, ct);
    if (connection is null || string.IsNullOrWhiteSpace(connection.RuntimeCredentialReference))
      throw Block();
    return new(binding.Id, binding.TenantId, binding.SiteId, binding.DriveId,
      binding.RootFolderId, connection.RuntimeCredentialReference);
  }

  /// <summary>Fingerprint the exact tested target, so changing a binding invalidates its capability row.</summary>
  public static string BindingDigest(RepositoryBinding binding) =>
    Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
      binding.FirmId, binding.ClientId, binding.EngagementId, binding.Id,
      binding.TenantId, binding.SiteId, binding.DriveId, binding.RootFolderId,
      binding.Classification, binding.DesiredAccess, binding.ObservedAccess,
      binding.CapabilityProfile
    }))).ToLowerInvariant();

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
