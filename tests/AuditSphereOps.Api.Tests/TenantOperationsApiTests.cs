using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

/// <summary>Independent API host, owned PostgreSQL database, and Microsoft provider fakes only.</summary>
public sealed class TenantOperationsApiTests
{
  [Fact]
  public async Task CreateAndReconcile_RequireReview_UseOneTimePassword_AndNeverDuplicateBindings()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TENANT-PROVISIONING");
    var f = await Seed(pg);
    using var factory = Factory(pg, f);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    const string path = "/api/ui/administration/directory/create";
    var request = Create(f, "api.joiner@example.test", "api-create-reviewed-0001");
    Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { request, reviewed = true, reviewedPasswordHandling = true })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, path, csrf, new { request, reviewed = false, reviewedPasswordHandling = true })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, path, csrf, new { request, reviewed = true, reviewedPasswordHandling = false })).StatusCode);
    using var created = await Post(client, path, csrf, new { request, reviewed = true, reviewedPasswordHandling = true });
    Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    Assert.True(created.Headers.CacheControl!.NoStore);
    var value = await Value(created);
    Assert.Equal(ExternalOperationStates.Bound, value.GetProperty("state").GetString());
    var password = value.GetProperty("temporaryPassword").GetString()!;
    Assert.True(password.Length >= 16);
    var id = value.GetProperty("operationId").GetGuid();
    using var repeated = await Post(client, path, csrf, new { request, reviewed = true, reviewedPasswordHandling = true });
    Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
    Assert.Equal(JsonValueKind.Null, (await Value(repeated)).GetProperty("temporaryPassword").ValueKind);
    using var review = await client.GetAsync($"/api/ui/administration/microsoft365/operations/{id}");
    Assert.Equal(HttpStatusCode.OK, review.StatusCode);
    Assert.DoesNotContain("temporaryPassword", await review.Content.ReadAsStringAsync());
    using var resume = await Post(client, $"/api/ui/administration/microsoft365/operations/{id}/resume", csrf, new { reviewed = true });
    Assert.Equal(JsonValueKind.Null, (await Value(resume)).GetProperty("temporaryPassword").ValueKind);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var user = await db.Users.SingleAsync(x => x.Email == "api.joiner@example.test");
      var grant = Assert.Single(await db.RoleGrants.Where(x => x.UserId == user.Id).ToListAsync());
      Assert.Equal(f.ClientId, grant.ClientId);
      Assert.Equal("Staff", grant.Role);
      Assert.Single(await db.RoleGrantChangeEvidences.Where(x => x.RoleGrantId == grant.Id).ToListAsync());
      Assert.DoesNotContain(password, JsonSerializer.Serialize(new object[] { await db.Users.ToListAsync(),
        await db.Microsoft365ExternalOperations.ToListAsync(), await db.Microsoft365AdministrationEvents.ToListAsync(), await db.DirectoryUserObservations.ToListAsync() }));
    }
    var unknownRequest = Create(f, "unknown.api@example.test", "api-create-unknown-0001");
    using var unknown = await Post(client, path, csrf, new { request = unknownRequest, reviewed = true, reviewedPasswordHandling = true });
    var unknownValue = await Value(unknown);
    Assert.Equal(ExternalOperationStates.Unknown, unknownValue.GetProperty("state").GetString());
    var unknownId = unknownValue.GetProperty("operationId").GetGuid();
    using var recovered = await Post(client, $"/api/ui/administration/microsoft365/operations/{unknownId}/resume", csrf, new { reviewed = true });
    var recoveredValue = await Value(recovered);
    Assert.Equal(ExternalOperationStates.Bound, recoveredValue.GetProperty("state").GetString());
    Assert.Equal(JsonValueKind.Null, recoveredValue.GetProperty("temporaryPassword").ValueKind);
    await using var final = new AuditSphereDbContext(pg.Options);
    Assert.Single(await final.Users.Where(x => x.Email == "unknown.api@example.test").ToListAsync());
    Assert.Equal(1, (await final.Microsoft365ExternalOperations.SingleAsync(x => x.Id == unknownId)).AttemptCount);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ui/administration/microsoft365/operations/{Guid.NewGuid()}")).StatusCode);
  }

  [Fact]
  public async Task GuestsAndGroups_RequireExactClientAndMembershipReview_WithoutChangingLocalRoles()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TENANT-GUEST-GROUPS");
    var f = await Seed(pg);
    using var factory = Factory(pg, f);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    const string invite = "/api/ui/administration/directory/invite";
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, invite, csrf, new { request = new { idempotencyKey = "api-invite-invalid-0001", email = "guest@client.test", clientId = Guid.NewGuid(), reason = "Approved finance contact" }, reviewed = true })).StatusCode);
    using var invited = await Post(client, invite, csrf, new { request = new { idempotencyKey = "api-invite-reviewed-001", email = "guest@client.test", clientId = f.ClientId, engagementId = f.EngagementId, reason = "Approved finance contact" }, reviewed = true });
    Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var guest = await db.Users.SingleAsync(x => x.Email == "guest@client.test");
      Assert.Equal("Client", guest.UserKind);
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == guest.Id);
      Assert.Equal("ClientUser", grant.Role);
      Assert.Equal(f.ClientId, grant.ClientId);
      Assert.Equal(f.EngagementId, grant.EngagementId);
    }
    using var privileged = await Post(client, "/api/ui/administration/groups/approve", csrf,
      new { groupObjectId = PrivilegedGroup, purpose = "Unsafe group", reason = "Explicit rejection test", reviewed = true });
    Assert.Equal(HttpStatusCode.BadRequest, privileged.StatusCode);
    Assert.Contains("m365.group.privileged", await privileged.Content.ReadAsStringAsync());
    using var approved = await Post(client, "/api/ui/administration/groups/approve", csrf,
      new { groupObjectId = ManagedGroup, purpose = "Client collaboration", reason = "Approved collaboration team", reviewed = true });
    Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    var group = (await Value(approved)).GetGuid();
    using var preview = await Post(client, $"/api/ui/administration/groups/{group}/preview", csrf, new { userId = f.Admin.Id });
    Assert.False((await Value(preview)).GetProperty("existingMembership").GetBoolean());
    object Change(bool add, bool? expected, string key) => new { request = new { idempotencyKey = key, managedGroupId = group, userId = f.Admin.Id, add, reason = "Reviewed team membership", expectedExistingMembership = expected }, reviewed = true };
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/ui/administration/groups/change", csrf, Change(true, null, "api-group-no-review-001"))).StatusCode);
    using var added = await Post(client, "/api/ui/administration/groups/change", csrf, Change(true, false, "api-group-add-reviewed-01"));
    Assert.Equal(ExternalOperationStates.Accepted, (await Value(added)).GetProperty("state").GetString());
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "/api/ui/administration/groups/change", csrf, Change(false, false, "api-group-stale-review-1"))).StatusCode);
    using var removed = await Post(client, "/api/ui/administration/groups/change", csrf, Change(false, true, "api-group-remove-review1"));
    Assert.Equal(ExternalOperationStates.Accepted, (await Value(removed)).GetProperty("state").GetString());
    using var members = await Post(client, $"/api/ui/administration/groups/{group}/members", csrf, new { pageToken = (string?)null });
    Assert.Equal(0, (await Value(members)).GetProperty("members").GetArrayLength());
    await using var final = new AuditSphereDbContext(pg.Options);
    Assert.Single(await final.RoleGrants.Where(x => x.UserId == f.Admin.Id).ToListAsync());
    Assert.Equal(2, await final.Microsoft365ExternalOperations.CountAsync(x => x.ManagedGroupId == group));
    Assert.Equal(2, await final.Microsoft365AdministrationEvents.CountAsync(x => x.Operation.EndsWith("GROUP_MEMBER_OUTCOME")));
  }

  [Fact]
  public async Task OptionalCapabilitiesAndActorAuthority_AreRecheckedByTheApi()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TENANT-OPERATION-AUTHORITY");
    var f = await Seed(pg);
    using var factory = Factory(pg, f, enabled: false);
    using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(admin);
    using var blocked = await Post(admin, "/api/ui/administration/directory/create", csrf,
      new { request = Create(f, "blocked@example.test", "api-create-blocked-0001"), reviewed = true, reviewedPasswordHandling = true });
    Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
    Assert.Contains("gate.blocked", await blocked.Content.ReadAsStringAsync());
    using var staffFactory = Factory(pg, f, staff: true);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    var staffCsrf = await SignIn(staff);
    foreach (var path in new[] { "/api/ui/administration/directory/create", "/api/ui/administration/directory/invite", "/api/ui/administration/groups/change" })
      Assert.Equal(HttpStatusCode.Forbidden, (await Post(staff, path, staffCsrf, new { request = (object?)null, reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/ui/administration/microsoft365/operations/{Guid.NewGuid()}")).StatusCode);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await db.Microsoft365ExternalOperations.ToListAsync());
  }

  private const string ManagedGroup = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
  private const string PrivilegedGroup = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
  private static async Task<PbcSeed.Fixture> Seed(OwnedPostgresDatabase pg)
  {
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var admin = await db.Users.SingleAsync(x => x.Id == f.Admin.Id);
    f.Admin.TenantId = admin.TenantId = Guid.NewGuid().ToString("D");
    f.Admin.Subject = admin.Subject = Guid.NewGuid().ToString("D");
    var staff = await db.Users.SingleAsync(x => x.Id == f.Staff.Id);
    f.Staff.TenantId = staff.TenantId = admin.TenantId;
    foreach (var (capability, permission) in new[] { (Microsoft365Capabilities.TenantUserProvisioning, "User.Create"),
      (Microsoft365Capabilities.GuestInvitation, "User.Invite.All"), (Microsoft365Capabilities.GroupMembership, "GroupMember.ReadWrite.All") })
      db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification { Id = Guid.CreateVersion7(), FirmId = f.FirmId,
        TenantId = admin.TenantId, Capability = capability, Permission = permission, State = CapabilityVerificationStates.Verified,
        DiagnosticCode = "simulated-verified", ObservedAt = DateTimeOffset.UtcNow, ObservedByUserId = admin.Id });
    await db.SaveChangesAsync();
    return f;
  }
  private static object Create(PbcSeed.Fixture f, string upn, string key) => new { idempotencyKey = key, displayName = "API joiner",
    userPrincipalName = upn, mailNickname = "apijoiner", accountEnabled = true, role = "Staff", scopeKind = "CLIENT", clientId = f.ClientId,
    engagementId = (Guid?)null, reason = "Approved client team joiner" };
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, PbcSeed.Fixture f, bool enabled = true, bool staff = false) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:TenantId"] = staff ? f.Staff.TenantId : f.Admin.TenantId,
    ["DevelopmentIdentity:Subject"] = staff ? f.Staff.Subject : f.Admin.Subject,
    ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
    ["TenantAdministration:Simulation:Enabled"] = "true", ["TenantAdministration:Provisioning:Enabled"] = enabled.ToString(),
    ["TenantAdministration:GuestInvitation:Enabled"] = "true", ["TenantAdministration:GroupMembership:Enabled"] = "true",
    ["TenantAdministration:GuestRedirectUrl"] = "https://example.test/auth/landing",
    ["TenantAdministration:Simulation:Users:0:ObjectId"] = f.Admin.Subject,
    ["TenantAdministration:Simulation:Users:0:DisplayName"] = f.Admin.DisplayName,
    ["TenantAdministration:Simulation:Users:0:UserPrincipalName"] = f.Admin.Email,
    ["TenantAdministration:Simulation:Groups:0:ObjectId"] = ManagedGroup,
    ["TenantAdministration:Simulation:Groups:0:DisplayName"] = "Approved team",
    ["TenantAdministration:Simulation:Groups:1:ObjectId"] = PrivilegedGroup,
    ["TenantAdministration:Simulation:Groups:1:DisplayName"] = "Privileged test group",
    ["TenantAdministration:Simulation:Groups:1:IsAssignableToRole"] = "true"
  });
  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }
  private static Task<HttpResponseMessage> Post(HttpClient client, string path, string csrf, object input)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(input) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }
  private static async Task<JsonElement> Value(HttpResponseMessage response)
  {
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.TryGetProperty("value", out var v) ? v.Clone() : json.RootElement.Clone();
  }
}
