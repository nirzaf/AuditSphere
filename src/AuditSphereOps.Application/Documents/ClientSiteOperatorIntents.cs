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

/// <summary>Published provider evidence, distinct from an operator's request or recovery receipt.</summary>
public sealed record ClientSitePublishedReceipt(Guid OperationId, string Digest, DateTimeOffset? CompletedAt,
  string SiteId, string DriveId, string RootItemId, string StaffGroupId, string StaffAuthorizationDigest,
  IReadOnlyList<string> ManagedStaffObjectIds, IReadOnlyList<string> ResidualAccessReferences);

/// <summary>Current exact-client review. No caller-supplied resource or membership authority.</summary>
public sealed record ClientSiteOperatorReview(Guid ClientId, string TenantId, DateTimeOffset CreatedAt,
  DateTimeOffset? RolloutBoundary, string? Url, string? OwnershipMarker, string ReviewToken,
  string StaffAuthorizationDigest, IReadOnlyList<string> StaffObjectIds, bool Eligible, string RequiredAction,
  string State, string MembershipState, Guid? OperationId, string? OperationState,
  string ReceiptState, ClientSitePublishedReceipt? PublishedReceipt);

/// <summary>A request awaiting existing worker discovery, never a claim of a provisioned site.</summary>
public sealed record ClientSiteIntentRequestReceipt(Guid ClientId, string State, Guid? OperationId);

public static class ClientSiteOperatorIntents
{
  public static async Task<CommandResult<ClientSiteOperatorReview>> ReviewAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid clientId, ClientSitePolicy policy, bool reconcileOnly, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ClientSiteOperatorReview>();
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == clientId, ct);
    if (client is null) return TenantAdministration.Denied<ClientSiteOperatorReview>();
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var configurations = await db.FirmWorkspaceConfigurations.AsNoTracking().Where(x => x.FirmId == actor.FirmId).ToListAsync(ct);
    var boundaries = configurations.Where(x => x.ClientSitesRequiredFrom != null)
      .Select(x => x.ClientSitesRequiredFrom!.Value.ToUniversalTime()).Distinct().ToArray();
    var cutoff = policy.ClientsCreatedAfter is { Offset: var offset } configured && offset == TimeSpan.Zero
      ? new DateTimeOffset(configured.UtcTicks - configured.UtcTicks % 10, TimeSpan.Zero) : (DateTimeOffset?)null;
    var effective = boundaries.Length == 1 ? boundaries[0] : (DateTimeOffset?)null;
    var site = await db.ClientSharePointSites.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    var operation = site?.LastOperationId is Guid operationId
      ? await db.DurableOperations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == operationId &&
          x.TargetId == clientId && x.ClientId == clientId && x.OperationKind == ClientSharePointSiteHandler.Kind, ct) : null;
    var outstanding = await db.DurableOperations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.TargetId == clientId &&
      x.OperationKind == ClientSharePointSiteHandler.Kind && x.Status != OperationState.COMPLETED &&
      x.Status != OperationState.CANCELLED_WITH_DISPOSITION).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    var staff = await ClientSharePointSites.DesiredStaffAsync(db, actor.FirmId, clientId, policy.TenantId, ct);
    var staffBasis = await ClientSharePointSites.AuthorizationDigestAsync(db, actor.FirmId, clientId, ct);
    var title = EngagementWorkspaceProvisioningHandler.SteClientName(client);
    var hostValid = Uri.TryCreate("https://" + policy.SiteHost, UriKind.Absolute, out var host) && host.Host == policy.SiteHost &&
      host.AbsolutePath == "/" && host.IsDefaultPort && !host.UserInfo.Any() && !host.Query.Any() && !host.Fragment.Any() &&
      host.Host.EndsWith(".sharepoint.com", StringComparison.Ordinal);
    var url = hostValid ? $"https://{policy.SiteHost}/sites/{ClientSharePointSites.SiteSlug(title, clientId)}" : null;
    var marker = $"AuditSphere:{actor.FirmId:D}:{clientId:D}";
    var heartbeat = await db.ClientSiteWorkerHeartbeats.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.WorkerGroup == ClientSharePointSiteHandler.Group)
      .OrderByDescending(x => x.LastSeenAt).FirstOrDefaultAsync(ct);
    var now = DateTimeOffset.UtcNow;
    var blockedOperation = outstanding?.Status is OperationState.AUTHORIZATION_BLOCKED or OperationState.PROVIDER_BLOCKED or
      OperationState.RESULT_UNCERTAIN or OperationState.DEAD_LETTER or OperationState.CANCEL_REQUESTED;
    var reason = !policy.Enabled ? "Dedicated-site provisioning is disabled." :
      cutoff is null || effective is null || effective != cutoff ? "One matching configured and persisted UTC rollout boundary is required." :
      client.CreatedAt < effective ? "This client predates the rollout boundary; historical conversion is not authorized." :
      !hostValid || !Guid.TryParse(policy.TenantId, out var tenant) || tenant == Guid.Empty ? "The server client-site tenant and host must be configured." :
      connection is not { State: "ACTIVE", ConsentState: "VERIFIED" } || connection.TenantId != policy.TenantId ||
        !configurations.Any(x => x.ConnectionRevisionId == connection.Id && x.TenantId == connection.TenantId && x.ClientSitesRequiredFrom == effective)
        ? "An active consent-verified tenant and its persisted rollout configuration are required." :
      site is not null && (site.ConnectionRevisionId != connection.Id || site.TenantId != policy.TenantId || site.RequestedUrl != url || site.OwnershipMarker != marker)
        ? "The retained site binding differs from the reviewed policy; reconcile ownership before requesting work." :
      site is null && outstanding is not null || site?.LastOperationId is not null && operation is null
        ? "The retained client-site operation binding is incomplete; recover the original evidence before requesting work." :
      reconcileOnly && site?.SiteId is null ? "Staff-only reconciliation requires an existing published dedicated site." :
      blockedOperation ? "Recover the original blocked or uncertain operation through reviewed Operations; do not replace it." :
      heartbeat is null || heartbeat.ReadinessState != "READY" || heartbeat.LastSeenAt < now.AddSeconds(-45) || heartbeat.LastSeenAt > now.AddSeconds(5)
        ? "The isolated client-sites worker must report current readiness." : "Ready for the existing worker's discovery cycle.";
    var eligible = reason == "Ready for the existing worker's discovery cycle.";
    var receipt = Published(site, operation);
    var receiptIsCurrent = receipt is not null && receipt.StaffAuthorizationDigest == staffBasis &&
      receipt.ManagedStaffObjectIds.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(staff.OrderBy(x => x, StringComparer.Ordinal));
    // Normalize absence and an untouched REQUESTED row so an identical replay is a no-op.
    var token = Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      actor.FirmId, actor.UserId, actor.SessionEpoch, client.Id, client.CreatedAt, title, policy.Enabled,
      policy.TenantId, policy.SiteHost, cutoff, effective, Connection = connection?.Id, ConnectionState = connection?.State,
      ConsentState = connection?.ConsentState, staffBasis, staff, reconcileOnly, RequestedBy = site?.RequestedByUserId ?? actor.UserId,
      Url = site?.RequestedUrl ?? url, Marker = site?.OwnershipMarker ?? marker, State = site?.State ?? "REQUESTED",
      site?.SiteId, site?.DriveId, site?.RootItemId, site?.CreationDispatchedAt,
      LastOperation = operation?.Id, LastStatus = operation?.Status, Outstanding = outstanding?.Id, OutstandingState = outstanding?.Status
    }));
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ClientSiteOperatorReview>();
    return CommandResult<ClientSiteOperatorReview>.Ok(new(clientId, policy.TenantId, client.CreatedAt, effective, url, marker,
      token, staffBasis, staff, eligible, reason, site?.State ?? "NOT_REQUESTED",
      receiptIsCurrent ? site!.MembershipState : "PENDING",
      outstanding?.Id ?? operation?.Id, (outstanding ?? operation)?.Status.ToString(), receipt is null ? "NOT_PUBLISHED" : receiptIsCurrent ? "PUBLISHED" : "HISTORICAL", receipt));
  }

  public static async Task<CommandResult<ClientSiteIntentRequestReceipt>> RequestAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid clientId, ClientSitePolicy policy, string? reviewToken, bool reviewed, string? reason,
    bool reconcileOnly, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ClientSiteIntentRequestReceipt>();
    if (!reviewed || reviewToken is null || reviewToken.Length != 64 || !reviewToken.All(Uri.IsHexDigit) ||
        string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
      return CommandResult<ClientSiteIntentRequestReceipt>.Fail("review.required", "Review the exact client and staff authority and provide a bounded reason.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await ClientSharePointSites.LockIntentScopeAsync(db, actor.FirmId, clientId, ct)) return TenantAdministration.Denied<ClientSiteIntentRequestReceipt>();
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id = {actor.FirmId} AND id = {actor.UserId} FOR SHARE").AsNoTracking().LoadAsync(ct);
    // Capture the connection before validating the review; never select a newer revision afterwards.
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var current = await ReviewAsync(db, actor, clientId, policy, reconcileOnly, ct);
    if (!current.Succeeded) return CommandResult<ClientSiteIntentRequestReceipt>.Fail(current.ErrorCode!, current.Message!);
    var view = current.Value!;
    if (view.ReviewToken != reviewToken) return CommandResult<ClientSiteIntentRequestReceipt>.Fail(ErrorCodes.StaleRevision, "Client-site authority changed. Review the exact current snapshot again.");
    if (!view.Eligible) return CommandResult<ClientSiteIntentRequestReceipt>.Fail(ErrorCodes.GateBlocked, view.RequiredAction);
    if (view.OperationId is Guid operationId && view.OperationState is not ("COMPLETED" or "CANCELLED_WITH_DISPOSITION"))
    {
      await tx.CommitAsync(ct);
      return CommandResult<ClientSiteIntentRequestReceipt>.Ok(new(clientId, "ALREADY_QUEUED", operationId));
    }
    var site = await db.ClientSharePointSites.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    if (site is null)
    {
      var client = await db.PracticeClients.SingleAsync(x => x.FirmId == actor.FirmId && x.Id == clientId, ct);
      if (connection is not { State: "ACTIVE", ConsentState: "VERIFIED" })
        return CommandResult<ClientSiteIntentRequestReceipt>.Fail(ErrorCodes.StaleRevision, "Review the active tenant connection again.");
      site = new ClientSharePointSite { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
        ConnectionRevisionId = connection.Id, RequestedByUserId = actor.UserId, TenantId = view.TenantId,
        Title = EngagementWorkspaceProvisioningHandler.SteClientName(client), RequestedUrl = view.Url!, OwnershipMarker = view.OwnershipMarker!,
        CreatedAt = DateTimeOffset.UtcNow, Reason = reason.Trim() };
      db.ClientSharePointSites.Add(site);
    }
    else if (site.LastMembershipSyncAt is null && site.MembershipState == "PENDING")
    {
      await tx.CommitAsync(ct);
      return CommandResult<ClientSiteIntentRequestReceipt>.Ok(new(clientId, "PENDING_DISCOVERY", null));
    }
    site.LastMembershipSyncAt = null;
    site.MembershipState = "PENDING";
    TenantAdministration.AddEvent(db, actor, "CLIENT_SITE_INTENT_REQUESTED", DateTimeOffset.UtcNow, view.State,
      "PENDING_DISCOVERY", reason.Trim(), "REQUESTED", view.TenantId, clientId.ToString("D"));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ClientSiteIntentRequestReceipt>.Ok(new(clientId, "PENDING_DISCOVERY", null));
  }

  private static ClientSitePublishedReceipt? Published(ClientSharePointSite? site, DurableOperation? operation)
  {
    if (site is not { State: "READY" } || operation is not { Status: OperationState.COMPLETED, ResultIdentity: not null, ResultDigest: not null, CompletedAt: not null } ||
        operation.CompletedAt > DateTimeOffset.UtcNow.AddSeconds(5) || operation.ResultIdentity.Length > 1048576 ||
        operation.ResultDigest != Hashing.Sha256Hex(Encoding.UTF8.GetBytes(operation.ResultIdentity))) return null;
    try
    {
      var receipt = JsonSerializer.Deserialize<ClientSiteReceipt>(operation.ResultIdentity);
      if (receipt is null || receipt.MemberObjectIds is null || receipt.MemberObjectIds.Count > 200 || receipt.MemberObjectIds.Any(x => !Guid.TryParse(x, out _)) ||
          string.IsNullOrWhiteSpace(receipt.SiteId) || string.IsNullOrWhiteSpace(receipt.DriveId) || string.IsNullOrWhiteSpace(receipt.RootItemId) ||
          string.IsNullOrWhiteSpace(receipt.StaffGroupId) || receipt.AuthorizedStaffDigest is not { Length: 64 } ||
          !receipt.MemberObjectIds.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
            (JsonSerializer.Deserialize<string[]>(site.MemberObjectIdsJson) ?? []).OrderBy(x => x, StringComparer.Ordinal)) ||
          receipt.TenantId != site.TenantId || receipt.Url != site.RequestedUrl || receipt.OwnershipMarker != site.OwnershipMarker ||
          receipt.SiteId != site.SiteId || receipt.DriveId != site.DriveId || receipt.RootItemId != site.RootItemId || receipt.StaffGroupId != site.StaffGroupId) return null;
      return new(operation.Id, operation.ResultDigest, operation.CompletedAt, receipt.SiteId, receipt.DriveId,
        receipt.RootItemId, receipt.StaffGroupId, receipt.AuthorizedStaffDigest, receipt.MemberObjectIds, receipt.ResidualAccessReferences ?? []);
    }
    catch (JsonException) { return null; }
  }
}
