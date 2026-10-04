using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Tests;
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

    var request = new { userId = seed.Staff.Id, role = "Manager", scopeKind = "CLIENT", clientId = seed.ClientId, reason = "Review assigned team" };
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
}
