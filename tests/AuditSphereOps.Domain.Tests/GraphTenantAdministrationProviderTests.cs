using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Graph provider behaviour with stubbed HTTP: no live Microsoft access is used.</summary>
public sealed class GraphTenantAdministrationProviderTests : IDisposable
{
  private static readonly string Tenant = Guid.NewGuid().ToString("D");
  private readonly string directory = Path.Combine(Path.GetTempPath(), "as-graph-" + Guid.NewGuid().ToString("N"));
  private readonly string certificatePath;
  private readonly string keyPath;

  public GraphTenantAdministrationProviderTests()
  {
    Directory.CreateDirectory(directory);
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=AuditSphere test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    certificatePath = Path.Combine(directory, "cert.pem");
    keyPath = Path.Combine(directory, "key.pem");
    File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
    File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
  }

  public void Dispose() { try { Directory.Delete(directory, recursive: true); } catch (IOException) { } }

  private GraphCapabilityCredentialOptions Credential(string role) =>
    new(true, Tenant, Guid.NewGuid().ToString("D"), certificatePath, keyPath, role);

  private static string Jwt(object payload) =>
    "eyJhbGciOiJub25lIn0." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";

  private static HttpResponseMessage TokenResponse(params string[] roles) => Json(JsonSerializer.Serialize(new
  {
    access_token = Jwt(new { tid = Tenant, aud = "https://graph.microsoft.com", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), roles })
  }));

  private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
    new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

  [Fact]
  public async Task TokenSource_RequiresExactlyTheCapabilityRole()
  {
    var granted = new GraphCapabilityTokenSource(new HttpClient(new Stub(_ => TokenResponse("User.Create"))), Credential("User.Create"));
    Assert.Contains("User.Create", (await granted.GetAsync(Tenant, default)).ApplicationRoles);
    var missing = new GraphCapabilityTokenSource(new HttpClient(new Stub(_ => TokenResponse())), Credential("User.Create"));
    var notGranted = await Assert.ThrowsAsync<GraphCapabilityTokenException>(() => missing.GetAsync(Tenant, default));
    Assert.True(notGranted.NotGranted);
    // A credential holding extra roles (e.g. Sites.Selected + User.Create) is refused: selected-site isolation.
    var broad = new GraphCapabilityTokenSource(new HttpClient(new Stub(_ => TokenResponse("User.Create", "Sites.Selected"))), Credential("User.Create"));
    Assert.Equal("app-role-set-too-broad", (await Assert.ThrowsAsync<GraphCapabilityTokenException>(() => broad.GetAsync(Tenant, default))).Code);
    var wrongTenant = new GraphCapabilityTokenSource(new HttpClient(new Stub(_ => TokenResponse("User.Create"))), Credential("User.Create"));
    await Assert.ThrowsAsync<GraphCapabilityTokenException>(() => wrongTenant.GetAsync(Guid.NewGuid().ToString("D"), default));
  }

  [Theory]
  [InlineData(HttpStatusCode.Created, ProviderOutcomes.Accepted)]
  [InlineData(HttpStatusCode.BadRequest, ProviderOutcomes.Failed)]
  [InlineData(HttpStatusCode.Forbidden, ProviderOutcomes.Failed)]
  [InlineData(HttpStatusCode.TooManyRequests, ProviderOutcomes.Unknown)]
  [InlineData(HttpStatusCode.ServiceUnavailable, ProviderOutcomes.Unknown)]
  [InlineData(HttpStatusCode.GatewayTimeout, ProviderOutcomes.Unknown)]
  public async Task CreateUser_ClassifiesMicrosoftOutcomes(HttpStatusCode status, string expected)
  {
    var created = Guid.NewGuid().ToString("D");
    string? sentBody = null;
    var graph = new Stub(request =>
    {
      Assert.Equal(HttpMethod.Post, request.Method);
      Assert.Equal("https://graph.microsoft.com/v1.0/users", request.RequestUri!.ToString());
      Assert.True(request.Headers.Contains("client-request-id"));
      sentBody = request.Content!.ReadAsStringAsync().Result;
      return Json(status == HttpStatusCode.Created ? $$"""{"id":"{{created}}"}""" : """{"error":{"code":"x","message":"raw provider text"}}""", status);
    });
    var provisioner = new GraphDirectoryUserProvisioner(new HttpClient(graph),
      new(new HttpClient(new Stub(_ => TokenResponse("User.Create"))), Credential("User.Create")),
      new(new HttpClient(new Stub(_ => TokenResponse("User.Read.All"))), Credential("User.Read.All")));
    var result = await provisioner.CreateUserAsync(Tenant,
      new("Ada", "ada@example.test", "ada", true, "Temp#Pass1234567", true), default);
    Assert.Equal(expected, result.Outcome);
    Assert.False(string.IsNullOrWhiteSpace(result.CorrelationId));
    Assert.DoesNotContain("raw provider text", result.ErrorCode ?? string.Empty);
    if (expected == ProviderOutcomes.Accepted) Assert.Equal(created, result.ObjectId);
    using var body = JsonDocument.Parse(sentBody!);
    Assert.True(body.RootElement.GetProperty("passwordProfile").GetProperty("forceChangePasswordNextSignIn").GetBoolean());
  }

  [Fact]
  public async Task CreateUser_TransportTimeoutIsUnknown()
  {
    var provisioner = new GraphDirectoryUserProvisioner(new HttpClient(new Stub(_ => throw new HttpRequestException("reset"))),
      new(new HttpClient(new Stub(_ => TokenResponse("User.Create"))), Credential("User.Create")),
      new(new HttpClient(new Stub(_ => TokenResponse("User.Read.All"))), Credential("User.Read.All")));
    var result = await provisioner.CreateUserAsync(Tenant, new("Ada", "ada@example.test", "ada", true, "Temp#Pass1234567", true), default);
    Assert.Equal(ProviderOutcomes.Unknown, result.Outcome);
  }

  [Fact]
  public async Task Groups_UnknownRoleAssignabilityIsTreatedAsPrivileged()
  {
    var groupId = Guid.NewGuid().ToString("D");
    var provider = new GraphGroupMembershipProvider(new HttpClient(new Stub(_ =>
        Json($$"""{"id":"{{groupId}}","displayName":"G","securityEnabled":true,"groupTypes":[]}"""))),
      new(new HttpClient(new Stub(_ => TokenResponse("GroupMember.ReadWrite.All"))), Credential("GroupMember.ReadWrite.All")));
    var group = await provider.GetGroupAsync(Tenant, groupId, default);
    Assert.True(group.IsAssignableToRole);
  }

  [Fact]
  public void IdentityChallenge_RequestsProfileForImmutableObjectId()
  {
    var clientId = Guid.NewGuid().ToString("D");
    var verifier = new GraphTenantConsentVerifier(new HttpClient(new Stub(_ => Json("{}"))),
      new TenantConsentVerifierOptions(true, Tenant, clientId,
        "https://app.example.test/auth/m365-consent/identity-callback", certificatePath, keyPath),
      new Dictionary<string, GraphCapabilityTokenSource>());
    var challenge = verifier.BuildIdentityChallenge(Tenant, "state", "nonce");
    Assert.Contains("scope=openid%20profile", challenge.Query);
  }

  [Fact]
  public async Task SelectedSite_AppRoleAloneDoesNotVerifySavedResource()
  {
    var verifier = new GraphTenantConsentVerifier(new HttpClient(new Stub(_ => Json("{}"))),
      new TenantConsentVerifierOptions(true, Tenant, Guid.NewGuid().ToString("D"),
        "https://app.example.test/auth/m365-consent/identity-callback", certificatePath, keyPath),
      new Dictionary<string, GraphCapabilityTokenSource>
      {
        [Microsoft365Capabilities.SelectedSite] = new(
          new HttpClient(new Stub(_ => TokenResponse("Sites.Selected"))), Credential("Sites.Selected"))
      });
    var result = await verifier.VerifyCapabilitiesAsync(Tenant,
      [new CapabilityProbe(Microsoft365Capabilities.SelectedSite, "Sites.Selected")], default);
    Assert.Equal(CapabilityVerificationStates.BlockedExternal, Assert.Single(result).State);
    Assert.Equal("selected-site-resource-not-verified", result[0].DiagnosticCode);
  }

  [Fact]
  public void IdToken_ValidationRequiresTenantAudienceIssuerNonceAndWorkIdentity()
  {
    var client = Guid.NewGuid().ToString("D");
    var oid = Guid.NewGuid().ToString("D");
    JsonElement Claims(object value) => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement;
    var exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
    var issuer = $"https://login.microsoftonline.com/{Tenant}/v2.0";
    var valid = GraphTenantConsentVerifier.ValidateIdToken(Claims(new { tid = Tenant, aud = client, iss = issuer, oid, nonce = "n", exp }),
      Tenant, client, DateTimeOffset.UtcNow);
    Assert.Equal((Tenant, oid, false), (valid.TenantId, valid.ObjectId, valid.ExternalIdentity));
    var guest = GraphTenantConsentVerifier.ValidateIdToken(Claims(new { tid = Tenant, aud = client, iss = issuer, oid, nonce = "n", exp,
      idp = "https://sts.windows.net/" + Guid.NewGuid() + "/" }), Tenant, client, DateTimeOffset.UtcNow);
    Assert.True(guest.ExternalIdentity);
    Assert.Throws<InvalidOperationException>(() => GraphTenantConsentVerifier.ValidateIdToken(
      Claims(new { tid = Guid.NewGuid().ToString("D"), aud = client, iss = issuer, oid, nonce = "n", exp }), Tenant, client, DateTimeOffset.UtcNow));
    Assert.Throws<InvalidOperationException>(() => GraphTenantConsentVerifier.ValidateIdToken(
      Claims(new { tid = Tenant, aud = Guid.NewGuid().ToString("D"), iss = issuer, oid, nonce = "n", exp }), Tenant, client, DateTimeOffset.UtcNow));
    Assert.Throws<InvalidOperationException>(() => GraphTenantConsentVerifier.ValidateIdToken(
      Claims(new { tid = Tenant, aud = client, iss = issuer, oid, exp }), Tenant, client, DateTimeOffset.UtcNow));
    Assert.Throws<InvalidOperationException>(() => GraphTenantConsentVerifier.ValidateIdToken(
      Claims(new { tid = Tenant, aud = client, iss = issuer, oid, nonce = "n", exp = 1 }), Tenant, client, DateTimeOffset.UtcNow));
    Assert.Throws<InvalidOperationException>(() => GraphTenantConsentVerifier.ValidateIdToken(
      Claims(new { tid = "9188040d-6c67-4c5b-b112-36a304b66dad", aud = client, iss = "https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0", oid, nonce = "n", exp }),
      "9188040d-6c67-4c5b-b112-36a304b66dad", client, DateTimeOffset.UtcNow));
  }

  [Fact]
  public async Task CapabilityVerification_MapsGrantedMissingAndUnconfiguredCredentials()
  {
    var verifier = new GraphTenantConsentVerifier(new HttpClient(new Stub(_ => Json("""{"value":[]}"""))),
      new TenantConsentVerifierOptions(false, Tenant, Guid.NewGuid().ToString("D"), "https://app.example.test/auth/m365-consent/identity-callback", certificatePath, keyPath),
      new Dictionary<string, GraphCapabilityTokenSource>
      {
        [Microsoft365Capabilities.DirectoryRead] = new(new HttpClient(new Stub(_ => TokenResponse("User.Read.All"))), Credential("User.Read.All")),
        [Microsoft365Capabilities.GuestInvitation] = new(new HttpClient(new Stub(_ => TokenResponse())), Credential("User.Invite.All")),
        [Microsoft365Capabilities.GroupMembership] = new(new HttpClient(new Stub(_ => TokenResponse())),
          Credential("GroupMember.ReadWrite.All") with { Enabled = false }),
      });
    var results = await verifier.VerifyCapabilitiesAsync(Tenant,
      [new(Microsoft365Capabilities.DirectoryRead, "User.Read.All"), new(Microsoft365Capabilities.GuestInvitation, "User.Invite.All"),
       new(Microsoft365Capabilities.GroupMembership, "GroupMember.ReadWrite.All"), new(Microsoft365Capabilities.TenantUserProvisioning, "User.Create")], default);
    Assert.Equal(CapabilityVerificationStates.Verified, results.Single(x => x.Capability == Microsoft365Capabilities.DirectoryRead).State);
    Assert.Equal(CapabilityVerificationStates.NotGranted, results.Single(x => x.Capability == Microsoft365Capabilities.GuestInvitation).State);
    Assert.Equal(CapabilityVerificationStates.BlockedExternal, results.Single(x => x.Capability == Microsoft365Capabilities.GroupMembership).State);
    Assert.Equal(CapabilityVerificationStates.BlockedExternal, results.Single(x => x.Capability == Microsoft365Capabilities.TenantUserProvisioning).State);
  }

  [Fact]
  public void TemporaryPassword_MeetsComplexityAndIsRandom()
  {
    var first = DirectoryProvisioningService.TemporaryPassword();
    var second = DirectoryProvisioningService.TemporaryPassword();
    Assert.NotEqual(first, second);
    Assert.Equal(16, first.Length);
    Assert.Contains(first, char.IsUpper);
    Assert.Contains(first, char.IsLower);
    Assert.Contains(first, char.IsDigit);
    Assert.Contains(first, c => !char.IsLetterOrDigit(c));
  }

  private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
      Task.FromResult(respond(request));
  }
}
