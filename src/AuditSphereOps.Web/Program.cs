using System.Buffers;
using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Serilog;
using AuditSphereOps.Web.Authentication;
using AuditSphereOps.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Serilog console bootstrap (§45.8); file/central sinks land with operations hardening.
Log.Logger = new LoggerConfiguration()
  .ReadFrom.Configuration(builder.Configuration)
  .Enrich.FromLogContext()
  .WriteTo.Console()
  .CreateLogger();
builder.Host.UseSerilog();

// Razor components: Interactive Server, no prerender for auth-sensitive shells (§43.4 draft).
builder.Services.AddRazorComponents()
  .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// PostgreSQL: single AuditSphere connection string; startup validates, never auto-applies destructive DDL (§45.6).
var connectionString = builder.Configuration.GetConnectionString("AuditSphere");
builder.Services.AddDbContextFactory<AuditSphereDbContext>(options =>
  options.UseNpgsql(connectionString ?? "Host=127.0.0.1;Port=5433;Database=auditsphere;Username=postgres"));

var identity = builder.Configuration.GetSection("Identity");
var tenantId = identity["TenantId"];
var clientId = identity["ClientId"];
var clientSecret = identity["ClientSecret"];
var oidcConfigured = !string.IsNullOrWhiteSpace(tenantId) &&
                     !string.IsNullOrWhiteSpace(clientId) &&
                     !string.IsNullOrWhiteSpace(clientSecret);
var authentication = builder.Services.AddAuthentication(options =>
{
  options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
  options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
  options.DefaultChallengeScheme = oidcConfigured ? "Entra" : CookieAuthenticationDefaults.AuthenticationScheme;
}).AddCookie(options => options.LoginPath = "/auth/sign-in");
if (oidcConfigured)
{
  authentication.AddOpenIdConnect("Entra", options =>
  {
    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
    options.ClientId = clientId!;
    options.ClientSecret = clientSecret!;
    options.ResponseType = "code";
    options.CallbackPath = identity["CallbackPath"] ?? "/signin-oidc";
    options.SaveTokens = false;
    options.GetClaimsFromUserInfoEndpoint = false;
  });
}
builder.Services.AddAuthorization();
builder.Services.AddScoped<CurrentActorResolver>();
builder.Services.AddScoped<TrustedActorResolver>();

// Durable operation enqueue boundary for trusted PBC completion (§43.5 item 5). The web host
// records transfer intents in the durable outbox inside the completion transaction; it never
// executes provider effects and never claims operations, so no claim registry is registered.
// The simulated sink is harmless here: simulated execution requires the Test worker
// composition, and this host only records queue entries.
builder.Services.AddSingleton<IAuditSphereDbContextFactory, OperationContextFactory>();
builder.Services.AddSingleton<IOperationStore, PostgresOperationStore>();
var externalEffectsEnabled = builder.Configuration.GetValue<bool>("ExternalEffects:Enabled");
if (externalEffectsEnabled)
{
  if (string.IsNullOrWhiteSpace(connectionString) || !oidcConfigured ||
      !long.TryParse(builder.Configuration["ExternalEffects:DeploymentEpoch"], out var externalEpoch) || externalEpoch < 1)
    throw new InvalidOperationException("External effects require a connection string, OIDC identity and positive deployment epoch.");
  throw new InvalidOperationException("ExternalEffects:Enabled requires an approved live provider composition; startup refused.");
}

var simulationSinkRoot = builder.Configuration["Storage:PbcProviderSimulationRoot"]
  ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "pbc-provider-simulation");
builder.Services.AddSingleton<IPbcProviderSink>(new SimulationPbcProviderSink(simulationSinkRoot));
builder.Services.AddSingleton<PbcDocumentTransferHandler>();

var checkpointRoot = builder.Configuration["Storage:ReleaseCheckpointRoot"]
  ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "release-checkpoints");
builder.Services.AddSingleton<IReleaseCheckpointStore>(new LocalAppendOnlyCheckpointStore(checkpointRoot));
builder.Services.AddSingleton<ReleaseCheckpointHandler>();

var releaseSafety = builder.Configuration.GetSection(ReleaseSafetyOptions.SectionName).Get<ReleaseSafetyOptions>() ?? new();
releaseSafety.Validate(
  externalEffectsEnabled,
  builder.Configuration.GetValue<bool>("Application:AllowSimulationAdapters"),
  builder.Environment.EnvironmentName,
  builder.Configuration.GetValue<bool>("FeatureActivation:LiveAuditRelease"));
builder.Services.AddSingleton(releaseSafety);

// Liveness/readiness split (§45.4): self = always; ready = DB reachable (custom check, no extra package).
builder.Services.AddHealthChecks()
  .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("web alive"))
  .AddNpgSqlCheck(connectionString ?? "Host=127.0.0.1;Port=5433;Database=auditsphere;Username=postgres")
  .AddCheck<MigrationCheck>("migrations", tags: ["ready"]);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error", createScopeForErrors: true);
  app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapHealthChecks("/health/live", new() { Predicate = r => r.Name == "self" });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
if (oidcConfigured)
{
  app.MapGet("/auth/sign-in", (HttpContext http, string? returnUrl) =>
  {
    var destination = !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') &&
                      !returnUrl.StartsWith("//", StringComparison.Ordinal)
      ? returnUrl : "/app";
    return Results.Challenge(new AuthenticationProperties { RedirectUri = destination }, ["Entra"]);
  });
  app.MapGet("/auth/sign-out", async (HttpContext http) =>
  {
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
  });
}

app.MapPost("/api/pbc/uploads/{uploadId:guid}/chunks/{chunkIndex:int}", async (
  Guid uploadId,
  int chunkIndex,
  HttpContext http,
  TrustedActorResolver actorResolver,
  IDbContextFactory<AuditSphereDbContext> dbFactory,
  CancellationToken ct) =>
{
  var actor = await actorResolver.ResolveAsync(http.User, ct);
  if (actor is null)
    return Results.Unauthorized();
  var origin = http.Request.Headers.Origin.ToString();
  if (!string.IsNullOrWhiteSpace(origin))
  {
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
      return Results.Forbid();
    var expectedPort = http.Request.Host.Port ?? (http.Request.IsHttps ? 443 : 80);
    var originPort = originUri.IsDefaultPort ? (originUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80) : originUri.Port;
    if (!string.Equals(originUri.Scheme, http.Request.Scheme, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(originUri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase) ||
        originPort != expectedPort)
      return Results.Forbid();
  }
  if (chunkIndex < 0 || !long.TryParse(http.Request.Headers["X-Upload-Offset"], out var offset) || offset < 0 ||
      string.IsNullOrWhiteSpace(http.Request.Headers["X-Content-SHA256"]) ||
      string.IsNullOrWhiteSpace(http.Request.Headers["X-Pbc-Upload-Capability"]))
    return Results.BadRequest(new { code = "pbc.chunk.invalid" });
  if (http.Request.ContentLength is > PbcService.MaxChunkBytes)
    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

  var configuredRoot = builder.Configuration["Storage:PbcStagingRoot"];
  if (string.IsNullOrWhiteSpace(configuredRoot) && !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test"))
    return Results.Problem("PBC staging storage is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
  var stagingRoot = configuredRoot ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "pbc-staging");
  var directory = Path.Combine(stagingRoot, actor.FirmId.ToString("N"), uploadId.ToString("N"));
  var path = Path.Combine(directory, $"{chunkIndex}-{Guid.NewGuid():N}.part");
  var keep = false;
  try
  {
    Directory.CreateDirectory(directory);
    long count = 0;
    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
    try
    {
      await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
        bufferSize: buffer.Length, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
      while (true)
      {
        var read = await http.Request.Body.ReadAsync(buffer.AsMemory(), ct);
        if (read == 0) break;
        count += read;
        if (count > PbcService.MaxChunkBytes)
          return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        digest.AppendData(buffer, 0, read);
        await output.WriteAsync(buffer.AsMemory(0, read), ct);
      }
      await output.FlushAsync(ct);
    }
    finally { ArrayPool<byte>.Shared.Return(buffer); }

    var hash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    var declaredHash = http.Request.Headers["X-Content-SHA256"].ToString();
    if (count == 0 || !string.Equals(hash, declaredHash, StringComparison.OrdinalIgnoreCase))
      return Results.BadRequest(new { code = "pbc.chunk.hash-mismatch" });
    if (http.Request.ContentLength.HasValue && http.Request.ContentLength.Value != count)
      return Results.BadRequest(new { code = "pbc.chunk.length-mismatch" });

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var result = await PbcService.RecordChunkAsync(db, actor,
      new RecordPbcUploadChunkRequest(uploadId, chunkIndex, offset, checked((int)count), hash,
        http.Request.Headers["X-Pbc-Upload-Capability"].ToString(), path), ct);
    if (!result.Succeeded)
    {
      return result.ErrorCode switch
      {
        "pbc.chunk.conflict" or "pbc.chunk-offset" => Results.Conflict(new { code = result.ErrorCode }),
        "pbc.quota" => Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
        ErrorCodes.ScopeDenied => Results.Forbid(),
        _ => Results.BadRequest(new { code = result.ErrorCode })
      };
    }

    var stored = await db.PbcUploadChunks.AsNoTracking().SingleAsync(x =>
      x.FirmId == actor.FirmId && x.PbcUploadIntentId == uploadId && x.ChunkIndex == chunkIndex, ct);
    keep = string.Equals(stored.StagedPath, path, StringComparison.Ordinal);
    http.Response.Headers.CacheControl = "no-store";
    http.Response.Headers["X-Content-Type-Options"] = "nosniff";
    return Results.Ok(new { uploadId, chunkIndex, offset, byteCount = count, state = result.Value!.State });
  }
  catch (OperationCanceledException) when (ct.IsCancellationRequested)
  {
    throw;
  }
  catch (IOException)
  {
    return Results.Problem("The bounded staging write could not be completed.", statusCode: StatusCodes.Status503ServiceUnavailable);
  }
  finally
  {
    if (!keep)
    {
      try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
  }
});

app.MapRazorComponents<AuditSphereOps.Web.Components.App>()
  .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Npgsql readiness probe without an extra health-check package (§45.4).</summary>
file sealed class NpgsqlCheck(string connectionString) : IHealthCheck
{
  public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
  {
    try
    {
      await using var conn = new NpgsqlConnection(connectionString);
      await conn.OpenAsync(ct);
      await using var cmd = new NpgsqlCommand("SELECT 1", conn);
      await cmd.ExecuteScalarAsync(ct);
      return HealthCheckResult.Healthy("postgres reachable");
    }
    catch (Exception ex)
    {
      return HealthCheckResult.Unhealthy("postgres unreachable", ex);
    }
  }
}

file static class NpgsqlCheckExtensions
{
  public static Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder AddNpgSqlCheck(
    this Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder builder,
    string connectionString) =>
    builder.AddCheck("postgres", new NpgsqlCheck(connectionString), tags: ["ready"]);
}

file sealed class MigrationCheck(IDbContextFactory<AuditSphereDbContext> factory) : IHealthCheck
{
  public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
  {
    try
    {
      await using var db = await factory.CreateDbContextAsync(ct);
      var pending = await db.Database.GetPendingMigrationsAsync(ct);
      return pending.Any()
        ? HealthCheckResult.Unhealthy($"{pending.Count()} database migrations are pending")
        : HealthCheckResult.Healthy("database schema is current");
    }
    catch (Exception ex)
    {
      return HealthCheckResult.Unhealthy("database migration state unavailable", ex);
    }
  }
}
