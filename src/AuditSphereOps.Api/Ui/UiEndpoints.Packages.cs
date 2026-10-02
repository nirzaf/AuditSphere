using System.Text;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PackageReviewInput(string Stage, string Decision, string EvidenceMode, string EvidenceReference, string? Comment);
  public sealed record PackageArtifactInput(string ArtifactVersion);

  private static void MapPackageEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/accounting/reviews", http => ReadAsync(http, (db, actor, ct) => FinancialPackageReviewService.GetStaffQueueAsync(db, actor, ct)));
    group.MapGet("/accounting/packages/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => FinancialPackageWorkspaceQuery.GetAsync(db, actor, id, ct)));
    group.MapPost("/accounting/packages/{id:guid}/reviews", (Guid id, PackageReviewInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialPackageReviewService.RecordAsync(db, actor,
        new FinancialPackageReviewRequest(id, i.Stage, i.Decision, i.EvidenceMode, i.EvidenceReference ?? "", i.Comment ?? ""), ct)));
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
    });
  }
}
