using System.Net;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

/// <summary>Opt-in local synthetic browser verification; never discovered as an xUnit test.</summary>
public static class ArchiveRecoveryBrowserHarness
{
  public sealed record BrowserScope(string Origin, Guid EngagementId, Guid ArchiveId, string StorageKey);
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

  public static async Task RunAsync(string evidenceDirectory,
    Func<BrowserScope, CancellationToken, Task> browserJourney, CancellationToken cancellationToken)
  {
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_ARCHIVE_BROWSER_OPT_IN") != "local-synthetic-only")
      throw new InvalidOperationException("Explicit local synthetic browser opt-in is required.");
    var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_CONNECTION") ?? "");
    if (connection.Host != "127.0.0.1" || connection.Port != 5433 || connection.Database != "auditsphere_tests")
      throw new InvalidOperationException("Only isolated loopback5433 auditsphere_tests is permitted.");
    evidenceDirectory = Path.GetFullPath(evidenceDirectory);
    if (Directory.Exists(evidenceDirectory)) throw new InvalidOperationException("Use a fresh owned evidence directory.");
    Directory.CreateDirectory(evidenceDirectory);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromMinutes(5));
    var ct = timeout.Token;
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var scope = world.Primary;
    AppUser user;
    ActorContext actor;
    var archiveId = Guid.CreateVersion7();
    string originalStatus;
    string[] originalBlockers;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var administrator = await db.Users.SingleAsync(x => x.Id == world.Administrator.UserId, ct);
      var created = await RoleAdministrationService.EnsureUserAsync(db, world.Administrator,
        new(administrator.TenantId, Guid.NewGuid().ToString("D"), "archive-custodian@example.test",
          "Synthetic archive custodian", "Staff", "APPROVED_ROSTER"), ct);
      Assert.True(created.Succeeded, created.Message);
      var granted = await RoleAdministrationService.ApplyRoleGrantAsync(db, world.Administrator,
        new(created.Value, "Partner", "ENGAGEMENT", scope.ClientId, scope.EngagementId), ct);
      Assert.True(granted.Succeeded, granted.Message);
      user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == created.Value, ct);
      actor = new(user.Id, user.FirmId, user.SessionEpoch, ["Partner"]);
      originalStatus = await db.Engagements.Where(x => x.Id == scope.EngagementId).Select(x => x.Status).SingleAsync(ct);
      Assert.NotEqual("Completed", originalStatus); Assert.NotEqual("Closed", originalStatus);
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, world.Reviewer, scope.EngagementId, ct);
      Assert.True(completion.Succeeded, completion.Message);
      originalBlockers = completion.Value!.Blockers.Order(StringComparer.Ordinal).ToArray();
      Assert.NotEmpty(originalBlockers);
      // Owner-approved exception: exactly one synthetic approved TEST profile and issued archive.
      // No completion, identity, role, consent, hold or professional approval state is seeded.
      var now = DateTimeOffset.UtcNow;
      db.RecordsProfiles.Add(new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ProfileCode = "TEST-RECOVERY", Version = 1,
        RecordClass = "Synthetic archive recovery", Jurisdiction = "TEST", ServiceRoute = "STATUTORY_AUDIT",
        RetentionTrigger = "REPORT_DATE", ProtectionMode = "LOCAL_ONLY", LabelId = "test-only",
        LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
        DispositionOwner = "Synthetic custodian", BackupRequirements = "Synthetic fixture only",
        CreatedByUserId = user.Id, CreatedAt = now, Approved = true, ApprovedByUserId = user.Id, ApprovedAt = now
      });
      db.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
        ProfileId = "TEST-RECOVERY", ProfileVersion = 1, Status = ArchiveStates.Issued, CreatedAt = now
      });
      await db.SaveChangesAsync(ct);
      Assert.NotEqual(scope.EngagementId, archiveId);
      var denied = await EngagementArchiveHandoffQuery.GetAsync(db, actor, world.Sibling.EngagementId, ct);
      Assert.False(denied.Succeeded); Assert.Null(denied.Value);
    }
    var fault = new LostResponseFilter(pg, archiveId, evidenceDirectory, ct);
    await using var host = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["contentRoot"] = "/Users/qts/Repos/AuditSphere/src/AuditSphereOps.Api",
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = user.Subject,
      ["DevelopmentIdentity:TenantId"] = user.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
      ["AngularUi:Enabled"] = "true", ["AngularUi:CanonicalRoutes"] = "true",
      ["AngularUi:BuildPath"] = "/Users/qts/Repos/AuditSphere/src/AuditSphereOps.Ui/dist/auditsphere-ui/browser"
    }, services => services.AddSingleton<IStartupFilter>(fault));
    host.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
    using var client = host.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal("Test", host.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().EnvironmentName);
    var origin = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    var uri = new Uri(origin);
    Assert.Equal("127.0.0.1", uri.Host);
    var browserScope = new BrowserScope(uri.GetLeftPart(UriPartial.Authority), scope.EngagementId, archiveId,
      $"auditsphere-archive-intent-v1:{scope.FirmId}:{user.Id}:{scope.EngagementId}:{archiveId}");
    await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "scope.json"), JsonSerializer.Serialize(browserScope, Json), ct);
    await browserJourney(browserScope, ct);
    fault.AssertRecovered();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var manifest = await db.ArchiveManifests.AsNoTracking().SingleAsync(ct);
      Assert.Equal(archiveId, manifest.ArchiveId); Assert.Equal(1, manifest.Version);
      Assert.Equal("REVIEWED", manifest.Status); Assert.Equal("COMPLETE", manifest.CompletenessStatus);
      Assert.Equal(user.Id, manifest.ReviewedByUserId);
      Assert.Equal(fault.Build!.RequestKey, manifest.BuildRequestKey);
      Assert.Equal(fault.Review!.RequestKey, manifest.ReviewRequestKey);
      Assert.Equal(fault.Build.Result.ManifestId, manifest.Id);
      Assert.Equal(fault.Build.Result.Digest, manifest.ManifestDigest);
      var entries = await db.ArchiveManifestEntries.AsNoTracking().OrderBy(x => x.Ordinal).ToListAsync(ct);
      var export = await db.ArchiveStructuredExports.AsNoTracking().SingleAsync(ct);
      Assert.Equal(manifest.EntryCount, entries.Count);
      Assert.Equal(fault.OriginalEntries, JsonSerializer.Serialize(entries, Json));
      Assert.Equal(fault.OriginalExport, JsonSerializer.Serialize(export, Json));
      // Independent service replay after review proves historical build recovery, not another browser variant.
      var replay = await RecordsArchiveService.BuildManifestWithRequestAsync(db, actor, archiveId, null, manifest.BuildRequestKey, ct);
      Assert.True(replay.Succeeded, replay.Message); Assert.Equal(fault.Build.Result, replay.Value);
      var refused = await RecordsArchiveService.BuildManifestWithRequestAsync(db, actor, archiveId, null, Guid.NewGuid().ToString(), ct);
      Assert.False(refused.Succeeded);
      Assert.Equal(1, await db.ArchiveManifests.CountAsync(ct));
      Assert.Equal(1, await db.Archives.CountAsync(ct));
      Assert.Equal(originalStatus, await db.Engagements.Where(x => x.Id == scope.EngagementId).Select(x => x.Status).SingleAsync(ct));
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, world.Reviewer, scope.EngagementId, ct);
      Assert.True(completion.Succeeded, completion.Message);
      Assert.Equal(originalBlockers, completion.Value!.Blockers.Order(StringComparer.Ordinal).ToArray());
      await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "database-verified.json"), JsonSerializer.Serialize(new
      { manifest, entries, export.ContentHash, export.ByteCount, originalStatus, originalBlockers, replay = replay.Value,
        newWriteRefusal = refused.ErrorCode, source = "Actual isolated PostgreSQL; not live or professional evidence" }, Json), ct);
    }
  }

  private sealed record Transmission(string RequestKey, string RequestBody, ArchiveManifestResult Result);

  private sealed class LostResponseFilter(PgTestSchema pg, Guid archiveId, string evidenceDirectory, CancellationToken ct) : IStartupFilter
  {
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, int> counts = [];
    public Transmission? Build { get; private set; }
    public Transmission? Review { get; private set; }
    public string? OriginalEntries { get; private set; }
    public string? OriginalExport { get; private set; }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
      app.Use(async (http, continuation) =>
      {
        var action = http.Request.Path.Value switch
        {
          var p when p == $"/api/ui/records/archives/{archiveId:D}/build" => "build",
          var p when p == $"/api/ui/records/archives/{archiveId:D}/review" => "review",
          _ => null
        };
        if (http.Request.Method != "POST" || action is null) { await continuation(); return; }
        await gate.WaitAsync(ct);
        try
        {
          http.Request.EnableBuffering();
          using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
          var requestBody = await reader.ReadToEndAsync(ct); http.Request.Body.Position = 0;
          using var request = JsonDocument.Parse(requestBody);
          var key = request.RootElement.GetProperty("requestKey").GetString()!;
          Assert.True(Guid.TryParse(key, out _));
          var previous = action == "build" ? Build : Review;
          if (previous is not null) Assert.Equal(previous.RequestBody, requestBody);
          var responseStream = http.Response.Body;
          await using var buffered = new MemoryStream();
          http.Response.Body = buffered;
          try
          {
            await continuation(); // The real endpoint owns authentication, CSRF, authorization and transaction commit.
            Assert.Equal(200, http.Response.StatusCode);
            buffered.Position = 0;
            using var response = await JsonDocument.ParseAsync(buffered, cancellationToken: ct);
            var receipt = response.RootElement.GetProperty("value").Deserialize<ArchiveManifestResult>(Json)!;
            Assert.Equal(archiveId, receipt.ArchiveId);
            await using var db = new AuditSphereDbContext(pg.Options);
            var row = await db.ArchiveManifests.AsNoTracking().SingleAsync(ct);
            Assert.Equal(receipt.ManifestId, row.Id); Assert.Equal(receipt.Digest, row.ManifestDigest);
            Assert.Equal(key, action == "build" ? row.BuildRequestKey : row.ReviewRequestKey);
            var transmission = new Transmission(key, requestBody, receipt);
            if (previous is null)
            {
              if (action == "build")
              {
                Build = transmission;
                OriginalEntries = JsonSerializer.Serialize(await db.ArchiveManifestEntries.AsNoTracking().OrderBy(x => x.Ordinal).ToListAsync(ct), Json);
                OriginalExport = JsonSerializer.Serialize(await db.ArchiveStructuredExports.AsNoTracking().SingleAsync(ct), Json);
              }
              else Review = transmission;
            }
            else Assert.Equal(previous.Result, receipt);
            counts[action] = counts.GetValueOrDefault(action) + 1;
            Assert.InRange(counts[action], 1, 2);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, $"{action}-{counts[action]}-committed.json"),
              JsonSerializer.Serialize(transmission, Json), ct);
            if (previous is null)
            {
              // Deliver the original status/headers and a strict prefix of its real body before
              // disconnecting. A pre-header abort may be transparently resent by Chromium.
              var originalBytes = buffered.ToArray();
              Assert.True(originalBytes.Length > 1);
              http.Response.Body = responseStream;
              http.Response.ContentLength = originalBytes.Length;
              await http.Response.StartAsync(ct);
              await responseStream.WriteAsync(originalBytes.AsMemory(0, 1), ct);
              await responseStream.FlushAsync(ct);
              await Task.Delay(100, ct);
              http.Abort(); // Truncated real response after commit, never a fabricated business result.
            }
            else { buffered.Position = 0; await buffered.CopyToAsync(responseStream, ct); }
          }
          finally { http.Response.Body = responseStream; }
        }
        finally { gate.Release(); }
      });
      next(app);
    };

    public void AssertRecovered()
    {
      Assert.Equal(2, counts.GetValueOrDefault("build"));
      Assert.Equal(2, counts.GetValueOrDefault("review"));
      Assert.NotNull(Build); Assert.NotNull(Review);
      Assert.Equal(Build.Result.ManifestId, Review.Result.ManifestId);
    }
  }
}
