using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Search;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.HttpBoundary;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record SessionHttpResponse(Guid UserId, Guid FirmId, string Generation, bool Staff,
    string DisplayName, string Email, bool MicrosoftAccount);
  public sealed record ContactInput(string Name, string Email, string Role, bool Primary, string SafetyGeneration);
  public sealed record StaffingInput(Guid UserId, string Level, bool ReviewedClientSiteAccess);
  public sealed record BudgetInput(string Currency, string ExpectedVersion, IReadOnlyList<BudgetLineRequest> Lines);
  public sealed record EngagementInput(string ServiceRoute, string PeriodStart, string PeriodEnd, string ServiceProfile);
  public sealed record AcceptanceAnswerInput(string Answer, string? Evidence, string Generation, string Revision);
  public static void MapUiEndpoints(this WebApplication app, bool legacyPresentation = false)
  {
    var canonical = AngularRouteOwnership.Canonical(app.Configuration);
    if (canonical && !app.Configuration.GetValue<bool>("AngularUi:Enabled"))
      throw new InvalidOperationException("Canonical Angular routes require the standalone API host and enabled Angular assets.");
    if (app.Configuration.GetValue<bool>("AngularUi:Enabled"))
    {
      var root = AngularRouteOwnership.ResolveBuildRoot(app.Configuration, app.Environment)!;
      if (!File.Exists(Path.Combine(root, "index.html")))
        throw new InvalidOperationException("Angular UI is enabled but its production build is missing.");
      app.UseStaticFiles(BuildAngularStaticOptions(root));
      if (app.Configuration["AngularUi:PreviousBuildPath"] is { Length: > 0 } previousBuild)
      {
        var previous = Path.GetFullPath(previousBuild);
        if (previous == root || !File.Exists(Path.Combine(previous, "index.html")))
          throw new InvalidOperationException("Previous Angular assets require a distinct retained approved build directory.");
        // Existing tabs may still request old lazy chunks. Serve only fingerprinted assets,
        // never an old HTML shell, source map or broad application-route fallback.
        var asset = AngularRouteOwnership.FingerprintedAsset;
        app.UseWhen(http => http.Request.Path.StartsWithSegments("/ui", out var rest) &&
          asset.IsMatch(Path.GetFileName(rest.Value ?? "")), branch => branch.UseStaticFiles(BuildAngularStaticOptions(previous)));
      }
      // Explicit Angular-owned routes only: /auth, /api, /health and unrouted paths never fall back to the SPA.
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
    // Every native Angular API endpoint requires an authenticated cookie session before any
    // handler runs; Application authorization below still decides each business action. The
    // single boundary rate-limit policy resolves per-request cost classes for these endpoints.
    var group = app.MapGroup("/api/ui")
      .RequireAuthorization(HttpPolicies.AuthenticatedSession());
    group.WithMetadata(new ProducesResponseTypeMetadata(
      StatusCodes.Status503ServiceUnavailable, typeof(ApiError), ["application/json"]));
    MapAcceptanceCommands(group);
    MapCommercialEndpoints(group);
    MapPortfolioEndpoints(group);
    MapUserProfileEndpoints(group);
    MapClientContactCreationEndpoints(group);
    MapClientRelationshipEndpoints(group);
    MapEngagementActivationReviewEndpoints(group);
    MapEngagementCreationEndpoints(group);
    MapBudgetPreparationEndpoints(group);
    MapBudgetApprovalEndpoints(group);
    MapStaffingChangeEndpoints(group);
    MapClientConversionEndpoints(group);
    MapQuotationEndpoints(group);
    MapCommercialDocumentEndpoints(group);
    MapFeeAgreementEndpoints(group);
    MapFeeSettlementEndpoints(group);
    MapCommercialSettingsEndpoints(group);
    MapAccountingEndpoints(group);
    MapClientOperationalLedgerEndpoints(group);
    MapClientCounterpartyEndpoints(group);
    MapClientSalesInvoiceDraftEndpoints(group);
    MapClientSalesInvoiceEndpoints(group);
    MapClientSalesCreditNoteEndpoints(group);
    MapClientPurchaseInvoiceEndpoints(group);
    MapClientPurchaseCreditNoteEndpoints(group);
    MapClientOpenItemAllocationEndpoints(group);
    MapClientManualSettlementEndpoints(group);
    MapClientAccountRoleEndpoints(group);
    MapClientCounterpartyAmendmentEndpoints(group);
    MapChartEndpoints(group);
    MapCurrencyConfigurationEndpoints(group);
    MapWorkbenchEndpoints(group);
    MapWorkflowTaskEndpoints(group);
    MapWorkflowTemplateEndpoints(group);
    MapWorkflowReminderRuleEndpoints(group);
    MapPbcStorageEndpoints(group);
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
    }).WithSummary("Read the client acceptance workspace")
      .WithDescription("Returns the authorized acceptance checklist, recorded evidence, decisions, and clearance state for the client.")
      .Produces<AcceptanceWorkspace>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");
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
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    }).WithSummary("Record a client acceptance answer")
      .WithDescription("Records an answer and optional evidence against the supplied client acceptance question and generation/revision basis.")
      .Produces(StatusCodes.Status204NoContent)
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");
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
      return result.Succeeded
        ? Results.Ok(new { id = result.Value })
        : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    }).WithSummary("Create a client engagement")
      .WithDescription("Creates a draft engagement for the client and returns its identifier.")
      .Produces<UiIdentifierResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/engagements/{id:guid}/activate", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await EngagementLifecycleService.ActivateAsync(db, actor, id, http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    }).WithSummary("Activate an engagement")
      .WithDescription("Activates the specified engagement after the existing lifecycle checks succeed and returns its identifier.")
      .Produces<UiIdentifierResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");
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
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    }).WithSummary("Prepare an engagement budget")
      .WithDescription("Creates or revises the engagement budget from the supplied currency, version, and lines, then returns its identifier.")
      .Produces<UiIdentifierResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");
    group.MapPost("/engagements/{id:guid}/budgets/{budgetId:guid}/approve", async (Guid id, Guid budgetId, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await EngagementPlanningQuery.ApproveBudgetAsync(db, actor, id, budgetId, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    }).WithSummary("Approve an engagement budget")
      .WithDescription("Approves the specified budget for the engagement when the existing approval checks succeed.")
      .Produces(StatusCodes.Status204NoContent)
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");
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
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    }).WithSummary("Assign staff to an engagement")
      .WithDescription("Assigns the supplied staff member and level after client-site access review and returns the assignment identifier.")
      .Produces<UiIdentifierResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");
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
    }).WithSummary("Revoke an engagement staff assignment")
      .WithDescription("Revokes the specified staff assignment from the engagement when the existing authorization checks succeed.")
      .Produces(StatusCodes.Status204NoContent)
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");
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
    }).WithSummary("Read engagement planning")
      .WithDescription("Returns the authorized engagement team, budget and planning state, plus available staffing and preparation actions.")
      .Produces<EngagementPlanning>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");
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
    }).WithSummary("Create a client contact")
      .WithDescription("Creates a client contact after validating the supplied contact data and current safety generation, then returns its identifier.")
      .Produces<UiIdentifierResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");
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
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Search))
      .Produces<GlobalSearchResult>()
      .Produces<ClientNativeErrorHttpResponse>(StatusCodes.Status400BadRequest)
      .Produces<ClientNativeErrorHttpResponse>(StatusCodes.Status403Forbidden);
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
    }).Produces<EngagementWorkspace>(StatusCodes.Status200OK, "application/json");
    group.MapGet("/engagements/{id:guid}/lifecycle", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AuditSphereOps.Application.Acceptance.EngagementLifecycleQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: WorkflowReadFailureStatusCode(result.ErrorCode));
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    }).Produces<EngagementLifecycleReport>(StatusCodes.Status200OK, "application/json");
    // Seven-phase workflow projection (WF-01). Read-only: it never accepts a stage, and the canonical 11-stage route above stays unchanged.
    group.MapGet("/engagements/{id:guid}/workflow", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AuditSphereOps.Application.Acceptance.EngagementWorkflowWorkspaceQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: WorkflowReadFailureStatusCode(result.ErrorCode));
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    }).Produces<EngagementWorkflowReport>(StatusCodes.Status200OK, "application/json");
    // Engagement communication timeline (PM-07): the outbound mail the platform already recorded for this engagement. Read-only.
    group.MapGet("/engagements/{id:guid}/communications", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AuditSphereOps.Application.Acceptance.EngagementCommunicationTimelineQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: WorkflowReadFailureStatusCode(result.ErrorCode));
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    }).Produces<EngagementCommunicationTimeline>(StatusCodes.Status200OK, "application/json");
    group.MapGet("/engagements/{id:guid}/activity",
      (Guid id, DateOnly? from, DateOnly? to, DateTimeOffset? snapshotAtUtc, DateTimeOffset? afterAtUtc,
        int? afterSource, Guid? afterId, int? pageSize, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => EngagementCommunicationTimelineQuery.GetActivityAsync(db, actor, id,
          new EngagementActivityRequest(from, to, snapshotAtUtc, afterAtUtc, afterSource, afterId, pageSize ?? 50), ct)))
      .Produces<EngagementActivityPage>(StatusCodes.Status200OK, "application/json");
    // Dual-Key onboarding readiness (WF-03): independent cards for client consent, Partner risk, letter and advance payment. Read-only.
    group.MapGet("/engagements/{id:guid}/onboarding-readiness", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var automaticDrafting = AutomaticDraftingEnabled(http.RequestServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
      var result = await AuditSphereOps.Application.Acceptance.ClientOnboardingReadinessQuery.GetAsync(
        db, actor, id, http.RequestAborted, automaticDrafting);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: WorkflowReadFailureStatusCode(result.ErrorCode));
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    }).Produces<OnboardingReadinessReport>(StatusCodes.Status200OK, "application/json");
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
    }).Produces<ClientWorkspace>(StatusCodes.Status200OK, "application/json");
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
      var user = await db.Users.AsNoTracking().Where(u => u.Id == actor.UserId && u.FirmId == actor.FirmId &&
        u.SessionEpoch == actor.SessionEpoch && !u.Disabled)
        .Select(u => new { u.UserKind, u.DisplayName, u.Email }).SingleOrDefaultAsync(http.RequestAborted);
      if (user is null || await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      var tokens = csrf.GetAndStoreTokens(http);
      http.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
      { HttpOnly = false, Secure = http.Request.IsHttps ||
          !(app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test")), SameSite = SameSiteMode.Strict, Path = "/" });
      return Results.Ok(new SessionHttpResponse(actor.UserId, actor.FirmId,
        actor.SessionEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
        user.UserKind == "Staff" && actor.Roles.Any() && !actor.Roles.Contains("ClientUser"),
        user.DisplayName, user.Email, !app.Configuration.GetValue<bool>("DevelopmentIdentity:Enabled") &&
          !string.IsNullOrWhiteSpace(app.Configuration["Identity:TenantId"])));
    }).Produces<SessionHttpResponse>();
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
    }).Produces(StatusCodes.Status204NoContent)
      .Produces<ClientNativeErrorHttpResponse>(StatusCodes.Status403Forbidden);
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
    }).Produces<PortfolioPage>(StatusCodes.Status200OK, "application/json");
  }
}
