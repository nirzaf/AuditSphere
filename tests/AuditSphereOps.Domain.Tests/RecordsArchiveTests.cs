using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class RecordsArchiveTests
{
  [Fact]
  public async Task ArchiveWorkflow_StoresStructuredManifest_AndRequiresObservedProtection()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    var archiveId = Guid.CreateVersion7();

    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.RecordsProfiles.Add(new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ProfileCode = "AUDIT-RECORDS",
        Version = 1, RecordClass = "Issued audit file", Jurisdiction = "TEST",
        ServiceRoute = "STATUTORY_AUDIT", RetentionTrigger = "REPORT_DATE",
        RetentionDurationDays = null, ProtectionMode = "PURVIEW_RECORD",
        LabelId = "label-test-audit-record", LegalHoldBehavior = "SUSPEND_DISPOSITION",
        AmendmentRoute = "CONTROLLED_AMENDMENT", DispositionOwner = "Records Custodian",
        BackupRequirements = "Protected checkpoint and restore rehearsal", CreatedByUserId = scope.Actor.UserId,
        Approved = true, ApprovedAt = DateTimeOffset.UtcNow, ApprovedByUserId = scope.Actor.UserId,
        CreatedAt = DateTimeOffset.UtcNow
      });
      seed.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = scope.FirmId, ClientId = scope.ClientId,
        EngagementId = scope.EngagementId, ProfileId = "AUDIT-RECORDS", ProfileVersion = 1,
        Status = ArchiveStates.Issued, CreatedAt = DateTimeOffset.UtcNow
      });
      await seed.SaveChangesAsync();
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    var built = await RecordsArchiveService.BuildManifestAsync(db, scope.Actor, archiveId);
    Assert.True(built.Succeeded, built.Message);
    Assert.Equal(ArchiveStates.ManifestBuilt, (await db.Archives.AsNoTracking().SingleAsync(x => x.Id == archiveId)).Status);
    Assert.Equal(1, await db.ArchiveManifestEntries.CountAsync(x => x.ArchiveManifestId == built.Value!.ManifestId));
    Assert.Contains("records-export.v1", await db.ArchiveManifestEntries
      .Where(x => x.ArchiveManifestId == built.Value!.ManifestId && x.EntryKind == "STRUCTURED_EXPORT")
      .Select(x => x.MetadataJson).SingleAsync());
    var structuredExport = await db.ArchiveStructuredExports
      .Where(x => x.ArchiveManifestId == built.Value!.ManifestId).SingleAsync();
    Assert.Equal("records-export.v1", structuredExport.Schema);
    Assert.Contains("\"accounting\"", structuredExport.PayloadJson);
    Assert.Contains("\"activityEvents\"", structuredExport.PayloadJson);
    Assert.Equal(structuredExport.ByteCount, System.Text.Encoding.UTF8.GetByteCount(structuredExport.PayloadJson));

    var reviewed = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId);
    Assert.True(reviewed.Succeeded, reviewed.Message);
    Assert.True((await db.ArchiveManifests.SingleAsync(x => x.ArchiveId == archiveId)).ReviewedAt.HasValue);

    Assert.True((await RecordsArchiveService.RequestRecordsActionAsync(db, scope.Actor,
      new RequestRecordsActionRequest(archiveId, "purview-request-test-1"))).Succeeded);
    Assert.Equal(ArchiveStates.RecordsActionRequested, (await db.Archives.AsNoTracking().SingleAsync(x => x.Id == archiveId)).Status);
    Assert.Equal("REQUESTED", await db.RecordsActions.Where(x => x.ArchiveId == archiveId).Select(x => x.State).SingleAsync());
    Assert.Equal("REQUESTED", await db.RecordsActionEvidences.Where(x => x.ArchiveId == archiveId)
      .OrderBy(x => x.Sequence).Select(x => x.EventKind).SingleAsync());

    var observed = await RecordsArchiveService.ObserveRecordsActionAsync(db, scope.Actor,
      new ObserveRecordsActionRequest(archiveId, "label-test-audit-record", "RECORD_LOCKED", "records-admin@test", "purview-observation-1"));
    Assert.True(observed.Succeeded, observed.Message);
    Assert.Equal(ArchiveStates.ProtectionObserved, (await db.Archives.AsNoTracking().SingleAsync(x => x.Id == archiveId)).Status);
    Assert.Equal(new[] { "REQUESTED", "OBSERVED" }, await db.RecordsActionEvidences.Where(x => x.ArchiveId == archiveId)
      .OrderBy(x => x.Sequence).Select(x => x.EventKind).ToArrayAsync());
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE records_action_evidence SET event_kind = 'FAILED' WHERE archive_id = {archiveId}
      """));

    var verified = await RecordsArchiveService.VerifyArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(verified.Succeeded, verified.Message);
    Assert.Equal(ArchiveStates.ArchiveVerified, (await db.Archives.AsNoTracking().SingleAsync(x => x.Id == archiveId)).Status);

    Assert.True((await RecordsArchiveService.RequestLegalHoldAsync(db, scope.Actor,
      new RequestLegalHoldRequest(archiveId, "HOLD-001", "Synthetic test hold"))).Succeeded);
    Assert.True((await RecordsArchiveService.ObserveLegalHoldAsync(db, scope.Actor,
      new ObserveLegalHoldRequest(archiveId, "HOLD-001", "purview-hold-1"))).Succeeded);
    var hold = await db.LegalHolds.SingleAsync(x => x.ArchiveId == archiveId);
    Assert.Equal("OBSERVED", hold.State);
    Assert.NotNull(hold.ObservedAt);
  }

  [Fact]
  public async Task ArchiveWorkflow_DoesNotTreatRequestedRecordsActionAsProtection()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var archiveId = Guid.CreateVersion7();
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.RecordsProfiles.Add(new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.Primary.FirmId, ProfileCode = "AUDIT-RECORDS",
        Version = 1, RecordClass = "Issued audit file", Jurisdiction = "TEST",
        ServiceRoute = "STATUTORY_AUDIT", RetentionTrigger = "REPORT_DATE",
        ProtectionMode = "PURVIEW_RECORD", LabelId = "label-test-audit-record",
        LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
        DispositionOwner = "Records Custodian", BackupRequirements = "Protected checkpoint",
        CreatedByUserId = fixture.Primary.Actor.UserId, Approved = true,
        ApprovedAt = DateTimeOffset.UtcNow, ApprovedByUserId = fixture.Primary.Actor.UserId,
        CreatedAt = DateTimeOffset.UtcNow
      });
      seed.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = fixture.Primary.FirmId, ClientId = fixture.Primary.ClientId,
        EngagementId = fixture.Primary.EngagementId, ProfileId = "AUDIT-RECORDS", ProfileVersion = 1,
        Status = ArchiveStates.AssemblyReviewed, CreatedAt = DateTimeOffset.UtcNow
      });
      var manifest = new ArchiveManifest
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.Primary.FirmId, ClientId = fixture.Primary.ClientId,
        EngagementId = fixture.Primary.EngagementId, ArchiveId = archiveId, Version = 1, Status = "REVIEWED",
        ManifestDigest = new string('a', 64), EntryCount = 1, CompletenessStatus = "COMPLETE",
        BuiltAt = DateTimeOffset.UtcNow, ReviewedAt = DateTimeOffset.UtcNow, ReviewedByUserId = fixture.Primary.Actor.UserId
      };
      seed.ArchiveManifests.Add(manifest);
      await seed.SaveChangesAsync();
      await Assert.ThrowsAsync<PostgresException>(() => seed.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE archives SET status = 'ARCHIVE_VERIFIED' WHERE id = {archiveId}
        """));
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    var requested = await RecordsArchiveService.RequestRecordsActionAsync(db, fixture.Primary.Actor,
      new RequestRecordsActionRequest(archiveId, null));
    Assert.True(requested.Succeeded, requested.Message);
    Assert.Equal(ArchiveStates.RecordsActionRequested, await db.Archives.Where(x => x.Id == archiveId).Select(x => x.Status).SingleAsync());
    Assert.NotEqual(ArchiveStates.ProtectionObserved, await db.Archives.Where(x => x.Id == archiveId).Select(x => x.Status).SingleAsync());
  }
}
