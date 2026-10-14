using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Application.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Records a source decision the way the product does: read the current link digest and latest decision, then submit a new intent.</summary>
internal static class CashFlowSourceReviewCall
{
  internal static async Task<CommandResult<Guid>> RecordAsync(IClientAccountingDbContext db, ActorContext actor, Guid packageId,
    string decision, string? reason)
  {
    var state = await FinancialStatementService.GetCashFlowSourceReviewAsync(db, actor, packageId);
    if (!state.Succeeded) return CommandResult<Guid>.Fail(state.ErrorCode!, state.Message!);
    return await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, actor, packageId, decision, reason,
      Guid.NewGuid(), state.Value!.LinkDigest, state.Value.LatestReviewId);
  }
}
