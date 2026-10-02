using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Antiforgery;
using System.Globalization;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record ChartCreationInput(string LatestVersion, string SourceScope, string EffectiveFrom, bool Reviewed);
  public sealed record ChartAccountsInput(string Version, IReadOnlyList<ClientAccountInput> Accounts, bool Reviewed);
  public sealed record ChartPublicationInput(string Version, string Digest, bool Reviewed);
  private static void MapChartEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/clients/{id:guid}/charts/{chartId:guid}/publication", async (Guid id, Guid chartId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ChartPublicationQuery.GetAsync(db, actor, id, chartId, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/accounting/clients/{id:guid}/charts/{chartId:guid}/publish", async (Guid id, Guid chartId,
      ChartPublicationInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !int.TryParse(input.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1 ||
        input.Digest is not { Length: 64 } || !input.Digest.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9'))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.PublishChartVersionAsync(db, actor, chartId, http.RequestAborted, id, version, input.Digest);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/accounting/clients/{id:guid}/charts", async (Guid id, ChartCreationInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || string.IsNullOrWhiteSpace(input.SourceScope) || input.SourceScope.Length > 200 ||
        !int.TryParse(input.LatestVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 0 ||
        !DateOnly.TryParseExact(input.EffectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effective))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.CreateChartVersionAsync(db, actor, id, input.SourceScope, effective, http.RequestAborted, version);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/accounting/clients/{id:guid}/charts/{chartId:guid}/accounts", async (Guid id, Guid chartId,
      ChartAccountsInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !int.TryParse(input.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1 ||
        input.Accounts is null || input.Accounts.Count is < 1 or > 100 || input.Accounts.Any(x => x is null ||
          string.IsNullOrWhiteSpace(x.StableIdentity) || x.StableIdentity.Length > 200 || string.IsNullOrWhiteSpace(x.AccountCode) || x.AccountCode.Length > 100 ||
          string.IsNullOrWhiteSpace(x.AccountName) || x.AccountName.Length > 200 || string.IsNullOrWhiteSpace(x.AccountType) || x.AccountType.Length > 50 ||
          x.NormalBalance is not ("DEBIT" or "CREDIT") || x.ParentStableIdentity?.Length > 200))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.AddAccountsAsync(db, actor, chartId, input.Accounts, http.RequestAborted, id, version);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapGet("/accounting/clients/{id:guid}/charts", async (Guid id, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ChartWorkspaceQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapGet("/accounting/clients/{id:guid}/charts/{chartId:guid}/accounts", async (Guid id, Guid chartId, int? page,
      int? pageSize, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.GetChartRevisionAccountsAsync(db, actor, chartId, page ?? 1, pageSize ?? 50, http.RequestAborted, id);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
  }
}
