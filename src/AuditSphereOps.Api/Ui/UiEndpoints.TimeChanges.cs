using System.Globalization;
using AuditSphereOps.Api.Contracts;
using AuditSphereOps.Application.Practice;
using Microsoft.OpenApi;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record TimeEntryChangeInput(Guid RequestId, string ExpectedRevision, string Kind,
    string WorkDate, int StartMinute, int DurationMinutes, string Activity, string? Narrative, string? Reason);

  private static void MapTimeChangeEndpoints(RouteGroupBuilder group)
  {
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<Guid>>(
      group.MapPost("/practice/time/entries/{id:guid}/changes", (Guid id, TimeEntryChangeInput input, HttpContext http) =>
      {
        if (input.RequestId == Guid.Empty ||
            !long.TryParse(input.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1 ||
            input.Kind is not ("EDIT_UNAPPROVED" or "CORRECT_APPROVED") || !TryDate(input.WorkDate, out var workDate))
          return Task.FromResult(Invalid("Provide a request identity, exact retained revision, supported change and ISO work date."));
        if (string.IsNullOrWhiteSpace(input.Activity) || string.IsNullOrWhiteSpace(input.Reason))
          return Task.FromResult(Invalid("A time activity and an attributable change reason are required."));
        return CommandAsync(http, (db, actor, ct) => PracticeTimeService.ReviseTimeEntryAsync(db, actor,
          new ReviseTimeEntryRequest(id, input.RequestId, revision, input.Kind, workDate, input.StartMinute,
            input.DurationMinutes, input.Activity, input.Narrative ?? "", input.Reason), ct));
      }));

    UiEndpointContractMetadata.WithUiResponse<TimeEntryChangeLookup>(
      group.MapGet("/practice/time/entries/{id:guid}/changes/requests/{requestId:guid}",
        (Guid id, Guid requestId, HttpContext http) => ReadAsync(http,
          (db, actor, ct) => PracticeTimeChangeQuery.ReceiptAsync(db, actor, id, requestId, ct))));

    UiEndpointContractMetadata.WithUiResponse<TimeEntryChangePage>(
      group.MapGet("/practice/time/entries/{id:guid}/changes", (Guid id, HttpContext http) =>
      {
        if (!TryGetSinglePracticeTimeQueryValue(http.Request.Query, "beforeReceiptId", out var rawCursor))
          return Task.FromResult(Invalid("Use one retained change cursor."));
        Guid? cursor = null;
        if (rawCursor is not null)
        {
          if (!Guid.TryParse(rawCursor, out var parsed) || parsed == Guid.Empty)
            return Task.FromResult(Invalid("Use a nonempty retained change cursor."));
          cursor = parsed;
        }
        return ReadAsync(http, (db, actor, ct) => PracticeTimeChangeQuery.HistoryAsync(db, actor, id,
          beforeReceiptId: cursor, ct: ct));
      })).WithMetadata(new ManualParameterMetadata("beforeReceiptId", ParameterLocation.Query, false,
        JsonSchemaType.String, Format: "uuid", Description: "Exclusive cursor from this entry's retained change page; omit for the newest page."));
  }
}
