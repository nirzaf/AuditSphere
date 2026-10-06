using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record ApplicabilityInput(string Decision);
  public sealed record ProcedureRiskInput(Guid? RiskId);
  public sealed record TailorProcedureInput(string Title, string CustomWording, string? Rationale);
  public sealed record SubmitProcedureResultInput(long? ExpectedInputGeneration, string WorkPerformed, string? StructuredResultJson, string[]? EvidenceReferences, string Conclusion);
  public sealed record ReviewProcedureResultInput(string Decision, string? Comment);
  public sealed record AggregateConclusionInput(string Conclusion);
  public sealed record SamplingInput(Guid ProcedureId, Guid ScheduleId, string Method, string? Interval, string? KeyItemThreshold, int? SampleSize, int? Seed,
    string Rationale, string[]? AttributeFields = null, string? ExpectedPreviewDigest = null);
  public sealed record SelectionReviewInput(string Decision, string? Comment);
  public sealed record SamplingItemTestInput(string WorkPerformed, string[]? EvidenceReferences, string Result, string? ExceptionAmount,
    string? ContradictoryEvidence, string? FollowUp);
  public sealed record SamplingItemTestReviewInput(string Decision, string? Comment);
  public sealed record EvidenceLinkInput(Guid UploadIntentId, string? Note);
  public sealed record PhysicalItemInput(string FileIndex, string BoxReference, string Description, string Location);
  public sealed record PhysicalLinkInput(Guid ProcedureId);
  public sealed record PhysicalMoveInput(string ToLocation);
  public sealed record AdHocInput(string Title, string Wording, string Reason, string? SectionTitle);
  public sealed record ReviewNoteInput(Guid ResultId, string Field, string Excerpt, string Body);
  public sealed record ReviewNoteEventInput(string Kind, string? Body);

  private static void MapFieldworkEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{id:guid}/fieldwork", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AuditFieldworkWorkspaceQuery.GetAsync(db, actor, id, ct)));
    group.MapGet("/procedures/{id:guid}/review", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AuditFieldworkWorkspaceQuery.ProcedureAsync(db, actor, id, ct)));
    group.MapPost("/procedures/{id:guid}/tailor", (Guid id, TailorProcedureInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditProgramService.TailorProcedureAsync(db, actor, new TailorProcedureRequest(id, i.Title ?? "", i.CustomWording ?? "", i.Rationale), ct)));
    group.MapPost("/procedures/{id:guid}/workpaper", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditProgramService.GetOrCreateWorkpaperAsync(db, actor, id, ct)));
    group.MapPost("/procedures/{id:guid}/results", (Guid id, SubmitProcedureResultInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditProgramService.SubmitResultAsync(db, actor, new SubmitProcedureResultRequest(id, i.ExpectedInputGeneration ?? 1, i.WorkPerformed ?? "", i.StructuredResultJson ?? "{}", i.EvidenceReferences ?? [], i.Conclusion ?? ""), ct)));
    group.MapPost("/procedure-results/{id:guid}/review", (Guid id, ReviewProcedureResultInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditProgramService.ReviewResultAsync(db, actor, new ReviewProcedureResultRequest(id, i.Decision ?? "", i.Comment), ct)));
    group.MapPost("/engagements/{id:guid}/fieldwork/program", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkWorkspaceQuery.PublishAndAdoptAsync(db, actor, id, AuditFieldworkWorkspaceQuery.DefaultProgramVersion, ct)));
    group.MapPost("/procedures/{id:guid}/applicability", (Guid id, ApplicabilityInput i, HttpContext http) =>
      i.Decision is AuditApplicabilityStatuses.Applicable or AuditApplicabilityStatuses.NotApplicablePendingReview
        ? CommandAsync(http, (db, actor, ct) => AuditProgramService.DecideApplicabilityAsync(db, actor, new DecideProcedureApplicabilityRequest(id, i.Decision,
            i.Decision == AuditApplicabilityStatuses.NotApplicablePendingReview ? "Not applicable pending independent review." : null), ct))
        : Task.FromResult(Invalid("Choose applicable or not applicable pending review.")));
    group.MapPost("/procedures/{id:guid}/risk", (Guid id, ProcedureRiskInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditProgramService.LinkRiskAsync(db, actor, id, i.RiskId, ct)));
    group.MapPost("/engagements/{id:guid}/fieldwork/aggregate", (Guid id, AggregateConclusionInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.RecordAreaAssessmentAsync(db, actor, new RecordAreaAssessmentRequest(id, null, AuditAreaCodes.AuditDifferences,
        AuditAreaAssessmentKinds.AggregateDifferences, "audit-differences-aggregate.v1", "{}", null, null, null, null, null, null, null, ["aggregate-difference-schedule"], i.Conclusion ?? ""), ct)));
    group.MapPost("/area-assessments/{id:guid}/review", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.ReviewAreaAssessmentAsync(db, actor, new ReviewAreaAssessmentRequest(id, AuditAreaAssessmentStatuses.Reviewed, null), ct)));
    group.MapPost("/engagements/{id:guid}/fieldwork/sampling/preview", (Guid id, SamplingInput i, HttpContext http) =>
    {
      decimal? interval = TryDecimal(i.Interval, out var iv) ? iv : null, key = TryDecimal(i.KeyItemThreshold, out var k) ? k : null;
      if ((!string.IsNullOrWhiteSpace(i.Interval) && interval is null) || (!string.IsNullOrWhiteSpace(i.KeyItemThreshold) && key is null))
        return Task.FromResult(Invalid("Enter the interval and key-item threshold as numbers."));
      return CommandAsync(http, async (db, actor, ct) =>
      {
        var r = await AuditFieldworkService.PreviewSamplingAsync(db, actor, new RunSamplingRequest(id, i.ProcedureId, i.ScheduleId, i.Method,
          interval, key, i.SampleSize, i.Seed, i.Rationale ?? "", i.AttributeFields), ct);
        return r.Succeeded
          ? CommandResult<object>.Ok(r.Value!)
          : CommandResult<object>.Fail(r.ErrorCode!, r.Message!);
      });
    });
    group.MapPost("/engagements/{id:guid}/fieldwork/sampling", (Guid id, SamplingInput i, HttpContext http) =>
    {
      decimal? interval = TryDecimal(i.Interval, out var iv) ? iv : null, key = TryDecimal(i.KeyItemThreshold, out var k) ? k : null;
      if ((!string.IsNullOrWhiteSpace(i.Interval) && interval is null) || (!string.IsNullOrWhiteSpace(i.KeyItemThreshold) && key is null))
        return Task.FromResult(Invalid("Enter the interval and key-item threshold as numbers."));
      return CommandAsync(http, async (db, actor, ct) =>
      {
        var r = await AuditFieldworkService.RunSamplingAsync(db, actor, new RunSamplingRequest(id, i.ProcedureId, i.ScheduleId, i.Method,
          interval, key, i.SampleSize, i.Seed, i.Rationale ?? "", i.AttributeFields, i.ExpectedPreviewDigest), ct);
        return r.Succeeded
          ? CommandResult<object>.Ok(new { r.Value!.Run.SelectedCount, r.Value.Run.PopulationCount, r.Value.Run.CoveragePercent,
            selectionId = r.Value.Run.SelectionId, previewDigest = r.Value.Run.PreviewDigest })
          : CommandResult<object>.Fail(r.ErrorCode!, r.Message!);
      });
    });
    group.MapGet("/selections/{id:guid}/sample-set", (Guid id, int? page, int? pageSize, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AuditSamplingService.GetSampleSetAsync(db, actor, id, page ?? 1, pageSize ?? 100, ct)));
    group.MapPost("/selections/{id:guid}/review", (Guid id, SelectionReviewInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.ReviewSelectionAsync(db, actor,
        new ReviewSelectionRequest(id, i.Decision ?? "", i.Comment), ct)));
    group.MapPost("/selection-items/{id:guid}/tests", (Guid id, SamplingItemTestInput i, HttpContext http) =>
    {
      decimal? exceptionAmount = null;
      if (!string.IsNullOrWhiteSpace(i.ExceptionAmount))
      {
        if (!TryDecimal(i.ExceptionAmount, out var parsed)) return Task.FromResult(Invalid("Enter the exception amount as a decimal."));
        exceptionAmount = parsed;
      }
      return CommandAsync(http, (db, actor, ct) => AuditFieldworkService.RecordItemTestAsync(db, actor,
        new RecordItemTestRequest(id, i.WorkPerformed ?? "", i.EvidenceReferences ?? [], i.Result ?? "", exceptionAmount,
          i.ContradictoryEvidence, i.FollowUp), ct));
    });
    group.MapPost("/item-tests/{id:guid}/review", (Guid id, SamplingItemTestReviewInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.ReviewItemTestAsync(db, actor,
        new ReviewItemTestRequest(id, i.Decision ?? "", i.Comment), ct)));
    group.MapPost("/procedures/{id:guid}/evidence", (Guid id, EvidenceLinkInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.LinkClientEvidenceAsync(db, actor, id, i.UploadIntentId, i.Note, ct)));
    group.MapPost("/engagements/{id:guid}/fieldwork/physical", (Guid id, PhysicalItemInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.RegisterPhysicalItemAsync(db, actor, id, i.FileIndex ?? "", i.BoxReference ?? "", i.Description ?? "", i.Location ?? "", ct)));
    group.MapPost("/physical-items/{id:guid}/link", (Guid id, PhysicalLinkInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.LinkPhysicalItemAsync(db, actor, i.ProcedureId, id, ct)));
    group.MapPost("/physical-items/{id:guid}/move", (Guid id, PhysicalMoveInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.MovePhysicalItemAsync(db, actor, id, i.ToLocation ?? "", "Moved from the fieldwork page", ct)));
    group.MapPost("/engagements/{id:guid}/fieldwork/ad-hoc", (Guid id, AdHocInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.InsertAdHocProcedureAsync(db, actor, new InsertAdHocProcedureRequest(id, i.Title ?? "", i.Wording ?? "",
        i.Reason ?? "", i.SectionTitle, null), ct)));
    group.MapPost("/review-notes", (ReviewNoteInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ReviewNotesService.AddNoteAsync(db, actor, new AddReviewNoteRequest(i.ResultId, i.Field, i.Excerpt, i.Body), ct)));
    group.MapPost("/review-notes/{id:guid}/events", (Guid id, ReviewNoteEventInput i, HttpContext http) =>
      i.Kind switch
      {
        "RESOLVED" => CommandAsync(http, (db, actor, ct) => ReviewNotesService.ResolveAsync(db, actor, id, string.IsNullOrWhiteSpace(i.Body) ? "Resolved." : i.Body, ct)),
        "RESPONSE" => CommandAsync(http, (db, actor, ct) => ReviewNotesService.RespondAsync(db, actor, id, i.Body ?? "", ct)),
        _ => Task.FromResult(Invalid("Respond to or resolve the note.")),
      });
  }
}
