using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>
/// Automatic folder provisioning when the Partner activates an engagement (live "pbc" worker group). It creates the
/// client folder and the engagement tree from the firm's approved templates, tests the PBC intake folder with a
/// disposable upload and read-back, and records the same binding and capability evidence as the administrator action.
/// Folder creation is get-or-create, so a retry or an uncertain outcome is reconciled by running it again and never
/// produces a second tree.
/// <para>
/// Layout follows the approved client template: with an <c>engagements</c> node the earlier
/// <c>Client (id)/Engagements/period service (id)</c> naming is kept; without one the exact
/// <c>/Client Name/Engagement Year/…</c> tree is created (a client name collision or two engagements of one year get a
/// deterministic disambiguator, never a silent merge).
/// </para>
/// </summary>
public sealed class EngagementWorkspaceProvisioningHandler(
  IAuditSphereDbContextFactory factory, ISelectedSiteWorkspaceProvisioner provider) : IOperationHandler
{
  public const string Kind = "ProvisionEngagementWorkspace.v1";

  public OperationDefinition Definition { get; } = new(Kind, OperationMode.LIVE, OperationAuthority.LIVE_PROVIDER, Group: PbcDocumentTransferHandler.LiveGroup);

  private sealed record RemoteResult(string ClientFolderId, string ClientFolderName, string EngagementFolderId, string PbcFolderId, bool TestPassed, string Diagnostic, string? Correlation,
    string? PlanDigest = null);

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      using var document = JsonDocument.Parse(request.PayloadJson);
      if (!document.RootElement.TryGetProperty("engagementId", out var id) || !id.TryGetGuid(out var value) ||
          value != request.TargetId || request.ExpectedRevision != 1 || request.ClientId is null || request.EngagementId != value)
        throw new OperationBlockedException("invalid-workspace-request");
      return JsonSerializer.Serialize(new { engagementId = value.ToString("D") });
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("invalid-workspace-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    var engagements = await db.Engagements.FromSqlInterpolated($"""
      SELECT * FROM engagements WHERE firm_id = {op.FirmId} AND id = {op.TargetId} AND practice_client_id = {op.ClientId} FOR UPDATE
      """).ToListAsync(ct);
    if (engagements.Count != 1 || !await db.EngagementActivations.AnyAsync(x => x.FirmId == op.FirmId && x.EngagementId == op.TargetId, ct))
      throw new OperationBlockedException("engagement-not-activated", authorization: true);
  }

  public async Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct)
  {
    if (!provider.IsConfigured) throw new SafeRetryException(TimeSpan.FromMinutes(5));
    await using var db = await factory.CreateAsync(ct);
    var plan = await PlanAsync(db, op.FirmId, op.TargetId, ct);
    var location = new SelectedSiteLocation(plan.Config.TenantId, plan.Config.SiteId, plan.Config.DriveId, plan.Connection.RuntimeCredentialReference);

    var clientName = plan.Ste ? SteClientName(plan.Client) : PbcRepositoryProvisioningService.FolderName(plan.Client.CommercialName ?? plan.Client.LegalName, plan.Client.Id);
    var clientFolder = await provider.EnsureFolderAsync(location, plan.Config.RootFolderId, clientName, ct);
    if (plan.Ste && await db.ClientWorkspaces.AsNoTracking().AnyAsync(x => x.TenantId == plan.Config.TenantId && x.PracticeClientId != plan.Client.Id &&
          x.DriveId == plan.Config.DriveId && x.RemoteItemId == clientFolder.ItemId, ct))
    {
      // Another client already owns a folder of this name: use the stable disambiguated name instead of merging clients.
      clientName = PbcRepositoryProvisioningService.FolderName(plan.Client.CommercialName ?? plan.Client.LegalName, plan.Client.Id);
      clientFolder = await provider.EnsureFolderAsync(location, plan.Config.RootFolderId, clientName, ct);
    }
    await PbcRepositoryProvisioningService.EnsureTreeAsync(provider, location, clientFolder.ItemId, plan.ClientTemplate.ManifestJson, ct);

    string parent = clientFolder.ItemId;
    if (!plan.Ste)
    {
      var engagementsNode = PbcRepositoryProvisioningService.FindNode(plan.ClientTemplate.ManifestJson, PbcRepositoryProvisioningService.EngagementsNodeKey)!;
      parent = (await provider.EnsureFolderAsync(location, clientFolder.ItemId, engagementsNode, ct)).ItemId;
    }
    var engagementName = plan.Ste
      ? SteEngagementName(plan.Engagement, plan.Siblings)
      : PbcRepositoryProvisioningService.FolderName(string.Join(" ", new[] { plan.Engagement.PeriodEnd, plan.Engagement.ServiceRoute }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } l ? l : "Engagement", plan.Engagement.Id);
    var engagementFolder = await provider.EnsureFolderAsync(location, parent, engagementName, ct);
    var created = await PbcRepositoryProvisioningService.EnsureTreeAsync(provider, location, engagementFolder.ItemId, plan.EngagementTemplate.ManifestJson, ct);
    var pbc = created[PbcRepositoryProvisioningService.PbcNodeKey];
    var test = await provider.TestReadWriteAsync(location, pbc.ItemId, ct);
    return Encode(new RemoteResult(clientFolder.ItemId, clientName, engagementFolder.ItemId, pbc.ItemId, test.Passed, test.DiagnosticCode, test.CorrelationId, PlanDigest(plan)));
  }

  // Folder creation is get-or-create, so an unknown outcome is reconciled by running the same steps again.
  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) => ExecuteEffectAsync(op, ct);

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op, OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    if (verifiedRemoteResult is null || !string.Equals(Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(verifiedRemoteResult.Identity)), verifiedRemoteResult.Digest, StringComparison.Ordinal))
      throw new OperationBlockedException("provider-result-unverifiable");
    var remote = JsonSerializer.Deserialize<RemoteResult>(verifiedRemoteResult.Identity) ?? throw new OperationBlockedException("provider-result-unverifiable");
    var plan = await PlanAsync(db, op.FirmId, op.TargetId, ct);
    if (!await PbcRepositoryProvisioningService.LockWorkspacePublicationAsync(db, op.FirmId, plan.Config.TenantId, plan.Config.DriveId, ct)) throw new OperationBlockedException("workspace-firm-guard-unavailable");
    plan = await PlanAsync(db, op.FirmId, op.TargetId, ct);
    if (remote.PlanDigest != PlanDigest(plan)) throw new OperationBlockedException("workspace-provider-binding-changed");
    if (await db.ClientWorkspaces.AsNoTracking().AnyAsync(x => x.TenantId == plan.Config.TenantId && x.PracticeClientId != plan.Client.Id &&
        x.DriveId == plan.Config.DriveId && x.RemoteItemId == remote.ClientFolderId, ct))
      throw new OperationBlockedException("workspace-client-folder-already-owned", authorization: true);
    var activation = await db.EngagementActivations.AsNoTracking().SingleAsync(x => x.FirmId == op.FirmId && x.EngagementId == op.TargetId, ct);
    var now = DateTimeOffset.UtcNow;
    var eventActor = new ActorContext(activation.ActivatedByUserId, op.FirmId, 0, ["Partner"]);

    var workspace = await db.ClientWorkspaces.SingleAsync(x => x.Id == plan.Workspace.Id && x.FirmId == op.FirmId, ct);
    if (workspace.State != ClientWorkspaceStates.Ready || workspace.RemoteItemId != remote.ClientFolderId || workspace.DriveId != plan.Config.DriveId)
    {
      var previous = workspace.State;
      workspace.State = ClientWorkspaceStates.Ready;
      workspace.ConnectionRevisionId = plan.Connection.Id;
      workspace.FolderTemplateVersionId = plan.ClientTemplate.Id;
      workspace.TenantId = plan.Config.TenantId;
      workspace.SiteId = plan.Config.SiteId;
      workspace.DriveId = plan.Config.DriveId;
      workspace.RootFolderId = plan.Config.RootFolderId;
      workspace.RemoteItemId = remote.ClientFolderId;
      workspace.LastErrorCode = null;
      workspace.LastVerifiedAt = now;
      workspace.Revision++;
      TenantAdministration.AddEvent(db, eventActor, "CLIENT_WORKSPACE_PROVISIONED", now, oldState: previous, newState: workspace.State,
        reason: "Provisioned automatically on Partner engagement activation", result: "READY");
    }
    var test = new RemoteCapabilityTest(remote.TestPassed, remote.TestPassed, remote.TestPassed, remote.Diagnostic, remote.Correlation);
    var applied = await PbcRepositoryProvisioningService.ApplyEngagementBindingAsync(db, eventActor, plan.Engagement, plan.Config,
      remote.PbcFolderId, test, now, "Provisioned automatically on Partner engagement activation", ct);
    if (!applied.Succeeded) throw new OperationBlockedException(applied.ErrorCode ?? "pbc.binding-conflict");
    return verifiedRemoteResult;
  }

  private sealed record Plan(Engagement Engagement, PracticeClient Client, ClientWorkspace Workspace, FirmWorkspaceConfiguration Config,
    Microsoft365ConnectionRevision Connection, FolderTemplateVersion ClientTemplate, FolderTemplateVersion EngagementTemplate,
    bool Ste, IReadOnlyList<Engagement> Siblings);

  private static async Task<Plan> PlanAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == engagementId, ct)
      ?? throw new OperationBlockedException("engagement-not-found", authorization: true);
    if (!await db.EngagementActivations.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.EngagementId == engagementId, ct))
      throw new OperationBlockedException("engagement-not-activated", authorization: true);
    var client = await db.PracticeClients.AsNoTracking().SingleAsync(x => x.FirmId == firmId && x.Id == engagement.PracticeClientId, ct);
    var workspace = await db.ClientWorkspaces.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.PracticeClientId == client.Id && x.Purpose == "PRIMARY", ct)
      ?? throw new OperationBlockedException("client-workspace-missing");
    if (!await db.AcceptanceDecisions.AsNoTracking().AnyAsync(x => x.Id == workspace.AcceptanceDecisionId && x.FirmId == firmId &&
          x.PracticeClientId == client.Id && x.Decision == "Accepted", ct))
      throw new OperationBlockedException("client-not-accepted", authorization: true);
    var active = await PbcRepositoryProvisioningService.ActiveSiteAsync(db, firmId, ct, client.Id);
    if (!active.Succeeded) throw new SafeRetryException(TimeSpan.FromMinutes(5));
    var (config, connection, clientTemplate) = active.Value!;
    var engagementTemplate = await db.FolderTemplateVersions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.Purpose == FolderTemplatePurposes.EngagementWorkspace && x.ApprovedAt != null)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (engagementTemplate is null || PbcRepositoryProvisioningService.FindNode(engagementTemplate.ManifestJson, PbcRepositoryProvisioningService.PbcNodeKey) is null)
      throw new OperationBlockedException("engagement-template-missing-pbc-node");
    var ste = PbcRepositoryProvisioningService.FindNode(clientTemplate.ManifestJson, PbcRepositoryProvisioningService.EngagementsNodeKey) is null;
    var siblings = await db.Engagements.AsNoTracking().Where(x => x.FirmId == firmId && x.PracticeClientId == client.Id && x.Id != engagementId).ToListAsync(ct);
    return new(engagement, client, workspace, config, connection, clientTemplate, engagementTemplate, ste, siblings);
  }

  /// <summary>Exact client folder name from the display name with unsafe path characters removed.</summary>
  public static string SteClientName(PracticeClient client)
  {
    var display = client.CommercialName ?? client.LegalName;
    var invalid = new[] { '"', '*', ':', '<', '>', '?', '/', '\\', '|', '#', '%', '~', '&', '{', '}' };
    var clean = new string(display.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
    clean = string.Join(" ", clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
    if (clean.Length > 80) clean = clean[..80].TrimEnd('.', ' ');
    return clean.Length == 0 ? PbcRepositoryProvisioningService.FolderName("Client", client.Id) : clean;
  }

  /// <summary>
  /// The engagement year of the period end (e.g. "2026"). The first-created engagement of a year keeps the plain year;
  /// a later engagement of the same client and year is disambiguated by service route (then by id), deterministically.
  /// </summary>
  public static string SteEngagementName(Engagement engagement, IReadOnlyList<Engagement> siblings)
  {
    var year = engagement.PeriodEnd.Length >= 4 && engagement.PeriodEnd[..4].All(char.IsDigit) ? engagement.PeriodEnd[..4] : "Engagement";
    bool SameYear(Engagement e) => e.PeriodEnd.StartsWith(year, StringComparison.Ordinal);
    bool Earlier(Engagement e) => e.CreatedAt < engagement.CreatedAt || (e.CreatedAt == engagement.CreatedAt && e.Id.CompareTo(engagement.Id) < 0);
    var sameYear = siblings.Where(SameYear).ToList();
    if (!sameYear.Any(Earlier)) return year;
    var route = PbcRepositoryProvisioningService.FolderName(engagement.ServiceRoute, engagement.Id)[..^11].Trim();
    var named = $"{year} - {(route.Length == 0 ? "Engagement" : route)}";
    return sameYear.Any(e => Earlier(e) && string.Equals(e.ServiceRoute, engagement.ServiceRoute, StringComparison.OrdinalIgnoreCase))
      ? $"{named} ({engagement.Id.ToString("N")[..8]})" : named;
  }

  private static OperationResult Encode(RemoteResult result)
  {
    var identity = JsonSerializer.Serialize(result);
    return new(identity, Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(identity)));
  }

  private static string PlanDigest(Plan plan) => TenantAdministration.Fingerprint(plan.Connection.Id.ToString(),
    plan.Config.TenantId, plan.Config.SiteId, plan.Config.DriveId, plan.Config.RootFolderId,
    plan.ClientTemplate.Id.ToString(), plan.ClientTemplate.ManifestDigest, plan.EngagementTemplate.Id.ToString(), plan.EngagementTemplate.ManifestDigest,
    plan.Connection.RuntimeCredentialReference, plan.Client.LegalName, plan.Client.CommercialName,
    plan.Engagement.Id.ToString(), plan.Engagement.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
    plan.Engagement.PeriodEnd, plan.Engagement.ServiceRoute, plan.Engagement.CreatedAt.ToString("O"),
    SteEngagementName(plan.Engagement, plan.Siblings), plan.Workspace.AcceptanceDecisionId.ToString());
}

/// <summary>Finds activated engagements that have no workspace operation and no working binding, and queues one each.</summary>
public sealed class EngagementWorkspaceDiscovery(
  IAuditSphereDbContextFactory factory, IOperationStore store, EngagementWorkspaceProvisioningHandler handler, WorkerOptions options) : IPendingOperationDiscovery
{
  public async Task<int> EnqueuePendingAsync(CancellationToken ct)
  {
    await using var read = await factory.CreateAsync(ct);
    var pending = await read.EngagementActivations.AsNoTracking().Where(a => a.FirmId == options.FirmId &&
        !read.DurableOperations.Any(o => o.FirmId == a.FirmId && o.TargetId == a.EngagementId && o.OperationKind == EngagementWorkspaceProvisioningHandler.Kind) &&
        !read.RepositoryBindings.Any(b => b.FirmId == a.FirmId && b.EngagementId == a.EngagementId && b.Classification == "working" && b.CapabilityProfile == "selected-site"))
      .OrderBy(a => a.ActivatedAt).ThenBy(a => a.Id).Take(25).Select(a => new { a.EngagementId, a.PracticeClientId, a.ActivatedByUserId }).ToListAsync(ct);
    var count = 0;
    foreach (var item in pending)
    {
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var result = await store.EnqueueAsync(db, new OperationRequest(options.FirmId, item.PracticeClientId, item.EngagementId, EngagementWorkspaceProvisioningHandler.Kind,
        item.EngagementId, 1, "engagement-workspace:" + item.EngagementId.ToString("D"),
        JsonSerializer.Serialize(new { engagementId = item.EngagementId.ToString("D") }), item.ActivatedByUserId), handler, ct);
      if (!result.Succeeded) continue;
      await tx.CommitAsync(ct);
      count++;
    }
    return count;
  }
}
