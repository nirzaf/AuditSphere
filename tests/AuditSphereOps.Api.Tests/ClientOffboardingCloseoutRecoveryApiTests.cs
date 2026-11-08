using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ClientOffboardingCloseoutRecoveryApiTests
{
  [Fact]
  public async Task RecoveryRouteReturnsOnlyServerProvenTerminalNonCommitAfterArchiveRevisionAdvances()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("OFFBOARDING-CLOSEOUT-RECOVERY-API");
    var f = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.User(f.FirmId, "Staff");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(manager);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId));
      await db.SaveChangesAsync();
    }
    var actor = PbcSeed.Actor(manager, "Manager");
    ClientOffboardingReadiness readiness;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
      Assert.True(result.Succeeded, result.Message);
      readiness = result.Value!;
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = manager.Subject,
      ["DevelopmentIdentity:TenantId"] = manager.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    var csrf = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))
      .Split(';')[0]["XSRF-TOKEN=".Length..]);

    var requestKey = Guid.CreateVersion7().ToString("D");
    var body = new
    {
      requestKey,
      expectedRevision = readiness.ArchiveRevision,
      expectedReadinessDigest = readiness.ReadinessDigest,
      dispositionNotes = "Original blocked review.",
      acknowledgedActionItems = Array.Empty<string>()
    };
    using (var nullBody = await Post(client, csrf, f.ClientId, null))
      Assert.Equal(HttpStatusCode.BadRequest, nullBody.StatusCode);

    using (var unresolved = await Post(client, csrf, f.ClientId, body))
    {
      Assert.Equal(HttpStatusCode.Conflict, unresolved.StatusCode);
      using var error = JsonDocument.Parse(await unresolved.Content.ReadAsStringAsync());
      Assert.Equal(ErrorCodes.GenerationStale, error.RootElement.GetProperty("code").GetString());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var archived = await PracticeCrmService.SetClientArchivedAsync(db, actor,
        new(f.ClientId, true, long.Parse(readiness.ArchiveRevision, System.Globalization.CultureInfo.InvariantCulture)));
      Assert.True(archived.Succeeded, archived.Message);
    }

    using var terminal = await Post(client, csrf, f.ClientId, body);
    Assert.Equal(HttpStatusCode.OK, terminal.StatusCode);
    var resolved = await terminal.Content.ReadFromJsonAsync<ClientOffboardingCloseoutRecoveryResult>();
    Assert.NotNull(resolved);
    Assert.Equal(ClientOffboardingCloseoutRecoveryOutcomes.SupersededWithoutCommit, resolved.Outcome);
    Assert.Equal(f.ClientId, resolved.ClientId);
    Assert.Equal(manager.Id, resolved.ActorUserId);
    Assert.Equal(requestKey, resolved.RequestKey);
    Assert.Equal(readiness.ReadinessDigest, resolved.ReadinessDigest);
    Assert.Equal(readiness.ArchiveRevision, resolved.ExpectedArchiveRevision);
    Assert.Equal("1", resolved.CurrentArchiveRevision);
    Assert.Null(resolved.Receipt);
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await proof.ClientOffboardingRecords.Where(x => x.ClientId == f.ClientId).ToListAsync());
    Assert.Equal(1, await proof.ClientLifecycleEvents.CountAsync(x => x.ClientId == f.ClientId));
  }

  private static Task<HttpResponseMessage> Post(HttpClient client, string csrf, Guid clientId, object? body)
  {
    var request = new HttpRequestMessage(HttpMethod.Post,
      $"/api/ui/clients/{clientId}/offboarding-closeout/recovery") { Content = JsonContent.Create(body) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }
}
