using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
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
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return UnavailableClient();
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(c => c.FirmId == actor.FirmId && c.Id == id, ct);
    if (client is null) return UnavailableClient();

    // Capture contributing scope requests once. Reauthorization later refuses a mixed old/new view.
    var grants = await db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId && g.UserId == actor.UserId &&
      g.RevokedAt == null && EngagementRoles.Contains(g.Role) && (g.ClientId == null || g.ClientId == id))
      .Select(g => new { g.ClientId, g.EngagementId }).Distinct().ToListAsync(ct);
    var requests = new List<AuthorizationRequest> { request };
    var engagementIds = new List<Guid>();
    var allEngagements = false;
    foreach (var grant in grants)
    {
      var scope = new AuthorizationRequest(actor.FirmId, grant.ClientId, grant.EngagementId, EngagementRoles,
        InternalOnly: true, RequireFirmWide: grant.ClientId is null && grant.EngagementId is null);
      if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, scope, ct)).Succeeded) continue;
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
    var canManage = safetyGeneration.HasValue && (await AuthorizationDecision.AuthorizeAsync(db, actor, manage, ct)).Succeeded;
    if (canManage) requests.Add(manage);
    var create = new AuthorizationRequest(actor.FirmId, ClientId: id, RequiredRoles: ["Partner", "Manager"], InternalOnly: true);
    var canCreate = (await AuthorizationDecision.AuthorizeAsync(db, actor, create, ct)).Succeeded;
    if (canCreate) requests.Add(create);
    foreach (var scope in requests)
      if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, scope, ct)).Succeeded) return UnavailableClient();
    return CommandResult<ClientWorkspace>.Ok(new(client.Id, client.LegalName, client.Status, rows, contacts, canManage,
      (safetyGeneration ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), canCreate,
      client.CommercialName, client.RegistrationNumber, client.Jurisdiction, client.CreatedAt,
      new(engagementTotal, blockedTotal, contactTotal), paging, portalIntent));
  }

  private static CommandResult<ClientWorkspace> UnavailableClient() =>
    CommandResult<ClientWorkspace>.Fail(ErrorCodes.ScopeDenied, "Client unavailable.");
}
