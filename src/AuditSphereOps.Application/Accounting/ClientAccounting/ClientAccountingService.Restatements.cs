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
  public static async Task<CommandResult<Guid>> CreatePeriodRestatementAsync(
    IClientAccountingDbContext db, ActorContext actor, CreatePeriodRestatementRequest request,
    CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || request.PeriodId == Guid.Empty || request.OriginalPackageId == Guid.Empty ||
        request.RevisedPackageId == Guid.Empty || request.OriginalPackageId == request.RevisedPackageId ||
        string.IsNullOrWhiteSpace(request.RevisedBasis) || string.IsNullOrWhiteSpace(request.Reason) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A restatement needs distinct issued/revised packages, a revised basis, reason and evidence.");
    var changeType = request.ChangeType.Trim().ToUpperInvariant();
    if (!PeriodRestatementChangeTypes.All.Contains(changeType))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A restatement needs a supported change type: reclassified, restated error, policy transition or prospective estimate change.");
    if (changeType == PeriodRestatementChangeTypes.ProspectiveEstimateChange &&
        string.IsNullOrWhiteSpace(request.AffectedPeriods))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A prospective estimate change must name the periods it affects; prior periods are never silently restated.");
    var affectedPeriods = string.Join(',', (request.AffectedPeriods ?? string.Empty)
      .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .Select(x => x.ToUpperInvariant())
      .Distinct(StringComparer.Ordinal)
      .OrderBy(x => x, StringComparer.Ordinal));
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.PeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting period is outside the client scope.");
    if (period.Status != AccountingWorkflowStates.Closed)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Restatements require an issued closed reporting period.");
    var periodStart = period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var periodEnd = period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var packages = await db.FinancialPackages.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      (x.Id == request.OriginalPackageId || x.Id == request.RevisedPackageId)).ToListAsync(ct);
    var original = packages.SingleOrDefault(x => x.Id == request.OriginalPackageId);
    var revised = packages.SingleOrDefault(x => x.Id == request.RevisedPackageId);
    if (original is null || revised is null || original.Status != AccountingPackageStates.PackageValidated ||
        revised.Status != AccountingPackageStates.PackageValidated || original.EngagementId != revised.EngagementId ||
        original.PeriodStart != periodStart || original.PeriodEnd != periodEnd ||
        revised.PeriodStart != periodStart || revised.PeriodEnd != periodEnd ||
        !string.Equals(original.Currency, period.Currency, StringComparison.Ordinal) ||
        !string.Equals(revised.Currency, period.Currency, StringComparison.Ordinal) ||
        string.Equals(original.CalculationHash, revised.CalculationHash, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "Both packages must be validated, immutable, period-bound and materially different before restatement.");
    if (await db.ClientPeriodRestatements.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.OriginalPackageId == original.Id && x.RevisedPackageId == revised.Id, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This package restatement already exists.");
    var restatement = new ClientPeriodRestatement
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      EngagementId = original.EngagementId, PeriodId = period.Id,
      OriginalPackageId = original.Id, RevisedPackageId = revised.Id,
      OriginalPackageHash = original.CalculationHash, RevisedPackageHash = revised.CalculationHash,
      RevisedBasis = request.RevisedBasis.Trim(), Reason = request.Reason.Trim(),
      ChangeType = changeType, AffectedPeriods = affectedPeriods,
      EvidenceReference = request.EvidenceReference.Trim(), CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientPeriodRestatements.Add(restatement);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(restatement.Id);
  }

  public static async Task<CommandResult> ApprovePeriodRestatementAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid restatementId,
    CancellationToken ct = default)
  {
    var restatement = await db.ClientPeriodRestatements.SingleOrDefaultAsync(x =>
      x.Id == restatementId && x.FirmId == actor.FirmId, ct);
    if (restatement is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, restatement.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (restatement.Status != AccountingWorkflowStates.Submitted || restatement.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A submitted restatement requires an independent reviewer.");
    var packages = await db.FinancialPackages.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == restatement.ClientId &&
      (x.Id == restatement.OriginalPackageId || x.Id == restatement.RevisedPackageId)).ToListAsync(ct);
    if (packages.Count != 2 || packages.Any(x => x.Status != AccountingPackageStates.PackageValidated) ||
        packages.Single(x => x.Id == restatement.OriginalPackageId).CalculationHash != restatement.OriginalPackageHash ||
        packages.Single(x => x.Id == restatement.RevisedPackageId).CalculationHash != restatement.RevisedPackageHash)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "Restatement package lineage changed; reload the issued evidence.");
    restatement.Status = AccountingWorkflowStates.Approved;
    restatement.ApprovedByUserId = actor.UserId;
    restatement.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
