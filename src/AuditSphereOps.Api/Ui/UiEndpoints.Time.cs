using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record WorkTaskInput(string Title, Guid? ReportingPeriodId, string? DueDate,
    Guid? EngagementId = null, Guid? MappingVersionId = null, string? FsliCode = null);
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
        // Every selected scope comes from this actor's current workspace; mapping validation is repeated by the command.
        var workspace = await PracticeTimeWorkspaceQuery.GetAsync(db, actor, ct);
        if (!workspace.Succeeded) return CommandResult<Guid>.Fail(workspace.ErrorCode!, workspace.Message!);
        var period = input.ReportingPeriodId is { } id ? workspace.Value!.Periods.FirstOrDefault(x => x.Id == id) : null;
        var engagement = input.EngagementId is { } engagementId
          ? workspace.Value!.Engagements.FirstOrDefault(x => x.Id == engagementId) : null;
        if ((input.ReportingPeriodId is not null && period is null) || (input.EngagementId is not null && engagement is null))
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Select only a client period or engagement available in your current scope.");
        if (period is not null && engagement is not null && period.ClientId != engagement.ClientId)
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The selected period and engagement belong to different clients.");
        if (period is null && engagement is null && !workspace.Value!.FirmWide)
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Select an authorized client period or engagement to scope this task.");
        if (input.MappingVersionId is not null && (engagement is null || input.MappingVersionId != engagement.MappingVersionId ||
            string.IsNullOrWhiteSpace(input.FsliCode) || !engagement.FsliCodes.Contains(input.FsliCode.Trim(), StringComparer.Ordinal)))
          return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Select an FSLI from the current approved mapping for this engagement.");
        if (input.MappingVersionId is null && !string.IsNullOrWhiteSpace(input.FsliCode))
          return CommandResult<Guid>.Fail("time.invalid", "An FSLI code must be paired with its approved mapping version.");
        return await PracticeTimeService.CreateTaskAsync(db, actor, new CreateTaskRequest(input.Title,
          ClientId: period?.ClientId ?? engagement?.ClientId,
          EngagementId: engagement?.Id,
          ReportingPeriodId: period?.Id,
          DueDate: due,
          MappingVersionId: engagement?.MappingVersionId is not null && input.MappingVersionId is not null ? input.MappingVersionId : null,
          FsliCode: input.MappingVersionId is not null ? input.FsliCode : null), ct);
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
