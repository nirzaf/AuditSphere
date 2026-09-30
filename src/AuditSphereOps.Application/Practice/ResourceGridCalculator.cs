using AuditSphereOps.Domain.Practice;

namespace AuditSphereOps.Application.Practice;

public sealed record ResourceWeekCell(
  DateOnly WeekStart, int CapacityMinutes, int UnavailableMinutes, int PlannedMinutes, int ApprovedActualMinutes,
  decimal? PlannedUtilizationPercent, decimal? ActualUtilizationPercent, bool OverAllocated);

/// <summary>
/// Pure week-by-week capacity arithmetic. Capacity is the profile's weekly minutes less recorded unavailability on
/// working days (Monday–Friday unless a firm calendar is supplied); utilization is only reported
/// when capacity is positive, and planned and approved-actual figures are kept separate so nothing is inferred.
/// </summary>
public static class ResourceGridCalculator
{
  public static readonly DayOfWeek[] MondayToFriday = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

  public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

  public static IReadOnlyList<ResourceWeekCell> Build(
    int weeklyCapacityMinutes, IReadOnlyCollection<StaffAvailability> availability, IReadOnlyCollection<StaffAllocation> allocations,
    IReadOnlyCollection<(DateOnly WorkDate, int Minutes)> approvedTime, DateOnly firstWeek, int weeks, IReadOnlyCollection<DayOfWeek>? workingDays = null)
  {
    workingDays ??= MondayToFriday;
    var cells = new List<ResourceWeekCell>(weeks);
    var perDayCapacity = workingDays.Count == 0 ? 0 : weeklyCapacityMinutes / workingDays.Count;
    for (var w = 0; w < weeks; w++)
    {
      var start = WeekStart(firstWeek).AddDays(7 * w);
      var end = start.AddDays(6);
      var unavailable = 0;
      for (var d = start; d <= end; d = d.AddDays(1))
      {
        if (!workingDays.Contains(d.DayOfWeek)) continue;
        var dayOff = availability.Where(a => a.StartDate <= d && a.EndDate >= d).Sum(a => a.MinutesPerDay);
        unavailable += Math.Min(dayOff, perDayCapacity);
      }
      var capacity = Math.Max(0, weeklyCapacityMinutes - unavailable);
      var planned = allocations.Where(a => a.WeekStart == start).Sum(a => a.PlannedMinutes);
      var actual = approvedTime.Where(t => t.WorkDate >= start && t.WorkDate <= end).Sum(t => t.Minutes);
      cells.Add(new ResourceWeekCell(start, capacity, unavailable, planned, actual,
        capacity > 0 ? Math.Round(planned * 100m / capacity, 1) : null,
        capacity > 0 ? Math.Round(actual * 100m / capacity, 1) : null,
        planned > capacity));
    }
    return cells;
  }
}
