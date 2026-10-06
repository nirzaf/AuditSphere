using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Api.Contracts;
using Microsoft.OpenApi;

namespace AuditSphereOps.Api.Tests;

/// <summary>
/// Acceptance tests for the canonical OpenAPI 3.1 contract generated from the actual endpoints.
/// Assertions run against the served HTTP artifact because that is exactly what the CI drift
/// check consumes. The contract documents transport behavior only; the custom Angular Api
/// service remains the request execution authority and runtime decoders remain in place.
/// </summary>
public sealed class OpenApiContractTests
{
  private static StandaloneApiApplicationFactory ContractFactory() => new(new Dictionary<string, string?>
  {
    ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:Subject"] = "contract-subject",
    ["DevelopmentIdentity:TenantId"] = Guid.NewGuid().ToString("D"),
    ["Application:AllowSimulationAdapters"] = "true",
    ["ExternalEffects:Enabled"] = "false"
  });

  private static async Task<JsonElement> FetchContractAsync(HttpClient client)
  {
    using var response = await client.GetAsync($"/api/contract/{ApiContract.DocumentName}.json");
    Assert.True(response.StatusCode == System.Net.HttpStatusCode.OK, $"Contract request returned {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    await using var stream = await response.Content.ReadAsStreamAsync();
    var json = await JsonDocument.ParseAsync(stream);
    return json.RootElement.Clone();
  }

  [Fact]
  public async Task ContractIsGeneratedFromActualEndpointsAsOpenApi31()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    Assert.StartsWith("3.1", contract.GetProperty("openapi").GetString(), StringComparison.Ordinal);
    var paths = contract.GetProperty("paths");
    Assert.True(paths.TryGetProperty("/api/ui/session", out _));
    Assert.True(paths.TryGetProperty("/api/ui/search", out _));
    Assert.True(paths.TryGetProperty("/auth/sign-in", out _));
    Assert.True(paths.TryGetProperty("/api/pbc/uploads/{uploadId}/chunks/{chunkIndex}", out _));
  }

  [Fact]
  public async Task ProtectedEndpointsDeclareTheSessionCookieAndBoundaryResponses()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    var paths = contract.GetProperty("paths");

    var sessionGet = paths.GetProperty("/api/ui/session").GetProperty("get");
    Assert.Contains("sessionCookie", sessionGet.GetProperty("security").EnumerateArray()
      .SelectMany(requirement => requirement.EnumerateObject().Select(property => property.Name)));
    Assert.True(sessionGet.GetProperty("responses").TryGetProperty("401", out _));
    Assert.True(sessionGet.GetProperty("responses").TryGetProperty("429", out _));

    var exportPost = paths.GetProperty("/api/ui/portfolio/export").GetProperty("post");
    Assert.True(exportPost.GetProperty("responses").TryGetProperty("413", out _));
    Assert.Contains("sessionCookie", exportPost.GetProperty("security").EnumerateArray()
      .SelectMany(requirement => requirement.EnumerateObject().Select(property => property.Name)));

    var signInGet = paths.GetProperty("/auth/sign-in").GetProperty("get");
    Assert.False(signInGet.TryGetProperty("security", out _));
  }

  [Fact]
  public async Task SafeErrorSchemaIsDocumentedForBoundaryRefusals()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    var schemas = contract.GetProperty("components").GetProperty("schemas");
    Assert.True(schemas.TryGetProperty(ApiContract.ApiErrorSchemaId, out var error));
    Assert.True(error.GetProperty("properties").TryGetProperty("code", out _));
    Assert.True(error.GetProperty("properties").TryGetProperty("correlationId", out _));

    var throttled = contract.GetProperty("paths").GetProperty("/api/ui/session")
      .GetProperty("get").GetProperty("responses").GetProperty("429");
    var schema = throttled.GetProperty("content").EnumerateObject().First().Value.GetProperty("schema");
    Assert.EndsWith("/" + ApiContract.ApiErrorSchemaId, schema.GetProperty("$ref").GetString(), StringComparison.Ordinal);
  }

  [Fact]
  public void FinancialDecimalSchemasRemainExactStrings()
  {
    var schema = new OpenApiSchema { Type = JsonSchemaType.Number, Format = "decimal" };
    ApiContract.ApplyFinancialDecimalStringRule(schema);
    Assert.Equal(JsonSchemaType.String, schema.Type);
    Assert.Null(schema.Format);
    Assert.Equal(ApiContract.DecimalStringPattern, schema.Pattern);
    Assert.Matches(ApiContract.DecimalStringPattern, "1234567890.12");
    Assert.Matches(ApiContract.DecimalStringPattern, "-9007199254740993");
    Assert.DoesNotMatch(ApiContract.DecimalStringPattern, "1.5e10");
  }

  [Fact]
  public async Task ContractSerializationIsDeterministicForDriftDetection()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var first = await client.GetStringAsync($"/api/contract/{ApiContract.DocumentName}.json");
    var second = await client.GetStringAsync($"/api/contract/{ApiContract.DocumentName}.json");
    Assert.Equal(first, second);

    // CI contract drift check: emit the current contract when asked, then compare with the
    // committed artifact (scripts/contracts/verify-openapi.sh).
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_EMIT_OPENAPI") is { Length: > 0 } path)
      await File.WriteAllTextAsync(path, first);
  }
}
