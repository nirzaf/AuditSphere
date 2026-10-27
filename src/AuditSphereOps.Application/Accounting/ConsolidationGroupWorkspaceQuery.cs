using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ConsolidationGroupMemberRow(
  Guid Id, Guid ClientId, string ClientName, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
  string ControlMethod, decimal OwnershipPercent, decimal EconomicInterestPercent,
  string EvidenceReference, string Status, long Revision);

public sealed record ConsolidationEligibleClient(Guid Id, string LegalName);

public sealed record ConsolidationGroupMembershipWorkspace(
  Guid GroupId, string GroupCode, string GroupName, long GroupRevision,
  int Page, int PageSize, int Total,
  IReadOnlyList<ConsolidationGroupMemberRow> Members,
  IReadOnlyList<ConsolidationEligibleClient> EligibleClients,
  bool CanAddMember, string AccessMode = "OPERATIONAL", string? AccessReason = null);

/// <summary>
/// Reads the effective-dated legal-entity perimeter through explicit group access or audited Administrator oversight.
/// It never resolves Microsoft groups or includes clients from another firm.
/// </summary>
public static class ConsolidationGroupWorkspaceQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ManageRoles = ["Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<ConsolidationGroupMembershipWorkspace>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid groupId,
    int page = 0, int pageSize = 25, Guid? clientId = null, DateOnly? effectiveFrom = null,
    CancellationToken ct = default)
  {
    if (page < 0 || pageSize is < 1 or > 100 || (long)page * pageSize > int.MaxValue ||
        clientId.HasValue != effectiveFrom.HasValue)
      return CommandResult<ConsolidationGroupMembershipWorkspace>.Fail(
        ErrorCodes.Accounting.MappingInvalid, "Membership page or reconciliation filter is invalid.");

    var access = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, groupId, ReadRoles, ct);
    if (!access.Succeeded)
      access = await AdministratorOversightReadAuthorization.AuthorizeGroupAsync(db, actor, groupId, ct);
    if (!access.Succeeded)
      return CommandResult<ConsolidationGroupMembershipWorkspace>.Fail(access.ErrorCode!, access.Message!);

    var group = await db.ClientGroups.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == groupId, ct);
    if (group is null)
      return CommandResult<ConsolidationGroupMembershipWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var membershipQuery = from membership in db.ClientGroupMemberships.AsNoTracking()
                          join client in db.PracticeClients.AsNoTracking()
                            on new { membership.FirmId, membership.ClientId }
                            equals new { client.FirmId, ClientId = client.Id }
                          where membership.FirmId == actor.FirmId && membership.GroupId == groupId &&
                                client.FirmId == actor.FirmId
                          select new { Membership = membership, ClientName = client.LegalName };
    if (clientId.HasValue)
      membershipQuery = membershipQuery.Where(x => x.Membership.ClientId == clientId.Value &&
        x.Membership.EffectiveFrom == effectiveFrom!.Value);

    var total = await membershipQuery.CountAsync(ct);
    var skip = (int)((long)page * pageSize);
    var members = await membershipQuery
      .OrderByDescending(x => x.Membership.EffectiveFrom)
      .ThenBy(x => x.Membership.ClientId)
      .ThenBy(x => x.Membership.Id)
      .Skip(skip).Take(pageSize)
      .Select(x => new ConsolidationGroupMemberRow(
        x.Membership.Id, x.Membership.ClientId, x.ClientName,
        x.Membership.EffectiveFrom, x.Membership.EffectiveTo,
        x.Membership.ControlMethod, x.Membership.OwnershipPercent,
        x.Membership.EconomicInterestPercent, x.Membership.EvidenceReference,
        x.Membership.Status, x.Membership.Revision))
      .ToListAsync(ct);

    var manageAccess = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, groupId, ManageRoles, ct);
    List<ConsolidationEligibleClient> clients = !manageAccess.Succeeded ? [] : await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderBy(x => x.LegalName).ThenBy(x => x.Id)
      .Select(x => new ConsolidationEligibleClient(x.Id, x.LegalName))
      .ToListAsync(ct);

    var operationalRead = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, groupId, ReadRoles, ct);
    access = operationalRead.Succeeded ? operationalRead :
      await AdministratorOversightReadAuthorization.AuthorizeGroupAsync(db, actor, groupId, ct);
    if (!access.Succeeded)
      return CommandResult<ConsolidationGroupMembershipWorkspace>.Fail(access.ErrorCode!, access.Message!);
    manageAccess = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, groupId, ManageRoles, ct);
    if (!manageAccess.Succeeded) clients.Clear();
    return CommandResult<ConsolidationGroupMembershipWorkspace>.Ok(new ConsolidationGroupMembershipWorkspace(
      group.Id, group.Code, group.Name, group.Revision, page, pageSize, total, members, clients, manageAccess.Succeeded,
      operationalRead.Succeeded ? "OPERATIONAL" : AdministratorOversightReadAuthorization.ReadOnly,
      operationalRead.Succeeded ? null : AdministratorOversightReadAuthorization.Reason));
  }
}
