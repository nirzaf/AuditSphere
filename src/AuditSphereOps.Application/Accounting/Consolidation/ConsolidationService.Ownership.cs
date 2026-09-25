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
  private sealed record OwnershipEdge(Guid ParentClientId, Guid ChildClientId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

  public static async Task<CommandResult<Guid>> AddOwnershipInterestAsync(
    IClientAccountingDbContext db, ActorContext actor, OwnershipInterestRequest request,
    CancellationToken ct = default)
  {
    if (request.ScopeVersionId == Guid.Empty || request.ParentClientId == Guid.Empty || request.ChildClientId == Guid.Empty ||
        request.ParentClientId == request.ChildClientId || request.EffectiveTo < request.EffectiveFrom ||
        request.OwnershipPercent is < 0 or > 100 || request.EconomicInterestPercent is < 0 or > 100 ||
        string.IsNullOrWhiteSpace(request.ControlAssessment) || string.IsNullOrWhiteSpace(request.Method) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An ownership interest needs distinct entities, valid dates, percentages, control assessment, method and evidence.");

    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.ScopeVersionId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (scope.Status != AccountingWorkflowStates.Draft)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Ownership can only be changed on a draft consolidation scope.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");

    var memberIds = await db.ClientGroupMemberships.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.Status == AccountingWorkflowStates.Approved &&
      x.EffectiveTo == null).Select(x => x.ClientId).ToHashSetAsync(ct);
    if (!memberIds.Contains(request.ParentClientId) || !memberIds.Contains(request.ChildClientId))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Both ownership entities must be approved members of the group perimeter.");

    var existing = await db.OwnershipInterestVersions.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id &&
      x.Status != AccountingWorkflowStates.Rejected).Select(x => new OwnershipEdge(x.ParentClientId, x.ChildClientId,
        x.EffectiveFrom, x.EffectiveTo)).ToListAsync(ct);
    if (existing.Any(x => x.ParentClientId == request.ParentClientId && x.ChildClientId == request.ChildClientId))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This ownership relationship already exists in the scope; create a new scope for a historical change.");
    if (CreatesOwnershipCycle(existing, request.ParentClientId, request.ChildClientId))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "The ownership relationship would create a circular hierarchy.");

    var ownership = new OwnershipInterestVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      ParentClientId = request.ParentClientId, ChildClientId = request.ChildClientId,
      EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
      OwnershipPercent = MoneyPolicy.Normalize(request.OwnershipPercent),
      EconomicInterestPercent = MoneyPolicy.Normalize(request.EconomicInterestPercent),
      ControlAssessment = request.ControlAssessment.Trim().ToUpperInvariant(), Method = request.Method.Trim().ToUpperInvariant(),
      EvidenceReference = request.EvidenceReference.Trim(), Status = AccountingWorkflowStates.Approved,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.OwnershipInterestVersions.Add(ownership);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(ownership.Id);
  }

  private static bool CreatesOwnershipCycle(IReadOnlyCollection<OwnershipEdge> existing,
    Guid parentClientId, Guid childClientId)
  {
    var adjacency = existing.GroupBy(x => x.ParentClientId).ToDictionary(x => x.Key,
      x => x.Select(y => y.ChildClientId).ToArray());
    var pending = new Stack<Guid>([childClientId]);
    var visited = new HashSet<Guid>();
    while (pending.TryPop(out var current) && visited.Add(current))
    {
      if (current == parentClientId)
        return true;
      if (adjacency.TryGetValue(current, out var children))
        foreach (var child in children) pending.Push(child);
    }
    return false;
  }

  private static bool HasOwnershipCycle(IReadOnlyCollection<OwnershipEdge> edges) =>
    edges.Any(edge => CreatesOwnershipCycle(edges.Where(x => x != edge).ToArray(), edge.ParentClientId, edge.ChildClientId));
}
