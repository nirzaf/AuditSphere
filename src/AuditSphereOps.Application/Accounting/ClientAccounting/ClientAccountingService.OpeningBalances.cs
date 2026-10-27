using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
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
    CancellationToken ct = default, Guid? expectedClientId = null, string? expectedSourceHash = null,
    long? expectedPriorRevision = null, string? reviewReason = null)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var seed = await db.OpeningBalanceBridges.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == bridgeId && x.FirmId == actor.FirmId, ct);
    if (seed is null || expectedClientId.HasValue && seed.ClientId != expectedClientId.Value)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await LockPeriodDecisionScopeAsync(db, actor, seed.CurrentPeriodId, ReviewerRoles, ct);
    if (!auth.Succeeded) return auth;
    var period = await db.ClientReportingPeriods.FromSqlInterpolated(
      $"SELECT * FROM client_reporting_periods WHERE id = {seed.CurrentPeriodId} AND firm_id = {actor.FirmId} AND client_id = {seed.ClientId} FOR UPDATE")
      .AsNoTracking().SingleAsync(ct);
    if (seed.PriorPeriodId is { } priorId)
      await db.ClientReportingPeriods.FromSqlInterpolated(
        $"SELECT * FROM client_reporting_periods WHERE id = {priorId} AND firm_id = {actor.FirmId} AND client_id = {seed.ClientId} FOR SHARE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
    var bridge = await db.OpeningBalanceBridges.FromSqlInterpolated(
      $"SELECT * FROM opening_balance_bridges WHERE id = {bridgeId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleAsync(ct);
    if (expectedSourceHash is not null && !string.Equals(expectedSourceHash, bridge.SourceHash, StringComparison.Ordinal))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The reviewed opening evidence has changed.");
    if (period.Status == AccountingWorkflowStates.Closed || period.PriorPeriodId != bridge.PriorPeriodId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Opening review requires the exact open successor period.");
    if (bridge.Residual != 0m || bridge.Status is not ("RECONCILED" or AccountingWorkflowStates.Approved))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "An unexplained opening difference blocks bridge approval.");
    if (!IsSha256(bridge.SourceHash) || string.IsNullOrWhiteSpace(bridge.EvidenceReference) || bridge.CreatedAt == default)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The opening bridge source evidence is stale.");
    var state = await OpeningBalanceBridgeCurrentness.GetAsync(db, bridge, ct);
    var revalidation = bridge.Status == AccountingWorkflowStates.Approved;
    if (revalidation && state.Current)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The opening bridge is already current for this prior close.");
    if (bridge.PriorPeriodId.HasValue)
    {
      if (state.Prior is not { Status: AccountingWorkflowStates.Closed } prior)
        return CommandResult.Fail(ErrorCodes.GateBlocked, state.Blocker!);
      // Legacy first-close callers remain compatible. Every amended close requires an explicit
      // reviewed revision; a stale screen or omitted pin can never revalidate an old approval.
      if (expectedPriorRevision.HasValue ? expectedPriorRevision != prior.Revision : prior.Revision != 2)
        return CommandResult.Fail(ErrorCodes.StaleRevision, "Reload and review the exact prior closing revision.");
      if (revalidation && (expectedPriorRevision is null || expectedSourceHash is null ||
          string.IsNullOrWhiteSpace(reviewReason) || reviewReason.Trim().Length > 2000))
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Revalidation needs the exact source, prior revision and a recorded review reason.");
      db.OpeningBalanceBridgeReviews.Add(new OpeningBalanceBridgeReview
      {
        Id = Guid.CreateVersion7(), FirmId = bridge.FirmId, ClientId = bridge.ClientId, BridgeId = bridge.Id,
        PriorPeriodId = prior.Id, PriorPeriodRevision = prior.Revision,
        EvidenceHash = OpeningBalanceBridgeCurrentness.EvidenceHash(bridge),
        Reason = revalidation ? reviewReason!.Trim() : "Initial opening bridge review",
        ReviewerId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      });
    }
    else if (revalidation)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The opening bridge is already approved.");
    if (!revalidation)
    {
      var tracked = db.OpeningBalanceBridges.Local.SingleOrDefault(x => x.Id == bridge.Id);
      if (tracked is not null) bridge = tracked;
      else db.OpeningBalanceBridges.Attach(bridge);
      bridge.Status = AccountingWorkflowStates.Approved;
      bridge.ApprovedByUserId = actor.UserId;
      bridge.ApprovedAt = DateTimeOffset.UtcNow;
    }
    await db.SaveChangesAsync(ct);
    auth = await AuthorizeClientAsync(db, actor, bridge.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded) return auth;
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
