using System.Net;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Testing;
using AuditSphereOps.Api.Ui;
using Microsoft.Extensions.Configuration;

namespace AuditSphereOps.Api.Tests;

public sealed class CanonicalAngularHostTests
{
  [Theory]
  [InlineData(false,"/ui")]
  [InlineData(true,"")]
  public void SetupConsentAndWorkspaceDestinationsFollowDeploymentOwnership(bool canonical,string prefix)
  {
    var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["AngularUi:CanonicalRoutes"]=canonical.ToString()}).Build();
    foreach(var path in new[]{"/app","/portal","/setup/microsoft365","/app/administration/microsoft365/tenant-connection"})
      Assert.Equal(prefix+path,AngularRouteOwnership.Destination(configuration,path));
  }
  [Fact]
  public async Task CanonicalOwnershipKeepsApiAuthAndHealthSeparateAndRetainsOnlyOldHashedAssets()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("CANONICAL-ANGULAR-HOST");var f=await PbcSeed.SeedAsync(pg);
    var root=Path.Combine(pg.RunRoot,"current-ui");var previous=Path.Combine(pg.RunRoot,"previous-ui");
    Directory.CreateDirectory(root);Directory.CreateDirectory(previous);
    const string index="<html><head><base href=\"/ui/\"><link rel=\"stylesheet\" href=\"/ui/styles-CURRENT1.css\"></head><body><app-root></app-root><script src=\"/ui/main-CURRENT1.js\"></script></body></html>";
    await File.WriteAllTextAsync(Path.Combine(root,"index.html"),index);await File.WriteAllTextAsync(Path.Combine(previous,"index.html"),"OLD-SHELL-MUST-NOT-BE-SERVED");
    await File.WriteAllTextAsync(Path.Combine(root,"main-CURRENT1.js"),"CURRENT-SYNTHETIC-ASSET");
    await File.WriteAllTextAsync(Path.Combine(previous,"chunk-PREVIOUS.js"),"PREVIOUS-SYNTHETIC-CHUNK");
    await File.WriteAllTextAsync(Path.Combine(previous,"chunk-DfG_6ZD-.js"),"PREVIOUS-URLSAFE-HASH");
    await File.WriteAllTextAsync(Path.Combine(previous,"unhashed.js"),"NOT-AN-APPROVED-RETAINED-ASSET");
    await File.WriteAllTextAsync(Path.Combine(previous,"chunk-PREVIOUS.js.map"),"SOURCE-MAP-NOT-SERVED");
    var settings=new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:Subject"]=f.Staff.Subject,["DevelopmentIdentity:TenantId"]=f.Staff.TenantId,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false",["AngularUi:Enabled"]="true",["AngularUi:BuildPath"]=root,["AngularUi:PreviousBuildPath"]=previous,["AngularUi:CanonicalRoutes"]="true"};
    using(var factory=new StandaloneApiApplicationFactory(settings))using(var c=factory.CreateClient(new(){AllowAutoRedirect=false}))
    {
      foreach(var path in new[]{"/app","/portal","/setup/microsoft365",$"/app/clients/{f.ClientId}"})
      {
        using var r=await c.GetAsync(path);Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal("text/html",r.Content.Headers.ContentType!.MediaType);
        var body=await r.Content.ReadAsStringAsync();Assert.Contains("<base href=\"/\">",body);Assert.Contains("src=\"/ui/main-CURRENT1.js\"",body);Assert.True(r.Headers.CacheControl!.NoStore);
      }
      Assert.Contains("<base href=\"/ui/\">",await c.GetStringAsync("/ui/app"));
      Assert.Equal("CURRENT-SYNTHETIC-ASSET",await c.GetStringAsync("/ui/main-CURRENT1.js"));
      Assert.Equal("PREVIOUS-SYNTHETIC-CHUNK",await c.GetStringAsync("/ui/chunk-PREVIOUS.js"));
      Assert.Equal("PREVIOUS-URLSAFE-HASH",await c.GetStringAsync("/ui/chunk-DfG_6ZD-.js"));
      foreach(var path in new[]{"/app/no-such-page","/app/clients/not-a-guid","/api/unknown","/auth/unknown","/_blazor/negotiate","/ui/unhashed.js","/ui/chunk-PREVIOUS.js.map"})
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync(path)).StatusCode);
      Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/ui/portfolio")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/health/live")).StatusCode);
      Assert.Equal("/app",(await c.GetAsync("/")).Headers.Location!.OriginalString);
      Assert.Equal("/app",(await c.GetAsync("/auth/sign-in")).Headers.Location!.OriginalString);
      Assert.Equal("/app",(await c.GetAsync("/auth/landing")).Headers.Location!.OriginalString);
      Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsync("/api/ui/sign-out",null)).StatusCode);
    }
    // A fresh host with canonical ownership disabled is the explicit local rollback.
    settings["AngularUi:CanonicalRoutes"]="false";
    using(var factory=new StandaloneApiApplicationFactory(settings))using(var c=factory.CreateClient(new(){AllowAutoRedirect=false}))
    {Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync("/app")).StatusCode);Assert.Contains("<base href=\"/ui/\">",await c.GetStringAsync("/ui/app"));Assert.Equal("/ui/app",(await c.GetAsync("/")).Headers.Location!.OriginalString);}
  }
  [Fact]
  public async Task CanonicalRoutesRejectMissingOrIncompatibleAssetsAtStartup()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("CANONICAL-ASSET-GUARD");
    using var disabled=new StandaloneApiApplicationFactory(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["AngularUi:CanonicalRoutes"]="true",["AngularUi:Enabled"]="false"});
    Assert.Throws<InvalidOperationException>(()=>disabled.CreateClient());
    var root=Path.Combine(pg.RunRoot,"bad-ui");Directory.CreateDirectory(root);await File.WriteAllTextAsync(Path.Combine(root,"index.html"),"<base href=\"/ui/\"><script src=\"main.js\"></script>");
    using var incompatible=new StandaloneApiApplicationFactory(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["AngularUi:CanonicalRoutes"]="true",["AngularUi:Enabled"]="true",["AngularUi:BuildPath"]=root});
    Assert.Throws<InvalidOperationException>(()=>incompatible.CreateClient());
  }
}
