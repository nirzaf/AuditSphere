using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>A scope-limited, payload-free status for one durable package build.</summary>
public sealed record FinancialPackageBuildOperationStatus(Guid OperationId, string Status, string? ErrorCode, Guid? PackageId);

public static class FinancialPackageBuildStatusQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<FinancialPackageBuildOperationStatus>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid operationId, CancellationToken ct = default)
  {
    if (operationId == Guid.Empty)
      return CommandResult<FinancialPackageBuildOperationStatus>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var operation = await db.DurableOperations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == operationId && x.FirmId == actor.FirmId && x.OperationKind == FinancialPackageBuildHandler.Kind, ct);
    if (operation is null || operation.ClientId is not Guid clientId || operation.EngagementId is not Guid engagementId)
      return CommandResult<FinancialPackageBuildOperationStatus>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialPackageBuildOperationStatus>.Fail(auth.ErrorCode!, auth.Message!);

    Guid? packageId = null;
    if (operation.Status == OperationState.COMPLETED && Guid.TryParse(operation.ResultIdentity, out var resultId))
    {
      var package = await db.FinancialPackages.AsNoTracking().Where(x => x.Id == resultId && x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.EngagementId == engagementId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
      packageId = package;
    }
    return CommandResult<FinancialPackageBuildOperationStatus>.Ok(new(operation.Id, operation.Status.ToString(),
      operation.Status is OperationState.AUTHORIZATION_BLOCKED or OperationState.PROVIDER_BLOCKED or OperationState.DEAD_LETTER
        ? operation.ErrorCode : null,
      packageId));
  }
}
