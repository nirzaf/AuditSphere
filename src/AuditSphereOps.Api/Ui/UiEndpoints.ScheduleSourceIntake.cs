using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record ScheduleSourceCaptureFieldsInput(Guid PbcAcceptanceReceiptId,
    Guid AcceptedSourceDecisionId, DateOnly SourceDate, AuditScheduleCsvColumns Columns,
    string[]? SelectedControlAccountCodes);

  public sealed record ScheduleSourceCapturePreviewInput(Guid RequestId,
    ScheduleSourceCaptureFieldsInput Fields);

  public sealed record ScheduleSourceCaptureActionInput(Guid RequestId,
    ScheduleSourceCaptureFieldsInput Fields, string ExpectedManifestHash,
    string ExpectedInputGeneration, string ExpectedPeriodRevision,
    string ExpectedRequestHash, bool Reviewed);

  public sealed record ScheduleSourceReviewPreviewInput(Guid RequestId,
    AuditScheduleSourceReviewFields Fields);

  public sealed record ScheduleSourceReviewActionInput(Guid RequestId,
    string ExpectedCaptureHash, string ExpectedInputGeneration, string ExpectedReviewBasis,
    AuditScheduleSourceReviewFields Fields, string ExpectedRequestHash, bool Reviewed);

  private static void MapScheduleSourceIntakeEndpoints(RouteGroupBuilder group)
  {
    UiEndpointContractMetadata.WithUiResponse<AuditScheduleSourceOptions>(group.MapGet(
      "/engagements/{id:guid}/fieldwork/schedule-intake/options",
      (Guid id, int? pbcOffset, int? trialBalanceOffset, int? limit, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => AuditScheduleSourceWorkspace.GetIntakeOptionsAsync(db, actor,
          id, pbcOffset ?? 0, trialBalanceOffset ?? 0, limit ?? 25, ct))));

    UiEndpointContractMetadata.WithUiResponse<AuditScheduleSourceHeaders>(group.MapGet(
      "/engagements/{id:guid}/fieldwork/schedule-intake/pbc/{receiptId:guid}/headers",
      (Guid id, Guid receiptId, HttpContext http) => ReadAsync(http, (db, actor, ct) =>
        AuditScheduleSourceWorkspace.GetSourceHeadersAsync(db, actor,
          http.RequestServices.GetRequiredService<IAuditScheduleSourceContentReader>(), id, receiptId, ct))));

    UiEndpointContractMetadata.WithUiResponse<AuditScheduleControlAccountPage>(group.MapGet(
      "/engagements/{id:guid}/fieldwork/schedule-intake/trial-balances/{decisionId:guid}/accounts",
      (Guid id, Guid decisionId, DateOnly? sourceDate, int? offset, int? limit, HttpContext http) => sourceDate is null
        ? Task.FromResult(Failure("request.invalid", "Refresh the selected TB period and source date."))
        : ReadAsync(http, (db, actor, ct) => AuditScheduleSourceWorkspace.GetControlAccountsAsync(db,
          actor, id, decisionId, sourceDate.Value, offset ?? 0, limit ?? 100, ct))));

    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<AuditScheduleCapturePreview>>(group.MapPost(
      "/engagements/{id:guid}/fieldwork/schedule-intake/capture/preview",
      (Guid id, ScheduleSourceCapturePreviewInput input, HttpContext http) =>
        input?.Fields is null ? Task.FromResult(Invalid("Select an exact accepted PBC listing and current TB source.")) :
        CommandAsync(http, (db, actor, ct) => AuditScheduleSourceWorkspace.PreviewCaptureAsync(db, actor,
          http.RequestServices.GetRequiredService<IAuditScheduleSourceContentReader>(), id,
          input.RequestId, CaptureFields(input.Fields), ct))), includeConflict: true);

    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<AuditScheduleSourceReceiptView>>(group.MapPost(
      "/engagements/{id:guid}/fieldwork/schedule-intake/capture/actions",
      (Guid id, ScheduleSourceCaptureActionInput input, HttpContext http) =>
        input?.Fields is null || !TryPositiveInt64(input.ExpectedInputGeneration, out var generation) ||
        !TryPositiveInt64(input.ExpectedPeriodRevision, out var periodRevision)
          ? Task.FromResult(Invalid("Refresh and assent to the exact source-capture preview."))
          : CommandAsync(http, (db, actor, ct) => AuditScheduleSourceWorkspace.CaptureAsync(db, actor,
            http.RequestServices.GetRequiredService<IAuditScheduleSourceContentReader>(), id,
            new AuditScheduleCaptureRequest(input.RequestId, CaptureFields(input.Fields),
              input.ExpectedManifestHash ?? "", generation, periodRevision, input.ExpectedRequestHash ?? "", input.Reviewed), ct))),
      includeConflict: true);

    UiEndpointContractMetadata.WithUiResponse<AuditScheduleSourceReceiptLookup>(group.MapGet(
      "/engagements/{id:guid}/fieldwork/schedule-intake/capture/receipts/{requestId:guid}",
      (Guid id, Guid requestId, string? requestHash, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => AuditScheduleSourceWorkspace.GetCaptureReceiptAsync(db,
          actor, id, requestId, requestHash ?? "", ct))), includeConflict: true);

    UiEndpointContractMetadata.WithUiResponse<AuditScheduleReviewCandidatePage>(group.MapGet(
      "/engagements/{id:guid}/fieldwork/schedule-intake/review-candidates",
      (Guid id, int? offset, int? limit, HttpContext http) => ReadAsync(http, (db, actor, ct) =>
        AuditScheduleSourceWorkspace.GetReviewCandidatesAsync(db, actor, id, offset ?? 0, limit ?? 25, ct))));

    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<AuditScheduleSourceReviewPreview>>(group.MapPost(
      "/engagements/{id:guid}/fieldwork/schedule-intake/schedules/{scheduleId:guid}/review/preview",
      (Guid id, Guid scheduleId, ScheduleSourceReviewPreviewInput input, HttpContext http) =>
        input?.Fields is null ? Task.FromResult(Invalid("Enter the independent source-review decision.")) :
        CommandAsync(http, (db, actor, ct) => AuditScheduleSourceWorkspace.PreviewReviewAsync(db,
          actor, http.RequestServices.GetRequiredService<IAuditScheduleSourceContentReader>(), id,
          scheduleId, input.RequestId, input.Fields, ct))), includeConflict: true);

    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<AuditScheduleSourceReceiptView>>(group.MapPost(
      "/engagements/{id:guid}/fieldwork/schedule-intake/schedules/{scheduleId:guid}/review/actions",
      (Guid id, Guid scheduleId, ScheduleSourceReviewActionInput input, HttpContext http) =>
        input?.Fields is null || !TryPositiveInt64(input.ExpectedInputGeneration, out var generation)
          ? Task.FromResult(Invalid("Refresh and assent to the exact independent review preview."))
          : CommandAsync(http, (db, actor, ct) => AuditScheduleSourceWorkspace.ReviewAsync(db, actor,
            http.RequestServices.GetRequiredService<IAuditScheduleSourceContentReader>(), id,
            new AuditScheduleSourceReviewRequest(input.RequestId, scheduleId,
              input.ExpectedCaptureHash ?? "", generation, input.ExpectedReviewBasis ?? "",
              input.Fields, input.ExpectedRequestHash ?? "", input.Reviewed), ct))), includeConflict: true);

    UiEndpointContractMetadata.WithUiResponse<AuditScheduleSourceReceiptLookup>(group.MapGet(
      "/engagements/{id:guid}/fieldwork/schedule-intake/review/receipts/{requestId:guid}",
      (Guid id, Guid requestId, string? requestHash, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => AuditScheduleSourceWorkspace.GetReviewReceiptAsync(db,
          actor, id, requestId, requestHash ?? "", ct))), includeConflict: true);
  }

  private static AuditScheduleSourceFields CaptureFields(ScheduleSourceCaptureFieldsInput input) => new(
    input.PbcAcceptanceReceiptId, input.AcceptedSourceDecisionId, input.SourceDate,
    input.Columns, input.SelectedControlAccountCodes ?? []);

  private static bool TryPositiveInt64(string? value, out long result) =>
    long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result > 0;
}
