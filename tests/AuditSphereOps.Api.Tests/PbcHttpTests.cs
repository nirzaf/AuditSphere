using System.Net;
using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

[Trait("Category", "ClientDocuments")]
public sealed class PbcHttpTests
{
  [Fact]
  [Trait("CaseId", "PROP-API-01")]
  public async Task UnauthenticatedAndForgedOriginRequestsHaveNoTransferEffect()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PROP-API-01");
    var fixture = await PbcSeed.SeedAsync(pg);
    var clientActor = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture,
      PbcSeed.Actor(fixture.Staff, "Staff"), clientActor);
    using var ownedRoot = new OwnedApiRoot();
    var root = ownedRoot.Path;
    using var factory = CreateFactory(pg.ConnectionString, fixture.Client, root);
    using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
    var uploadId = Guid.NewGuid();
    using var anonymousChunk = await anonymous.PostAsync(
      $"/api/pbc/uploads/{uploadId:D}/chunks/0", new ByteArrayContent("fixture"u8.ToArray()));
    using var anonymousDownload = await anonymous.GetAsync($"/api/pbc/uploads/{uploadId:D}/download");
    Assert.Equal(HttpStatusCode.Unauthorized, anonymousChunk.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, anonymousDownload.StatusCode);
    Assert.DoesNotContain("text/html", anonymousChunk.Content.Headers.ContentType?.MediaType ?? "");

    var started = await StartUploadAsync(pg, fixture, clientActor, requestId, "origin.txt"u8.ToArray());
    using var signedIn = await SignInAsync(factory, fixture.Client);
    using var missing = ChunkRequest(started.UploadIntentId, 0, "origin.txt"u8.ToArray(), started.Capability,
      origin: null);
    using var missingResponse = await signedIn.SendAsync(missing);
    Assert.Equal(HttpStatusCode.Forbidden, missingResponse.StatusCode);

    using var forged = ChunkRequest(started.UploadIntentId, 0, "origin.txt"u8.ToArray(), started.Capability,
      origin: "https://attacker.invalid");
    using var forgedResponse = await signedIn.SendAsync(forged);
    Assert.Equal(HttpStatusCode.Forbidden, forgedResponse.StatusCode);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await verify.PbcUploadChunks.AsNoTracking().ToListAsync());
    Assert.False(Directory.Exists(root));
  }

  [Fact]
  [Trait("CaseId", "PROP-API-02")]
  public async Task InvalidAndOverLimitChunksFailWithoutAcceptedReceipts()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PROP-API-02");
    var fixture = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture,
      PbcSeed.Actor(fixture.Staff, "Staff"), actor);
    using var ownedRoot = new OwnedApiRoot();
    var root = ownedRoot.Path;
    using var factory = CreateFactory(pg.ConnectionString, fixture.Client, root);
    using var client = await SignInAsync(factory, fixture.Client);
    var bytes = "bounded upload test"u8.ToArray();
    var upload = await StartUploadAsync(pg, fixture, actor, requestId, bytes);

    using var invalidIndex = ChunkRequest(upload.UploadIntentId, -1, bytes, upload.Capability);
    using var invalidIndexResponse = await client.SendAsync(invalidIndex);
    Assert.Equal(HttpStatusCode.BadRequest, invalidIndexResponse.StatusCode);

    using var invalidOffset = ChunkRequest(upload.UploadIntentId, 0, bytes, upload.Capability, offset: 1);
    using var invalidOffsetResponse = await client.SendAsync(invalidOffset);
    Assert.Equal(HttpStatusCode.Conflict, invalidOffsetResponse.StatusCode);

    using var invalidHash = ChunkRequest(upload.UploadIntentId, 0, bytes, upload.Capability,
      hash: new string('0', 64));
    using var invalidHashResponse = await client.SendAsync(invalidHash);
    Assert.Equal(HttpStatusCode.BadRequest, invalidHashResponse.StatusCode);

    using var invalidCapability = ChunkRequest(upload.UploadIntentId, 0, bytes, "expired-capability");
    using var invalidCapabilityResponse = await client.SendAsync(invalidCapability);
    Assert.Equal(HttpStatusCode.Forbidden, invalidCapabilityResponse.StatusCode);

    var declaredOversize = new byte[PbcService.MaxChunkBytes + 1];
    using var declared = ChunkRequest(upload.UploadIntentId, 0, declaredOversize, upload.Capability);
    using var declaredResponse = await client.SendAsync(declared);
    Assert.Equal(HttpStatusCode.RequestEntityTooLarge, declaredResponse.StatusCode);

    using var streamed = new HttpRequestMessage(HttpMethod.Post,
      $"/api/pbc/uploads/{upload.UploadIntentId:D}/chunks/0");
    streamed.Headers.TryAddWithoutValidation("Origin", "http://localhost");
    streamed.Headers.TryAddWithoutValidation("X-Upload-Offset", "0");
    streamed.Headers.TryAddWithoutValidation("X-Content-SHA256", Convert.ToHexString(SHA256.HashData(declaredOversize)).ToLowerInvariant());
    streamed.Headers.TryAddWithoutValidation("X-Pbc-Upload-Capability", upload.Capability);
    streamed.Content = new UnknownLengthContent(declaredOversize);
    using var streamedResponse = await client.SendAsync(streamed);
    Assert.Equal(HttpStatusCode.RequestEntityTooLarge, streamedResponse.StatusCode);

    using var cancelled = new HttpRequestMessage(HttpMethod.Post,
      $"/api/pbc/uploads/{upload.UploadIntentId:D}/chunks/0");
    cancelled.Headers.TryAddWithoutValidation("Origin", "http://localhost");
    cancelled.Headers.TryAddWithoutValidation("X-Upload-Offset", "0");
    cancelled.Headers.TryAddWithoutValidation("X-Content-SHA256", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    cancelled.Headers.TryAddWithoutValidation("X-Pbc-Upload-Capability", upload.Capability);
    cancelled.Content = new CancellationContent(bytes);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(cancelled, cancellation.Token));

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await verify.PbcUploadChunks.AsNoTracking().ToListAsync());
    Assert.Empty(Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : []);
  }

  [Fact]
  [Trait("CaseId", "PROP-API-03")]
  public async Task DuplicateChunkIsIdempotentChangedBytesConflictAndUnusedStagingIsRemoved()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PROP-API-03");
    var fixture = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture,
      PbcSeed.Actor(fixture.Staff, "Staff"), actor);
    using var ownedRoot = new OwnedApiRoot();
    var root = ownedRoot.Path;
    using var factory = CreateFactory(pg.ConnectionString, fixture.Client, root);
    using var client = await SignInAsync(factory, fixture.Client);
    var bytes = "synthetic duplicate chunk"u8.ToArray();
    var upload = await StartUploadAsync(pg, fixture, actor, requestId, bytes);

    using var first = await client.SendAsync(ChunkRequest(upload.UploadIntentId, 0, bytes, upload.Capability));
    using var duplicate = await client.SendAsync(ChunkRequest(upload.UploadIntentId, 0, bytes, upload.Capability));
    using var changed = await client.SendAsync(ChunkRequest(upload.UploadIntentId, 0,
      "changed chunk"u8.ToArray(), upload.Capability));
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Single(await verify.PbcUploadChunks.AsNoTracking().ToListAsync());
    Assert.Single(Directory.EnumerateFiles(Path.Combine(root, fixture.FirmId.ToString("N"),
      upload.UploadIntentId.ToString("N"))));
  }

  [Fact]
  [Trait("CaseId", "PROP-API-04")]
  public async Task DownloadIsScopedAndReturnsExactBytesWithSafeHeaders()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PROP-API-04");
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var clientActor = PbcSeed.Actor(fixture.Client, "ClientUser");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, clientActor);
    using var ownedRoot = new OwnedApiRoot();
    var root = ownedRoot.Path;
    var bytes = "%PDF-1.7 exact API download"u8.ToArray();
    var upload = await StartUploadAsync(pg, fixture, clientActor, requestId, bytes);
    using var clientFactory = CreateFactory(pg.ConnectionString, fixture.Client, root);
    using var uploadClient = await SignInAsync(clientFactory, fixture.Client);
    var expiredBytes = "synthetic expired upload"u8.ToArray();
    var expired = await StartUploadAsync(pg, fixture, clientActor, requestId, expiredBytes);
    await using (var expire = new AuditSphereDbContext(pg.Options))
      await expire.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE pbc_upload_intents
        SET created_at = statement_timestamp() - interval '2 days',
            expires_at = statement_timestamp() - interval '1 day'
        WHERE firm_id = {fixture.FirmId} AND id = {expired.UploadIntentId}
        """);
    using (var expiredRequest = ChunkRequest(expired.UploadIntentId, 0, expiredBytes, expired.Capability))
    using (var expiredResponse = await uploadClient.SendAsync(expiredRequest))
      Assert.Equal(HttpStatusCode.BadRequest, expiredResponse.StatusCode);
    await using (var verifyExpired = new AuditSphereDbContext(pg.Options))
    {
      Assert.Empty(await verifyExpired.PbcUploadChunks.AsNoTracking()
        .Where(x => x.PbcUploadIntentId == expired.UploadIntentId).ToListAsync());
      Assert.Empty(Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : []);
    }
    using var chunk = await uploadClient.SendAsync(ChunkRequest(upload.UploadIntentId, 0, bytes, upload.Capability));
    Assert.Equal(HttpStatusCode.OK, chunk.StatusCode);
    var contextFactory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var transferHandler = new PbcDocumentTransferHandler(contextFactory,
      new SimulationPbcProviderSink(Path.Combine(root, "provider")));
    var operationStore = new PostgresOperationStore(contextFactory);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var completed = await PbcService.CompleteUploadAsync(db, staff,
        new CompletePbcUploadRequest(upload.UploadIntentId,
          Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()), operationStore, transferHandler);
      Assert.True(completed.Succeeded, completed.ErrorCode);
    }
    var options = new WorkerOptions(fixture.FirmId, "Test", AllowSimulationAdapters: true);
    var discovery = new PbcTransferDiscovery(contextFactory, operationStore,
      transferHandler, options);
    var workerHost = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(operationStore, new DurableOperationRegistry([transferHandler], options), options),
      [discovery], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    Assert.True(await workerHost.ProcessNextAsync());

    using var factory = CreateFactory(pg.ConnectionString, fixture.Staff, root);
    using var client = await SignInAsync(factory, fixture.Staff);
    using var response = await client.GetAsync($"/api/pbc/uploads/{upload.UploadIntentId:D}/download");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? "");
    Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());

    var unrelatedClient = PbcSeed.User(fixture.FirmId, "Client");
    var unrelatedClientId = Guid.NewGuid();
    var unrelatedEngagementId = Guid.NewGuid();
    await using (var seedOther = new AuditSphereDbContext(pg.Options))
    {
      seedOther.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = fixture.FirmId, LegalName = "UNRELATED SYNTHETIC CLIENT",
        CreatedAt = DateTimeOffset.UtcNow
      });
      seedOther.Engagements.Add(new Engagement
      {
        Id = unrelatedEngagementId, FirmId = fixture.FirmId, PracticeClientId = unrelatedClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      seedOther.ClientSafetyStates.Add(new ClientSafetyState { Id = unrelatedClientId, FirmId = fixture.FirmId });
      seedOther.Users.Add(unrelatedClient);
      seedOther.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, unrelatedClient, "ClientUser",
        unrelatedClientId, unrelatedEngagementId));
      await seedOther.SaveChangesAsync();
    }
    using var wrongScopeFactory = CreateFactory(pg.ConnectionString, unrelatedClient, root);
    using var wrongScope = await SignInAsync(wrongScopeFactory, unrelatedClient);
    using var sibling = await wrongScope.GetAsync($"/api/pbc/uploads/{upload.UploadIntentId:D}/download");
    Assert.Equal(HttpStatusCode.Forbidden, sibling.StatusCode);

    var outsidePath = Path.Combine(Path.GetTempPath(), "AuditSphereOps-api-escape-" + Guid.NewGuid().ToString("N"));
    try
    {
      var traversalBytes = "synthetic traversal boundary"u8.ToArray();
      var traversalRequestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, staff, clientActor);
      var traversalStart = await StartUploadAsync(pg, fixture, clientActor, traversalRequestId, traversalBytes);
      await File.WriteAllBytesAsync(outsidePath, traversalBytes);
      await using (var maliciousBoundary = new AuditSphereDbContext(pg.Options))
      {
        var staged = await PbcService.RecordChunkAsync(maliciousBoundary, clientActor,
          new RecordPbcUploadChunkRequest(traversalStart.UploadIntentId, 0, 0, traversalBytes.Length,
            Convert.ToHexString(SHA256.HashData(traversalBytes)).ToLowerInvariant(),
            traversalStart.Capability, outsidePath));
        Assert.True(staged.Succeeded, staged.ErrorCode);
      }
      await using (var complete = new AuditSphereDbContext(pg.Options))
      {
        var staged = await PbcService.CompleteUploadAsync(complete, staff,
          new CompletePbcUploadRequest(traversalStart.UploadIntentId,
            Convert.ToHexString(SHA256.HashData(traversalBytes)).ToLowerInvariant()), operationStore, transferHandler);
        Assert.True(staged.Succeeded, staged.ErrorCode);
      }
      Assert.True(await workerHost.ProcessNextAsync());
      using var traversal = await client.GetAsync($"/api/pbc/uploads/{traversalStart.UploadIntentId:D}/download");
      Assert.Equal(HttpStatusCode.Forbidden, traversal.StatusCode);
    }
    finally
    {
      try { File.Delete(outsidePath); } catch (IOException) { }
    }
  }

  [Fact]
  [Trait("Category", "HealthAndConfiguration")]
  [Trait("CaseId", "PROP-API-05")]
  public async Task LivenessStaysHealthyWhenDatabaseIsAbsentAndStartupDoesNotCreateIt()
  {
    var missingDatabase = "auditsphere_api_missing_" + Guid.NewGuid().ToString("N")[..12];
    var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_CONNECTION") ??
      "Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres")
    {
      Database = "postgres",
      Pooling = false
    };
    var settings = new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
      {
        Database = missingDatabase,
        Pooling = false
      }.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "false",
      ["Application:AllowSimulationAdapters"] = "false",
      ["ExternalEffects:Enabled"] = "false"
    };
    using var factory = new ApiWebApplicationFactory(settings);
    using var client = factory.CreateClient();
    using var live = await client.GetAsync("/health/live");
    using var ready = await client.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    await using var connection = new NpgsqlConnection(admin.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", connection);
    command.Parameters.AddWithValue("name", missingDatabase);
    Assert.False((bool)(await command.ExecuteScalarAsync())!);

    await using var oldSchema = await OwnedPostgresDatabase.CreateAsync("PROP-API-05", "20260917073959_InitialCreate");
    using var pendingFactory = new ApiWebApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = oldSchema.ConnectionString,
      ["Application:AllowSimulationAdapters"] = "false",
      ["ExternalEffects:Enabled"] = "false",
      ["DevelopmentIdentity:Enabled"] = "false"
    });
    using var pendingClient = pendingFactory.CreateClient();
    using var pendingLive = await pendingClient.GetAsync("/health/live");
    using var pendingReady = await pendingClient.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.OK, pendingLive.StatusCode);
    Assert.Equal(HttpStatusCode.ServiceUnavailable, pendingReady.StatusCode);
    await using var verifyPending = new AuditSphereDbContext(oldSchema.Options);
    Assert.Equal(["20260917073959_InitialCreate"], await verifyPending.Database.GetAppliedMigrationsAsync());
    Assert.NotEmpty(await verifyPending.Database.GetPendingMigrationsAsync());
  }

  [Fact]
  [Trait("Category", "HealthAndConfiguration")]
  [Trait("CaseId", "PROP-API-06")]
  public async Task UnsafeTestIdentityProfilesAndReturnUrlsFailClosed()
  {
    var identity = PbcSeed.User(Guid.NewGuid(), "Staff");
    var noDatabase = "Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres";

    var productionSettings = new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = noDatabase,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = identity.Subject,
      ["DevelopmentIdentity:TenantId"] = identity.TenantId,
      ["Application:AllowSimulationAdapters"] = "false",
      ["ExternalEffects:Enabled"] = "false"
    };
    using (var production = new ApiWebApplicationFactory(productionSettings, "Production"))
    {
      var error = Assert.ThrowsAny<Exception>(() => production.CreateClient());
      Assert.Contains("Development identity is allowed only", error.ToString());
    }

    var oidcSettings = new Dictionary<string, string?>(productionSettings)
    {
      ["DevelopmentIdentity:Enabled"] = "true",
      ["Identity:TenantId"] = "tenant-test",
      ["Identity:ClientId"] = Guid.NewGuid().ToString("D"),
      ["Identity:ClientSecret"] = "synthetic-test-secret",
      ["Application:AllowSimulationAdapters"] = "true"
    };
    using (var mixed = new ApiWebApplicationFactory(oidcSettings))
    {
      var error = Assert.ThrowsAny<Exception>(() => mixed.CreateClient());
      Assert.Contains("Development identity cannot be enabled with OIDC", error.ToString());
    }

    await using var pg = await OwnedPostgresDatabase.CreateAsync("PROP-API-06");
    var seeded = await PbcSeed.SeedAsync(pg);
    var missingIdentity = PbcSeed.User(seeded.FirmId, "Staff");
    using var unavailableFactory = CreateFactory(pg.ConnectionString, missingIdentity,
      Path.Combine(Path.GetTempPath(), "AuditSphereOps-api-tests", Guid.NewGuid().ToString("N")));
    using var unavailableClient = unavailableFactory.CreateClient(new() { AllowAutoRedirect = false });
    using var unavailable = await unavailableClient.GetAsync("/auth/sign-in?returnUrl=%2Fapp");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);

    using var safeFactory = CreateFactory(pg.ConnectionString, seeded.Staff,
      Path.Combine(Path.GetTempPath(), "AuditSphereOps-api-tests", Guid.NewGuid().ToString("N")));
    using var safeClient = safeFactory.CreateClient(new() { AllowAutoRedirect = false });
    using var unsafeReturn = await safeClient.GetAsync("/auth/sign-in?returnUrl=%2F%2Fattacker.invalid");
    Assert.Equal(HttpStatusCode.Redirect, unsafeReturn.StatusCode);
    Assert.Equal("/auth/landing", unsafeReturn.Headers.Location?.OriginalString);
  }

  private static ApiWebApplicationFactory CreateFactory(string connectionString,
    AuditSphereOps.Domain.Security.AppUser identity, string root, string? environment = null,
    bool developmentIdentity = true, bool oidc = false)
  {
    var settings = new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = connectionString,
      ["DevelopmentIdentity:Enabled"] = developmentIdentity.ToString(),
      ["DevelopmentIdentity:Subject"] = identity.Subject,
      ["DevelopmentIdentity:TenantId"] = identity.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false",
      ["Storage:PbcStagingRoot"] = root,
      ["Storage:PbcProviderSimulationRoot"] = Path.Combine(root, "provider")
    };
    if (oidc)
    {
      settings["Identity:TenantId"] = "tenant-test";
      settings["Identity:ClientId"] = Guid.NewGuid().ToString("D");
      settings["Identity:ClientSecret"] = "synthetic-test-secret";
    }
    if (environment is not null) settings["ASPNETCORE_ENVIRONMENT"] = environment;
    return new(settings, environment ?? "Test");
  }

  private static async Task<HttpClient> SignInAsync(ApiWebApplicationFactory factory,
    AuditSphereOps.Domain.Security.AppUser user)
  {
    var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var response = await client.GetAsync($"/auth/sign-in?returnUrl=%2Fportal&subject={Uri.EscapeDataString(user.Subject)}");
    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    Assert.NotNull(response.Headers.Location);
    response.Dispose();
    return client;
  }

  private static async Task<(Guid UploadIntentId, string Capability)> StartUploadAsync(ITestPostgresDatabase pg,
    PbcSeed.Fixture fixture, AuditSphereOps.Application.Abstractions.ActorContext actor,
    Guid requestId, byte[] bytes)
  {
    var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    await using var db = new AuditSphereDbContext(pg.Options);
    var started = await PbcService.StartUploadAsync(db, actor,
      new StartPbcUploadRequest(requestId, "synthetic.txt", "text/plain", bytes.Length, hash));
    Assert.True(started.Succeeded, started.ErrorCode);
    return (started.Value!.UploadIntentId, started.Value.Capability!);
  }

  private static HttpRequestMessage ChunkRequest(Guid uploadId, int index, byte[] bytes, string capability,
    long offset = 0, string? hash = null, string? origin = "http://localhost")
  {
    var request = new HttpRequestMessage(HttpMethod.Post, $"/api/pbc/uploads/{uploadId:D}/chunks/{index}");
    request.Headers.TryAddWithoutValidation("X-Upload-Offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
    request.Headers.TryAddWithoutValidation("X-Content-SHA256", hash ??
      Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    request.Headers.TryAddWithoutValidation("X-Pbc-Upload-Capability", capability);
    if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
    request.Content = new ByteArrayContent(bytes);
    return request;
  }

  private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
  {
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
      stream.WriteAsync(bytes.AsMemory()).AsTask();
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
  }

  private sealed class CancellationContent(byte[] bytes) : HttpContent
  {
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
      SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
      CancellationToken cancellationToken)
    {
      await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    protected override bool TryComputeLength(out long length) { length = 0; return false; }
  }

  private sealed class OwnedApiRoot : IDisposable
  {
    public string Path { get; } = System.IO.Path.Combine(
      System.IO.Path.GetTempPath(), "AuditSphereOps-api-tests", Guid.NewGuid().ToString("N"));

    public void Dispose() => PbcSeed.DeleteDirectory(Path);
  }
}
