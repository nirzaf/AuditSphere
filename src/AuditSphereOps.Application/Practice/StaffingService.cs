using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record AssignStaffRequest(Guid EngagementId, Guid UserId, string StaffingLevel);

public sealed record StaffAssignmentRow(Guid AssignmentId, Guid UserId, string Name, string Level, string LevelLabel, string AuthorizationRole, bool Certified);

public sealed record StaffCandidate(Guid UserId, string Name, string? Department, bool Certified);

/// <summary>
/// Four-level engagement staffing. Each level maps explicitly to one engagement-scoped authorization role
/// (Partner, Manager, Senior, Staff) that the assignment creates and its revocation removes, with role-change
/// evidence and session invalidation. Partner and Manager levels need a current certification on the staff profile;
/// an engagement has at most one Engagement Partner; nobody staffs themselves or above their own rank.
/// </summary>
public static class StaffingService
{
  private static readonly string[] StaffingRoles = ["Partner", "Manager", "Administrator"];
  private const string StaffingGrantReason = "Engagement staffing: ";

  public static async Task<CommandResult<Guid>> AssignAsync(IAuditSphereDbContext db, ActorContext actor, AssignStaffRequest request, CancellationToken ct = default)
  {
    var level = (request.StaffingLevel ?? string.Empty).Trim().ToUpperInvariant();
    if (!StaffingLevels.All.Contains(level)) return CommandResult<Guid>.Fail("staffing.invalid", "Choose a staffing level.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.EngagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, StaffingRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.UserId == actor.UserId) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "You cannot staff yourself.");
    var actorRank = await ActorRankAsync(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (StaffingLevels.Rank(level) > actorRank)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "You cannot staff someone above your own level.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var target = await db.Users.SingleOrDefaultAsync(x => x.Id == request.UserId && x.FirmId == actor.FirmId, ct);
    if (target is null || target.Disabled || target.UserKind != "Staff")
      return CommandResult<Guid>.Fail("staffing.ineligible", "Only an enabled staff identity can be staffed.");
    var active = await db.EngagementStaffAssignments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.RevokedAt == null).ToListAsync(ct);
    if (active.SingleOrDefault(x => x.UserId == target.Id) is { } existing)
      return existing.StaffingLevel == level ? CommandResult<Guid>.Ok(existing.Id)
        : CommandResult<Guid>.Fail("staffing.conflict", "This person is already staffed at another level; revoke that assignment first.");
    if (level == StaffingLevels.EngagementPartner && active.Any(x => x.StaffingLevel == StaffingLevels.EngagementPartner))
      return CommandResult<Guid>.Fail("staffing.conflict", "The engagement already has an Engagement Partner.");
    if (StaffingLevels.Rank(level) >= 3 && !await IsCertifiedAsync(db, actor.FirmId, target.Id, ct))
      return CommandResult<Guid>.Fail("staffing.certification", "Partner and Manager levels need a current professional certification on the staff profile.");

    var now = DateTimeOffset.UtcNow;
    var role = StaffingLevels.AuthorizationRole(level);
    // An identical engagement grant made elsewhere is reused, not duplicated; staffing revocation leaves it in place.
    var grant = await db.RoleGrants.FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.UserId == target.Id && x.Role == role &&
      x.ClientId == engagement.PracticeClientId && x.EngagementId == engagement.Id && x.RevokedAt == null, ct);
    if (grant is null)
    {
      grant = new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = target.Id, Role = role, ClientId = engagement.PracticeClientId,
        EngagementId = engagement.Id, GrantedAt = now, GrantedByUserId = actor.UserId, Reason = StaffingGrantReason + StaffingLevels.Label(level)
      };
      db.RoleGrants.Add(grant);
      db.RoleGrantChangeEvidences.Add(RoleAssignmentService.Evidence(actor, target.Id, grant.Id, "GRANTED", string.Empty, null, null,
        role, engagement.PracticeClientId, engagement.Id, "ENGAGEMENT_STAFFING", grant.Reason, now));
    }
    var assignment = new EngagementStaffAssignment
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId, EngagementId = engagement.Id, UserId = target.Id,
      StaffingLevel = level, RoleGrantId = grant.Id, AssignedByUserId = actor.UserId, AssignedAt = now
    };
    db.EngagementStaffAssignments.Add(assignment);
    var site = await db.ClientSharePointSites.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId, ct);
    if (site != null) { site.LastMembershipSyncAt = null; site.MembershipState = "PENDING"; }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(assignment.Id);
  }

  public static async Task<CommandResult> RevokeAsync(IAuditSphereDbContext db, ActorContext actor, Guid assignmentId, CancellationToken ct = default)
  {
    var snapshot = await db.EngagementStaffAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assignmentId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, snapshot.ClientId, snapshot.EngagementId, StaffingRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    if (StaffingLevels.Rank(snapshot.StaffingLevel) > await ActorRankAsync(db, actor, snapshot.ClientId, snapshot.EngagementId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "You cannot revoke someone above your own level.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var assignment = await db.EngagementStaffAssignments.SingleAsync(x => x.Id == assignmentId, ct);
    if (assignment.RevokedAt is not null) return CommandResult.Ok();
    var now = DateTimeOffset.UtcNow;
    assignment.RevokedAt = now;
    assignment.RevokedByUserId = actor.UserId;
    var grant = await db.RoleGrants.SingleOrDefaultAsync(x => x.Id == assignment.RoleGrantId && x.FirmId == actor.FirmId, ct);
    if (grant is { RevokedAt: null } && grant.Reason?.StartsWith(StaffingGrantReason, StringComparison.Ordinal) == true)
    {
      grant.RevokedAt = now;
      db.RoleGrantChangeEvidences.Add(RoleAssignmentService.Evidence(actor, assignment.UserId, grant.Id, "REVOKED", grant.Role, grant.ClientId,
        grant.EngagementId, "REVOKED", null, null, "ENGAGEMENT_STAFFING", "Engagement staffing revoked", now));
      var user = await db.Users.SingleAsync(x => x.Id == assignment.UserId && x.FirmId == actor.FirmId, ct);
      user.SessionEpoch++;
    }
    var site = await db.ClientSharePointSites.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == assignment.ClientId, ct);
    if (site != null) { site.LastMembershipSyncAt = null; site.MembershipState = "PENDING"; }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<IReadOnlyList<StaffAssignmentRow>>> ListAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<IReadOnlyList<StaffAssignmentRow>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId,
      ["Partner", "Manager", "Senior", "Staff", "Administrator"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<StaffAssignmentRow>>.Fail(auth.ErrorCode!, auth.Message!);
    var rows = await db.EngagementStaffAssignments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.RevokedAt == null)
      .Join(db.Users.AsNoTracking(), a => a.UserId, u => u.Id, (a, u) => new { a, u.DisplayName }).ToListAsync(ct);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var certified = await db.StaffCertifications.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (x.ExpiresOn == null || x.ExpiresOn >= today))
      .Select(x => x.UserId).Distinct().ToListAsync(ct);
    return CommandResult<IReadOnlyList<StaffAssignmentRow>>.Ok(rows
      .OrderByDescending(x => StaffingLevels.Rank(x.a.StaffingLevel)).ThenBy(x => x.DisplayName)
      .Select(x => new StaffAssignmentRow(x.a.Id, x.a.UserId, x.DisplayName, x.a.StaffingLevel, StaffingLevels.Label(x.a.StaffingLevel),
        StaffingLevels.AuthorizationRole(x.a.StaffingLevel), certified.Contains(x.a.UserId))).ToList());
  }

  public static async Task<IReadOnlyList<StaffCandidate>> CandidatesAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    return await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserKind == "Staff" && !x.Disabled && x.Id != actor.UserId)
      .OrderBy(x => x.DisplayName)
      .Select(x => new StaffCandidate(x.Id, x.DisplayName,
        db.StaffProfiles.Where(p => p.FirmId == x.FirmId && p.UserId == x.Id).Select(p => p.Department).FirstOrDefault(),
        db.StaffCertifications.Any(c => c.FirmId == x.FirmId && c.UserId == x.Id && (c.ExpiresOn == null || c.ExpiresOn >= today))))
      .ToListAsync(ct);
  }

  /// <summary>Staffing rank of the acting user on this engagement: administrators and firm Partners rank as Partner.</summary>
  internal static async Task<int> ActorRankAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, CancellationToken ct)
  {
    var roles = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null &&
        (x.ClientId == null || x.ClientId == clientId) && (x.EngagementId == null || x.EngagementId == engagementId))
      .Select(x => x.Role).ToListAsync(ct);
    return roles.Contains("Administrator") || roles.Contains("Partner") ? 4 : roles.Contains("Manager") ? 3 : roles.Contains("Senior") ? 2 : 1;
  }

  private static async Task<bool> IsCertifiedAsync(IAuditSphereDbContext db, Guid firmId, Guid userId, CancellationToken ct)
  {
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    return await db.StaffCertifications.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.UserId == userId && (x.ExpiresOn == null || x.ExpiresOn >= today), ct);
  }
}
