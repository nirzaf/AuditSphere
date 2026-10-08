using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditSphereOps.Api.Tests;

public sealed class WorkspaceProvisioningApiTests
{
  private const string Prefix = "/api/ui/administration/microsoft365/";
  [Fact]
  public async Task ExactReviewedTargets_CreateOnce_VerifySeparately_AndRetainActorReasonAndScopeEvidence()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKSPACE-PROVISION"); var f = await Seed(pg); var provider = new FakeProvider();
    using var factory = Factory(pg,f,provider); using var client = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(client);
    Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync(Prefix+"workspaces/clients/provision",new { targetId=f.ClientId,reviewed=true })).StatusCode);
    var before = await Review(client,f.ClientId,false);
    Assert.True(before.GetProperty("eligible").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest,(await Provision(client,csrf,f.ClientId,false,before,reviewed:false)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Provision(client,csrf,f.ClientId,false,before,reason:" ")).StatusCode);
    Assert.Equal(0,provider.Calls);
    var result = await Value(await Provision(client,csrf,f.ClientId,false,before)); Assert.Equal("READY",result.GetProperty("state").GetString());
    var calls = provider.Calls; var after = await Review(client,f.ClientId,false);
    Assert.Equal(HttpStatusCode.Conflict,(await Provision(client,csrf,f.ClientId,false,before)).StatusCode);
    await Value(await Provision(client,csrf,f.ClientId,false,after)); Assert.Equal(calls,provider.Calls);
    var review = await Review(client,f.EngagementId,true); Assert.True(review.GetProperty("eligible").GetBoolean());
    var verified = await Value(await Provision(client,csrf,f.EngagementId,true,review)); Assert.Equal("VERIFIED",verified.GetProperty("state").GetString());
    var folderCount = provider.Folders.Count;
    await Value(await Provision(client,csrf,f.EngagementId,true,await Review(client,f.EngagementId,true))); Assert.Equal(folderCount,provider.Folders.Count);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Single(await db.RepositoryBindings.Where(x=>x.FirmId==f.FirmId).ToListAsync());
    Assert.Equal("VERIFIED",(await db.IntegrationCapabilities.SingleAsync()).HealthStatus);
    var evidence=await db.Microsoft365AdministrationEvents.Where(x=>x.Operation=="PBC_REPOSITORY_VERIFIED").ToListAsync();
    Assert.All(evidence,x=>{Assert.Equal(f.Admin.Id,x.ActorUserId);Assert.Equal("Reviewed API setup",x.Reason);Assert.Contains(f.EngagementId.ToString(),x.RoleScopeChange);});
    var listed=await client.GetStringAsync(Prefix+"workspaces?page=0"); Assert.DoesNotContain("private-synthetic-slot",listed);
    Assert.Contains("VERIFIED",listed); Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(Prefix+"workspaces?page=-1")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(Prefix+"workspaces?page=100001")).StatusCode);
    provider.TestPass=false;
    var failed=await Value(await Provision(client,csrf,f.EngagementId,true,await Review(client,f.EngagementId,true)));
    Assert.Equal("FAILED",failed.GetProperty("state").GetString());
    for(var n=0;n<26;n++)
    {
      var clientId=Guid.NewGuid();var decisionId=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
      db.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient{Id=clientId,FirmId=f.FirmId,LegalName=$"Synthetic paged client {n:00}",CreatedAt=now});
      db.AcceptanceDecisions.Add(new AcceptanceDecision{Id=decisionId,FirmId=f.FirmId,PracticeClientId=clientId,Decision="Accepted",Rationale="Synthetic bounded page fixture",ServiceRoute="FinancialStatementAudit",EvaluationTemplateVersion="synthetic-v1",EvaluationSnapshotDigest=new string('a',64),DecidedByUserId=f.Admin.Id,DecidedAt=now});
      db.ClientWorkspaces.Add(new ClientWorkspace{Id=Guid.NewGuid(),FirmId=f.FirmId,PracticeClientId=clientId,AcceptanceDecisionId=decisionId,LogicalKey="workspace/"+clientId,State=ClientWorkspaceStates.WaitingForIntegration,CreatedAt=now});
    }
    await db.SaveChangesAsync();
    using var first=JsonDocument.Parse(await client.GetStringAsync(Prefix+"workspaces?page=0"));
    using var second=JsonDocument.Parse(await client.GetStringAsync(Prefix+"workspaces?page=1"));
    Assert.Equal(25,first.RootElement.GetProperty("workspace").GetProperty("clients").GetArrayLength());
    Assert.True(first.RootElement.GetProperty("workspace").GetProperty("hasMore").GetBoolean());
    Assert.Equal(2,second.RootElement.GetProperty("workspace").GetProperty("clients").GetArrayLength());
    Assert.False(second.RootElement.GetProperty("workspace").GetProperty("hasMore").GetBoolean());
  }

  [Fact]
  public async Task ConfigurationChangedDuringMicrosoftCall_OrRevokedAdministrator_PreventsLocalPublication()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKSPACE-FENCES"); var f=await Seed(pg);var provider=new FakeProvider();
    using var factory=Factory(pg,f,provider);using var client=factory.CreateClient(new(){AllowAutoRedirect=false});var csrf=await SignIn(client);
    var review=await Review(client,f.ClientId,false);
    provider.Hook=async()=>{await using var changed=new AuditSphereDbContext(pg.Options);(await changed.FirmWorkspaceConfigurations.SingleAsync()).RootFolderId="changed-root";await changed.SaveChangesAsync();};
    Assert.Equal(HttpStatusCode.Conflict,(await Provision(client,csrf,f.ClientId,false,review)).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options)){Assert.Null((await db.ClientWorkspaces.SingleAsync()).RemoteItemId);Assert.Empty(await db.Microsoft365AdministrationEvents.ToListAsync());}
    review=await Review(client,f.ClientId,false);
    provider.Hook=async()=>{await using var changed=new AuditSphereDbContext(pg.Options);(await changed.Users.SingleAsync(x=>x.Id==f.Admin.Id)).SessionEpoch++;await changed.SaveChangesAsync();};
    Assert.Equal(HttpStatusCode.Forbidden,(await Provision(client,csrf,f.ClientId,false,review)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(Prefix+"workspaces")).StatusCode);
    await using var final=new AuditSphereDbContext(pg.Options);Assert.Null((await final.ClientWorkspaces.SingleAsync()).RemoteItemId);
  }

  [Fact]
  public async Task AdministratorCheckedFirst_UnknownIdsAndOtherFirmsDenied_DedicatedPendingSiteNeverFallsBack()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-WORKSPACE-ISOLATION");var f=await Seed(pg);var other=await Seed(pg);var provider=new FakeProvider();
    using var staffFactory=Factory(pg,f,provider,staff:true);using var staff=staffFactory.CreateClient(new(){AllowAutoRedirect=false});var csrf=await SignIn(staff);
    foreach(var path in new[]{"workspaces?page=invalid","client-sites","workspaces/clients/"+f.ClientId+"/review"})Assert.Equal(HttpStatusCode.Forbidden,(await staff.GetAsync(Prefix+path)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(staff,"workspaces/clients/provision",csrf,new{reviewed=true})).StatusCode);
    using var factory=Factory(pg,f,provider);using var admin=factory.CreateClient(new(){AllowAutoRedirect=false});csrf=await SignIn(admin);
    foreach(var id in new[]{other.ClientId,Guid.NewGuid()})Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync(Prefix+"workspaces/clients/"+id+"/review")).StatusCode);
    var raw=await admin.GetStringAsync(Prefix+"workspaces");Assert.DoesNotContain(other.ClientId.ToString(),raw);
    await using(var db=new AuditSphereDbContext(pg.Options)){
      db.ClientSharePointSites.Add(new ClientSharePointSite{Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,TenantId=f.Admin.TenantId,
        ConnectionRevisionId=await db.Microsoft365ConnectionRevisions.Where(x=>x.FirmId==f.FirmId).Select(x=>x.Id).SingleAsync(),RequestedByUserId=f.Admin.Id,
        RequestedUrl="https://synthetic.sharepoint.com/sites/pending",Title="Synthetic client",OwnershipMarker="synthetic-owned",State="REQUESTED",CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();}
    var pending=await Review(admin,f.ClientId,false);Assert.False(pending.GetProperty("eligible").GetBoolean());Assert.Contains("fallback is refused",pending.GetProperty("requiredAction").GetString());
    Assert.Equal(HttpStatusCode.BadRequest,(await Provision(admin,csrf,f.ClientId,false,pending)).StatusCode);Assert.Equal(0,provider.Calls);
    await using(var db=new AuditSphereDbContext(pg.Options)){var site=await db.ClientSharePointSites.SingleAsync(x=>x.FirmId==f.FirmId);site.State="READY";site.SiteId="site";site.DriveId="drive";site.RootItemId="root";await db.SaveChangesAsync();}
    using var unverified=JsonDocument.Parse(await admin.GetStringAsync(Prefix+"client-sites"));
    Assert.Equal("BLOCKED_EXTERNAL",unverified.RootElement[0].GetProperty("state").GetString());
    Assert.Contains("site verification",unverified.RootElement[0].GetProperty("requiredAction").GetString());
  }

  private static async Task<PbcSeed.Fixture> Seed(OwnedPostgresDatabase pg)
  {
    var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);var now=DateTimeOffset.UtcNow;
    var admin=await db.Users.SingleAsync(x=>x.Id==f.Admin.Id);f.Admin.TenantId=admin.TenantId=Guid.NewGuid().ToString();f.Admin.Subject=admin.Subject=Guid.NewGuid().ToString();
    Guid decision=Guid.NewGuid(), connection=Guid.NewGuid(), template=Guid.NewGuid();
    var engagement=await db.Engagements.SingleAsync(x=>x.Id==f.EngagementId);engagement.PeriodEnd="2026-12-31";engagement.ServiceRoute="FinancialStatementAudit";
    db.AcceptanceDecisions.Add(new AcceptanceDecision{Id=decision,FirmId=f.FirmId,PracticeClientId=f.ClientId,Decision="Accepted",Rationale="Synthetic accepted fixture",ServiceRoute=engagement.ServiceRoute,EvaluationTemplateVersion="synthetic-v1",EvaluationSnapshotDigest=new string('a',64),DecidedByUserId=admin.Id,DecidedAt=now});
    db.ClientWorkspaces.Add(new ClientWorkspace{Id=Guid.NewGuid(),FirmId=f.FirmId,PracticeClientId=f.ClientId,AcceptanceDecisionId=decision,LogicalKey="workspace/"+f.ClientId,State=ClientWorkspaceStates.WaitingForIntegration,CreatedAt=now});
    db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision{Id=connection,FirmId=f.FirmId,TenantId=admin.TenantId,RuntimeCredentialReference="private-synthetic-slot",LoginClientIdReference="slot:login",State="ACTIVE",ConsentState="VERIFIED",CreatedAt=now});
    foreach(var (purpose,id) in new[]{("CLIENT_WORKSPACE",template),("ENGAGEMENT_WORKSPACE",Guid.NewGuid())})db.FolderTemplateVersions.Add(new FolderTemplateVersion{Id=id,FirmId=f.FirmId,Purpose=purpose,Version=1,ManifestJson=Microsoft365ConfigurationService.SteManifest(purpose),ManifestDigest=new string('b',64),CreatedAt=now,ApprovedAt=now});
    db.FirmWorkspaceConfigurations.Add(new FirmWorkspaceConfiguration{Id=Guid.NewGuid(),FirmId=f.FirmId,ConnectionRevisionId=connection,FolderTemplateVersionId=template,TenantId=admin.TenantId,SiteId="synthetic-site",DriveId="synthetic-drive",RootFolderId="synthetic-root",AccessProfile="APP_MEDIATED",DisplayUrl="https://synthetic.sharepoint.com/sites/working",CreatedAt=now});
    await db.SaveChangesAsync();return f;
  }
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,PbcSeed.Fixture f,FakeProvider provider,bool staff=false)=>new(new Dictionary<string,string?>{
    ["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=staff?f.Staff.TenantId:f.Admin.TenantId,["DevelopmentIdentity:Subject"]=staff?f.Staff.Subject:f.Admin.Subject,
    ["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"},s=>s.AddSingleton<ISelectedSiteWorkspaceProvisioner>(provider));
  private sealed class FakeProvider:ISelectedSiteWorkspaceProvisioner
  {
    public bool IsConfigured=>true;public int Calls{get;private set;}public bool TestPass{get;set;}=true;public Func<Task>? Hook{get;set;}
    public Dictionary<string,RemoteFolder> Folders{get;}=[];
    public async Task<RemoteFolder> EnsureFolderAsync(SelectedSiteLocation location,string parent,string name,CancellationToken ct){Calls++;if(Hook is {} hook){Hook=null;await hook();}var key=location.DriveId+"/"+parent+"/"+name;
      if(!Folders.TryGetValue(key,out var folder))Folders[key]=folder=new("synthetic-folder-"+Folders.Count,name);return folder;}
    public Task<RemoteCapabilityTest> TestReadWriteAsync(SelectedSiteLocation location,string folder,CancellationToken ct)=>Task.FromResult(new RemoteCapabilityTest(TestPass,TestPass,true,"synthetic-provider-test"));
  }
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var response=await c.GetAsync("/api/ui/session");Assert.Equal(HttpStatusCode.OK,response.StatusCode);return Uri.UnescapeDataString(response.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static Task<HttpResponseMessage> Post(HttpClient c,string path,string csrf,object body){var r=new HttpRequestMessage(HttpMethod.Post,Prefix+path){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
  private static Task<HttpResponseMessage> Provision(HttpClient c,string csrf,Guid id,bool engagement,JsonElement review,bool reviewed=true,string reason="Reviewed API setup")=>Post(c,"workspaces/"+(engagement?"engagements":"clients")+"/provision",csrf,new{targetId=id,reviewToken=review.GetProperty("reviewToken").GetString(),reason,reviewed});
  private static async Task<JsonElement> Review(HttpClient c,Guid id,bool engagement){using var response=await c.GetAsync(Prefix+"workspaces/"+(engagement?"engagements":"clients")+"/"+id+"/review");Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.Clone();}
  private static async Task<JsonElement> Value(HttpResponseMessage r){Assert.Equal(HttpStatusCode.OK,r.StatusCode);using var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return json.RootElement.GetProperty("value").Clone();}
}
