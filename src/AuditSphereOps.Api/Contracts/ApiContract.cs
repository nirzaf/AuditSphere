using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace AuditSphereOps.Api.Contracts;

/// <summary>
/// The canonical OpenAPI 3.1 contract, generated from the actual ASP.NET Core endpoints. The
/// document describes transport behavior only: URLs, methods, authentication requirements and
/// boundary-produced responses. It is never the business authorization authority. Financial
/// decimal values are described as exact strings, matching the DecimalStringConverter wire
/// behavior; commands are never retried by clients and uncertain outcomes stay uncertain.
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
            ["message"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Safe user-facing message." },
            ["correlationId"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Matches X-Correlation-Id telemetry." }
          },
          Required = new HashSet<string> { "code" }
        };
      });
      options.AddOperationTransformer(async (operation, context, ct) =>
      {
        var authorized = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any();
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
      });
      options.AddSchemaTransformer(async (schema, context, ct) => ApplyFinancialDecimalStringRule(schema));
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
  /// JavaScript-compatible floating-point numbers.
  /// </summary>
  internal static void ApplyFinancialDecimalStringRule(OpenApiSchema schema)
  {
    if (schema.Type is JsonSchemaType.Number && string.Equals(schema.Format, "decimal", StringComparison.OrdinalIgnoreCase))
    {
      schema.Type = JsonSchemaType.String;
      schema.Format = null;
      schema.Pattern = DecimalStringPattern;
    }
  }
}

file static class ApiDescriptionExtensions
{
  /// <summary>JSON-bodied commands can be refused by the boundary size guard; multipart uploads cannot.</summary>
  public static bool AcceptsJsonBody(this Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description) =>
    description.SupportedRequestFormats.Any(f =>
      string.Equals(f.MediaType, "application/json", StringComparison.OrdinalIgnoreCase));
}
