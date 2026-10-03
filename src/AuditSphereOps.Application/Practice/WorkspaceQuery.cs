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
public sealed record WorkspaceHold(string Kind, string Reason, bool Released, DateTimeOffset CreatedAt, DateTimeOffset? ReleasedAt,
  Guid Id = default);
public sealed record EngagementHoldMetrics(int Total, int Active, int Released);
public sealed record EngagementWorkspacePaging(int HoldPage = 0, int HoldPageSize = 10);
public sealed record EngagementWorkspace(Guid Id, Guid ClientId, string ClientName, string ServiceRoute,
  string Status, string PeriodStart, string PeriodEnd, string Generation, bool ProfessionalWorkBlocked,
  IReadOnlyList<WorkspaceHold> Holds, bool CanActivate, string ServiceProfileId, DateTimeOffset CreatedAt,
  bool CanViewClientProfile, EngagementHoldMetrics HoldMetrics, EngagementWorkspacePaging Paging);

/// <summary>Explicitly authorized projections; engagement scope never grants a client-wide profile.</summary>
public static partial class WorkspaceQuery
{
  private static readonly string[] ClientRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "CommercialManager", "EngagementLeader"];
  private static readonly string[] EngagementRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"];

}
