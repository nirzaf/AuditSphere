using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class RecordsArchiveRecoveryApiTests
{
  [Fact]
  [Trait("CaseId", "R09-PERSISTED-INCOMPLETE-REPLAY")]
  public async Task BuildReadFaultPersistsIncompleteAndExactHttpReplayDoesNotWriteAgain()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("R09-PERSISTED-INCOMPLETE-REPLAY");
    // The supported single-engagement fixture generates the engagement letter and binds
    // portal intake to this engagement. The completion-only sibling fixture omits that letter.
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg, includeSibling: false);
    var scope = world.Primary;
    var archiveId = Guid.CreateVersion7();
    var path = $"/api/ui/records/archives/{archiveId:D}";

    async Task<(AppUser User, Guid GrantId)> ProvisionAsync(Guid clientId, Guid? engagementId, string role, string kind)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var admin = await db.Users.AsNoTracking().SingleAsync(x => x.Id == world.Administrator.UserId);
      var subject = Guid.NewGuid();
      var created = await RoleAdministrationService.EnsureUserAsync(db, world.Administrator,
        new(admin.TenantId, subject.ToString("D"), $"r09-{subject:N}@example.test",
          "Synthetic archive recovery " + role, kind, "APPROVED_ROSTER"));
      Assert.True(created.Succeeded, created.Message);
      Guid grantId;
      var grantScope = engagementId is null ? "CLIENT" : "ENGAGEMENT";
      if (kind == "Client")
      {
        var grant = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, world.Administrator,
          new(created.Value, role, grantScope, clientId, engagementId));
        Assert.True(grant.Succeeded, grant.Message);
        Assert.Equal("NOT_SENT", grant.Value!.DeliveryState);
        grantId = grant.Value.RoleGrantId;
      }
      else
      {
        var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, world.Administrator,
          new(created.Value, role, grantScope, clientId, engagementId));
        Assert.True(grant.Succeeded, grant.Message);
        grantId = grant.Value;
      }
      return (await db.Users.AsNoTracking().SingleAsync(x => x.Id == created.Value), grantId);
    }

    var (custodian, custodianGrant) = await ProvisionAsync(scope.ClientId, scope.EngagementId, "Partner", "Staff");
    var (clientUser, _) = await ProvisionAsync(scope.ClientId, scope.EngagementId, "ClientUser", "Client");
    var clientActor = new ActorContext(clientUser.Id, scope.FirmId, clientUser.SessionEpoch, ["ClientUser"]);
    PbcSeed.Fixture pbc;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var firstSignIn = await ClientPortalService.CompleteFirstSignInAsync(db, clientActor, true);
      Assert.True(firstSignIn.Succeeded, firstSignIn.Message);
      var portalAccess = await AuthorizationDecision.AuthorizeAsync(db, clientActor,
        new(scope.FirmId, scope.ClientId, scope.EngagementId, RequiredRoles: ["ClientUser"]));
      Assert.True(portalAccess.Succeeded, portalAccess.Message);
      pbc = new(scope.FirmId, scope.ClientId, scope.EngagementId,
        await db.Users.AsNoTracking().SingleAsync(x => x.Id == scope.Preparer.UserId),
        await db.Users.AsNoTracking().SingleAsync(x => x.Id == world.Reviewer.UserId),
        clientUser, await db.Users.AsNoTracking().SingleAsync(x => x.Id == world.Administrator.UserId));
    }
    var source = await SupportedArchiveSourceFixture.PrepareAsync(pg, pbc, world.Administrator, scope.Preparer,
      clientActor, "Synthetic R09 source bound to the captured PBC version."u8.ToArray());
    var otherClientId = await SupportedAuditCompletionWorldBuilder.CreateAdditionalClientAsync(pg, world);
    var (otherClientPartner, _) = await ProvisionAsync(otherClientId, null, "Partner", "Staff");

    Guid referenceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var snapshot = await db.DocumentSnapshots.AsNoTracking().SingleAsync(x => x.Id == source.SnapshotId);
      referenceId = snapshot.DocumentReferenceId;
      Assert.True(await db.DocumentReferences.AnyAsync(x => x.Id == referenceId && x.FirmId == scope.FirmId &&
        x.ClientId == scope.ClientId && x.EngagementId == scope.EngagementId));
      await ArchiveRecoveryBrowserHarness.AddSyntheticArchiveAsync(db, scope, custodian, archiveId);
      Assert.False(await db.ArchiveManifests.AnyAsync());
    }
    var originalBlockers = await CompletionBlockersAsync();
    Assert.NotEmpty(originalBlockers);
    var initialSources = await SourceRowsAsync();

    var fault = new HideReferenceOnBuild(scope, referenceId, path + "/build");
    using var factory = Factory(pg, custodian, services =>
    {
      services.RemoveAll<IDbContextFactory<AuditSphereDbContext>>();
      services.RemoveAll<DbContextOptions<AuditSphereDbContext>>();
      services.AddDbContextFactory<AuditSphereDbContext>((provider, options) => options.UseNpgsql(pg.ConnectionString)
        .AddInterceptors(fault.ForRequests(provider.GetRequiredService<IHttpContextAccessor>())));
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignInAsync(client);
    var requestKey = Guid.CreateVersion7().ToString("D");
    var request = new { commercialBundleId = (Guid?)null, requestKey };

    fault.Armed = true;
    using var first = await PostAsync(client, csrf, path + "/build", request);
    fault.Armed = false;
    Assert.Equal(1, fault.Hits);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    using var responseBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
    var built = responseBody.RootElement.GetProperty("value").Deserialize<ArchiveManifestResult>(JsonSerializerOptions.Web)!;
    Assert.Equal((archiveId, 1L, "BUILT", "INCOMPLETE"), (built.ArchiveId, built.ManifestVersion, built.Status, built.CompletenessStatus));

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var row = Assert.Single(await db.ArchiveManifests.AsNoTracking().ToListAsync());
      Assert.Equal((built.ManifestId, built.ManifestVersion, built.Digest, built.CompletenessStatus, requestKey),
        (row.Id, row.Version, row.ManifestDigest, row.CompletenessStatus, row.BuildRequestKey));
      Assert.Equal("1 snapshot reference(s) are missing.", row.CompletenessException);
      var entries = await db.ArchiveManifestEntries.AsNoTracking().OrderBy(x => x.Ordinal).ToListAsync();
      Assert.Equal(built.EntryCount, entries.Count);
      Assert.Equal(source.SnapshotId, Assert.Single(entries, x => x.EntryKind == "DOCUMENT_SNAPSHOT").SourceId);
      var export = Assert.Single(await db.ArchiveStructuredExports.AsNoTracking().ToListAsync());
      Assert.Equal(row.Id, export.ArchiveManifestId);
      Assert.Equal(export.ContentHash, Assert.Single(entries, x => x.EntryKind == "STRUCTURED_EXPORT").ContentHash);
      using var exportedPayload = JsonDocument.Parse(export.PayloadJson);
      var exportedReferences = exportedPayload.RootElement.GetProperty("documents").GetProperty("references");
      Assert.Equal(referenceId, Assert.Single(exportedReferences.EnumerateArray()).GetProperty("Id").GetGuid());
      Assert.Equal(ArchiveStates.Issued, (await db.Archives.AsNoTracking().SingleAsync()).Status);
    }
    var retained = await ArchiveRowsAsync();

    // The second host uses the normal context factory. No fault is registered on any recovery request.
    using var recoveryFactory = Factory(pg, custodian);
    using var recovery = recoveryFactory.CreateClient(new() { AllowAutoRedirect = false });
    var recoveryCsrf = await SignInAsync(recovery);
    using (var read = await recovery.GetAsync(path))
    {
      Assert.Equal(HttpStatusCode.OK, read.StatusCode);
      var view = (await read.Content.ReadFromJsonAsync<ArchiveRecordView>())!;
      Assert.Equal((built.ManifestId, built.ManifestVersion, built.Digest, "INCOMPLETE"),
        (view.ManifestId!.Value, view.ManifestVersion!.Value, view.ManifestDigest, view.CompletenessStatus));
      Assert.Equal(built.EntryCount, view.Entries.Count);
      Assert.False(view.CanReview);
      Assert.False(view.CanExport);
    }
    using var replay = await PostAsync(recovery, recoveryCsrf, path + "/build", request);
    Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
    using (var conflict = await PostAsync(recovery, recoveryCsrf, path + "/build",
      new { commercialBundleId = Guid.CreateVersion7(), requestKey }))
      await AssertRefusedAsync(conflict, HttpStatusCode.Conflict, ErrorCodes.IdempotencyConflict);
    using (var review = await PostAsync(recovery, recoveryCsrf, path + "/review",
      new { manifestId = built.ManifestId, manifestVersion = built.ManifestVersion, manifestDigest = built.Digest,
        requestKey = Guid.CreateVersion7().ToString("D") }))
      await AssertRefusedAsync(review, HttpStatusCode.BadRequest, ErrorCodes.GateBlocked);
    using (var export = await recovery.GetAsync(path + "/export"))
    {
      await AssertRefusedAsync(export, HttpStatusCode.BadRequest, ErrorCodes.GateBlocked);
      Assert.Null(export.Content.Headers.ContentDisposition);
    }

    using var otherClientFactory = Factory(pg, otherClientPartner);
    using var denied = otherClientFactory.CreateClient(new() { AllowAutoRedirect = false });
    var deniedCsrf = await SignInAsync(denied);
    using (var foreign = await PostAsync(denied, deniedCsrf, path + "/build", request))
      await AssertRefusedAsync(foreign, HttpStatusCode.Forbidden, ErrorCodes.ScopeDenied);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, world.Administrator,
        new(custodianGrant, Reason: "Synthetic recovery session revocation."));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    using (var stale = await PostAsync(recovery, recoveryCsrf, path + "/build", request))
      Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    Assert.Equal(retained, await ArchiveRowsAsync());
    Assert.Equal(initialSources, await SourceRowsAsync());
    Assert.Equal(originalBlockers, await CompletionBlockersAsync());
    Assert.Equal(1, fault.Hits);

    async Task<string[]> CompletionBlockersAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      Assert.Equal("Active", await db.Engagements.Where(x => x.Id == scope.EngagementId).Select(x => x.Status).SingleAsync());
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, world.Reviewer, scope.EngagementId);
      Assert.True(completion.Succeeded, completion.Message);
      return completion.Value!.Blockers.Order(StringComparer.Ordinal).ToArray();
    }

    async Task<string> ArchiveRowsAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return JsonSerializer.Serialize(new
      {
        archive = await db.Archives.AsNoTracking().SingleAsync(),
        manifest = await db.ArchiveManifests.AsNoTracking().SingleAsync(),
        entries = await db.ArchiveManifestEntries.AsNoTracking().OrderBy(x => x.Ordinal).ToListAsync(),
        export = await db.ArchiveStructuredExports.AsNoTracking().SingleAsync(),
        rowVersions = await db.Database.SqlQueryRaw<string>("""
          SELECT 'archive:' || id || ':' || xmin::text AS "Value" FROM archives
          UNION ALL SELECT 'manifest:' || id || ':' || xmin::text FROM archive_manifests
          UNION ALL SELECT 'entry:' || id || ':' || xmin::text FROM archive_manifest_entries
          UNION ALL SELECT 'export:' || id || ':' || xmin::text FROM archive_structured_exports
          """).OrderBy(x => x).ToListAsync()
      });
    }

    async Task<string> SourceRowsAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return JsonSerializer.Serialize(new
      {
        reference = await db.DocumentReferences.AsNoTracking().SingleAsync(x => x.Id == referenceId),
        snapshot = await db.DocumentSnapshots.AsNoTracking().SingleAsync(x => x.Id == source.SnapshotId)
      });
    }
  }

  private static StandaloneApiApplicationFactory Factory(ITestPostgresDatabase pg, AppUser user,
    Action<IServiceCollection>? configure = null) => new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = user.Subject,
      ["DevelopmentIdentity:TenantId"] = user.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    }, configure);

  private static async Task<string> SignInAsync(HttpClient client)
  {
    using var signIn = await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string csrf, string path, object body)
  {
    using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    message.Headers.Add("X-XSRF-TOKEN", csrf);
    return await client.SendAsync(message);
  }

  private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
  {
    Assert.Equal(status, response.StatusCode);
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    Assert.False(body.RootElement.TryGetProperty("value", out _));
  }

  private sealed class HideReferenceOnBuild(SupportedAuditScope scope, Guid referenceId, string buildPath)
  {
    private int hits;
    public int Hits => Volatile.Read(ref hits);
    public bool Armed { get; set; }

    public DbCommandInterceptor ForRequests(IHttpContextAccessor http) => new SourceReadInterceptor(this, http);

    private sealed class SourceReadInterceptor(HideReferenceOnBuild fault, IHttpContextAccessor http) : DbCommandInterceptor
    {
      public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
      {
        fault.HideReference(command, http.HttpContext?.Request);
        return ValueTask.FromResult(result);
      }
    }

    private void HideReference(DbCommand command, HttpRequest? request)
    {
      if (!Armed || request?.Path != buildPath || !HttpMethods.IsPost(request.Method) ||
          !command.CommandText.ReplaceLineEndings("\n").StartsWith(
            "SELECT d.id\nFROM document_references AS d\nWHERE ", StringComparison.Ordinal))
        return;

      Assert.NotNull(command.Transaction);
      Assert.Equal(new[] { scope.FirmId, scope.ClientId, scope.EngagementId }.Order(),
        command.Parameters.Cast<DbParameter>().Select(p => Assert.IsType<Guid>(p.Value)).Order());
      Assert.Equal(1, Interlocked.Increment(ref hits));
      // Hide only the matched reference from this SELECT. PostgreSQL retains the reference and its restrictive FK.
      var parameter = command.CreateParameter();
      parameter.ParameterName = "r09_hidden_reference";
      parameter.Value = referenceId;
      command.Parameters.Add(parameter);
      command.CommandText += " AND d.id <> @r09_hidden_reference";
    }
  }
}
