using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record FinancialPackageBuildStatus(
  Guid OperationId,
  string Status,
  int AttemptCount,
  DateTimeOffset CreatedAt,
  DateTimeOffset? CompletedAt,
  Guid? PackageId);

/// <summary>Returns the redacted status of the exact package-build operation initiated by this actor.</summary>
public static class FinancialPackageBuildStatusQuery
{
  private static readonly string[] Roles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<FinancialPackageBuildStatus>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid operationId,
    CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || operationId == Guid.Empty)
      return CommandResult<FinancialPackageBuildStatus>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null)
      return CommandResult<FinancialPackageBuildStatus>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var request = new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
      Roles, InternalOnly: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialPackageBuildStatus>.Fail(auth.ErrorCode!, auth.Message!);

    // Only the initiating actor can poll this receipt. No payload, request digest, lease owner,
    // error detail or other operation can be discovered through this endpoint.
    var operation = await db.DurableOperations.AsNoTracking().Where(x =>
        x.Id == operationId && x.FirmId == actor.FirmId && x.EngagementId == engagement.Id &&
        x.OriginatorId == actor.UserId && x.OperationKind == FinancialPackageBuildHandler.Kind)
      .Select(x => new { x.Id, x.Status, x.AttemptCount, x.CreatedAt, x.CompletedAt, x.ResultIdentity, x.ResultDigest })
      .SingleOrDefaultAsync(ct);
    if (operation is null)
      return CommandResult<FinancialPackageBuildStatus>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialPackageBuildStatus>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    Guid? packageId = operation.Status == OperationState.COMPLETED && operation.ResultDigest is not null &&
      Guid.TryParse(operation.ResultIdentity, out var parsed) ? parsed : null;
    return CommandResult<FinancialPackageBuildStatus>.Ok(new(
      operation.Id, operation.Status.ToString(), operation.AttemptCount, operation.CreatedAt,
      operation.CompletedAt, packageId));
  }
}
