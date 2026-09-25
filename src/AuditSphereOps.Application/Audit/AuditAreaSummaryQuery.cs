using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// T061-T072 area summary: one parameterized read over the substantive audit areas
// (cash, receivables, inventory, revenue, payables, fixed assets, expenses, payroll,
// loans, equity, related parties, tax, journals, analytical review, going concern and
// subsequent events). Each area's workpapers, item tests, cut-off evidence, subsequent
// matches, confirmations and differences are reported together with honest coverage
// counters. Reporting facts only; it never expresses an audit conclusion.
public sealed record AuditAreaProcedureRow(
  Guid ProcedureId, string SourceProcedureId, string Status, string ApplicabilityStatus);

public sealed record AuditAreaAssessmentRow(
  Guid AssessmentId, string AssessmentKind, string Conclusion, string Status,
  decimal? BookedAmount, decimal? AuditedAmount, decimal? ResidualAmount, string? Currency, long Revision);

public sealed record AuditAreaDifferenceRow(
  Guid DifferenceId, string DifferenceType, decimal Amount, string Currency, string Status, string? CorrectionState);

public sealed record AuditAreaSummaryView(
  Guid EngagementId, string AreaCode, int SectionNumber, string SectionTitle,
  int ProcedureCount, int ApplicableProcedureCount, int ReviewedProcedureCount,
  IReadOnlyList<AuditAreaProcedureRow> Procedures,
  IReadOnlyList<AuditAreaAssessmentRow> Assessments,
  int ScheduleCount, int ApprovedScheduleCount,
  int SelectionCount, int SelectedItemCount, int TestedItemCount, int ExceptionItemCount,
  int CutOffRecordedCount, int CutOffExceptionCount,
  int SubsequentRecordedCount, int SubsequentMatchedCount,
  int ConfirmationCount, int ConfirmationClosedCount,
  IReadOnlyList<AuditAreaDifferenceRow> Differences,
  bool WorkpapersReviewed, bool HasBlockingEvidence);

public static class AuditAreaSummaryQuery
{
  private static readonly string[] ReadRoles =
    ["Auditor", "Reviewer", "Manager", "Partner", "Administrator", "AccountingPreparer", "AccountingReviewer"];

  private static readonly IReadOnlyDictionary<int, string> SectionTitles = new Dictionary<int, string>
  {
    [1] = "Planning & Risk Assessment", [2] = "Cash & Bank", [3] = "Trade Receivables", [4] = "Inventory",
    [5] = "Revenue / Sales", [6] = "Purchases & Trade Payables", [7] = "Fixed Assets", [8] = "Expenses",
    [9] = "Payroll", [10] = "Loans & Borrowings", [11] = "Equity / Share Capital", [12] = "Related Parties",
    [13] = "Tax & Statutory Liabilities", [14] = "Journal Entries & Fraud", [15] = "Analytical Review",
    [16] = "Going Concern", [17] = "Subsequent Events", [18] = "Financial Statements & Disclosures",
    [19] = "Audit Differences & Adjustments", [20] = "Final Completion & Audit Report"
  };

  public static async Task<CommandResult<AuditAreaSummaryView>> GetAreaSummaryAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string areaCode,
    CancellationToken ct = default)
  {
    var area = areaCode?.Trim().ToUpperInvariant() ?? string.Empty;
    if (engagementId == Guid.Empty || !AuditAreaCodes.SectionByArea.TryGetValue(area, out var sectionNumber))
      return CommandResult<AuditAreaSummaryView>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A supported audit area code and engagement are required.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null)
      return CommandResult<AuditAreaSummaryView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AuditAreaSummaryView>.Fail(auth.ErrorCode!, auth.Message!);

    var procedures = await db.AuditProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
        x.SourceSectionNumber == sectionNumber)
      .Select(x => new AuditAreaProcedureRow(x.Id, x.SourceProcedureId, x.Status, x.ApplicabilityStatus))
      .ToListAsync(ct);
    var assessments = await db.AuditAreaAssessments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.AreaCode == area)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Select(x => new AuditAreaAssessmentRow(x.Id, x.AssessmentKind, x.Conclusion, x.Status,
        x.BookedAmount, x.AuditedAmount, x.ResidualAmount, x.Currency, x.Revision))
      .ToListAsync(ct);
    var schedules = await db.AuditSchedules.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .Select(x => new { x.Id, x.Status, x.ScheduleType })
      .ToListAsync(ct);
    var procedureIds = procedures.Select(x => x.ProcedureId).ToArray();
    var selections = await db.AuditSelections.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
        procedureIds.Contains(x.ProcedureId))
      .Select(x => x.Id).ToListAsync(ct);
    var selectionIds = selections.ToArray();
    var selectionItems = await db.AuditSelectionItems.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && selectionIds.Contains(x.SelectionId))
      .Select(x => x.Id).ToListAsync(ct);
    var itemIds = selectionItems.ToArray();
    var tests = await db.AuditItemTests.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && itemIds.Contains(x.SelectionItemId))
      .Select(x => new { x.SelectionItemId, x.Result }).ToListAsync(ct);
    var cutOffs = await db.AuditCutOffTestRecords.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && itemIds.Contains(x.SelectionItemId))
      .Select(x => x.IsCutOffException).ToListAsync(ct);
    var matches = await db.AuditSubsequentMatchRecords.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && itemIds.Contains(x.SelectionItemId))
      .Select(x => x.State).ToListAsync(ct);
    var confirmations = await db.AuditConfirmationCases.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.AreaCode == area)
      .Select(x => x.Status).ToListAsync(ct);
    var differences = await db.AuditDifferences.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .Select(x => new { x.Id, AccountArea = x.AccountArea, x.DifferenceType, x.Amount, x.Currency, x.Status, x.CorrectionState })
      .ToListAsync(ct);
    var areaDifferences = differences
      .Where(x => string.Equals(x.AccountArea, area, StringComparison.OrdinalIgnoreCase) ||
        SectionTitleMatches(x.AccountArea, sectionNumber))
      .Select(x => new AuditAreaDifferenceRow(x.Id, x.DifferenceType, x.Amount, x.Currency, x.Status, x.CorrectionState))
      .ToList();

    // The area schedule set is resolved through the source section of the shared schedule
    // types; only approved schedules count as covering the area.
    var areaScheduleTypes = AreaScheduleTypes(sectionNumber);
    var areaSchedules = schedules.Where(x => areaScheduleTypes.Contains(x.ScheduleType)).ToList();
    var assessedReviewed = assessments.Count > 0 && assessments.All(x => x.Status == AuditAreaAssessmentStatuses.Reviewed);
    var hasBlockingEvidence = cutOffs.Any(x => x) ||
      matches.Any(x => x != AuditSubsequentMatchStates.Matched) ||
      areaDifferences.Any(x => x.Status != AuditDifferenceStatuses.VerifiedReflected &&
        x.Status != AuditDifferenceStatuses.Corrected);

    return CommandResult<AuditAreaSummaryView>.Ok(new AuditAreaSummaryView(
      engagementId, area, sectionNumber,
      SectionTitles.TryGetValue(sectionNumber, out var title) ? title : $"Section {sectionNumber}",
      procedures.Count,
      procedures.Count(x => x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable),
      procedures.Count(x => x.Status == AuditProcedureStatuses.Reviewed),
      procedures, assessments,
      areaSchedules.Count, areaSchedules.Count(x => x.Status == AuditScheduleStatuses.Approved),
      selections.Count, selectionItems.Count,
      tests.Count(x => x.Result != AuditItemTestResults.Pending),
      tests.Count(x => x.Result is AuditItemTestResults.Exception or AuditItemTestResults.Limitation),
      cutOffs.Count, cutOffs.Count(x => x),
      matches.Count, matches.Count(x => x == AuditSubsequentMatchStates.Matched),
      confirmations.Count, confirmations.Count(x => x == AuditConfirmationStatuses.Closed),
      areaDifferences, assessedReviewed, hasBlockingEvidence));
  }

  private static bool SectionTitleMatches(string? accountArea, int sectionNumber) =>
    !string.IsNullOrWhiteSpace(accountArea) && SectionTitles.TryGetValue(sectionNumber, out var title) &&
    string.Equals(accountArea.Trim(), title, StringComparison.OrdinalIgnoreCase);

  /// <summary>Schedule types that evidence each substantive area.</summary>
  private static HashSet<string> AreaScheduleTypes(int sectionNumber) => sectionNumber switch
  {
    2 => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BANK_RECONCILIATION", "BANK_LEDGER" },
    3 => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RECEIVABLES_LEAD", "RECEIVABLE_AGEING" },
    4 => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "INVENTORY_LEAD", "STOCK_COUNT" },
    5 => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "REVENUE_LEAD" },
    6 => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PAYABLES_LEAD", "SUPPLIER_STATEMENTS" },
    7 => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FIXED_ASSET_REGISTER" },
    _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
  };
}
