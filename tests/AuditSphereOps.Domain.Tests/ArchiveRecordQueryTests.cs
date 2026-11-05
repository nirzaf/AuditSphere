using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

public sealed class ArchiveRecordQueryTests
{
  [Theory]
  [InlineData(null)]
  [InlineData(0)]
  [InlineData(100)]
  public async Task UnbuiltArchiveHasAnEmptyPageEvenWhenAnOldCursorIsSupplied(int? afterOrdinal)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var archiveId = Guid.NewGuid();
    db.Archives.Add(new Archive
    {
      Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      ProfileId = "SYNTHETIC-UNBUILT", ProfileVersion = 1,
      Status = ArchiveStates.AssemblyInProgress, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var result = await AuditRecordQueries.ArchiveAsync(db, PbcSeed.Actor(f.Staff, "Staff"), archiveId, afterOrdinal);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(archiveId, result.Value!.Id);
    Assert.Null(result.Value.ManifestVersion);
    Assert.Empty(result.Value.Entries);
    Assert.Equal(0, result.Value.TotalEntryCount);
    Assert.Null(result.Value.NextOrdinal);
    Assert.Null(result.Value.SecondarySharePointCopy);
  }

  [Fact]
  public async Task ExportEligibilityIsSeparateFromReadAccessAndRequiresTheSafeReviewedPayload()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var archiveId = Guid.NewGuid();
    var manifestId = Guid.NewGuid();
    var content = "{\"schema\":\"records-export.v2\"}";
    var bytes = System.Text.Encoding.UTF8.GetBytes(content);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Archives.Add(new Archive
    {
      Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      ProfileId = "SYNTHETIC-REVIEWED", ProfileVersion = 1,
      Status = ArchiveStates.AssemblyReviewed, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ArchiveManifests.Add(new ArchiveManifest
    {
      Id = manifestId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      ArchiveId = archiveId, Version = 1, Status = "REVIEWED", ManifestDigest = new string('a', 64),
      EntryCount = 1, CompletenessStatus = "COMPLETE", BuiltAt = DateTimeOffset.UtcNow,
      ReviewedAt = DateTimeOffset.UtcNow, ReviewedByUserId = f.Admin.Id
    });
    db.ArchiveStructuredExports.Add(new ArchiveStructuredExport
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      ArchiveId = archiveId, ArchiveManifestId = manifestId, Version = 1,
      Schema = ArchiveStructuredExportSchemas.EngagementV2, PayloadJson = content,
      ContentHash = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(bytes), ByteCount = bytes.Length,
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var staff = await AuditRecordQueries.ArchiveAsync(db, PbcSeed.Actor(f.Staff, "Staff"), archiveId);
    Assert.True(staff.Succeeded, staff.Message);
    Assert.False(staff.Value!.CanExport);
    var custodian = await AuditRecordQueries.ArchiveAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), archiveId);
    Assert.True(custodian.Succeeded, custodian.Message);
    Assert.True(custodian.Value!.CanExport);
  }
}
