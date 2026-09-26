using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ConsolidationService
{
  public static async Task<CommandResult<Guid>> CreateGroupAsync(
    IClientAccountingDbContext db, ActorContext actor, ClientGroupRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "Group code and name are required.");
    var auth = await FirmAuthAsync(db, actor, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var activeRoles = await db.RoleGrants.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId &&
                  x.ClientId == null && x.EngagementId == null && x.RevokedAt == null)
      .Select(x => x.Role).ToListAsync(ct);
    var creatorRole = new[] { "Manager", "Partner", "Administrator" }
      .FirstOrDefault(role => activeRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    if (creatorRole is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Firm-wide group authority is no longer active.");
    var code = request.Code.Trim();
    if (await db.ClientGroups.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == code, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The group code already exists.");
    var group = new ClientGroup
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Code = code, Name = request.Name.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientGroups.Add(group);
    db.GroupAccessGrants.Add(new GroupAccessGrant
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = group.Id, UserId = actor.UserId,
      Role = creatorRole, GrantedAt = group.CreatedAt, GrantedByUserId = actor.UserId
    });
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(group.Id);
  }

  public static async Task<CommandResult> AddMembershipAsync(
    IClientAccountingDbContext db, ActorContext actor, GroupMembershipRequest request,
    CancellationToken ct = default)
  {
    if (request.GroupId == Guid.Empty || request.ClientId == Guid.Empty || request.EffectiveTo < request.EffectiveFrom ||
        string.IsNullOrWhiteSpace(request.ControlMethod) || string.IsNullOrWhiteSpace(request.EvidenceReference) ||
        request.OwnershipPercent is < 0 or > 100 || request.EconomicInterestPercent is < 0 or > 100)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A group membership needs valid dates, method, ownership and evidence.");
    var auth = await GroupAuthAsync(db, actor, request.GroupId, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return auth;
    if (!await db.PracticeClients.AnyAsync(x => x.FirmId == actor.FirmId && x.Id == request.ClientId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The legal entity is outside the firm scope.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var group = await db.ClientGroups.FromSqlInterpolated($"SELECT * FROM client_groups WHERE id = {request.GroupId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (group is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var overlaps = await db.ClientGroupMemberships.AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == request.GroupId &&
      x.ClientId == request.ClientId && x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) &&
      request.EffectiveFrom <= (x.EffectiveTo ?? DateOnly.MaxValue), ct);
    if (overlaps)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Overlapping historical group memberships are not allowed.");
    db.ClientGroupMemberships.Add(new ClientGroupMembership
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = request.GroupId, ClientId = request.ClientId,
      EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, ControlMethod = request.ControlMethod.Trim().ToUpperInvariant(),
      OwnershipPercent = MoneyPolicy.Normalize(request.OwnershipPercent), EconomicInterestPercent = MoneyPolicy.Normalize(request.EconomicInterestPercent),
      EvidenceReference = request.EvidenceReference.Trim(), Status = AccountingWorkflowStates.Approved,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    group.Revision++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
