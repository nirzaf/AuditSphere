// Guarded trial-balance read: the caller supplies only the dataset ID; the server
// resolves firm/client/engagement from the stored row and enforces assignment inside
// the command (§§8.3, 28.2). Guessed IDs fail with a nondisclosing scope error, and
// counts/metadata follow the same rule because they go through this path.
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record TrialBalanceDatasetDto(
  Guid Id, Guid FirmId, Guid ClientId, Guid EngagementId,
  string Currency, string ValidationStatus, bool Balanced, long Revision);

public static class TrialBalanceDatasetQuery
{
  public static async Task<CommandResult<TrialBalanceDatasetDto>> GetDatasetAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid datasetId,
    CancellationToken ct = default)
  {
    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == datasetId, ct);
    if (dataset is null)
      return CommandResult<TrialBalanceDatasetDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(dataset.FirmId, dataset.ClientId, dataset.EngagementId), ct);
    if (!auth.Succeeded)
      return CommandResult<TrialBalanceDatasetDto>.Fail(auth.ErrorCode!, auth.Message!);

    return CommandResult<TrialBalanceDatasetDto>.Ok(new TrialBalanceDatasetDto(
      dataset.Id, dataset.FirmId, dataset.ClientId, dataset.EngagementId,
      dataset.Currency, dataset.ValidationStatus, dataset.Balanced, dataset.Revision));
  }
}
