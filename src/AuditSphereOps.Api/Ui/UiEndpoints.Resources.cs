using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record StaffProfileInput(Guid UserId, string Department, string Skills, int WeeklyCapacityMinutes, string TargetUtilizationPercent);
  public sealed record CertificationInput(Guid UserId, string Name, string? ExpiresOn);
  public sealed record AvailabilityInput(Guid UserId, string StartDate, string EndDate, string Kind, int MinutesPerDay);
  public sealed record AllocationInput(Guid EngagementId, Guid UserId, string WeekStart, int PlannedMinutes);

  private static void MapResourceEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/practice/resources", (string? start, int? weeks, HttpContext http) =>
    {
      var first = TryDate(start, out var s) ? s : DateOnly.FromDateTime(DateTime.UtcNow);
      return ReadAsync(http, (db, actor, ct) => ResourcePlanningWorkspaceQuery.GetAsync(db, actor, first, weeks ?? 4, ct));
    });
    group.MapPost("/practice/resources/profiles", (StaffProfileInput input, HttpContext http) =>
      TryDecimal(input.TargetUtilizationPercent, out var target)
        ? CommandAsync(http, (db, actor, ct) => ResourcePlanningService.SaveProfileAsync(db, actor,
            new SaveStaffProfileRequest(input.UserId, input.Department, input.Skills, input.WeeklyCapacityMinutes, target), ct))
        : Task.FromResult(Invalid("Enter a target utilization percentage.")));
    group.MapPost("/practice/resources/certifications", (CertificationInput input, HttpContext http) =>
    {
      DateOnly? expires = TryDate(input.ExpiresOn, out var e) ? e : null;
      if (!string.IsNullOrWhiteSpace(input.ExpiresOn) && expires is null) return Task.FromResult(Invalid("Enter the expiry as a date."));
      return CommandAsync(http, (db, actor, ct) => ResourcePlanningService.AddCertificationAsync(db, actor, new AddCertificationRequest(input.UserId, input.Name, null, expires), ct));
    });
    group.MapPost("/practice/resources/availability", (AvailabilityInput input, HttpContext http) =>
      TryDate(input.StartDate, out var start) && TryDate(input.EndDate, out var end)
        ? CommandAsync(http, (db, actor, ct) => ResourcePlanningService.AddAvailabilityAsync(db, actor,
            new AddAvailabilityRequest(input.UserId, start, end, input.Kind, input.MinutesPerDay), ct))
        : Task.FromResult(Invalid("Enter the unavailable dates.")));
    group.MapPost("/practice/resources/allocations", (AllocationInput input, HttpContext http) =>
      TryDate(input.WeekStart, out var week)
        ? CommandAsync(http, (db, actor, ct) => ResourcePlanningService.SetAllocationAsync(db, actor,
            new SetAllocationRequest(input.EngagementId, input.UserId, week, input.PlannedMinutes), ct))
        : Task.FromResult(Invalid("Choose the allocation week.")));

    group.MapPost("/practice/resources/preview", (ResourcePlanningCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ResourcePlanningCommandWorkspace.PreviewAsync(db, actor, input, ct)));
    group.MapPost("/practice/resources/commands", (ResourcePlanningCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ResourcePlanningCommandWorkspace.ExecuteAsync(db, actor, input, ct)));
    group.MapGet("/practice/resources/receipts/{requestId:guid}", (Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ResourcePlanningCommandWorkspace.LookupAsync(db, actor, requestId, requestHash, ct)));

    MapStatementEndpoints(group);
    group.MapGet("/engagements/{id:guid}/statements", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FinancialStatementDrillDownQuery.GetAsync(db, actor, id, ct)));
  }
}
