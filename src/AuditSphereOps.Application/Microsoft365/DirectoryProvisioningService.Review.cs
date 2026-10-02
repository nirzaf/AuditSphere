using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record TenantOperationReview(Guid OperationId, string Kind, string State, string TenantId,
  string Target, string? ObjectId, string? Role, string? ScopeKind, Guid? ClientId, Guid? EngagementId,
  Guid? ManagedGroupId, string Reason, Guid RequestedByUserId, string? CorrelationId, string? Reconciliation);

public static partial class DirectoryProvisioningService
{
  /// <summary>Immutable requested intent for an explicit reconciliation/binding review; no password or callback is returned.</summary>
  public static async Task<CommandResult<TenantOperationReview>> ReviewOperationAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid operationId, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<TenantOperationReview>();
    var row = await db.Microsoft365ExternalOperations.AsNoTracking().Where(x => x.Id == operationId && x.FirmId == actor.FirmId)
      .Select(x => new TenantOperationReview(x.Id, x.Kind, x.State, x.TenantId, x.TargetDescriptor, x.ResultObjectId,
        x.RequestedRole, x.RequestedScopeKind, x.RequestedClientId, x.RequestedEngagementId, x.ManagedGroupId,
        x.Reason, x.RequestedByUserId, x.ProviderCorrelationId, x.ReconciliationResult)).SingleOrDefaultAsync(ct);
    if(row is null || !await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<TenantOperationReview>();
    return CommandResult<TenantOperationReview>.Ok(row);
  }
}
