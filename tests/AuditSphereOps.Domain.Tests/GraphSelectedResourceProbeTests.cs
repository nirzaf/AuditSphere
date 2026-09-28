using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class GraphSelectedResourceProbeTests
{
  private const string SiteId = "synthetic.sharepoint.com,site-guid,web-guid";
  private const string SiteUrl = "https://synthetic.sharepoint.com/sites/audit";
  private const string TenantId = "synthetic-tenant";
  private const string TenantGuid = "23f6c523-c84b-4135-bdad-73c3dc9ae9ae";

  [Fact]
  public async Task ExactActiveSelectedResource_ProducesReadOnlyObservation()
  {
    await using var fixture = await Fixture.CreateAsync();
    var http = new StubGraphHandler();
    var probe = fixture.Probe(http, new SelectedSiteToken("test-token", TenantId,
      new HashSet<string> { "Sites.Selected" }));

    var observation = await probe.ProbeAsync(fixture.FirmId, fixture.WorkspaceId, CancellationToken.None);

    Assert.Equal(fixture.WorkspaceId, observation.WorkspaceId);
    Assert.Equal(SiteId, observation.SiteId);
    Assert.Equal(3, http.Paths.Count);
    Assert.All(http.Authenticated, Assert.True);
    Assert.Equal(0, http.PostCount);
  }

  [Fact]
  public async Task PendingDraft_CanBeProbedBeforeActivationWithoutChangingItsState()
  {
    await using var fixture = await Fixture.CreateAsync();
    var draftId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    await using (var db = new AuditSphereDbContext(fixture.Options))
    {
      var connection = await db.Microsoft365ConnectionRevisions.SingleAsync();
      connection.State = Microsoft365RevisionStates.ConsentRequired;
      connection.ConsentState = "REQUIRED";
      var sessionId = Guid.NewGuid();
      db.Microsoft365SetupSessions.Add(new Microsoft365SetupSession
      {
        Id = sessionId, FirmId = fixture.FirmId, InstallationId = "probe-test",
        BootstrapProofHash = new string('a', 64), CapabilityHash = new string('b', 64),
        ClaimedAt = now, ExpiresAt = now.AddHours(1)
      });
      db.Microsoft365SetupDrafts.Add(new Microsoft365SetupDraft
      {
        Id = draftId, FirmId = fixture.FirmId, SetupSessionId = sessionId,
        ConnectionRevisionId = connection.Id, State = Microsoft365RevisionStates.ConsentRequired,
        ExpectedTenantId = TenantId, SiteUrl = SiteUrl, SiteId = SiteId,
        DriveId = "drive-1", RootFolderId = "root-1", CreatedAt = now, UpdatedAt = now
      });
      await db.SaveChangesAsync();
    }

    var http = new StubGraphHandler();
    var probe = fixture.Probe(http, new SelectedSiteToken("test-token", TenantId,
      new HashSet<string> { "Sites.Selected" }));
    var adminId = Guid.NewGuid();
    var actor = new ActorContext(adminId, fixture.FirmId, 1, ["Administrator"]);
    var service = new Microsoft365SelectedResourceTestService(new TestFactory(fixture.Options), probe);
    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
      service.TestDraftAsync(actor, draftId, CancellationToken.None));
    Assert.Empty(http.Paths);
    await using (var db = new AuditSphereDbContext(fixture.Options))
    {
      db.Users.Add(new AppUser
      {
        Id = adminId, FirmId = fixture.FirmId, TenantId = TenantId, Subject = "synthetic-admin",
        Email = "admin@example.test", DisplayName = "Synthetic administrator", CreatedAt = now,
        SessionEpoch = 1
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = adminId, Role = "Administrator",
        GrantedAt = now, GrantedByUserId = adminId
      });
      await db.SaveChangesAsync();
    }
    await service.TestDraftAsync(actor, draftId, CancellationToken.None);
    Assert.Equal(3, http.Paths.Count);
    http.Paths.Clear();
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      probe.ProbeDraftAsync(Guid.NewGuid(), draftId, CancellationToken.None));
    Assert.Empty(http.Paths);
    var observation = await probe.ProbeDraftAsync(fixture.FirmId, draftId, CancellationToken.None);
    Assert.Equal(draftId, observation.DraftId);
    Assert.Equal(SiteId, observation.SiteId);
    Assert.Equal(3, http.Paths.Count);
    Assert.All(http.Authenticated, Assert.True);
    Assert.Equal(0, http.PostCount);
    await using var readback = new AuditSphereDbContext(fixture.Options);
    Assert.Equal(Microsoft365RevisionStates.ConsentRequired,
      (await readback.Microsoft365SetupDrafts.SingleAsync()).State);
    Assert.Empty(readback.IntegrationVerificationEvidences);

    const string negativeSite = "https://synthetic.sharepoint.com/sites/unrelated-control";
    http.Paths.Clear();
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      probe.ProbeDraftBoundaryAsync(fixture.FirmId, draftId, negativeSite, CancellationToken.None));
    Assert.Equal(4, http.Paths.Count); // An accessible control fails closed.
    http.Paths.Clear();
    http.DenyNegativeControl = true;
    var boundaryService = new Microsoft365SelectedResourceTestService(
      new TestFactory(fixture.Options), probe, negativeSite);
    await boundaryService.TestDraftBoundaryAsync(actor, draftId, CancellationToken.None);
    Assert.Equal(4, http.Paths.Count);
    Assert.All(http.Authenticated, Assert.True);
    Assert.Equal(0, http.PostCount);
    Assert.Empty(readback.IntegrationVerificationEvidences);
    http.Paths.Clear();
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      probe.ProbeDraftBoundaryAsync(fixture.FirmId, draftId, SiteUrl, CancellationToken.None));
    Assert.Equal(3, http.Paths.Count); // The selected site cannot be its own negative control.

    var pending = await readback.Microsoft365ConnectionRevisions.SingleAsync();
    pending.State = Microsoft365RevisionStates.Suspended;
    await readback.SaveChangesAsync();
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      probe.ProbeDraftAsync(fixture.FirmId, draftId, CancellationToken.None));
    Assert.Equal(3, http.Paths.Count);
  }

  [Theory]
  [InlineData("wrong-site")]
  [InlineData("wrong-drive")]
  [InlineData("wrong-root")]
  public async Task MismatchedGraphResource_FailsClosed(string mismatch)
  {
    await using var fixture = await Fixture.CreateAsync();
    var http = new StubGraphHandler(mismatch);
    var probe = fixture.Probe(http, new SelectedSiteToken("test-token", TenantId,
      new HashSet<string> { "Sites.Selected" }));

    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      probe.ProbeAsync(fixture.FirmId, fixture.WorkspaceId, CancellationToken.None));
    Assert.Equal(0, http.PostCount);
  }

  [Fact]
  public async Task WrongTenantOrBroadRole_NeverContactsGraph()
  {
    await using var fixture = await Fixture.CreateAsync();
    foreach (var token in new[]
    {
      new SelectedSiteToken("test-token", "other-tenant", new HashSet<string> { "Sites.Selected" }),
      new SelectedSiteToken("test-token", TenantId, new HashSet<string> { "Sites.Selected", "Sites.ReadWrite.All" })
    })
    {
      var http = new StubGraphHandler();
      await Assert.ThrowsAsync<OperationBlockedException>(() =>
        fixture.Probe(http, token).ProbeAsync(fixture.FirmId, fixture.WorkspaceId, CancellationToken.None));
      Assert.Empty(http.Paths);
    }
  }

  [Fact]
  public async Task InactiveOrCrossFirmWorkspace_NeverRequestsToken()
  {
    await using var fixture = await Fixture.CreateAsync();
    var source = new StubTokenSource(new SelectedSiteToken("test-token", TenantId,
      new HashSet<string> { "Sites.Selected" }));
    var http = new StubGraphHandler();
    var probe = new GraphSelectedResourceProbe(new TestFactory(fixture.Options), new HttpClient(http), source);
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      probe.ProbeAsync(Guid.NewGuid(), fixture.WorkspaceId, CancellationToken.None));
    await using (var db = new AuditSphereDbContext(fixture.Options))
    {
      var connection = await db.Microsoft365ConnectionRevisions.SingleAsync();
      connection.State = Microsoft365RevisionStates.Suspended;
      await db.SaveChangesAsync();
    }
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      probe.ProbeAsync(fixture.FirmId, fixture.WorkspaceId, CancellationToken.None));
    Assert.Equal(0, source.Calls);
    Assert.Empty(http.Paths);
  }

  [Fact]
  public async Task CertificateTokenSource_BindsTenantCredentialAndSelectedRole()
  {
    var directory = Path.Combine(Path.GetTempPath(), "AuditSphereGraphProbeTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var certificatePath = Path.Combine(directory, "public.pem");
    var keyPath = Path.Combine(directory, "private.pem");
    try
    {
      using var key = RSA.Create(2048);
      var request = new CertificateRequest("CN=synthetic-selected-site", key,
        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
      await File.WriteAllTextAsync(certificatePath, certificate.ExportCertificatePem());
      await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem());
      var handler = new StubTokenHandler();
      var source = new CertificateSelectedSiteTokenSource(new HttpClient(handler),
        new(TenantGuid, Guid.NewGuid().ToString("D"), "slot:runtime", certificatePath, keyPath));

      var token = await source.GetAsync(TenantGuid, "slot:runtime", CancellationToken.None);
      Assert.Equal(TenantGuid, token.TenantId);
      Assert.Equal(["Sites.Selected"], token.ApplicationRoles);
      Assert.Equal(1, handler.Calls);
      await Assert.ThrowsAsync<OperationBlockedException>(() =>
        source.GetAsync("other-tenant", "slot:runtime", CancellationToken.None));
      Assert.Equal(1, handler.Calls);
      handler.BroadRole = true;
      await Assert.ThrowsAsync<OperationBlockedException>(() =>
        source.GetAsync(TenantGuid, "slot:runtime", CancellationToken.None));
    }
    finally { Directory.Delete(directory, recursive: true); }
  }

  private sealed class Fixture : IAsyncDisposable
  {
    private readonly PgTestSchema _pg;
    public DbContextOptions<AuditSphereDbContext> Options => _pg.Options;
    public Guid FirmId { get; } = Guid.NewGuid();
    public Guid WorkspaceId { get; } = Guid.NewGuid();

    private Fixture(PgTestSchema pg) => _pg = pg;

    public static async Task<Fixture> CreateAsync()
    {
      var fixture = new Fixture(await PgTestSchema.CreateAsync());
      var connectionId = Guid.NewGuid();
      var templateId = Guid.NewGuid();
      await using var db = new AuditSphereDbContext(fixture.Options);
      db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
      {
        Id = connectionId, FirmId = fixture.FirmId, TenantId = TenantId,
        LoginClientIdReference = "slot:login", RuntimeCredentialReference = "slot:runtime",
        State = Microsoft365RevisionStates.Active, ConsentState = "OBSERVED",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.FolderTemplateVersions.Add(new FolderTemplateVersion
      {
        Id = templateId, FirmId = fixture.FirmId, Purpose = FolderTemplatePurposes.ClientWorkspace,
        ManifestJson = "{}", ManifestDigest = new string('a', 64), CreatedAt = DateTimeOffset.UtcNow,
        ApprovedAt = DateTimeOffset.UtcNow
      });
      db.FirmWorkspaceConfigurations.Add(new FirmWorkspaceConfiguration
      {
        Id = fixture.WorkspaceId, FirmId = fixture.FirmId, ConnectionRevisionId = connectionId,
        FolderTemplateVersionId = templateId, TenantId = TenantId, SiteId = SiteId,
        DriveId = "drive-1", RootFolderId = "root-1", DisplayUrl = SiteUrl,
        AccessProfile = Microsoft365AccessProfiles.AppMediated, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      return fixture;
    }

    public GraphSelectedResourceProbe Probe(StubGraphHandler http, SelectedSiteToken token) =>
      new(new TestFactory(Options), new HttpClient(http), new StubTokenSource(token));

    public ValueTask DisposeAsync() => _pg.DisposeAsync();
  }

  private sealed class TestFactory(DbContextOptions<AuditSphereDbContext> options) : IAuditSphereDbContextFactory
  {
    public Task<IAuditSphereDbContext> CreateAsync(CancellationToken ct = default) =>
      Task.FromResult<IAuditSphereDbContext>(new AuditSphereDbContext(options));
  }

  private sealed class StubTokenSource(SelectedSiteToken token) : ISelectedSiteTokenSource
  {
    public int Calls { get; private set; }
    public Task<SelectedSiteToken> GetAsync(string tenantId, string credentialReference, CancellationToken ct)
    {
      Calls++;
      Assert.Equal(TenantId, tenantId);
      Assert.Equal("slot:runtime", credentialReference);
      return Task.FromResult(token);
    }
  }

  private sealed class StubGraphHandler(string? mismatch = null) : HttpMessageHandler
  {
    public List<string> Paths { get; } = [];
    public List<bool> Authenticated { get; } = [];
    public int PostCount { get; private set; }
    public bool DenyNegativeControl { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      var path = request.RequestUri!.AbsolutePath;
      Paths.Add(path);
      Authenticated.Add(request.Headers.Authorization?.Scheme == "Bearer" &&
        request.Headers.Authorization.Parameter == "test-token");
      if (request.Method != HttpMethod.Get) PostCount++;
      if (DenyNegativeControl && path.Contains("unrelated-control", StringComparison.Ordinal))
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
      var body = path switch
      {
        var p when p.StartsWith("/v1.0/sites/", StringComparison.Ordinal) && p.EndsWith("/drives", StringComparison.Ordinal) =>
          "{\"value\":[{\"id\":\"" + (mismatch == "wrong-drive" ? "other-drive" : "drive-1") + "\"}]}",
        var p when p.StartsWith("/v1.0/sites/", StringComparison.Ordinal) =>
          "{\"id\":\"" + (mismatch == "wrong-site" ? "other-site" : SiteId) + "\",\"webUrl\":\"" + SiteUrl + "\"}",
        _ => "{\"id\":\"" + (mismatch == "wrong-root" ? "other-root" : "root-1") +
          "\",\"folder\":{},\"parentReference\":{\"driveId\":\"drive-1\"},\"webUrl\":\"" + SiteUrl + "/Shared%20Documents/root\"}"
      };
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
      });
    }
  }

  private sealed class StubTokenHandler : HttpMessageHandler
  {
    public int Calls { get; private set; }
    public bool BroadRole { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Calls++;
      Assert.Equal(HttpMethod.Post, request.Method);
      Assert.Equal("login.microsoftonline.com", request.RequestUri!.Host);
      Assert.Contains(TenantGuid, request.RequestUri.AbsolutePath, StringComparison.Ordinal);
      var form = await request.Content!.ReadAsStringAsync(ct);
      Assert.Contains("client_assertion=", form, StringComparison.Ordinal);
      var claims = JsonSerializer.SerializeToUtf8Bytes(new
      {
        tid = TenantGuid, aud = "https://graph.microsoft.com",
        exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
        roles = BroadRole ? new[] { "Sites.Selected", "Sites.ReadWrite.All" } : ["Sites.Selected"]
      });
      var encoded = Convert.ToBase64String(claims).TrimEnd('=').Replace('+', '-').Replace('/', '_');
      var token = "header." + encoded + ".signature";
      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(JsonSerializer.Serialize(new { access_token = token }),
          Encoding.UTF8, "application/json")
      };
    }
  }
}
