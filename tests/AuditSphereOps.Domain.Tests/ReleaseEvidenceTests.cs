using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class ReleaseEvidenceTests
{
  [Fact]
  public async Task NT19_SignatureLineage_PreservesLineageWhenPreSignDiffersFromSignedHash()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("manifest-for-signature-lineage-test");
    var preSignDigest = Hashing.Sha256Hex(manifestBytes);
    var signedBytes = System.Text.Encoding.UTF8.GetBytes("manifest-for-signature-lineage-test-signed-envelope");
    var signedDigest = Hashing.Sha256Hex(signedBytes);

    Assert.NotEqual(preSignDigest, signedDigest);

    var store = new LocalAppendOnlyCheckpointStore(Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N")));
    Guid candidateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approval = await ApprovalService.CreateAsync(db, fixture.Reviewer,
        new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, preSignDigest));
      Assert.True(approval.Succeeded);

      var candidate = await ReleaseService.CreateCandidateAsync(db, fixture.Partner,
        new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", fixture.WorkpaperId, 1, 1, 1, preSignDigest));
      Assert.True(candidate.Succeeded);
      candidateId = candidate.Value;

      var cpResult = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, store, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "release-nt19", preSignDigest, manifestBytes));
      Assert.True(cpResult.Succeeded);

      // Seed SignatureLineage where pre-sign hash != signed artifact hash
      db.SignatureLineages.Add(new SignatureLineage
      {
        Id = Guid.CreateVersion7(),
        FirmId = fixture.FirmId,
        ClientId = fixture.ClientId,
        EngagementId = fixture.EngagementId,
        CandidateId = candidateId,
        PreSignArtifactHash = preSignDigest,
        SignedArtifactHash = signedDigest,
        SigningMethod = "X509-PKCS7-SHA256",
        RequestIdentity = "partner-signer-001",
        VerificationOutcome = "VERIFIED",
        Verifier = fixture.Partner.UserId.ToString("D"),
        VerifiedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var options = new ReleaseSafetyOptions
      {
        RequireSignatureLineage = true,
        RequireExternalCheckpointBeforeDelivery = true
      };

      var issued = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, preSignDigest, "release-nt19"), options);
      Assert.True(issued.Succeeded);

      var lineage = await db.SignatureLineages.SingleAsync(x => x.CandidateId == candidateId);
      Assert.Equal(preSignDigest, lineage.PreSignArtifactHash);
      Assert.Equal(signedDigest, lineage.SignedArtifactHash);
      Assert.Equal("VERIFIED", lineage.VerificationOutcome);

      // Immutability: triggers prevent UPDATE or DELETE
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE signature_lineages SET signed_artifact_hash = {preSignDigest} WHERE id = {lineage.Id}
        """));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM signature_lineages WHERE id = {lineage.Id}
        """));
    }
  }

  [Fact]
  public async Task ReleaseGate_RefusesUnsignedRelease_WhenRequireSignatureLineageIsTrue()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("manifest-for-unsigned-release-test");
    var digest = Hashing.Sha256Hex(manifestBytes);
    var store = new LocalAppendOnlyCheckpointStore(Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N")));
    Guid candidateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approval = await ApprovalService.CreateAsync(db, fixture.Reviewer,
        new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(approval.Succeeded);

      var candidate = await ReleaseService.CreateCandidateAsync(db, fixture.Partner,
        new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(candidate.Succeeded);
      candidateId = candidate.Value;

      var cpResult = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, store, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "release-unsigned", digest, manifestBytes));
      Assert.True(cpResult.Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var options = new ReleaseSafetyOptions
      {
        RequireSignatureLineage = true,
        RequireExternalCheckpointBeforeDelivery = true
      };

      var blocked = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-unsigned"), options);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      Assert.Contains("Signature lineage is absent", blocked.Message);
      Assert.Empty(await db.Releases.ToListAsync());
    }
  }

  [Fact]
  public async Task ReleaseGate_RefusesExpiredProtectionAttestation_WhenRequireProtectionAttestationIsTrue()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("manifest-for-attestation-test");
    var digest = Hashing.Sha256Hex(manifestBytes);
    var store = new LocalAppendOnlyCheckpointStore(Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N")));
    Guid candidateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approval = await ApprovalService.CreateAsync(db, fixture.Reviewer,
        new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(approval.Succeeded);

      var candidate = await ReleaseService.CreateCandidateAsync(db, fixture.Partner,
        new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(candidate.Succeeded);
      candidateId = candidate.Value;

      var cpResult = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, store, fixture.Partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "release-attest", digest, manifestBytes));
      Assert.True(cpResult.Succeeded);

      // Seed expired attestation
      db.ProtectionAttestations.Add(new ProtectionAttestation
      {
        Id = Guid.CreateVersion7(),
        FirmId = fixture.FirmId,
        ClientId = fixture.ClientId,
        EngagementId = fixture.EngagementId,
        ArtifactId = fixture.WorkpaperId,
        ArtifactHash = digest,
        Binding = "sharepoint:site-123:drive-456",
        ProfileId = "AUDIT-RECORD-RETENTION",
        ProfileVersion = 1,
        ObservedState = "PROTECTED",
        VerificationTime = DateTimeOffset.UtcNow.AddDays(-30),
        Verifier = "purview-reconciler",
        ExpiryTime = DateTimeOffset.UtcNow.AddMinutes(-10), // expired!
        RecheckRule = "HOURLY",
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
      });
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var options = new ReleaseSafetyOptions
      {
        RequireProtectionAttestation = true,
        RequireExternalCheckpointBeforeDelivery = true
      };

      var blocked = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-attest"), options);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      Assert.Contains("Protection attestation has expired", blocked.Message);
      Assert.Empty(await db.Releases.ToListAsync());

      // Immutability trigger test
      var att = await db.ProtectionAttestations.SingleAsync(x => x.ArtifactHash == digest);
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE protection_attestations SET observed_state = 'EXPIRED' WHERE id = {att.Id}
        """));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM protection_attestations WHERE id = {att.Id}
        """));
    }
  }

  [Fact]
  public void ReleaseSafetyOptions_StartupValidation_RefusesLiveActivationWithoutEvidence()
  {
    var options = new ReleaseSafetyOptions();

    // LiveAuditRelease=true without ExternalEffects:Enabled=true -> refuses startup
    var ex1 = Assert.Throws<InvalidOperationException>(() =>
      options.Validate(externalEffectsEnabled: false, allowSimulationAdapters: true,
        environmentName: "Development", liveAuditRelease: true));
    Assert.Contains("LiveAuditRelease requires ExternalEffects:Enabled=true", ex1.Message);

    // Production environment with simulation adapters -> refuses startup
    var ex2 = Assert.Throws<InvalidOperationException>(() =>
      options.Validate(externalEffectsEnabled: false, allowSimulationAdapters: true,
        environmentName: "Production", liveAuditRelease: false));
    Assert.Contains("Simulation adapters are forbidden in production", ex2.Message);

    // Valid Development configuration
    options.Validate(externalEffectsEnabled: false, allowSimulationAdapters: true,
      environmentName: "Development", liveAuditRelease: false);
  }

  [Fact]
  public async Task LocalAppendOnlyCheckpointStore_EnforcesContentAddressedIntegrityAndDetectsConflicts()
  {
    var rootDir = Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N"));
    var store = new LocalAppendOnlyCheckpointStore(rootDir);
    var bytes = System.Text.Encoding.UTF8.GetBytes("manifest-checkpoint-payload-integrity-test");
    var digest = Hashing.Sha256Hex(bytes);

    // Write succeeds and verifies read-back
    var receipt = await store.WriteAsync(digest, bytes);
    Assert.Equal(digest, receipt.ContentSha256Hex);
    Assert.Equal(bytes.Length, receipt.ByteCount);
    Assert.True(File.Exists(receipt.Reference));

    // Second write with same bytes is idempotent
    var repeatReceipt = await store.WriteAsync(digest, bytes);
    Assert.Equal(receipt.Reference, repeatReceipt.Reference);
    Assert.Equal(receipt.ContentSha256Hex, repeatReceipt.ContentSha256Hex);

    // Conflict: write different bytes claiming same digest throws conflict
    var conflictBytes = System.Text.Encoding.UTF8.GetBytes("different-content-conflict");
    await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(digest, conflictBytes));

    // Verify
    var verified = await store.VerifyAsync(receipt.Reference, digest);
    Assert.NotNull(verified);
    Assert.Equal(digest, verified.ContentSha256Hex);

    // Probe
    var probed = await store.ProbeAsync(digest);
    Assert.NotNull(probed);
    Assert.Equal(digest, probed.ContentSha256Hex);

    // Probe non-existent
    var nonExistent = await store.ProbeAsync(new string('0', 64));
    Assert.Null(nonExistent);
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var scope = await pg.SeedScopeAsync();
    var reviewer = NewUser(scope.FirmId, "reviewer");
    var partner = NewUser(scope.FirmId, "partner");
    var workpaperId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.AddRange(reviewer, partner);
    db.RoleGrants.AddRange(
      NewGrant(scope, reviewer, "Reviewer"), NewGrant(scope, partner, "Partner"));
    db.Workpapers.Add(new Workpaper
    {
      Id = workpaperId, FirmId = scope.FirmId, ClientId = scope.ClientId,
      EngagementId = scope.EngagementId, ActorId = partner.Id, Index = "R-02",
      Title = "Release evidence workpaper", Objective = "Validate evidence gates",
      TemplateVersion = "RELEASE-2026-v1", Procedure = "Agree release evidence",
      Status = "WORKING", CreatedAt = DateTimeOffset.UtcNow
    });
    await db.Engagements.Where(x => x.FirmId == scope.FirmId && x.Id == scope.EngagementId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProfessionalWorkBlocked, false));
    await db.SaveChangesAsync();
    return new Fixture(scope.FirmId, scope.ClientId, scope.EngagementId, workpaperId,
      Actor(reviewer, "Reviewer"), Actor(partner, "Partner"));
  }

  private static AppUser NewUser(Guid firmId, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = role + "-" + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-" + Guid.NewGuid().ToString("N"), Email = role + "@example.test",
    DisplayName = role, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant NewGrant((Guid FirmId, Guid ClientId, Guid EngagementId) scope,
    AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = user.Id, Role = role,
    ClientId = scope.ClientId, EngagementId = scope.EngagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId, Guid WorkpaperId,
    ActorContext Reviewer, ActorContext Partner);
}
