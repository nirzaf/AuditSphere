using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class Microsoft365OnboardingTests
{
  [Fact]
  public async Task SetupClaim_IsSingleInstallationScoped_AndDraftResumesWithNewCapability()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    const string installation = "install-test";
    const string proof = "one-time-bootstrap-proof";
    var proofHash = Hash(proof);
    var now = DateTimeOffset.UtcNow;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var denied = await Microsoft365OnboardingService.ClaimAsync(db, firmId, installation, "wrong", proofHash, now);
      Assert.False(denied.Succeeded);

      var first = await Microsoft365OnboardingService.ClaimAsync(db, firmId, installation, proof, proofHash, now);
      Assert.True(first.Succeeded);
      Assert.NotNull(first.Value);

      var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.SetupSessionId == first.Value!.SessionId);
      var saved = await Microsoft365OnboardingService.SaveDraftAsync(db,
        new(first.Value.SessionId, first.Value.Capability, draft.Revision, "tenant-verified",
          "Developer tenant", "https://easyguide.sharepoint.com/sites/AuditSphere", null, null, null,
          Microsoft365AccessProfiles.AppMediated, "NOT_CONFIGURED", "NOT_CONFIGURED"), now);
      Assert.True(saved.Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var resumed = await Microsoft365OnboardingService.ClaimAsync(db, firmId, installation, proof, proofHash, now.AddMinutes(1));
      Assert.True(resumed.Succeeded);
      Assert.NotEqual(resumed.Value!.Capability, string.Empty);

      var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.SetupSessionId == resumed.Value.SessionId);
      Assert.Equal(2, draft.Revision);
      Assert.Equal("tenant-verified", draft.ExpectedTenantId);
      Assert.Equal(Microsoft365RevisionStates.Draft, draft.State);

      var stale = await Microsoft365OnboardingService.SaveDraftAsync(db,
        new(resumed.Value.SessionId, resumed.Value.Capability, 1, "tenant-verified", null, null, null, null, null,
          Microsoft365AccessProfiles.AppMediated, "NOT_CONFIGURED", "NOT_CONFIGURED"), now.AddMinutes(1));
      Assert.False(stale.Succeeded);
      Assert.Equal("revision.stale", stale.ErrorCode);
    }
  }

  [Fact]
  public async Task DraftRejectsUnsafeSiteAndUnknownOptionalState()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    const string proof = "proof";
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(pg.Options);
    var claim = await Microsoft365OnboardingService.ClaimAsync(db, firmId, "install-test", proof, Hash(proof), now);
    var setup = claim.Value!;
    var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.SetupSessionId == setup.SessionId);

    var unsafeUrl = await Microsoft365OnboardingService.SaveDraftAsync(db,
      new(setup.SessionId, setup.Capability, draft.Revision, "tenant", null,
        "http://sharepoint.example.test/sites/audit", null, null, null,
        Microsoft365AccessProfiles.AppMediated, "NOT_CONFIGURED", "NOT_CONFIGURED"), now);
    Assert.False(unsafeUrl.Succeeded);

    var unknownState = await Microsoft365OnboardingService.SaveDraftAsync(db,
      new(setup.SessionId, setup.Capability, draft.Revision, "tenant", null, null, null, null, null,
        Microsoft365AccessProfiles.AppMediated, "VERIFIED", "NOT_CONFIGURED"), now);
    Assert.False(unknownState.Succeeded);
  }

  private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
