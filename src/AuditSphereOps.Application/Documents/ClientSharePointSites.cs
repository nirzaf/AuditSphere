using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record ClientSitePolicy(bool Enabled, string TenantId, string SiteHost, Guid AdministratorUserId, DateTimeOffset? ClientsCreatedAfter = null);
public sealed record ClientSiteRequest(Guid FirmId, Guid ClientId, string TenantId, string Url, string Title, string OwnershipMarker,
  string? KnownSiteId, IReadOnlyList<string> StaffObjectIds, Guid? CorrelationId = null);
public sealed record ClientSiteMutation(string Operation, string Target, string OldState, string NewState, string? CorrelationId);
public sealed record ClientSiteReceipt(string TenantId, string Url, string OwnershipMarker, string SiteId, string DriveId,
  string RootItemId, string StaffGroupId, IReadOnlyList<string> MemberObjectIds, string? CorrelationId, IReadOnlyList<ClientSiteMutation>? Mutations = null);

/// <summary>Privileged site calls live only in Infrastructure and the isolated client-sites worker.</summary>
public sealed class ClientSitePreflightException(bool retryable, bool authorization = false) : Exception("client-site-preflight")
{
  public bool Retryable { get; } = retryable;
  public bool Authorization { get; } = authorization;
}

public interface IClientSharePointSiteProvider
{
  Task<ClientSiteReceipt> ReconcileAsync(ClientSiteRequest request, bool mayCreate, CancellationToken ct);
}

public static class ClientSharePointSites
{
  public static string SiteSlug(string name, Guid clientId)
  {
    var slug = new string(name.Normalize(NormalizationForm.FormD).Where(c => c <= 127)
      .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
    slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
    if (slug.Length > 50) slug = slug[..50].TrimEnd('-');
    return $"{(slug.Length == 0 ? "client" : slug)}-{clientId:N}";
  }

  internal static async Task<ActorContext> AdministratorAsync(IAuditSphereDbContext db, Guid firmId, Guid userId, CancellationToken ct)
  {
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == userId, ct);
    var actor = new ActorContext(userId, firmId, user?.SessionEpoch ?? 0, ["Administrator"]);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      throw new OperationBlockedException("client-site-administrator-revoked", authorization: true);
    return actor;
  }

  public static async Task<IReadOnlyList<string>> DesiredStaffAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId,
    string tenantId, CancellationToken ct = default)
  {
    var now = DateTimeOffset.UtcNow;
    var subjects = await (from assignment in db.EngagementStaffAssignments.AsNoTracking()
      join user in db.Users.AsNoTracking() on new { assignment.FirmId, Id = assignment.UserId } equals new { user.FirmId, user.Id }
      join grant in db.RoleGrants.AsNoTracking() on new { assignment.FirmId, Id = assignment.RoleGrantId } equals new { grant.FirmId, grant.Id }
      where assignment.FirmId == firmId && assignment.ClientId == clientId && assignment.RevokedAt == null &&
        !user.Disabled && user.UserKind == "Staff" && user.TenantId == tenantId && grant.UserId == user.Id &&
        grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > now) &&
        grant.ClientId == clientId && grant.EngagementId == assignment.EngagementId && grant.GrantedAt <= now
      select user.Subject).ToListAsync(ct);
    return subjects.Where(x => Guid.TryParse(x, out _)).Select(x => Guid.Parse(x).ToString("D")).Distinct(StringComparer.Ordinal)
      .OrderBy(x => x, StringComparer.Ordinal).ToArray();
  }

  internal static async Task<bool> HasUnverifiedStaffAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, string tenantId, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    var users = await (from assignment in db.EngagementStaffAssignments.AsNoTracking()
      join user in db.Users.AsNoTracking() on new { assignment.FirmId, Id = assignment.UserId } equals new { user.FirmId, user.Id }
      join grant in db.RoleGrants.AsNoTracking() on new { assignment.FirmId, Id = assignment.RoleGrantId } equals new { grant.FirmId, grant.Id }
      where assignment.FirmId == firmId && assignment.ClientId == clientId && assignment.RevokedAt == null && !user.Disabled &&
        user.UserKind == "Staff" && grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > now)
      select new { user.TenantId, user.Subject }).ToListAsync(ct);
    return users.Any(x => x.TenantId != tenantId || !Guid.TryParse(x.Subject, out _));
  }

  internal static string Digest(IReadOnlyList<string> ids) => TenantAdministration.Fingerprint(ids.ToArray());
}

public sealed class ClientSharePointSiteHandler(IAuditSphereDbContextFactory factory, IClientSharePointSiteProvider provider) : IOperationHandler
{
  public const string Kind = "ReconcileClientSharePointSite.v1";
  public const string Group = "client-sites";
  public OperationDefinition Definition { get; } = new(Kind, OperationMode.LIVE, OperationAuthority.LIVE_PROVIDER, Group: Group);

  public string NormalizePayload(OperationRequest request)
  {
    using var json = JsonDocument.Parse(request.PayloadJson);
    if (request.ClientId != request.TargetId || request.EngagementId != null || request.ExpectedRevision != 1 ||
        !json.RootElement.TryGetProperty("desiredDigest", out var digest) || digest.GetString() is not { Length: 64 } value ||
        !value.All(char.IsAsciiHexDigit)) throw new OperationBlockedException("invalid-client-site-request");
    return JsonSerializer.Serialize(new { desiredDigest = value });
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    var rows = await db.ClientSharePointSites.FromSqlInterpolated($"SELECT * FROM client_share_point_sites WHERE firm_id = {op.FirmId} AND client_id = {op.TargetId} FOR UPDATE").ToListAsync(ct);
    if (rows.Count != 1 || op.ClientId != op.TargetId || op.OriginatorId != rows[0].RequestedByUserId)
      throw new OperationBlockedException("client-site-scope-denied", authorization: true);
    await ClientSharePointSites.AdministratorAsync(db, op.FirmId, rows[0].RequestedByUserId, ct);
    if (!await db.Microsoft365ConnectionRevisions.AnyAsync(x => x.Id == rows[0].ConnectionRevisionId && x.FirmId == op.FirmId &&
        x.TenantId == rows[0].TenantId && x.State == Microsoft365RevisionStates.Active && x.ConsentState == "VERIFIED", ct))
      throw new OperationBlockedException("client-site-connection-unverified", authorization: true);
  }

  public Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct) => RunAsync(op, mayCreate: true, ct);
  // An unknown create must never initiate another create, even if the status endpoint currently says absent.
  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) => RunAsync(op, mayCreate: false, ct);

  private async Task<OperationResult> RunAsync(DurableOperation op, bool mayCreate, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockTargetAsync(db, op, ct);
    var site = await db.ClientSharePointSites.SingleAsync(x => x.FirmId == op.FirmId && x.ClientId == op.TargetId, ct);
    var desired = await ClientSharePointSites.DesiredStaffAsync(db, op.FirmId, site.ClientId, site.TenantId, ct);
    var allowCreate = mayCreate && site.State == "REQUESTED" && site.CreationOperationId == op.Id && site.CreationDispatchedAt == null;
    if (allowCreate) { site.CreationDispatchedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); }
    await tx.CommitAsync(ct); // Durable creation fence survives timeouts, cancellation and operator re-arming.
    ClientSiteReceipt receipt;
    try
    {
      receipt = await provider.ReconcileAsync(new(op.FirmId, site.ClientId, site.TenantId, site.RequestedUrl, site.Title,
        site.OwnershipMarker, site.SiteId, desired, op.CorrelationId), allowCreate, ct);
    }
    catch (ClientSitePreflightException failure)
    {
      // Only the provider's read-only preflight can prove that no creation POST was attempted.
      if (allowCreate)
      {
        await using var recovery = await factory.CreateAsync(ct);
        await recovery.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_share_point_sites SET creation_dispatched_at = NULL WHERE firm_id = {op.FirmId} AND client_id = {site.ClientId} AND creation_operation_id = {op.Id} AND state = 'REQUESTED' AND site_id IS NULL", ct);
      }
      if (failure.Retryable) throw new SafeRetryException(TimeSpan.FromSeconds(30));
      throw new OperationBlockedException("client-site-preflight-blocked", failure.Authorization);
    }
    var identity = JsonSerializer.Serialize(receipt);
    return new(identity, Hashing.Sha256Hex(Encoding.UTF8.GetBytes(identity)));
  }

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op, OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    if (verifiedRemoteResult == null || verifiedRemoteResult.Digest != Hashing.Sha256Hex(Encoding.UTF8.GetBytes(verifiedRemoteResult.Identity)))
      throw new OperationBlockedException("client-site-result-invalid");
    var receipt = JsonSerializer.Deserialize<ClientSiteReceipt>(verifiedRemoteResult.Identity) ?? throw new OperationBlockedException("client-site-result-invalid");
    var site = await db.ClientSharePointSites.SingleAsync(x => x.FirmId == op.FirmId && x.ClientId == op.TargetId, ct);
    var actor = await ClientSharePointSites.AdministratorAsync(db, op.FirmId, site.RequestedByUserId, ct);
    var desired = await ClientSharePointSites.DesiredStaffAsync(db, op.FirmId, site.ClientId, site.TenantId, ct);
    if (receipt.TenantId != site.TenantId || receipt.Url != site.RequestedUrl || receipt.OwnershipMarker != site.OwnershipMarker ||
        string.IsNullOrWhiteSpace(receipt.SiteId) || string.IsNullOrWhiteSpace(receipt.DriveId) || string.IsNullOrWhiteSpace(receipt.RootItemId) ||
        string.IsNullOrWhiteSpace(receipt.StaffGroupId) || site.SiteId != null && site.SiteId != receipt.SiteId ||
        receipt.MemberObjectIds.Any(x => !Guid.TryParse(x, out _))) throw new OperationBlockedException("client-site-result-scope-mismatch", authorization: true);
    var now = DateTimeOffset.UtcNow;
    foreach (var mutation in receipt.Mutations ?? [])
      TenantAdministration.AddEvent(db, actor, mutation.Operation, now, mutation.OldState, mutation.NewState, site.Reason,
        "VERIFIED", site.TenantId, mutation.Target, roleScopeChange: $"Client {site.ClientId:D}; SharePoint site {receipt.SiteId}; local roles unchanged",
        externalOperationId: op.Id, correlationId: mutation.CorrelationId);
    var previous = JsonSerializer.Deserialize<string[]>(site.MemberObjectIdsJson) ?? [];
    var observed = receipt.MemberObjectIds.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
    foreach (var oid in previous.Except(observed).Concat(observed.Except(previous)))
      TenantAdministration.AddEvent(db, actor, "CLIENT_SITE_STAFF_ACCESS", now, previous.Contains(oid) ? "FULL_CONTROL" : "NONE",
        observed.Contains(oid) ? "FULL_CONTROL" : "NONE", site.Reason, "VERIFIED", site.TenantId, oid,
        roleScopeChange: $"Client {site.ClientId:D}; entire SharePoint site; AuditSphere scope unchanged", externalOperationId: op.Id, correlationId: receipt.CorrelationId);
    TenantAdministration.AddEvent(db, actor, "CLIENT_SITE_RECONCILED", now, site.State, "READY", site.Reason, "VERIFIED",
      site.TenantId, receipt.SiteId, externalOperationId: op.Id, correlationId: receipt.CorrelationId);
    site.State = "READY"; site.SiteId = receipt.SiteId; site.DriveId = receipt.DriveId; site.RootItemId = receipt.RootItemId;
    site.StaffGroupId = receipt.StaffGroupId; site.VerifiedAt = now; site.LastMembershipSyncAt = now; site.LastOperationId = op.Id;
    site.MemberObjectIdsJson = JsonSerializer.Serialize(observed);
    site.DesiredDigest = ClientSharePointSites.Digest(desired);
    site.MembershipState = desired.SequenceEqual(observed) && !await ClientSharePointSites.HasUnverifiedStaffAsync(db, op.FirmId, site.ClientId, site.TenantId, ct) ? "VERIFIED" : "PARTIAL";
    return verifiedRemoteResult;
  }
}

/// <summary>Standing administrator policy: every client gets an intent; current assignments are reconciled at least every five minutes.</summary>
public sealed class ClientSharePointSiteDiscovery(IAuditSphereDbContextFactory factory, IOperationStore store,
  ClientSharePointSiteHandler handler, WorkerOptions options, ClientSitePolicy policy) : IPendingOperationDiscovery
{
  public async Task<int> EnqueuePendingAsync(CancellationToken ct)
  {
    if (!policy.Enabled) return 0;
    await using var read = await factory.CreateAsync(ct);
    await ClientSharePointSites.AdministratorAsync(read, options.FirmId, policy.AdministratorUserId, ct);
    var connection = await read.Microsoft365ConnectionRevisions.AsNoTracking().Where(x => x.FirmId == options.FirmId &&
      x.TenantId == policy.TenantId && x.State == Microsoft365RevisionStates.Active && x.ConsentState == "VERIFIED")
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (connection == null) return 0;
    if (!Uri.TryCreate("https://" + policy.SiteHost, UriKind.Absolute, out var host) || host.Host != policy.SiteHost ||
        !host.Host.EndsWith(".sharepoint.com", StringComparison.Ordinal) || host.AbsolutePath != "/")
      throw new OperationBlockedException("client-site-host-invalid");
    var cutover = policy.ClientsCreatedAfter?.ToUniversalTime() ?? DateTimeOffset.MinValue;
    await using (var policyDb = await factory.CreateAsync(ct))
    {
      await using var policyTx = await policyDb.Database.BeginTransactionAsync(ct);
      var configurations = await policyDb.FirmWorkspaceConfigurations.FromSqlInterpolated($"SELECT * FROM firm_workspace_configurations WHERE firm_id = {options.FirmId} AND connection_revision_id = {connection.Id} FOR UPDATE").ToListAsync(ct);
      if (configurations.Count != 1) return 0;
      var configuration = configurations[0];
      if (configuration.ClientSitesRequiredFrom != null && configuration.ClientSitesRequiredFrom != cutover)
        throw new OperationBlockedException("client-site-rollout-policy-changed", authorization: true);
      if (configuration.ClientSitesRequiredFrom == null)
      {
        configuration.ClientSitesRequiredFrom = cutover;
        var actor = await ClientSharePointSites.AdministratorAsync(policyDb, options.FirmId, policy.AdministratorUserId, ct);
        TenantAdministration.AddEvent(policyDb, actor, "CLIENT_SITE_POLICY_ENABLED", DateTimeOffset.UtcNow, "DISABLED", "ENABLED",
          "Owner-approved new-client site policy; existing repositories retain their location", "AUTHORIZED",
          roleScopeChange: "Assigned staff: entire client site Full Control; local roles unchanged");
        await policyDb.SaveChangesAsync(ct);
      }
      await policyTx.CommitAsync(ct);
    }
    var clients = await read.PracticeClients.AsNoTracking().Where(x => x.FirmId == options.FirmId && x.CreatedAt >= cutover &&
      !read.DurableOperations.Any(o => o.FirmId == x.FirmId && o.TargetId == x.Id && o.OperationKind == ClientSharePointSiteHandler.Kind && o.Status != OperationState.COMPLETED && o.Status != OperationState.CANCELLED_WITH_DISPOSITION) &&
      (!read.ClientSharePointSites.Any(s => s.FirmId == x.FirmId && s.ClientId == x.Id) ||
       read.ClientSharePointSites.Any(s => s.FirmId == x.FirmId && s.ClientId == x.Id &&
         (s.LastMembershipSyncAt == null || s.LastMembershipSyncAt < DateTimeOffset.UtcNow.AddMinutes(-5)))))
      .OrderBy(x => x.CreatedAt).Take(25).ToListAsync(ct);
    var count = 0;
    foreach (var client in clients)
    {
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      // Serialize intent creation and queueing for this client across multiple worker processes.
      await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id = {options.FirmId} AND id = {client.Id} FOR UPDATE").LoadAsync(ct);
      var actor = await ClientSharePointSites.AdministratorAsync(db, options.FirmId, policy.AdministratorUserId, ct);
      var site = await db.ClientSharePointSites.SingleOrDefaultAsync(x => x.FirmId == options.FirmId && x.ClientId == client.Id, ct);
      if (site == null)
      {
        var title = EngagementWorkspaceProvisioningHandler.SteClientName(client);
        site = new ClientSharePointSite { Id = Guid.CreateVersion7(), FirmId = options.FirmId, ClientId = client.Id,
          ConnectionRevisionId = connection.Id, RequestedByUserId = actor.UserId, TenantId = policy.TenantId,
          Title = title, RequestedUrl = $"https://{policy.SiteHost}/sites/{ClientSharePointSites.SiteSlug(title, client.Id)}",
          OwnershipMarker = $"AuditSphere:{options.FirmId:D}:{client.Id:D}", CreatedAt = DateTimeOffset.UtcNow,
          Reason = "Owner-approved client site policy: all assigned staff receive site-wide Full Control" };
        db.ClientSharePointSites.Add(site);
        TenantAdministration.AddEvent(db, actor, "CLIENT_SITE_REQUESTED", site.CreatedAt, "NONE", "REQUESTED", site.Reason,
          "REQUESTED", policy.TenantId, client.Id.ToString("D"));
        await db.SaveChangesAsync(ct);
      }
      // Blocked/uncertain requests require reconciliation or operator recovery, never a replacement create request.
      if (await db.DurableOperations.AnyAsync(x => x.FirmId == options.FirmId && x.TargetId == client.Id && x.OperationKind == ClientSharePointSiteHandler.Kind &&
          x.Status != OperationState.COMPLETED && x.Status != OperationState.CANCELLED_WITH_DISPOSITION, ct)) continue;
      var digest = ClientSharePointSites.Digest(await ClientSharePointSites.DesiredStaffAsync(db, options.FirmId, client.Id, policy.TenantId, ct));
      var tick = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300;
      var result = await store.EnqueueAsync(db, new(options.FirmId, client.Id, null, ClientSharePointSiteHandler.Kind, client.Id, 1,
        $"client-site:{client.Id:D}:{tick}:{digest}", JsonSerializer.Serialize(new { desiredDigest = digest }), site.RequestedByUserId), handler, ct);
      if (!result.Succeeded) continue;
      site.LastOperationId = result.Value;
      site.CreationOperationId ??= result.Value;
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      count++;
    }
    return count;
  }
}

public sealed record ClientSiteStatusRow(Guid ClientId, string ClientName, string State, string MembershipState,
  string? Url, int VerifiedMembers, DateTimeOffset? VerifiedAt, string? OperationState, string RequiredAction, DateTimeOffset? MembershipVerifiedAt = null);

public static class ClientSharePointSiteQuery
{
  public static async Task<CommandResult<IReadOnlyList<ClientSiteStatusRow>>> GetAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<IReadOnlyList<ClientSiteStatusRow>>();
    var clients = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderBy(x => x.LegalName).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
    var ids = clients.Select(x => x.Id).ToArray();
    var activeConnection = await db.Microsoft365ConnectionRevisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.State == "ACTIVE" && x.ConsentState == "VERIFIED")
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var sites = await db.ClientSharePointSites.AsNoTracking().Where(x => x.FirmId == actor.FirmId && ids.Contains(x.ClientId)).ToListAsync(ct);
    var operationIds = sites.Where(x => x.LastOperationId != null).Select(x => x.LastOperationId!.Value).ToArray();
    var operations = await db.DurableOperations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && operationIds.Contains(x.Id) &&
      x.OperationKind == ClientSharePointSiteHandler.Kind).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    var rows = clients.Select(client =>
    {
      var site = sites.SingleOrDefault(x => x.ClientId == client.Id);
      var op = operations.FirstOrDefault(x => x.TargetId == client.Id);
      var blocked = op?.Status is OperationState.PROVIDER_BLOCKED or OperationState.AUTHORIZATION_BLOCKED or OperationState.DEAD_LETTER or OperationState.RESULT_UNCERTAIN;
      var invalidSite = site is { State: "READY" } && (site.VerifiedAt is null || site.VerifiedAt > DateTimeOffset.UtcNow ||
        site.ConnectionRevisionId != activeConnection?.Id || site.TenantId != activeConnection?.TenantId ||
        string.IsNullOrWhiteSpace(site.SiteId) || string.IsNullOrWhiteSpace(site.DriveId) || string.IsNullOrWhiteSpace(site.RootItemId));
      var stale = site is not null && (site.LastMembershipSyncAt is null || site.LastMembershipSyncAt < DateTimeOffset.UtcNow.AddMinutes(-10) || site.LastMembershipSyncAt > DateTimeOffset.UtcNow);
      var action = site == null ? "Enable the isolated client-sites worker with separately consented credentials and approved administrator policy." :
        invalidSite ? "Review the active consent-verified tenant connection, exact site resources and site verification before using this site." :
        blocked ? "Review Microsoft consent, site ownership and operation evidence; use Operations to re-arm reconciliation. Never replace an uncertain create." :
        site.MembershipState == "PENDING" ? "The worker must reconcile the updated team before staff permissions are verified." :
        site.MembershipState == "PARTIAL" ? "Some assigned identities are disabled, absent or changed. Verify their Microsoft directory status; membership will be reconciled." :
        stale ? "Membership verification is stale. Check the client-sites worker." :
        site.State == "READY" ? "No action required." : "Wait for site provisioning and exact-site verification.";
      return new ClientSiteStatusRow(client.Id, client.CommercialName ?? client.LegalName, blocked || invalidSite ? "BLOCKED_EXTERNAL" : site?.State ?? "BLOCKED_EXTERNAL",
        stale ? "STALE" : site?.MembershipState ?? "PENDING", site?.RequestedUrl, site == null ? 0 : (JsonSerializer.Deserialize<string[]>(site.MemberObjectIdsJson) ?? []).Length,
        site?.VerifiedAt, op?.Status.ToString(), action, site?.LastMembershipSyncAt);
    }).ToArray();
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<IReadOnlyList<ClientSiteStatusRow>>();
    return CommandResult<IReadOnlyList<ClientSiteStatusRow>>.Ok(rows);
  }
}
