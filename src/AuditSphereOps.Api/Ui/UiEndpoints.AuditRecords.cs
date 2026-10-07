using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record FindingResponseInput(string ManagementResponse, bool Corrected);
  public sealed record ManagementLetterDesignationInput(bool Designated, string? Recommendation);
  public sealed record DispositionInput(bool Cleared);
  public sealed record IssueInput(long ExpectedRevision, string ManifestDigest, string ReleaseKey);
  public sealed record WorkpaperDraftInput(long ExpectedDraftRevision, long BaseWorkpaperRevision, long BaseInputGeneration, long BasePolicyGeneration, Guid SaveId,
    string WorkPerformed, string Conclusion);
  public sealed record WorkpaperSubmitInput(long ExpectedRevision, string WorkPerformed, string Conclusion, long ExpectedDraftRevision, Guid DraftSaveId);
  public sealed record WorkpaperDiscardInput(long ExpectedDraftRevision);

  private static void MapAuditRecordEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/audit/library", http => ReadAsync(http, (db, actor, ct) =>
      AuditProgramLibraryQuery.GetLibraryAsync(db, actor, http.Request.Query["version"].FirstOrDefault() is { Length: > 0 } v ? v : null, ct)));
    group.MapGet("/audit/library/{versionId:guid}/sections/{section:int}", (Guid versionId, int section, string? search, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AuditProgramLibraryQuery.GetSectionAsync(db, actor, versionId, section, string.IsNullOrWhiteSpace(search) ? null : search, ct: ct)));
    group.MapGet("/audit/populations/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AuditRecordQueries.PopulationAsync(db, actor, id, ct)));
    group.MapGet("/findings/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AuditRecordQueries.FindingAsync(db, actor, id, ct)));
    group.MapPost("/findings/{id:guid}/response", (Guid id, FindingResponseInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.RecordFindingResponseAsync(db, actor, new RecordFindingResponseRequest(id, i.ManagementResponse ?? "", i.Corrected), ct)));
    group.MapPost("/findings/{id:guid}/management-letter-designation", (Guid id, ManagementLetterDesignationInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.DesignateManagementLetterFindingAsync(db, actor,
        new RecordManagementLetterDesignationRequest(id, i.Designated, i.Recommendation), ct)));
    group.MapGet("/reviews/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AuditRecordQueries.ReviewPointAsync(db, actor, id, ct)));
    group.MapPost("/reviews/{id:guid}/disposition", (Guid id, DispositionInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.SetReviewPointDispositionAsync(db, actor, id, i.Cleared, ct)));
    group.MapGet("/releases/{id:guid}", (Guid id, HttpContext http, ReleaseSafetyOptions safety) =>
      ReadAsync(http, (db, actor, ct) => AuditRecordQueries.ReleaseCandidateAsync(db, actor, id, safety, DateTimeOffset.UtcNow, ct)));
    // The release key is a one-time authorization input: it is never echoed, logged or stored by the browser.
    group.MapPost("/releases/{id:guid}/issue", (Guid id, IssueInput i, HttpContext http, ReleaseSafetyOptions safety) =>
      CommandAsync(http, (db, actor, ct) => ReleaseService.IssueAsync(db, actor, new IssueReleaseRequest(id, i.ExpectedRevision, i.ManifestDigest ?? "", i.ReleaseKey ?? ""), safety, ct)));
    group.MapGet("/records/archives/{id:guid}", (Guid id, int? afterOrdinal, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AuditRecordQueries.ArchiveAsync(db, actor, id, afterOrdinal, ct)));
    group.MapGet("/audit/workpapers/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AuditRecordQueries.WorkpaperAsync(db, actor, id, ct)));
    group.MapPost("/audit/workpapers/{id:guid}/draft", (Guid id, WorkpaperDraftInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.SaveWorkpaperDraftAsync(db, actor, new SaveWorkpaperDraftRequest(id, i.ExpectedDraftRevision,
        i.BaseWorkpaperRevision, i.BaseInputGeneration, i.BasePolicyGeneration, i.SaveId, i.WorkPerformed ?? "", i.Conclusion ?? ""), ct)));
    group.MapPost("/audit/workpapers/{id:guid}/draft/discard", (Guid id, WorkpaperDiscardInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.DiscardWorkpaperDraftAsync(db, actor, id, i.ExpectedDraftRevision, ct)));
    group.MapPost("/audit/workpapers/{id:guid}/submit", (Guid id, WorkpaperSubmitInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.SubmitWorkpaperAsync(db, actor, new SubmitWorkpaperRequest(id, i.ExpectedRevision, i.WorkPerformed ?? "",
        i.Conclusion ?? "", i.ExpectedDraftRevision, i.DraftSaveId), ct)));
  }
}
