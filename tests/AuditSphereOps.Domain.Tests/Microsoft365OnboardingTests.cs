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

  [Fact]
  public async Task FolderTemplates_AreAllowlistedVersionedAndIdempotent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, _, _) = await pg.SeedScopeAsync();
    var admin = new AuditSphereOps.Domain.Security.AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "admin-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "admin@example.test", DisplayName = "Administrator",
      CreatedAt = DateTimeOffset.UtcNow
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(admin);
      db.RoleGrants.Add(new AuditSphereOps.Domain.Security.RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = firmId, UserId = admin.Id, Role = "Administrator",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = admin.Id
      });
      await db.SaveChangesAsync();

      var actor = new AuditSphereOps.Application.Abstractions.ActorContext(
        admin.Id, firmId, admin.SessionEpoch, ["Administrator"]);
      var defaultManifest = Microsoft365ConfigurationService.DefaultManifest(FolderTemplatePurposes.ClientWorkspace);
      var saved = await Microsoft365ConfigurationService.SaveFolderTemplateAsync(db, actor,
        new(FolderTemplatePurposes.ClientWorkspace, defaultManifest),
        DateTimeOffset.UtcNow);
      Assert.True(saved.Succeeded);
      Assert.Equal(1, saved.Value!.Version);

      var repeat = await Microsoft365ConfigurationService.SaveFolderTemplateAsync(db, actor,
        new(FolderTemplatePurposes.ClientWorkspace, "{ \"nodes\": [ { \"key\": \"permanent\", \"name\": \"00_Permanent\", \"children\": [ { \"key\": \"corporate\", \"name\": \"01_Corporate_Records\" }, { \"key\": \"terms\", \"name\": \"02_Engagement_Terms\" }, { \"key\": \"reference\", \"name\": \"03_Shared_Reference\" } ] }, { \"key\": \"engagements\", \"name\": \"Engagements\" } ] }", 1),
        DateTimeOffset.UtcNow);
      Assert.True(repeat.Succeeded);
      Assert.Equal(saved.Value.Id, repeat.Value!.Id);

      var next = await Microsoft365ConfigurationService.SaveFolderTemplateAsync(db, actor,
        new(FolderTemplatePurposes.ClientWorkspace, "{\"nodes\":[{\"key\":\"permanent\",\"name\":\"00_Permanent\"},{\"key\":\"engagements\",\"name\":\"Engagements\"}]}", 1),
        DateTimeOffset.UtcNow);
      Assert.True(next.Succeeded);
      Assert.Equal(2, next.Value!.Version);

      var stale = await Microsoft365ConfigurationService.SaveFolderTemplateAsync(db, actor,
        new(FolderTemplatePurposes.ClientWorkspace, defaultManifest, 1),
        DateTimeOffset.UtcNow);
      Assert.False(stale.Succeeded);
      Assert.Equal("revision.stale", stale.ErrorCode);
    }

    var invalid = Microsoft365ConfigurationService.ValidateFolderTemplate(
      FolderTemplatePurposes.ClientWorkspace,
      "{\"nodes\":[{\"key\":\"bad\",\"name\":\"../secrets\"}]}" );
    Assert.False(invalid.Succeeded);
  }

  [Fact]
  public async Task ConnectionActivation_RequiresExactObservedEvidenceAndApprovedTemplate()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, _, _) = await pg.SeedScopeAsync();
    var admin = new AuditSphereOps.Domain.Security.AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "admin-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "admin@example.test", DisplayName = "Administrator",
      CreatedAt = DateTimeOffset.UtcNow
    };
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(admin);
    db.RoleGrants.Add(new AuditSphereOps.Domain.Security.RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = admin.Id, Role = "Administrator",
      GrantedAt = now, GrantedByUserId = admin.Id
    });
    await db.SaveChangesAsync();
    var actor = new AuditSphereOps.Application.Abstractions.ActorContext(admin.Id, firmId, admin.SessionEpoch, ["Administrator"]);

    var setup = await Microsoft365OnboardingService.ClaimAsync(db, firmId, "install-activation", "proof", Hash("proof"), now);
    var setupClaim = setup.Value!;
    var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.SetupSessionId == setupClaim.SessionId);
    Assert.True((await Microsoft365OnboardingService.SaveDraftAsync(db,
      new(setupClaim.SessionId, setupClaim.Capability, draft.Revision, "tenant-1", "EasyGuide",
        "https://easyguide.sharepoint.com/sites/AuditSphere", "site-1", "drive-1", "root-1",
        Microsoft365AccessProfiles.AppMediated, "NOT_CONFIGURED", "NOT_CONFIGURED"), now)).Succeeded);
    draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.Id == draft.Id);

    var prepared = await Microsoft365ConfigurationService.PrepareConnectionRevisionAsync(db, actor,
      new(draft.Id, draft.Revision, "slot:login-client", "slot:runtime-graph"), now);
    Assert.True(prepared.Succeeded);
    var connectionId = prepared.Value!;
    draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.Id == draft.Id);

    var template = await Microsoft365ConfigurationService.SaveFolderTemplateAsync(db, actor,
      new(FolderTemplatePurposes.ClientWorkspace, Microsoft365ConfigurationService.DefaultManifest(FolderTemplatePurposes.ClientWorkspace)), now);
    Assert.True(template.Succeeded);

    var blockedActivation = await Microsoft365ConfigurationService.ActivateConnectionAsync(db, actor,
      new(draft.Id, connectionId, template.Value!.Id, draft.Revision, "site-1", "drive-1", "root-1",
        "https://easyguide.sharepoint.com/sites/AuditSphere", Microsoft365AccessProfiles.AppMediated), now);
    Assert.False(blockedActivation.Succeeded);
    Assert.Equal("gate.blocked", blockedActivation.ErrorCode);

    Assert.True((await Microsoft365ConfigurationService.ApproveFolderTemplateAsync(db, actor, template.Value!.Id, now)).Succeeded);

    foreach (var (kind, id, operation) in new[]
    {
      ("TENANT", "tenant-1", "CONSENT"), ("SITE", "site-1", "READ"),
      ("DRIVE", "drive-1", "READ"), ("ROOT", "root-1", "READ")
    })
    {
      var evidence = await Microsoft365ConfigurationService.RecordVerificationEvidenceAsync(db, actor,
        new(draft.Id, connectionId, kind, id, operation, "runtime:acceptance", "PASS", $"evidence:{kind.ToLowerInvariant()}"), now);
      Assert.True(evidence.Succeeded);
    }

    draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.Id == draft.Id);
    var activated = await Microsoft365ConfigurationService.ActivateConnectionAsync(db, actor,
      new(draft.Id, connectionId, template.Value!.Id, draft.Revision, "site-1", "drive-1", "root-1",
        "https://easyguide.sharepoint.com/sites/AuditSphere", Microsoft365AccessProfiles.AppMediated), now);
    Assert.True(activated.Succeeded);
    Assert.Equal(Microsoft365RevisionStates.Active,
      await db.Microsoft365ConnectionRevisions.Where(x => x.Id == connectionId).Select(x => x.State).SingleAsync());
    Assert.Equal(1, await db.FirmWorkspaceConfigurations.CountAsync(x => x.FirmId == firmId && x.DefaultForFutureClients));
  }

  private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
