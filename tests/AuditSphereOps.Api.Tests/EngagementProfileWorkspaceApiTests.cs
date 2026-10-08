using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class EngagementProfileWorkspaceApiTests
{
  [Fact]
  public async Task ScopedHistoryMetadataBoundsNoStoreAndEpochAreCheckedByStandaloneApi()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ENGAGEMENT-PROFILE-WORKSPACE-API");
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options)) { await EngagementProfileWorkspaceSeed.PopulateAsync(db,f,105); }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string,string?> {
      ["ConnectionStrings:AuditSphere"]=pg.ConnectionString, ["DevelopmentIdentity:Enabled"]="true",
      ["DevelopmentIdentity:Subject"]=f.Staff.Subject, ["DevelopmentIdentity:TenantId"]=f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"]="true", ["ExternalEffects:Enabled"]="false" });
    using var c = factory.CreateClient(new(){AllowAutoRedirect=false}); var path="/api/ui/engagements/"+f.EngagementId;
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(path)).StatusCode); await c.GetAsync("/auth/sign-in");
    var retained=(await c.GetFromJsonAsync<EngagementWorkspace>(path))!; Assert.Equal(100,retained.Holds.Count);
    using var response=await c.GetAsync(path+"?holdPage=2&holdPageSize=50");
    Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
    var v=(await response.Content.ReadFromJsonAsync<EngagementWorkspace>())!;
    Assert.Equal(5,v.Holds.Count); Assert.Equal(new EngagementHoldMetrics(105,52,53),v.HoldMetrics);
    Assert.Equal(new EngagementWorkspacePaging(2,50),v.Paging); Assert.False(v.CanViewClientProfile); Assert.False(v.CanPrepareAccounting);
    Assert.Equal("9007199254740993",v.Generation); Assert.Equal("Synthetic annual audit profile",v.ServiceProfileId);
    foreach(var q in new[]{"holdPage=-1","holdPage=10001","holdPageSize=100","holdPageSize=bad","holdPageSize=0"})
      Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(path+"?"+q)).StatusCode);
    using var hidden=await c.GetAsync("/api/ui/engagements/"+foreign.EngagementId);
    using var guessed=await c.GetAsync("/api/ui/engagements/"+Guid.NewGuid());
    Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode); Assert.Equal(await guessed.Content.ReadAsStringAsync(),await hidden.Content.ReadAsStringAsync());
    Assert.Empty((await c.GetFromJsonAsync<EngagementWorkspace>(path+"?holdPage=10000"))!.Holds);
    await using (var db=new AuditSphereDbContext(pg.Options)) {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"AccountingPreparer",clientId:f.ClientId)); await db.SaveChangesAsync(); }
    Assert.True((await c.GetFromJsonAsync<EngagementWorkspace>(path))!.CanPrepareAccounting);
    await using(var db=new AuditSphereDbContext(pg.Options)) {
      await db.RoleGrants.Where(g=>g.UserId==f.Staff.Id).ExecuteUpdateAsync(s=>s.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow)); }
    Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(path)).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options)) {
      await db.Users.Where(u=>u.Id==f.Staff.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1)); }
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(path)).StatusCode);
  }
}
