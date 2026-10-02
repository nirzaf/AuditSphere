using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PbcCreateInput(string Description, Guid ClientOwnerId, Guid ReviewerId, string DueDate, string? RequestedFormat);
  public sealed record PbcMoreInput(string Body);
  public sealed record PbcCompleteInput(string DeclaredSha256);

  private static string PublicBaseUrl(HttpContext http) =>
    http.RequestServices.GetRequiredService<IConfiguration>()["Application:PublicBaseUrl"] ?? $"{http.Request.Scheme}://{http.Request.Host}/";

  private static void MapPbcEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{id:guid}/pbc", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PbcInboxWorkspaceQuery.GetAsync(db, actor, id, ct)));
    group.MapPost("/engagements/{id:guid}/pbc", (Guid id, PbcCreateInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PbcInboxWorkspaceQuery.CreateAndSendAsync(db, actor, id, i.Description ?? "", i.ClientOwnerId, i.ReviewerId,
        i.DueDate ?? "", i.RequestedFormat, PublicBaseUrl(http), ct)));
    group.MapPost("/pbc-requests/{id:guid}/more-files", (Guid id, PbcMoreInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PbcService.RequestMoreFilesAsync(db, actor, new PbcMessageRequest(id, i.Body ?? "", PublicBaseUrl(http)), ct)));
    // The staff boundary verifies the staged bytes and binds a durable transfer; the declared hash is the only admissible identity.
    group.MapPost("/pbc-uploads/{id:guid}/complete", (Guid id, PbcCompleteInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PbcService.CompleteUploadAsync(db, actor, new CompletePbcUploadRequest(id, i.DeclaredSha256 ?? ""),
        http.RequestServices.GetRequiredService<IOperationStore>(), http.RequestServices.GetRequiredService<PbcDocumentTransferHandler>(), ct)));
  }
}
