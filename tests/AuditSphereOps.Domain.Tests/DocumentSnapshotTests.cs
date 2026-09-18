using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class DocumentSnapshotTests
{
  [Fact]
  public async Task CaptureComputesExactHash_AndRejectsDuplicateVersion()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var bytes = new byte[] { 0, 1, 255, 42 };

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var first = await DocumentSnapshotService.CaptureAsync(db, fixture.Actor,
        new CaptureDocumentSnapshotRequest(fixture.DocumentReferenceId, "v1", bytes));
      Assert.True(first.Succeeded);
      Assert.Equal(Hashing.Sha256Hex(bytes), first.Value!.Sha256Hex);
      Assert.Equal(bytes.Length, first.Value.ByteCount);

      var duplicate = await DocumentSnapshotService.CaptureAsync(db, fixture.Actor,
        new CaptureDocumentSnapshotRequest(fixture.DocumentReferenceId, "v1", [9]));
      Assert.False(duplicate.Succeeded);
      Assert.Equal("documents.snapshot-duplicate", duplicate.ErrorCode);

      var second = await DocumentSnapshotService.CaptureAsync(db, fixture.Actor,
        new CaptureDocumentSnapshotRequest(fixture.DocumentReferenceId, "v2", []));
      Assert.True(second.Succeeded);
      Assert.Equal(Hashing.Sha256Hex(Array.Empty<byte>()), second.Value!.Sha256Hex);
      Assert.Equal(0, second.Value.ByteCount);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var snapshots = await db.DocumentSnapshots
        .Where(x => x.DocumentReferenceId == fixture.DocumentReferenceId)
        .OrderBy(x => x.VersionId).ToListAsync();
      Assert.Equal(2, snapshots.Count);
      Assert.Equal("v1", snapshots[0].VersionId);
      Assert.Equal("v2", snapshots[1].VersionId);
      Assert.All(snapshots, x => Assert.Matches("^[0-9a-f]{64}$", x.Sha256Hex));

      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE document_snapshots SET sha256_hex = {new string('0', 64)}
        WHERE firm_id = {fixture.FirmId} AND document_reference_id = {fixture.DocumentReferenceId}
        """));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM document_snapshots
        WHERE firm_id = {fixture.FirmId} AND document_reference_id = {fixture.DocumentReferenceId}
        """));
    }
  }

  [Fact]
  public async Task DatabaseRejectsInvalidHash_AndCrossScopeSnapshot()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var reference = await db.DocumentReferences.SingleAsync(x => x.Id == fixture.DocumentReferenceId);

    db.DocumentSnapshots.Add(new DocumentSnapshot
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = reference.ClientId,
      EngagementId = reference.EngagementId, DocumentReferenceId = reference.Id,
      DriveId = reference.DriveId, ItemId = reference.ItemId, VersionId = "invalid-hash",
      Sha256Hex = "not-a-sha256", ByteCount = 1, CapturedBy = fixture.Actor.UserId.ToString("D"),
      CapturedAt = DateTimeOffset.UtcNow
    });
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    db.ChangeTracker.Clear();

    db.DocumentSnapshots.Add(new DocumentSnapshot
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = Guid.NewGuid(),
      EngagementId = Guid.NewGuid(), DocumentReferenceId = reference.Id,
      DriveId = reference.DriveId, ItemId = reference.ItemId, VersionId = "cross-scope",
      Sha256Hex = new string('a', 64), ByteCount = 1, CapturedBy = fixture.Actor.UserId.ToString("D"),
      CapturedAt = DateTimeOffset.UtcNow
    });
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
  }

  [Fact]
  public async Task CaptureDeniesForeignFirmActor()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var denied = await DocumentSnapshotService.CaptureAsync(db,
      fixture.Actor with { FirmId = Guid.NewGuid() },
      new CaptureDocumentSnapshotRequest(fixture.DocumentReferenceId, "v1", [1]));
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var scope = await pg.SeedScopeAsync();
    var user = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId,
      Subject = "snapshot-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-" + Guid.NewGuid().ToString("N"),
      Email = "snapshot@example.test", DisplayName = "Snapshot operator",
      CreatedAt = DateTimeOffset.UtcNow
    };
    var referenceId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(user);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = user.Id, Role = "Staff",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
    });
    db.DocumentReferences.Add(new DocumentReference
    {
      Id = referenceId, FirmId = scope.FirmId, ClientId = scope.ClientId,
      EngagementId = scope.EngagementId, Provider = "SharePoint", DriveId = "drive-1",
      ItemId = "item-1", Path = "/Documents/source.xlsx", Purpose = "Evidence",
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    return new Fixture(scope.FirmId, user.Id,
      new ActorContext(user.Id, scope.FirmId, user.SessionEpoch, ["Staff"]), referenceId);
  }

  private sealed record Fixture(Guid FirmId, Guid UserId, ActorContext Actor, Guid DocumentReferenceId);
}
