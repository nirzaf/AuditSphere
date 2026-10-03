using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Api.Tests;
public sealed class EngagementActivationReviewApiTests
{
  [Fact]
  public async Task CookieCsrfPartnerScopeExactReviewIdempotencyAndRevocationAreServerOwned()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-ACTIVATION-REVIEW");var f=await PbcSeed.SeedAsync(pg);var foreign=await PbcSeed.SeedAsync(pg);
    await using(var setup=new AuditSphereDbContext(pg.Options)){await EngagementActivationReviewSeed.PopulateAsync(setup,f);}
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient(new(){AllowAutoRedirect=false});var url="/api/ui/engagements/"+f.EngagementId+"/activation-review";
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(url)).StatusCode);var csrf=await SignIn(c);
    using var stateResponse=await c.GetAsync(url);Assert.True(stateResponse.Headers.CacheControl!.NoStore);var s=(await stateResponse.Content.ReadFromJsonAsync<EngagementActivationState>())!;Assert.True(s.Eligible);
    var r=new EngagementActivationRequest(Guid.NewGuid(),s.ReviewBasis);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"forged",url+"/preview",r)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,url,r)).StatusCode);
    using var preview=await Post(c,csrf,url+"/preview",r);Assert.Equal(HttpStatusCode.OK,preview.StatusCode);
    var p=JsonDocument.Parse(await preview.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Deserialize<EngagementActivationPreview>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    var reviewed=r with{Reviewed=true,ExpectedRequestHash=p.RequestHash};
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,url,reviewed with{ExpectedRequestHash=new string('a',64)})).StatusCode);
    using var created=await Post(c,csrf,url,reviewed);Assert.Equal(HttpStatusCode.OK,created.StatusCode);Assert.True(created.Headers.CacheControl!.NoStore);
    using var replay=await Post(c,csrf,url,reviewed);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Equal(await created.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
    var receipt=JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Deserialize<EngagementActivationReceipt>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    Assert.Equal(receipt,(await c.GetFromJsonAsync<EngagementActivationLookup>(url+"/receipts/"+r.RequestId+"?requestHash="+p.RequestHash))!.Receipt);
    Assert.False((await c.GetFromJsonAsync<EngagementActivationLookup>(url+"/receipts/"+Guid.NewGuid()+"?requestHash="+p.RequestHash))!.Found);
    using var hidden=await c.GetAsync("/api/ui/engagements/"+foreign.EngagementId+"/activation-review");using var guessed=await c.GetAsync("/api/ui/engagements/"+Guid.NewGuid()+"/activation-review");
    Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);Assert.Equal(await hidden.Content.ReadAsStringAsync(),await guessed.Content.ReadAsStringAsync());
    using var adminFactory=Factory(pg,f.Admin);using var admin=adminFactory.CreateClient();await SignIn(admin);Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync(url)).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options)){Assert.Single(await db.EngagementActivations.ToListAsync());
      await db.RoleGrants.Where(g=>g.UserId==f.Staff.Id&&g.Role=="Partner").ExecuteUpdateAsync(x=>x.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow));}
    Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(url)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(url+"/receipts/"+r.RequestId+"?requestHash="+p.RequestHash)).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options)){await db.Users.Where(u=>u.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));}
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(url)).StatusCode);
  }
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AuditSphereOps.Domain.Security.AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:Subject"]=u.Subject,["DevelopmentIdentity:TenantId"]=u.TenantId,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var s=await c.GetAsync("/api/ui/session");return Uri.UnescapeDataString(s.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,object input){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(input)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
}
