using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkspaceEngagement(Guid Id, string ServiceRoute, string Status,
  string PeriodStart, string PeriodEnd, bool ProfessionalWorkBlocked);
public sealed record ClientWorkspace(Guid Id, string Name, string Status,
  IReadOnlyList<WorkspaceEngagement> Engagements, IReadOnlyList<WorkspaceContact> Contacts, bool CanManageContacts, string SafetyGeneration,
  bool CanCreateEngagement, string? CommercialName, string? RegistrationNumber, string? Jurisdiction,
  DateTimeOffset CreatedAt, ClientWorkspaceMetrics Metrics, ClientWorkspacePaging Paging,
  AuditSphereOps.Application.Documents.ClientPortalIntentView? PortalIntent);
public sealed record ClientWorkspaceMetrics(int Engagements, int WorkBlocked, int Contacts);
public sealed record ClientWorkspacePaging(int EngagementPage = 0, int EngagementPageSize = 10,
  int ContactPage = 0, int ContactPageSize = 10);
public sealed record WorkspaceContact(Guid Id, string Name, string Email, string Role, bool Primary);
public sealed record WorkspaceHold(string Kind, string Reason, bool Released, DateTimeOffset CreatedAt, DateTimeOffset? ReleasedAt);
public sealed record EngagementWorkspace(Guid Id, Guid ClientId, string ClientName, string ServiceRoute,
  string Status, string PeriodStart, string PeriodEnd, string Generation, bool ProfessionalWorkBlocked,
  IReadOnlyList<WorkspaceHold> Holds, bool CanActivate);

/// <summary>Explicitly authorized projections; engagement scope never grants a client-wide profile.</summary>
public static partial class WorkspaceQuery
{
  private static readonly string[] ClientRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "CommercialManager", "EngagementLeader"];
  private static readonly string[] EngagementRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"];

  public static async Task<CommandResult<EngagementWorkspace>> EngagementAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, EngagementId: id, RequiredRoles: EngagementRoles, InternalOnly: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<EngagementWorkspace>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");
    var row = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.FirmId == actor.FirmId && e.Id == id, ct);
    if (row is null) return CommandResult<EngagementWorkspace>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");
    var clientName = await db.PracticeClients.AsNoTracking().Where(c => c.FirmId == actor.FirmId && c.Id == row.PracticeClientId)
      .Select(c => c.LegalName).SingleOrDefaultAsync(ct);
    if (clientName is null) return CommandResult<EngagementWorkspace>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");
    var holds = await db.EngagementHolds.AsNoTracking().Where(h => h.FirmId == actor.FirmId && h.EngagementId == id)
      .OrderByDescending(h => h.CreatedAt).Take(100)
      .Select(h => new WorkspaceHold(h.HoldKind, h.Reason, h.Released, h.CreatedAt, h.ReleasedAt)).ToListAsync(ct);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<EngagementWorkspace>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");
    var canActivate = row.Status == "Draft" && (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: id, RequiredRoles: ["Partner"], InternalOnly: true), ct)).Succeeded;
    return CommandResult<EngagementWorkspace>.Ok(new(row.Id, row.PracticeClientId, clientName, row.ServiceRoute,
      row.Status, row.PeriodStart, row.PeriodEnd, row.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
      row.ProfessionalWorkBlocked, holds, canActivate));
  }

}
