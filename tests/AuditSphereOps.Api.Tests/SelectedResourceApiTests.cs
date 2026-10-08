using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditSphereOps.Api.Tests;

public sealed class SelectedResourceApiTests
{
  private const string Prefix = "/api/ui/administration/microsoft365/";
  [Fact]
  public async Task Templates_RequireReviewAndCsrf_RejectStaleVersions_AndKeepApprovalEvidenceImmutable()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-SELECTED-TEMPLATES");
    var s = await Seed(pg); using var factory = Factory(pg, s); using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    var manifest = Microsoft365ConfigurationService.DefaultManifest(FolderTemplatePurposes.ClientWorkspace);
    object Input(string json, string version = "0", bool review = true) => new { purpose = "CLIENT_WORKSPACE", manifestJson = json, expectedVersion = version, reviewed = review };
    Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Prefix + "templates/save", Input(manifest))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "templates/save", csrf, Input(manifest, review: false))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "templates/save", csrf, Input("{\"nodes\":[{\"key\":\"bad\",\"name\":\"../escape\"}]}"))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "templates/save", csrf, Input("{\"nodes\":[{\"key\":\"ok\",\"name\":\"Safe\"}],\"outside\":true}"))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "templates/save", csrf, new { purpose = "ENGAGEMENT_WORKSPACE", manifestJson = "{\"nodes\":[]}", expectedVersion = "0", reviewed = true })).StatusCode);
    var saved = await Value(await Post(client, "templates/save", csrf, Input(manifest)));
    var id = saved.GetProperty("id").GetGuid(); var digest = saved.GetProperty("manifestDigest").GetString();
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "templates/save", csrf, Input(Microsoft365ConfigurationService.SteManifest("CLIENT_WORKSPACE")))).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "templates/approve", csrf, new { templateId = id, expectedDigest = new string('f',64), reviewed = true })).StatusCode);
    foreach (var _ in new[] { 1, 2 }) Assert.Equal(HttpStatusCode.OK, (await Post(client, "templates/approve", csrf, new { templateId = id, expectedDigest = digest, reviewed = true })).StatusCode);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Single(await db.FolderTemplateVersions.Where(x => x.FirmId == s.F.FirmId).ToListAsync());
    Assert.Equal(1, await db.Microsoft365AdministrationEvents.CountAsync(x => x.Operation == "FOLDER_TEMPLATE_APPROVED"));
    Assert.Equal(1, await db.Microsoft365AdministrationEvents.CountAsync(x => x.Operation == "FOLDER_TEMPLATE_SAVED"));
    var sibling = await PbcSeed.SeedAsync(pg);
    using var siblingFactory = Factory(pg, s with { F = sibling }); using var other = siblingFactory.CreateClient(new() { AllowAutoRedirect = false });
    var otherCsrf = await SignIn(other);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(other, "templates/approve", otherCsrf, new { templateId = id, expectedDigest = digest, reviewed = true })).StatusCode);
    using var read = await client.GetAsync(Prefix + "resources"); var body = await read.Content.ReadAsStringAsync();
    Assert.True(read.Headers.CacheControl!.NoStore); Assert.DoesNotContain("private-synthetic-slot", body);
    Assert.DoesNotContain("capabilityHash", body); Assert.DoesNotContain("bootstrapProofHash", body); Assert.DoesNotContain("stateHash", body);
  }

  [Fact]
  public async Task ResourceEdits_InvalidatePreviousPass_AndActivationRequiresTrustedBoundaryProof()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-SELECTED-BOUNDARY");
    var s = await Seed(pg); var probe = new FakeProbe(pg.Options) { Block = true };
    using var factory = Factory(pg, s, probe); using var client = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(client);
    var template = await Value(await Post(client, "templates/save", csrf, new { purpose = "CLIENT_WORKSPACE", expectedVersion = "0", manifestJson = Microsoft365ConfigurationService.DefaultManifest("CLIENT_WORKSPACE"), reviewed = true }));
    var templateId = template.GetProperty("id").GetGuid();
    Assert.Equal(HttpStatusCode.OK, (await Post(client, "templates/approve", csrf, new { templateId, expectedDigest = template.GetProperty("manifestDigest").GetString(), reviewed = true })).StatusCode);
    object Save(string revision, string url = "https://synthetic.sharepoint.com/sites/working") => new { draftId = s.DraftId, expectedRevision = revision, siteUrl = url, siteId = "site-1", driveId = "drive-1", rootFolderId = "root-1", accessProfile = "APP_MEDIATED", reviewed = true };
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "resources/save", csrf, Save("1", "https://synthetic.sharepoint.com/sites/working?token=do-not-store"))).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await Post(client, "resources/save", csrf, Save("1"))).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "resources/save", csrf, Save("1"))).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    { Assert.Equal(2, (await db.Microsoft365SetupDrafts.SingleAsync()).Revision); Assert.Equal("BLOCKED_EXTERNAL", (await db.TenantCapabilityVerifications.OrderByDescending(x => x.ObservedAt).FirstAsync()).State); }
    object Verify(string revision) => new { draftId = s.DraftId, expectedRevision = revision, reviewed = true };
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "resources/verify", csrf, Verify("1"))).StatusCode); Assert.Equal(0, probe.Calls);
    var blocked = await Post(client, "resources/verify", csrf, Verify("2")); Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
    Assert.Contains("BLOCKED_EXTERNAL", await blocked.Content.ReadAsStringAsync()); Assert.DoesNotContain("private-error", await blocked.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "resources/activate", csrf, new { draftId = s.DraftId, expectedRevision = "2", templateId, reviewed = true })).StatusCode);
    probe.Block = false;
    Assert.Equal(HttpStatusCode.OK, (await Post(client, "resources/verify", csrf, Verify("2"))).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "resources/activate", csrf, new { draftId = s.DraftId, expectedRevision = "2", templateId, reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await Post(client, "resources/activate", csrf, new { draftId = s.DraftId, expectedRevision = "3", templateId, reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "resources/save", csrf, Save("4"))).StatusCode);
    await using var final = new AuditSphereDbContext(pg.Options);
    Assert.Equal("ACTIVE", (await final.Microsoft365ConnectionRevisions.SingleAsync()).State);
    Assert.Equal(3, await final.IntegrationVerificationEvidences.CountAsync(x => x.Operation == "GRAPH_BOUNDARY_READ"));
    Assert.Single(await final.FirmWorkspaceConfigurations.ToListAsync());
    Assert.Equal(1, await final.Microsoft365AdministrationEvents.CountAsync(x => x.Operation == "SELECTED_WORKSPACE_ACTIVATED"));
  }

  [Fact]
  public async Task ResourceAuthority_IsCheckedBeforeInputValidation_AndUnknownFirmIdsRevealNothing()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-SELECTED-AUTHORITY"); var s = await Seed(pg);
    using var staffFactory = Factory(pg, s, staff: true); using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(staff);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Prefix + "resources")).StatusCode);
    foreach (var path in new[] { "templates/save", "templates/approve", "resources/save", "resources/verify", "resources/activate" })
      Assert.Equal(HttpStatusCode.Forbidden, (await Post(staff, path, csrf, new { reviewed = true })).StatusCode);
    using var adminFactory = Factory(pg, s); using var admin = adminFactory.CreateClient(new() { AllowAutoRedirect = false }); var adminCsrf = await SignIn(admin);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, "resources/save", adminCsrf, new { draftId = Guid.NewGuid(), expectedRevision = "1", siteUrl = "https://synthetic.sharepoint.com/sites/working", siteId = "site-1", driveId = "drive-1", rootFolderId = "root-1", accessProfile = "APP_MEDIATED", reviewed = true })).StatusCode);
    await using var db = new AuditSphereDbContext(pg.Options);
    var draft = await db.Microsoft365SetupDrafts.SingleAsync(); draft.ConnectionRevisionId = null;
    db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification { Id = Guid.NewGuid(), FirmId = s.F.FirmId,
      TenantId = s.F.Admin.TenantId, Capability = Microsoft365Capabilities.SelectedSite, Permission = "Sites.Selected", State = "VERIFIED", DiagnosticCode = "unbound-synthetic-observation", ObservedAt = DateTimeOffset.UtcNow, ObservedByUserId = s.F.Admin.Id });
    await db.SaveChangesAsync();
    using var unbound = await admin.GetAsync(Prefix + "resources");
    using var json = JsonDocument.Parse(await unbound.Content.ReadAsStringAsync());
    Assert.Equal("BLOCKED_EXTERNAL",json.RootElement.GetProperty("workspace").GetProperty("verification").GetProperty("state").GetString());
    var user = await db.Users.SingleAsync(x => x.Id == s.F.Admin.Id); user.SessionEpoch++; await db.SaveChangesAsync();
    Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync(Prefix + "resources")).StatusCode);
    Assert.Empty(await db.Microsoft365AdministrationEvents.ToListAsync());
  }

  private sealed record Seeded(PbcSeed.Fixture F, Guid DraftId);
  private static async Task<Seeded> Seed(OwnedPostgresDatabase pg)
  {
    var f = await PbcSeed.SeedAsync(pg); await using var db = new AuditSphereDbContext(pg.Options);
    var admin = await db.Users.SingleAsync(x => x.Id == f.Admin.Id); f.Admin.TenantId = admin.TenantId = Guid.NewGuid().ToString("D"); f.Admin.Subject = admin.Subject = Guid.NewGuid().ToString("D");
    var now = DateTimeOffset.UtcNow; var session = new Microsoft365SetupSession { Id = Guid.NewGuid(), FirmId = f.FirmId, InstallationId = "synthetic-resource-test", BootstrapProofHash = new string('a',64), CapabilityHash = new string('b',64), ClaimedByUserId = admin.Id, ClaimedAt = now, ExpiresAt = now.AddHours(1) };
    var connection = new Microsoft365ConnectionRevision { Id = Guid.NewGuid(), FirmId = f.FirmId, TenantId = admin.TenantId, RuntimeCredentialReference = "private-synthetic-slot", LoginClientIdReference = "configuration:Identity:ClientId", State = "VERIFIED", ConsentState = "VERIFIED", CreatedAt = now };
    var draft = new Microsoft365SetupDraft { Id = Guid.NewGuid(), FirmId = f.FirmId, SetupSessionId = session.Id, ConnectionRevisionId = connection.Id, State = "VERIFIED", ExpectedTenantId = admin.TenantId, SiteUrl = "https://synthetic.sharepoint.com/sites/working", SiteId = "site-1", DriveId = "drive-1", RootFolderId = "root-1", CreatedAt = now, UpdatedAt = now };
    db.Microsoft365SetupSessions.Add(session); db.Microsoft365ConnectionRevisions.Add(connection); db.Microsoft365SetupDrafts.Add(draft);
    db.TenantConsentAttempts.Add(new TenantConsentAttempt { Id = Guid.NewGuid(), FirmId = f.FirmId, SetupDraftId = draft.Id, InitiatedByUserId = admin.Id, InitiatingSessionEpoch = admin.SessionEpoch, InitiatorObjectId = admin.Subject, ExpectedTenantId = admin.TenantId, ApplicationClientId = "synthetic-consent-app", StateHash = new string('c',64), State = TenantConsentAttemptStates.ConsentVerified, ConsentingTenantId = admin.TenantId, ConsentingObjectId = admin.Subject, ConsentVerifiedAt = now, CreatedAt = now, ExpiresAt = now.AddHours(1) });
    db.IntegrationVerificationEvidences.Add(new IntegrationVerificationEvidence { Id = Guid.NewGuid(), FirmId = f.FirmId, SetupDraftId = draft.Id, ConnectionRevisionId = connection.Id, ResourceKind = "TENANT", ResourceId = admin.TenantId, Operation = "CONSENT", Result = "PASS", EvidenceReference = "synthetic provider fixture", IdentityReference = "synthetic-consent", ObservedAt = now });
    await db.SaveChangesAsync(); return new(f, draft.Id);
  }
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, Seeded s, FakeProbe? probe = null, bool staff = false) => new(new Dictionary<string,string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = staff ? s.F.Staff.TenantId : s.F.Admin.TenantId, ["DevelopmentIdentity:Subject"] = staff ? s.F.Staff.Subject : s.F.Admin.Subject,
    ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
    ["SelectedSite:TenantId"] = s.F.Admin.TenantId, ["SelectedSite:ClientId"] = Guid.NewGuid().ToString(), ["SelectedSite:CredentialReference"] = "synthetic-test", ["SelectedSite:CertificatePath"] = "synthetic-unused", ["SelectedSite:PrivateKeyPath"] = "synthetic-unused", ["SelectedSite:NegativeControlSiteUrl"] = "https://synthetic.sharepoint.com/sites/control"
  }, services => services.AddSingleton<ISelectedSiteBoundaryProbe>(probe ?? new FakeProbe(pg.Options)));
  private sealed class FakeProbe(DbContextOptions<AuditSphereDbContext> options) : ISelectedSiteBoundaryProbe
  {
    public bool Block { get; set; } public int Calls { get; private set; }
    public async Task<SelectedSiteBoundaryObservation> ProbeAsync(Guid firmId, Guid draftId, string negativeControlSiteUrl, CancellationToken ct)
    { Calls++; if(Block) throw new OperationBlockedException("private-error"); await using var db = new AuditSphereDbContext(options);
      var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.FirmId == firmId && x.Id == draftId,ct);
      return new(draft.Id,draft.Revision,draft.ConnectionRevisionId!.Value,draft.ExpectedTenantId!,draft.SiteId!,draft.DriveId!,draft.RootFolderId!,draft.SiteUrl!); }
  }
  private static async Task<string> SignIn(HttpClient client) { await client.GetAsync("/auth/sign-in"); using var session = await client.GetAsync("/api/ui/session"); Assert.Equal(HttpStatusCode.OK,session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
  private static Task<HttpResponseMessage> Post(HttpClient client,string path,string csrf,object body) { var r = new HttpRequestMessage(HttpMethod.Post,Prefix+path) { Content = JsonContent.Create(body) }; r.Headers.Add("X-XSRF-TOKEN",csrf); return client.SendAsync(r); }
  private static async Task<JsonElement> Value(HttpResponseMessage response) { Assert.Equal(HttpStatusCode.OK,response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("value").Clone(); }
}
