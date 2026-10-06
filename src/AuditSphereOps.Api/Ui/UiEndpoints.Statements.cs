using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record StatementExportInput(string Revision);
  private static void MapStatementEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{id:guid}/statements/workspace", (Guid id, string? section, string? filter, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => StatementReviewWorkspace.GetAsync(db, actor, id, section ?? "profit", filter, page ?? 1, ct)));
    group.MapGet("/engagements/{id:guid}/statements/split", (Guid id, string? filter, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => StatementReviewWorkspace.GetSplitDashboardAsync(db, actor, id, filter, ct)));
    group.MapGet("/engagements/{id:guid}/statements/contributions", (Guid id, string section, string destination, string statementSection, string revision, int? page, int? procedurePage, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => StatementReviewWorkspace.ContributionsAsync(db, actor, id, section, destination, statementSection, revision, page ?? 1, procedurePage ?? 1, ct)));
    group.MapPost("/engagements/{id:guid}/statements/export", async (Guid id, StatementExportInput? input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await StatementReviewWorkspace.ExportAsync(db, actor, id, input?.Revision ?? "", http.RequestAborted);
      if (!result.Succeeded) return Failure(result.ErrorCode, result.Message);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Failure("session.unavailable", "Sign in again.", 401);
      http.Response.Headers["X-Statement-Basis"] = result.Value!.Revision;
      http.Response.Headers["X-Mapping-Id"] = result.Value.MappingId.ToString("D");
      http.Response.Headers["X-Engagement-Id"] = id.ToString("D");
      return Results.File(result.Value.Content, "text/csv; charset=utf-8", result.Value.FileName);
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));
  }
}
