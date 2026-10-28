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

  private static JsonElement SchemaProperty(JsonElement contract, string schemaName, string propertyName) =>
    contract.GetProperty("components").GetProperty("schemas").GetProperty(schemaName)
      .GetProperty("properties").GetProperty(propertyName);

  private static string[] SchemaTypes(JsonElement schema)
  {
    var type = schema.GetProperty("type");
    return type.ValueKind == JsonValueKind.Array
      ? type.EnumerateArray().Select(value => value.GetString()!).ToArray()
      : [type.GetString()!];
  }

  [Fact]
  public async Task WorkflowApplicabilityContractsPreserveExactRevisionAndReceiptVersionsAsStrings()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    foreach (var (schema, property) in new[]
    {
      ("WorkflowApplicabilityProposalInput", "expectedTaskRevision"),
      ("WorkflowApplicabilityDecisionInput", "expectedTaskRevision"),
      ("WorkflowApplicabilityWorkspaceResponse", "taskRevision"),
      ("WorkflowApplicabilityProposalResponse", "version"),
      ("WorkflowApplicabilityDecisionResponse", "version"),
      ("WorkflowApplicabilityReceiptResponse", "version"),
      ("WorkflowApplicabilityHistoryItemResponse", "version")
    })
      Assert.Equal(new[] { "string" }, SchemaTypes(SchemaProperty(contract, schema, property)));
  }

  private static void AssertExactDecimalSchema(JsonElement schema, bool nullable)
  {
    var expectedTypes = nullable ? new[] { "null", "string" } : ["string"];
    Assert.Equal(expectedTypes.OrderBy(value => value, StringComparer.Ordinal),
      SchemaTypes(schema).OrderBy(value => value, StringComparer.Ordinal));
    Assert.Equal(ApiContract.DecimalStringPattern, schema.GetProperty("pattern").GetString());
    Assert.False(schema.TryGetProperty("format", out _));
    Assert.Contains("JSON numbers for compatibility", schema.GetProperty("description").GetString(), StringComparison.Ordinal);
    Assert.Contains("canonical on output", schema.GetProperty("description").GetString(), StringComparison.Ordinal);
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
    Assert.True(paths.TryGetProperty("/api/ui/accounting/clients/{clientId}/open-item-control-reconciliation", out _));
    Assert.True(paths.TryGetProperty("/api/ui/engagements/{engagementId}/workflow/tasks", out var workflowTasks));
    Assert.True(workflowTasks.TryGetProperty("get", out _));
    Assert.True(workflowTasks.TryGetProperty("post", out var createWorkflowTask));
    var createSchemaName = createWorkflowTask.GetProperty("requestBody").GetProperty("content")
      .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!.Split('/').Last();
    var createProperties = contract.GetProperty("components").GetProperty("schemas")
      .GetProperty(createSchemaName).GetProperty("properties");
    Assert.True(createProperties.TryGetProperty("idempotencyKey", out _));
    Assert.True(paths.TryGetProperty("/api/ui/engagements/{engagementId}/workflow/tasks/{taskId}/assign", out _));
    Assert.True(paths.TryGetProperty("/api/ui/engagements/{engagementId}/workflow/tasks/{taskId}/reassign", out _));
    Assert.True(paths.TryGetProperty("/api/ui/engagements/{engagementId}/workflow/tasks/{taskId}/complete", out _));
    Assert.True(paths.TryGetProperty("/api/ui/accounting/clients/{clientId}/operational-ledger", out var operationalLedger));
    var ledgerParameters = operationalLedger.GetProperty("get").GetProperty("parameters").EnumerateArray()
      .Select(parameter => parameter.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
    Assert.Contains("postingSnapshotThrough", ledgerParameters);
    Assert.Contains("accountCodeFrom", ledgerParameters);
    Assert.Contains("accountCodeTo", ledgerParameters);
    Assert.Contains("sourceType", ledgerParameters);
    Assert.Contains("reference", ledgerParameters);
    Assert.Contains("counterpartyId", ledgerParameters);
    Assert.True(paths.TryGetProperty("/api/ui/accounting/clients/{clientId}/operational-journals/{journalId}/snapshots", out var journalSnapshots));
    var snapshotSchemaName = journalSnapshots.GetProperty("get").GetProperty("responses").GetProperty("200")
      .GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("items")
      .GetProperty("$ref").GetString()!.Split('/').Last();
    var schemas = contract.GetProperty("components").GetProperty("schemas");
    var sourceOrigins = schemas.GetProperty(snapshotSchemaName).GetProperty("properties").GetProperty("sourceOrigins");
    var sourceOriginSchemaName = sourceOrigins.GetProperty("items").GetProperty("$ref").GetString()!.Split('/').Last();
    var sourceOriginProperties = schemas.GetProperty(sourceOriginSchemaName).GetProperty("properties");
    Assert.True(sourceOriginProperties.TryGetProperty("manifestSha256", out _));
    Assert.True(sourceOriginProperties.TryGetProperty("intentSha256", out _));
    Assert.True(sourceOriginProperties.TryGetProperty("evidenceSha256", out _));
    Assert.True(sourceOriginProperties.TryGetProperty("evidenceReference", out _));
    Assert.True(paths.TryGetProperty("/api/ui/accounting/clients/{clientId}/operational-opening-balances/{periodId}", out _));
    Assert.True(paths.TryGetProperty("/api/ui/accounting/clients/{clientId}/operational-opening-balances", out var openingCreate));
    Assert.True(openingCreate.TryGetProperty("post", out var openingCreatePost));
    var openingRequestSchemaName = openingCreatePost.GetProperty("requestBody").GetProperty("content")
      .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!.Split('/').Last();
    var openingRequestProperties = schemas.GetProperty(openingRequestSchemaName).GetProperty("properties");
    Assert.True(openingRequestProperties.TryGetProperty("openItems", out var openingItems));
    var openingItemSchemaName = openingItems.GetProperty("items").GetProperty("$ref").GetString()!.Split('/').Last();
    var openingItemProperties = schemas.GetProperty(openingItemSchemaName).GetProperty("properties");
    Assert.True(openingItemProperties.TryGetProperty("counterpartyId", out _));
    Assert.True(openingItemProperties.TryGetProperty("dueDate", out _));
    Assert.True(openingItemProperties.TryGetProperty("accountCode", out _));
    Assert.True(openingItemProperties.TryGetProperty("amount", out _));
    Assert.True(paths.TryGetProperty("/api/ui/accounting/clients/{clientId}/operational-opening-balances/{openingId}/approve", out var openingApproval));
    Assert.True(openingApproval.TryGetProperty("post", out _));
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
  public async Task SessionSearchAndAuthenticationProtocolsDescribeTheirActualWireShapes()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    var paths = contract.GetProperty("paths");
    JsonElement SuccessSchema(string path)
    {
      var schema = paths.GetProperty(path).GetProperty("get").GetProperty("responses").GetProperty("200")
        .GetProperty("content").GetProperty("application/json").GetProperty("schema");
      return schema.TryGetProperty("$ref", out var reference)
        ? contract.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/').Last()) : schema;
    }
    var session = SuccessSchema("/api/ui/session").GetProperty("properties");
    Assert.Equal("uuid", session.GetProperty("userId").GetProperty("format").GetString());
    Assert.Equal("uuid", session.GetProperty("firmId").GetProperty("format").GetString());
    Assert.Equal("string", session.GetProperty("generation").GetProperty("type").GetString());
    Assert.Equal("boolean", session.GetProperty("staff").GetProperty("type").GetString());
    var search = SuccessSchema("/api/ui/search").GetProperty("properties");
    Assert.Equal("array", search.GetProperty("hits").GetProperty("type").GetString());
    Assert.Equal("string", search.GetProperty("term").GetProperty("type").GetString());
    Assert.Equal("boolean", search.GetProperty("truncated").GetProperty("type").GetString());
    var signOut = paths.GetProperty("/api/ui/sign-out").GetProperty("post").GetProperty("responses");
    Assert.True(signOut.TryGetProperty("204", out var noContent));
    Assert.False(noContent.TryGetProperty("content", out _));
    Assert.False(signOut.TryGetProperty("200", out _));
    foreach (var path in new[] { "/", "/auth/sign-in", "/auth/landing", "/auth/sign-out",
      "/auth/m365-consent/callback", "/auth/m365-consent/identity-callback" })
    {
      var responses = paths.GetProperty(path).GetProperty("get").GetProperty("responses");
      Assert.True(responses.TryGetProperty("302", out var redirect), $"{path} must describe its redirect.");
      Assert.False(redirect.TryGetProperty("content", out _));
      Assert.False(responses.TryGetProperty("200", out _));
    }
    var denied = paths.GetProperty("/auth/access-not-assigned").GetProperty("get").GetProperty("responses").GetProperty("403");
    Assert.True(denied.GetProperty("content").TryGetProperty("text/html", out _));
    Assert.False(denied.GetProperty("content").TryGetProperty("application/json", out _));
  }

  [Fact]
  public async Task TypedAccountingContractsKeepDirectReadsCommandValuesAndDownloadMediaDistinct()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    var paths = contract.GetProperty("paths");
    JsonElement Resolve(JsonElement schema)
    {
      if (schema.TryGetProperty("$ref", out var reference))
        return Resolve(contract.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/').Last()));
      if (schema.TryGetProperty("oneOf", out var alternatives) || schema.TryGetProperty("anyOf", out alternatives))
        return Resolve(Assert.Single(alternatives.EnumerateArray(), candidate =>
          !candidate.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "null"));
      return schema;
    }
    JsonElement JsonSchema(string path, string method) => Resolve(paths.GetProperty(path).GetProperty(method)
      .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
    foreach (var (path, valueType, valueFormat) in new (string, string, string?)[]
    {
      ("/api/ui/gl-sources/{id}/acceptance", "string", "uuid"),
      ("/api/ui/datasets/{id}/source/acceptance", "string", "uuid"),
      ("/api/ui/datasets/{id}/adjustment-plans/preview", "object", null),
      ("/api/ui/finance/end-of-service/treatment", "string", "uuid"),
      ("/api/ui/finance/end-of-service/treatment/{id}/confirm", "boolean", null),
      ("/api/ui/finance/books/expenses/{id}/submit", "boolean", null)
    })
    {
      var value = Resolve(JsonSchema(path, "post").GetProperty("properties").GetProperty("value"));
      Assert.Contains(valueType, SchemaTypes(value));
      if (valueFormat is not null) Assert.Equal(valueFormat, value.GetProperty("format").GetString());
      Assert.True(paths.GetProperty(path).GetProperty("post").GetProperty("responses").TryGetProperty("403", out _));
    }
    foreach (var path in new[] { "/api/ui/accounting/task-owners", "/api/ui/gl-sources/{id}/acceptance",
      "/api/ui/accounting/adjustment-plans", "/api/ui/accounting/period-maintenance/clients/{clientId}/context", "/api/ui/finance/books" })
      Assert.False(JsonSchema(path, "get").GetProperty("properties").TryGetProperty("value", out _));
    foreach (var path in new[] { "/api/ui/datasets/{id}/source/export", "/api/ui/accounting/journals/{id}/instructions",
      "/api/ui/finance/books/trial-balance/export" })
    {
      var content = paths.GetProperty(path).GetProperty("post").GetProperty("responses").GetProperty("200").GetProperty("content");
      Assert.True(content.TryGetProperty("text/csv", out var csv));
      Assert.Contains("string", SchemaTypes(Resolve(csv.GetProperty("schema"))));
      Assert.False(content.TryGetProperty("application/json", out _));
    }
    foreach (var path in new[] { "/api/ui/finance/books/expenses", "/api/ui/finance/end-of-service/accruals" })
    {
      var content = paths.GetProperty(path).GetProperty("post").GetProperty("requestBody").GetProperty("content");
      var fields = Resolve(content.GetProperty("multipart/form-data").GetProperty("schema")).GetProperty("properties");
      Assert.Contains("string", SchemaTypes(fields.GetProperty("amount")));
      Assert.True(fields.TryGetProperty("evidence", out _));
      Assert.False(content.TryGetProperty("application/json", out _));
    }
    var nullableReceipt = paths.GetProperty("/api/ui/accounting/mappings/{id}/draft-receipts/{requestId}").GetProperty("get")
      .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
    var alternatives = nullableReceipt.GetProperty("anyOf").EnumerateArray().ToArray();
    Assert.Contains(alternatives, schema => schema.TryGetProperty("type", out var type) && type.GetString() == "null");
    Assert.Contains(alternatives, schema => schema.TryGetProperty("$ref", out var reference) && reference.GetString()!.EndsWith("/MappingDraftReceipt", StringComparison.Ordinal));
    var profile = contract.GetProperty("components").GetProperty("schemas").GetProperty("UiProfileResponse");
    Assert.False(profile.TryGetProperty("discriminator", out _));
    var profileVariants = profile.GetProperty("anyOf").EnumerateArray().Select(Resolve).ToArray();
    Assert.Contains(profileVariants, variant => variant.GetProperty("properties").TryGetProperty("id", out _));
    Assert.Contains(profileVariants, variant => variant.GetProperty("properties").TryGetProperty("revision", out _));
    var download = paths.GetProperty("/api/ui/finance/journals/{id}/evidence").GetProperty("post")
      .GetProperty("responses").GetProperty("200").GetProperty("content");
    Assert.False(download.TryGetProperty("application/json", out _));
    var binary = Resolve(download.GetProperty("application/octet-stream").GetProperty("schema"));
    Assert.Equal("string", binary.GetProperty("type").GetString());
    Assert.Equal("binary", binary.GetProperty("format").GetString());
    var retainedDownload = paths.GetProperty("/api/pbc/uploads/{uploadId}/download").GetProperty("get")
      .GetProperty("responses").GetProperty("200").GetProperty("content");
    var retainedBinary = Resolve(retainedDownload.GetProperty("*/*").GetProperty("schema"));
    Assert.Equal("string", retainedBinary.GetProperty("type").GetString());
    Assert.Equal("binary", retainedBinary.GetProperty("format").GetString());
    Assert.False(retainedDownload.TryGetProperty("application/json", out _));
  }

  [Fact]
  public async Task PublishedUiSuccessResponsesHaveExplicitBodySchemasOrNoContent()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    var missing = new List<string>();
    static bool HasStructuralSchema(JsonElement schema) => schema.ValueKind == JsonValueKind.False ||
      schema.ValueKind == JsonValueKind.Object && schema.EnumerateObject().Any(property => property.Name is
        "$ref" or "type" or "properties" or "items" or "additionalProperties" or
        "allOf" or "anyOf" or "oneOf" or "not" or "enum" or "const" or "pattern" or
        "minimum" or "maximum" or "minLength" or "maxLength" or "minItems" or "maxItems" or "required");
    foreach (var path in contract.GetProperty("paths").EnumerateObject()
      .Where(x => x.Name.StartsWith("/api/ui/", StringComparison.Ordinal)))
    foreach (var operation in path.Value.EnumerateObject().Where(x =>
      x.Name is "get" or "post" or "put" or "patch" or "delete"))
    foreach (var response in operation.Value.GetProperty("responses").EnumerateObject()
      .Where(x => x.Name.StartsWith('2')))
    {
      // Empty success bodies must be explicit 204s. File/media responses need their own
      // schemas too; inventing a JSON envelope does not satisfy this transport contract.
      if (response.Name == "204")
      {
        Assert.False(response.Value.TryGetProperty("content", out _), $"{operation.Name} {path.Name} 204 has a body.");
        continue;
      }
      if (!response.Value.TryGetProperty("content", out var content) ||
          !content.EnumerateObject().Any() || content.EnumerateObject().Any(x =>
            !x.Value.TryGetProperty("schema", out var schema) || !HasStructuralSchema(schema)))
        missing.Add($"{operation.Name.ToUpperInvariant()} {path.Name} {response.Name}");
    }
    Assert.True(missing.Count == 0, "Missing success schemas: " + string.Join(", ", missing));
  }

  [Fact]
  public async Task ManualProviderTransportParametersAndCookieRequirementsAreExplicit()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    var paths = contract.GetProperty("paths");
    var chunks = paths.GetProperty("/api/pbc/uploads/{uploadId}/chunks/{chunkIndex}").GetProperty("post");
    var parameters = chunks.GetProperty("parameters").EnumerateArray().ToArray();
    foreach (var name in new[] { "Origin", "X-Upload-Offset", "X-Content-SHA256", "X-Pbc-Upload-Capability" })
    {
      var header = Assert.Single(parameters, value => value.GetProperty("name").GetString() == name);
      Assert.Equal("header", header.GetProperty("in").GetString());
      Assert.True(header.GetProperty("required").GetBoolean());
    }
    var offset = Assert.Single(parameters, value => value.GetProperty("name").GetString() == "X-Upload-Offset");
    Assert.Equal(0L, offset.GetProperty("schema").GetProperty("minimum").GetInt64());
    foreach (var path in new[]
      {
        "/api/ui/proposals/{id}/client-conversion/receipts/{requestId}",
        "/api/ui/engagements/{id}/specialist-preparation/state"
      })
      Assert.True(paths.GetProperty(path).GetProperty("get").GetProperty("responses").TryGetProperty("400", out _),
        path + " must document required-query binding refusals.");
    Assert.Contains("sessionCookie", chunks.GetProperty("security").EnumerateArray()
      .SelectMany(value => value.EnumerateObject().Select(property => property.Name)));
    var rawContent = chunks.GetProperty("requestBody").GetProperty("content");
    Assert.True(rawContent.TryGetProperty("application/octet-stream", out _));
    Assert.False(rawContent.TryGetProperty("application/json", out _));
    var workspaces = paths.GetProperty("/api/ui/administration/microsoft365/workspaces").GetProperty("get");
    var page = Assert.Single(workspaces.GetProperty("parameters").EnumerateArray(),
      value => value.GetProperty("name").GetString() == "page");
    Assert.Equal("query", page.GetProperty("in").GetString());
    Assert.Equal("integer", page.GetProperty("schema").GetProperty("type").GetString());
    Assert.False(page.TryGetProperty("required", out var required) && required.GetBoolean());
  }

  [Fact]
  public async Task SafeErrorSchemaIsDocumentedForBoundaryRefusals()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    var schemas = contract.GetProperty("components").GetProperty("schemas");
    Assert.True(schemas.TryGetProperty(ApiContract.ApiErrorSchemaId, out var error));
    var errorProperties = error.GetProperty("properties");
    Assert.Contains("string", SchemaTypes(errorProperties.GetProperty("code")));
    Assert.DoesNotContain("null", SchemaTypes(errorProperties.GetProperty("code")));
    foreach (var nullableProperty in new[] { "message", "correlationId" })
    {
      Assert.True(errorProperties.TryGetProperty(nullableProperty, out var nullableSchema));
      Assert.Equal(new[] { "null", "string" },
        SchemaTypes(nullableSchema).OrderBy(value => value, StringComparer.Ordinal));
    }
    Assert.Equal(new[] { "code" }, error.GetProperty("required").EnumerateArray()
      .Select(value => value.GetString()).ToArray());

    var throttled = contract.GetProperty("paths").GetProperty("/api/ui/session")
      .GetProperty("get").GetProperty("responses").GetProperty("429");
    var schema = throttled.GetProperty("content").EnumerateObject().First().Value.GetProperty("schema");
    Assert.EndsWith("/" + ApiContract.ApiErrorSchemaId, schema.GetProperty("$ref").GetString(), StringComparison.Ordinal);

    JsonElement Resolve(JsonElement value) => value.TryGetProperty("$ref", out var reference)
      ? schemas.GetProperty(reference.GetString()!.Split('/').Last()) : value;
    var paths = contract.GetProperty("paths");
    // The shared UI endpoint filter converts unhandled read failures to this safe envelope.
    foreach (var path in paths.EnumerateObject().Where(path => path.Name.StartsWith("/api/ui/", StringComparison.Ordinal)))
    foreach (var operation in path.Value.EnumerateObject().Where(operation =>
      operation.Name is "get" or "post" or "put" or "patch" or "delete"))
    {
      var unavailable = operation.Value.GetProperty("responses").GetProperty("503")
        .GetProperty("content").GetProperty("application/json").GetProperty("schema");
      Assert.EndsWith("/" + ApiContract.ApiErrorSchemaId, unavailable.GetProperty("$ref").GetString(), StringComparison.Ordinal);
    }
    var unauthenticated = paths.GetProperty("/api/ui/practice/resources").GetProperty("get")
      .GetProperty("responses").GetProperty("401");
    Assert.Contains("empty body", unauthenticated.GetProperty("description").GetString(), StringComparison.Ordinal);
    var sessionErrors = unauthenticated.GetProperty("content").GetProperty("application/json")
      .GetProperty("schema").GetProperty("anyOf").EnumerateArray().Select(Resolve).ToArray();
    Assert.Equal(2, sessionErrors.Length);
    Assert.All(sessionErrors, variant =>
    {
      var properties = variant.GetProperty("properties");
      Assert.True(properties.TryGetProperty("code", out _));
      Assert.False(properties.TryGetProperty("correlationId", out _));
    });
    Assert.Contains(sessionErrors, variant => variant.GetProperty("properties").TryGetProperty("message", out _));
    Assert.Contains(sessionErrors, variant => !variant.GetProperty("properties").TryGetProperty("message", out _));

    var progress = paths.GetProperty("/api/ui/administration/project-progress").GetProperty("get")
      .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
    var untracked = Resolve(progress).GetProperty("properties").GetProperty("untracked").GetProperty("items");
    Assert.EndsWith("/ProjectUntrackedArea", untracked.GetProperty("$ref").GetString(), StringComparison.Ordinal);
    var untrackedProperties = Resolve(untracked).GetProperty("properties");
    Assert.Contains("string", SchemaTypes(untrackedProperties.GetProperty("name")));
    Assert.Contains("string", SchemaTypes(untrackedProperties.GetProperty("route")));

    foreach (var (path, method) in new[]
      {
        ("/api/ui/finance/end-of-service/treatment/{id}/confirm", "post"),
        ("/api/ui/engagements/{id}/completion/clearance", "post"),
        ("/api/ui/finance/books/expenses/receipts/{requestId}", "get"),
        ("/api/ui/accounting/currency-configuration", "get"),
        ("/api/ui/accounting/currency-configuration/sets/{id}", "get"),
        ("/api/ui/accounting/currency-configuration/policies/{id}", "get")
      })
    {
      var conflictSchema = paths.GetProperty(path).GetProperty(method).GetProperty("responses").GetProperty("409")
        .GetProperty("content").GetProperty("application/json").GetProperty("schema");
      Assert.EndsWith("/UiFailureResponse", conflictSchema.GetProperty("$ref").GetString(), StringComparison.Ordinal);
    }
  }

  [Fact]
  public async Task EmittedDecimalSchemasUseExactStringsAndRetainClrNullability()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);

    var journalLine = SchemaProperty(contract, "ConsolidationJournalLineInput", "debit");
    AssertExactDecimalSchema(journalLine, nullable: false);
    AssertExactDecimalSchema(SchemaProperty(contract, "ConsolidationJournalLineInput", "credit"), nullable: false);

    // This is decimal? in the actual API input type. Its property is required, but null is a
    // valid value; requiredness and nullability are separate OpenAPI semantics.
    AssertExactDecimalSchema(SchemaProperty(contract, "CreateRelationshipInput", "ownershipPercentage"), nullable: true);

    // A real integer input remains an integer schema with its int32 format and is not mistaken
    // for a financial decimal by the CLR-type-based transformer.
    var integerControl = SchemaProperty(contract, "AccountingProfileInput", "fiscalMonth");
    Assert.Contains("integer", SchemaTypes(integerControl));
    Assert.Equal("int32", integerControl.GetProperty("format").GetString());
    Assert.NotEqual(ApiContract.DecimalStringPattern, integerControl.GetProperty("pattern").GetString());
  }

  [Fact]
  public void FinancialDecimalSchemasRemainExactStrings()
  {
    var schema = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.String, Format = "double" };
    ApiContract.ApplyFinancialDecimalStringRule(schema, typeof(decimal));
    Assert.Equal(JsonSchemaType.String, schema.Type);
    Assert.Null(schema.Format);
    Assert.Equal(ApiContract.DecimalStringPattern, schema.Pattern);
    Assert.Contains("JSON numbers for compatibility", schema.Description, StringComparison.Ordinal);
    Assert.Matches(ApiContract.DecimalStringPattern, "1234567890.12");
    Assert.Matches(ApiContract.DecimalStringPattern, "-9007199254740993");
    Assert.DoesNotMatch(ApiContract.DecimalStringPattern, "1.5e10");

    var nullable = new OpenApiSchema
    {
      Type = JsonSchemaType.Null | JsonSchemaType.Number | JsonSchemaType.String,
      Format = "double"
    };
    ApiContract.ApplyFinancialDecimalStringRule(nullable, typeof(decimal?));
    Assert.Equal(JsonSchemaType.Null | JsonSchemaType.String, nullable.Type);

    var nonDecimal = new OpenApiSchema { Type = JsonSchemaType.Number, Format = "double" };
    ApiContract.ApplyFinancialDecimalStringRule(nonDecimal, typeof(double));
    Assert.Equal(JsonSchemaType.Number, nonDecimal.Type);
    Assert.Equal("double", nonDecimal.Format);
  }

  [Fact]
  public async Task NativeAccountingContractsRetainExactEnvelopesReceiptsAndEmptySuccess()
  {
    using var factory = ContractFactory();
    using var client = factory.CreateClient();
    var contract = await FetchContractAsync(client);
    var paths = contract.GetProperty("paths");
    var schemas = contract.GetProperty("components").GetProperty("schemas");
    JsonElement Response(string path, string method, string status = "200") =>
      paths.GetProperty(path).GetProperty(method).GetProperty("responses").GetProperty(status);
    JsonElement Body(JsonElement response)
    {
      var schema = response.GetProperty("content").GetProperty("application/json").GetProperty("schema");
      return schema.TryGetProperty("$ref", out var reference)
        ? schemas.GetProperty(reference.GetString()!.Split('/').Last()) : schema;
    }

    const string prefix = "/api/ui/accounting/clients/{clientId}";
    var created = Body(Response(prefix + "/account-roles", "post"));
    var id = created.GetProperty("properties").GetProperty("id");
    Assert.Contains("string", SchemaTypes(id));
    Assert.Equal("uuid", id.GetProperty("format").GetString());
    Assert.False(created.GetProperty("properties").TryGetProperty("value", out _));

    var posted = Body(Response(prefix + "/operational-journals/{journalId}/post", "post"));
    Assert.True(posted.GetProperty("properties").TryGetProperty("commandId", out _));
    Assert.False(posted.GetProperty("properties").TryGetProperty("value", out _));
    var snapshots = Body(Response(prefix + "/operational-journals/{journalId}/snapshots", "get"));
    Assert.Contains("array", SchemaTypes(snapshots));

    var approved = Response(prefix + "/operational-opening-balances/{openingId}/approve", "post", "204");
    Assert.False(approved.TryGetProperty("content", out _));
    var missingReceipt = Body(Response(prefix + "/operational-posting-receipts/{commandId}", "get", "404"));
    Assert.True(missingReceipt.GetProperty("properties").TryGetProperty("code", out _));
    var creditLookup = Body(Response(prefix + "/sales-credit-note-requests/{commandId}", "get", "404"));
    Assert.True(creditLookup.GetProperty("properties").TryGetProperty("code", out _));
    var purchaseDrafts = Body(Response(prefix + "/purchase-invoice-drafts", "get"));
    Assert.True(purchaseDrafts.GetProperty("properties").TryGetProperty("page", out _));
    Assert.Equal("boolean", purchaseDrafts.GetProperty("properties").GetProperty("hasMore").GetProperty("type").GetString());
    Assert.Equal("array", purchaseDrafts.GetProperty("properties").GetProperty("rows").GetProperty("type").GetString());
    Assert.True(purchaseDrafts.GetProperty("properties").TryGetProperty("pageSize", out _));
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
