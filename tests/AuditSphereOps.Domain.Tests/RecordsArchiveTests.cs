using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class RecordsArchiveTests
{
  [Fact]
  public async Task EngagementScopedPartner_CannotCreateFirmWideRecordsProfile()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var request = new CreateRecordsProfileRequest("AUDIT-TEST", 1, "Audit record", "QA",
      "STATUTORY_AUDIT", "REPORT_DATE", null, "PURVIEW_RECORD", "label-test",
      "SUSPEND_DISPOSITION", "CONTROLLED_AMENDMENT", "Records Custodian", "Protected backup");

    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await RecordsArchiveService.CreateProfileAsync(db, fixture.Primary.Actor, request);

    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    Assert.Empty(await db.RecordsProfiles.ToListAsync());
  }

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
      var groupId = Guid.CreateVersion7();
      seed.ClientGroups.Add(new ClientGroup
      {
        Id = groupId, FirmId = scope.FirmId, Code = "ARCHIVE-GROUP", Name = "Archive group",
        CreatedByUserId = scope.Actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      });
      seed.ClientGroupMemberships.Add(new ClientGroupMembership
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, GroupId = groupId, ClientId = scope.ClientId,
        EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow), ControlMethod = "CONTROLLED",
        OwnershipPercent = 100, EconomicInterestPercent = 100, EvidenceReference = "archive-membership",
        Status = AccountingWorkflowStates.Approved, CreatedByUserId = scope.Actor.UserId,
        CreatedAt = DateTimeOffset.UtcNow
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
    Assert.Contains("\"reportingPeriods\"", structuredExport.PayloadJson);
    Assert.Contains("\"periodAmendments\"", structuredExport.PayloadJson);
    Assert.Contains("\"specialistSchedules\"", structuredExport.PayloadJson);
    Assert.Contains("\"accountingEvidenceAuditLinks\"", structuredExport.PayloadJson);
    Assert.Contains("\"accountingEvidenceActions\"", structuredExport.PayloadJson);
    Assert.Contains("\"valuationPreparations\"", structuredExport.PayloadJson);
    Assert.Contains("\"clientGroups\"", structuredExport.PayloadJson);
    Assert.Contains("\"groupMemberships\"", structuredExport.PayloadJson);
    Assert.Contains("\"activityEvents\"", structuredExport.PayloadJson);
    Assert.Equal(structuredExport.ByteCount, System.Text.Encoding.UTF8.GetByteCount(structuredExport.PayloadJson));

    var reviewed = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId);
    Assert.True(reviewed.Succeeded, reviewed.Message);
    Assert.True((await db.ArchiveManifests.SingleAsync(x => x.ArchiveId == archiveId)).ReviewedAt.HasValue);

    var localExport = await RecordsArchiveService.ExportReviewedArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(localExport.Succeeded, localExport.Message);
    Assert.Equal("BLOCKED_EXTERNAL", localExport.Value!.ProviderProtectionState);
    Assert.Equal(Hashing.Sha256Hex(localExport.Value.Content), localExport.Value.ContentSha256);
    using (var exportJson = System.Text.Json.JsonDocument.Parse(localExport.Value.Content))
    {
      Assert.Equal("auditsphere-archive-evidence-export.v1",
        exportJson.RootElement.GetProperty("schema").GetString());
      Assert.Equal("BLOCKED_EXTERNAL", exportJson.RootElement.GetProperty("providerProtection")
        .GetProperty("state").GetString());
      Assert.Equal(1, exportJson.RootElement.GetProperty("manifest").GetProperty("entryCount").GetInt32());
      Assert.False(exportJson.RootElement.GetProperty("providerProtection")
        .GetProperty("externalDocumentBytesIncluded").GetBoolean());
    }
    var unprivilegedId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = unprivilegedId, FirmId = scope.FirmId, Subject = "archive-unprivileged-" + unprivilegedId.ToString("N"),
      TenantId = "tenant-archive-test", Email = "unprivileged@example.test", DisplayName = "Unprivileged reader",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var unprivileged = new ActorContext(unprivilegedId, scope.FirmId, 1, ["Staff"]);
    var deniedExport = await RecordsArchiveService.ExportReviewedArchiveAsync(db, unprivileged, archiveId);
    Assert.False(deniedExport.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, deniedExport.ErrorCode);

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
    var verifiedExport = await RecordsArchiveService.ExportReviewedArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(verifiedExport.Succeeded, verifiedExport.Message);
    Assert.Equal("VERIFIED", verifiedExport.Value!.ProviderProtectionState);

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

  [Fact]
  public async Task ReArchive_ProducesSeparateVersionWithPredecessorLink()
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
        BackupRequirements = "Protected checkpoint", CreatedByUserId = scope.Actor.UserId,
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
    var v1 = await RecordsArchiveService.BuildManifestAsync(db, scope.Actor, archiveId);
    Assert.True(v1.Succeeded, v1.Message);

    var manifest1 = await db.ArchiveManifests.SingleAsync(x => x.Id == v1.Value!.ManifestId);
    Assert.Equal(1, manifest1.Version);
    Assert.Null(manifest1.PredecessorManifestId);
    Assert.Null(manifest1.SupersededByManifestId);

    // Re-archive: rebuild manifest produces v2 chaining back to v1
    var v2 = await RecordsArchiveService.BuildManifestAsync(db, scope.Actor, archiveId);
    Assert.True(v2.Succeeded, v2.Message);

    var manifest2 = await db.ArchiveManifests.SingleAsync(x => x.Id == v2.Value!.ManifestId);
    Assert.Equal(2, manifest2.Version);
    Assert.Equal(manifest1.Id, manifest2.PredecessorManifestId);
    Assert.Null(manifest2.SupersededByManifestId);

    // Prior manifest is now marked as superseded
    await db.Entry(manifest1).ReloadAsync();
    Assert.Equal(manifest2.Id, manifest1.SupersededByManifestId);

    // Superseded manifest cannot be reviewed
    var reviewSuperseded = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId, manifest1.Id);
    Assert.False(reviewSuperseded.Succeeded);
    Assert.Equal(ErrorCodes.ProtectedState, reviewSuperseded.ErrorCode);
  }

  [Fact]
  public async Task VerifyArchive_BlockedByActiveLegalHold()
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
        ProtectionMode = "PURVIEW_RECORD", LabelId = "label-test-audit-record",
        LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
        DispositionOwner = "Records Custodian", BackupRequirements = "Protected checkpoint",
        CreatedByUserId = scope.Actor.UserId, Approved = true,
        ApprovedAt = DateTimeOffset.UtcNow, ApprovedByUserId = scope.Actor.UserId,
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
    Assert.True((await RecordsArchiveService.BuildManifestAsync(db, scope.Actor, archiveId)).Succeeded);
    Assert.True((await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId)).Succeeded);
    Assert.True((await RecordsArchiveService.RequestRecordsActionAsync(db, scope.Actor,
      new RequestRecordsActionRequest(archiveId, "req-1"))).Succeeded);
    Assert.True((await RecordsArchiveService.ObserveRecordsActionAsync(db, scope.Actor,
      new ObserveRecordsActionRequest(archiveId, "label-test-audit-record", "RECORD_LOCKED", "records-admin@test", "obs-1"))).Succeeded);

    // Apply and observe an active legal hold
    Assert.True((await RecordsArchiveService.RequestLegalHoldAsync(db, scope.Actor,
      new RequestLegalHoldRequest(archiveId, "HOLD-LITIGATION-01", "Pending litigation"))).Succeeded);
    Assert.True((await RecordsArchiveService.ObserveLegalHoldAsync(db, scope.Actor,
      new ObserveLegalHoldRequest(archiveId, "HOLD-LITIGATION-01", "purview-hold-lit-1"))).Succeeded);

    // Verification must be blocked while hold is active
    var blocked = await RecordsArchiveService.VerifyArchiveAsync(db, scope.Actor, archiveId);
    Assert.False(blocked.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);

    // Release the hold
    var released = await RecordsArchiveService.ReleaseLegalHoldAsync(db, scope.Actor,
      new ReleaseLegalHoldRequest(archiveId, "HOLD-LITIGATION-01", "Litigation resolved"));
    Assert.True(released.Succeeded, released.Message);

    // Now verification succeeds
    var verified = await RecordsArchiveService.VerifyArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(verified.Succeeded, verified.Message);
    Assert.Equal(ArchiveStates.ArchiveVerified, (await db.Archives.AsNoTracking().SingleAsync(x => x.Id == archiveId)).Status);
  }

  [Fact]
  public async Task BuildManifest_RequiresCurrentApprovedProfile()
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
        ProtectionMode = "PURVIEW_RECORD", LabelId = "label-test-audit-record",
        LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
        DispositionOwner = "Records Custodian", BackupRequirements = "Protected checkpoint",
        CreatedByUserId = scope.Actor.UserId, Approved = false, // Not approved!
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
    Assert.False(built.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, built.ErrorCode);
  }

  [Fact]
  public async Task ReArchive_DigestStableForIdenticalContent()
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
        ProtectionMode = "PURVIEW_RECORD", LabelId = "label-test-audit-record",
        LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
        DispositionOwner = "Records Custodian", BackupRequirements = "Protected checkpoint",
        CreatedByUserId = scope.Actor.UserId, Approved = true,
        ApprovedAt = DateTimeOffset.UtcNow, ApprovedByUserId = scope.Actor.UserId,
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
    var v1 = await RecordsArchiveService.BuildManifestAsync(db, scope.Actor, archiveId);
    Assert.True(v1.Succeeded);

    var v2 = await RecordsArchiveService.BuildManifestAsync(db, scope.Actor, archiveId);
    Assert.True(v2.Succeeded);

    // Identical underlying documents & structured export produce identical manifest digests
    Assert.Equal(v1.Value!.Digest, v2.Value!.Digest);
  }

  [Trait("CompletionUnit", "WU-023")]
  [Fact]
  public void ArchiveCopyTargetAndPermissionReadback_RejectCrossClientAndStaffWriteEvenWithMatchingHashes()
  {
    var firmId = Guid.CreateVersion7();
    var clientA = Guid.CreateVersion7();
    var clientB = Guid.CreateVersion7();
    var siteA = Guid.CreateVersion7();
    var target = CreateArchiveCopyTarget(firmId, clientA, siteA);
    var otherClientArchive = new Archive { FirmId = firmId, ClientId = clientB };

    var crossClient = Assert.Throws<OperationBlockedException>(() =>
      ArchiveCopySourceGuard.EnsureTargetClientBinding(otherClientArchive, target, siteA));
    Assert.Equal("archive-copy-target-client-scope-mismatch", crossClient.Code);

    var expectedRead = ArchiveCopyPermissionEncoding.Encode(
      ArchiveCopyPermissionEncoding.ClientStaffRead(target.StaffGroupId));
    var observedWriteGrants = ArchiveCopyPermissionEncoding.Canonicalize(
      [new ArchiveCopyPermissionGrant("SHAREPOINT_GROUP", target.StaffGroupId, "WRITE")]);
    var observedWrite = ArchiveCopyPermissionEncoding.Encode(observedWriteGrants);
    Assert.False(ArchiveCopyPermissionEncoding.IsExactClientStaffReadGrant(
      observedWriteGrants, target.StaffGroupId));

    ArchiveCopyPermissionObservation MakeObservation((string Json, string Sha256) expected,
      (string Json, string Sha256) observed) => ArchiveCopyPermissionObservation.Create(
        Guid.CreateVersion7(), firmId, clientA, Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
        Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('a', 64),
        Guid.CreateVersion7(), Guid.CreateVersion7(), target.Id, target.Revision, target.ConfigurationSha256,
        target.AuthorizedStaffSha256, target.StaffGroupId, expected.Json, expected.Sha256,
        observed.Json, observed.Sha256, true, true, target.AuthorizedStaffSha256,
        target.TenantId, target.SiteId, target.DriveId, target.LibraryRootItemId, DateTimeOffset.UtcNow);

    var staffWriteReadback = MakeObservation(expectedRead, observedWrite);
    Assert.Equal(ArchiveCopyVerificationOutcomes.Mismatch, staffWriteReadback.ComparisonOutcome);

    // A self-consistent hash authenticates bytes only; it cannot authorize a staff WRITE expectation.
    Assert.Throws<ArgumentException>(() => MakeObservation(observedWrite, observedWrite));
  }

  [Trait("CompletionUnit", "WU-023")]
  [Fact]
  public void ArchiveCopyTargetRevision_EnforcesLegacyOrSharedProofXor_WithoutChangingLegacyHash()
  {
    var firmId = Guid.CreateVersion7();
    var clientId = Guid.CreateVersion7();
    var siteId = Guid.CreateVersion7();
    var legacy = CreateArchiveCopyTarget(firmId, clientId, siteId);
    Assert.True(legacy.HasValidDigests());
    var legacyHash = Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      targetCode = "SECONDARY_ARCHIVE", firmId, clientId, clientSharePointSiteId = siteId,
      staffGroupId = legacy.StaffGroupId, revision = legacy.Revision,
      connectionRevisionId = legacy.ConnectionRevisionId, tenant = legacy.TenantId,
      site = legacy.SiteId, drive = legacy.DriveId, root = legacy.LibraryRootItemId,
      expectedPermissionsSha256 = legacy.ExpectedPermissionsSha256,
      authorizedStaffSha256 = legacy.AuthorizedStaffSha256
    }));
    Assert.Equal(legacyHash, legacy.ConfigurationSha256);

    var shared = CreateSharedArchiveCopyTarget(firmId, clientId);
    Assert.True(shared.HasValidDigests());
    var sharedWithFolderSubstitution = CreateSharedArchiveCopyTarget(firmId, clientId);
    typeof(ArchiveCopyTargetRevision).GetProperty(nameof(ArchiveCopyTargetRevision.SelectedResourceRootFolderId))!
      .SetValue(sharedWithFolderSubstitution, "child-folder-is-not-library-root");
    Assert.False(sharedWithFolderSubstitution.HasValidDigests());
    typeof(ArchiveCopyTargetRevision).GetProperty(nameof(ArchiveCopyTargetRevision.ClientSharePointSiteId))!
      .SetValue(shared, siteId);
    Assert.False(shared.HasValidDigests());

    var legacyWithPartialProof = CreateArchiveCopyTarget(firmId, clientId, siteId);
    typeof(ArchiveCopyTargetRevision).GetProperty(nameof(ArchiveCopyTargetRevision.SelectedResourceDraftId))!
      .SetValue(legacyWithPartialProof, Guid.NewGuid());
    Assert.False(legacyWithPartialProof.HasValidDigests());
  }

  private static ArchiveCopyTargetRevision CreateArchiveCopyTarget(Guid firmId, Guid clientId, Guid clientSiteId)
  {
    var permissions = ArchiveCopyPermissionEncoding.Encode(
      ArchiveCopyPermissionEncoding.ClientStaffRead("42"));
    return ArchiveCopyTargetRevision.Create(Guid.CreateVersion7(), firmId, clientId, clientSiteId, "42", 1,
      Guid.CreateVersion7(), "tenant-a", "site-a", "archive-drive-a", "archive-root-a",
      permissions.Json, permissions.Sha256,
      Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("authorized-client-staff")),
      Guid.CreateVersion7(), DateTimeOffset.UtcNow);
  }

  private static ArchiveCopyTargetRevision CreateSharedArchiveCopyTarget(Guid firmId, Guid clientId)
  {
    var permissions = ArchiveCopyPermissionEncoding.Encode(
      ArchiveCopyPermissionEncoding.ClientStaffRead("43"));
    return ArchiveCopyTargetRevision.CreateSharedSelectedResource(Guid.CreateVersion7(), firmId, clientId,
      "43", 1, Guid.CreateVersion7(), "tenant-a", "example.sharepoint.com,site,web", "archive-drive",
      "archive-library-root", Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
      "https://example.sharepoint.com/sites/archive", "archive-library-root",
      Hashing.Sha256Hex("selected-resource-proof"), permissions.Json, permissions.Sha256,
      Hashing.Sha256Hex("authorized-client-staff"), Guid.CreateVersion7(), DateTimeOffset.UtcNow);
  }
}
