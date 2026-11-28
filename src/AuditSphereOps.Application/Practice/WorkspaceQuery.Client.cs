using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class WorkspaceQuery
{
  /// <summary>Client-level authority is required even when the caller knows an engagement identity.</summary>
  public static async Task<CommandResult<ClientWorkspace>> ClientAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid id, CancellationToken ct = default, ClientWorkspacePaging? paging = null)
  {
    // Retained older UI builds call without paging and expect the original bounded window.
    var legacyWindow = paging is null;
    paging ??= new(0, 100, 0, 100);
    if (paging.EngagementPage is < 0 or > 10000 || paging.ContactPage is < 0 or > 10000 ||
        (!legacyWindow && (paging.EngagementPageSize is not (10 or 25 or 50) || paging.ContactPageSize is not (10 or 25 or 50))))
      return CommandResult<ClientWorkspace>.Fail("request.invalid", "Invalid client pages.");
    var request = new AuthorizationRequest(actor.FirmId, ClientId: id, RequiredRoles: ClientRoles, InternalOnly: true);
    string[] summaryRoles = [.. EngagementRoles, "RelationshipManager"];
    var initialSnapshot = await AuthorizationDecision.LoadBatchAuthorizationSnapshotAsync(
      db, actor, requestedClientIds: [id], requestedEngagementIds: null, includeGrantTargets: true, ct);
    var initialAt = DateTimeOffset.UtcNow;
    if (!initialSnapshot.Authorize(actor, request, initialAt).Succeeded)
      return UnavailableClient();
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(c => c.FirmId == actor.FirmId && c.Id == id, ct);
    if (client is null) return UnavailableClient();
    var latestLifecycle = await db.ClientLifecycleEvents.AsNoTracking().Where(e => e.FirmId == actor.FirmId && e.ClientId == id)
      .OrderByDescending(e => e.Revision).Select(e => new { e.Revision, e.EventKind }).FirstOrDefaultAsync(ct);
    var lastArchivedAt = await db.ClientLifecycleEvents.AsNoTracking().Where(e => e.FirmId == actor.FirmId &&
        e.ClientId == id && e.EventKind == ClientLifecycleEventKinds.Archived)
      .OrderByDescending(e => e.Revision).Select(e => (DateTimeOffset?)e.OccurredAt).FirstOrDefaultAsync(ct);

    // Capture contributing scope requests once. Reauthorization later refuses a mixed old/new view.
    // Business summaries support annual preparation; this does not grant the professional engagement workspace.
    var summaryRoleSet = new HashSet<string>(summaryRoles, StringComparer.Ordinal);
    var grants = initialSnapshot.Grants
      .Where(g => summaryRoleSet.Contains(g.Role) && (g.ClientId == null || g.ClientId == id))
      .Select(g => (g.ClientId, g.EngagementId))
      .Distinct()
      .ToList();
    var requests = new List<AuthorizationRequest> { request };
    var engagementIds = new List<Guid>();
    var allEngagements = false;
    foreach (var grant in grants)
    {
      var scope = new AuthorizationRequest(actor.FirmId, grant.ClientId, grant.EngagementId, summaryRoles,
        InternalOnly: true, RequireFirmWide: grant.ClientId is null && grant.EngagementId is null);
      if (!initialSnapshot.Authorize(actor, scope, initialAt).Succeeded) continue;
      requests.Add(scope);
      if (grant.EngagementId is { } engagementId) engagementIds.Add(engagementId);
      else allEngagements = true;
    }
    var engagements = db.Engagements.AsNoTracking().Where(e => e.FirmId == actor.FirmId && e.PracticeClientId == id &&
      (allEngagements || engagementIds.Contains(e.Id)));
    var engagementTotal = await engagements.CountAsync(ct);
    var blockedTotal = await engagements.CountAsync(e => e.ProfessionalWorkBlocked, ct);
    var rows = await engagements.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id)
      .Skip(paging.EngagementPage * paging.EngagementPageSize).Take(paging.EngagementPageSize)
      .Select(e => new WorkspaceEngagement(e.Id, e.ServiceRoute, e.Status, e.PeriodStart, e.PeriodEnd, e.ProfessionalWorkBlocked)).ToListAsync(ct);
    var contactQuery = db.ClientContacts.AsNoTracking().Where(c => c.FirmId == actor.FirmId && c.PracticeClientId == id);
    var contactTotal = await contactQuery.CountAsync(ct);
    var contacts = await contactQuery.OrderByDescending(c => c.Primary).ThenBy(c => c.FullName).ThenBy(c => c.Id)
      .Skip(paging.ContactPage * paging.ContactPageSize).Take(paging.ContactPageSize)
      .Select(c => new WorkspaceContact(c.Id, c.FullName, c.Email, c.Role, c.Primary)).ToListAsync(ct);
    var safetyGeneration = await db.ClientSafetyStates.AsNoTracking().Where(c => c.FirmId == actor.FirmId && c.Id == id)
      .Select(c => (long?)c.InputGeneration).SingleOrDefaultAsync(ct);
    var portalIntent = await ClientPortalService.GetIntentAsync(db, actor.FirmId, id, ct);
    var manage = new AuthorizationRequest(actor.FirmId, ClientId: id,
      RequiredRoles: ["Administrator", "Partner", "Manager", "RelationshipManager"], InternalOnly: true);
    var create = new AuthorizationRequest(actor.FirmId, ClientId: id, RequiredRoles: EngagementCreationWorkspace.DraftRoles, InternalOnly: true);
    var currentSnapshot = await AuthorizationDecision.LoadBatchAuthorizationSnapshotAsync(
      db,
      actor,
      requests.Where(r => r.ClientId.HasValue).Select(r => r.ClientId!.Value).Append(id),
      requests.Where(r => r.EngagementId.HasValue).Select(r => r.EngagementId!.Value),
      includeGrantTargets: false,
      ct);
    var currentAt = DateTimeOffset.UtcNow;
    var manageAuthorized = currentSnapshot.Authorize(actor, manage, currentAt).Succeeded;
    var canManage = safetyGeneration.HasValue && manageAuthorized;
    var canManageArchive = manageAuthorized;
    if (manageAuthorized) requests.Add(manage);
    var canCreate = currentSnapshot.Authorize(actor, create, currentAt).Succeeded;
    if (canCreate) requests.Add(create);
    foreach (var scope in requests)
      if (!currentSnapshot.Authorize(actor, scope, currentAt).Succeeded) return UnavailableClient();
    return CommandResult<ClientWorkspace>.Ok(new(client.Id, client.LegalName, client.Status,
      latestLifecycle?.EventKind == ClientLifecycleEventKinds.Archived, lastArchivedAt,
      (latestLifecycle?.Revision ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), rows, contacts, canManage,
      (safetyGeneration ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), canManageArchive, canCreate,
      client.CommercialName, client.RegistrationNumber, client.Jurisdiction, client.CreatedAt,
      new(engagementTotal, blockedTotal, contactTotal), paging, portalIntent));
  }

  private static CommandResult<ClientWorkspace> UnavailableClient() =>
    CommandResult<ClientWorkspace>.Fail(ErrorCodes.ScopeDenied, "Client unavailable.");
}
