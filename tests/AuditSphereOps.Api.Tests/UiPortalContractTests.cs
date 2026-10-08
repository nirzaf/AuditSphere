using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class UiPortalContractTests
{
  private static StandaloneApiApplicationFactory Factory(string connection, string subject, string tenant) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = connection, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = subject,
    ["DevelopmentIdentity:TenantId"] = tenant, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
  });
  private static async Task<string> SignInAsync(HttpClient client)
  {
    Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fportal")).StatusCode);
    var response = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return Uri.UnescapeDataString(response.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("XSRF-TOKEN=")).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }
  private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body, string? proof = null)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
    if (proof is not null) request.Headers.Add("X-XSRF-TOKEN", proof);
    return await client.SendAsync(request);
  }

  [Fact]
  public async Task PortalContracts_RequireCurrentParticipantAndCsrf_AndNeverReturnCapabilityInReads()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-PORTAL-API");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staff = PbcSeed.Actor(seed.Staff, "Staff");
      var created = await PbcService.CreateRequestAsync(db, staff, new(seed.EngagementId, "Bank statements", "TEST", "2026-01-01", "2026-12-31", "Bank evidence", "PDF", "Totals",
        seed.Client.Id, seed.Staff.Id, seed.Reviewer.Id, "2027-01-31", "Confidential", "Complete readable statements"));
      Assert.True(created.Succeeded, created.Message); requestId = created.Value;
      Assert.True((await PbcService.ChangeStateAsync(db, staff, new(requestId, PbcStates.Sent, 1))).Succeeded);
    }
    using var clientFactory = Factory(pg.ConnectionString, seed.Client.Subject, seed.Client.TenantId);
    using var client = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/portal")).StatusCode);
    var csrf = await SignInAsync(client);
    var workspace = await client.GetFromJsonAsync<JsonElement>("/api/ui/portal?page=0&pageSize=10");
    Assert.Equal(requestId, workspace.GetProperty("requests")[0].GetProperty("id").GetGuid());
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/portal?page=0&pageSize=20")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ui/portal/requests/{Guid.NewGuid()}")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, $"/api/ui/portal/requests/{requestId}/reply", new { body = "Ready for review" })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/api/ui/portal/requests/{requestId}/reply", new { body = "Ready for review" }, csrf)).StatusCode);
    var detail = await client.GetFromJsonAsync<JsonElement>($"/api/ui/portal/requests/{requestId}");
    Assert.Contains(detail.GetProperty("conversation").EnumerateArray(), x => x.GetProperty("body").GetString() == "Ready for review");
    using var started = await PostAsync(client, $"/api/ui/portal/requests/{requestId}/uploads", new { fileName = "statement.txt", contentType = "text/plain", byteCount = "1", sha256 = new string('a', 64) }, csrf);
    Assert.Equal(HttpStatusCode.OK, started.StatusCode);
    var receipt = await started.Content.ReadFromJsonAsync<JsonElement>();
    Assert.False(string.IsNullOrWhiteSpace(receipt.GetProperty("value").GetProperty("capability").GetString()));
    detail = await client.GetFromJsonAsync<JsonElement>($"/api/ui/portal/requests/{requestId}");
    Assert.Single(detail.GetProperty("uploads").EnumerateArray());
    Assert.DoesNotContain("capability", detail.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("stagedPath", detail.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, $"/api/ui/portal/requests/{requestId}/delegations", new { userId = seed.Admin.Id }, csrf)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ui/portal/accounting/packages/{Guid.NewGuid()}")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/portal/documents")).StatusCode);
    using var staffFactory = Factory(pg.ConnectionString, seed.Staff.Subject, seed.Staff.TenantId);
    using var staffClient = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(staffClient);
    Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.GetAsync("/api/ui/portal")).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x => x.Id == seed.Client.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Disabled, true).SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
      Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/ui/portal/requests/{requestId}")).StatusCode);
  }

  [Fact]
  public async Task PortalUploadResume_RotatesCapabilityForExactFileAndKeepsItOutOfReads()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-PORTAL-UPLOAD-RESUME");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staff = PbcSeed.Actor(seed.Staff, "Staff");
      var created = await PbcService.CreateRequestAsync(db, staff, new(seed.EngagementId, "Bank statements", "TEST", "2026-01-01", "2026-12-31", "Bank evidence", "PDF", "Totals",
        seed.Client.Id, seed.Staff.Id, seed.Reviewer.Id, "2027-01-31", "Confidential", "Complete readable statements"));
      Assert.True(created.Succeeded, created.Message); requestId = created.Value;
      Assert.True((await PbcService.ChangeStateAsync(db, staff, new(requestId, PbcStates.Sent, 1))).Succeeded);
    }

    var bytes = "exact synthetic portal resume bytes"u8.ToArray();
    var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    using var clientFactory = Factory(pg.ConnectionString, seed.Client.Subject, seed.Client.TenantId);
    using var client = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignInAsync(client);
    using var started = await PostAsync(client, $"/api/ui/portal/requests/{requestId}/uploads", new
    {
      fileName = "resume-evidence.txt", contentType = "text/plain", byteCount = bytes.Length.ToString(), sha256 = hash
    }, csrf);
    Assert.Equal(HttpStatusCode.OK, started.StatusCode);
    var startBody = await started.Content.ReadFromJsonAsync<JsonElement>();
    var startReceipt = startBody.GetProperty("value");
    var uploadId = startReceipt.GetProperty("uploadIntentId").GetGuid();
    var oldCapability = startReceipt.GetProperty("capability").GetString();
    Assert.False(string.IsNullOrWhiteSpace(oldCapability));

    var resumeUrl = $"/api/ui/portal/requests/{requestId}/uploads/{uploadId}/resume";
    var resumeInput = new { fileName = "resume-evidence.txt", byteCount = bytes.Length.ToString(), sha256 = hash };
    using (var noCsrf = await PostAsync(client, resumeUrl, resumeInput))
      Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
    using (var mismatch = await PostAsync(client, resumeUrl,
      new { fileName = "resume-evidence.txt", byteCount = bytes.Length.ToString(), sha256 = new string('0', 64) }, csrf))
      Assert.NotEqual(HttpStatusCode.OK, mismatch.StatusCode);

    using var resumed = await PostAsync(client, resumeUrl, resumeInput, csrf);
    Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
    Assert.Equal("no-store", resumed.Headers.CacheControl?.ToString());
    var resumeBody = await resumed.Content.ReadFromJsonAsync<JsonElement>();
    var resume = resumeBody.GetProperty("value");
    Assert.Equal(uploadId, resume.GetProperty("uploadIntentId").GetGuid());
    Assert.Equal(requestId, resume.GetProperty("pbcRequestId").GetGuid());
    Assert.Equal(0, resume.GetProperty("receivedByteCount").GetInt64());
    Assert.Equal(0, resume.GetProperty("nextChunkIndex").GetInt32());
    var newCapability = resume.GetProperty("capability").GetString();
    Assert.False(string.IsNullOrWhiteSpace(newCapability));
    Assert.NotEqual(oldCapability, newCapability);

    var actor = PbcSeed.Actor(seed.Client, "ClientUser");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var stale = await PbcService.RecordChunkAsync(db, actor,
        new RecordPbcUploadChunkRequest(uploadId, 0, 0, bytes.Length, hash, oldCapability!, "synthetic-old-capability.part"));
      Assert.False(stale.Succeeded);
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied, stale.ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var current = await PbcService.RecordChunkAsync(db, actor,
        new RecordPbcUploadChunkRequest(uploadId, 0, 0, bytes.Length, hash, newCapability!, "synthetic-resumed-chunk.part"));
      Assert.True(current.Succeeded, current.ErrorCode);
      Assert.Equal(bytes.Length, current.Value!.ReceivedByteCount);
    }

    var detail = await client.GetFromJsonAsync<JsonElement>($"/api/ui/portal/requests/{requestId}");
    Assert.DoesNotContain("capability", detail.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("stagedPath", detail.ToString(), StringComparison.OrdinalIgnoreCase);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var persisted = await verify.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == uploadId);
    Assert.Equal(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(newCapability!), persisted.CapabilityHash);
    Assert.NotEqual(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(oldCapability!), persisted.CapabilityHash);
    Assert.Single(await verify.PbcUploadChunks.AsNoTracking().Where(x => x.PbcUploadIntentId == uploadId).ToListAsync());
  }
}
