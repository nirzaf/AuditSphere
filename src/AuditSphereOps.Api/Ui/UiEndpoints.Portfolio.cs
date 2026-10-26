using System.Text;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PortfolioExportInput(string? Search);

  private static void MapPortfolioEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/portfolio/workspace", (string? search, int? page, int? pageSize, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PortfolioQuery.WorkspaceAsync(db, actor, search, page ?? 0, pageSize ?? 25, ct)))
      .WithT046Response<PortfolioWorkspace>();
    // Client progress tracker: read-only summaries computed from current task, evidence and gate records.
    group.MapGet("/portfolio/progress", (int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PortfolioQuery.ProgressAsync(db, actor, page ?? 0, ct)))
      .WithT046Response<EngagementProgressList>();
    group.MapGet("/clients/{id:guid}/progress", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => EngagementProgressQuery.ForClientPageAsync(db, actor, id, page ?? 0, ct)))
      .WithT046Response<EngagementProgressList>();
    group.MapGet("/engagements/{id:guid}/progress", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => EngagementProgressQuery.GetAsync(db, actor, id, ct)))
      .WithT046Response<EngagementProgressView>();
    group.MapPost("/portfolio/export", async (PortfolioExportInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PortfolioQuery.ExportAsync(db, actor, input.Search, http.RequestAborted);
      if (!result.Succeeded) return Failure(result.ErrorCode, result.Message, result.ErrorCode is "request.invalid" or "export.limit" ? 400 : 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Failure("session.unavailable", "Sign in again.", 401);
      http.Response.Headers["X-Portfolio-Clients"] = result.Value!.ClientCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
      return Results.File(Encoding.UTF8.GetBytes(result.Value.Csv), "text/csv; charset=utf-8", result.Value.FileName);
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export))
      .WithT046Response<Stream>("text/csv");
  }
}
