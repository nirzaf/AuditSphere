using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PackageReviewInput(string Stage, string Decision, string EvidenceMode, string EvidenceReference, string? Comment,
    Guid? PresentationRevisionId = null, string? TargetArtifactVersion = null,
    Guid? PresentationCompositionId = null, string? CompositionManifestDigest = null);
  public sealed record PackageArtifactInput(string ArtifactVersion);
  public sealed record CashFlowSourceReviewInput(string Decision, string? Reason, Guid RequestId, string? ExpectedLinkDigest,
    Guid? ExpectedPreviousReviewId);
  public sealed record StatementPresentationExportInput(string? Format, Guid? LayoutVersionId);
  public sealed record StatementPresentationPinInput(Guid LayoutVersionId);
  public sealed record StatementPresentationPinnedArtifactInput(string? Format);

  private static void MapPackageEndpoints(RouteGroupBuilder group)
  {
    // Preserve exact decimal strings from the browser while queueing the reviewed build durably.
    group.MapUiPost("/accounting/packages/builds", http => CommandAsync(http, async (db, actor, ct) =>
    {
      BuildFinancialPackageRequest? input;
      try
      {
        input = await JsonSerializer.DeserializeAsync<BuildFinancialPackageRequest>(http.Request.Body, UiJson, ct);
      }
      catch (Exception ex) when (ex is JsonException or FormatException or OverflowException)
      {
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "The financial-package input is invalid.");
      }
      if (input is null)
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "A financial-package input is required.");
      return await FinancialStatementService.EnqueueFinancialPackageBuildAsync(db, actor, input,
        http.RequestServices.GetRequiredService<IOperationStore>(),
        http.RequestServices.GetRequiredService<FinancialPackageBuildHandler>(), ct);
    })).Accepts<BuildFinancialPackageRequest>("application/json");

    group.MapUiGet("/accounting/reviews", http => ReadAsync(http, (db, actor, ct) => FinancialPackageReviewService.GetStaffQueueAsync(db, actor, ct)));
    group.MapGet("/accounting/packages/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => FinancialPackageWorkspaceQuery.GetAsync(db, actor, id, ct)));
    group.MapPost("/accounting/packages/{id:guid}/render", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialStatementService.EnqueueFinancialPackageRenderAsync(db, actor, id,
        http.RequestServices.GetRequiredService<IOperationStore>(),
        http.RequestServices.GetRequiredService<FinancialPackageRenderHandler>(), ct)));
    group.MapPost("/accounting/packages/{id:guid}/reviews", (Guid id, PackageReviewInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialPackageReviewService.RecordAsync(db, actor,
        new FinancialPackageReviewRequest(id, i.Stage, i.Decision, i.EvidenceMode, i.EvidenceReference ?? "", i.Comment ?? "",
          i.PresentationRevisionId, i.TargetArtifactVersion, i.PresentationCompositionId, i.CompositionManifestDigest), ct)));
    // Build inputs: the approved mappings, finalized plans and reporting periods a preparer can choose from.
    group.MapGet("/engagements/{id:guid}/financial-package-inputs", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FinancialPackageBuildInputsQuery.GetAsync(db, actor, id, ct)));
    group.MapUiGet("/engagements/{id:guid}/financial-package-builds/{operationId:guid}", http => ReadAsync(http,
      (db, actor, ct) => FinancialPackageBuildStatusQuery.GetAsync(db, actor,
        Guid.Parse(http.Request.RouteValues["id"]!.ToString()!),
        Guid.Parse(http.Request.RouteValues["operationId"]!.ToString()!), ct)));
    group.MapGet("/accounting/packages/{id:guid}/cash-flow-source-review", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FinancialStatementService.GetCashFlowSourceReviewAsync(db, actor, id, ct)));
    group.MapPost("/accounting/packages/{id:guid}/cash-flow-source-review", (Guid id, CashFlowSourceReviewInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialStatementService.RecordCashFlowSourceReviewAsync(db, actor, id, i.Decision, i.Reason,
        i.RequestId, i.ExpectedLinkDigest, i.ExpectedPreviousReviewId, ct)));
    // Artifact downloads are version-bound; downloading never releases or delivers the package.
    group.MapPost("/accounting/packages/{id:guid}/artifact", async (Guid id, PackageArtifactInput i, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var package = await db.FinancialPackages.AsNoTracking().Where(x => x.Id == id && x.FirmId == actor.FirmId).Select(x => new { x.Revision }).SingleOrDefaultAsync(http.RequestAborted);
      if (i.ArtifactVersion == AuditSphereOps.Domain.Accounting.FinancialPackageArtifactVersions.Text)
      {
        var text = await FinancialStatementService.GetStoredPackageArtifactAsync(db, actor, id, http.RequestAborted);
        if (!text.Succeeded || text.Value is null || package is null) return Failure(text.ErrorCode, text.Message ?? "The current artifact is unavailable; no download occurred.");
        return Results.File(Encoding.UTF8.GetBytes(text.Value.RenderedText), "text/plain; charset=utf-8", $"auditsphere-financial-package-{id:D}-r{package.Revision}.txt");
      }
      var office = await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, actor, id, i.ArtifactVersion ?? "", http.RequestAborted);
      if (!office.Succeeded || office.Value is null || package is null) return Failure(office.ErrorCode, office.Message ?? "Export artifact generation was blocked.");
      return Results.File(office.Value.ArtifactBytes, office.Value.ContentType, $"auditsphere-financial-package-{id:D}-r{package.Revision}.{office.Value.FileExtension}");
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));
    // Statement presentation (FSR-002/010/011 slice): a read-only typed view, and version-bound exports of the same view.
    group.MapGet("/accounting/packages/{id:guid}/presentation", (Guid id, Guid? layoutVersionId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FinancialStatementService.BuildPresentationAsync(db, actor, id, layoutVersionId, ct)));
    group.MapPost("/accounting/packages/{id:guid}/presentation/export", async (Guid id, StatementPresentationExportInput i, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FinancialStatementService.RenderPresentationAsync(db, actor, id, i.Format ?? string.Empty, i.LayoutVersionId, http.RequestAborted);
      if (!result.Succeeded || result.Value is null) return Failure(result.ErrorCode, result.Message ?? "The statement presentation is unavailable.");
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Failure("session.unavailable", "Sign in again.", 401);
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      http.Response.Headers["X-Statement-Presentation-SHA256"] = result.Value.ArtifactSha256Hex;
      return Results.File(result.Value.Bytes, result.Value.ContentType, result.Value.FileName);
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));

    // A pinned draft is immutable; its exact bytes use the package artifact and seal manifest, while its status stays UNAPPROVED_DRAFT.
    group.MapUiGet("/accounting/packages/{id:guid}/presentation/pins", http =>
      ReadAsync(http, (db, actor, ct) => FinancialStatementService.ListPresentationPinsAsync(db, actor,
        Guid.Parse(http.Request.RouteValues["id"]!.ToString()!), ct)));
    group.MapPost("/accounting/packages/{id:guid}/presentation/pins", (Guid id, StatementPresentationPinInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialStatementService.PinPresentationAsync(db, actor, id, i.LayoutVersionId, ct)));
    group.MapGet("/accounting/packages/{id:guid}/presentation/pins/{revisionId:guid}", (Guid id, Guid revisionId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FinancialStatementService.GetPinnedPresentationAsync(db, actor, id, revisionId, ct)));
    group.MapPost("/accounting/packages/{id:guid}/presentation/pins/{revisionId:guid}/candidate", (Guid id, Guid revisionId, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialStatementService.PromotePresentationToCandidateAsync(db, actor, id, revisionId, ct)));
    group.MapPost("/accounting/packages/{id:guid}/presentation/pins/{revisionId:guid}/composition", (Guid id, Guid revisionId, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialStatementService.SelectCompositionAsync(db, actor, id, revisionId, ct)));
    group.MapPost("/accounting/packages/{id:guid}/presentation/pins/{revisionId:guid}/artifacts", async (
      Guid id, Guid revisionId, StatementPresentationPinnedArtifactInput i, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var pin = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == revisionId && x.FinancialPackageId == id && x.FirmId == actor.FirmId, http.RequestAborted);
      if (pin is null) return Failure("scope.denied", "Access denied.", 404);
      var result = await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, actor, revisionId,
        i.Format ?? string.Empty, http.RequestAborted);
      if (!result.Succeeded || result.Value is null) return Failure(result.ErrorCode, result.Message ?? "The pinned draft export was blocked.");
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Failure("session.unavailable", "Sign in again.", 401);
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      http.Response.Headers["X-Statement-Presentation-SHA256"] = result.Value.ArtifactSha256Hex;
      http.Response.Headers["X-Statement-Presentation-Revision"] = result.Value.RevisionId.ToString("D");
      return Results.File(result.Value.Bytes, result.Value.ContentType, result.Value.FileName);
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));
  }
}
