using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;
public sealed class ResourcePlanningReceiptApiTests
{
  [Fact]
  public async Task PlanningCsrfActorRecoveryAndRetainedMigrationEvidenceAreEnforced()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-RESOURCE-RECEIPTS");var f=await PbcSeed.SeedAsync(pg);
    await using(var db=new AuditSphereDbContext(pg.Options)) {
      var migrations=db.Database.GetMigrations().ToArray();
      Assert.Contains(migrations,x=>x.EndsWith("_NativeResourcePlanningReceipts",StringComparison.Ordinal));
    }
    using var factory=new StandaloneApiApplicationFactory(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:Subject"]=f.Admin.Subject,["DevelopmentIdentity:TenantId"]=f.Admin.TenantId,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
    using var c=factory.CreateClient(new(){AllowAutoRedirect=false});const string url="/api/ui/practice/resources";
    var request=new ResourcePlanningCommandRequest(Guid.NewGuid(),new("CERTIFICATION",f.Staff.Id,Name:"Synthetic API qualification"));
    Assert.Equal(HttpStatusCode.Unauthorized,(await Post(c,"forged",url+"/preview",request)).StatusCode);
    await c.GetAsync("/auth/sign-in");using var session=await c.GetAsync("/api/ui/session");var csrf=Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"forged",url+"/preview",request)).StatusCode);
    using var preview=await Post(c,csrf,url+"/preview",request);Assert.Equal(HttpStatusCode.OK,preview.StatusCode);Assert.True(preview.Headers.CacheControl!.NoStore);
    var p=JsonDocument.Parse(await preview.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Deserialize<ResourcePlanningPreview>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,url+"/commands",request)).StatusCode);
    request=request with{Fields=p.Fields,Reviewed=true,RequestHash=p.RequestHash,ReviewBasis=p.ReviewBasis};
    using var first=await Post(c,csrf,url+"/commands",request);Assert.Equal(HttpStatusCode.OK,first.StatusCode);Assert.True(first.Headers.CacheControl!.NoStore);
    using var replay=await Post(c,csrf,url+"/commands",request);Assert.Equal(await first.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
    var lookupUrl=url+"/receipts/"+p.RequestId+"?requestHash="+p.RequestHash;using var lookup=await c.GetAsync(lookupUrl);Assert.True(lookup.Headers.CacheControl!.NoStore);Assert.True((await lookup.Content.ReadFromJsonAsync<ResourcePlanningReceiptLookup>())!.Found);
    Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(url+"/receipts/"+p.RequestId+"?requestHash="+new string('a',64))).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options)) {
      await PermanentFileFreezeMigrationAssertions.AssertDowngradeBlockedAsync(db,"_NativeResourcePlanningReceipts");
      Assert.Single(await db.ResourcePlanningReceipts.ToListAsync());Assert.Single(await db.StaffCertifications.ToListAsync());
      await db.Users.Where(x=>x.Id==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));
    }
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(lookupUrl)).StatusCode);
  }
  [Fact]
  public async Task CertificationReviewUsesCsrfExactSourceAndConflictResponses()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CERT-REVIEW");
    var f = await PbcSeed.SeedAsync(pg);
    var source = new AuditSphereOps.Domain.Practice.StaffCertification
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, UserId = f.Staff.Id, Name = "Synthetic qualification",
      RecordedByUserId = f.Reviewer.Id, RecordedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };
    await using (var db = new AuditSphereDbContext(pg.Options)) { db.StaffCertifications.Add(source); await db.SaveChangesAsync(); }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Admin.Subject, ["DevelopmentIdentity:TenantId"] = f.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    using var c = factory.CreateClient(new() { AllowAutoRedirect = false });
    var read = $"/api/ui/practice/resources/certifications/{source.Id}/review";
    const string write = "/api/ui/practice/resources/certifications/reviews";
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(read)).StatusCode);
    await c.GetAsync("/auth/sign-in"); using var session = await c.GetAsync("/api/ui/session");
    var csrf = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
    using var initial = await c.GetAsync(read); Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
    var workspace = (await initial.Content.ReadFromJsonAsync<StaffCertificationReviewWorkspace>())!;
    Assert.False(workspace.VerifiedForCurrentSource); Assert.True(workspace.CanReview);
    var input = new AuditSphereOps.Api.Ui.UiEndpoints.CertificationReviewInput(source.Id, Guid.NewGuid(), workspace.SourceDigest,
      "0", "VERIFIED", "Synthetic evidence", "Independent verification", true);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "forged", write, input)).StatusCode);
    using var first = await Post(c, csrf, write, input); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    using var retry = await Post(c, csrf, write, input); Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.Conflict, (await Post(c, csrf, write, input with { Reason = "Changed" })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(c, csrf, write, input with { RequestId = Guid.NewGuid() })).StatusCode);
    var verified = (await c.GetFromJsonAsync<StaffCertificationReviewWorkspace>(read))!;
    Assert.True(verified.VerifiedForCurrentSource); Assert.Equal(1, verified.Revision);
  }

  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,object value) {var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(value)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
}
