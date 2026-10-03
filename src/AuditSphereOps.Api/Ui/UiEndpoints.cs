using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Search;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record ContactInput(string Name, string Email, string Role, bool Primary, string SafetyGeneration);
  public sealed record StaffingInput(Guid UserId, string Level, bool ReviewedClientSiteAccess);
  public sealed record BudgetInput(string Currency, string ExpectedVersion, IReadOnlyList<BudgetLineRequest> Lines);
  public sealed record EngagementInput(string ServiceRoute, string PeriodStart, string PeriodEnd, string ServiceProfile);
  public sealed record AcceptanceAnswerInput(string Answer, string? Evidence, string Generation, string Revision);
  public static void MapUiEndpoints(this WebApplication app, bool legacyPresentation = false)
  {
    var canonical = AngularRouteOwnership.Canonical(app.Configuration);
    if (canonical && (legacyPresentation || !app.Configuration.GetValue<bool>("AngularUi:Enabled")))
      throw new InvalidOperationException("Canonical Angular routes require the standalone API host and enabled Angular assets.");
    if (app.Configuration.GetValue<bool>("AngularUi:Enabled"))
    {
      var published = Path.Combine(app.Environment.ContentRootPath, "ui");
      var root = Path.GetFullPath(app.Configuration["AngularUi:BuildPath"] ??
        (File.Exists(Path.Combine(published, "index.html")) ? published :
          Path.Combine(app.Environment.ContentRootPath, "../AuditSphereOps.Ui/dist/auditsphere-ui/browser")));
      if (!File.Exists(Path.Combine(root, "index.html")))
        throw new InvalidOperationException("Angular UI is enabled but its production build is missing.");
      app.UseStaticFiles(new StaticFileOptions
      {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(root),
        RequestPath = "/ui"
      });
      if (app.Configuration["AngularUi:PreviousBuildPath"] is { Length: > 0 } previousBuild)
      {
        var previous = Path.GetFullPath(previousBuild);
        if (previous == root || !File.Exists(Path.Combine(previous, "index.html")))
          throw new InvalidOperationException("Previous Angular assets require a distinct retained approved build directory.");
        // Existing tabs may still request old lazy chunks. Serve only fingerprinted assets,
        // never an old HTML shell, source map or broad application-route fallback.
        var asset = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9_-]+-[A-Za-z0-9_-]{8,}\.(js|css|woff2?|svg|png|webp)$",
          System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        app.UseWhen(http => http.Request.Path.StartsWithSegments("/ui", out var rest) &&
          asset.IsMatch(Path.GetFileName(rest.Value ?? "")), branch => branch.UseStaticFiles(new StaticFileOptions
          {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(previous),
            RequestPath = "/ui"
          }));
      }
      // Explicit Angular-owned routes only: /auth, /api, /health and unmigrated Blazor routes never fall back to the SPA.
      foreach (var route in SpaRoutes)
        app.MapGet(route, (HttpContext http) =>
        {
          http.Response.Headers.CacheControl = "no-store";
          return Results.File(Path.Combine(root, "index.html"), "text/html");
        });
      if (canonical)
      {
        var index = File.ReadAllText(Path.Combine(root, "index.html"));
        const string previewBase = "<base href=\"/ui/\">";
        if (!index.Contains(previewBase, StringComparison.Ordinal) ||
            index.IndexOf(previewBase, StringComparison.Ordinal) != index.LastIndexOf(previewBase, StringComparison.Ordinal) ||
            !index.Contains("src=\"/ui/", StringComparison.Ordinal) || !index.Contains("href=\"/ui/styles-", StringComparison.Ordinal))
          throw new InvalidOperationException("Canonical routes require the approved Angular build with preview base and /ui asset URLs.");
        var canonicalIndex = index.Replace(previewBase, "<base href=\"/\">", StringComparison.Ordinal);
        foreach (var route in SpaRoutes.Where(x => x.StartsWith("/ui/", StringComparison.Ordinal) && x != "/ui/").Select(x => x[3..]))
          app.MapGet(route, (HttpContext http) =>
          {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Content(canonicalIndex, "text/html; charset=utf-8");
          });
      }
    }
    var group = app.MapGroup("/api/ui");
    MapAcceptanceCommands(group);
    MapCommercialEndpoints(group);
    MapPortfolioEndpoints(group);
    MapClientContactCreationEndpoints(group);
    MapQuotationEndpoints(group);
    MapCommercialDocumentEndpoints(group);
    MapFeeAgreementEndpoints(group);
    MapCommercialSettingsEndpoints(group);
    MapAccountingEndpoints(group);
    MapChartEndpoints(group);
    MapCurrencyConfigurationEndpoints(group);
    MapWorkbenchEndpoints(group);
    group.MapGet("/clients/{id:guid}/acceptance", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AcceptanceWorkspaceQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/clients/{id:guid}/acceptance/answers/{code}", async (Guid id, string code, AcceptanceAnswerInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!long.TryParse(input.Generation, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var generation) || generation < 1 ||
          !long.TryParse(input.Revision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var revision) || revision < 0)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AcceptanceChecklistService.RecordAnswerAsync(db, actor, id, code, input.Answer, input.Evidence,
        http.RequestAborted, generation, revision);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/clients/{id:guid}/engagements", async (Guid id, EngagementInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await EngagementLifecycleService.CreateDraftAsync(db, actor,
        new(id, input.ServiceRoute, input.PeriodStart, input.PeriodEnd, input.ServiceProfile), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/engagements/{id:guid}/activate", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await EngagementLifecycleService.ActivateAsync(db, actor, id, http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/engagements/{id:guid}/budgets", async (Guid id, BudgetInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (input.Lines is null || input.Lines.Count is < 1 or > 200 || string.IsNullOrWhiteSpace(input.Currency) ||
        !long.TryParse(input.ExpectedVersion, System.Globalization.NumberStyles.None,
          System.Globalization.CultureInfo.InvariantCulture, out var version) || version < 0)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeTimeService.ReviseBudgetAsync(db, actor, new(id, input.Currency, input.Lines, version), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/engagements/{id:guid}/budgets/{budgetId:guid}/approve", async (Guid id, Guid budgetId, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await EngagementPlanningQuery.ApproveBudgetAsync(db, actor, id, budgetId, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/engagements/{id:guid}/staffing", async (Guid id, StaffingInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.ReviewedClientSiteAccess) return Results.Json(new { code = "staffing.review-required" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await StaffingService.AssignAsync(db, actor, new(id, input.UserId, input.Level), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/engagements/{id:guid}/staffing/{assignmentId:guid}/revoke", async (Guid id, Guid assignmentId,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await EngagementPlanningQuery.RevokeAsync(db, actor, id, assignmentId, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 403);
    });
    group.MapGet("/engagements/{id:guid}/planning", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await EngagementPlanningQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/clients/{id:guid}/contacts", async (Guid id, ContactInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 ||
          string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 254 ||
          string.IsNullOrWhiteSpace(input.Role) || input.Role.Length > 200 ||
          !long.TryParse(input.SafetyGeneration, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var generation) || generation < 1)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var access = await WorkspaceQuery.ClientAsync(db, actor, id, http.RequestAborted);
      if (!access.Succeeded || !access.Value!.CanManageContacts)
        return Results.Json(new { code = "scope.denied" }, statusCode: 403);
      var result = await PracticeCrmService.CreateClientContactAsync(db, actor,
        new(id, input.Name, input.Email, input.Role, ValidFrom: DateTimeOffset.UtcNow,
          Primary: input.Primary, ExpectedSafetyGeneration: generation), http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode },
        statusCode: result.ErrorCode == "generation.stale" ? 409 : 400);
      return Results.Ok(new { id = result.Value });
    });
    group.MapGet("/search", async (string? term, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      if (term?.Length > GlobalSearchQuery.MaximumTermLength)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await GlobalSearchQuery.SearchAsync(db, actor, term, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapGet("/engagements/{id:guid}", async (Guid id, int? holdPage, int? holdPageSize, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await WorkspaceQuery.EngagementAsync(db, actor, id, http.RequestAborted,
        holdPage is null && holdPageSize is null ? null : new(holdPage ?? 0, holdPageSize ?? 10));
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "request.invalid" ? 400 : 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapGet("/clients/{id:guid}", async (Guid id, int? engagementPage, int? engagementPageSize, int? contactPage, int? contactPageSize,
      HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await WorkspaceQuery.ClientAsync(db, actor, id, http.RequestAborted,
        engagementPage is null && engagementPageSize is null && contactPage is null && contactPageSize is null
          ? null : new(engagementPage ?? 0, engagementPageSize ?? 10, contactPage ?? 0, contactPageSize ?? 10));
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "request.invalid" ? 400 : 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.AddEndpointFilter(async (context, next) =>
    {
      context.HttpContext.Response.Headers.CacheControl = "no-store";
      try { return await next(context); }
      catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
      catch (Exception exception)
      {
        app.Logger.LogError("UI read failed: {ErrorType}; diagnostic {DiagnosticId}",
          exception.GetType().Name, context.HttpContext.TraceIdentifier);
        return Results.Json(new { code = "read.unavailable", message = "Please retry shortly.",
          correlationId = context.HttpContext.TraceIdentifier }, statusCode: 503);
      }
    });
    group.MapGet("/session", async (HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var isStaff = await db.Users.AsNoTracking().AnyAsync(u => u.Id == actor.UserId && u.FirmId == actor.FirmId &&
        u.UserKind == "Staff" && !u.Disabled, http.RequestAborted);
      var tokens = csrf.GetAndStoreTokens(http);
      http.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
      { HttpOnly = false, Secure = http.Request.IsHttps ||
          !(app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test")), SameSite = SameSiteMode.Strict, Path = "/" });
      return Results.Ok(new { userId = actor.UserId, firmId = actor.FirmId,
        generation = actor.SessionEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
        staff = isStaff && actor.Roles.Any() && !actor.Roles.Contains("ClientUser") });
    });
    group.MapPost("/sign-out", async (HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf) =>
    {
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException)
      { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
      http.Response.Cookies.Delete("XSRF-TOKEN", new CookieOptions { Path = "/" });
      return Results.NoContent();
    });
    group.MapGet("/portfolio", async (HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory, string? search, int? page, int? pageSize) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PortfolioQuery.ListAsync(db, actor, search, page ?? 0, pageSize ?? 25, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode, message = "Portfolio unavailable." },
        statusCode: result.ErrorCode == "request.invalid" ? 400 : 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
  }
}
