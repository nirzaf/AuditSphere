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
  }
}
