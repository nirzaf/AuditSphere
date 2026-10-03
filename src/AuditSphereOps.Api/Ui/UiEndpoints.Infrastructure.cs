using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

/// <summary>
/// Shared shape for Angular capability endpoints. Every read resolves the trusted cookie actor, delegates to a named
/// Application query that performs its own scope check, re-resolves the session before returning (so a revoked
/// session never receives data), and serializes decimals as invariant strings. Every command additionally validates
/// antiforgery before delegating to the existing Application command; nothing here decides authorization or business
/// eligibility, and no command is retried.
/// </summary>
public static partial class UiEndpoints
{
  public delegate Task<CommandResult<T>> UiQuery<T>(AuditSphereDbContext db, ActorContext actor, CancellationToken ct);
  public delegate Task<CommandResult> UiCommand(AuditSphereDbContext db, ActorContext actor, CancellationToken ct);

  /// <summary>Angular-owned preview routes (served under /ui). Keep in step with app.routes.ts.</summary>
  public static readonly string[] SpaRoutes =
  [
    "/ui/setup/microsoft365", "/ui/app/administration", "/ui/app/administration/users", "/ui/app/administration/microsoft365", "/ui/app/administration/microsoft365/tenant-connection",
    "/ui/portal/accounting/journals/{id:guid}", "/ui/portal/accounting/packages/{id:guid}", "/ui/portal", "/ui/portal/requests/{id:guid}",
    "/ui", "/ui/", "/ui/app", "/ui/app/accounting", "/ui/app/practice/leads", "/ui/app/practice/commercial-settings",
    "/ui/app/clients/{id:guid}", "/ui/app/clients/{id:guid}/contacts/new", "/ui/app/engagements/{id:guid}", "/ui/app/clients/{id:guid}/assessment",
    "/ui/app/practice/proposals/{id:guid}", "/ui/app/practice/leads/{id:guid}",
    "/ui/app/finance/books", "/ui/app/finance", "/ui/app/practice/invoices/{id:guid}", "/ui/app/practice/analytics", "/ui/app/library", "/ui/app/library/{id:guid}",
    "/ui/app/practice/resources", "/ui/app/practice/time", "/ui/app/engagements/{id:guid}/statements", "/ui/app/engagements/{id:guid}/tb-intake", "/ui/app/engagements/{id:guid}/general-ledger", "/ui/app/engagements/{id:guid}/general-ledger/upload", "/ui/app/engagements/{id:guid}/audit-plan", "/ui/app/engagements/{id:guid}/audit-fieldwork", "/ui/app/engagements/{id:guid}/confirmations", "/ui/app/engagements/{id:guid}/completion", "/ui/app/completion/{id:guid}", "/ui/app/engagements/{id:guid}/pbc",
    "/ui/app/accounting/adjustment-plans", "/ui/app/accounting/adjustment-plans/{id:guid}", "/ui/app/accounting/mappings", "/ui/app/accounting/journals", "/ui/app/accounting/differences", "/ui/app/accounting/journals/{id:guid}", "/ui/app/accounting/journals/{id:guid}/management",
    "/ui/app/accounting/mappings/{id:guid}", "/ui/app/accounting/mappings/{id:guid}/approval", "/ui/app/accounting/mappings/{id:guid}/edit", "/ui/app/accounting/periods/{id:guid}", "/ui/app/accounting/reviews", "/ui/app/accounting/packages/{id:guid}",
    "/ui/app/accounting/adjustment-plans/{id:guid}/finalize", "/ui/app/accounting/sources/{id:guid}/adjustment-plan", "/ui/app/accounting/gl-sources/{id:guid}/completeness", "/ui/app/accounting/gl-sources/{id:guid}/acceptance", "/ui/app/accounting/sources/{id:guid}/acceptance", "/ui/app/accounting/sources/{id:guid}/journal-draft", "/ui/app/accounting/evidence", "/ui/app/accounting/evidence/{kind}/{id:guid}", "/ui/app/accounting/evidence/{kind}/{id:guid}/actions", "/ui/app/accounting/reconciliations/{id:guid}", "/ui/app/accounting/reconciliations/{id:guid}/prepare/{kind}", "/ui/app/accounting/rollforward", "/ui/app/accounting/restatements", "/ui/app/accounting/remeasurement", "/ui/app/accounting/currency-configuration",
    "/ui/app/audit/library", "/ui/app/audit/populations/{id:guid}", "/ui/app/audit/workpapers/{id:guid}", "/ui/app/findings/{id:guid}", "/ui/app/reviews/{id:guid}",
    "/ui/app/releases/{id:guid}", "/ui/app/records/archives/{id:guid}",
    "/ui/app/overview", "/ui/app/assessments/{id:guid}", "/ui/app/assessments/{id:guid}/decision", "/ui/app/operations", "/ui/app/administration/project-progress", "/ui/app/consolidation", "/ui/app/consolidation/scopes/{id:guid}", "/ui/app/consolidation/advanced/{id:guid}", "/ui/app/audit/plans/{id:guid}",
  ];

  /// <summary>Response options for /api/ui: camelCase, decimals as exact invariant strings, enums as names.</summary>
  public static readonly JsonSerializerOptions UiJson = CreateUiJson();

  private static JsonSerializerOptions CreateUiJson()
  {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    options.Converters.Add(new DecimalStringConverter());
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
  }

  /// <summary>Capability endpoint groups added by the Angular migration waves.</summary>
  private static void MapWorkbenchEndpoints(RouteGroupBuilder group)
  {
    MapFirmBooksEndpoints(group);
    MapPracticeInsightEndpoints(group);
    MapResourceEndpoints(group);
    MapIntakeEndpoints(group);
    MapGeneralLedgerEndpoints(group);
    MapTimeEndpoints(group);
    MapAuditPlanEndpoints(group);
    MapFieldworkEndpoints(group);
    MapConfirmationEndpoints(group);
    MapCompletionEndpoints(group);
    MapPbcEndpoints(group);
    MapPortalEndpoints(group);
    MapAdministrationEndpoints(group);
    MapMicrosoft365Endpoints(group);
    MapAccountingRecordEndpoints(group);
    MapAdjustmentPlanEndpoints(group);
    MapAccountingDetailEndpoints(group);
    MapPackageEndpoints(group);
    MapPeriodMaintenanceEndpoints(group);
    MapRemeasurementEndpoints(group);
    MapFinanceEndpoints(group);
    MapAuditRecordEndpoints(group);
    MapRouteResolutionEndpoints(group);
    MapOperationsEndpoints(group);
    MapConsolidationEndpoints(group);
  }

  // Handlers taking only HttpContext would otherwise bind as RequestDelegate and discard the IResult.
  internal static RouteHandlerBuilder MapUiGet(this RouteGroupBuilder group, string pattern, Func<HttpContext, Task<IResult>> handler) =>
    group.MapGet(pattern, (Delegate)handler);
  internal static RouteHandlerBuilder MapUiPost(this RouteGroupBuilder group, string pattern, Func<HttpContext, Task<IResult>> handler) =>
    group.MapPost(pattern, (Delegate)handler);

  /// <summary>Reads one uploaded file from a multipart command body (bounded); null when absent or oversized.</summary>
  internal static async Task<(string Name, string ContentType, byte[] Content)?> ReadUploadAsync(HttpContext http, string field, long maxBytes)
  {
    if (!http.Request.HasFormContentType) return null;
    var form = await http.Request.ReadFormAsync(http.RequestAborted);
    var file = form.Files.GetFile(field);
    if (file is null || file.Length <= 0 || file.Length > maxBytes) return null;
    using var buffer = new MemoryStream();
    await file.CopyToAsync(buffer, http.RequestAborted);
    return (Path.GetFileName(file.FileName), string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, buffer.ToArray());
  }

  internal static IResult UiResult(object? value) => Results.Json(value, UiJson);

  internal static IResult Failure(string? code, string? message, int? status = null) =>
    Results.Json(new { code = code ?? "request.failed", message = message ?? "The request could not be completed." },
      statusCode: status ?? StatusFor(code));

  internal static int StatusFor(string? code) => code switch
  {
    ErrorCodes.ScopeDenied => 403,
    ErrorCodes.GenerationStale or ErrorCodes.StaleRevision or ErrorCodes.IdempotencyConflict => 409,
    "session.unavailable" => 401,
    _ => 400
  };

  internal static async Task<IResult> ReadAsync<T>(HttpContext http, UiQuery<T> query)
  {
    var resolver = http.RequestServices.GetRequiredService<TrustedActorResolver>();
    var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
    if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
    await using var db = await http.RequestServices.GetRequiredService<IDbContextFactory<AuditSphereDbContext>>().CreateDbContextAsync(http.RequestAborted);
    var result = await query(db, actor, http.RequestAborted);
    if (!result.Succeeded)
      return Failure(result.ErrorCode, result.Message, result.ErrorCode == ErrorCodes.ScopeDenied || result.ErrorCode is null ? 403 : StatusFor(result.ErrorCode));
    if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Failure("session.unavailable", "Sign in again.", 401);
    return UiResult(result.Value);
  }

  internal static async Task<IResult> CommandAsync<T>(HttpContext http, UiQuery<T> command)
  {
    var resolver = http.RequestServices.GetRequiredService<TrustedActorResolver>();
    var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
    if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
    try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
    catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
    await using var db = await http.RequestServices.GetRequiredService<IDbContextFactory<AuditSphereDbContext>>().CreateDbContextAsync(http.RequestAborted);
    var result = await command(db, actor, http.RequestAborted);
    return result.Succeeded ? UiResult(new { value = result.Value }) : Failure(result.ErrorCode, result.Message);
  }

  internal static Task<IResult> CommandAsync(HttpContext http, UiCommand command) =>
    CommandAsync<bool>(http, async (db, actor, ct) =>
    {
      var result = await command(db, actor, ct);
      return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
    });

  internal static IResult Invalid(string message) => Failure("request.invalid", message, 400);

  internal static bool TryDate(string? value, out DateOnly date) =>
    DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

  internal static bool TryDecimal(string? value, out decimal amount) =>
    decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);

  /// <summary>Writes decimals as exact invariant strings; reads strings (preferred) or JSON numbers.</summary>
  public sealed class DecimalStringConverter : JsonConverter<decimal>
  {
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      reader.TokenType == JsonTokenType.String
        ? decimal.Parse(reader.GetString()!, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
        : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
      writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
  }
}
