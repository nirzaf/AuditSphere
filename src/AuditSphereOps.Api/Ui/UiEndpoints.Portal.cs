using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PortalFirstSignInInput(bool Acknowledged);
  public sealed record PortalReplyInput(string Body);
  public sealed record PortalDelegateInput(Guid UserId);
  public sealed record PortalUploadInput(string FileName, string ContentType, string ByteCount, string Sha256);
  public sealed record PortalUploadResumeInput(string FileName, string ByteCount, string Sha256);

  public sealed record PortalPackageDecisionInput(string Decision, string EvidenceReference, string Comment, string ExpectedHash, bool Reviewed);
  public sealed record PortalAcknowledgementInput(string Sha256, bool Reviewed);

  private static void MapPortalEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/portal", (int? page, int? pageSize, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => ClientPortalWorkspaceQuery.GetAsync(db, actor, page ?? 0, pageSize ?? 50, ct)));
    group.MapGet("/portal/requests/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => ClientPortalWorkspaceQuery.RequestAsync(db, actor, id, ct)));
    group.MapUiGet("/portal/documents", (HttpContext http) => ReadAsync(http,
      (db, actor, ct) => ClientPortalReviewQuery.GetAsync(db, actor, ct)));
    group.MapGet("/portal/accounting/packages/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => FinancialPackageReviewService.GetClientViewAsync(db, actor, id, ct)));
    group.MapPost("/portal/accounting/packages/{id:guid}/decision", (Guid id, PortalPackageDecisionInput i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        var current = await FinancialPackageReviewService.GetClientViewAsync(db, actor, id, ct);
        if (!current.Succeeded) return CommandResult<Guid>.Fail(current.ErrorCode!, "Package unavailable.");
        if (!i.Reviewed || current.Value!.PackageHash != i.ExpectedHash)
          return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Review the current package before recording a decision.");
        return await FinancialPackageReviewService.RecordAsync(db, actor, new(id, "MANAGEMENT_APPROVAL", i.Decision, "SIGNED_IN", i.EvidenceReference ?? "", i.Comment ?? ""), ct);
      }));
    group.MapPost("/portal/documents/{id:guid}/comment", (Guid id, PortalReplyInput i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => ClientPortalReviewCommands.CommentAsync(db, actor, id, i.Body ?? "", ct)));
    group.MapPost("/portal/documents/{id:guid}/acknowledge", (Guid id, PortalAcknowledgementInput i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => i.Reviewed ? ClientPortalReviewCommands.AcknowledgeAsync(db, actor, id, i.Sha256 ?? "", ct) :
        Task.FromResult(CommandResult.Fail("review.required", "Review the exact document before acknowledgement."))));
    group.MapPost("/portal/documents/{id:guid}/signed-representation", (Guid id, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        var upload = await ReadUploadAsync(http, "file", AuditDeliverableService.MaxSignedLetterBytes);
        if (upload is null) return CommandResult<Guid>.Fail("request.invalid", "Upload a PDF of at most 10 MB.");
        var form = await http.Request.ReadFormAsync(ct);
        return await AuditDeliverableService.UploadSignedRepresentationAsync(db, actor, id, form["expectedHash"].ToString(), form["signatory"].ToString(), upload.Value.Content, ct);
      }));
    group.MapPost("/portal/first-sign-in", (PortalFirstSignInInput i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => ClientPortalService.CompleteFirstSignInAsync(db, actor, i.Acknowledged, ct)));
    group.MapPost("/portal/requests/{id:guid}/reply", (Guid id, PortalReplyInput i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => PbcService.ReplyAsync(db, actor, id, i.Body ?? "", ct)));
    group.MapPost("/portal/requests/{id:guid}/delegations", (Guid id, PortalDelegateInput i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => ClientPortalService.DelegateRequestAsync(db, actor, id, i.UserId, ct)));
    group.MapPost("/portal/requests/{id:guid}/delegations/{delegationId:guid}/revoke", (Guid id, Guid delegationId, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        var current = await ClientPortalWorkspaceQuery.RequestAsync(db, actor, id, ct);
        if (!current.Succeeded || !current.Value!.Delegations.Any(x => x.Id == delegationId))
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Delegation unavailable.");
        return await ClientPortalService.RevokeDelegationAsync(db, actor, delegationId, ct);
      }));
    group.MapPost("/portal/requests/{id:guid}/uploads", (Guid id, PortalUploadInput i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        var current = await ClientPortalWorkspaceQuery.RequestAsync(db, actor, id, ct);
        if (!current.Succeeded) return CommandResult<PbcUploadReceipt>.Fail(current.ErrorCode!, "Request unavailable.");
        if (!long.TryParse(i.ByteCount, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count))
          return CommandResult<PbcUploadReceipt>.Fail("request.invalid", "File size is invalid.");
        return await PbcService.StartUploadAsync(db, actor, new(id, i.FileName ?? "", i.ContentType ?? "application/octet-stream", count, i.Sha256 ?? ""), ct);
      }));
    group.MapPost("/portal/requests/{id:guid}/uploads/{uploadId:guid}/resume", (Guid id, Guid uploadId, PortalUploadResumeInput i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => long.TryParse(i.ByteCount, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var count)
        ? PbcService.ResumeUploadAsync(db, actor, new(id, uploadId, i.FileName ?? "", count, i.Sha256 ?? ""), ct)
        : Task.FromResult(CommandResult<PbcUploadResumeReceipt>.Fail("pbc.upload.invalid", "File size is invalid."))));
  }
}
