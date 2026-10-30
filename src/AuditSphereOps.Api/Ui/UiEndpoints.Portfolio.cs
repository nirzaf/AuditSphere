using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PortfolioExportInput(string? Search);
  public sealed record PortfolioContextChoicesResponse(Guid ClientId, string ClientName, bool CanOpenClient, int PageSize,
    IReadOnlyList<PortfolioContextChoice> Items, bool HasMore, string? NextCursor, int ValidForMs);

  private sealed record PortfolioChoiceContinuation(int Version, Guid FirmId, Guid UserId, long Epoch, Guid ClientId,
    int PageSize, Guid AfterId, string ScopeStamp, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

  private static class PortfolioChoiceCursor
  {
    private const string Prefix = "pc1.";
    private const int MaximumLength = 4096;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private static IDataProtector Protector(HttpContext http) => http.RequestServices.GetRequiredService<IDataProtectionProvider>()
      .CreateProtector("AuditSphere.PortfolioContextChoices.v1");

    internal static bool TryRead(HttpContext http, string token, ActorContext actor, Guid clientId, int pageSize,
      out PortfolioContextChoiceCursor? cursor)
    {
      cursor = null;
      if (token.Length > MaximumLength || !token.StartsWith(Prefix, StringComparison.Ordinal)) return false;
      try
      {
        var value = JsonSerializer.Deserialize<PortfolioChoiceContinuation>(Protector(http).Unprotect(token[Prefix.Length..]));
        var now = DateTimeOffset.UtcNow;
        if (value is null || value.Version != 1 || value.FirmId != actor.FirmId || value.UserId != actor.UserId ||
            value.Epoch != actor.SessionEpoch || value.ClientId != clientId || value.PageSize != pageSize || value.AfterId == Guid.Empty ||
            value.IssuedAt > now || value.ExpiresAt <= now || value.ExpiresAt - value.IssuedAt != Lifetime ||
            value.ScopeStamp is not { Length: 64 } || value.ScopeStamp.Any(c => c is not (>= '0' and <= '9' or >= 'A' and <= 'F'))) return false;
        cursor = new(value.FirmId, value.UserId, value.Epoch, value.ClientId, value.PageSize, value.AfterId, value.ScopeStamp);
        return true;
      }
      catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
      { return false; }
    }

    internal static bool TryWrite(HttpContext http, ActorContext actor, Guid clientId, int pageSize,
      Guid afterId, string scopeStamp, out string? token)
    {
      token = null;
      try
      {
        var now = DateTimeOffset.UtcNow;
        token = Prefix + Protector(http).Protect(JsonSerializer.Serialize(new PortfolioChoiceContinuation(1, actor.FirmId,
          actor.UserId, actor.SessionEpoch, clientId, pageSize, afterId, scopeStamp, now, now + Lifetime)));
        if (token.Length <= MaximumLength) return true;
        token = null;
        return false;
      }
      catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
      { return false; }
    }
  }

  private static void MapPortfolioEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/portfolio/context-choices", async (Guid? clientId, int? pageSize, string? cursor, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      http.Response.Headers.CacheControl = "no-store";
      var requestStartedAt = DateTimeOffset.UtcNow;
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      var size = pageSize ?? 25;
      if (clientId is null || clientId == Guid.Empty || size is < 1 or > PortfolioQuery.ContextChoiceMaxPageSize)
        return Failure("request.invalid", "Invalid context choices request.", 400);
      PortfolioContextChoiceCursor? after = null;
      if (cursor is not null && !PortfolioChoiceCursor.TryRead(http, cursor, actor, clientId.Value, size, out after))
        return Failure("request.invalid", "Invalid context continuation.", 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PortfolioQuery.ContextChoicesAsync(db, actor, clientId.Value, size, after, http.RequestAborted);
      if (!result.Succeeded) return Failure(result.ErrorCode, result.Message, StatusFor(result.ErrorCode));
      var page = result.Value!;
      if (page.ClientId != clientId.Value || page.PageSize != size || page.Items.Count > size ||
          page.HasMore != page.NextAfterId.HasValue || page.HasMore &&
          (page.Items.Count != size || page.Items[^1].EngagementId != page.NextAfterId))
        return Failure(ErrorCodes.GateBlocked, "Context choices unavailable.", 400);
      string? next = null;
      if (page.HasMore && !PortfolioChoiceCursor.TryWrite(http, actor, clientId.Value,
            size, page.NextAfterId!.Value, page.ScopeStamp, out next))
        return Failure(ErrorCodes.GateBlocked, "Context continuation unavailable.", 400);
      var current = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (current is null || current.FirmId != actor.FirmId || current.UserId != actor.UserId || current.SessionEpoch != actor.SessionEpoch)
        return Failure("session.unavailable", "Sign in again.", 401);
      var requestDeadline = requestStartedAt.AddSeconds(30);
      var validUntil = page.ValidUntilUtc < requestDeadline ? page.ValidUntilUtc : requestDeadline;
      var remaining = (int)Math.Floor((validUntil - DateTimeOffset.UtcNow).TotalMilliseconds);
      if (remaining <= 0) return Failure("scope.changed", "Context changed; start again.", 409);
      return Results.Ok(new PortfolioContextChoicesResponse(page.ClientId, page.ClientName, page.CanOpenClient,
        page.PageSize, page.Items, page.HasMore, next, Math.Min(remaining, 30000)));
    }).WithT046Response<PortfolioContextChoicesResponse>(includeConflict: true)
      .Produces<UiFailureResponse>(StatusCodes.Status401Unauthorized, "application/json");
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
