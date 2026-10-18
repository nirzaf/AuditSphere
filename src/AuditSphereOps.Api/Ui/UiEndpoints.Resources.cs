using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record StaffProfileInput(Guid UserId, string Department, string Skills, int WeeklyCapacityMinutes, string TargetUtilizationPercent);
  public sealed record CertificationInput(Guid UserId, string Name, string? ExpiresOn);
  public sealed record AvailabilityInput(Guid UserId, string StartDate, string EndDate, string Kind, int MinutesPerDay);
  public sealed record AllocationInput(Guid EngagementId, Guid UserId, string WeekStart, int PlannedMinutes, string Phase, string? RiskArea);

  /// <summary>Independent administrative decision against an exact certification source and review revision.</summary>
  public sealed record CertificationReviewInput(Guid CertificationId, Guid RequestId, string SourceDigest,
    string ExpectedRevision, string Decision, string EvidenceReference, string Reason, bool Reviewed);

  private static void MapResourceEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/practice/resources", (string? start, int? weeks, HttpContext http) =>
    {
      var first = TryDate(start, out var s) ? s : DateOnly.FromDateTime(DateTime.UtcNow);
      return ReadAsync(http, (db, actor, ct) => ResourcePlanningWorkspaceQuery.GetAsync(db, actor, first, weeks ?? 4, ct));
    })
      .Produces<ResourcePlanningWorkspace>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/practice/resources/profiles", (StaffProfileInput input, HttpContext http) =>
      TryDecimal(input.TargetUtilizationPercent, out var target)
        ? CommandAsync(http, (db, actor, ct) => ResourcePlanningService.SaveProfileAsync(db, actor,
            new SaveStaffProfileRequest(input.UserId, input.Department, input.Skills, input.WeeklyCapacityMinutes, target), ct))
        : Task.FromResult(Invalid("Enter a target utilization percentage.")))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/practice/resources/certifications", (CertificationInput input, HttpContext http) =>
    {
      DateOnly? expires = TryDate(input.ExpiresOn, out var e) ? e : null;
      if (!string.IsNullOrWhiteSpace(input.ExpiresOn) && expires is null) return Task.FromResult(Invalid("Enter the expiry as a date."));
      return CommandAsync(http, (db, actor, ct) => ResourcePlanningService.AddCertificationAsync(db, actor, new AddCertificationRequest(input.UserId, input.Name, null, expires), ct));
    })
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapGet("/practice/resources/certifications/{certificationId:guid}/review", (Guid certificationId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ResourcePlanningService.GetCertificationReviewAsync(db, actor, certificationId, ct)))
      .Produces<StaffCertificationReviewWorkspace>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/practice/resources/certifications/reviews", (CertificationReviewInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => input.Reviewed && long.TryParse(input.ExpectedRevision,
        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var revision)
        ? ResourcePlanningService.ReviewCertificationAsync(db, actor, new(input.CertificationId, input.RequestId,
          input.SourceDigest, revision, input.Decision, input.EvidenceReference, input.Reason), ct)
        : Task.FromResult(CommandResult<Guid>.Fail("resource.review-invalid", "Review the exact source and decision before publishing."))))
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/practice/resources/availability", (AvailabilityInput input, HttpContext http) =>
      TryDate(input.StartDate, out var start) && TryDate(input.EndDate, out var end)
        ? CommandAsync(http, (db, actor, ct) => ResourcePlanningService.AddAvailabilityAsync(db, actor,
            new AddAvailabilityRequest(input.UserId, start, end, input.Kind, input.MinutesPerDay), ct))
        : Task.FromResult(Invalid("Enter the unavailable dates.")))
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/practice/resources/allocations", (AllocationInput input, HttpContext http) =>
      TryDate(input.WeekStart, out var week)
        ? CommandAsync(http, (db, actor, ct) => ResourcePlanningService.SetAllocationAsync(db, actor,
            new SetAllocationRequest(input.EngagementId, input.UserId, week, input.PlannedMinutes, input.Phase, input.RiskArea), ct))
        : Task.FromResult(Invalid("Choose the allocation week.")))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");

    group.MapPost("/practice/resources/preview", (ResourcePlanningCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ResourcePlanningCommandWorkspace.PreviewAsync(db, actor, input, ct)))
      .Produces<UiValueResponse<ResourcePlanningPreview>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/practice/resources/commands", (ResourcePlanningCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ResourcePlanningCommandWorkspace.ExecuteAsync(db, actor, input, ct)))
      .Produces<UiValueResponse<ResourcePlanningReceiptView>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapGet("/practice/resources/receipts/{requestId:guid}", (Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ResourcePlanningCommandWorkspace.LookupAsync(db, actor, requestId, requestHash, ct)))
      .Produces<ResourcePlanningReceiptLookup>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");

    MapStatementEndpoints(group);
    group.MapGet("/engagements/{id:guid}/statements", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FinancialStatementDrillDownQuery.GetAsync(db, actor, id, ct)))
      .Produces<StatementDrillDownView>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
  }
}
