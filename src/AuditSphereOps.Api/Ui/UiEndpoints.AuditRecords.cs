using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record FindingResponseInput(string ManagementResponse, bool Corrected);
  public sealed record ManagementLetterDesignationInput(bool Designated, string? Recommendation);
  public sealed record IssueInput(long ExpectedRevision, string ManifestDigest, string ReleaseKey);
  public sealed record WorkpaperDraftInput(long ExpectedDraftRevision, long BaseWorkpaperRevision, long BaseInputGeneration, long BasePolicyGeneration, Guid SaveId,
    string WorkPerformed, string Conclusion);
  public sealed record WorkpaperSubmitInput(long ExpectedRevision, string WorkPerformed, string Conclusion, long ExpectedDraftRevision, Guid DraftSaveId);
  public sealed record WorkpaperDiscardInput(long ExpectedDraftRevision);
  public sealed record PrepareAuditProgramInput(string Version, string SourceHash);
  public sealed record PublishAuditProgramInput(string ExpectedContentDigest, string PublicationReviewReference);

  private static void MapAuditRecordEndpoints(RouteGroupBuilder group)
  {
    const string reviewCommands = "/reviews/{reviewPointId:guid}/commands";
    group.MapGet(reviewCommands + "/state", (Guid reviewPointId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ReviewPointCommandWorkspace.StateAsync(db, actor, reviewPointId, ct)))
      .WithUiResponse<ReviewPointCommandState>(includeConflict: true);
    group.MapPost(reviewCommands + "/preview", (Guid reviewPointId, ReviewPointCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ReviewPointCommandWorkspace.PreviewAsync(db, actor, reviewPointId, input, ct)))
      .WithUiResponse<UiValueResponse<ReviewPointCommandPreview>>(includeConflict: true);
    group.MapPost(reviewCommands + "/actions", (Guid reviewPointId, ReviewPointCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ReviewPointCommandWorkspace.ExecuteAsync(db, actor, reviewPointId, input, ct)))
      .WithUiResponse<UiValueResponse<ReviewPointCommandReceiptView>>(includeConflict: true);
    group.MapGet(reviewCommands + "/receipts/{requestId:guid}", (Guid reviewPointId, Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ReviewPointCommandWorkspace.LookupAsync(db, actor, reviewPointId, requestId, requestHash, ct)))
      .WithUiResponse<ReviewPointCommandLookup>(includeConflict: true);

    const string findingCommands = "/findings/{findingId:guid}/commands";
    group.MapGet(findingCommands + "/state", (Guid findingId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FindingCommandWorkspace.StateAsync(db, actor, findingId, ct)))
      .WithUiResponse<FindingCommandState>(includeConflict: true);
    group.MapPost(findingCommands + "/preview", (Guid findingId, FindingCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FindingCommandWorkspace.PreviewAsync(db, actor, findingId, input, ct)))
      .WithUiResponse<UiValueResponse<FindingCommandPreview>>(includeConflict: true);
    group.MapPost(findingCommands + "/actions", (Guid findingId, FindingCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FindingCommandWorkspace.ExecuteAsync(db, actor, findingId, input, ct)))
      .WithUiResponse<UiValueResponse<FindingCommandReceiptView>>(includeConflict: true);
    group.MapGet(findingCommands + "/receipts/{requestId:guid}", (Guid findingId, Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FindingCommandWorkspace.LookupAsync(db, actor, findingId, requestId, requestHash, ct)))
      .WithUiResponse<FindingCommandLookup>(includeConflict: true);

    group.MapGet("/audit/library", (string? version, HttpContext http) => ReadAsync(http, (db, actor, ct) =>
      AuditProgramLibraryQuery.GetLibraryAsync(db, actor, version is { Length: > 0 } v ? v : null, ct)))
      .WithUiResponse<AuditProgramLibraryView>();
    group.MapPost("/audit/library/versions/prepare", (PrepareAuditProgramInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditProgramService.PrepareControlledAuditProgramAsync(db, actor,
        new PrepareAuditProgramRequest(input.Version, input.SourceHash), ct)))
      .WithUiResponse<UiValueResponse<AuditProgramResult>>(includeConflict: true);
    group.MapPost("/audit/library/versions/{versionId:guid}/publish", (Guid versionId, PublishAuditProgramInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, actor,
        new ReviewAuditProgramPublicationRequest(versionId, input.ExpectedContentDigest, input.PublicationReviewReference), ct)))
      .WithUiResponse<UiValueResponse<AuditProgramResult>>(includeConflict: true);
    group.MapGet("/audit/library/{versionId:guid}/sections/{section:int}", (Guid versionId, int section, string? search, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AuditProgramLibraryQuery.GetSectionAsync(db, actor, versionId, section, string.IsNullOrWhiteSpace(search) ? null : search, ct: ct)))
      .WithUiResponse<AuditProgramSectionPage>();
    group.MapGet("/audit/populations/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AuditRecordQueries.PopulationAsync(db, actor, id, ct)))
      .WithUiResponse<PopulationRecord>();
    group.MapGet("/findings/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AuditRecordQueries.FindingAsync(db, actor, id, ct)))
      .WithUiResponse<FindingRecordView>();
    group.MapPost("/findings/{id:guid}/response", (Guid id, FindingResponseInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.RecordFindingResponseAsync(db, actor, new RecordFindingResponseRequest(id, i.ManagementResponse ?? "", i.Corrected), ct)))
      .WithUiResponse<UiValueResponse<FindingResponseResult>>(includeConflict: true);
    group.MapPost("/findings/{id:guid}/management-letter-designation", (Guid id, ManagementLetterDesignationInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.DesignateManagementLetterFindingAsync(db, actor,
        new RecordManagementLetterDesignationRequest(id, i.Designated, i.Recommendation), ct)))
      .WithUiResponse<UiValueResponse<ManagementLetterDesignationResult>>(includeConflict: true);
    group.MapGet("/reviews/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AuditRecordQueries.ReviewPointAsync(db, actor, id, ct)))
      .WithUiResponse<ReviewPointView>(includeConflict: true);
    group.MapPost("/reviews/{id:guid}/disposition", (Guid id, ReviewPointDispositionInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ReviewPointCommandWorkspace.ReviseAsync(db, actor, id, i, ct)))
      .WithUiResponse<UiValueResponse<bool>>(includeConflict: true);
    group.MapGet("/releases/{id:guid}", (Guid id, HttpContext http, ReleaseSafetyOptions safety) =>
      ReadAsync(http, (db, actor, ct) => AuditRecordQueries.ReleaseCandidateAsync(db, actor, id, safety, DateTimeOffset.UtcNow, ct)))
      .WithUiResponse<ReleaseCandidateView>();
    // The release key is a one-time authorization input: it is never echoed, logged or stored by the browser.
    group.MapPost("/releases/{id:guid}/issue", (Guid id, IssueInput i, HttpContext http, ReleaseSafetyOptions safety) =>
      CommandAsync(http, (db, actor, ct) => ReleaseService.IssueAsync(db, actor, new IssueReleaseRequest(id, i.ExpectedRevision, i.ManifestDigest ?? "", i.ReleaseKey ?? ""), safety, ct)))
      .WithUiResponse<UiValueResponse<Guid>>(includeConflict: true);
    group.MapGet("/records/archives/{id:guid}", (Guid id, int? afterOrdinal, HttpContext http) =>
    {
      if (afterOrdinal is < 0) return Task.FromResult<IResult>(Invalid("The archive cursor is invalid."));
      return ReadAsync(http, (db, actor, ct) => AuditRecordQueries.ArchiveAsync(db, actor, id, afterOrdinal, ct));
    }).WithUiResponse<ArchiveRecordView>();
    group.MapGet("/records/archives/{id:guid}/export", async (Guid id, HttpContext http) =>
    {
      var resolver = http.RequestServices.GetRequiredService<TrustedActorResolver>();
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      await using var db = await http.RequestServices.GetRequiredService<IDbContextFactory<AuditSphereDbContext>>()
        .CreateDbContextAsync(http.RequestAborted);
      var result = await RecordsArchiveService.ExportReviewedArchiveAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded)
        return Failure(result.ErrorCode, result.Message,
          result.ErrorCode == AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied ? 403 : StatusFor(result.ErrorCode));
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Failure("session.unavailable", "Sign in again.", 401);

      var export = result.Value!;
      http.Response.Headers.CacheControl = "no-store";
      http.Response.Headers["X-Content-SHA256"] = export.ContentSha256;
      http.Response.Headers["X-AuditSphere-Manifest-SHA256"] = export.ManifestSha256;
      http.Response.Headers["X-AuditSphere-Provider-Protection"] = export.ProviderProtectionState;
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      return Results.File(export.Content, "application/json; charset=utf-8", export.FileName,
        enableRangeProcessing: false);
    }).Produces<Stream>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest)
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden);
    const string workpaperCommands="/audit/workpapers/{id:guid}/commands";
    group.MapGet(workpaperCommands+"/state",(Guid id,HttpContext http)=>ReadAsync(http,(db,actor,ct)=>WorkpaperCommandWorkspace.StateAsync(db,actor,id,ct)))
      .WithUiResponse<WorkpaperCommandState>(includeConflict: true);
    group.MapPost(workpaperCommands+"/prepare-save",(Guid id,WorkpaperCommandRequest input,HttpContext http)=>CommandAsync(http,(db,actor,ct)=>WorkpaperCommandWorkspace.PrepareSaveAsync(db,actor,id,input,ct)))
      .WithUiResponse<UiValueResponse<WorkpaperSaveIntent>>(includeConflict: true);
    group.MapPost(workpaperCommands+"/preview",(Guid id,WorkpaperCommandRequest input,HttpContext http)=>CommandAsync(http,(db,actor,ct)=>WorkpaperCommandWorkspace.PreviewAsync(db,actor,id,input,ct)))
      .WithUiResponse<UiValueResponse<WorkpaperCommandPreview>>(includeConflict: true);
    group.MapPost(workpaperCommands+"/recover",(Guid id,WorkpaperCommandRequest input,HttpContext http)=>CommandAsync(http,(db,actor,ct)=>WorkpaperCommandWorkspace.RecoverAsync(db,actor,id,input,ct)))
      .WithUiResponse<UiValueResponse<WorkpaperCommandLookup>>(includeConflict: true);
    group.MapPost(workpaperCommands+"/actions",(Guid id,WorkpaperCommandRequest input,HttpContext http)=>CommandAsync(http,(db,actor,ct)=>WorkpaperCommandWorkspace.ExecuteAsync(db,actor,id,input,ct)))
      .WithUiResponse<UiValueResponse<WorkpaperCommandReceiptView>>(includeConflict: true);
    group.MapGet(workpaperCommands+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string? requestHash,HttpContext http)=>ReadAsync(http,(db,actor,ct)=>WorkpaperCommandWorkspace.LookupAsync(db,actor,id,requestId,requestHash,ct)))
      .WithUiResponse<WorkpaperCommandLookup>(includeConflict: true);
    group.MapGet("/audit/workpapers/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AuditRecordQueries.WorkpaperAsync(db, actor, id, ct)))
      .WithUiResponse<WorkpaperRecordView>();
    group.MapPost("/audit/workpapers/{id:guid}/draft", (Guid id, WorkpaperDraftInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.SaveWorkpaperDraftAsync(db, actor, new SaveWorkpaperDraftRequest(id, i.ExpectedDraftRevision,
        i.BaseWorkpaperRevision, i.BaseInputGeneration, i.BasePolicyGeneration, i.SaveId, i.WorkPerformed ?? "", i.Conclusion ?? ""), ct)))
      .WithUiResponse<UiValueResponse<WorkpaperDraftResult>>(includeConflict: true);
    group.MapPost("/audit/workpapers/{id:guid}/draft/discard", (Guid id, WorkpaperDiscardInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.DiscardWorkpaperDraftAsync(db, actor, id, i.ExpectedDraftRevision, ct)))
      .WithUiResponse<UiValueResponse<bool>>(includeConflict: true);
    group.MapPost("/audit/workpapers/{id:guid}/submit", (Guid id, WorkpaperSubmitInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.SubmitWorkpaperAsync(db, actor, new SubmitWorkpaperRequest(id, i.ExpectedRevision, i.WorkPerformed ?? "",
        i.Conclusion ?? "", i.ExpectedDraftRevision, i.DraftSaveId), ct)))
      .WithUiResponse<UiValueResponse<WorkpaperResult>>(includeConflict: true);
  }
}
