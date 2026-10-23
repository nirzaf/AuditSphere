using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

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

  [Theory]
  [InlineData("request-review")]
  [InlineData("record-review")]
  [InlineData("decision")]
  [InlineData("continuance")]
  public async Task AcceptanceCommands_ReturnScopeDenialWithoutEffects(string command)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("WU015-ACCEPT-" + command.ToUpperInvariant());
    var own = await PbcSeed.SeedAsync(pg);
    var target = await SiblingClientSeed.SeedAsync(pg, own.FirmId, "WU015");
    var foreign = await PbcSeed.SeedAsync(pg);
    var reviewId = Guid.NewGuid();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(own.FirmId, own.Staff, "Partner", own.ClientId),
        PbcSeed.Grant(foreign.FirmId, foreign.Staff, "Partner", foreign.ClientId));
      db.SpecialistClearances.Add(new SpecialistClearance
      {
        Id = reviewId, FirmId = target.Fixture.FirmId, PracticeClientId = target.Fixture.ClientId,
        Area = "AML", SpecialistName = "Synthetic specialist", Status = "PENDING", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var siblingFactory = Factory(pg, own.Staff);
    using var siblingClient = siblingFactory.CreateClient(new() { AllowAutoRedirect = false });
    var siblingCsrf = await SignIn(siblingClient);
    using var foreignFactory = Factory(pg, foreign.Staff);
    using var foreignClient = foreignFactory.CreateClient(new() { AllowAutoRedirect = false });
    var foreignCsrf = await SignIn(foreignClient);

    var baseline = await CaptureAcceptanceState(pg, target.Fixture.FirmId, target.Fixture.ClientId);
    var (path, validBody) = BuildAcceptanceCommand(command, target.Fixture.ClientId, reviewId, "1");

    using (var noCsrf = await Post(siblingClient, path, null, validBody))
      await AssertFailure(noCsrf, HttpStatusCode.Forbidden, "csrf.invalid");
    var (_, invalidGenerationBody) = BuildAcceptanceCommand(command, target.Fixture.ClientId, reviewId, "0");
    using (var invalidGeneration = await Post(siblingClient, path, siblingCsrf, invalidGenerationBody))
      await AssertFailure(invalidGeneration, HttpStatusCode.BadRequest, "request.invalid");
    Assert.Equal(baseline, await CaptureAcceptanceState(pg, target.Fixture.FirmId, target.Fixture.ClientId));

    // Both callers carry a valid session and CSRF token, and each command body is valid.
    using (var siblingDenied = await Post(siblingClient, path, siblingCsrf, validBody))
      await AssertFailure(siblingDenied, HttpStatusCode.Forbidden, "scope.denied");
    Assert.Equal(baseline, await CaptureAcceptanceState(pg, target.Fixture.FirmId, target.Fixture.ClientId));
    using (var wrongFirmDenied = await Post(foreignClient, path, foreignCsrf, validBody))
      await AssertFailure(wrongFirmDenied, HttpStatusCode.Forbidden, "scope.denied");
    Assert.Equal(baseline, await CaptureAcceptanceState(pg, target.Fixture.FirmId, target.Fixture.ClientId));
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
    ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = user.Subject,
    ["DevelopmentIdentity:TenantId"] = user.TenantId, ["Application:AllowSimulationAdapters"] = "true",
    ["ExternalEffects:Enabled"] = "false"
  });

  private static async Task<string> SignIn(HttpClient client)
  {
    using var signIn = await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    var token = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
    Assert.NotEmpty(token);
    return token;
  }

  private static (string Path, object Body) BuildAcceptanceCommand(string command, Guid clientId, Guid reviewId, string generation)
  {
    var root = $"/api/ui/clients/{clientId:D}/acceptance";
    return command switch
    {
      "request-review" => ($"{root}/reviews", new { area = "AML", specialist = "Synthetic specialist", generation }),
      "record-review" => ($"{root}/reviews/{reviewId:D}", new { status = "HOLD", evidence = (string?)null, conditions = (string?)null, generation, expectedStatus = "PENDING" }),
      "decision" => ($"{root}/decision", new { serviceRoute = "FinancialStatementAudit", decision = "Accepted", rationale = "Synthetic scope-denial probe", conditions = (string?)null, generation }),
      "continuance" => ($"{root}/continuance", new { generation }),
      _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown acceptance command.")
    };
  }

  private static Task<HttpResponseMessage> Post(HttpClient client, string path, string? csrf, object body)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    if (csrf is not null) request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }

  private static async Task AssertFailure(HttpResponseMessage response, HttpStatusCode status, string code)
  {
    Assert.Equal(status, response.StatusCode);
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
  }

  private static async Task<string> CaptureAcceptanceState(OwnedPostgresDatabase pg, Guid firmId, Guid clientId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var state = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleAsync();
    var clientStatus = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == clientId)
      .Select(x => x.Status).SingleAsync();
    var decisions = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == firmId && x.PracticeClientId == clientId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.Decision, x.Generation, x.Path, x.PriorDecisionId, x.DecidedByUserId, x.DecidedAt, x.Rationale, x.Conditions }).ToListAsync();
    var clearances = await db.SpecialistClearances.AsNoTracking().Where(x => x.FirmId == firmId && x.PracticeClientId == clientId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.Area, x.SpecialistName, x.Status, x.EvidenceReference, x.Conditions, x.SpecialistUserId, x.ClearedAt }).ToListAsync();
    var responses = await db.EvaluationResponses.AsNoTracking().Where(x => x.FirmId == firmId && x.PracticeClientId == clientId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.Bank, x.QuestionId, x.Generation, x.Revision, x.Answer, x.EvidenceReference, x.AnsweredByUserId }).ToListAsync();
    var workspaces = await db.ClientWorkspaces.AsNoTracking().Where(x => x.FirmId == firmId && x.PracticeClientId == clientId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.AcceptanceDecisionId, x.State, x.Revision }).ToListAsync();
    return JsonSerializer.Serialize(new { generation = state, clientStatus, decisions, clearances, responses, workspaces });
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
