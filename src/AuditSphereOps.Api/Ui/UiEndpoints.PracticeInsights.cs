using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record LibraryCreateInput(string Code, string Title, string Category, string Audience, string Body, string SourceReference);
  public sealed record LibraryDraftInput(string Body, string SourceReference, string EffectiveFrom);

  private static void MapPracticeInsightEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/practice/analytics", (string from, string to, HttpContext http) =>
      TryDate(from, out var f) && TryDate(to, out var t)
        ? ReadAsync(http, (db, actor, ct) => PracticeAnalyticsQuery.GetAsync(db, actor, f, t, ct))
        : Task.FromResult(Invalid("Choose a from and to date.")));

    group.MapUiGet("/library", http => ReadAsync(http, (db, actor, ct) => TechnicalLibraryWorkspaceQuery.CatalogueAsync(db, actor, ct)));
    group.MapGet("/library/search", (string term, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => TechnicalLibraryWorkspaceQuery.SearchAsync(db, actor, term ?? "", ct)));
    group.MapGet("/library/{id:guid}", (Guid id, int? v, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => TechnicalLibraryWorkspaceQuery.OpenAsync(db, actor, id, v, ct)));
    group.MapPost("/library", (LibraryCreateInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => TechnicalLibraryService.CreateAsync(db, actor, input.Code, input.Title, input.Category, input.Audience,
        input.Body, input.SourceReference, DateOnly.FromDateTime(DateTime.UtcNow), ct)));
    group.MapPost("/library/{id:guid}/versions", (Guid id, LibraryDraftInput input, HttpContext http) =>
      TryDate(input.EffectiveFrom, out var effective)
        ? CommandAsync(http, (db, actor, ct) => TechnicalLibraryService.DraftVersionAsync(db, actor, id, input.Body, input.SourceReference, effective, ct))
        : Task.FromResult(Invalid("Choose an effective date.")));
    group.MapPost("/library/versions/{versionId:guid}/publish", (Guid versionId, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => TechnicalLibraryService.PublishAsync(db, actor, versionId, ct)));
  }
}
