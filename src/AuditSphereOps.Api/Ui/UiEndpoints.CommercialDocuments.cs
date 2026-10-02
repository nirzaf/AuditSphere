using System.Globalization;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record CommercialDocumentInput(Guid QuotationId, string ProfileVersion, bool Reviewed,
    string? TeamCvs, string? Timeline);
  private static void MapCommercialDocumentEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/proposals/{id:guid}/documents", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await CommercialDocumentWorkspaceQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    foreach (var kind in new[] { "quotation", "letter", "tender" })
    {
      group.MapPost("/proposals/{id:guid}/documents/" + kind, async (Guid id, CommercialDocumentInput input,
        HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
      {
        var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
        if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
        try { await csrf.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
        if (!input.Reviewed || input.QuotationId == Guid.Empty
          || !long.TryParse(input.ProfileVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
          || input.TeamCvs?.Length > 16000 || input.Timeline?.Length > 8000)
          return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
        var result = kind switch
        {
          "quotation" => await CommercialDocumentService.GenerateBriefQuotationAsync(db, actor, id, http.RequestAborted, input.QuotationId, version),
          "letter" => await CommercialDocumentService.GenerateEngagementLetterAsync(db, actor, id, http.RequestAborted, input.QuotationId, version),
          _ => await CommercialDocumentService.GenerateTenderAsync(db, actor, id, input.TeamCvs ?? "", input.Timeline ?? "", input.Reviewed,
            http.RequestAborted, input.QuotationId, version)
        };
        return result.Succeeded ? Results.Ok(new { id = result.Value!.Id }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
      });
    }
  }
}
