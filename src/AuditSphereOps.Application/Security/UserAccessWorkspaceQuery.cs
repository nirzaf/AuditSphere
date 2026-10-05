using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

public sealed record UserAccessRow(
  Guid UserId, string DisplayName, string MicrosoftIdentity, string TenantId, string ObjectId,
  string AccountType, string DirectoryStatus, string AuditSphereStatus, IReadOnlyList<AccessLine> Access,
  Guid? InvitationId, string InvitationStatus, DateTimeOffset? LastMicrosoftVerification, DateTimeOffset? LastAuditSphereAccess,
  string CreatedBy, DateTimeOffset CreatedDate);

public sealed record AccessHistoryRow(DateTimeOffset At, string Operation, string Target, string Change,
  string Reason, string Actor, string Result, string? MicrosoftOperation);

public sealed record AccessGrantEvidenceRow(Guid GrantId, DateTimeOffset At, string Action, string PriorRole,
  string NewRole, string? Reason, string Actor, string Source);

public sealed record ManagedGroupRow(Guid Id, string DisplayName, string GroupObjectId, string Purpose,
  DateTimeOffset ApprovedAt, string ApprovedBy);

public sealed record ExternalOperationRow(Guid Id, string Kind, string State, string Target, string Reason,
  string? ResultObjectId, string? CorrelationId, string? ResultCode, string? Reconciliation, string RequestedBy,
  DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ScopeOption(Guid Id, string Name, Guid? ParentId = null);

public sealed record UserAccessWorkspace(
  IReadOnlyList<UserAccessRow> Users,
  IReadOnlyList<UserAccessRow> DisabledOrRevoked,
  IReadOnlyList<ExternalOperationRow> Operations,
  IReadOnlyList<ManagedGroupRow> Groups,
  IReadOnlyList<AccessHistoryRow> History,
  IReadOnlyList<AccessGrantEvidenceRow> GrantHistory,
  IReadOnlyList<ScopeOption> Clients,
  IReadOnlyList<ScopeOption> Engagements,
  IReadOnlyList<ScopeOption> ClientGroups);

/// <summary>
/// Firm-wide Administrator projection for Users &amp; Access. It exposes only fields required for
/// administration (no phone, address, manager or other directory profile data).
/// </summary>
public static class UserAccessWorkspaceQuery
{
  private static readonly TimeSpan DirectoryStaleAfter = TimeSpan.FromDays(30);

  public static async Task<CommandResult<UserAccessWorkspace>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, DateTimeOffset now, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<UserAccessWorkspace>();
    var firmId = actor.FirmId;
    var users = await db.Users.AsNoTracking().Where(x => x.FirmId == firmId).OrderBy(x => x.DisplayName).Take(2000).ToListAsync(ct);
    var names = users.ToDictionary(x => x.Id, x => x.DisplayName);
    string Name(Guid id) => id == RoleGrantExpiry.SystemActor ? "System (expiry)" : names.TryGetValue(id, out var n) ? n : "Unknown";
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == firmId).ToListAsync(ct);
    var groupGrants = await db.GroupAccessGrants.AsNoTracking().Where(x => x.FirmId == firmId).ToListAsync(ct);
    var invitations = await db.UserAccessInvitations.AsNoTracking().Where(x => x.FirmId == firmId).ToListAsync(ct);
    var observations = await db.DirectoryUserObservations.AsNoTracking().Where(x => x.FirmId == firmId)
      .Select(x => new { x.TenantId, x.ObjectId, x.EnabledState, x.UserType, x.Source, x.ObservedAt }).ToListAsync(ct);
    var latestObservation = observations.GroupBy(x => (x.TenantId, x.ObjectId))
      .ToDictionary(x => x.Key, x => x.OrderByDescending(o => o.ObservedAt).First());

    UserAccessRow Row(Domain.Security.AppUser user)
    {
      var active = grants.Where(x => x.UserId == user.Id && x.RevokedAt == null)
        .Select(x => new AccessLine(x.Id, x.Role, x.EngagementId.HasValue ? "ENGAGEMENT" : x.ClientId.HasValue ? "CLIENT" : "FIRM_WIDE",
          x.ClientId, x.EngagementId, null, x.GrantedAt, x.ExpiresAt, false))
        .Concat(groupGrants.Where(x => x.UserId == user.Id && x.RevokedAt == null)
          .Select(x => new AccessLine(x.Id, x.Role, "GROUP", null, null, x.GroupId, x.GrantedAt, null, true)))
        .ToList();
      latestObservation.TryGetValue((user.TenantId, user.Subject), out var observation);
      var directory = observation is null ? "NOT_OBSERVED" :
        observation.EnabledState == "DISABLED" ? "DISABLED" :
        observation.ObservedAt < now - DirectoryStaleAfter ? "STALE" : observation.EnabledState;
      var invitation = invitations.Where(x => x.UserId == user.Id).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
      var invitationGrantActive = invitation is not null && active.Any(x => x.GrantId == invitation.RoleGrantId);
      return new UserAccessRow(user.Id, user.DisplayName, user.Email, user.TenantId, user.Subject,
        user.UserKind == "Client" ? $"Client ({observation?.UserType ?? "Guest"})" : $"Staff ({observation?.UserType ?? "Member"})",
        directory,
        user.Disabled ? "DISABLED" : active.Count == 0 ? "NO_ACCESS" : "ACTIVE",
        active,
        invitation is null || invitation.FirstAccessAt is not null || !invitationGrantActive ? null : invitation.Id,
        invitation is null ? "NONE" : invitation.FirstAccessAt is not null ? "ACCEPTED" : invitationGrantActive ? invitation.DeliveryState : "REVOKED",
        observation?.ObservedAt, user.LastSignInAt,
        user.CreatedByUserId is { } creator ? Name(creator) : "Bootstrap / roster", user.CreatedAt);
    }

    var rows = users.Select(Row).ToList();
    var history = await db.RoleGrantChangeEvidences.AsNoTracking().Where(x => x.FirmId == firmId)
      .OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct);
    var events = await db.Microsoft365AdministrationEvents.AsNoTracking().Where(x => x.FirmId == firmId)
      .OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct);
    var operations = await db.Microsoft365ExternalOperations.AsNoTracking().Where(x => x.FirmId == firmId)
      .OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
    var groups = await db.ManagedDirectoryGroups.AsNoTracking().Where(x => x.FirmId == firmId && x.RetiredAt == null)
      .OrderBy(x => x.DisplayName).ToListAsync(ct);
    var clients = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == firmId).OrderBy(x => x.LegalName)
      .Select(x => new ScopeOption(x.Id, x.LegalName, null)).Take(2000).ToListAsync(ct);
    var engagements = await db.Engagements.AsNoTracking().Where(x => x.FirmId == firmId)
      .Select(x => new { x.Id, x.PracticeClientId, x.ServiceRoute, x.PeriodEnd, x.CreatedAt }).Take(5000).ToListAsync(ct);
    var clientNames = clients.ToDictionary(x => x.Id, x => x.Name);
    var clientGroups = await db.ClientGroups.AsNoTracking().Where(x => x.FirmId == firmId).OrderBy(x => x.Name)
      .Select(x => new ScopeOption(x.Id, x.Code + " — " + x.Name, null)).ToListAsync(ct);

    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<UserAccessWorkspace>();

    var historyRows = history.Select(x => new AccessHistoryRow(x.CreatedAt, $"ROLE_{x.Action}", Name(x.TargetUserId),
        x.Action == "REVOKED" ? $"{x.PriorRole} → {x.NewRole}" : $"{x.PriorRole} → {x.NewRole}",
        x.Reason ?? x.Source, Name(x.ActorUserId), x.Source, null))
      .Concat(events.Select(x => new AccessHistoryRow(x.CreatedAt, x.Operation,
        x.TargetUserId is { } t ? Name(t) : x.TargetObjectId ?? x.TargetTenantId ?? "-",
        x.RoleScopeChange ?? $"{x.OldState} → {x.NewState}", x.Reason, Name(x.ActorUserId), x.Result,
        x.ExternalOperationId?.ToString("D"))))
      .OrderByDescending(x => x.At).Take(400).ToList();
    var grantEvidence = await db.RoleGrantChangeEvidences.AsNoTracking().Where(x => x.FirmId == firmId && x.RoleGrantId != null)
      .OrderByDescending(x => x.CreatedAt).Take(4000).ToListAsync(ct);
    var grantHistory = grantEvidence.GroupBy(x => x.RoleGrantId!.Value)
      .Select(g => g.Take(20).Select(x => new AccessGrantEvidenceRow(g.Key, x.CreatedAt, x.Action, x.PriorRole,
        x.NewRole, x.Reason, Name(x.ActorUserId), x.Source)))
      .SelectMany(x => x).ToList();
    return CommandResult<UserAccessWorkspace>.Ok(new(
      rows.Where(x => x.AuditSphereStatus != "DISABLED").ToList(),
      rows.Where(x => x.AuditSphereStatus is "DISABLED" or "NO_ACCESS" ||
        grants.Any(g => g.UserId == x.UserId && g.RevokedAt != null)).ToList(),
      operations.Select(x => new ExternalOperationRow(x.Id, x.Kind, x.State, x.TargetDescriptor, x.Reason,
        x.ResultObjectId, x.ProviderCorrelationId, x.ResultCode, x.ReconciliationResult, Name(x.RequestedByUserId),
        x.CreatedAt, x.UpdatedAt)).ToList(),
      groups.Select(x => new ManagedGroupRow(x.Id, x.DisplayName, x.GroupObjectId, x.Purpose, x.ApprovedAt, Name(x.ApprovedByUserId))).ToList(),
      historyRows, grantHistory, clients,
      engagements.OrderBy(x => clientNames.GetValueOrDefault(x.PracticeClientId)).ThenBy(x => x.CreatedAt)
        .Select(x => new ScopeOption(x.Id, $"{clientNames.GetValueOrDefault(x.PracticeClientId, "Client")} — {(string.IsNullOrWhiteSpace(x.ServiceRoute) ? "engagement" : x.ServiceRoute)} {x.PeriodEnd} ({x.Id.ToString()[..8]})", x.PracticeClientId)).ToList(),
      clientGroups));
  }
}
