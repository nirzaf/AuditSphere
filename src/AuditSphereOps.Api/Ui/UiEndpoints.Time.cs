using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record WorkTaskInput(string Title, Guid? ReportingPeriodId, string? DueDate);
  public sealed record TimeDraftInput(Guid TaskId, string WorkDate, int DurationMinutes, string Role, string Activity, bool Billable, string? Narrative);

  private static void MapTimeEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/practice/time", http => ReadAsync(http, (db, actor, ct) => PracticeTimeWorkspaceQuery.GetAsync(db, actor, ct)));
    group.MapPost("/practice/time/tasks", (WorkTaskInput input, HttpContext http) =>
    {
      DateOnly? due = TryDate(input.DueDate, out var d) ? d : null;
      if (!string.IsNullOrWhiteSpace(input.DueDate) && due is null) return Task.FromResult(Invalid("Enter the due date as a date."));
      return CommandAsync(http, async (db, actor, ct) =>
      {
        // The period must be one the workspace offers this actor; non-firm-wide staff must scope the task to a period.
        var workspace = await PracticeTimeWorkspaceQuery.GetAsync(db, actor, ct);
        if (!workspace.Succeeded) return CommandResult<Guid>.Fail(workspace.ErrorCode!, workspace.Message!);
        var period = input.ReportingPeriodId is { } id ? workspace.Value!.Periods.FirstOrDefault(x => x.Id == id) : null;
        if ((input.ReportingPeriodId is not null && period is null) || (period is null && !workspace.Value!.FirmWide))
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Select an authorized client reporting period to scope this task.");
        return await PracticeTimeService.CreateTaskAsync(db, actor, new CreateTaskRequest(input.Title, ClientId: period?.ClientId, ReportingPeriodId: period?.Id, DueDate: due), ct);
      });
    });
    group.MapPost("/practice/time/entries", (TimeDraftInput input, HttpContext http) =>
      TryDate(input.WorkDate, out var date)
        ? CommandAsync(http, (db, actor, ct) => PracticeTimeService.SaveTimeDraftAsync(db, actor, new SaveTimeDraftRequest(input.TaskId, date, 540, input.DurationMinutes,
            input.Role, input.Activity, input.Billable ? PracticeTimeStates.Billable : PracticeTimeStates.NonBillable, input.Narrative ?? "",
            PracticeTimeStates.NarrativeInternal, "QAR"), ct))
        : Task.FromResult(Invalid("Enter the work date.")));
    group.MapPost("/practice/time/entries/{id:guid}/submit", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.SubmitTimeAsync(db, actor, id, ct)));
    group.MapPost("/practice/time/entries/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.ApproveTimeAsync(db, actor, id, ct)));
  }
}
