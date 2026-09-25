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
  public static async Task<CommandResult<Guid>> CreatePeriodAsync(
    IClientAccountingDbContext db, ActorContext actor, ReportingPeriodRequest request,
    CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.Currency) ? AccountingDefaults.DefaultCurrency : request.Currency).Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.PeriodCode) ||
        request.StartDate > request.EndDate || string.IsNullOrWhiteSpace(request.Basis) ||
        currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid reporting period is required.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.PriorPeriodId.HasValue && !await db.ClientReportingPeriods.AnyAsync(x =>
        x.Id == request.PriorPeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The prior period is outside the client scope.");
    var duplicate = await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.PeriodCode == request.PeriodCode.Trim() && x.Basis == request.Basis.Trim(), ct);
    if (duplicate)
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The reporting period already exists.");
    var period = new ClientReportingPeriod
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      PeriodCode = request.PeriodCode.Trim(), StartDate = request.StartDate, EndDate = request.EndDate,
      Basis = request.Basis.Trim(), Currency = currency, PriorPeriodId = request.PriorPeriodId,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientReportingPeriods.Add(period);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(period.Id);
  }

  public static async Task<CommandResult<Guid>> RollForwardPeriodAsync(
    IClientAccountingDbContext db, ActorContext actor, RollForwardPeriodRequest request,
    CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.Currency) ? AccountingDefaults.DefaultCurrency : request.Currency).Trim().ToUpperInvariant();
    var sourceHash = request.SourceHash.Trim().ToLowerInvariant();
    if (request.ClientId == Guid.Empty || request.PriorPeriodId == Guid.Empty || string.IsNullOrWhiteSpace(request.PeriodCode) ||
        request.StartDate > request.EndDate || string.IsNullOrWhiteSpace(request.Basis) ||
        currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        !IsSha256(sourceHash) || string.IsNullOrWhiteSpace(request.EvidenceReference) ||
        request.PriorClosingAmount != MoneyPolicy.Normalize(request.PriorClosingAmount) ||
        request.CurrentOpeningAmount != MoneyPolicy.Normalize(request.CurrentOpeningAmount))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A roll-forward needs valid dates, currency, opening amounts, source hash and evidence.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var prior = await db.ClientReportingPeriods
      .FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE id = {request.PriorPeriodId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (prior is null || prior.ClientId != request.ClientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The prior period is outside the client scope.");
    if (prior.Status != AccountingWorkflowStates.Closed)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only a closed period can be rolled forward.");
    if (request.StartDate <= prior.EndDate || !string.Equals(currency, prior.Currency, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The next period must follow the closed period and use the same reporting currency.");
    if (await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.PeriodCode == request.PeriodCode.Trim() && x.Basis == request.Basis.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The reporting period already exists.");

    if (request.SourcePackageId is { } sourcePackageId)
    {
      var sourcePackage = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == sourcePackageId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
      var priorStart = prior.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      var priorEnd = prior.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      if (sourcePackage is null || sourcePackage.Status != AccountingPackageStates.PackageValidated ||
          sourcePackage.PeriodStart != priorStart || sourcePackage.PeriodEnd != priorEnd ||
          !string.Equals(sourcePackage.Currency, currency, StringComparison.Ordinal) ||
          !string.Equals(sourcePackage.CalculationHash, sourceHash, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale,
          "The opening source package is outside the closed prior period or has a different hash.");
    }

    var current = new ClientReportingPeriod
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      PeriodCode = request.PeriodCode.Trim(), StartDate = request.StartDate, EndDate = request.EndDate,
      Basis = request.Basis.Trim(), Currency = currency, PriorPeriodId = prior.Id,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientReportingPeriods.Add(current);
    var priorBooks = await db.ClientReportingBooks.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.PeriodId == prior.Id)
      .ToListAsync(ct);
    db.ClientReportingBooks.AddRange(priorBooks.Select(x => new ClientReportingBook
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, PeriodId = current.Id,
      Code = x.Code, Basis = x.Basis, InclusionRule = x.InclusionRule, Currency = x.Currency,
      Status = AccountingWorkflowStates.Draft, Revision = 1, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    }));
    var residual = MoneyPolicy.Normalize(request.CurrentOpeningAmount - request.PriorClosingAmount);
    db.OpeningBalanceBridges.Add(new OpeningBalanceBridge
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      CurrentPeriodId = current.Id, PriorPeriodId = prior.Id, SourcePackageId = request.SourcePackageId,
      SourceHash = sourceHash, PriorClosingAmount = MoneyPolicy.Normalize(request.PriorClosingAmount),
      CurrentOpeningAmount = MoneyPolicy.Normalize(request.CurrentOpeningAmount), Residual = residual,
      Status = residual == 0m ? "RECONCILED" : "UNEXPLAINED",
      EvidenceReference = request.EvidenceReference.Trim(), CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(current.Id);
  }

  public static async Task<CommandResult> ClosePeriodAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, string reason,
    CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var period = await db.ClientReportingPeriods
      .FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE id = {periodId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (period is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, period.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (period.Status == AccountingWorkflowStates.Closed)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The reporting period is already closed.");
    if (string.IsNullOrWhiteSpace(reason))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "A close decision needs a reason.");
    var unresolved = await db.AccountingReconciliations.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.PeriodId == period.Id && x.Status != AccountingWorkflowStates.Approved, ct);
    if (unresolved)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Unapproved accounting reconciliations block period close.");
    var unreviewedEcl = await db.EclAssessments.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected &&
      db.AccountingReconciliations.Any(r => r.Id == x.ReconciliationId && r.PeriodId == period.Id), ct);
    var unreviewedInventory = await db.InventoryValuationAssessments.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected &&
      db.AccountingReconciliations.Any(r => r.Id == x.ReconciliationId && r.PeriodId == period.Id), ct);
    var unreviewedSpecialist = await db.SpecialistAccountingSchedules.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.PeriodId == period.Id && x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected, ct);
    var unreviewedAnalytics = await db.AnalyticalReviews.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.PeriodId == period.Id && x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected, ct);
    var openRisk = await db.JournalRiskFlags.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.Status == "OPEN" && db.SourceImportBatches.Any(b => b.Id == x.ImportBatchId && b.PeriodId == period.Id), ct);
    if (unreviewedEcl || unreviewedInventory || unreviewedSpecialist || unreviewedAnalytics || openRisk)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Unreviewed accounting analysis or journal-risk evidence blocks period close.");
    var periodStart = period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var periodEnd = period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var packageIds = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
        x.PeriodStart == periodStart && x.PeriodEnd == periodEnd)
      .Select(x => x.Id).ToListAsync(ct);
    foreach (var packageId in packageIds)
    {
      var packageReview = await FinancialPackageReviewService.RequireCurrentAsync(db, actor, packageId, requirePartner: true, ct);
      if (!packageReview.Succeeded)
        return CommandResult.Fail(ErrorCodes.GateBlocked,
          "Every financial package for the period needs current management, accounting, and partner approval before close.");
    }
    period.Status = AccountingWorkflowStates.Closed;
    period.ClosedByUserId = actor.UserId;
    period.ClosedAt = DateTimeOffset.UtcNow;
    period.CloseReason = reason.Trim();
    period.Revision++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ReopenPeriodAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, string reason,
    CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var period = await db.ClientReportingPeriods
      .FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE id = {periodId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (period is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, period.ClientId, ["Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return auth;
    if (period.Status != AccountingWorkflowStates.Closed || string.IsNullOrWhiteSpace(reason))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Only a closed period can be reopened with a recorded decision.");
    var previousRevision = period.Revision;
    var amendmentRevision = previousRevision + 1;
    period.Status = AccountingWorkflowStates.Draft;
    period.CloseReason = "REOPENED: " + reason.Trim();
    period.Revision = amendmentRevision;
    period.ClosedAt = null;
    period.ClosedByUserId = null;
    db.ClientPeriodAmendments.Add(new ClientPeriodAmendment
    {
      Id = Guid.CreateVersion7(), FirmId = period.FirmId, ClientId = period.ClientId, PeriodId = period.Id,
      PreviousRevision = previousRevision, AmendmentRevision = amendmentRevision,
      Reason = reason.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
