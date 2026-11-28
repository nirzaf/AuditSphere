using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Antiforgery;
using System.Globalization;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record ChartCreationInput(string LatestVersion, string SourceScope, string EffectiveFrom, bool Reviewed);
  public sealed record ChartAccountsInput(string Version, IReadOnlyList<ClientAccountInput> Accounts, bool Reviewed);
  public sealed record ChartPublicationInput(string Version, string Digest, bool Reviewed);
  public sealed record ChartAliasesInput(string Version, IReadOnlyList<SourceAccountAliasInput> Aliases, bool Reviewed);
  public sealed record ClientDimensionsInput(IReadOnlyList<AccountingDimensionInput> Dimensions, bool Reviewed);
  public sealed record DimensionSchemaValueHttpInput(string Code, string Name, string EffectiveFrom,
    string? EffectiveTo, bool Active, Guid? ValueId = null);
  public sealed record DimensionSchemaDefinitionHttpInput(string DimensionType, string Code, string Name,
    bool Required, IReadOnlyList<DimensionSchemaValueHttpInput> Values, Guid? DefinitionId = null);
  public sealed record DimensionSchemaDraftHttpInput(Guid? BaseRevisionRef,
    IReadOnlyList<DimensionSchemaDefinitionHttpInput> Definitions, Guid? DraftRevisionRef = null,
    bool ImpactAcknowledged = false, string? ExpectedCanonicalDigest = null);

  private static bool TryBuildDimensionSchemaDraftDto(Guid clientId, DimensionSchemaDraftHttpInput? input,
    out DimensionSchemaDraftDto? dto, out string error)
  {
    dto = null;
    error = "Provide a valid client dimension schema definition set and YYYY-MM-DD effective dates.";
    if (clientId == Guid.Empty || input is null || input.Definitions is null)
      return false;
    var definitions = new List<DimensionSchemaDefinitionDraftDto>(input.Definitions.Count);
    foreach (var def in input.Definitions)
    {
      if (def is null || def.Values is null) return false;
      var values = new List<DimensionSchemaValueDraftDto>(def.Values.Count);
      foreach (var val in def.Values)
      {
        if (val is null || !TryDate(val.EffectiveFrom, out var effectiveFrom))
          return false;
        DateOnly? effectiveTo = null;
        if (!string.IsNullOrWhiteSpace(val.EffectiveTo))
        {
          if (!TryDate(val.EffectiveTo, out var parsedTo))
            return false;
          effectiveTo = parsedTo;
        }
        values.Add(new DimensionSchemaValueDraftDto(
          val.Code ?? string.Empty,
          val.Name ?? string.Empty,
          effectiveFrom,
          effectiveTo,
          val.Active,
          val.ValueId));
      }
      definitions.Add(new DimensionSchemaDefinitionDraftDto(
        def.DimensionType ?? string.Empty,
        def.Code ?? string.Empty,
        def.Name ?? string.Empty,
        def.Required,
        values,
        def.DefinitionId));
    }
    dto = new DimensionSchemaDraftDto(clientId, input.BaseRevisionRef, definitions, input.DraftRevisionRef,
      input.ImpactAcknowledged, input.ExpectedCanonicalDigest);
    error = string.Empty;
    return true;
  }

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
    })
      .Produces<ChartPublicationReview>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
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
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode },
        statusCode: result.ErrorCode == ErrorCodes.ScopeDenied ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest);
    })
      .Produces(StatusCodes.Status204NoContent)
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
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
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode },
        statusCode: result.ErrorCode == ErrorCodes.ScopeDenied ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest);
    })
      .Produces<UiIdentifierResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
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
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode },
        statusCode: result.ErrorCode == ErrorCodes.ScopeDenied ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest);
    })
      .Produces(StatusCodes.Status204NoContent)
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
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
    })
      .Produces<ChartWorkspace>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
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
    })
      .Produces<ChartRevisionAccountsPage>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapGet("/accounting/clients/{id:guid}/charts/{chartId:guid}/aliases", async (Guid id, Guid chartId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.GetChartRevisionAliasesAsync(db, actor, chartId, http.RequestAborted, id);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    })
      .Produces<IReadOnlyList<SourceAccountAliasViewDto>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost("/accounting/clients/{id:guid}/charts/{chartId:guid}/aliases", async (Guid id, Guid chartId,
      ChartAliasesInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !int.TryParse(input.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1 ||
        input.Aliases is null || input.Aliases.Count is < 1 or > 100 || input.Aliases.Any(x => x is null ||
          x.ClientAccountId == Guid.Empty || string.IsNullOrWhiteSpace(x.SourceSystem) || x.SourceSystem.Length > 100 ||
          string.IsNullOrWhiteSpace(x.AliasCode) || x.AliasCode.Length > 100 || (x.AliasName is not null && x.AliasName.Length > 200)))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.AddSourceAccountAliasesAsync(db, actor, chartId, input.Aliases, http.RequestAborted, id, version);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode },
        statusCode: result.ErrorCode == ErrorCodes.ScopeDenied ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest);
    })
      .Produces(StatusCodes.Status204NoContent)
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapGet("/accounting/clients/{id:guid}/dimensions", async (Guid id,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.GetDimensionDefinitionsAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    })
      .Produces<IReadOnlyList<ClientDimensionViewDto>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost("/accounting/clients/{id:guid}/dimensions", async (Guid id,
      ClientDimensionsInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || input.Dimensions is null || input.Dimensions.Count is < 1 or > 100 || input.Dimensions.Any(x => x is null ||
        !AuditSphereOps.Domain.Accounting.AccountingDimensionTypes.All.Contains(x.DimensionType?.Trim() ?? string.Empty) ||
        string.IsNullOrWhiteSpace(x.Code) || x.Code.Length > 100 || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 300))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientAccountingService.AddDimensionDefinitionsAsync(db, actor, id, input.Dimensions, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode },
        statusCode: result.ErrorCode == ErrorCodes.ScopeDenied ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest);
    })
      .Produces(StatusCodes.Status204NoContent)
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost("/clients/{clientId:guid}/dimension-schemas/drafts", (Guid clientId, DimensionSchemaDraftHttpInput input, HttpContext http) =>
      !TryBuildDimensionSchemaDraftDto(clientId, input, out var dto, out var error)
        ? Task.FromResult(Invalid(error))
        : CommandAsync(http, (db, actor, ct) => ClientAccountingService.SaveDimensionSchemaDraftAsync(db, actor, dto!, ct)))
      .WithUiResponse<UiValueResponse<DimensionSchemaDraftReceiptDto>>(includeConflict: true);
    group.MapPost("/clients/{clientId:guid}/dimension-schemas/{schemaId:guid}/publish", (Guid clientId, Guid schemaId, DimensionSchemaDraftHttpInput input, HttpContext http) =>
    {
      if (schemaId == Guid.Empty) return Task.FromResult(Invalid("A schema identity is required."));
      if (!TryBuildDimensionSchemaDraftDto(clientId, input, out var dto, out var error)) return Task.FromResult(Invalid(error));
      return CommandAsync(http, async (db, actor, ct) =>
      {
        if (dto!.DraftRevisionRef is { } draftId && await db.ClientAccountingDimensionSchemaRevisions.AsNoTracking()
              .AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == draftId && x.SchemaId != schemaId, ct))
          return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        return await ClientAccountingService.PublishDimensionSchemaAsync(db, actor, dto, ct);
      });
    })
      .WithUiResponse<UiValueResponse<DimensionSchemaPublicationReceiptDto>>(includeConflict: true);
    group.MapGet("/clients/{clientId:guid}/dimension-schemas/{schemaId:guid}/revisions/{revision:int}",
      (Guid clientId, Guid schemaId, int revision, Guid revisionId, int canonicalDigestVersion, string canonicalDigest, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => ClientAccountingService.ResolvePublishedDimensionSchemaRevisionAsync(
          db, actor, clientId, new DimensionSchemaRevisionRef(schemaId, revisionId, revision, canonicalDigestVersion, canonicalDigest ?? string.Empty), ct)))
      .WithUiResponse<ResolvedDimensionSchemaRevisionDto>(includeConflict: true);
  }
}
