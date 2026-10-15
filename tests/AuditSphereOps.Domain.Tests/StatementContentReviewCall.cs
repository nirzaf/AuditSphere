using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Application.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Records a content decision the way the product does: read the exact draft snapshot and latest decision, then submit a new intent.</summary>
internal static class StatementContentReviewCall
{
  internal static async Task<CommandResult<Guid>> RecordAsync(IClientAccountingDbContext db, ActorContext actor, Guid packageId,
    Guid draftRevisionId, string decision = "APPROVED", string? reason = null)
  {
    var state = await FinancialStatementService.GetStatementContentReviewAsync(db, actor, packageId, draftRevisionId);
    if (!state.Succeeded) return CommandResult<Guid>.Fail(state.ErrorCode!, state.Message!);
    return await FinancialStatementService.RecordStatementContentReviewAsync(db, actor, packageId, draftRevisionId, decision, reason,
      Guid.NewGuid(), state.Value!.SnapshotSha256Hex, state.Value.LatestReviewId);
  }
}
