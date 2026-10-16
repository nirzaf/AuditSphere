using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Testing;

namespace AuditSphereOps.Api.Tests;

/// <summary>
/// Native administration access parity: roster fallback binding, reviewed assignment with an atomic copy-link
/// invitation intent, copyable invitation retrieval with recorded copying, and per-grant change evidence in the
/// access workspace. Authorization, antiforgery and review fences are enforced exactly as for plain assignment.
/// </summary>
public sealed class AdministrationAccessInvitationApiTests
{
  private static StandaloneApiApplicationFactory Factory(string connection, string subject, string tenant) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = connection,
    ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = subject, ["DevelopmentIdentity:TenantId"] = tenant,
    ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
  });

  private static async Task<string> SignInAsync(HttpClient client)
  {
    Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fapp")).StatusCode);
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("XSRF-TOKEN=")).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, HttpContent content, string? proof)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
    if (proof is not null) request.Headers.Add("X-XSRF-TOKEN", proof);
    return await client.SendAsync(request);
  }

  private static string RosterSubject() => "sub-roster-" + Guid.NewGuid().ToString("N");

  [Fact]
  public async Task InvitationFlow_BindsRosterIdentity_AssignsWithInvitation_AndExposesCopyableLinkAndGrantHistory()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-ADMIN-INVITE-01");
    var seed = await PbcSeed.SeedAsync(pg);

    using (var staffFactory = Factory(pg.ConnectionString, seed.Staff.Subject, seed.Staff.TenantId))
    using (var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false }))
    {
      var staffProof = await SignInAsync(staff);
      Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/ui/administration/access/invitations/{Guid.NewGuid()}")).StatusCode);
      Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(staff, "/api/ui/administration/access/bind-manual",
        JsonContent.Create(new { tenantId = "tenant-test", subject = RosterSubject(), email = "roster@example.test",
          displayName = "Roster Staff", userKind = "Staff", source = "APPROVED_ROSTER" }), staffProof)).StatusCode);
    }

    using var adminFactory = Factory(pg.ConnectionString, seed.Admin.Subject, seed.Admin.TenantId);
    using var admin = adminFactory.CreateClient(new() { AllowAutoRedirect = false });
    var proof = await SignInAsync(admin);

    Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(admin, "/api/ui/administration/access/bind-manual",
      JsonContent.Create(new { tenantId = "tenant-test", subject = RosterSubject(), email = "roster@example.test",
        displayName = "Roster Staff", userKind = "Staff", source = "APPROVED_ROSTER" }), null)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(admin, "/api/ui/administration/access/bind-manual",
      JsonContent.Create(new { tenantId = "tenant-test", subject = RosterSubject(), email = "roster@example.test",
        displayName = "Roster Staff", userKind = "Staff", source = "GRAPH" }), proof)).StatusCode);

    using var bound = await PostAsync(admin, "/api/ui/administration/access/bind-manual",
      JsonContent.Create(new { tenantId = "tenant-test", subject = RosterSubject(), email = "roster@example.test",
        displayName = "Roster Staff", userKind = "Staff", source = "APPROVED_ROSTER" }), proof);
    Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
    var rosterUserId = (await bound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("userId").GetGuid();

    var request = new { userId = seed.Staff.Id, role = "Manager", scopeKind = "CLIENT", clientId = seed.ClientId, reason = "Review assigned team", confirmScopeExpansion = true };
    using var accessPreview = await PostAsync(admin, "/api/ui/administration/access/preview", JsonContent.Create(request), proof);
    Assert.Equal(HttpStatusCode.OK, accessPreview.StatusCode);
    var digest = (await accessPreview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("digest").GetString()!;

    Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(admin, "/api/ui/administration/access/assign-invitation",
      JsonContent.Create(new { request, reviewDigest = digest, reviewed = false }), proof)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(admin, "/api/ui/administration/access/assign-invitation",
      JsonContent.Create(new { request, reviewDigest = new string('a', 64), reviewed = true }), proof)).StatusCode);

    using var assigned = await PostAsync(admin, "/api/ui/administration/access/assign-invitation",
      JsonContent.Create(new { request, reviewDigest = digest, reviewed = true }), proof);
    Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
    var assignment = (await assigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
    var invitationId = assignment.GetProperty("invitationId").GetGuid();
    Assert.Equal("/auth/landing", assignment.GetProperty("destinationPath").GetString());
    Assert.Equal("NOT_SENT", assignment.GetProperty("deliveryState").GetString());

    var invitation = await admin.GetFromJsonAsync<JsonElement>($"/api/ui/administration/access/invitations/{invitationId}");
    Assert.Equal(seed.Staff.Email, invitation.GetProperty("recipientEmail").GetString());
    Assert.Equal("/auth/landing", invitation.GetProperty("destinationPath").GetString());
    Assert.Equal("/auth/landing", invitation.GetProperty("link").GetString());

    Assert.Equal(HttpStatusCode.OK, (await PostAsync(admin, $"/api/ui/administration/access/invitations/{invitationId}/copied",
      JsonContent.Create(new { }), proof)).StatusCode);

    var workspace = await admin.GetFromJsonAsync<JsonElement>("/api/ui/administration/access");
    var staffRow = workspace.GetProperty("users").EnumerateArray().Single(u => u.GetProperty("userId").GetString() == seed.Staff.Id.ToString("D"));
    Assert.Equal("COPIED", staffRow.GetProperty("invitationStatus").GetString());
    Assert.Equal(JsonValueKind.String, staffRow.GetProperty("invitationId").ValueKind);
    var rosterRow = workspace.GetProperty("users").EnumerateArray().Single(u => u.GetProperty("userId").GetString() == rosterUserId.ToString("D"));
    Assert.Equal("NO_ACCESS", rosterRow.GetProperty("auditSphereStatus").GetString());
    Assert.Contains(workspace.GetProperty("grantHistory").EnumerateArray(),
      e => e.GetProperty("action").GetString() == "GRANTED" && e.GetProperty("newRole").GetString() == "Manager" &&
        e.GetProperty("grantId").GetString() == assignment.GetProperty("roleGrantId").GetString());
  }
  [Theory,InlineData("assign"),InlineData("assign-invitation")]
  public async Task ReviewedReplacementPreservesExactScopeExpiryReasonAndSessionInvalidation(string action)
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-REVIEWED-REPLACEMENT");var seed=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var old=await db.RoleGrants.AsNoTracking().SingleAsync(x=>x.UserId==seed.Staff.Id && x.Role=="Staff");var epoch=seed.Staff.SessionEpoch;
    using var factory=Factory(pg.ConnectionString,seed.Admin.Subject,seed.Admin.TenantId);using var client=factory.CreateClient(new(){AllowAutoRedirect=false});var proof=await SignInAsync(client);
    var expiry=new DateTimeOffset(DateTimeOffset.UtcNow.AddDays(3).Ticks/10*10,TimeSpan.Zero);
    var request=new RoleAssignmentRequest(seed.Staff.Id,"Manager","ENGAGEMENT",seed.ClientId,seed.EngagementId,ExpiresAt:expiry,Reason:"Exact reviewed replacement reason",ReplacesGrantId:old.Id,ConfirmScopeExpansion:true);
    using var preview=await PostAsync(client,"/api/ui/administration/access/preview",JsonContent.Create(request),proof);Assert.Equal(HttpStatusCode.OK,preview.StatusCode);
    var digest=(await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("digest").GetString();
    Assert.Equal(HttpStatusCode.BadRequest,(await PostAsync(client,"/api/ui/administration/access/"+action,JsonContent.Create(new{request,reviewDigest=digest,reviewed=false}),proof)).StatusCode);
    using var applied=await PostAsync(client,"/api/ui/administration/access/"+action,JsonContent.Create(new{request,reviewDigest=digest,reviewed=true}),proof);Assert.Equal(HttpStatusCode.OK,applied.StatusCode);
    var value=(await applied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");var id=value.GetProperty(action=="assign"?"grantId":"roleGrantId").GetGuid();
    var grant=await db.RoleGrants.AsNoTracking().SingleAsync(x=>x.Id==id);Assert.Equal(expiry,grant.ExpiresAt);Assert.Equal(request.Reason,grant.Reason);Assert.Equal(seed.ClientId,grant.ClientId);Assert.Equal(seed.EngagementId,grant.EngagementId);
    Assert.NotNull((await db.RoleGrants.AsNoTracking().SingleAsync(x=>x.Id==old.Id)).RevokedAt);Assert.Equal(epoch+1,await db.Users.Where(x=>x.Id==seed.Staff.Id).Select(x=>x.SessionEpoch).SingleAsync());
    Assert.Equal(2,await db.RoleGrantChangeEvidences.CountAsync(x=>x.TargetUserId==seed.Staff.Id && x.Reason==request.Reason));
    if(action=="assign-invitation")Assert.Equal(id,(await db.UserAccessInvitations.SingleAsync()).RoleGrantId);else Assert.Empty(await db.UserAccessInvitations.ToListAsync());
  }
  [Theory,InlineData("assign"),InlineData("assign-invitation")]
  public async Task ReviewedExistingGrantDifferentExpiryRefusesWithoutPublishing(string action)
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-REVIEWED-EXPIRY");var seed=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    using var factory=Factory(pg.ConnectionString,seed.Admin.Subject,seed.Admin.TenantId);using var client=factory.CreateClient(new(){AllowAutoRedirect=false});var proof=await SignInAsync(client);
    var request=new RoleAssignmentRequest(seed.Staff.Id,"Staff","ENGAGEMENT",seed.ClientId,seed.EngagementId,ExpiresAt:DateTimeOffset.UtcNow.AddDays(2),Reason:"Requested bounded expiry",ConfirmScopeExpansion:true);
    using var preview=await PostAsync(client,"/api/ui/administration/access/preview",JsonContent.Create(request),proof);Assert.Equal(HttpStatusCode.OK,preview.StatusCode);var value=(await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");Assert.NotEmpty(value.GetProperty("review").GetProperty("blockingReasons").EnumerateArray());
    using var result=await PostAsync(client,"/api/ui/administration/access/"+action,JsonContent.Create(new{request,reviewDigest=value.GetProperty("digest").GetString(),reviewed=true}),proof);Assert.Equal(HttpStatusCode.BadRequest,result.StatusCode);
    Assert.Null((await db.RoleGrants.SingleAsync(x=>x.UserId==seed.Staff.Id)).ExpiresAt);Assert.Equal(seed.Staff.SessionEpoch,await db.Users.Where(x=>x.Id==seed.Staff.Id).Select(x=>x.SessionEpoch).SingleAsync());Assert.Empty(await db.UserAccessInvitations.ToListAsync());Assert.Empty(await db.RoleGrantChangeEvidences.ToListAsync());
  }
  [Theory,InlineData("assign"),InlineData("assign-invitation")]
  public async Task ReviewedAssignmentRejectsChangedCurrentScopeOrIndependenceBasis(string action)
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-REVIEWED-STALE");var seed=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    using var factory=Factory(pg.ConnectionString,seed.Admin.Subject,seed.Admin.TenantId);using var client=factory.CreateClient(new(){AllowAutoRedirect=false});var proof=await SignInAsync(client);
    var request=new RoleAssignmentRequest(seed.Staff.Id,"Manager","CLIENT",seed.ClientId,Reason:"Review scope before publication",ConfirmScopeExpansion:true);
    using var preview=await PostAsync(client,"/api/ui/administration/access/preview",JsonContent.Create(request),proof);Assert.Equal(HttpStatusCode.OK,preview.StatusCode);var digest=(await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("digest").GetString();
    db.SpecialistClearances.Add(new(){Id=Guid.NewGuid(),FirmId=seed.FirmId,PracticeClientId=seed.ClientId,Area="Independence",SpecialistName="Synthetic reviewer",Status="HOLD",CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
    using var result=await PostAsync(client,"/api/ui/administration/access/"+action,JsonContent.Create(new{request,reviewDigest=digest,reviewed=true}),proof);Assert.Equal(HttpStatusCode.Conflict,result.StatusCode);Assert.Empty(await db.UserAccessInvitations.ToListAsync());Assert.False(await db.RoleGrants.AnyAsync(x=>x.UserId==seed.Staff.Id && x.Role=="Manager"));
  }
  [Fact]
  public async Task InvitationRequiresExpansionAssentEvenWithReviewedEnvelopeAndRejectsRevokedAdministrator()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-REVIEWED-ASSENT");var seed=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    using var factory=Factory(pg.ConnectionString,seed.Admin.Subject,seed.Admin.TenantId);using var client=factory.CreateClient(new(){AllowAutoRedirect=false});var proof=await SignInAsync(client);
    var request=new RoleAssignmentRequest(seed.Staff.Id,"Manager","CLIENT",seed.ClientId,Reason:"Explicit expansion assent");
    using var preview=await PostAsync(client,"/api/ui/administration/access/preview",JsonContent.Create(request),proof);Assert.Equal(HttpStatusCode.OK,preview.StatusCode);var digest=(await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("digest").GetString();
    Assert.Equal(HttpStatusCode.BadRequest,(await PostAsync(client,"/api/ui/administration/access/assign-invitation",JsonContent.Create(new{request,reviewDigest=digest,reviewed=true}),proof)).StatusCode);
    await db.RoleGrants.Where(x=>x.UserId==seed.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    Assert.Equal(HttpStatusCode.Forbidden,(await PostAsync(client,"/api/ui/administration/access/assign-invitation",JsonContent.Create(new{request=request with{ConfirmScopeExpansion=true},reviewDigest=digest,reviewed=true}),proof)).StatusCode);Assert.Empty(await db.UserAccessInvitations.ToListAsync());
  }

}
