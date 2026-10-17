using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace AuditSphereOps.Api.Contracts;

/// <summary>Marks an existing JSON read whose successful body may be null; CLR response metadata erases reference nullability.</summary>
public sealed class NullableJsonSuccessBodyMetadata { }

/// <summary>Documents a download whose retained media type is determined by the uploaded evidence.</summary>
public sealed class RetainedBinaryContentTypeMetadata { }

/// <summary>Documents scalar inputs read manually from HttpContext without changing binding or refusal order.</summary>
public sealed record ManualParameterMetadata(string Name, ParameterLocation Location, bool Required,
  JsonSchemaType Type, string? Format = null, string? Pattern = null, string? Description = null);

/// <summary>Transport-only session declaration for handlers that resolve the cookie actor themselves.</summary>
public sealed class CookieSessionContractMetadata { }

/// <summary>
/// The canonical OpenAPI 3.1 contract, generated from the actual ASP.NET Core endpoints. The
/// document describes transport behavior only: URLs, methods, authentication requirements and
/// boundary-produced responses. It is never the business authorization authority. Financial
/// decimal values are described as exact strings, matching the DecimalStringConverter wire
/// behavior. Unknown command outcomes require explicit recovery. Native client journal posting alone supports
/// user-confirmed retry of the original durable command identity; no generic automatic retry is implied.
/// </summary>
public static class ApiContract
{
  public const string DocumentName = "auditsphere";
  public const string SessionCookieSchemeId = "sessionCookie";
  public const string ApiErrorSchemaId = "ApiError";

  /// <summary>OpenAPI schema pattern for exact .NET decimal values transported as invariant strings.</summary>
  public const string DecimalStringPattern = "^-?[0-9]{1,29}(?:\\.[0-9]{1,28})?$";

  public static void Configure(WebApplicationBuilder builder) =>
    builder.Services.AddOpenApi(DocumentName, options =>
    {
      options.AddDocumentTransformer(async (document, context, ct) =>
      {
        document.Info.Title = "AuditSphereOps API";
        document.Info.Description = "Transport contract for the AuditSphereOps Angular application. " +
          "HTTP authentication only establishes an AuditSphere browser session; every business action is " +
          "authorized in the Application layer.";
        var cookieOptions = context.ApplicationServices
          .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
          .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SessionCookieSchemeId] = new OpenApiSecurityScheme
        {
          Type = SecuritySchemeType.ApiKey,
          In = ParameterLocation.Cookie,
          Name = cookieOptions?.Cookie?.Name ?? ".AspNetCore.Cookies",
          Description = "The AuditSphere session cookie established at sign-in."
        };
        document.Components.Schemas[ApiErrorSchemaId] = new OpenApiSchema
        {
          Type = JsonSchemaType.Object,
          Properties = new Dictionary<string, IOpenApiSchema>
          {
            ["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Stable bounded error code." },
            ["message"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Description = "Safe user-facing message." },
            ["correlationId"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Description = "Matches X-Correlation-Id telemetry." }
          },
          Required = new HashSet<string> { "code" }
        };
      });
      options.AddOperationTransformer(async (operation, context, ct) =>
      {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        // Produces metadata rejects wildcard media types at host startup. OpenAPI allows
        // them: uploaded evidence retains its original type rather than always being octet-stream.
        if (metadata.OfType<RetainedBinaryContentTypeMetadata>().Any() &&
            operation.Responses?.TryGetValue("200", out var binaryResponse) == true &&
            binaryResponse.Content?.Remove("application/octet-stream", out var binaryContent) == true)
          binaryResponse.Content["*/*"] = binaryContent;
        foreach (var parameter in metadata.OfType<ManualParameterMetadata>())
        {
          operation.Parameters ??= [];
          operation.Parameters.Add(new OpenApiParameter
          {
            Name = parameter.Name, In = parameter.Location, Required = parameter.Required,
            Description = parameter.Description,
            Schema = new OpenApiSchema { Type = parameter.Type, Format = parameter.Format, Pattern = parameter.Pattern }
          });
        }
        var authorized = metadata.OfType<IAuthorizeData>().Any() || metadata.OfType<CookieSessionContractMetadata>().Any();
        if (!authorized) return;
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
          [new OpenApiSecuritySchemeReference(SessionCookieSchemeId, context.Document)] = new List<string>()
        });
        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", BoundaryError("The request has no current AuditSphere session."));
        operation.Responses.TryAdd("429", BoundaryError("The boundary rate limit rejected the request."));
        if (HttpMethods.IsPost(context.Description.HttpMethod ?? "") && context.Description.AcceptsJsonBody())
          operation.Responses.TryAdd("413", BoundaryError("The JSON command body exceeded the accepted size."));
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<NullableJsonSuccessBodyMetadata>().Any() &&
            operation.Responses.TryGetValue("200", out var success) && success.Content is not null &&
            success.Content.TryGetValue("application/json", out var json) && json.Schema is not null)
          json.Schema = new OpenApiSchema { AnyOf = [json.Schema, new OpenApiSchema { Type = JsonSchemaType.Null }] };
      });
      options.AddSchemaTransformer((schema, context, _) =>
      {
        var clrType = context.JsonPropertyInfo?.PropertyType ?? context.JsonTypeInfo.Type;
        ApplyFinancialDecimalStringRule(schema, clrType);
        // This metadata-only union describes existing anonymous {id}/{revision} replies.
        // Neither reply carries a discriminator on the wire.
        if (clrType == typeof(AuditSphereOps.Api.Ui.UiEndpoints.UiProfileResponse))
          schema.Discriminator = null;
        return Task.CompletedTask;
      });
    });

  /// <summary>Exposes the contract outside production; production exposure stays disabled by default.</summary>
  public static void Map(WebApplication app)
  {
    if (!app.Environment.IsProduction())
      app.MapOpenApi("/api/contract/{documentName}.json");
  }

  private static OpenApiResponse BoundaryError(string description) => new()
  {
    Description = description,
    Content = new Dictionary<string, OpenApiMediaType>
    {
      ["application/json"] = new OpenApiMediaType { Schema = new OpenApiSchemaReference(ApiErrorSchemaId) }
    }
  };

  /// <summary>
  /// Exact decimal wire values stay strings; financial values are never described as
  /// JavaScript-compatible floating-point numbers. Request bodies also accept JSON numbers
  /// for compatibility, while responses are emitted as invariant decimal strings.
  /// </summary>
  internal static void ApplyFinancialDecimalStringRule(OpenApiSchema schema, Type clrType)
  {
    var underlyingType = Nullable.GetUnderlyingType(clrType);
    if ((underlyingType ?? clrType) != typeof(decimal)) return;

    var allowsNull = underlyingType is not null || (schema.Type & JsonSchemaType.Null) != 0;
    schema.Type = allowsNull ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
    schema.Format = null;
    schema.Pattern = DecimalStringPattern;
    const string decimalDescription = "Exact invariant decimal strings are canonical on output. Request bodies also accept JSON numbers for compatibility; amount-specific range and scale are enforced by the owning command.";
    if (schema.Description?.Contains(decimalDescription, StringComparison.Ordinal) is not true)
      schema.Description = string.IsNullOrWhiteSpace(schema.Description)
        ? decimalDescription
        : $"{schema.Description} {decimalDescription}";
  }
}

file static class ApiDescriptionExtensions
{
  /// <summary>JSON-bodied commands can be refused by the boundary size guard; multipart uploads cannot.</summary>
  public static bool AcceptsJsonBody(this Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description) =>
    description.SupportedRequestFormats.Any(f =>
      string.Equals(f.MediaType, "application/json", StringComparison.OrdinalIgnoreCase));
}
