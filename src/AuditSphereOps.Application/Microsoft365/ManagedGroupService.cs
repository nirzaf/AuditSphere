using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record ChangeGroupMembershipRequest(
  string IdempotencyKey, Guid ManagedGroupId, Guid UserId, bool Add, string Reason);

public sealed record GroupMembershipChangeResult(Guid? OperationId, string State, bool ExistingMembership,
  string Message, string? CorrelationId);

/// <summary>
/// Bounded administration of allowlisted AuditSphere-managed Microsoft groups (§11). Group membership
/// is a Microsoft collaboration convenience only; it is never a source of AuditSphere authorization.
/// Role-assignable and dynamic groups are refused, so this path cannot escalate Entra privilege.
/// </summary>
public static class ManagedGroupService
{
  public static async Task<CommandResult<Guid>> ApproveGroupAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftGroupMembershipProvider groups,
    TenantAdministrationOptions options, string groupObjectId, string purpose, string reason, DateTimeOffset now,
    CancellationToken ct = default)
  {
    if (!Guid.TryParse(groupObjectId, out var groupGuid) || string.IsNullOrWhiteSpace(purpose) || purpose.Length > 300 ||
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 1000 || !Guid.TryParse(options.TenantId, out var tenant))
      return CommandResult<Guid>.Fail("m365.group.invalid", "A group object ID, purpose and reason are required.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
    var gate = await TenantCapabilityService.RequireVerifiedAsync(db, actor.FirmId, options, Microsoft365Capabilities.GroupMembership, now, ct);
    if (!gate.Succeeded || !groups.IsConfigured) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, gate.Message ?? "Group administration is not configured.");
    var tenantId = tenant.ToString("D");
    DirectoryGroupRecord group;
    try { group = await groups.GetGroupAsync(tenantId, groupGuid.ToString("D"), ct); }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Microsoft could not return this group.");
    }
    if (!string.Equals(group.ObjectId, groupGuid.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(group.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The Microsoft group identity did not match.");
    if (group.IsAssignableToRole)
      return CommandResult<Guid>.Fail("m365.group.privileged", "Role-assignable groups grant Entra administrator roles and cannot be managed by AuditSphere.");
    if (group.DynamicMembership)
      return CommandResult<Guid>.Fail("m365.group.dynamic", "Dynamic-membership groups cannot be managed manually.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
    var objectId = groupGuid.ToString("D");
    var existing = await db.ManagedDirectoryGroups.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.TenantId == tenantId && x.GroupObjectId == objectId && x.RetiredAt == null, ct);
    if (existing is not null) { await tx.CommitAsync(ct); return CommandResult<Guid>.Ok(existing.Id); }
    var managed = new ManagedDirectoryGroup
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TenantId = tenantId, GroupObjectId = objectId,
      DisplayName = string.IsNullOrWhiteSpace(group.DisplayName) ? objectId : group.DisplayName.Trim(),
      Purpose = purpose.Trim(), ApprovalReason = reason.Trim(), ApprovedByUserId = actor.UserId, ApprovedAt = now
    };
    db.ManagedDirectoryGroups.Add(managed);
    TenantAdministration.AddEvent(db, actor, "MANAGED_GROUP_APPROVED", now, oldState: "NOT_MANAGED",
      newState: "MANAGED", reason: reason, result: "APPLIED", targetTenantId: tenantId, targetObjectId: objectId);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(managed.Id);
  }

  public static async Task<CommandResult> RetireGroupAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid managedGroupId, string reason, DateTimeOffset now, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5) return CommandResult.Fail("m365.group.invalid", "A reason is required.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied();
    var group = await db.ManagedDirectoryGroups.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == managedGroupId, ct);
    if (group is null) return TenantAdministration.Denied();
    if (group.RetiredAt is null)
    {
      group.RetiredAt = now;
      group.RetiredByUserId = actor.UserId;
      TenantAdministration.AddEvent(db, actor, "MANAGED_GROUP_RETIRED", now, oldState: "MANAGED", newState: "RETIRED",
        reason: reason, result: "APPLIED", targetTenantId: group.TenantId, targetObjectId: group.GroupObjectId);
      await db.SaveChangesAsync(ct);
    }
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<DirectoryGroupMemberPage>> ListMembersAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftGroupMembershipProvider groups,
    TenantAdministrationOptions options, Guid managedGroupId, string? pageToken, DateTimeOffset now,
    CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<DirectoryGroupMemberPage>();
    var gate = await TenantCapabilityService.RequireVerifiedAsync(db, actor.FirmId, options, Microsoft365Capabilities.GroupMembership, now, ct);
    if (!gate.Succeeded || !groups.IsConfigured) return CommandResult<DirectoryGroupMemberPage>.Fail(ErrorCodes.GateBlocked, gate.Message ?? "Group administration is not configured.");
    var group = await db.ManagedDirectoryGroups.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == managedGroupId && x.RetiredAt == null, ct);
    if (group is null || pageToken is { Length: > 2048 }) return TenantAdministration.Denied<DirectoryGroupMemberPage>();
    try
    {
      var page = await groups.ListMembersAsync(group.TenantId, group.GroupObjectId, pageToken, ct);
      return page.Members.Count > 50
        ? CommandResult<DirectoryGroupMemberPage>.Fail(ErrorCodes.GateBlocked, "The group member page could not be verified.")
        : CommandResult<DirectoryGroupMemberPage>.Ok(page);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return CommandResult<DirectoryGroupMemberPage>.Fail(ErrorCodes.GateBlocked, "Microsoft could not return the group members.");
    }
  }

  public static async Task<CommandResult<GroupMembershipChangeResult>> ChangeMembershipAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftGroupMembershipProvider groups,
    TenantAdministrationOptions options, ChangeGroupMembershipRequest request, DateTimeOffset now,
    CancellationToken ct = default)
  {
    if (!DirectoryProvisioningService.ValidKey(request.IdempotencyKey) || string.IsNullOrWhiteSpace(request.Reason) ||
        request.Reason.Trim().Length is < 5 or > 1000)
      return Fail("m365.group.invalid", "A request key and a reason are required.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<GroupMembershipChangeResult>();
    var gate = await TenantCapabilityService.RequireVerifiedAsync(db, actor.FirmId, options, Microsoft365Capabilities.GroupMembership, now, ct);
    if (!gate.Succeeded || !groups.IsConfigured) return Fail(ErrorCodes.GateBlocked, gate.Message ?? "Group administration is not configured.");
    var group = await db.ManagedDirectoryGroups.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.ManagedGroupId && x.RetiredAt == null, ct);
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == request.UserId, ct);
    if (group is null || user is null) return TenantAdministration.Denied<GroupMembershipChangeResult>();
    if (!string.Equals(user.TenantId, group.TenantId, StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(user.Subject, out var member))
      return Fail(ErrorCodes.GateBlocked, "Only identities bound from this Microsoft tenant can be group members.");
    if (request.Add && user.Disabled)
      return Fail(ErrorCodes.GateBlocked, "A disabled AuditSphere identity cannot be added to a group.");
    var memberId = member.ToString("D");

    bool existing;
    try { existing = await groups.IsMemberAsync(group.TenantId, group.GroupObjectId, memberId, ct); }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return Fail(ErrorCodes.GateBlocked, "Microsoft could not confirm the current membership. Nothing was changed.");
    }
    var kind = request.Add ? ExternalOperationKinds.AddGroupMember : ExternalOperationKinds.RemoveGroupMember;
    if (existing == request.Add)
    {
      await using var noop = await db.Database.BeginTransactionAsync(ct);
      TenantAdministration.AddEvent(db, actor, kind + "_NO_CHANGE", now,
        oldState: existing ? "MEMBER" : "NOT_MEMBER", newState: existing ? "MEMBER" : "NOT_MEMBER",
        reason: request.Reason, result: "NO_CHANGE", targetTenantId: group.TenantId, targetObjectId: memberId,
        targetUserId: user.Id, roleScopeChange: $"group:{group.GroupObjectId}");
      await db.SaveChangesAsync(ct);
      await noop.CommitAsync(ct);
      return CommandResult<GroupMembershipChangeResult>.Ok(new(null, "NO_CHANGE", existing,
        request.Add ? "The user is already a member." : "The user is not a member.", null));
    }

    var fingerprint = TenantAdministration.Fingerprint(kind, group.TenantId, group.GroupObjectId, memberId);
    var start = await DirectoryProvisioningService.StartOperationAsync(db, actor, request.IdempotencyKey, kind, fingerprint,
      group.TenantId, $"{group.GroupObjectId}/{memberId}", user.DisplayName, request.Reason.Trim(), null, null, null, null,
      group.Id, now, ct, memberObjectId: memberId);
    if (!start.Succeeded) return Fail(start.ErrorCode!, start.Message!);
    var operation = start.Value!;
    if (operation.State != ExternalOperationStates.Authorized)
      return CommandResult<GroupMembershipChangeResult>.Ok(new(operation.Id, operation.State, existing,
        "This request was already submitted; reconcile it from the operations list if its result is unknown.", operation.ProviderCorrelationId));
    if (!await DirectoryProvisioningService.MarkDispatchingAsync(db, actor, operation.Id, now, ct))
      return TenantAdministration.Denied<GroupMembershipChangeResult>();
    ProviderMutationResult outcome;
    try
    {
      outcome = request.Add
        ? await groups.AddMemberAsync(group.TenantId, group.GroupObjectId, memberId, ct)
        : await groups.RemoveMemberAsync(group.TenantId, group.GroupObjectId, memberId, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      outcome = ProviderMutationResult.Unknown();
    }
    // Membership mutations have no new object; an accepted result keeps the member's object ID.
    var normalized = outcome.Outcome == ProviderOutcomes.Accepted ? outcome with { ObjectId = memberId } : outcome;
    var state = DirectoryProvisioningService.OutcomeState(normalized);
    await DirectoryProvisioningService.RecordOutcomeAsync(db, actor, operation.Id, state, memberId,
      outcome.CorrelationId, outcome.ErrorCode, null, now, ct);
    return CommandResult<GroupMembershipChangeResult>.Ok(new(operation.Id, state, existing, state switch
    {
      ExternalOperationStates.Accepted => request.Add ? "Microsoft added the member." : "Microsoft removed the member.",
      ExternalOperationStates.Failed => "Microsoft rejected the membership change.",
      _ => "Microsoft did not confirm the result. Reconcile before retrying."
    }, outcome.CorrelationId));
  }

  private static CommandResult<GroupMembershipChangeResult> Fail(string code, string message) =>
    CommandResult<GroupMembershipChangeResult>.Fail(code, message);
}
