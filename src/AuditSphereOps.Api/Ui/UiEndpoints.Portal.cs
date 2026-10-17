using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PortalFirstSignInInput(bool Acknowledged);
  public sealed record PortalReplyInput(string Body);
  public sealed record PortalDocumentCommentInput(Guid RequestId, string Body);
  public sealed record PortalDelegateInput(Guid UserId);
  public sealed record PortalUploadInput(string FileName, string ContentType, string ByteCount, string Sha256);
  public sealed record PortalUploadResumeInput(string FileName, string ByteCount, string Sha256);

  public sealed record PortalPackageDecisionInput(string Decision, string EvidenceReference, string Comment, string ExpectedHash, bool Reviewed,
    Guid? PresentationRevisionId = null, Guid? PresentationCompositionId = null, string? CompositionManifestDigest = null,
    Guid? RequestId = null, Guid? ExpectedLatestDecisionId = null);
  public sealed record PortalAcknowledgementInput(string Sha256, bool Reviewed);
  public sealed record PortalBundleAcknowledgementInput(string ContentSha256, string ManifestSha256, bool Received);

  private static void MapPortalEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/portal/branding", http => ReadAsync(http,
      (db, actor, ct) => ClientPortalBrandingQuery.GetAsync(db, actor, ct)));
    group.MapGet("/portal/branding/{profileId:guid}/logo", async (Guid profileId, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPortalBrandingQuery.LogoAsync(db, actor, profileId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Failure("session.unavailable", "Sign in again.", 401);
      if (!result.Succeeded || result.Value is null)
        return Failure(result.ErrorCode, result.Message);
      http.Response.Headers.CacheControl = "no-store";
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      http.Response.Headers["X-Logo-SHA256"] = result.Value.Sha256Hex;
      return Results.File(result.Value.Bytes, "image/png");
    });
    group.MapGet("/portal", (int? page, int? pageSize, int? packagePage, int? packagePageSize, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => ClientPortalWorkspaceQuery.GetAsync(db, actor, page ?? 0, pageSize ?? 50,
        packagePage ?? 0, packagePageSize ?? 10, ct)));
    group.MapGet("/portal/requests/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => ClientPortalWorkspaceQuery.RequestAsync(db, actor, id, ct)));
    group.MapGet("/portal/requests/{id:guid}/history", (Guid id, int? uploadPage, int? conversationPage, int? pageSize, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => ClientPortalWorkspaceQuery.RequestHistoryAsync(db, actor, id,
        uploadPage ?? 0, conversationPage ?? 0, pageSize ?? 25, ct)));
    group.MapUiGet("/portal/documents", (HttpContext http) => ReadAsync(http,
      (db, actor, ct) => ClientPortalReviewQuery.GetAsync(db, actor, ct)));
    group.MapUiGet("/portal/finance", (HttpContext http) => ReadAsync(http,
      (db, actor, ct) => ClientPortalFinanceQuery.GetAsync(db, actor, ct)));
    group.MapGet("/portal/accounting/packages/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => FinancialPackageReviewService.GetClientViewAsync(db, actor, id, ct)));
    group.MapGet("/portal/accounting/packages/{id:guid}/decision-receipts/{requestId:guid}", (Guid id, Guid requestId, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => FinancialPackageReviewService.GetClientDecisionReceiptAsync(db, actor, id, requestId, ct)));
    group.MapPost("/portal/accounting/packages/{id:guid}/decision", (Guid id, PortalPackageDecisionInput i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        var current = await FinancialPackageReviewService.GetClientViewAsync(db, actor, id, ct);
        if (!current.Succeeded) return CommandResult<Guid>.Fail(current.ErrorCode!, "Package unavailable.");
        if (!i.Reviewed || current.Value!.PackageHash != i.ExpectedHash)
          return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Review the current package before recording a decision.");
        // When a sealed composition awaits approval, the decision must name exactly that composition. A package-only decision would not
        // be the management approval the composition path requires, and a decision on an earlier composition is stale.
        var invited = current.Value.Composition;
        if (invited is null ? i.PresentationCompositionId is not null || i.PresentationRevisionId is not null || i.CompositionManifestDigest is not null
            : invited.CompositionId != i.PresentationCompositionId || invited.PresentationRevisionId != i.PresentationRevisionId ||
              invited.CompositionManifestDigest != i.CompositionManifestDigest)
          return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
            "The report awaiting your approval changed. Refresh the package and review the current report before deciding.");
        return await FinancialPackageReviewService.RecordAsync(db, actor, new(id, "MANAGEMENT_APPROVAL", i.Decision, "SIGNED_IN",
          i.EvidenceReference ?? "", i.Comment ?? "", invited?.PresentationRevisionId, null, invited?.CompositionId,
          invited?.CompositionManifestDigest, i.RequestId, i.ExpectedLatestDecisionId), ct);
      }));
    // The exact sealed PDF of the composition awaiting approval. Downloading records nothing and approves nothing.
    group.MapPost("/portal/accounting/packages/{id:guid}/compositions/{compositionId:guid}/pdf", async (Guid id, Guid compositionId,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FinancialPackageReviewService.GetClientCompositionPdfAsync(db, actor, id, compositionId, http.RequestAborted);
      if (!result.Succeeded || result.Value is null) return Failure(result.ErrorCode, result.Message ?? "The report is unavailable.");
      http.Response.Headers.CacheControl = "no-store";
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      http.Response.Headers["X-Statement-Composition-PDF-SHA256"] = result.Value.ArtifactSha256Hex;
      return Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName);
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));
    group.MapPost("/portal/documents/{id:guid}/comment", (Guid id, PortalDocumentCommentInput i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => ClientPortalReviewCommands.CommentAsync(db, actor, id, i.RequestId, i.Body ?? "", ct)));
    group.MapGet("/portal/documents/{id:guid}/comment-receipts/{requestId:guid}", (Guid id, Guid requestId, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => ClientPortalReviewCommands.GetCommentReceiptAsync(db, actor, id, requestId, ct)));
    group.MapPost("/portal/documents/{id:guid}/acknowledge", (Guid id, PortalAcknowledgementInput i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => i.Reviewed ? ClientPortalReviewCommands.AcknowledgeAsync(db, actor, id, i.Sha256 ?? "", ct) :
        Task.FromResult(CommandResult.Fail("review.required", "Review the exact document before acknowledgement."))));
    group.MapPost("/portal/bundles/{id:guid}/acknowledge", (Guid id, PortalBundleAcknowledgementInput i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => ClientBundleAcknowledgementCommands.AcknowledgeAsync(db, actor, id, i.ContentSha256 ?? "", i.ManifestSha256 ?? "", i.Received, ct)));
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
