using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record ApplicabilityInput(string Decision);
  public sealed record AggregateConclusionInput(string Conclusion);
  public sealed record SamplingInput(Guid ProcedureId, Guid ScheduleId, string Method, string? Interval, string? KeyItemThreshold, int? SampleSize, int? Seed, string Rationale);
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
    group.MapPost("/engagements/{id:guid}/fieldwork/program", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkWorkspaceQuery.PublishAndAdoptAsync(db, actor, id, AuditFieldworkWorkspaceQuery.DefaultProgramVersion, ct)));
    group.MapPost("/procedures/{id:guid}/applicability", (Guid id, ApplicabilityInput i, HttpContext http) =>
      i.Decision is AuditApplicabilityStatuses.Applicable or AuditApplicabilityStatuses.NotApplicablePendingReview
        ? CommandAsync(http, (db, actor, ct) => AuditProgramService.DecideApplicabilityAsync(db, actor, new DecideProcedureApplicabilityRequest(id, i.Decision,
            i.Decision == AuditApplicabilityStatuses.NotApplicablePendingReview ? "Not applicable pending independent review." : null), ct))
        : Task.FromResult(Invalid("Choose applicable or not applicable pending review.")));
    group.MapPost("/engagements/{id:guid}/fieldwork/aggregate", (Guid id, AggregateConclusionInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.RecordAreaAssessmentAsync(db, actor, new RecordAreaAssessmentRequest(id, null, AuditAreaCodes.AuditDifferences,
        AuditAreaAssessmentKinds.AggregateDifferences, "audit-differences-aggregate.v1", "{}", null, null, null, null, null, null, null, ["aggregate-difference-schedule"], i.Conclusion ?? ""), ct)));
    group.MapPost("/area-assessments/{id:guid}/review", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.ReviewAreaAssessmentAsync(db, actor, new ReviewAreaAssessmentRequest(id, AuditAreaAssessmentStatuses.Reviewed, null), ct)));
    group.MapPost("/engagements/{id:guid}/fieldwork/sampling", (Guid id, SamplingInput i, HttpContext http) =>
    {
      decimal? interval = TryDecimal(i.Interval, out var iv) ? iv : null, key = TryDecimal(i.KeyItemThreshold, out var k) ? k : null;
      if ((!string.IsNullOrWhiteSpace(i.Interval) && interval is null) || (!string.IsNullOrWhiteSpace(i.KeyItemThreshold) && key is null))
        return Task.FromResult(Invalid("Enter the interval and key-item threshold as numbers."));
      var countBased = i.Method is AuditSamplingMethods.Random or AuditSamplingMethods.Systematic or AuditSamplingMethods.Stratified;
      return CommandAsync(http, async (db, actor, ct) =>
      {
        var r = await AuditFieldworkService.RunSamplingAsync(db, actor, new RunSamplingRequest(id, i.ProcedureId, i.ScheduleId, i.Method,
          i.Method == AuditSamplingMethods.MonetaryUnit ? interval : null, i.Method is AuditSamplingMethods.KeyItem or AuditSamplingMethods.Stratified ? key : null,
          countBased ? i.SampleSize : null, countBased ? i.Seed : null, i.Rationale ?? ""), ct);
        return r.Succeeded
          ? CommandResult<object>.Ok(new { r.Value!.Run.SelectedCount, r.Value.Run.PopulationCount, r.Value.Run.CoveragePercent })
          : CommandResult<object>.Fail(r.ErrorCode!, r.Message!);
      });
    });
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
