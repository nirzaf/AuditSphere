using System.Globalization;
using AuditSphereOps.Api.Contracts;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.OpenApi;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record WorkTaskInput(string Title, Guid? ReportingPeriodId, string? DueDate,
    Guid? EngagementId = null, Guid? MappingVersionId = null, string? FsliCode = null, string? Phase = null);
  public sealed record TimeDraftInput(Guid TaskId, string WorkDate, int DurationMinutes, string Role, string Activity, bool Billable, string? Narrative,
    int? StartMinute = null);
  public sealed record TimeReturnInput(string Reason);

  private static void MapTimeEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/practice/time", http =>
    {
      var query = http.Request.Query;
      if (!TryGetSinglePracticeTimeQueryValue(query, "approvalAfterWorkDate", out var rawAfterWorkDate) ||
          !TryGetSinglePracticeTimeQueryValue(query, "approvalAfterEntryId", out var rawAfterEntryId) ||
          !TryGetSinglePracticeTimeQueryValue(query, "approvalPageSize", out var rawPageSize))
        return Task.FromResult(Invalid("Use one valid value for each approval paging parameter."));

      DateOnly? afterWorkDate = null;
      if (rawAfterWorkDate is not null)
      {
        if (!TryDate(rawAfterWorkDate, out var parsedDate))
          return Task.FromResult(Invalid("Use both approval cursor values and a page size from 1 to 100."));
        afterWorkDate = parsedDate;
      }
      Guid? afterEntryId = null;
      if (rawAfterEntryId is not null)
      {
        if (!Guid.TryParse(rawAfterEntryId, out var parsedEntryId) || parsedEntryId == Guid.Empty)
          return Task.FromResult(Invalid("Use both approval cursor values and a page size from 1 to 100."));
        afterEntryId = parsedEntryId;
      }
      if ((afterWorkDate is null) != (afterEntryId is null))
        return Task.FromResult(Invalid("Provide both approval cursor values or neither."));

      var pageSize = 50;
      if (rawPageSize is not null &&
          (!int.TryParse(rawPageSize, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) || pageSize is < 1 or > 100))
        return Task.FromResult(Invalid("Approval page size must be a whole number from 1 to 100."));

      var request = new PracticeTimeWorkspaceRequest(afterWorkDate, afterEntryId, pageSize);
      return ReadAsync(http, (db, actor, ct) => PracticeTimeWorkspaceQuery.GetAsync(db, actor, request, ct));
    })
      .WithT046Response<PracticeTimeWorkspace>()
      .WithMetadata(
        new ManualParameterMetadata("approvalAfterWorkDate", ParameterLocation.Query, false, JsonSchemaType.String,
          Format: "date", Description: "Work-date component of the exclusive approval cursor. Supply together with approvalAfterEntryId; omit both to start at the first page."),
        new ManualParameterMetadata("approvalAfterEntryId", ParameterLocation.Query, false, JsonSchemaType.String,
          Format: "uuid", Description: "Entry-id cursor component. Supply together with approvalAfterWorkDate; omit both to start at the first page."),
        new ManualParameterMetadata("approvalPageSize", ParameterLocation.Query, false, JsonSchemaType.Integer,
          Format: "int32", Description: "Approval queue page size from 1 through 100; omitted values default to 50.", Minimum: 1));
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
          Phase: input.Phase,
          MappingVersionId: engagement?.MappingVersionId is not null && input.MappingVersionId is not null ? input.MappingVersionId : null,
          FsliCode: input.MappingVersionId is not null ? input.FsliCode : null), ct);
      });
    }).WithT046Response<UiValueResponse<Guid>>(includeConflict: true);
    group.MapPost("/practice/time/entries", (TimeDraftInput input, HttpContext http) =>
      TryDate(input.WorkDate, out var date)
        ? CommandAsync(http, (db, actor, ct) => PracticeTimeService.SaveTimeDraftAsync(db, actor, new SaveTimeDraftRequest(input.TaskId, date, input.StartMinute ?? 540, input.DurationMinutes,
            input.Role, input.Activity, input.Billable ? PracticeTimeStates.Billable : PracticeTimeStates.NonBillable, input.Narrative ?? "",
            PracticeTimeStates.NarrativeInternal, "QAR"), ct))
        : Task.FromResult(Invalid("Enter the work date.")))
      .WithT046Response<UiValueResponse<Guid>>();
    group.MapPost("/practice/time/entries/{id:guid}/submit", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.SubmitTimeAsync(db, actor, id, ct)))
      .WithT046Response<UiValueResponse<bool>>();
    group.MapPost("/practice/time/entries/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.ApproveTimeAsync(db, actor, id, ct)))
      .WithT046Response<UiValueResponse<bool>>();
    group.MapPost("/practice/time/entries/{id:guid}/return", (Guid id, TimeReturnInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.ReturnTimeAsync(db, actor, id, input.Reason, ct)))
      .WithT046Response<UiValueResponse<bool>>();
  }

  private static bool TryGetSinglePracticeTimeQueryValue(IQueryCollection query, string key, out string? value)
  {
    value = null;
    if (!query.TryGetValue(key, out var values)) return true;
    if (values.Count != 1 || string.IsNullOrEmpty(values[0])) return false;
    value = values[0];
    return true;
  }
}
