using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class TenantSetupMetadataApiTests
{
  [Fact]
  public async Task ExactReviewCsrfReceiptRecoveryRevocationAndRetainedMigrationAreEnforced()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TENANT-SETUP-RECEIPT");
    var f = await PbcSeed.SeedAsync(pg);
    var tenant = Guid.NewGuid().ToString("D"); f.Admin.TenantId = tenant; var draftId = Guid.CreateVersion7(); var sessionId = Guid.CreateVersion7();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.TenantId, tenant));
      var migrations = db.Database.GetMigrations().ToArray();
      var index = Array.FindIndex(migrations, x => x.EndsWith("_NativeTenantSetupMetadataReceipts", StringComparison.Ordinal));
      Assert.True(index > 0);
      await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
      await db.GetService<IMigrator>().MigrateAsync();
      var now = DateTimeOffset.UtcNow;
      db.Microsoft365SetupSessions.Add(new() { Id=sessionId, FirmId=f.FirmId, InstallationId="synthetic-api-setup",
        BootstrapProofHash=new string('a',64), CapabilityHash=new string('b',64), ClaimedByUserId=f.Admin.Id,
        ClaimedAt=now, ExpiresAt=now.AddHours(1) });
      db.Microsoft365SetupDrafts.Add(new() { Id=draftId, FirmId=f.FirmId, SetupSessionId=sessionId,
        ExpectedTenantId=tenant, State=Microsoft365RevisionStates.Draft, CreatedAt=now, UpdatedAt=now });
      await db.SaveChangesAsync();
    }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string,string?> {
      ["ConnectionStrings:AuditSphere"]=pg.ConnectionString, ["Application:FirmId"]=f.FirmId.ToString(),
      ["DevelopmentIdentity:Enabled"]="true",
      ["DevelopmentIdentity:Subject"]=f.Admin.Subject, ["DevelopmentIdentity:TenantId"]=f.Admin.TenantId,
      ["Application:AllowSimulationAdapters"]="true", ["ExternalEffects:Enabled"]="false" });
    using var c=factory.CreateClient(new() { AllowAutoRedirect=false });
    const string root="/api/ui/administration/microsoft365/setup";
    var request=new TenantSetupEditRequest(Guid.NewGuid(),draftId,"1",new("Synthetic API setup","CONFIGURED","NOT_CONFIGURED"));
    Assert.Equal(HttpStatusCode.Unauthorized,(await Post(c,"forged",root+"/preview",request)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/ui/administration/runtime")).StatusCode);
    await c.GetAsync("/auth/sign-in"); using var session=await c.GetAsync("/api/ui/session");
    var csrf=Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
    using var runtime=await c.GetAsync("/api/ui/administration/runtime");
    Assert.Equal(HttpStatusCode.OK,runtime.StatusCode);Assert.True(runtime.Headers.CacheControl!.NoStore);
    var runtimeStatus=await runtime.Content.ReadFromJsonAsync<AuditSphereOps.Application.Security.AdministrationRuntimeStatus>();
    Assert.Equal(f.FirmId,runtimeStatus!.FirmId);Assert.Equal("Test",runtimeStatus.Environment);Assert.False(runtimeStatus.ExternalEffectsEnabled);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"forged",root+"/preview",request)).StatusCode);
    using var preview=await Post(c,csrf,root+"/preview",request);
    Assert.Equal(HttpStatusCode.OK,preview.StatusCode); Assert.True(preview.Headers.CacheControl!.NoStore);
    var p=JsonDocument.Parse(await preview.Content.ReadAsStringAsync()).RootElement.GetProperty("value")
      .Deserialize<TenantSetupPreview>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,root+"/commands",request)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,csrf,root+"/preview",request with {DraftId=Guid.NewGuid()})).StatusCode);
    request=request with {Reviewed=true,RequestHash=p.RequestHash,ReviewBasis=p.ReviewBasis};
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,root+"/commands",request with {Fields=request.Fields with {MailState="NOT_CONFIGURED"}})).StatusCode);
    using var saved=await Post(c,csrf,root+"/commands",request); Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
    using var replay=await Post(c,csrf,root+"/commands",request);
    Assert.Equal(await saved.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
    var lookup=root+"/receipts/"+request.RequestId+"?requestHash="+p.RequestHash;
    using var receipt=await c.GetAsync(lookup); Assert.True(receipt.Headers.CacheControl!.NoStore);
    Assert.True((await receipt.Content.ReadFromJsonAsync<TenantSetupReceiptLookup>())!.Found);
    await using (var db=new AuditSphereDbContext(pg.Options))
    {
      Assert.Single(await db.Microsoft365AdministrationEvents.Where(x=>x.SetupRequestId==request.RequestId).ToListAsync());
      await using (var connection=new NpgsqlConnection(pg.ConnectionString))
      {
        await connection.OpenAsync();
        await using var mutation=new NpgsqlCommand("UPDATE m365_administration_events SET reason='Altered' WHERE setup_request_id=@id",connection);
        mutation.Parameters.AddWithValue("id",request.RequestId);
        var refusal=await Assert.ThrowsAsync<PostgresException>(()=>mutation.ExecuteNonQueryAsync());
        Assert.Equal("55000",refusal.SqlState);
      }
      var migrations=db.Database.GetMigrations().ToArray();
      var index=Array.FindIndex(migrations,x=>x.EndsWith("_NativeTenantSetupMetadataReceipts",StringComparison.Ordinal));
      await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[index-1]));
      Assert.Single(await db.Microsoft365AdministrationEvents.Where(x=>x.SetupRequestId==request.RequestId).ToListAsync());
      await db.Users.Where(x=>x.Id==f.Admin.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    }
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(lookup)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/ui/administration/runtime")).StatusCode);
  }
  private static Task<HttpResponseMessage> Post(HttpClient client,string csrf,string path,object value)
  { var request=new HttpRequestMessage(HttpMethod.Post,path) {Content=JsonContent.Create(value)}; request.Headers.Add("X-XSRF-TOKEN",csrf); return client.SendAsync(request); }
}
