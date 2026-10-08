using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

public sealed class SharePointClientSiteProviderTests : IDisposable
{
  private const string Tenant = "11111111-1111-1111-1111-111111111111";
  private const string Host = "example.sharepoint.com";
  private readonly string directory = Path.Combine(Path.GetTempPath(), "as-client-sites-" + Guid.NewGuid().ToString("N"));
  private readonly ClientSiteProviderOptions options;
  private readonly string readerId = Guid.NewGuid().ToString("D");
  public SharePointClientSiteProviderTests()
  {
    Directory.CreateDirectory(directory);
    using var key = RSA.Create(2048);
    using var cert = new CertificateRequest("CN=AuditSphere fake provider", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
      .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    var certificatePath = Path.Combine(directory, "cert.pem"); var keyPath = Path.Combine(directory, "key.pem");
    File.WriteAllText(certificatePath, cert.ExportCertificatePem()); File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
    options = new(Tenant, Guid.NewGuid().ToString("D"), Host, certificatePath, keyPath, Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
  }
  public void Dispose() => Directory.Delete(directory, true);
  private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
  private static string Jwt(object body) => "header." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(body)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";
  private sealed class TokenHandler(string reader, bool extraRole = false) : HttpMessageHandler
  {
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      var form = await request.Content!.ReadAsStringAsync(ct);
      var isReader = form.Contains(reader, StringComparison.Ordinal);
      var sharePoint = form.Contains(Uri.EscapeDataString($"https://{Host}/.default"), StringComparison.OrdinalIgnoreCase);
      var role = isReader ? "User.Read.All" : "Sites.FullControl.All";
      return Json(new { access_token = Jwt(new { tid = Tenant, aud = sharePoint ? "00000003-0000-0ff1-ce00-000000000000" : "https://graph.microsoft.com",
        exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), roles = extraRole && !isReader ? new[] { role, "User.ReadWrite.All" } : new[] { role } }) });
    }
  }
  private sealed class Remote(ClientSiteProviderOptions options) : HttpMessageHandler
  {
    public int CreateCount;
    public string? Marker;
    public bool GroupExists, GroupRole, GrantExists, DisabledStaff, WrongObjectId, LostCreate, Throttle, WrongPage;
    public bool TooBroadWorkerGrant;
    public readonly Dictionary<int, (string Login, string Oid)> Members = [];
    private readonly Dictionary<string, (int Id, string Oid)> principals = [];
    private int nextId = 10;
    private string siteUrl = "";
    private readonly string siteId = Guid.NewGuid().ToString("D"), webId = Guid.NewGuid().ToString("D");
    public readonly List<string> Bodies = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
      var url = message.RequestUri!.AbsoluteUri;
      Assert.NotNull(message.Headers.Authorization);
      var body = message.Content == null ? null : await message.Content.ReadAsStringAsync(ct);
      if (body != null) Bodies.Add(body);
      if (Throttle) { Throttle = false; return Json(new { }, HttpStatusCode.TooManyRequests); }
      if (url.Contains("SPSiteManager/status", StringComparison.Ordinal)) return Json(new { SiteStatus = Marker == null ? 0 : 2 });
      if (url.Contains("SPSiteManager/create", StringComparison.Ordinal))
      {
        using var json = JsonDocument.Parse(body!); var request = json.RootElement.GetProperty("request");
        Assert.Equal("STS#3", request.GetProperty("WebTemplate").GetString());
        Assert.False(request.GetProperty("ShareByEmailEnabled").GetBoolean());
        Marker = request.GetProperty("Description").GetString(); siteUrl = request.GetProperty("Url").GetString()!; CreateCount++;
        if (LostCreate) { LostCreate = false; throw new HttpRequestException("accepted response lost"); }
        return Json(new { SiteStatus = 2 });
      }
      if (url.Contains("/v1.0/users/", StringComparison.Ordinal))
      {
        var oid = message.RequestUri.AbsolutePath.Split('/').Last();
        return Json(new { id = oid, userPrincipalName = oid + "@example.test", accountEnabled = !DisabledStaff || oid == options.CustodianObjectId, userType = "Member" });
      }
      if (url.Contains("/permissions", StringComparison.Ordinal))
      {
        if (message.Method == HttpMethod.Post)
        {
          using var json = JsonDocument.Parse(body!);
          Assert.Equal(options.DocumentWorkerClientId, json.RootElement.GetProperty("grantedToIdentities")[0].GetProperty("application").GetProperty("id").GetString());
          Assert.Equal("write", json.RootElement.GetProperty("roles")[0].GetString()); GrantExists = true;
          return Json(new { id = "worker-grant" });
        }
        if (WrongPage) return Json(new Dictionary<string, object> { ["value"] = Array.Empty<object>(), ["@odata.nextLink"] = "https://evil.example/steal" });
        return Json(new { value = GrantExists ? new[] { new { roles = new[] { TooBroadWorkerGrant ? "fullcontrol" : "write" },
          grantedToIdentitiesV2 = new[] { new { application = new { id = options.DocumentWorkerClientId } } } } } : [] });
      }
      if (url.Contains("/drive?", StringComparison.Ordinal)) return Json(new { id = "drive-client" });
      if (url.Contains("/root?", StringComparison.Ordinal)) return Json(new { id = "root-client" });
      if (message.RequestUri.Host == "graph.microsoft.com" && url.Contains("/sites/", StringComparison.Ordinal)) return Json(new { id = $"example.sharepoint.com,{siteId},{webId}", webUrl = siteUrl });
      if (url.Contains("/_api/web?", StringComparison.Ordinal)) return Json(new { Id = webId, Url = siteUrl, Description = Marker });
      if (url.Contains("/sitegroups/getbyname", StringComparison.Ordinal)) return GroupExists ? Json(new { Id = 7, Description = Marker }) : Json(new { }, HttpStatusCode.NotFound);
      if (url.EndsWith("/sitegroups", StringComparison.Ordinal)) { GroupExists = true; return Json(new { Id = 7 }); }
      if (url.Contains("roledefinitions/getbytype", StringComparison.Ordinal)) return Json(new { Id = 1073741829, RoleTypeKind = 5 });
      if (url.Contains("/roledefinitionbindings", StringComparison.Ordinal)) return Json(new { value = GroupRole ? new[] { new { Id = 1073741829 } } : [] });
      if (url.Contains("addroleassignment", StringComparison.Ordinal)) { GroupRole = true; return Json(new { }); }
      if (url.Contains("/ensureuser", StringComparison.Ordinal))
      {
        using var json = JsonDocument.Parse(body!); var login = json.RootElement.GetProperty("logonName").GetString()!;
        var oid = login.Split('|').Last().Split('@')[0];
        if (!principals.ContainsKey(login)) principals[login] = (nextId++, WrongObjectId ? Guid.NewGuid().ToString("D") : oid);
        return Json(new { Id = principals[login].Id, LoginName = login });
      }
      if (url.Contains("/siteusers/getbyid", StringComparison.Ordinal))
      {
        var id = int.Parse(message.RequestUri.AbsolutePath.Split('(').Last().TrimEnd(')'));
        var principal = principals.Single(x => x.Value.Id == id);
        return Json(new { Id = id, LoginName = principal.Key, AadObjectId = new { NameId = principal.Value.Oid } });
      }
      if (url.Contains("sitegroups(7)/users", StringComparison.Ordinal))
      {
        if (url.Contains("removebyid", StringComparison.Ordinal))
        { var id = int.Parse(message.RequestUri.AbsolutePath.Split('(').Last().TrimEnd(')')); Members.Remove(id); return Json(new { }); }
        if (message.Method == HttpMethod.Post)
        { using var json = JsonDocument.Parse(body!); var login = json.RootElement.GetProperty("LoginName").GetString()!;
          Members[principals[login].Id] = (login, principals[login].Oid); return Json(new { }); }
        return Json(new { value = Members.Select(x => new { Id = x.Key, LoginName = x.Value.Login }).ToArray() });
      }
      throw new InvalidOperationException("Unmapped fake endpoint: " + message.RequestUri.AbsolutePath);
    }
  }
  private SharePointClientSiteProvider Provider(Remote remote, bool extraRole = false)
  {
    var tokens = new HttpClient(new TokenHandler(readerId, extraRole));
    return new(new HttpClient(remote), tokens, options,
      new GraphCapabilityTokenSource(tokens, new(true, Tenant, readerId, options.CertificatePath, options.PrivateKeyPath, "User.Read.All")));
  }
  private static ClientSiteRequest Request()
  {
    var firm = Guid.NewGuid(); var client = Guid.NewGuid();
    return new(firm, client, Tenant, $"https://{Host}/sites/{ClientSharePointSites.SiteSlug("Example Client", client)}", "Example Client",
      $"AuditSphere:{firm:D}:{client:D}", null, [Guid.NewGuid().ToString("D")]);
  }

  [Fact]
  public async Task SupportedSiteCreation_VerifiesSelectedWorkerWriteAndFullControl_RemovesRevokedMembers()
  {
    var remote = new Remote(options); var provider = Provider(remote); var request = Request();
    var receipt = await provider.ReconcileAsync(request, true, default);
    Assert.Equal(request.StaffObjectIds, receipt.MemberObjectIds);
    Assert.True(remote.GroupRole); Assert.True(remote.GrantExists); Assert.Equal(1, remote.CreateCount);
    var again = await provider.ReconcileAsync(request with { KnownSiteId = receipt.SiteId }, false, default);
    Assert.Equal(receipt.SiteId, again.SiteId); Assert.Equal(1, remote.CreateCount);
    var revoked = await provider.ReconcileAsync(request with { StaffObjectIds = [], KnownSiteId = receipt.SiteId }, false, default);
    Assert.Empty(revoked.MemberObjectIds); Assert.Empty(remote.Members);
    Assert.DoesNotContain(remote.Bodies, b => b.Contains("password", StringComparison.OrdinalIgnoreCase) || b.Contains("access_token", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task LostCreateResponse_ReconcilesExistingSite_AbsentUnknownNeverCreates()
  {
    var remote = new Remote(options) { LostCreate = true }; var provider = Provider(remote); var request = Request();
    await Assert.ThrowsAsync<HttpRequestException>(() => provider.ReconcileAsync(request, true, default));
    await provider.ReconcileAsync(request, false, default);
    Assert.Equal(1, remote.CreateCount);
    var absent = new Remote(options);
    await Assert.ThrowsAsync<OperationBlockedException>(() => Provider(absent).ReconcileAsync(Request(), false, default));
    Assert.Equal(0, absent.CreateCount);
  }

  [Theory]
  [InlineData("wrong-tenant")]
  [InlineData("wrong-host")]
  [InlineData("wrong-client")]
  [InlineData("extra-role")]
  [InlineData("wrong-object-id")]
  [InlineData("worker-too-broad")]
  [InlineData("malicious-page")]
  public async Task ScopeAndPermissionFailures_FailClosed(string failure)
  {
    var request = Request(); var remote = new Remote(options);
    if (failure == "wrong-tenant") request = request with { TenantId = Guid.NewGuid().ToString("D") };
    if (failure == "wrong-host") request = request with { Url = request.Url.Replace(Host, "evil.example", StringComparison.Ordinal) };
    if (failure == "wrong-client") request = request with { ClientId = Guid.NewGuid() };
    if (failure == "wrong-object-id") remote.WrongObjectId = true;
    if (failure == "worker-too-broad") { remote.GrantExists = true; remote.TooBroadWorkerGrant = true; }
    if (failure == "malicious-page") remote.WrongPage = true;
    var ex = await Record.ExceptionAsync(() => Provider(remote, failure == "extra-role").ReconcileAsync(request, true, default));
    Assert.True(ex is OperationBlockedException or GraphCapabilityTokenException or ClientSitePreflightException, ex?.ToString());
    Assert.Empty(remote.Members);
  }

  [Fact]
  public async Task DisabledDirectoryIdentity_IsNotAdded_ThrottlingDoesNotCreateSite()
  {
    var remote = new Remote(options) { DisabledStaff = true };
    var receipt = await Provider(remote).ReconcileAsync(Request(), true, default);
    Assert.Empty(receipt.MemberObjectIds);
    var throttled = new Remote(options) { Throttle = true };
    Assert.True((await Assert.ThrowsAsync<ClientSitePreflightException>(() => Provider(throttled).ReconcileAsync(Request(), true, default))).Retryable);
    Assert.Equal(0, throttled.CreateCount);
  }
}
