using AuditSphereOps.Domain.Practice;

namespace AuditSphereOps.Application.Practice;

public sealed record ResourceWeekCell(
  DateOnly WeekStart, int CapacityMinutes, int UnavailableMinutes, int PlannedMinutes, int ApprovedActualMinutes,
  decimal? PlannedUtilizationPercent, decimal? ActualUtilizationPercent, bool OverAllocated, int ApprovedBillableMinutes);

/// <summary>
/// Pure week-by-week capacity arithmetic. Capacity is the profile's weekly minutes less recorded unavailability on
/// working days (the firm working week, Sunday to Thursday, unless a calendar is supplied); utilization is only reported
/// when capacity is positive, and planned and approved-actual figures are kept separate so nothing is inferred.
/// </summary>
public static class ResourceGridCalculator
{
  /// <summary>
  /// The firm working week (ADR-0017): Sunday to Thursday, with Friday and Saturday as the weekend, which is the normal working
  /// week in Qatar. The week itself starts on Sunday, so the five working days are the first five days of one week bucket. Capacity
  /// and utilization both read this one definition. Public holidays are not modelled here; they are
  /// recorded as unavailability.
  /// </summary>
  public static readonly DayOfWeek[] FirmWorkingWeek = [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

  public static bool IsWorkingDay(DateOnly date) => Array.IndexOf(FirmWorkingWeek, date.DayOfWeek) >= 0;

  /// <summary>The Sunday that opens the firm week containing the date. A week runs Sunday to Saturday.</summary>
  public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(int)date.DayOfWeek);

  /// <summary>Preserves every configured weekly minute; remainder minutes go to the earliest working days (Sunday first).</summary>
  public static int DailyCapacityMinutes(int weeklyCapacityMinutes, DayOfWeek day, IReadOnlyCollection<DayOfWeek>? workingDays = null)
  {
    var days = (workingDays ?? FirmWorkingWeek).Distinct().OrderBy(x => x).ToArray();
    var index = Array.IndexOf(days, day);
    if (index < 0 || weeklyCapacityMinutes <= 0) return 0;
    return weeklyCapacityMinutes / days.Length + (index < weeklyCapacityMinutes % days.Length ? 1 : 0);
  }

  public static IReadOnlyList<ResourceWeekCell> Build(
    int weeklyCapacityMinutes, IReadOnlyCollection<StaffAvailability> availability, IReadOnlyCollection<StaffAllocation> allocations,
    IReadOnlyCollection<(DateOnly WorkDate, int Minutes)> approvedTime, DateOnly firstWeek, int weeks, IReadOnlyCollection<DayOfWeek>? workingDays = null,
    IReadOnlyCollection<(DateOnly WorkDate, int Minutes)>? approvedBillableTime = null)
  {
    workingDays ??= FirmWorkingWeek;
    // Utilization is approved billable time over capacity, the same basis as department analytics (ADR-0018). Approved actual
    // minutes still show all approved time. A caller that does not separate the two is treated as all billable.
    approvedBillableTime ??= approvedTime;
    var cells = new List<ResourceWeekCell>(weeks);
    for (var w = 0; w < weeks; w++)
    {
      var start = WeekStart(firstWeek).AddDays(7 * w);
      var end = start.AddDays(6);
      var unavailable = 0;
      for (var d = start; d <= end; d = d.AddDays(1))
      {
        if (!workingDays.Contains(d.DayOfWeek)) continue;
        var dayOff = availability.Where(a => a.StartDate <= d && a.EndDate >= d).Sum(a => a.MinutesPerDay);
        unavailable += Math.Min(dayOff, DailyCapacityMinutes(weeklyCapacityMinutes, d.DayOfWeek, workingDays));
      }
      var capacity = Math.Max(0, weeklyCapacityMinutes - unavailable);
      var planned = allocations.Where(a => a.WeekStart == start).Sum(a => a.PlannedMinutes);
      var actual = approvedTime.Where(t => t.WorkDate >= start && t.WorkDate <= end).Sum(t => t.Minutes);
      var billable = approvedBillableTime.Where(t => t.WorkDate >= start && t.WorkDate <= end).Sum(t => t.Minutes);
      cells.Add(new ResourceWeekCell(start, capacity, unavailable, planned, actual,
        capacity > 0 ? Math.Round(planned * 100m / capacity, 1) : null,
        capacity > 0 ? Math.Round(billable * 100m / capacity, 1) : null,
        planned > capacity, billable));
    }
    return cells;
  }
}
