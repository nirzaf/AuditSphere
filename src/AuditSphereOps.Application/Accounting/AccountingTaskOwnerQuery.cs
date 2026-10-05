using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AccountingTaskOwnerRow(string Owner, int ActiveTaskCount);
public sealed record AccountingTaskOwnerSummary(bool HasAssignedEngagements, bool HasMoreOwners,
  IReadOnlyList<AccountingTaskOwnerRow> Owners);

/// <summary>
/// Shows a bounded owner summary for engagement tasks covered by the actor's current accounting grants.
/// This projection never grants access to client-wide books or period setup.
/// </summary>
public static class AccountingTaskOwnerQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private const int MaxEngagements = 1000;
  private const int MaxTasks = 5000;
  private const int MaxOwners = 100;

  public static async Task<CommandResult<AccountingTaskOwnerSummary>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var scopes = await ResolveEngagementsAsync(db, actor, ct);
    if (!scopes.Succeeded)
      return CommandResult<AccountingTaskOwnerSummary>.Fail(scopes.ErrorCode!, scopes.Message!);

    var engagements = scopes.Value!;
    if (engagements.Count == 0)
      return CommandResult<AccountingTaskOwnerSummary>.Ok(new(false, false, []));

    var engagementIds = engagements.Keys.ToArray();
    var candidates = await db.WorkTasks.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId.HasValue && engagementIds.Contains(x.EngagementId.Value) &&
        x.AssigneeUserId.HasValue && x.Status != PracticeTimeStates.TaskCancelled && x.Status != PracticeTimeStates.TaskCompleted)
      .OrderBy(x => x.Id).Take(MaxTasks + 1).ToListAsync(ct);
    if (candidates.Count > MaxTasks)
      return CommandResult<AccountingTaskOwnerSummary>.Fail(ErrorCodes.GateBlocked,
        "The assigned task summary exceeds its safe review limit.");

    // Engagement and client must agree on the stored task as well as the grant.
    var tasks = candidates.Where(x => x.EngagementId is { } id && x.ClientId is { } clientId &&
      engagements.TryGetValue(id, out var grantedClientId) && grantedClientId == clientId).ToArray();
    var ownerIds = tasks.Select(x => x.AssigneeUserId!.Value).Distinct().ToArray();
    var users = ownerIds.Length == 0
      ? new Dictionary<Guid, (string DisplayName, bool Disabled)>()
      : await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && ownerIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => (x.DisplayName, x.Disabled), ct);

    var owners = tasks.GroupBy(x => x.AssigneeUserId!.Value).Select(group =>
    {
      var user = users.GetValueOrDefault(group.Key);
      var name = user == default || user.Disabled || string.IsNullOrWhiteSpace(user.DisplayName)
        ? "Unavailable staff member"
        : user.DisplayName;
      return new AccountingTaskOwnerRow(name, group.Count());
    }).OrderBy(x => x.Owner, StringComparer.OrdinalIgnoreCase).ToArray();

    // Re-resolve the grant scopes so a concurrent revocation cannot return stale ownership data.
    var currentScopes = await ResolveEngagementsAsync(db, actor, ct);
    if (!currentScopes.Succeeded || !SameScopes(engagements, currentScopes.Value!))
      return CommandResult<AccountingTaskOwnerSummary>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    return CommandResult<AccountingTaskOwnerSummary>.Ok(new(true, owners.Length > MaxOwners, owners.Take(MaxOwners).ToArray()));
  }

  private static async Task<CommandResult<Dictionary<Guid, Guid>>> ResolveEngagementsAsync(
    IClientAccountingDbContext db, ActorContext actor, CancellationToken ct)
  {
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: Roles, InternalOnly: true), ct);
    if (!authorization.Succeeded)
      return CommandResult<Dictionary<Guid, Guid>>.Fail(authorization.ErrorCode!, authorization.Message!);

    var now = DateTimeOffset.UtcNow;
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId &&
      x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now) && Roles.Contains(x.Role))
      .Select(x => new { x.ClientId, x.EngagementId }).ToListAsync(ct);
    var firmWide = grants.Any(x => x.ClientId == null && x.EngagementId == null);
    var clientIds = grants.Where(x => x.ClientId.HasValue && x.EngagementId == null)
      .Select(x => x.ClientId!.Value).Distinct().ToArray();
    var exactEngagementIds = grants.Where(x => x.ClientId.HasValue && x.EngagementId.HasValue)
      .Select(x => x.EngagementId!.Value).Distinct().ToArray();

    var candidates = await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        (firmWide || clientIds.Contains(x.PracticeClientId) || exactEngagementIds.Contains(x.Id)))
      .OrderBy(x => x.Id).Take(MaxEngagements + 1).Select(x => new { x.Id, x.PracticeClientId }).ToListAsync(ct);
    if (candidates.Count > MaxEngagements)
      return CommandResult<Dictionary<Guid, Guid>>.Fail(ErrorCodes.GateBlocked,
        "The assigned engagement summary exceeds its safe review limit.");

    var allowed = candidates.Where(engagement => firmWide || clientIds.Contains(engagement.PracticeClientId) ||
      grants.Any(grant => grant.EngagementId == engagement.Id && grant.ClientId == engagement.PracticeClientId))
      .ToDictionary(x => x.Id, x => x.PracticeClientId);
    return CommandResult<Dictionary<Guid, Guid>>.Ok(allowed);
  }

  private static bool SameScopes(IReadOnlyDictionary<Guid, Guid> left, IReadOnlyDictionary<Guid, Guid> right) =>
    left.Count == right.Count && left.All(x => right.TryGetValue(x.Key, out var clientId) && clientId == x.Value);
}
