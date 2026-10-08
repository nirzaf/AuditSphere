using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ClientContactCreationApiTests
{
  [Fact]
  public async Task CookieCsrfReviewedHashRecoveryAndCurrentAuthorityFenceContactCreation()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("CLIENT-CONTACT-CREATION-API");var f=await PbcSeed.SeedAsync(pg);var foreign=await PbcSeed.SeedAsync(pg);
    using var factory=new StandaloneApiApplicationFactory(new Dictionary<string,string?> {
      ["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",
      ["DevelopmentIdentity:Subject"]=f.Admin.Subject,["DevelopmentIdentity:TenantId"]=f.Admin.TenantId,
      ["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false" });
    using var c=factory.CreateClient(new(){AllowAutoRedirect=false});var url="/api/ui/clients/"+f.ClientId+"/contact-creation";
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(url)).StatusCode);await c.GetAsync("/auth/sign-in");
    using var session=await c.GetAsync("/api/ui/session");var csrf=Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
    using var stateResponse=await c.GetAsync(url);Assert.True(stateResponse.Headers.CacheControl!.NoStore);
    var state=(await stateResponse.Content.ReadFromJsonAsync<ContactCreationState>())!;
    var r=new ContactCreationRequest(Guid.NewGuid(),state.ReviewBasis,new("Synthetic contact","contact@example.test","Finance",true));
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"forged",url+"/preview",r)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,url,r)).StatusCode);
    using var previewResponse=await Post(c,csrf,url+"/preview",r);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);
    var p=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Deserialize<ContactCreationPreview>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    var reviewed=r with{Reviewed=true,ExpectedRequestHash=p.RequestHash};
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,url,reviewed with{Fields=r.Fields with{Name="Changed"}})).StatusCode);
    using var created=await Post(c,csrf,url,reviewed);Assert.Equal(HttpStatusCode.OK,created.StatusCode);Assert.True(created.Headers.CacheControl!.NoStore);
    var receipt=JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Deserialize<ContactCreationReceipt>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    using var retry=await Post(c,csrf,url,reviewed);Assert.Equal(HttpStatusCode.OK,retry.StatusCode);
    Assert.Equal(await created.Content.ReadAsStringAsync(),await retry.Content.ReadAsStringAsync());
    var lookup=(await c.GetFromJsonAsync<ContactCreationLookup>(url+"/receipts/"+r.RequestId+"?requestHash="+p.RequestHash))!;
    Assert.Equal(receipt,lookup.Receipt);
    Assert.False((await c.GetFromJsonAsync<ContactCreationLookup>(url+"/receipts/"+Guid.NewGuid()+"?requestHash="+p.RequestHash))!.Found);
    using var guessed=await c.GetAsync("/api/ui/clients/"+Guid.NewGuid()+"/contact-creation");
    using var wrongFirm=await c.GetAsync("/api/ui/clients/"+foreign.ClientId+"/contact-creation");
    Assert.Equal(HttpStatusCode.Forbidden,wrongFirm.StatusCode);Assert.Equal(await guessed.Content.ReadAsStringAsync(),await wrongFirm.Content.ReadAsStringAsync());
    await using(var db=new AuditSphereDbContext(pg.Options)) {
      Assert.Single(await db.ClientContacts.ToListAsync());Assert.Single(await db.ClientContactCreations.ToListAsync());
      await db.RoleGrants.Where(g=>g.UserId==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow)); }
    Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(url)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(url+"/receipts/"+r.RequestId+"?requestHash="+p.RequestHash)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,csrf,url,reviewed)).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options)){await db.Users.Where(u=>u.Id==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));}
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(url)).StatusCode);
  }
  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,object input){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(input)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
}
