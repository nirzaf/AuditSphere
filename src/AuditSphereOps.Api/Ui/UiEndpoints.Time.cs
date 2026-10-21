using System.Globalization;
using System.Text.RegularExpressions;
using AuditSphereOps.Api.Contracts;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.OpenApi;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static readonly Regex PracticeTimeCursorInstantPattern = new(
    @"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})\z",
    RegexOptions.CultureInvariant);

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
      if (!TryGetSinglePracticeTimeQueryValue(query, "entriesBeforeWorkDate", out var rawEntriesBeforeWorkDate) ||
          !TryGetSinglePracticeTimeQueryValue(query, "entriesBeforeCreatedAt", out var rawEntriesBeforeCreatedAt) ||
          !TryGetSinglePracticeTimeQueryValue(query, "entriesBeforeEntryId", out var rawEntriesBeforeEntryId) ||
          !TryGetSinglePracticeTimeQueryValue(query, "entriesPageSize", out var rawEntriesPageSize))
        return Task.FromResult(Invalid("Use one valid value for each personal history paging parameter."));

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

      DateOnly? entriesBeforeWorkDate = null;
      if (rawEntriesBeforeWorkDate is not null)
      {
        if (!TryDate(rawEntriesBeforeWorkDate, out var parsedDate))
          return Task.FromResult(Invalid("Use an ISO work date and complete history cursor, with a page size from 1 to 100."));
        entriesBeforeWorkDate = parsedDate;
      }
      DateTimeOffset? entriesBeforeCreatedAt = null;
      if (rawEntriesBeforeCreatedAt is not null)
      {
        if (!TryParsePracticeTimeCursorInstant(rawEntriesBeforeCreatedAt, out var parsedInstant))
          return Task.FromResult(Invalid("Use an ISO timestamp with an explicit Z or UTC offset for the history cursor."));
        entriesBeforeCreatedAt = parsedInstant;
      }
      Guid? entriesBeforeEntryId = null;
      if (rawEntriesBeforeEntryId is not null)
      {
        if (!Guid.TryParse(rawEntriesBeforeEntryId, out var parsedEntryId) || parsedEntryId == Guid.Empty)
          return Task.FromResult(Invalid("Use a nonempty entry id in the history cursor."));
        entriesBeforeEntryId = parsedEntryId;
      }
      if ((entriesBeforeWorkDate is null) != (entriesBeforeCreatedAt is null) ||
          (entriesBeforeWorkDate is null) != (entriesBeforeEntryId is null))
        return Task.FromResult(Invalid("Provide all three history cursor values or omit them together."));

      var entriesPageSize = 50;
      if (rawEntriesPageSize is not null &&
          (!int.TryParse(rawEntriesPageSize, NumberStyles.None, CultureInfo.InvariantCulture, out entriesPageSize) || entriesPageSize is < 1 or > 100))
        return Task.FromResult(Invalid("History page size must be a whole number from 1 to 100."));

      var request = new PracticeTimeWorkspaceRequest(afterWorkDate, afterEntryId, pageSize,
        entriesBeforeWorkDate, entriesBeforeCreatedAt, entriesBeforeEntryId, entriesPageSize);
      return ReadAsync(http, (db, actor, ct) => PracticeTimeWorkspaceQuery.GetAsync(db, actor, request, ct));
    })
      .WithT046Response<PracticeTimeWorkspace>()
      .WithMetadata(
        new ManualParameterMetadata("approvalAfterWorkDate", ParameterLocation.Query, false, JsonSchemaType.String,
          Format: "date", Description: "Work-date component of the exclusive approval cursor. Supply together with approvalAfterEntryId; omit both to start at the first page."),
        new ManualParameterMetadata("approvalAfterEntryId", ParameterLocation.Query, false, JsonSchemaType.String,
          Format: "uuid", Description: "Entry-id cursor component. Supply together with approvalAfterWorkDate; omit both to start at the first page."),
        new ManualParameterMetadata("approvalPageSize", ParameterLocation.Query, false, JsonSchemaType.Integer,
          Format: "int32", Description: "Approval queue page size from 1 through 100; omitted values default to 50.", Minimum: 1),
        new ManualParameterMetadata("entriesBeforeWorkDate", ParameterLocation.Query, false, JsonSchemaType.String,
          Format: "date", Description: "Work-date component of the exclusive personal-entry history cursor. Supply with entriesBeforeCreatedAt and entriesBeforeEntryId; omit all three to start at the newest page."),
        new ManualParameterMetadata("entriesBeforeCreatedAt", ParameterLocation.Query, false, JsonSchemaType.String,
          Format: "date-time", Description: "Creation-instant component of the exclusive personal-entry history cursor. Supply with entriesBeforeWorkDate and entriesBeforeEntryId; requires an explicit Z or UTC offset, is normalized to UTC, and must match PostgreSQL's stored microsecond precision."),
        new ManualParameterMetadata("entriesBeforeEntryId", ParameterLocation.Query, false, JsonSchemaType.String,
          Format: "uuid", Description: "Entry-id component of the exclusive personal-entry history cursor. Supply with entriesBeforeWorkDate and entriesBeforeCreatedAt."),
        new ManualParameterMetadata("entriesPageSize", ParameterLocation.Query, false, JsonSchemaType.Integer,
          Format: "int32", Description: "Personal-entry history page size from 1 through 100; omitted values default to 50.", Minimum: 1));
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

  private static bool TryParsePracticeTimeCursorInstant(string value, out DateTimeOffset utcInstant)
  {
    utcInstant = default;
    if (!PracticeTimeCursorInstantPattern.IsMatch(value) ||
        !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
      return false;
    utcInstant = parsed.ToUniversalTime();
    return true;
  }
}
