using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

/// <summary>Proposed AuditSphere role change. AuditSphere roles never map to Entra directory roles.</summary>
public sealed record RoleAssignmentRequest(
  Guid UserId,
  string Role,
  string ScopeKind,
  Guid? ClientId = null,
  Guid? EngagementId = null,
  Guid? GroupId = null,
  DateTimeOffset? EffectiveFrom = null,
  DateTimeOffset? ExpiresAt = null,
  string Reason = "",
  Guid? ReplacesGrantId = null,
  bool ConfirmScopeExpansion = false);

public sealed record AccessLine(Guid GrantId, string Role, string ScopeKind, Guid? ClientId, Guid? EngagementId,
  Guid? GroupId, DateTimeOffset GrantedAt, DateTimeOffset? ExpiresAt, bool GroupGrant);

public sealed record RoleAssignmentPreview(
  Guid UserId,
  string UserKind,
  IReadOnlyList<AccessLine> CurrentAccess,
  AccessLine Proposed,
  IReadOnlyList<string> AddedCapabilities,
  IReadOnlyList<string> RemovedCapabilities,
  bool ScopeExpansion,
  bool ScopeReduction,
  IReadOnlyList<string> IndependenceImpact,
  IReadOnlyList<string> Warnings,
  IReadOnlyList<string> BlockingReasons);

public sealed record RoleAssignmentResult(Guid GrantId, bool GroupGrant, bool Replaced);

/// <summary>
/// Reviewed local role assignment (§10). Preserves self-elevation prevention, last-administrator
/// protection, independence holds, append-only evidence and session-epoch invalidation.
/// </summary>
public static class RoleAssignmentService
{
  private static readonly string[] GroupRoles = ["Partner", "Manager", "Staff", "FinanceManager", "FinanceReviewer"];

  /// <summary>Human-readable capability catalogue used for the before/after comparison.</summary>
  public static readonly IReadOnlyDictionary<string, string[]> RoleCapabilities = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
  {
    ["Administrator"] = ["Administer firm users, roles and scope", "Configure Microsoft 365 connection", "View firm administration and audit history"],
    ["Partner"] = ["Sign off engagement work", "Approve financial packages and releases", "Review and close review points"],
    ["Manager"] = ["Review engagement work", "Manage planning and fieldwork", "Prepare financial packages"],
    ["Staff"] = ["Prepare working papers", "Import and map source data", "Respond to review points"],
    ["RelationshipManager"] = ["Manage client relationship records", "View client acceptance status"],
    ["FinanceManager"] = ["Manage firm billing and ledger", "Prepare invoices"],
    ["FinanceReviewer"] = ["Review and approve firm finance postings"],
    ["ClientUser"] = ["Use the client portal for assigned client/engagement", "Respond to PBC requests"],
  };

  public static async Task<CommandResult<RoleAssignmentPreview>> PreviewAsync(
    IClientAccountingDbContext db, ActorContext actor, RoleAssignmentRequest request, DateTimeOffset now,
    CancellationToken ct = default)
  {
    var auth = await RoleAdministrationService.FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<RoleAssignmentPreview>.Fail(auth.ErrorCode!, auth.Message!);
    var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.UserId && x.FirmId == actor.FirmId, ct);
    if (target is null) return CommandResult<RoleAssignmentPreview>.Fail(ErrorCodes.ScopeDenied, "The target identity is unavailable.");
    return CommandResult<RoleAssignmentPreview>.Ok(await BuildPreviewAsync(db, actor, target, request, now, ct));
  }

  public static async Task<CommandResult<RoleAssignmentResult>> AssignAsync(
    IClientAccountingDbContext db, ActorContext actor, RoleAssignmentRequest request, DateTimeOffset now,
    CancellationToken ct = default, string? expectedReviewDigest = null)
  {
    var auth = await RoleAdministrationService.FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<RoleAssignmentResult>.Fail(auth.ErrorCode!, auth.Message!);
    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5 || request.Reason.Length > 1000)
      return CommandResult<RoleAssignmentResult>.Fail("roles.reason-required", "Record a reason of at least five characters for this access change.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Same firm guard as revocation and group creation: serializes last-administrator checks.
    var firmGuard = await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (firmGuard is null) return CommandResult<RoleAssignmentResult>.Fail(ErrorCodes.ScopeDenied, "The firm safety state is unavailable.");
    auth = await RoleAdministrationService.FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<RoleAssignmentResult>.Fail(auth.ErrorCode!, auth.Message!);
    var target = await db.Users.SingleOrDefaultAsync(x => x.Id == request.UserId && x.FirmId == actor.FirmId, ct);
    if (target is null || target.Disabled)
      return CommandResult<RoleAssignmentResult>.Fail(ErrorCodes.ScopeDenied, "The target identity is unavailable.");
    if (!await RoleAdministrationService.RecentGraphObservationAsync(db, target, ct))
      return CommandResult<RoleAssignmentResult>.Fail(ErrorCodes.GateBlocked, "Reverify this Microsoft identity before assigning access.");

    var preview = await BuildPreviewAsync(db, actor, target, request, now, ct);
    if (expectedReviewDigest is not null && !string.Equals(expectedReviewDigest, RoleAssignmentReviewDigest.Compute(preview, request), StringComparison.Ordinal))
      return CommandResult<RoleAssignmentResult>.Fail(ErrorCodes.StaleRevision, "The reviewed access changed. Reload and review the proposed assignment.");
    if (preview.BlockingReasons.Count > 0)
      return CommandResult<RoleAssignmentResult>.Fail("roles.blocked", preview.BlockingReasons[0]);
    if ((preview.ScopeExpansion || preview.IndependenceImpact.Count > 0) && !request.ConfirmScopeExpansion)
      return CommandResult<RoleAssignmentResult>.Fail("roles.confirmation-required",
        "This change expands access or has an independence impact. Review the preview and confirm explicitly.");

    var scopeKind = request.ScopeKind.Trim().ToUpperInvariant();
    var role = RoleAdministrationService.CanonicalRole(request.Role)!;
    var reason = request.Reason.Trim();
    var replaced = false;
    if (request.ReplacesGrantId is { } replacedId)
    {
      var old = await db.RoleGrants.SingleOrDefaultAsync(x => x.Id == replacedId && x.FirmId == actor.FirmId &&
        x.UserId == target.Id && x.RevokedAt == null, ct);
      if (old is null)
        return CommandResult<RoleAssignmentResult>.Fail(ErrorCodes.StaleRevision, "The grant being replaced changed; reload before saving.");
      if (IsFirmAdministrator(old) && !await AnotherFirmAdministratorAsync(db, actor.FirmId, old.Id, ct) &&
          !(role == "Administrator" && scopeKind == "FIRM_WIDE"))
        return CommandResult<RoleAssignmentResult>.Fail("roles.last-admin", "A verified replacement firm administrator is required before this change.");
      old.RevokedAt = now;
      db.RoleGrantChangeEvidences.Add(Evidence(actor, target.Id, old.Id, "REVOKED", old.Role, old.ClientId, old.EngagementId,
        "REVOKED", null, null, "ADMIN_ACTION", reason, now));
      replaced = true;
    }

    Guid grantId;
    if (scopeKind == "GROUP")
    {
      var existing = await db.GroupAccessGrants.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.GroupId == request.GroupId && x.UserId == target.Id && x.Role == role && x.RevokedAt == null, ct);
      if (existing is not null)
      {
        if (replaced) await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CommandResult<RoleAssignmentResult>.Ok(new(existing.Id, true, replaced));
      }
      var groupGrant = new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = request.GroupId!.Value, UserId = target.Id,
        Role = role, GrantedAt = now, GrantedByUserId = actor.UserId
      };
      db.GroupAccessGrants.Add(groupGrant);
      grantId = groupGrant.Id;
    }
    else
    {
      var existing = await db.RoleGrants.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.UserId == target.Id &&
        x.RevokedAt == null && x.Role == role && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId, ct);
      if (existing is not null)
      {
        if (replaced) { target.SessionEpoch++; await db.SaveChangesAsync(ct); }
        await tx.CommitAsync(ct);
        return CommandResult<RoleAssignmentResult>.Ok(new(existing.Id, false, replaced));
      }
      var grant = new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = target.Id, Role = role,
        ClientId = request.ClientId, EngagementId = request.EngagementId, GrantedAt = now,
        GrantedByUserId = actor.UserId, ExpiresAt = request.ExpiresAt, Reason = reason
      };
      db.RoleGrants.Add(grant);
      db.RoleGrantChangeEvidences.Add(Evidence(actor, target.Id, grant.Id, "GRANTED", string.Empty, null, null,
        role, request.ClientId, request.EngagementId, "ADMIN_ACTION", reason, now));
      grantId = grant.Id;
    }
    target.SessionEpoch++;
    TenantAdministration.AddEvent(db, actor, replaced ? "ROLE_REPLACED" : "ROLE_GRANTED", now,
      oldState: string.Join("; ", preview.CurrentAccess.Select(Describe)).DefaultIfEmpty("NONE"),
      newState: Describe(preview.Proposed), reason: reason, result: "APPLIED",
      targetTenantId: target.TenantId, targetObjectId: target.Subject, targetUserId: target.Id,
      roleScopeChange: $"{(preview.ScopeExpansion ? "expansion" : "no-expansion")};{(preview.ScopeReduction ? "reduction" : "no-reduction")}");
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<RoleAssignmentResult>.Ok(new(grantId, scopeKind == "GROUP", replaced));
  }

  /// <summary>Revokes a group-scoped grant with evidence and immediate session invalidation.</summary>
  public static async Task<CommandResult> RevokeGroupGrantAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid groupGrantId, string reason, DateTimeOffset now,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason)) return CommandResult.Fail("roles.reason-required", "A reason is required.");
    var auth = await RoleAdministrationService.FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var grant = await db.GroupAccessGrants.SingleOrDefaultAsync(x => x.Id == groupGrantId && x.FirmId == actor.FirmId, ct);
    if (grant is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The group grant is unavailable.");
    if (grant.RevokedAt is not null) return CommandResult.Ok();
    var target = await db.Users.SingleAsync(x => x.Id == grant.UserId && x.FirmId == actor.FirmId, ct);
    grant.RevokedAt = now;
    target.SessionEpoch++;
    TenantAdministration.AddEvent(db, actor, "GROUP_ROLE_REVOKED", now, oldState: $"{grant.Role}@GROUP:{grant.GroupId:D}",
      newState: "REVOKED", reason: reason, result: "APPLIED", targetTenantId: target.TenantId,
      targetObjectId: target.Subject, targetUserId: target.Id, roleScopeChange: "reduction");
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<RoleAssignmentPreview> BuildPreviewAsync(
    IClientAccountingDbContext db, ActorContext actor, AppUser target, RoleAssignmentRequest request,
    DateTimeOffset now, CancellationToken ct)
  {
    var blocking = new List<string>();
    var warnings = new List<string>();
    var independence = new List<string>();
    var scopeKind = (request.ScopeKind ?? string.Empty).Trim().ToUpperInvariant();
    var role = RoleAdministrationService.CanonicalRole(request.Role);
    if (role is null) blocking.Add("Choose a supported AuditSphere application role.");
    if (scopeKind is not ("FIRM_WIDE" or "CLIENT" or "ENGAGEMENT" or "GROUP"))
      blocking.Add("Choose an explicit firm, client, engagement or group scope.");
    else if (scopeKind == "FIRM_WIDE" && (request.ClientId.HasValue || request.EngagementId.HasValue || request.GroupId.HasValue) ||
        scopeKind == "CLIENT" && (!request.ClientId.HasValue || request.EngagementId.HasValue || request.GroupId.HasValue) ||
        scopeKind == "ENGAGEMENT" && (!request.ClientId.HasValue || !request.EngagementId.HasValue || request.GroupId.HasValue) ||
        scopeKind == "GROUP" && (!request.GroupId.HasValue || request.ClientId.HasValue || request.EngagementId.HasValue))
      blocking.Add("The selected scope and identifiers do not match.");
    if (target.Disabled) blocking.Add("The target identity is disabled.");
    if (target.Id == actor.UserId && request.ReplacesGrantId is null)
      blocking.Add("Another authorized administrator must approve a new role or scope for the acting administrator.");
    if (target.Id == actor.UserId && request.ReplacesGrantId is not null)
      blocking.Add("Another authorized administrator must perform this privileged self-change.");
    if (role is not null && scopeKind is "FIRM_WIDE" or "CLIENT" or "ENGAGEMENT" &&
        RoleAdministrationService.ValidateRoleForUser(target, role, scopeKind) is { } roleError)
      blocking.Add(roleError);
    if (role is not null && scopeKind == "GROUP" && (!GroupRoles.Contains(role) || target.UserKind != "Staff"))
      blocking.Add("Group scope accepts staff roles Partner, Manager, Staff, FinanceManager or FinanceReviewer only.");
    if (request.EffectiveFrom is { } from && from > now.AddMinutes(1))
      blocking.Add("Future-dated access is not supported; access starts when the change is approved.");
    if (request.ExpiresAt is { } expiry && expiry <= now.AddMinutes(5))
      blocking.Add("An expiry must be at least five minutes in the future.");
    if (request.ExpiresAt.HasValue && scopeKind == "GROUP")
      blocking.Add("Group-scoped grants do not support an expiry; revoke them explicitly.");
    if (request.ClientId.HasValue && !await db.PracticeClients.AsNoTracking().AnyAsync(x => x.Id == request.ClientId && x.FirmId == actor.FirmId, ct))
      blocking.Add("The client scope is unavailable.");
    if (request.EngagementId.HasValue && !await db.Engagements.AsNoTracking().AnyAsync(x =>
          x.Id == request.EngagementId && x.FirmId == actor.FirmId && x.PracticeClientId == request.ClientId, ct))
      blocking.Add("The engagement scope is unavailable.");
    if (request.GroupId.HasValue && !await db.ClientGroups.AsNoTracking().AnyAsync(x => x.Id == request.GroupId && x.FirmId == actor.FirmId, ct))
      blocking.Add("The group scope is unavailable.");

    var roleGrants = await db.RoleGrants.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.UserId == target.Id && x.RevokedAt == null).ToListAsync(ct);
    var groupGrants = await db.GroupAccessGrants.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.UserId == target.Id && x.RevokedAt == null).ToListAsync(ct);
    var current = roleGrants.Select(x => new AccessLine(x.Id, x.Role, ScopeOf(x), x.ClientId, x.EngagementId, null,
        x.GrantedAt, x.ExpiresAt, false))
      .Concat(groupGrants.Select(x => new AccessLine(x.Id, x.Role, "GROUP", null, null, x.GroupId, x.GrantedAt, null, true)))
      .ToList();
    if (request.ReplacesGrantId is { } replaces && !roleGrants.Any(x => x.Id == replaces))
      blocking.Add("The grant being replaced is no longer active.");
    var remaining = current.Where(x => x.GrantId != request.ReplacesGrantId).ToList();
    var proposed = new AccessLine(Guid.Empty, role ?? request.Role, scopeKind, request.ClientId, request.EngagementId,
      request.GroupId, now, request.ExpiresAt, scopeKind == "GROUP");

    var before = current.SelectMany(x => Capabilities(x.Role)).ToHashSet(StringComparer.Ordinal);
    var after = remaining.Append(proposed).SelectMany(x => Capabilities(x.Role)).ToHashSet(StringComparer.Ordinal);
    var removedLine = current.FirstOrDefault(x => x.GrantId == request.ReplacesGrantId);
    // Replacing a grant with a narrower one is not an expansion; anything not already covered is.
    var expansion = !remaining.Any(x => Covers(x, proposed)) && !(removedLine is not null && Covers(removedLine, proposed));
    var reduction = removedLine is not null && !Covers(proposed, removedLine);
    if (removedLine is not null && IsFirmAdministrator(removedLine) &&
        !(proposed.Role == "Administrator" && proposed.ScopeKind == "FIRM_WIDE") &&
        !await AnotherFirmAdministratorAsync(db, actor.FirmId, removedLine.GrantId, ct))
      blocking.Add("A verified replacement firm administrator is required before this change.");

    if (role is not null && scopeKind is "CLIENT" or "ENGAGEMENT" && role != "ClientUser" && request.ClientId.HasValue)
    {
      var clearances = await db.SpecialistClearances.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == request.ClientId && x.Area == "Independence" &&
          (x.EngagementId == null || x.EngagementId == request.EngagementId))
        .ToListAsync(ct);
      if (clearances.Any(x => x.Status == "HOLD"))
        blocking.Add("An independence hold is recorded for this client; access cannot be granted until it is released.");
      else if (clearances.Count == 0)
        independence.Add("No independence clearance is recorded for this client. Confirm the team member's independence before granting access.");
      else if (clearances.Any(x => x.Status is "PENDING" or "CONDITIONS"))
        independence.Add("Independence clearance for this client is pending or conditional.");
    }
    if (role is "FinanceReviewer" && remaining.Any(x => x.Role == "FinanceManager") ||
        role is "FinanceManager" && remaining.Any(x => x.Role == "FinanceReviewer"))
      independence.Add("Segregation of duties: this user would both prepare and review firm finance postings.");
    if (role == "Administrator")
      warnings.Add("AuditSphere Administrator is an application role only. It grants no Microsoft Entra administrator role.");
    if (target.UserKind == "Client")
      warnings.Add("Client identities are limited to client or engagement scope and never receive firm-wide access.");

    return new(target.Id, target.UserKind, current, proposed,
      after.Except(before).OrderBy(x => x).ToList(), before.Except(after).OrderBy(x => x).ToList(),
      expansion, reduction, independence, warnings, blocking);
  }

  private static bool Covers(AccessLine existing, AccessLine proposed)
  {
    if (!string.Equals(existing.Role, proposed.Role, StringComparison.OrdinalIgnoreCase)) return false;
    // Group access is always explicit (AuthorizeGroupAsync); firm-wide grants never imply it.
    if (existing.ScopeKind == "FIRM_WIDE") return proposed.ScopeKind != "GROUP";
    if (proposed.ScopeKind == "FIRM_WIDE") return false;
    if (existing.ScopeKind == "GROUP" || proposed.ScopeKind == "GROUP")
      return existing.ScopeKind == proposed.ScopeKind && existing.GroupId == proposed.GroupId;
    if (existing.ScopeKind == "CLIENT")
      return existing.ClientId == proposed.ClientId;
    return proposed.ScopeKind == "ENGAGEMENT" && existing.EngagementId == proposed.EngagementId;
  }

  private static IEnumerable<string> Capabilities(string role) =>
    RoleCapabilities.TryGetValue(role, out var caps) ? caps : [];

  private static string ScopeOf(RoleGrant grant) =>
    grant.EngagementId.HasValue ? "ENGAGEMENT" : grant.ClientId.HasValue ? "CLIENT" : "FIRM_WIDE";

  public static string Describe(AccessLine line) => line.ScopeKind switch
  {
    "FIRM_WIDE" => $"{line.Role}@FIRM_WIDE",
    "CLIENT" => $"{line.Role}@CLIENT:{line.ClientId:D}",
    "ENGAGEMENT" => $"{line.Role}@ENGAGEMENT:{line.EngagementId:D}",
    "GROUP" => $"{line.Role}@GROUP:{line.GroupId:D}",
    _ => line.Role
  };

  private static bool IsFirmAdministrator(RoleGrant grant) =>
    grant.Role.Equals("Administrator", StringComparison.OrdinalIgnoreCase) && grant.ClientId is null && grant.EngagementId is null;

  private static bool IsFirmAdministrator(AccessLine line) =>
    line.Role.Equals("Administrator", StringComparison.OrdinalIgnoreCase) && line.ScopeKind == "FIRM_WIDE";

  private static Task<bool> AnotherFirmAdministratorAsync(IAuditSphereDbContext db, Guid firmId, Guid excludingGrantId,
    CancellationToken ct) =>
    db.RoleGrants.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.Role == "Administrator" &&
      x.ClientId == null && x.EngagementId == null && x.RevokedAt == null && x.Id != excludingGrantId &&
      db.Users.Any(u => u.Id == x.UserId && !u.Disabled), ct);

  internal static RoleGrantChangeEvidence Evidence(ActorContext actor, Guid targetUserId, Guid grantId, string action,
    string priorRole, Guid? priorClient, Guid? priorEngagement, string newRole, Guid? newClient, Guid? newEngagement,
    string source, string? reason, DateTimeOffset now) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TargetUserId = targetUserId, RoleGrantId = grantId,
    Action = action, PriorRole = priorRole, PriorClientId = priorClient, PriorEngagementId = priorEngagement,
    NewRole = newRole, NewClientId = newClient, NewEngagementId = newEngagement, Source = source,
    Reason = reason, ActorUserId = actor.UserId, CreatedAt = now
  };
}

file static class PreviewExtensions
{
  public static string DefaultIfEmpty(this string value, string fallback) =>
    string.IsNullOrWhiteSpace(value) ? fallback : value;
}
