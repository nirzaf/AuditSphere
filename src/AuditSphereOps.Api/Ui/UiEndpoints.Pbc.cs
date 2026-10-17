using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PbcCreateInput(string Description, Guid ClientOwnerId, Guid ReviewerId, string DueDate, string? RequestedFormat);
  public sealed record PbcMoreInput(string Body);
  public sealed record PbcStateInput(string State, string ExpectedRevision, string? ClarificationReason,
    Guid? ReviewedUploadIntentId, long? ReviewedUploadRevision, string? ReviewedContentSha256Hex);
  public sealed record PbcCompleteInput(string DeclaredSha256);

  private static string PublicBaseUrl(HttpContext http) =>
    http.RequestServices.GetRequiredService<IConfiguration>()["Application:PublicBaseUrl"] ?? $"{http.Request.Scheme}://{http.Request.Host}/";

  private static void MapPbcEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{id:guid}/pbc", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PbcInboxWorkspaceQuery.GetAsync(db, actor, id, ct)))
      .WithUiResponse<PbcInboxWorkspace>();
    group.MapPost("/engagements/{id:guid}/pbc", (Guid id, PbcCreateInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PbcInboxWorkspaceQuery.CreateAndSendAsync(db, actor, id,
        i.Description ?? "", i.ClientOwnerId, i.ReviewerId, i.DueDate ?? "", i.RequestedFormat,
        PublicBaseUrl(http), ct,
        http.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("WorkflowExperience:Enabled"))))
      .WithUiResponse<UiValueResponse<Guid>>(includeConflict: true);
    group.MapPost("/pbc-requests/{id:guid}/more-files", (Guid id, PbcMoreInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PbcService.RequestMoreFilesAsync(db, actor, new PbcMessageRequest(id, i.Body ?? "", PublicBaseUrl(http)), ct)))
      .WithUiResponse<UiValueResponse<bool>>();
    group.MapPost("/pbc-requests/{id:guid}/state", async (Guid id, PbcStateInput input, HttpContext http) =>
    {
      if (input.State is not (AuditSphereOps.Domain.Documents.PbcStates.UnderReview or AuditSphereOps.Domain.Documents.PbcStates.Accepted) ||
          !long.TryParse(input.ExpectedRevision, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var revision) || revision < 1 ||
          input.ExpectedRevision != revision.ToString(System.Globalization.CultureInfo.InvariantCulture))
        return Results.Json(new { code = "request.invalid" }, statusCode: StatusCodes.Status400BadRequest);
      var reviewedHash = input.ReviewedContentSha256Hex;
      if (input.State == AuditSphereOps.Domain.Documents.PbcStates.Accepted &&
          (input.ReviewedUploadIntentId is null || input.ReviewedUploadIntentId == Guid.Empty ||
           input.ReviewedUploadRevision is null || input.ReviewedUploadRevision <= 0 ||
           reviewedHash is null || reviewedHash.Length != 64 || !reviewedHash.All(Uri.IsHexDigit)))
        return Results.Json(new { code = "request.reviewed-source-required" }, statusCode: StatusCodes.Status400BadRequest);
      return await CommandAsync(http, (db, actor, ct) => PbcService.ChangeStateAsync(db, actor,
        new PbcStateChangeRequest(id, input.State, revision, input.ClarificationReason,
          input.ReviewedUploadIntentId, input.ReviewedUploadRevision, input.ReviewedContentSha256Hex), ct));
    }).WithUiResponse<UiValueResponse<bool>>(includeConflict: true);
    // The staff boundary verifies the staged bytes and binds a durable transfer; the declared hash is the only admissible identity.
    group.MapPost("/pbc-uploads/{id:guid}/complete", (Guid id, PbcCompleteInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PbcService.CompleteUploadAsync(db, actor, new CompletePbcUploadRequest(id, i.DeclaredSha256 ?? ""),
        http.RequestServices.GetRequiredService<IOperationStore>(), http.RequestServices.GetRequiredService<PbcDocumentTransferHandler>(), ct)))
      .WithUiResponse<UiValueResponse<PbcUploadReceipt>>();
  }
}
