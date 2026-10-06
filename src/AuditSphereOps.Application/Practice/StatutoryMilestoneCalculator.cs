using System.Text.Json;
using AuditSphereOps.Domain.Audit;

namespace AuditSphereOps.Application.Practice;

public sealed record CalculatedMilestones(
  DateOnly PeriodEnd,
  DateOnly StatutoryFilingCutoff,
  DateOnly FieldworkStartDate,
  DateOnly DraftReportDate,
  DateOnly FinalReportDate,
  DateOnly ArchiveDeadlineDate,
  bool HasAdjustments,
  IReadOnlyList<string> Warnings,
  bool WarningOverrideRequired);

/// <summary>
/// Pure operational milestone calculator relative to explicitly stored statutory cutoffs (§4.2.2, AS-COMP-10).
/// No jurisdictional deadline is guessed or hard-coded. Distinguishes forbidden chronology from overridable scheduling warnings.
/// </summary>
public static class StatutoryMilestoneCalculator
{
  public const int ArchiveDaysAfterFinalReport = 60; // ISA 230 / §4.4.3
  public const int MinRecommendedFieldworkDays = 14;
  public const int MinRecommendedFinalReviewDays = 7;
  public const int MinRecommendedFilingBufferDays = 5;

  public static CalculatedMilestones CalculateDefault(DateOnly periodEnd, DateOnly statutoryFilingCutoff)
  {
    if (statutoryFilingCutoff <= periodEnd)
      throw new ArgumentException("Statutory filing cutoff must be strictly after the accounting period end date.");

    var totalDays = statutoryFilingCutoff.DayNumber - periodEnd.DayNumber;

    // Default fieldwork commencement: 7 days after period end (or next business week)
    var fieldworkStart = periodEnd.AddDays(Math.Min(7, Math.Max(1, totalDays / 6)));

    // Default final report: 15 days before statutory filing cutoff (or buffered if window is tight)
    var buffer = Math.Min(15, Math.Max(1, totalDays / 8));
    var finalReport = statutoryFilingCutoff.AddDays(-buffer);
    if (finalReport <= fieldworkStart)
      finalReport = statutoryFilingCutoff.AddDays(-1);

    // Default draft report delivery: ~2/3 of the way between fieldwork start and final report
    var fieldworkSpan = finalReport.DayNumber - fieldworkStart.DayNumber;
    var draftReport = fieldworkStart.AddDays(Math.Max(1, fieldworkSpan * 2 / 3));

    // Archive deadline: exactly 60 calendar days after final signed report delivery
    var archiveDeadline = finalReport.AddDays(ArchiveDaysAfterFinalReport);

    var warnings = EvaluateWarnings(statutoryFilingCutoff, fieldworkStart, draftReport, finalReport);
    return new(periodEnd, statutoryFilingCutoff, fieldworkStart, draftReport, finalReport, archiveDeadline,
      HasAdjustments: false, Warnings: warnings, WarningOverrideRequired: false);
  }

  public static string? ValidateChronology(
    DateOnly periodEnd,
    DateOnly statutoryFilingCutoff,
    DateOnly fieldworkStartDate,
    DateOnly draftReportDate,
    DateOnly finalReportDate,
    DateOnly archiveDeadlineDate)
  {
    if (statutoryFilingCutoff <= periodEnd)
      return "Statutory filing cutoff must be strictly after the accounting period end date.";

    if (fieldworkStartDate < periodEnd)
      return "Fieldwork cannot commence before the accounting period end date.";

    if (draftReportDate < fieldworkStartDate)
      return "Draft report delivery target cannot precede fieldwork commencement.";

    if (finalReportDate < draftReportDate)
      return "Final signed report cannot precede draft report delivery target.";

    if (finalReportDate > statutoryFilingCutoff)
      return "Final signed report delivery cannot exceed the statutory filing cutoff date.";

    if (archiveDeadlineDate < finalReportDate)
      return "Audit documentation archive deadline cannot precede the final signed report date.";

    return null;
  }

  public static IReadOnlyList<string> EvaluateWarnings(
    DateOnly statutoryFilingCutoff,
    DateOnly fieldworkStartDate,
    DateOnly draftReportDate,
    DateOnly finalReportDate)
  {
    var warnings = new List<string>();

    var fieldworkDays = draftReportDate.DayNumber - fieldworkStartDate.DayNumber;
    if (fieldworkDays < MinRecommendedFieldworkDays)
      warnings.Add($"Compressed fieldwork window: planned fieldwork is {fieldworkDays} days (recommended minimum {MinRecommendedFieldworkDays} days).");

    var reviewDays = finalReportDate.DayNumber - draftReportDate.DayNumber;
    if (reviewDays < MinRecommendedFinalReviewDays)
      warnings.Add($"Compressed review window: {reviewDays} days between draft report and final report (recommended minimum {MinRecommendedFinalReviewDays} days).");

    var bufferDays = statutoryFilingCutoff.DayNumber - finalReportDate.DayNumber;
    if (bufferDays < MinRecommendedFilingBufferDays)
      warnings.Add($"Tight statutory filing buffer: final report is scheduled only {bufferDays} days before filing cutoff (recommended minimum {MinRecommendedFilingBufferDays} days).");

    return warnings;
  }

  public static CalculatedMilestones Evaluate(
    DateOnly periodEnd,
    DateOnly statutoryFilingCutoff,
    DateOnly? requestedFieldworkStart = null,
    DateOnly? requestedDraftReport = null,
    DateOnly? requestedFinalReport = null,
    string? adjustmentReason = null,
    string? warningOverrideReason = null)
  {
    var standard = CalculateDefault(periodEnd, statutoryFilingCutoff);

    var fieldworkStart = requestedFieldworkStart ?? standard.FieldworkStartDate;
    var draftReport = requestedDraftReport ?? standard.DraftReportDate;
    var finalReport = requestedFinalReport ?? standard.FinalReportDate;
    var archiveDeadline = finalReport.AddDays(ArchiveDaysAfterFinalReport);

    var isAdjusted = fieldworkStart != standard.FieldworkStartDate ||
                     draftReport != standard.DraftReportDate ||
                     finalReport != standard.FinalReportDate;

    if (isAdjusted && string.IsNullOrWhiteSpace(adjustmentReason))
      throw new InvalidOperationException("An adjustment reason is mandatory when altering calculated statutory milestones.");

    var warnings = EvaluateWarnings(statutoryFilingCutoff, fieldworkStart, draftReport, finalReport);
    var overrideRequired = warnings.Count > 0 && string.IsNullOrWhiteSpace(warningOverrideReason);

    return new(periodEnd, statutoryFilingCutoff, fieldworkStart, draftReport, finalReport, archiveDeadline,
      HasAdjustments: isAdjusted, Warnings: warnings, WarningOverrideRequired: overrideRequired);
  }
}
