using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ConsolidationOwnershipMember(
  Guid ClientId, string LegalName, DateOnly MembershipEffectiveFrom);

public sealed record ConsolidationOwnershipRow(
  Guid Id, Guid ParentClientId, string ParentLegalName, Guid ChildClientId, string ChildLegalName,
  DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal OwnershipPercent, decimal EconomicInterestPercent,
  string ControlAssessment, string Method, string EvidenceReference, string Status);

public sealed record ConsolidationOwnershipWorkspace(
  Guid ScopeId, Guid GroupId, string GroupName, int ScopeVersion, long GroupRevision, long CurrentGroupRevision,
  string ScopeStatus, string ScopeMethod, bool CanAddOwnership,
  int Page, int PageSize, int Total, IReadOnlyList<ConsolidationOwnershipRow> Interests,
  IReadOnlyList<ConsolidationOwnershipMember> EligibleMembers);

/// <summary>Group-scoped ownership history and the complete bounded approved member choices.</summary>
public static class ConsolidationOwnershipWorkspaceQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ManageRoles = ["Manager", "Partner", "Administrator"];
  private const int MaximumMemberOptions = 1000;

  public static async Task<CommandResult<ConsolidationOwnershipWorkspace>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId, int page = 0, int pageSize = 25,
    Guid? parentClientId = null, Guid? childClientId = null, CancellationToken ct = default)
  {
    if (page < 0 || pageSize is < 1 or > 100 || (long)page * pageSize > int.MaxValue ||
        parentClientId.HasValue != childClientId.HasValue)
      return CommandResult<ConsolidationOwnershipWorkspace>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Ownership page or exact relationship filter is invalid.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == scopeId, ct);
    if (scope is null)
      return CommandResult<ConsolidationOwnershipWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var access = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReadRoles, ct);
    if (!access.Succeeded)
      return CommandResult<ConsolidationOwnershipWorkspace>.Fail(access.ErrorCode!, access.Message!);
    var group = await db.ClientGroups.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId, ct);
    if (group is null)
      return CommandResult<ConsolidationOwnershipWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var interests = from edge in db.OwnershipInterestVersions.AsNoTracking()
                    join parent in db.PracticeClients.AsNoTracking()
                      on new { edge.FirmId, ClientId = edge.ParentClientId }
                      equals new { parent.FirmId, ClientId = parent.Id }
                    join child in db.PracticeClients.AsNoTracking()
                      on new { edge.FirmId, ClientId = edge.ChildClientId }
                      equals new { child.FirmId, ClientId = child.Id }
                    where edge.FirmId == actor.FirmId && edge.GroupId == scope.GroupId && edge.ScopeVersionId == scope.Id
                    select new
                    {
                      edge.Id, edge.ParentClientId, ParentLegalName = parent.LegalName,
                      edge.ChildClientId, ChildLegalName = child.LegalName, edge.EffectiveFrom, edge.EffectiveTo,
                      edge.OwnershipPercent, edge.EconomicInterestPercent, edge.ControlAssessment,
                      edge.Method, edge.EvidenceReference, edge.Status
                    };
    if (parentClientId is not null)
      interests = interests.Where(x => x.ParentClientId == parentClientId.Value && x.ChildClientId == childClientId!.Value);
    var total = await interests.CountAsync(ct);
    var rows = await interests.OrderByDescending(x => x.EffectiveFrom).ThenBy(x => x.Id)
      .Skip((int)((long)page * pageSize)).Take(pageSize)
      .Select(x => new ConsolidationOwnershipRow(x.Id, x.ParentClientId, x.ParentLegalName,
        x.ChildClientId, x.ChildLegalName, x.EffectiveFrom, x.EffectiveTo,
        x.OwnershipPercent, x.EconomicInterestPercent, x.ControlAssessment,
        x.Method, x.EvidenceReference, x.Status)).ToListAsync(ct);
    var members = await (from membership in ConsolidationScopeGuards.ApprovedMembershipsOverlappingPeriod(
                           db, actor.FirmId, scope.GroupId, scope.PeriodId)
                         join client in db.PracticeClients.AsNoTracking()
                           on new { membership.FirmId, membership.ClientId }
                           equals new { client.FirmId, ClientId = client.Id }
                         where membership.FirmId == actor.FirmId
                         orderby client.LegalName, client.Id
                         select new ConsolidationOwnershipMember(client.Id, client.LegalName, membership.EffectiveFrom))
      .Take(MaximumMemberOptions + 1).ToListAsync(ct);
    if (members.Count > MaximumMemberOptions)
      return CommandResult<ConsolidationOwnershipWorkspace>.Fail(ErrorCodes.GateBlocked,
        "The group has more member options than this bounded ownership workspace supports; no partial option list is returned.");

    var manage = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ManageRoles, ct);
    access = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReadRoles, ct);
    if (!access.Succeeded)
      return CommandResult<ConsolidationOwnershipWorkspace>.Fail(access.ErrorCode!, access.Message!);
    var current = await db.ConsolidationScopeVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == scope.Id)
      .Select(x => new { x.GroupId, x.GroupRevision, x.Status }).SingleOrDefaultAsync(ct);
    var currentRevision = await db.ClientGroups.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId).Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
    if (current is null || currentRevision is null || current.GroupId != scope.GroupId ||
        current.GroupRevision != scope.GroupRevision || current.Status != scope.Status || currentRevision != group.Revision)
      return CommandResult<ConsolidationOwnershipWorkspace>.Fail(ErrorCodes.GenerationStale,
        "The group or scope changed while ownership was loaded. Refresh the current scope.");
    var choices = members.DistinctBy(x => x.ClientId).ToArray();
    return CommandResult<ConsolidationOwnershipWorkspace>.Ok(new(
      scope.Id, scope.GroupId, group.Name, scope.Version, scope.GroupRevision, group.Revision,
      scope.Status, scope.Method,
      manage.Succeeded && scope.Status == AccountingWorkflowStates.Draft && scope.GroupRevision == group.Revision && choices.Length > 1,
      page, pageSize, total, rows, choices));
  }
}
