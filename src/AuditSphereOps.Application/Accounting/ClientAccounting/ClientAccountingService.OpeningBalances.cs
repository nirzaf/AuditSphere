using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientAccountingService
{
  public static async Task<CommandResult<Guid>> CreateOpeningBalanceBridgeAsync(
    IClientAccountingDbContext db, ActorContext actor, OpeningBalanceBridgeRequest request,
    CancellationToken ct = default)
  {
    var sourceHash = request.SourceHash.Trim().ToLowerInvariant();
    if (request.ClientId == Guid.Empty || request.CurrentPeriodId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || !IsSha256(sourceHash))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "An opening bridge needs an exact source hash and evidence reference.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CurrentPeriodId &&
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null || request.PriorPeriodId != period.PriorPeriodId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The opening bridge periods are outside the declared client period lineage.");
    if (request.PriorClosingAmount != MoneyPolicy.Normalize(request.PriorClosingAmount) ||
        request.CurrentOpeningAmount != MoneyPolicy.Normalize(request.CurrentOpeningAmount))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Opening bridge amounts exceed accounting precision.");
    if (request.SourcePackageId is { } packageId)
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == packageId &&
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.Status == AccountingPackageStates.PackageValidated, ct);
      if (package is null || !string.Equals(package.CalculationHash, sourceHash, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The opening bridge source package is outside the client or has a different hash.");
    }
    if (await db.OpeningBalanceBridges.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.CurrentPeriodId == request.CurrentPeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The current period already has an opening bridge.");
    var residual = MoneyPolicy.Normalize(request.CurrentOpeningAmount - request.PriorClosingAmount);
    var bridge = new OpeningBalanceBridge
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      CurrentPeriodId = request.CurrentPeriodId, PriorPeriodId = request.PriorPeriodId,
      SourcePackageId = request.SourcePackageId, SourceHash = sourceHash,
      PriorClosingAmount = MoneyPolicy.Normalize(request.PriorClosingAmount),
      CurrentOpeningAmount = MoneyPolicy.Normalize(request.CurrentOpeningAmount), Residual = residual,
      Status = residual == 0m ? "RECONCILED" : "UNEXPLAINED",
      EvidenceReference = request.EvidenceReference.Trim(), CreatedAt = DateTimeOffset.UtcNow
    };
    db.OpeningBalanceBridges.Add(bridge);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(bridge.Id);
  }

  public static async Task<CommandResult> ApproveOpeningBalanceBridgeAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid bridgeId,
    CancellationToken ct = default)
  {
    var bridge = await db.OpeningBalanceBridges.SingleOrDefaultAsync(x => x.Id == bridgeId && x.FirmId == actor.FirmId, ct);
    if (bridge is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, bridge.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (bridge.Status != "RECONCILED")
      return CommandResult.Fail(ErrorCodes.GateBlocked, "An unexplained opening difference blocks bridge approval.");
    if (bridge.ApprovedByUserId is not null || bridge.CreatedAt == default)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The opening bridge is already approved.");
    if (bridge.SourceHash.Length != 64 || bridge.EvidenceReference.Length == 0)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The opening bridge source evidence is stale.");
    bridge.Status = AccountingWorkflowStates.Approved;
    bridge.ApprovedByUserId = actor.UserId;
    bridge.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
