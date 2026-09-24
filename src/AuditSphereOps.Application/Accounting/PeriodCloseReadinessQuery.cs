using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// Read-only Module 20 close-readiness report. It mirrors the authoritative close gates
// without locking or mutating anything: every blocker names its evidence class so the
// preparer can resolve it before requesting the close decision.
public sealed record PeriodCloseBlocker(string Code, string Detail);

public sealed record PeriodCloseReadinessReport(
  Guid PeriodId, string PeriodCode, string PeriodStatus, bool CanClose,
  int PackageCount, IReadOnlyList<PeriodCloseBlocker> Blockers);

public static class PeriodCloseReadinessQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<PeriodCloseReadinessReport>> GetReadinessAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId,
    CancellationToken ct = default)
  {
    if (periodId == Guid.Empty)
      return CommandResult<PeriodCloseReadinessReport>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A reporting period id is required.");
    var period = await db.ClientReportingPeriods.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == periodId && x.FirmId == actor.FirmId, ct);
    if (period is null)
      return CommandResult<PeriodCloseReadinessReport>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(period.FirmId, period.ClientId, null, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<PeriodCloseReadinessReport>.Fail(auth.ErrorCode!, auth.Message!);

    var blockers = new List<PeriodCloseBlocker>();
    if (period.Status == AccountingWorkflowStates.Closed)
    {
      blockers.Add(new PeriodCloseBlocker("period.already-closed", "The reporting period is already closed."));
    }
    else
    {
      if (await db.AccountingReconciliations.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
            x.ClientId == period.ClientId && x.PeriodId == period.Id && x.Status != AccountingWorkflowStates.Approved, ct))
        blockers.Add(new PeriodCloseBlocker("reconciliations.unapproved",
          "Unapproved accounting reconciliations block period close."));
      var unreviewedEcl = await db.EclAssessments.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == period.ClientId && x.Status != AccountingWorkflowStates.Approved &&
        x.Status != AccountingWorkflowStates.Rejected &&
        db.AccountingReconciliations.Any(r => r.Id == x.ReconciliationId && r.PeriodId == period.Id), ct);
      var unreviewedInventory = await db.InventoryValuationAssessments.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == period.ClientId && x.Status != AccountingWorkflowStates.Approved &&
        x.Status != AccountingWorkflowStates.Rejected &&
        db.AccountingReconciliations.Any(r => r.Id == x.ReconciliationId && r.PeriodId == period.Id), ct);
      var unreviewedSpecialist = await db.SpecialistAccountingSchedules.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == period.ClientId && x.PeriodId == period.Id &&
        x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected, ct);
      var unreviewedAnalytics = await db.AnalyticalReviews.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == period.ClientId && x.PeriodId == period.Id &&
        x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected, ct);
      if (unreviewedEcl || unreviewedInventory || unreviewedSpecialist || unreviewedAnalytics)
        blockers.Add(new PeriodCloseBlocker("analysis.unreviewed",
          "Unreviewed accounting analysis evidence blocks period close."));
      if (await db.JournalRiskFlags.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
            x.ClientId == period.ClientId && x.Status == "OPEN" &&
            db.SourceImportBatches.Any(b => b.Id == x.ImportBatchId && b.PeriodId == period.Id), ct))
        blockers.Add(new PeriodCloseBlocker("journal-risk.open", "Open journal-risk flags block period close."));
    }

    var periodStart = period.StartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var periodEnd = period.EndDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var packageIds = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
        x.PeriodStart == periodStart && x.PeriodEnd == periodEnd)
      .Select(x => x.Id).ToListAsync(ct);
    if (period.Status != AccountingWorkflowStates.Closed)
    {
      foreach (var packageId in packageIds)
      {
        var packageReview = await FinancialPackageReviewService.RequireCurrentAsync(db, actor, packageId, requirePartner: true, ct);
        if (!packageReview.Succeeded)
        {
          blockers.Add(new PeriodCloseBlocker("packages.not-current",
            "Every financial package for the period needs current management, accounting, and partner approval with no failed blocking validations."));
          break;
        }
      }
    }

    return CommandResult<PeriodCloseReadinessReport>.Ok(new PeriodCloseReadinessReport(
      period.Id, period.PeriodCode, period.Status,
      CanClose: blockers.Count == 0, packageIds.Count, blockers));
  }
}
