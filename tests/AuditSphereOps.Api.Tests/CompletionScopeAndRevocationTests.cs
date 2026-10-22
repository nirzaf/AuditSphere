using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Testing;

namespace AuditSphereOps.Api.Tests;

/// <summary>
/// WU-015: an authorization cohort generated from the emitted contract. Every protected operation refuses an
/// anonymous caller with 401 and no success or server error; the few public operations are named explicitly.
/// </summary>
[Trait("CompletionUnit", "WU-015")]
public sealed partial class CompletionScopeAndRevocationTests(Xunit.Abstractions.ITestOutputHelper output)
{
  // Operations that are public by design: the sign-in and consent protocol pages and the SPA shell entry.
  private static readonly HashSet<string> PublicOperations = new(StringComparer.Ordinal)
  {
    "GET /", "GET /auth/sign-in", "GET /auth/sign-out", "GET /auth/landing", "GET /auth/access-not-assigned",
    "GET /auth/m365-consent/callback", "GET /auth/m365-consent/identity-callback",
  };

  [Fact]
  public async Task AnonymousCallers_AreRefusedOnEveryProtectedContractOperation()
  {
    var root = RepositoryRoot();
    using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts", "auditsphere-openapi.json")));
    await using var pg = await OwnedPostgresDatabase.CreateAsync("WU-015-ANONYMOUS");
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

    var operations = 0;
    var violations = new List<string>();
    foreach (var path in contract.RootElement.GetProperty("paths").EnumerateObject())
      foreach (var operation in path.Value.EnumerateObject().Where(o => o.Name is "get" or "post" or "put" or "delete" or "patch"))
      {
        var key = $"{operation.Name.ToUpperInvariant()} {path.Name}";
        var url = PathParameter().Replace(path.Name, match => SampleValue(operation.Value, match.Groups[1].Value));
        using var request = new HttpRequestMessage(new HttpMethod(operation.Name.ToUpperInvariant()), url);
        if (operation.Name is not "get" and not "delete") request.Content = SampleBody(operation.Value);
        using var response = await client.SendAsync(request);
        operations++;
        var status = (int)response.StatusCode;
        var refused = status == 401 || (status is 302 or 303 && response.Headers.Location?.OriginalString.Contains("sign-in", StringComparison.OrdinalIgnoreCase) == true);
        // Public protocol pages may redirect, refuse or report sign-in as unavailable, but never fail with an unhandled error.
        if (PublicOperations.Contains(key)) { Assert.True(status != 500, $"{key} returned {status}"); continue; }
        if (!refused) violations.Add($"{key} -> {status}");
      }
    output.WriteLine($"Contract operations probed anonymously: {operations}; not refused: {violations.Count}");
    foreach (var violation in violations) output.WriteLine("NOT_REFUSED " + violation);
    Assert.True(operations > 600, $"Only {operations} operations probed; the cohort would be vacuous.");
    Assert.True(violations.Count == 0, "Anonymous access not refused: " + string.Join("; ", violations));
  }

  // Each operation receives the media type its contract declares, so the probe reaches authorization rather than content negotiation.
  private static HttpContent SampleBody(JsonElement operation)
  {
    var types = operation.TryGetProperty("requestBody", out var body) && body.TryGetProperty("content", out var content)
      ? content.EnumerateObject().Select(x => x.Name).ToArray() : [];
    if (types.Contains("multipart/form-data"))
      return new MultipartFormDataContent { { new ByteArrayContent([0x20]) { Headers = { ContentType = new("text/plain") } }, "file", "probe.txt" } };
    if (types.Contains("application/octet-stream"))
      return new ByteArrayContent([0x20]) { Headers = { ContentType = new("application/octet-stream") } };
    return new StringContent("{}", Encoding.UTF8, "application/json");
  }

  private static string SampleValue(JsonElement operation, string name)
  {
    if (operation.TryGetProperty("parameters", out var parameters))
      foreach (var parameter in parameters.EnumerateArray())
        if (parameter.GetProperty("name").GetString() == name && parameter.TryGetProperty("schema", out var schema) &&
            schema.TryGetProperty("type", out var type) && type.GetString() == "integer")
          return "1";
    return Guid.NewGuid().ToString("D");
  }

  private static string RepositoryRoot()
  {
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
      if (File.Exists(Path.Combine(dir.FullName, "AuditSphereOps.slnx"))) return dir.FullName;
    throw new InvalidOperationException("Repository root (AuditSphereOps.slnx) not found.");
  }

  [GeneratedRegex(@"\{([^}:?]+)[^}]*\}")] private static partial Regex PathParameter();
}
