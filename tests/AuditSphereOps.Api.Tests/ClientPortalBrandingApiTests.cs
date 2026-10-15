using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Testing;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ClientPortalBrandingApiTests
{
  private const string LogoBase64 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAN0lEQVR4nO3QQREAMAgDQYoYvGGtJstUBZ+Ngcvsqb4vFpebcQcIECBAgAABAgQIECBAgACBLzDWWQJ4Rpw0uQAAAABJRU5ErkJggg==";

  [Fact]
  public async Task ClientBrandingExposesMinimalMetadataAndExactProtectedCurrentLogoBytes()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CLIENT-PORTAL-BRANDING");
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var bytes = Convert.FromBase64String(LogoBase64);
    var hash = Hashing.Sha256Hex(bytes);
    var profileId = Guid.NewGuid();
    var foreignProfileId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.FirmCommercialProfiles.Add(Profile(profileId, fixture.FirmId, fixture.Admin.Id, 1,
        "Synthetic portal firm", bytes, hash));
      db.FirmCommercialProfiles.Add(Profile(foreignProfileId, foreign.FirmId, foreign.Admin.Id, 1,
        "Foreign synthetic firm", bytes, Hashing.Sha256Hex(bytes)));
      await db.SaveChangesAsync();
    }

    using var factory = Factory(pg, fixture.Client);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    var profileCountBefore = await CountProfilesAsync(pg);
    var documentsBefore = await CountDocumentsAsync(pg);

    using (var metadataResponse = await client.GetAsync("/api/ui/portal/branding"))
    {
      Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);
      using var json = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync());
      var root = json.RootElement;
      Assert.Equal(profileId, root.GetProperty("profileId").GetGuid());
      Assert.Equal("1", root.GetProperty("version").GetString());
      Assert.Equal("Synthetic portal firm", root.GetProperty("legalName").GetString());
      Assert.Equal("#2B6CB0", root.GetProperty("accentColorHex").GetString());
      Assert.True(root.GetProperty("hasLogo").GetBoolean());
      Assert.Equal(hash, root.GetProperty("logoSha256Hex").GetString());
      var names = root.EnumerateObject().Select(x => x.Name).Order().ToArray();
      Assert.Equal(new[] { "accentColorHex", "hasLogo", "legalName", "logoSha256Hex", "profileId", "version" }, names);
      var body = root.GetRawText();
      foreach (var forbidden in new[] { "address", "history", "credentials", "methodology", "pricing", "signature", "seal", "logoPngBytes", LogoBase64 })
        Assert.False(body.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    using (var image = await client.GetAsync($"/api/ui/portal/branding/{profileId}/logo"))
    {
      Assert.Equal(HttpStatusCode.OK, image.StatusCode);
      Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
      Assert.Equal(bytes, await image.Content.ReadAsByteArrayAsync());
      Assert.Equal(hash, image.Headers.GetValues("X-Logo-SHA256").Single());
      Assert.Contains("no-store", image.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
      Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
    }

    using var foreignFactory = Factory(pg, fixture.Staff);
    using var staff = foreignFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(staff);
    using (var denied = await staff.GetAsync("/api/ui/portal/branding"))
    {
      Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
      Assert.Equal("scope.denied", await ErrorCode(denied));
    }

    using (var foreignKnown = await client.GetAsync($"/api/ui/portal/branding/{foreignProfileId}/logo"))
    using (var foreignUnknown = await client.GetAsync($"/api/ui/portal/branding/{Guid.NewGuid()}/logo"))
    {
      Assert.Equal(HttpStatusCode.Conflict, foreignKnown.StatusCode);
      Assert.Equal(HttpStatusCode.Conflict, foreignUnknown.StatusCode);
      var knownBody = await foreignKnown.Content.ReadAsStringAsync();
      var unknownBody = await foreignUnknown.Content.ReadAsStringAsync();
      Assert.Equal(unknownBody, knownBody);
      Assert.False(knownBody.Contains("Foreign synthetic firm", StringComparison.Ordinal));
      Assert.False(knownBody.Contains(Hashing.Sha256Hex(bytes), StringComparison.Ordinal));
    }
    using (var emptyId = await client.GetAsync($"/api/ui/portal/branding/{Guid.Empty}/logo"))
    {
      Assert.Equal(HttpStatusCode.Forbidden, emptyId.StatusCode);
      Assert.Equal("scope.denied", await ErrorCode(emptyId));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.FirmCommercialProfiles.Add(Profile(Guid.NewGuid(), fixture.FirmId, fixture.Admin.Id, 2,
        "Synthetic replacement without logo", null, null));
      await db.SaveChangesAsync();
    }
    using (var staleLogo = await client.GetAsync($"/api/ui/portal/branding/{profileId}/logo"))
      Assert.Equal(HttpStatusCode.Conflict, staleLogo.StatusCode);
    var noLogo = await client.GetFromJsonAsync<JsonElement>("/api/ui/portal/branding");
    Assert.False(noLogo.GetProperty("hasLogo").GetBoolean());
    Assert.True(noLogo.GetProperty("logoSha256Hex").ValueKind == JsonValueKind.Null);

    var corruptId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.FirmCommercialProfiles.Add(Profile(corruptId, fixture.FirmId, fixture.Admin.Id, 3,
        "Synthetic corrupt logo record", [0x01, 0x02, 0x03], new string('a', 64)));
      await db.SaveChangesAsync();
    }
    using (var corrupt = await client.GetAsync($"/api/ui/portal/branding/{corruptId}/logo"))
    {
      Assert.Equal(HttpStatusCode.BadRequest, corrupt.StatusCode);
      Assert.Equal("gate.blocked", await ErrorCode(corrupt));
    }

    Assert.Equal(profileCountBefore + 2, await CountProfilesAsync(pg));
    Assert.Equal(documentsBefore, await CountDocumentsAsync(pg));
    using var noSessionFactory = Factory(pg, fixture.Client);
    using (var noSession = noSessionFactory.CreateClient(new() { AllowAutoRedirect = false }))
    {
      using var unauthenticated = await noSession.GetAsync("/api/ui/portal/branding");
      Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x => x.Id == fixture.Client.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    using (var revoked = await client.GetAsync($"/api/ui/portal/branding/{corruptId}/logo"))
    {
      Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
      Assert.Equal("session.unavailable", await ErrorCode(revoked));
    }
  }

  [Fact]
  public async Task ClientBrandingWithoutAProfileReturnsTheNonPersistedFallback()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CLIENT-PORTAL-BRANDING-FALLBACK");
    var fixture = await PbcSeed.SeedAsync(pg);
    using var factory = Factory(pg, fixture.Client);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    using var response = await client.GetAsync("/api/ui/portal/branding");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var view = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(JsonValueKind.Null, view.GetProperty("profileId").ValueKind);
    Assert.Equal("0", view.GetProperty("version").GetString());
    Assert.Equal("AuditSphere", view.GetProperty("legalName").GetString());
    Assert.Equal("#2B6CB0", view.GetProperty("accentColorHex").GetString());
    Assert.False(view.GetProperty("hasLogo").GetBoolean());
    Assert.Equal(JsonValueKind.Null, view.GetProperty("logoSha256Hex").ValueKind);
    Assert.Equal(0, await CountProfilesAsync(pg));
  }

  private static FirmCommercialProfile Profile(Guid id, Guid firmId, Guid createdBy, long version,
    string name, byte[]? bytes, string? hash) => new()
  {
    Id = id, FirmId = firmId, Version = version, LegalName = name, AccentColorHex = "#2B6CB0",
    LogoPngBytes = bytes, LogoSha256Hex = hash, CreatedByUserId = createdBy, CreatedAt = DateTimeOffset.UtcNow
  };

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
    ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:Subject"] = user.Subject,
    ["DevelopmentIdentity:TenantId"] = user.TenantId,
    ["ExternalEffects:Enabled"] = "false",
    ["Application:AllowSimulationAdapters"] = "true"
  });

  private static async Task SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    await client.GetAsync("/api/ui/session");
  }

  private static async Task<string?> ErrorCode(HttpResponseMessage response)
  {
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.TryGetProperty("code", out var value) ? value.GetString() : null;
  }

  private static async Task<int> CountProfilesAsync(OwnedPostgresDatabase pg)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await db.FirmCommercialProfiles.CountAsync();
  }

  private static async Task<int> CountDocumentsAsync(OwnedPostgresDatabase pg)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await db.CommercialDocuments.CountAsync();
  }
}
