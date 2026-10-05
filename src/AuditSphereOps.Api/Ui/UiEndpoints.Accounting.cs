using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Antiforgery;
using System.Globalization;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record AccountingProfileInput(Guid? ProfileId, string Revision, string Jurisdiction, string Currency,
    int FiscalMonth, int FiscalDay, string SourceSystem, string SourceIdentifier, bool Reviewed);
  public sealed record AccountingPeriodInput(string Code, string Start, string End, string Basis, string Currency,
    Guid? PriorPeriodId, bool Reviewed);
  public sealed record AccountingBookInput(Guid PeriodId, string Code, string Basis, string InclusionRule, string Currency, bool Reviewed);
  public sealed record AccountingPeriodDecisionInput(string Revision, string Reason, bool Reviewed);
  public sealed record AccountingOpeningApprovalInput(string SourceHash, bool Reviewed);
  public sealed record AccountingRollforwardInput(Guid PriorPeriodId, string Revision, string Code, string Start,
    string End, string Basis, string Currency, string SourceHash, string PriorClosing, string CurrentOpening,
    string Evidence, Guid? SourcePackageId, bool Reviewed);
  private static void MapAccountingEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/accounting/task-owners", http => ReadAsync(http,
      (db, actor, ct) => AccountingTaskOwnerQuery.GetAsync(db, actor, ct)));
    group.MapGet("/accounting/clients/{id:guid}/periods/{periodId:guid}/sources", async (Guid id, Guid periodId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await RollforwardSourceQuery.GetAsync(db, actor, id, periodId, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/accounting/clients/{id:guid}/opening-bridges/{bridgeId:guid}/approve", async (Guid id, Guid bridgeId,
      AccountingOpeningApprovalInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || input.SourceHash is not { Length: 64 }) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.ApproveOpeningBalanceBridgeAsync(db, actor, bridgeId, http.RequestAborted, id, input.SourceHash);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/accounting/clients/{id:guid}/rollforward", async (Guid id, AccountingRollforwardInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || input.PriorPeriodId == Guid.Empty ||
        !long.TryParse(input.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1 ||
        string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 100 || string.IsNullOrWhiteSpace(input.Basis) || input.Basis.Length > 100 ||
        input.Currency is not { Length: 3 } || input.SourceHash is not { Length: 64 } ||
        string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 2000 ||
        !DateOnly.TryParseExact(input.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
        !DateOnly.TryParseExact(input.End, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
        !AccountingAmount(input.PriorClosing, out var closing) || !AccountingAmount(input.CurrentOpening, out var opening))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.RollForwardPeriodAsync(db, actor,
        new(id, input.PriorPeriodId, input.Code, start, end, input.Basis, input.Currency, input.SourceHash,
          closing, opening, input.Evidence, input.SourcePackageId, revision), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapGet("/accounting/clients/{id:guid}/periods/{periodId:guid}/readiness", async (Guid id, Guid periodId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PeriodCloseReadinessQuery.GetReadinessAsync(db, actor, periodId, http.RequestAborted, id);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    foreach (var action in new[] { "close", "reopen" })
    {
      var decision = action;
      group.MapPost("/accounting/clients/{id:guid}/periods/{periodId:guid}/" + action, async (Guid id, Guid periodId,
        AccountingPeriodDecisionInput input, HttpContext http, TrustedActorResolver resolver,
        IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
      {
        var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
        if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
        try { await csrf.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
        if (!input.Reviewed || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000 ||
          !long.TryParse(input.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
          return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
        var result = decision == "close"
          ? await ClientAccountingService.ClosePeriodAsync(db, actor, periodId, input.Reason, http.RequestAborted, revision, id)
          : await ClientAccountingService.ReopenPeriodAsync(db, actor, periodId, input.Reason, http.RequestAborted, revision, id);
        return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
      });
    }
    group.MapPost("/accounting/clients/{id:guid}/books", async (Guid id, AccountingBookInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || input.PeriodId == Guid.Empty || string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 100 ||
        string.IsNullOrWhiteSpace(input.Basis) || input.Basis.Length > 100 ||
        string.IsNullOrWhiteSpace(input.InclusionRule) || input.InclusionRule.Length > 200 || input.Currency is not { Length: 3 })
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.CreateBookAsync(db, actor,
        new(id, input.PeriodId, input.Code, input.Basis, input.InclusionRule, input.Currency), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/accounting/clients/{id:guid}/periods", async (Guid id, AccountingPeriodInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 100 ||
        string.IsNullOrWhiteSpace(input.Basis) || input.Basis.Length > 100 || input.Currency is not { Length: 3 } ||
        !DateOnly.TryParseExact(input.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
        !DateOnly.TryParseExact(input.End, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.CreatePeriodAsync(db, actor,
        new(id, input.Code, start, end, input.Basis, input.Currency, input.PriorPeriodId), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/accounting/clients/{id:guid}/profile", async (Guid id, AccountingProfileInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !long.TryParse(input.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ||
        (input.ProfileId is null ? revision != 0 : revision < 1) ||
        string.IsNullOrWhiteSpace(input.Jurisdiction) || input.Jurisdiction.Length > 100 ||
        string.IsNullOrWhiteSpace(input.SourceSystem) || input.SourceSystem.Length > 100 ||
        input.SourceIdentifier is null || input.SourceIdentifier.Length > 200 || input.Currency is not { Length: 3 })
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var request = new ClientAccountingProfileRequest(id, input.Jurisdiction, input.Currency, input.FiscalMonth,
        input.FiscalDay, input.SourceSystem, input.SourceIdentifier);
      if (input.ProfileId is { } profileId)
      {
        var result = await ClientAccountingService.ReviseProfileAsync(db, actor, profileId, request, revision, http.RequestAborted);
        return result.Succeeded ? Results.Ok(new { revision = result.Value.ToString(CultureInfo.InvariantCulture) }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
      }
      var created = await ClientAccountingService.CreateProfileAsync(db, actor, request, http.RequestAborted);
      return created.Succeeded ? Results.Ok(new { id = created.Value }) : Results.Json(new { code = created.ErrorCode }, statusCode: 400);
    });
    group.MapGet("/accounting/clients", async (string? search, int? page, int? pageSize, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AccountingWorkspaceQuery.ListAsync(db, actor, search, page ?? 0, pageSize ?? 25, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "request.invalid" ? 400 : 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapGet("/accounting/clients/{id:guid}", async (Guid id, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AccountingWorkspaceQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
  }
  private static bool AccountingAmount(string? value, out decimal amount)
  {
    amount = 0m;
    return value is { Length: > 0 and <= 60 } &&
      decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
  }
}
