using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>An exact selected-site drive plus the runtime credential slot allowed to reach it.</summary>
public sealed record SelectedSiteLocation(string TenantId, string SiteId, string DriveId, string CredentialReference);

public sealed record RemoteFolder(string ItemId, string Name);

/// <summary>Disposable upload/read-back/delete test result. Nothing from it is kept in SharePoint.</summary>
public sealed record RemoteCapabilityTest(bool UploadSucceeded, bool ReadbackMatched, bool CleanupSucceeded,
  string DiagnosticCode, string? CorrelationId = null)
{
  public bool Passed => UploadSucceeded && ReadbackMatched && CleanupSucceeded;
}

/// <summary>
/// Selected-site folder provider (Infrastructure owns Graph). Every call names an exact drive and parent
/// item stored by AuditSphere; names are never taken from browser input without sanitizing.
/// </summary>
public interface ISelectedSiteWorkspaceProvisioner
{
  bool IsConfigured { get; }

  /// <summary>Returns the existing child folder with this exact name, or creates it. Idempotent.</summary>
  Task<RemoteFolder> EnsureFolderAsync(SelectedSiteLocation location, string parentItemId, string name, CancellationToken ct);

  /// <summary>Uploads a small probe file, reads it back by content hash, then deletes it.</summary>
  Task<RemoteCapabilityTest> TestReadWriteAsync(SelectedSiteLocation location, string folderItemId, CancellationToken ct);
}

public sealed record WorkspaceProvisioningResult(Guid WorkspaceOrBindingId, string State, string Message, string? DiagnosticCode = null);

/// <summary>
/// Provisions the client workspace and the engagement PBC repository binding inside the approved
/// selected site (P2). The binding's capability row is VERIFIED only by a real disposable upload and
/// exact read-back; that row is what the PBC transfer resolver requires before any document upload.
/// </summary>
public static class PbcRepositoryProvisioningService
{
  public const string PbcNodeKey = "pbc";
  public const string EngagementsNodeKey = "engagements";

  public static async Task<CommandResult<WorkspaceProvisioningResult>> ProvisionClientWorkspaceAsync(
    IAuditSphereDbContext db, ActorContext actor, ISelectedSiteWorkspaceProvisioner provider,
    Guid practiceClientId, DateTimeOffset now, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<WorkspaceProvisioningResult>();
    if (!provider.IsConfigured)
      return Fail(ErrorCodes.GateBlocked, "The selected-site credential is not configured on this server.");
    var workspace = await db.ClientWorkspaces.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.PracticeClientId == practiceClientId && x.Purpose == "PRIMARY", ct);
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == practiceClientId, ct);
    if (workspace is null || client is null)
      return Fail(ErrorCodes.GateBlocked, "Only an accepted client has a workspace intent to provision.");
    if (!await db.AcceptanceDecisions.AsNoTracking().AnyAsync(x => x.Id == workspace.AcceptanceDecisionId &&
          x.FirmId == actor.FirmId && x.PracticeClientId == practiceClientId && x.Decision == "Accepted", ct))
      return Fail(ErrorCodes.GateBlocked, "The client acceptance decision is not Accepted.");
    var active = await ActiveSiteAsync(db, actor.FirmId, ct);
    if (!active.Succeeded) return Fail(active.ErrorCode!, active.Message!);
    var (config, connection, template) = active.Value!;
    if (workspace.State == ClientWorkspaceStates.Ready && workspace.RemoteItemId is not null &&
        workspace.ConnectionRevisionId == connection.Id && workspace.DriveId == config.DriveId)
      return CommandResult<WorkspaceProvisioningResult>.Ok(new(workspace.Id, workspace.State, "The client workspace is already provisioned."));

    var location = new SelectedSiteLocation(config.TenantId, config.SiteId, config.DriveId, connection.RuntimeCredentialReference);
    RemoteFolder clientFolder;
    try
    {
      clientFolder = await provider.EnsureFolderAsync(location, config.RootFolderId,
        FolderName(client.CommercialName ?? client.LegalName, client.Id), ct);
      await EnsureTreeAsync(provider, location, clientFolder.ItemId, template.ManifestJson, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      workspace.State = ClientWorkspaceStates.BlockedConfiguration;
      workspace.LastErrorCode = ProviderCode(ex);
      workspace.Revision++;
      TenantAdministration.AddEvent(db, actor, "CLIENT_WORKSPACE_PROVISION_FAILED", now, oldState: ClientWorkspaceStates.WaitingForIntegration,
        newState: workspace.State, reason: "Administrator provisioned client workspace", result: workspace.LastErrorCode);
      await db.SaveChangesAsync(ct);
      return Fail(ErrorCodes.GateBlocked, "SharePoint did not accept the client workspace. Check the selected-site grant and try again.");
    }

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<WorkspaceProvisioningResult>();
    var previous = workspace.State;
    workspace.State = ClientWorkspaceStates.Ready;
    workspace.ConnectionRevisionId = connection.Id;
    workspace.FolderTemplateVersionId = template.Id;
    workspace.TenantId = config.TenantId;
    workspace.SiteId = config.SiteId;
    workspace.DriveId = config.DriveId;
    workspace.RootFolderId = config.RootFolderId;
    workspace.RemoteItemId = clientFolder.ItemId;
    workspace.LastErrorCode = null;
    workspace.LastVerifiedAt = now;
    workspace.Revision++;
    TenantAdministration.AddEvent(db, actor, "CLIENT_WORKSPACE_PROVISIONED", now, oldState: previous, newState: workspace.State,
      reason: "Administrator provisioned client workspace", result: "READY");
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<WorkspaceProvisioningResult>.Ok(new(workspace.Id, workspace.State,
      "Client workspace created in the approved SharePoint site."));
  }

  public static async Task<CommandResult<WorkspaceProvisioningResult>> ProvisionEngagementRepositoryAsync(
    IAuditSphereDbContext db, ActorContext actor, ISelectedSiteWorkspaceProvisioner provider,
    Guid engagementId, DateTimeOffset now, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<WorkspaceProvisioningResult>();
    if (!provider.IsConfigured)
      return Fail(ErrorCodes.GateBlocked, "The selected-site credential is not configured on this server.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return TenantAdministration.Denied<WorkspaceProvisioningResult>();
    var workspace = await db.ClientWorkspaces.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.PracticeClientId == engagement.PracticeClientId && x.Purpose == "PRIMARY", ct);
    if (workspace is null || workspace.State != ClientWorkspaceStates.Ready || workspace.RemoteItemId is null)
      return Fail(ErrorCodes.GateBlocked, "Provision the client workspace before the engagement repository.");
    var active = await ActiveSiteAsync(db, actor.FirmId, ct);
    if (!active.Succeeded) return Fail(active.ErrorCode!, active.Message!);
    var (config, connection, clientTemplate) = active.Value!;
    if (workspace.ConnectionRevisionId != connection.Id || workspace.DriveId != config.DriveId)
      return Fail(ErrorCodes.GateBlocked, "The client workspace belongs to a previous connection; provision it again first.");
    var engagementTemplate = await db.FolderTemplateVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Purpose == FolderTemplatePurposes.EngagementWorkspace && x.ApprovedAt != null)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (engagementTemplate is null || FindNode(engagementTemplate.ManifestJson, PbcNodeKey) is null)
      return Fail(ErrorCodes.GateBlocked, "Approve an engagement folder template that contains the PBC intake folder first.");
    var engagementsNode = FindNode(clientTemplate.ManifestJson, EngagementsNodeKey);
    if (engagementsNode is null)
      return Fail(ErrorCodes.GateBlocked, "The approved client template has no Engagements folder.");

    var location = new SelectedSiteLocation(config.TenantId, config.SiteId, config.DriveId, connection.RuntimeCredentialReference);
    RemoteFolder pbcFolder;
    RemoteCapabilityTest test;
    try
    {
      var engagements = await provider.EnsureFolderAsync(location, workspace.RemoteItemId, engagementsNode, ct);
      var label = string.Join(" ", new[] { engagement.PeriodEnd, engagement.ServiceRoute }.Where(x => !string.IsNullOrWhiteSpace(x)));
      var engagementFolder = await provider.EnsureFolderAsync(location, engagements.ItemId,
        FolderName(string.IsNullOrWhiteSpace(label) ? "Engagement" : label, engagement.Id), ct);
      var created = await EnsureTreeAsync(provider, location, engagementFolder.ItemId, engagementTemplate.ManifestJson, ct);
      pbcFolder = created[PbcNodeKey];
      test = await provider.TestReadWriteAsync(location, pbcFolder.ItemId, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return Fail(ErrorCodes.GateBlocked, $"SharePoint did not accept the engagement repository ({ProviderCode(ex)}).");
    }

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<WorkspaceProvisioningResult>();
    var existing = await db.RepositoryBindings.Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
      x.EngagementId == engagement.Id && x.Classification == "working" && x.CapabilityProfile == "selected-site").ToListAsync(ct);
    if (existing.Any(x => x.DriveId != config.DriveId || x.RootFolderId != pbcFolder.ItemId))
      return Fail("pbc.binding-conflict", "A different working repository binding already exists for this engagement; review it before provisioning.");
    var binding = existing.SingleOrDefault();
    var observed = test.Passed ? "read-write" : "none";
    if (binding is null)
    {
      binding = new RepositoryBinding
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId, EngagementId = engagement.Id,
        TenantId = config.TenantId, SiteId = config.SiteId, DriveId = config.DriveId, RootFolderId = pbcFolder.ItemId,
        Classification = "working", DesiredAccess = "read-write", ObservedAccess = observed,
        CapabilityProfile = "selected-site", CreatedAt = now
      };
      db.RepositoryBindings.Add(binding);
    }
    else binding.ObservedAccess = observed;

    var capability = await db.IntegrationCapabilities.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.RepositoryBindingId == binding.Id, ct);
    capability ??= db.IntegrationCapabilities.Add(new IntegrationCapability
      { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, RepositoryBindingId = binding.Id }).Entity;
    capability.HealthStatus = test.Passed ? "VERIFIED" : "FAILED";
    capability.TestedAt = now;
    capability.TestedPermissions = JsonSerializer.Serialize(new
    {
      bindingSha256 = RepositoryBindingDigest.Compute(binding),
      operations = test.Passed ? new[] { "PBC_UPLOAD", "PBC_READBACK" } : [],
      diagnostic = test.DiagnosticCode,
      correlationId = test.CorrelationId
    });
    TenantAdministration.AddEvent(db, actor, test.Passed ? "PBC_REPOSITORY_VERIFIED" : "PBC_REPOSITORY_TEST_FAILED", now,
      oldState: "-", newState: capability.HealthStatus, reason: "Administrator provisioned engagement PBC repository",
      result: test.DiagnosticCode, correlationId: test.CorrelationId);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return test.Passed
      ? CommandResult<WorkspaceProvisioningResult>.Ok(new(binding.Id, "VERIFIED",
          "Engagement PBC repository created and verified with a disposable upload and exact read-back."))
      : CommandResult<WorkspaceProvisioningResult>.Ok(new(binding.Id, "FAILED",
          "The engagement folders exist, but the upload/read-back test failed; PBC uploads stay blocked.", test.DiagnosticCode));
  }

  private static async Task<CommandResult<(FirmWorkspaceConfiguration Config, Microsoft365ConnectionRevision Connection, FolderTemplateVersion Template)>>
    ActiveSiteAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct)
  {
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.State == Microsoft365RevisionStates.Active && x.ConsentState == "VERIFIED")
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var config = connection is null ? null : await db.FirmWorkspaceConfigurations.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ConnectionRevisionId == connection.Id && x.TenantId == connection.TenantId)
      .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    var template = config is null ? null : await db.FolderTemplateVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Id == config.FolderTemplateVersionId && x.Purpose == FolderTemplatePurposes.ClientWorkspace && x.ApprovedAt != null, ct);
    if (connection is null || config is null || template is null || string.IsNullOrWhiteSpace(connection.RuntimeCredentialReference) ||
        config.AccessProfile != Microsoft365AccessProfiles.AppMediated)
      return CommandResult<(FirmWorkspaceConfiguration, Microsoft365ConnectionRevision, FolderTemplateVersion)>.Fail(ErrorCodes.GateBlocked,
        "An active, consent-verified working-site connection with an approved client template is required.");
    return CommandResult<(FirmWorkspaceConfiguration, Microsoft365ConnectionRevision, FolderTemplateVersion)>.Ok((config, connection, template));
  }

  /// <summary>Creates every template node under the parent; returns created folders by node key.</summary>
  private static async Task<Dictionary<string, RemoteFolder>> EnsureTreeAsync(ISelectedSiteWorkspaceProvisioner provider,
    SelectedSiteLocation location, string parentItemId, string manifestJson, CancellationToken ct)
  {
    var created = new Dictionary<string, RemoteFolder>(StringComparer.Ordinal);
    using var manifest = JsonDocument.Parse(manifestJson);
    async Task WalkAsync(JsonElement nodes, string parent)
    {
      foreach (var node in nodes.EnumerateArray())
      {
        var name = node.GetProperty("name").GetString()!;
        var folder = await provider.EnsureFolderAsync(location, parent, name, ct);
        if (node.TryGetProperty("key", out var key) && key.GetString() is { } k) created[k] = folder;
        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
          await WalkAsync(children, folder.ItemId);
      }
    }
    if (manifest.RootElement.TryGetProperty("nodes", out var roots) && roots.ValueKind == JsonValueKind.Array)
      await WalkAsync(roots, parentItemId);
    return created;
  }

  internal static string? FindNode(string manifestJson, string key)
  {
    using var manifest = JsonDocument.Parse(manifestJson);
    string? Find(JsonElement nodes)
    {
      foreach (var node in nodes.EnumerateArray())
      {
        if (node.TryGetProperty("key", out var k) && k.GetString() == key) return node.GetProperty("name").GetString();
        if (node.TryGetProperty("children", out var c) && c.ValueKind == JsonValueKind.Array && Find(c) is { } found) return found;
      }
      return null;
    }
    return manifest.RootElement.TryGetProperty("nodes", out var roots) ? Find(roots) : null;
  }

  /// <summary>SharePoint-safe, stable folder name: display text plus a short immutable ID suffix.</summary>
  public static string FolderName(string display, Guid id)
  {
    var invalid = new[] { '"', '*', ':', '<', '>', '?', '/', '\\', '|', '#', '%', '~', '&', '{', '}' };
    var clean = new string(display.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
    clean = string.Join(" ", clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
    if (clean.Length > 80) clean = clean[..80].TrimEnd('.', ' ');
    return $"{(clean.Length == 0 ? "Folder" : clean)} ({id.ToString("N")[..8]})";
  }

  private static string ProviderCode(Exception ex) => ex is OperationBlockedException blocked ? blocked.Code : "provider-request-failed";

  private static CommandResult<WorkspaceProvisioningResult> Fail(string code, string message) =>
    CommandResult<WorkspaceProvisioningResult>.Fail(code, message);
}

/// <summary>Fingerprint of the exact tested target; any change to the binding invalidates its capability row.</summary>
public static class RepositoryBindingDigest
{
  public static string Compute(RepositoryBinding binding) =>
    Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
      binding.FirmId, binding.ClientId, binding.EngagementId, binding.Id,
      binding.TenantId, binding.SiteId, binding.DriveId, binding.RootFolderId,
      binding.Classification, binding.DesiredAccess, binding.ObservedAccess,
      binding.CapabilityProfile
    }))).ToLowerInvariant();
}

public sealed record EngagementRepositoryRow(Guid EngagementId, string Label, string BindingState, DateTimeOffset? TestedAt);

public sealed record ClientWorkspaceRow(Guid ClientId, string ClientName, string WorkspaceState, string? LastErrorCode,
  DateTimeOffset? LastVerifiedAt, IReadOnlyList<EngagementRepositoryRow> Engagements);

/// <summary>Administrator view of client workspaces and engagement PBC repositories (identifiers only, no links).</summary>
public static class PbcRepositoryProvisioningQuery
{
  public static async Task<CommandResult<IReadOnlyList<ClientWorkspaceRow>>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<IReadOnlyList<ClientWorkspaceRow>>();
    var workspaces = await db.ClientWorkspaces.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Purpose == "PRIMARY").ToListAsync(ct);
    var clientIds = workspaces.Select(x => x.PracticeClientId).ToArray();
    var clients = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.CommercialName ?? x.LegalName, ct);
    var engagements = await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.PracticeClientId)).ToListAsync(ct);
    var bindings = await db.RepositoryBindings.AsNoTracking().Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.ClientId) &&
      x.Classification == "working" && x.CapabilityProfile == "selected-site").ToListAsync(ct);
    var bindingIds = bindings.Select(x => x.Id).ToArray();
    var capabilities = await db.IntegrationCapabilities.AsNoTracking().Where(x => x.FirmId == actor.FirmId && bindingIds.Contains(x.RepositoryBindingId))
      .ToDictionaryAsync(x => x.RepositoryBindingId, ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<IReadOnlyList<ClientWorkspaceRow>>();
    return CommandResult<IReadOnlyList<ClientWorkspaceRow>>.Ok(workspaces.OrderBy(x => clients.GetValueOrDefault(x.PracticeClientId)).Select(w =>
      new ClientWorkspaceRow(w.PracticeClientId, clients.GetValueOrDefault(w.PracticeClientId, "Client"), w.State, w.LastErrorCode, w.LastVerifiedAt,
        engagements.Where(e => e.PracticeClientId == w.PracticeClientId).OrderBy(e => e.CreatedAt).Select(e =>
        {
          var binding = bindings.FirstOrDefault(b => b.EngagementId == e.Id);
          var capability = binding is null ? null : capabilities.GetValueOrDefault(binding.Id);
          var label = string.Join(" ", new[] { e.ServiceRoute, e.PeriodEnd }.Where(x => !string.IsNullOrWhiteSpace(x)));
          return new EngagementRepositoryRow(e.Id, string.IsNullOrWhiteSpace(label) ? $"Engagement {e.Id.ToString()[..8]}" : label,
            binding is null ? "NOT_PROVISIONED" : capability?.HealthStatus ?? "NOT_VERIFIED", capability?.TestedAt);
        }).ToList())).ToList());
  }
}
