using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Testing;

namespace AuditSphereOps.Api.Tests;

/// <summary>
/// WU-003: every Angular API literal resolves to a path in the emitted contract (exactly, or as the base of a
/// path family the caller extends), and the HTTP boundary rejects malformed payloads, missing antiforgery
/// proof and out-of-scope routes without a success or server-error response.
/// </summary>
[Trait("CompletionUnit", "WU-003")]
public sealed partial class CompletionContractTraceTests(Xunit.Abstractions.ITestOutputHelper output)
{
  [Fact]
  public void AngularApiCallers_ResolveToEmittedContractPaths()
  {
    var root = RepositoryRoot();
    using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts", "auditsphere-openapi.json")));
    var templates = contract.RootElement.GetProperty("paths").EnumerateObject()
      .Select(path => path.Name).Where(path => path != "/")
      .Select(path => PathParameter().Replace(path, "*").Trim('/').Split('/')).ToList();

    var callers = 0;
    var unmatched = new List<string>();
    var app = Path.Combine(root, "src", "AuditSphereOps.Ui", "src", "app");
    foreach (var file in Directory.EnumerateFiles(app, "*.ts", SearchOption.AllDirectories).Where(f => !f.EndsWith(".spec.ts")))
    {
      var lines = File.ReadAllLines(file);
      for (var index = 0; index < lines.Length; index++)
        foreach (Match literal in ApiLiteral().Matches(lines[index]))
        {
          callers++;
          var url = literal.Groups[1].Value.Split('?')[0];
          url = TrailingQueryInterpolation().Replace(url, "");     // `${query}` glued to the last segment
          url = Interpolation().Replace(url, "*");
          var isPrefix = url.EndsWith('/');
          // Arrow-function base helpers may put their returned literal on the next line.
          var declaration = index > 0 && lines[index - 1].TrimEnd().EndsWith("=>", StringComparison.Ordinal)
            ? lines[index - 1] + lines[index] : lines[index];
          var segments = url.Trim('/').Split('/');
          // A literal matches a template of equal length, or is the base of a longer one that callers extend.
          if (!templates.Any(template => template.Length >= segments.Length && segments.Zip(template).All(pair => SegmentMatches(pair.First, pair.Second))
              && (isPrefix || template.Length == segments.Length || IsBaseUrl(declaration))))
            unmatched.Add($"{Path.GetRelativePath(root, file)}:{index + 1} {url}");
        }
    }
    output.WriteLine($"Angular API literals: {callers}; contract paths: {templates.Count}; unmatched: {unmatched.Count}");
    Assert.True(callers > 400, $"Only {callers} Angular API literals found; the trace would be vacuous.");
    Assert.True(unmatched.Count == 0, string.Join(Environment.NewLine, unmatched));
  }

  [Fact]
  public async Task CommandBoundary_RejectsMalformedPayloadMissingProofAndOutOfScopeRoute()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("WU-003-CONTRACT-NEGATIVE");
    var seed = await PbcSeed.SeedAsync(pg);
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = seed.Staff.Subject,
      ["DevelopmentIdentity:TenantId"] = seed.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fapp")).StatusCode);
    using var session = await client.GetAsync("/api/ui/session");
    var token = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(h => h.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
    var preview = $"/api/ui/engagements/{seed.EngagementId}/audit-plan/commands/preview";

    // Malformed JSON with valid proof: a client error, never success and never a server error.
    using var malformed = new HttpRequestMessage(HttpMethod.Post, preview) { Content = new StringContent("{\"command\":", Encoding.UTF8, "application/json") };
    malformed.Headers.Add("X-XSRF-TOKEN", token);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(malformed)).StatusCode);

    // Missing antiforgery proof on a state-changing route is refused before any command runs.
    using var unproven = await client.PostAsJsonAsync(preview, new { });
    Assert.InRange((int)unproven.StatusCode, 400, 403);

    // A staff identity without accounting grants is denied the client accounting command even with valid proof.
    using var denied = new HttpRequestMessage(HttpMethod.Post, $"/api/ui/accounting/clients/{seed.ClientId}/profile")
    {
      Content = JsonContent.Create(new { profileId = (Guid?)null, revision = "0", jurisdiction = "QA", currency = "QAR",
        fiscalMonth = 1, fiscalDay = 1, sourceSystem = "TEST", sourceIdentifier = "TEST", reviewed = true })
    };
    denied.Headers.Add("X-XSRF-TOKEN", token);
    // Antiforgery passes here, so the 403 comes from the scope decision itself, as the contract declares.
    using var deniedResponse = await client.SendAsync(denied);
    Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    Assert.Contains("scope.denied", await deniedResponse.Content.ReadAsStringAsync());
  }

  private static bool SegmentMatches(string caller, string template) =>
    caller == "*" || template == "*" || caller == template ||
    (caller.Contains('*') && Regex.IsMatch(template, "^" + Regex.Escape(caller).Replace("\\*", ".+") + "$"));

  // `const base = '/api/…'` or `return `/api/…`` helpers name a path family the caller extends.
  private static bool IsBaseUrl(string line) => BaseHelper().IsMatch(line);

  private static string RepositoryRoot()
  {
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
      if (File.Exists(Path.Combine(dir.FullName, "AuditSphereOps.slnx"))) return dir.FullName;
    throw new InvalidOperationException("Repository root (AuditSphereOps.slnx) not found.");
  }

  [GeneratedRegex(@"[`'""](/api/[^`'""\s]*)")] private static partial Regex ApiLiteral();
  [GeneratedRegex(@"\{[^}]+\}")] private static partial Regex PathParameter();
  [GeneratedRegex(@"\$\{[^}]*\}")] private static partial Regex Interpolation();
  [GeneratedRegex(@"(?<=[^/])\$\{[^}]*\}$")] private static partial Regex TrailingQueryInterpolation();
  [GeneratedRegex(@"\b(const|let)\s+\w*[bB]ase\w*\s*=|\bbase\w*\s*\([^)]*\)\s*(:\s*string\s*)?\{?\s*(return\s+)?[`'""]/api/|private\s+\w*[bB]ase\w*\(")] private static partial Regex BaseHelper();
}
