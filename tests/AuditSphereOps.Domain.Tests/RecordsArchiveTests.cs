using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
  public async Task EngagementArchiveBuilder_ExcludesSiblingAndOtherEngagementEvidenceForNarrowAndFirmWideActors()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    var secondEngagementId = Guid.CreateVersion7();
    var firmActorId = Guid.CreateVersion7();
    var archiveId = Guid.CreateVersion7();
    var groupId = Guid.CreateVersion7();
    const string a1Sentinel = "A1-approved-source-sentinel";
    const string bSentinel = "B-sibling-source-sentinel";
    const string a2Sentinel = "A2-other-engagement-source-sentinel";
    const string groupSentinel = "group-membership-only-sentinel";
    var now = DateTimeOffset.UtcNow;
    var firmActor = new ActorContext(firmActorId, scope.FirmId, 1, ["Partner"]);

    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.Users.Add(new AppUser
      {
        Id = firmActorId, FirmId = scope.FirmId, Subject = "archive-firm-" + firmActorId.ToString("N"),
        TenantId = "tenant-planning", Email = "archive-firm@example.test", DisplayName = "Firm Partner",
        UserKind = "Staff", SessionEpoch = 1, CreatedAt = now
      });
      seed.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, UserId = firmActorId,
        Role = "Partner", GrantedAt = now, GrantedByUserId = scope.Actor.UserId
      });
      seed.Engagements.Add(new Engagement
      {
        Id = secondEngagementId, FirmId = scope.FirmId, PracticeClientId = scope.ClientId,
        CreatedAt = now, ProfessionalWorkBlocked = false
      });
      seed.RecordsProfiles.Add(new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ProfileCode = "ENGAGEMENT-AUDIENCE-TEST",
        Version = 1, RecordClass = "Synthetic engagement archive", Jurisdiction = "TEST",
        ServiceRoute = "STATUTORY_AUDIT", RetentionTrigger = "REPORT_DATE", ProtectionMode = "LOCAL_ONLY",
        LabelId = "test-only", LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
        DispositionOwner = "Records Custodian", BackupRequirements = "Synthetic fixture",
        CreatedByUserId = scope.Actor.UserId, Approved = true, ApprovedByUserId = scope.Actor.UserId,
        ApprovedAt = now, CreatedAt = now
      });
      seed.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = scope.FirmId, ClientId = scope.ClientId,
        EngagementId = scope.EngagementId, ProfileId = "ENGAGEMENT-AUDIENCE-TEST", ProfileVersion = 1,
        Status = ArchiveStates.Issued, CreatedAt = now
      });
      seed.ClientGroups.Add(new ClientGroup
      {
        Id = groupId, FirmId = scope.FirmId, Code = "AUDIENCE-TEST-GROUP",
        Name = "Synthetic archive audience group", CreatedByUserId = scope.Actor.UserId, CreatedAt = now
      });
      seed.ClientGroupMemberships.AddRange(
        new ClientGroupMembership
        {
          Id = Guid.CreateVersion7(), FirmId = scope.FirmId, GroupId = groupId, ClientId = scope.ClientId,
          EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow), ControlMethod = "CONTROLLED",
          OwnershipPercent = 100, EconomicInterestPercent = 100, EvidenceReference = "A1-group-reference",
          Status = AccountingWorkflowStates.Approved, CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        },
        new ClientGroupMembership
        {
          Id = Guid.CreateVersion7(), FirmId = scope.FirmId, GroupId = groupId, ClientId = fixture.Other.ClientId,
          EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow), ControlMethod = "CONTROLLED",
          OwnershipPercent = 100, EconomicInterestPercent = 100, EvidenceReference = groupSentinel,
          Status = AccountingWorkflowStates.Approved, CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        });
      seed.SourceReceipts.AddRange(
        new SourceReceipt
        {
          Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientId,
          EngagementId = scope.EngagementId, SourceType = "PBC_UPLOAD", ReceiptToken = "a1-source",
          Sha256Digest = Hashing.Sha256Hex(a1Sentinel), ByteCount = 1, OriginalFileName = a1Sentinel,
          AcquiredAt = now, AcquiredByUserId = scope.Actor.UserId
        },
        new SourceReceipt
        {
          Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = fixture.Other.ClientId,
          EngagementId = fixture.Other.EngagementId, SourceType = "PBC_UPLOAD", ReceiptToken = "b-source",
          Sha256Digest = Hashing.Sha256Hex(bSentinel), ByteCount = 1, OriginalFileName = bSentinel,
          AcquiredAt = now, AcquiredByUserId = scope.Actor.UserId
        },
        new SourceReceipt
        {
          Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientId,
          EngagementId = secondEngagementId, SourceType = "PBC_UPLOAD", ReceiptToken = "a2-source",
          Sha256Digest = Hashing.Sha256Hex(a2Sentinel), ByteCount = 1, OriginalFileName = a2Sentinel,
          AcquiredAt = now, AcquiredByUserId = scope.Actor.UserId
        });
      await seed.SaveChangesAsync();
    }

    async Task<string> BuildPayloadAsync(ActorContext actor)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var built = await RecordsArchiveService.BuildManifestAsync(db, actor, archiveId);
      Assert.True(built.Succeeded, built.Message);
      return await db.ArchiveStructuredExports.AsNoTracking()
        .Where(x => x.FirmId == scope.FirmId && x.ClientId == scope.ClientId &&
          x.EngagementId == scope.EngagementId && x.ArchiveId == archiveId &&
          x.ArchiveManifestId == built.Value!.ManifestId)
        .Select(x => x.PayloadJson).SingleAsync();
    }

    var narrowPayload = await BuildPayloadAsync(scope.Actor);
    var firmWidePayload = await BuildPayloadAsync(firmActor);
    Assert.Equal(narrowPayload, firmWidePayload);
    foreach (var sentinel in new[] { bSentinel, a2Sentinel, groupSentinel })
      Assert.DoesNotContain(sentinel, narrowPayload, StringComparison.Ordinal);
    Assert.Contains(a1Sentinel, narrowPayload, StringComparison.Ordinal);
    using var payload = JsonDocument.Parse(narrowPayload);
    Assert.Equal(1, payload.RootElement.GetProperty("sourceReceipts").GetArrayLength());
    Assert.Equal(scope.EngagementId, payload.RootElement.GetProperty("scope").GetProperty("engagementId").GetGuid());
  }

  [Fact]
  public async Task ArchiveAssembly_CreatesOnce_BuildsAndReviewsTheExactRequest()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      await seed.Engagements.Where(x => x.Id == scope.EngagementId).ExecuteUpdateAsync(update =>
        update.SetProperty(x => x.ServiceRoute, "STATUTORY_AUDIT"));
      seed.RecordsProfiles.Add(new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ProfileCode = "ASSEMBLY-RECORDS",
        Version = 1, RecordClass = "Issued audit file", Jurisdiction = "TEST",
        ServiceRoute = "STATUTORY_AUDIT", RetentionTrigger = "REPORT_DATE", RetentionDurationDays = null,
        ProtectionMode = "PURVIEW_RECORD", LabelId = "label-assembly", LegalHoldBehavior = "SUSPEND_DISPOSITION",
        AmendmentRoute = "CONTROLLED_AMENDMENT", DispositionOwner = "Records Custodian",
        BackupRequirements = "Protected backup", CreatedByUserId = scope.Actor.UserId, Approved = true,
        ApprovedAt = DateTimeOffset.UtcNow, ApprovedByUserId = scope.Actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      });
      await seed.SaveChangesAsync();
      await seed.Engagements.Where(x => x.Id == scope.EngagementId)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "Completed"));
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    var first = await RecordsArchiveService.EnsureArchiveAsync(db, scope.Actor,
      new EnsureArchiveRequest(scope.ClientId, scope.EngagementId, "ASSEMBLY-RECORDS", 1));
    Assert.True(first.Succeeded, first.Message);
    Assert.True(first.Value!.Created);
    var second = await RecordsArchiveService.EnsureArchiveAsync(db, scope.Actor,
      new EnsureArchiveRequest(scope.ClientId, scope.EngagementId, "ASSEMBLY-RECORDS", 1));
    Assert.True(second.Succeeded, second.Message);
    Assert.False(second.Value!.Created);
    Assert.Equal(first.Value.ArchiveId, second.Value.ArchiveId);
    Assert.Equal(1, await db.Archives.CountAsync(x => x.Id == first.Value.ArchiveId));
    db.ChangeTracker.Clear();

    var build = await RecordsArchiveService.BuildManifestWithRequestAsync(db, scope.Actor,
      first.Value.ArchiveId, null, "build-request-1");
    Assert.True(build.Succeeded, build.Message);
    var replay = await RecordsArchiveService.BuildManifestWithRequestAsync(db, scope.Actor,
      first.Value.ArchiveId, null, "build-request-1");
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(build.Value!.ManifestId, replay.Value!.ManifestId);
    Assert.Equal(1, await db.ArchiveManifests.CountAsync(x => x.ArchiveId == first.Value.ArchiveId));
    Assert.Equal("build-request-1", await db.ArchiveManifests.Where(x => x.Id == build.Value.ManifestId)
      .Select(x => x.BuildRequestKey).SingleAsync());

    var reviewed = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, first.Value.ArchiveId,
      build.Value.ManifestId, requestKey: "review-request-1");
    Assert.True(reviewed.Succeeded, reviewed.Message);
    var reviewReplay = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, first.Value.ArchiveId,
      build.Value.ManifestId, requestKey: "review-request-1");
    Assert.True(reviewReplay.Succeeded, reviewReplay.Message);
    Assert.Equal(build.Value.ManifestId, reviewReplay.Value!.ManifestId);
    Assert.Equal("review-request-1", await db.ArchiveManifests.Where(x => x.Id == build.Value.ManifestId)
      .Select(x => x.ReviewRequestKey).SingleAsync());
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
    Assert.Contains("records-export.v2", await db.ArchiveManifestEntries
      .Where(x => x.ArchiveManifestId == built.Value!.ManifestId && x.EntryKind == "STRUCTURED_EXPORT")
      .Select(x => x.MetadataJson).SingleAsync());
    var structuredExport = await db.ArchiveStructuredExports
      .Where(x => x.ArchiveManifestId == built.Value!.ManifestId).SingleAsync();
    Assert.Equal("records-export.v2", structuredExport.Schema);
    using (var payload = JsonDocument.Parse(structuredExport.PayloadJson))
    {
      var audience = payload.RootElement.GetProperty("audience");
      Assert.Equal("ENGAGEMENT_ONLY", audience.GetProperty("scope").GetString());
      Assert.Equal(scope.ClientId, audience.GetProperty("clientId").GetGuid());
      Assert.False(audience.GetProperty("groupWideDataIncluded").GetBoolean());
    }
    Assert.Contains("\"accounting\"", structuredExport.PayloadJson);
    Assert.Contains("\"reportingPeriods\"", structuredExport.PayloadJson);
    Assert.Contains("\"periodAmendments\"", structuredExport.PayloadJson);
    Assert.Contains("\"specialistSchedules\"", structuredExport.PayloadJson);
    Assert.Contains("\"accountingEvidenceAuditLinks\"", structuredExport.PayloadJson);
    Assert.Contains("\"accountingEvidenceActions\"", structuredExport.PayloadJson);
    Assert.Contains("\"valuationPreparations\"", structuredExport.PayloadJson);
    Assert.DoesNotContain("\"clientGroups\"", structuredExport.PayloadJson);
    Assert.DoesNotContain("\"groupMemberships\"", structuredExport.PayloadJson);
    Assert.DoesNotContain("\"scopeVersions\"", structuredExport.PayloadJson);
    Assert.DoesNotContain("\"consolidationRuns\"", structuredExport.PayloadJson);
    Assert.Contains("\"activityEvents\"", structuredExport.PayloadJson);
    Assert.Equal(structuredExport.ByteCount, System.Text.Encoding.UTF8.GetByteCount(structuredExport.PayloadJson));

    var reviewed = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId);
    Assert.True(reviewed.Succeeded, reviewed.Message);
    Assert.True((await db.ArchiveManifests.SingleAsync(x => x.ArchiveId == archiveId)).ReviewedAt.HasValue);

    var localExport = await RecordsArchiveService.ExportReviewedArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(localExport.Succeeded, localExport.Message);
    Assert.Equal("BLOCKED_EXTERNAL", localExport.Value!.ProviderProtectionState);
    Assert.Equal(Hashing.Sha256Hex(localExport.Value.Content), localExport.Value.ContentSha256);
    var repeatedLocalExport = await RecordsArchiveService.ExportReviewedArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(repeatedLocalExport.Succeeded, repeatedLocalExport.Message);
    Assert.Equal(localExport.Value.ContentSha256, repeatedLocalExport.Value!.ContentSha256);
    Assert.Equal(localExport.Value.Content, repeatedLocalExport.Value.Content);
    using (var exportJson = System.Text.Json.JsonDocument.Parse(localExport.Value.Content))
    {
      Assert.Equal("auditsphere-archive-evidence-export.v1",
        exportJson.RootElement.GetProperty("schema").GetString());
      Assert.Equal("BLOCKED_EXTERNAL", exportJson.RootElement.GetProperty("providerProtection")
        .GetProperty("state").GetString());
      Assert.Equal(1, exportJson.RootElement.GetProperty("manifest").GetProperty("entryCount").GetInt32());
      Assert.False(exportJson.RootElement.GetProperty("providerProtection")
        .GetProperty("externalDocumentBytesIncluded").GetBoolean());
      var secondaryCopy = exportJson.RootElement.GetProperty("secondarySharePointCopy");
      Assert.Equal("NOT_REQUESTED", secondaryCopy.GetProperty("state").GetString());
      Assert.Equal("AuditSphere release/archive store", secondaryCopy.GetProperty("authoritativeStore").GetString());
      Assert.Equal("SharePoint read-only access copy", secondaryCopy.GetProperty("secondaryProvider").GetString());
    }
    var archiveView = await AuditRecordQueries.ArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(archiveView.Succeeded, archiveView.Message);
    Assert.Equal("NOT_REQUESTED", archiveView.Value!.SecondarySharePointCopy!.State);
    var unprivilegedId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = unprivilegedId, FirmId = scope.FirmId, Subject = "archive-unprivileged-" + unprivilegedId.ToString("N"),
      TenantId = "tenant-archive-test", Email = "unprivileged@example.test", DisplayName = "Unprivileged reader",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, UserId = unprivilegedId, Role = "Staff",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = scope.Actor.UserId
    });
    await db.SaveChangesAsync();
    var unprivileged = new ActorContext(unprivilegedId, scope.FirmId, 1, ["Staff"]);
    var staffArchiveView = await AuditRecordQueries.ArchiveAsync(db, unprivileged, archiveId);
    Assert.True(staffArchiveView.Succeeded, staffArchiveView.Message);
    Assert.Null(staffArchiveView.Value!.SecondarySharePointCopy);
    Assert.False(staffArchiveView.Value.CanExport);
    var deniedExport = await RecordsArchiveService.ExportReviewedArchiveAsync(db, unprivileged, archiveId);
    Assert.False(deniedExport.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, deniedExport.ErrorCode);

    var custodianGrant = new RoleGrant
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, UserId = unprivilegedId, Role = "RecordsCustodian",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = scope.Actor.UserId
    };
    db.RoleGrants.Add(custodianGrant);
    await db.SaveChangesAsync();
    var custodian = new ActorContext(unprivilegedId, scope.FirmId, 1, ["Staff", "RecordsCustodian"]);
    var custodianArchiveView = await AuditRecordQueries.ArchiveAsync(db, custodian, archiveId);
    Assert.True(custodianArchiveView.Succeeded, custodianArchiveView.Message);
    Assert.True(custodianArchiveView.Value!.CanExport);
    var custodianExport = await RecordsArchiveService.ExportReviewedArchiveAsync(db, custodian, archiveId);
    Assert.True(custodianExport.Succeeded, custodianExport.Message);
    Assert.Equal(Hashing.Sha256Hex(custodianExport.Value!.Content), custodianExport.Value.ContentSha256);

    await db.RoleGrants.Where(x => x.Id == custodianGrant.Id).ExecuteUpdateAsync(update =>
      update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    var readerAfterCustodyRevocation = new ActorContext(unprivilegedId, scope.FirmId, 1, ["Staff"]);
    var afterRevocationView = await AuditRecordQueries.ArchiveAsync(db, readerAfterCustodyRevocation, archiveId);
    Assert.True(afterRevocationView.Succeeded, afterRevocationView.Message);
    Assert.False(afterRevocationView.Value!.CanExport);
    var afterRevocationExport = await RecordsArchiveService.ExportReviewedArchiveAsync(db, readerAfterCustodyRevocation, archiveId);
    Assert.False(afterRevocationExport.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, afterRevocationExport.ErrorCode);

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
    Assert.Equal("NOT_REQUESTED", verifiedExport.Value.SecondarySharePointCopyState);
    var unchangedStructuredExport = await db.ArchiveStructuredExports.AsNoTracking()
      .SingleAsync(x => x.ArchiveId == archiveId && x.ArchiveManifestId == structuredExport.ArchiveManifestId);
    Assert.Equal(structuredExport.ContentHash, unchangedStructuredExport.ContentHash);
    Assert.Equal(structuredExport.PayloadJson, unchangedStructuredExport.PayloadJson);
    using (var verifiedJson = System.Text.Json.JsonDocument.Parse(verifiedExport.Value.Content))
    {
      Assert.Equal("VERIFIED", verifiedJson.RootElement.GetProperty("providerProtection").GetProperty("state").GetString());
      Assert.Equal("NOT_REQUESTED", verifiedJson.RootElement.GetProperty("secondarySharePointCopy").GetProperty("state").GetString());
    }

    Assert.True((await RecordsArchiveService.RequestLegalHoldAsync(db, scope.Actor,
      new RequestLegalHoldRequest(archiveId, "HOLD-001", "Synthetic test hold"))).Succeeded);
    Assert.True((await RecordsArchiveService.ObserveLegalHoldAsync(db, scope.Actor,
      new ObserveLegalHoldRequest(archiveId, "HOLD-001", "purview-hold-1"))).Succeeded);
    var hold = await db.LegalHolds.SingleAsync(x => x.ArchiveId == archiveId);
    Assert.Equal("OBSERVED", hold.State);
    Assert.NotNull(hold.ObservedAt);

    await using var revokedDb = new AuditSphereDbContext(pg.Options);
    var revokedGrants = await revokedDb.RoleGrants.Where(x => x.FirmId == scope.FirmId &&
      x.UserId == scope.Actor.UserId && x.RevokedAt == null).ExecuteUpdateAsync(update =>
      update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    Assert.True(revokedGrants > 0);
    var revokedExport = await RecordsArchiveService.ExportReviewedArchiveAsync(revokedDb, scope.Actor, archiveId);
    Assert.False(revokedExport.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, revokedExport.ErrorCode);
  }

  [Fact]
  public async Task ArchiveExport_RejectsAmbiguousStructuredEntriesAndKeepsMetadataReadable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    var archiveId = Guid.CreateVersion7();

    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.RecordsProfiles.Add(new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ProfileCode = "AMBIGUOUS-ARCHIVE-TEST",
        Version = 1, RecordClass = "Synthetic archive", Jurisdiction = "TEST",
        ServiceRoute = "STATUTORY_AUDIT", RetentionTrigger = "REPORT_DATE", ProtectionMode = "LOCAL_ONLY",
        LabelId = "test-only", LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
        DispositionOwner = "Records Custodian", BackupRequirements = "Synthetic fixture",
        CreatedByUserId = scope.Actor.UserId, Approved = true, ApprovedAt = DateTimeOffset.UtcNow,
        ApprovedByUserId = scope.Actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      });
      seed.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = scope.FirmId, ClientId = scope.ClientId,
        EngagementId = scope.EngagementId, ProfileId = "AMBIGUOUS-ARCHIVE-TEST", ProfileVersion = 1,
        Status = ArchiveStates.Issued, CreatedAt = DateTimeOffset.UtcNow
      });
      await seed.SaveChangesAsync();
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    var built = await RecordsArchiveService.BuildManifestAsync(db, scope.Actor, archiveId);
    Assert.True(built.Succeeded, built.Message);
    var manifest = await db.ArchiveManifests.SingleAsync(x => x.Id == built.Value!.ManifestId);
    var original = await db.ArchiveManifestEntries.SingleAsync(x =>
      x.ArchiveManifestId == manifest.Id && x.EntryKind == "STRUCTURED_EXPORT");
    var duplicate = new ArchiveManifestEntry
    {
      Id = Guid.CreateVersion7(), FirmId = original.FirmId, ClientId = original.ClientId,
      EngagementId = original.EngagementId, ArchiveManifestId = original.ArchiveManifestId,
      Ordinal = original.Ordinal + 1, EntryKind = original.EntryKind, SourceKind = original.SourceKind,
      SourceId = original.SourceId, RelativeName = original.RelativeName, ContentHash = original.ContentHash,
      ByteCount = original.ByteCount, Required = original.Required, MetadataJson = original.MetadataJson
    };
    var digestInput = new[] { original, duplicate }.OrderBy(x => x.Ordinal).Select(x => new
    {
      x.Ordinal, x.EntryKind, x.SourceKind, x.SourceId, x.RelativeName, x.ContentHash,
      x.ByteCount, x.Required, x.MetadataJson
    });
    manifest.EntryCount = 2;
    manifest.ManifestDigest = Hashing.Sha256Hex(JsonSerializer.Serialize(digestInput));
    db.ArchiveManifestEntries.Add(duplicate);
    await db.SaveChangesAsync();

    var reviewed = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId, manifest.Id);
    Assert.True(reviewed.Succeeded, reviewed.Message);
    var export = await RecordsArchiveService.ExportReviewedArchiveAsync(db, scope.Actor, archiveId);
    Assert.False(export.Succeeded);
    Assert.Equal(ErrorCodes.ProtectedState, export.ErrorCode);
    Assert.Contains("reconcile", export.Message, StringComparison.OrdinalIgnoreCase);

    var metadata = await AuditRecordQueries.ArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(metadata.Succeeded, metadata.Message);
    Assert.Equal(archiveId, metadata.Value!.Id);

    // Model a pre-existing corrupt row in this disposable test database so the export's
    // independent digest check is exercised even though normal DML is blocked by the trigger.
    const string tamperedMetadata = "{\"controlledTamper\":true}";
    await using (var corruption = new AuditSphereDbContext(pg.Options))
    await using (var transaction = await corruption.Database.BeginTransactionAsync())
    {
      await corruption.Database.ExecuteSqlRawAsync(
        "ALTER TABLE archive_manifest_entries DISABLE TRIGGER trg_archive_manifest_entry_mutation");
      try
      {
        var changed = await corruption.Database.ExecuteSqlInterpolatedAsync($"""
          UPDATE archive_manifest_entries SET metadata_json = {tamperedMetadata}
          WHERE firm_id = {scope.FirmId} AND id = {duplicate.Id}
          """);
        Assert.Equal(1, changed);
      }
      finally
      {
        await corruption.Database.ExecuteSqlRawAsync(
          "ALTER TABLE archive_manifest_entries ENABLE TRIGGER trg_archive_manifest_entry_mutation");
      }
      await transaction.CommitAsync();
    }

    await using (var corruptExportDb = new AuditSphereDbContext(pg.Options))
    {
      var corruptExport = await RecordsArchiveService.ExportReviewedArchiveAsync(corruptExportDb, scope.Actor, archiveId);
      Assert.False(corruptExport.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, corruptExport.ErrorCode);
      Assert.Contains("manifest digest does not match", corruptExport.Message, StringComparison.OrdinalIgnoreCase);
      Assert.Equal(tamperedMetadata, await corruptExportDb.ArchiveManifestEntries.AsNoTracking()
        .Where(x => x.Id == duplicate.Id).Select(x => x.MetadataJson).SingleAsync());
      Assert.Equal(manifest.ManifestDigest, await corruptExportDb.ArchiveManifests.AsNoTracking()
        .Where(x => x.Id == manifest.Id).Select(x => x.ManifestDigest).SingleAsync());
    }
  }

  [Fact]
  public async Task ArchiveExport_WithholdsNestedSiblingSentinelForScopedCustodianAndPreservesMetadata()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    var siblingClientId = fixture.Other.ClientId;
    var archiveId = Guid.CreateVersion7();
    var manifestId = Guid.CreateVersion7();
    var exportEntryId = Guid.CreateVersion7();
    var exportId = Guid.CreateVersion7();
    const string sentinel = "B-only-consolidation-sentinel";
    var now = DateTimeOffset.UtcNow;
    var metadataJson = JsonSerializer.Serialize(new
    {
      schema = ArchiveStructuredExportSchemas.EngagementV2,
      storage = "archive_structured_exports",
      audience = ArchiveStructuredExportSchemas.EngagementOnlyAudience
    });
    var payloadJson = JsonSerializer.Serialize(new
    {
      schema = ArchiveStructuredExportSchemas.EngagementV2,
      audience = new
      {
        scope = ArchiveStructuredExportSchemas.EngagementOnlyAudience,
        firmId = scope.FirmId, clientId = scope.ClientId, engagementId = scope.EngagementId,
        groupWideDataIncluded = false
      },
      scope = new { firmId = scope.FirmId, clientId = scope.ClientId, engagementId = scope.EngagementId },
      snapshot = new
      {
        nested = new
        {
          groupMemberships = new[] { new { clientId = siblingClientId, sentinel } }
        }
      }
    });
    var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
    var payloadHash = Hashing.Sha256Hex(payloadBytes);
    var exportEntry = new ArchiveManifestEntry
    {
      Id = exportEntryId, FirmId = scope.FirmId, ClientId = scope.ClientId,
      EngagementId = scope.EngagementId, ArchiveManifestId = manifestId, Ordinal = 1,
      EntryKind = "STRUCTURED_EXPORT", SourceKind = "AuditSphereOps", RelativeName = ArchiveStructuredExportSchemas.EngagementV2RelativeName,
      ContentHash = payloadHash, ByteCount = payloadBytes.Length, Required = true, MetadataJson = metadataJson
    };
    var manifestDigest = Hashing.Sha256Hex(JsonSerializer.Serialize(new[] { new
    {
      exportEntry.Ordinal, exportEntry.EntryKind, exportEntry.SourceKind, exportEntry.SourceId,
      exportEntry.RelativeName, exportEntry.ContentHash, exportEntry.ByteCount, exportEntry.Required,
      exportEntry.MetadataJson
    } }));

    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      Assert.Empty(await seed.RoleGrants.Where(x => x.FirmId == scope.FirmId &&
        x.UserId == scope.Actor.UserId && x.ClientId == siblingClientId && x.RevokedAt == null).ToListAsync());
      seed.RecordsProfiles.Add(new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ProfileCode = "SIBLING-SENTINEL-ARCHIVE-TEST",
        Version = 1, RecordClass = "Synthetic archive", Jurisdiction = "TEST",
        ServiceRoute = "STATUTORY_AUDIT", RetentionTrigger = "REPORT_DATE", ProtectionMode = "LOCAL_ONLY",
        LabelId = "test-only", LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
        DispositionOwner = "Records Custodian", BackupRequirements = "Synthetic fixture",
        CreatedByUserId = scope.Actor.UserId, Approved = true, ApprovedAt = DateTimeOffset.UtcNow,
        ApprovedByUserId = scope.Actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      });
      seed.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = scope.FirmId, ClientId = scope.ClientId,
        EngagementId = scope.EngagementId, ProfileId = "SIBLING-SENTINEL-ARCHIVE-TEST", ProfileVersion = 1,
        Status = ArchiveStates.ManifestBuilt, CreatedAt = now
      });
      seed.ArchiveManifests.Add(new ArchiveManifest
      {
        Id = manifestId, FirmId = scope.FirmId, ClientId = scope.ClientId,
        EngagementId = scope.EngagementId, ArchiveId = archiveId, Version = 1,
        Status = "BUILT", ManifestDigest = manifestDigest, EntryCount = 1,
        CompletenessStatus = "COMPLETE", BuiltAt = now
      });
      seed.ArchiveManifestEntries.Add(exportEntry);
      seed.ArchiveStructuredExports.Add(new ArchiveStructuredExport
      {
        Id = exportId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
        ArchiveId = archiveId, ArchiveManifestId = manifestId, Version = 1,
        Schema = ArchiveStructuredExportSchemas.EngagementV2, PayloadJson = payloadJson,
        ContentHash = payloadHash, ByteCount = payloadBytes.Length, CreatedAt = now
      });
      await seed.SaveChangesAsync();
    }

    await using (var reviewDb = new AuditSphereDbContext(pg.Options))
    {
      var reviewed = await RecordsArchiveService.ReviewManifestAsync(reviewDb, scope.Actor, archiveId, manifestId);
      Assert.True(reviewed.Succeeded, reviewed.Message);
    }

    await using var exportDb = new AuditSphereDbContext(pg.Options);
    var withheld = await RecordsArchiveService.ExportReviewedArchiveAsync(exportDb, scope.Actor, archiveId);
    Assert.False(withheld.Succeeded);
    Assert.Equal(ErrorCodes.ProtectedState, withheld.ErrorCode);
    Assert.Null(withheld.Value);
    Assert.Contains("Withhold export", withheld.Message, StringComparison.OrdinalIgnoreCase);

    var metadata = await AuditRecordQueries.ArchiveAsync(exportDb, scope.Actor, archiveId);
    Assert.True(metadata.Succeeded, metadata.Message);
    Assert.Equal(archiveId, metadata.Value!.Id);
    var retained = await exportDb.ArchiveStructuredExports.AsNoTracking()
      .SingleAsync(x => x.ArchiveId == archiveId && x.ArchiveManifestId == manifestId);
    Assert.Contains(sentinel, retained.PayloadJson, StringComparison.Ordinal);
    Assert.Equal(Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(retained.PayloadJson)), retained.ContentHash);
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
  public async Task ReArchive_ProducesSeparateVersionWithPredecessorLinkAndDowngradeRefusesRetainedManifests()
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
    var retainedDigest = manifest2.ManifestDigest;
    Assert.True((await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId, manifest2.Id)).Succeeded);

    // T17: a reviewed entry set is immutable through ordinary storage paths.
    var reviewedEntry = await db.ArchiveManifestEntries
      .Where(x => x.ArchiveManifestId == manifest2.Id).OrderBy(x => x.Ordinal).FirstAsync();
    reviewedEntry.MetadataJson = "{\"controlledTamper\":true}";
    var deniedUpdate = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
    Assert.Equal("55000", Assert.IsType<PostgresException>(deniedUpdate.GetBaseException()).SqlState);
    db.ChangeTracker.Clear();

    await using (var insert = new AuditSphereDbContext(pg.Options))
    {
      insert.ArchiveManifestEntries.Add(new ArchiveManifestEntry
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientId,
        EngagementId = scope.EngagementId, ArchiveManifestId = manifest2.Id, Ordinal = 999,
        EntryKind = "DOCUMENT", SourceKind = "TEST", RelativeName = "evidence/late-entry.pdf",
        ContentHash = new string('d', 64), ByteCount = 1, Required = true, MetadataJson = "{}"
      });
      var deniedInsert = await Assert.ThrowsAnyAsync<Exception>(() => insert.SaveChangesAsync());
      Assert.Equal("55000", Assert.IsType<PostgresException>(deniedInsert.GetBaseException()).SqlState);
    }

    await using (var raw = new AuditSphereDbContext(pg.Options))
    {
      var deniedDelete = await Assert.ThrowsAsync<PostgresException>(() => raw.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM archive_manifest_entries WHERE id = {reviewedEntry.Id}"));
      Assert.Equal("55000", deniedDelete.SqlState);
      var deniedTruncate = await Assert.ThrowsAsync<PostgresException>(() => raw.Database.ExecuteSqlRawAsync(
        "TRUNCATE TABLE archive_manifest_entries"));
      Assert.Equal("55000", deniedTruncate.SqlState);
      var deniedRegression = await Assert.ThrowsAsync<PostgresException>(() => raw.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE archive_manifests SET status = 'BUILT' WHERE id = {manifest2.Id}"));
      Assert.Equal("55000", deniedRegression.SqlState);
    }

    var exported = await RecordsArchiveService.ExportReviewedArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(exported.Succeeded, exported.Message);
    var requested = await RecordsArchiveService.RequestRecordsActionAsync(db, scope.Actor,
      new(archiveId, "synthetic-v2-protection-request"));
    Assert.True(requested.Succeeded, requested.Message);
    Assert.True((await RecordsArchiveService.ObserveRecordsActionAsync(db, scope.Actor,
      new(archiveId, "label-test-audit-record", "RECORD_LOCKED", "synthetic-records-owner", "synthetic-v2-observation"))).Succeeded);
    var verified = await RecordsArchiveService.VerifyArchiveAsync(db, scope.Actor, archiveId);
    Assert.True(verified.Succeeded, verified.Message);
    Assert.Equal(ArchiveStates.ArchiveVerified, (await db.Archives.AsNoTracking().SingleAsync(x => x.Id == archiveId)).Status);
    Assert.Equal(manifest2.Id, (await db.RecordsActions.AsNoTracking().SingleAsync(x => x.ArchiveId == archiveId)).ArchiveManifestId);
    Assert.All(await db.RecordsActionEvidences.AsNoTracking().Where(x => x.ArchiveId == archiveId).ToArrayAsync(),
      x => Assert.Equal(manifest2.Id, x.ArchiveManifestId));
    Assert.Equal(retainedDigest, (await db.ArchiveManifests.AsNoTracking().SingleAsync(x => x.Id == manifest2.Id)).ManifestDigest);
    Assert.Equal(2, await db.ArchiveManifests.CountAsync(x => x.ArchiveId == archiveId));
    Assert.Equal(manifest1.Id, (await db.ArchiveManifests.AsNoTracking().SingleAsync(x => x.Id == manifest2.Id)).PredecessorManifestId);

    // Isolate the manifest guard from the separate v2-payload downgrade guard, then
    // verify a populated database refuses to roll the integrity migration back.
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE archive_structured_exports SET schema = 'records-export.v1'
      WHERE firm_id = {scope.FirmId} AND archive_id = {archiveId}
        AND archive_manifest_id = {manifest2.Id}
      """);

    await using var migrationDb = new AuditSphereDbContext(pg.Options);
    var migrator = migrationDb.GetService<IMigrator>();
    var migrations = migrationDb.Database.GetMigrations().ToArray();
    const string integrityMigration = "20261105163636_ArchiveAudienceAndReviewedManifestIntegrity";
    var integrityIndex = Array.IndexOf(migrations, integrityMigration);
    Assert.True(integrityIndex > 0, $"Migration {integrityMigration} must be present after its baseline.");
    var refusedDowngrade = await Assert.ThrowsAnyAsync<Exception>(
      () => migrator.MigrateAsync(migrations[integrityIndex - 1]));
    var downgradeRefusal = Assert.IsType<PostgresException>(refusedDowngrade.GetBaseException());
    Assert.Equal("55000", downgradeRefusal.SqlState);
    Assert.Contains("retained manifests exist", downgradeRefusal.Message, StringComparison.OrdinalIgnoreCase);

    await using (var guardCheck = new AuditSphereDbContext(pg.Options))
    {
      const string attemptedTamper = "{\"controlledTamper\":true}";
      var refusedMutation = await Assert.ThrowsAsync<PostgresException>(() =>
        guardCheck.Database.ExecuteSqlInterpolatedAsync($"""
          UPDATE archive_manifest_entries SET metadata_json = {attemptedTamper}
          WHERE firm_id = {scope.FirmId} AND archive_manifest_id = {manifest2.Id}
          """));
      Assert.Equal("55000", refusedMutation.SqlState);
    }

    await migrator.MigrateAsync();
    Assert.Equal(migrations, (await migrationDb.Database.GetAppliedMigrationsAsync()).ToArray());
    await migrationDb.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE archive_structured_exports SET schema = 'records-export.v2'
      WHERE firm_id = {scope.FirmId} AND archive_id = {archiveId}
        AND archive_manifest_id = {manifest2.Id}
      """);
  }

  [Fact]
  public async Task ReviewManifest_RejectsEntryInsertedWhileReviewWaitsForManifestLock()
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

    ArchiveManifestResult built;
    await using (var builder = new AuditSphereDbContext(pg.Options))
    {
      var result = await RecordsArchiveService.BuildManifestAsync(builder, scope.Actor, archiveId);
      Assert.True(result.Succeeded, result.Message);
      built = result.Value!;
    }

    await using var writer = new AuditSphereDbContext(pg.Options);
    await using var writerTransaction = await writer.Database.BeginTransactionAsync();
    writer.ArchiveManifestEntries.Add(new ArchiveManifestEntry
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientId,
      EngagementId = scope.EngagementId, ArchiveManifestId = built.ManifestId,
      Ordinal = built.EntryCount + 1, EntryKind = "DOCUMENT", SourceKind = "TEST",
      RelativeName = "evidence/late-entry.pdf", ContentHash = new string('d', 64),
      ByteCount = 1, Required = true, MetadataJson = "{}"
    });
    await writer.SaveChangesAsync();

    await using var reviewer = new AuditSphereDbContext(pg.Options);
    await reviewer.Database.OpenConnectionAsync();
    var reviewerConnection = (NpgsqlConnection)reviewer.Database.GetDbConnection();
    await using var pidCommand = reviewerConnection.CreateCommand();
    pidCommand.CommandText = "SELECT pg_backend_pid()";
    var reviewerPid = Convert.ToInt32(await pidCommand.ExecuteScalarAsync());

    var reviewTask = RecordsArchiveService.ReviewManifestAsync(reviewer, scope.Actor, archiveId, built.ManifestId);
    await using var observer = new AuditSphereDbContext(pg.Options);
    await observer.Database.OpenConnectionAsync();
    var observerConnection = (NpgsqlConnection)observer.Database.GetDbConnection();
    await using var waitCommand = observerConnection.CreateCommand();
    waitCommand.CommandText = "SELECT wait_event_type FROM pg_stat_activity WHERE pid = @pid";
    waitCommand.Parameters.AddWithValue("pid", reviewerPid);

    string? waitEvent = null;
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    try
    {
      do
      {
        waitEvent = await waitCommand.ExecuteScalarAsync() as string;
        if (waitEvent == "Lock") break;
        await Task.Delay(TimeSpan.FromMilliseconds(20));
      } while (DateTimeOffset.UtcNow < deadline);
    }
    finally
    {
      await writerTransaction.CommitAsync();
    }

    var review = await reviewTask.WaitAsync(TimeSpan.FromSeconds(10));
    Assert.Equal("Lock", waitEvent);
    Assert.False(review.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, review.ErrorCode);

    await using var verification = new AuditSphereDbContext(pg.Options);
    var retained = await verification.ArchiveManifests.AsNoTracking().SingleAsync(x => x.Id == built.ManifestId);
    Assert.Equal("BUILT", retained.Status);
    Assert.Equal(built.Digest, retained.ManifestDigest);
    Assert.Equal(built.EntryCount, retained.EntryCount);
    Assert.Equal(built.EntryCount + 1, await verification.ArchiveManifestEntries.CountAsync(x => x.ArchiveManifestId == built.ManifestId));
    Assert.Equal(ArchiveStates.ManifestBuilt,
      await verification.Archives.AsNoTracking().Where(x => x.Id == archiveId).Select(x => x.Status).SingleAsync());
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

  [Fact]
  public void StructuredArchiveGuard_RejectsLegacyAndGroupWidePayloads()
  {
    var archive = new Archive
    {
      Id = Guid.CreateVersion7(), FirmId = Guid.CreateVersion7(), ClientId = Guid.CreateVersion7(),
      EngagementId = Guid.CreateVersion7()
    };
    var metadata = JsonSerializer.Serialize(new
    {
      schema = "records-export.v2", storage = "archive_structured_exports", audience = "ENGAGEMENT_ONLY"
    });
    string Payload(Guid clientId, Guid engagementId, bool groupWide, bool siblingSentinel = false,
      bool embeddedGroupRun = false, bool embeddedRunManifest = false)
    {
      var payload = new Dictionary<string, object?>
      {
        ["schema"] = "records-export.v2",
        ["audience"] = new
        {
          scope = "ENGAGEMENT_ONLY", firmId = archive.FirmId, clientId, engagementId,
          groupWideDataIncluded = groupWide
        },
        ["scope"] = new { firmId = archive.FirmId, clientId, engagementId }
      };
      var nested = new Dictionary<string, object?>();
      if (siblingSentinel)
        nested["groupMemberships"] = new[] { new { clientId = Guid.CreateVersion7(), sentinel = "B-only" } };
      if (embeddedGroupRun)
        nested["groupRun"] = new
        {
          formatVersion = "GROUP_DELIVERABLE_V1",
          firmId = archive.FirmId,
          groupId = Guid.CreateVersion7(),
          groupRevision = 4L,
          scopeVersionId = Guid.CreateVersion7(),
          runId = Guid.CreateVersion7(),
          runHash = new string('a', 64),
          inputManifest = JsonSerializer.Serialize(new { clientId = Guid.CreateVersion7(), sentinel = "B-only-run-input" }),
          components = new[] { new { clientId = Guid.CreateVersion7(), sentinel = "B-only-group-component" } }
        };
      if (embeddedRunManifest)
        nested["archiveCompatibility"] = new
        {
          inputManifest = JsonSerializer.Serialize(new { clientId = Guid.CreateVersion7(), sentinel = "B-only-run-input" })
        };
      if (nested.Count > 0)
        payload["snapshot"] = new { nested };
      return JsonSerializer.Serialize(payload);
    }

    Assert.True(ArchiveStructuredExportGuard.IsEngagementSafe(archive, "records-export.v2",
      "structured/records-export.v2.json", metadata, Payload(archive.ClientId, archive.EngagementId, false), out _));
    Assert.False(ArchiveStructuredExportGuard.IsEngagementSafe(archive, "records-export.v1",
      "structured/records-export.v1.json",
      "{\"schema\":\"records-export.v1\",\"storage\":\"archive_structured_exports\"}",
      "{\"schema\":\"records-export.v1\",\"documents\":[]}", out var legacyFailure));
    Assert.Contains("audience", legacyFailure, StringComparison.OrdinalIgnoreCase);
    Assert.False(ArchiveStructuredExportGuard.IsEngagementSafe(archive, "records-export.v2",
      "structured/records-export.v2.json", metadata, Payload(archive.ClientId, archive.EngagementId, true), out _));
    Assert.False(ArchiveStructuredExportGuard.IsEngagementSafe(archive, "records-export.v2",
      "structured/records-export.v2.json", metadata, Payload(archive.ClientId, archive.EngagementId, false, siblingSentinel: true), out _));
    Assert.False(ArchiveStructuredExportGuard.IsEngagementSafe(archive, "records-export.v2",
      "structured/records-export.v2.json", metadata, Payload(Guid.CreateVersion7(), archive.EngagementId, false), out _));
    Assert.False(ArchiveStructuredExportGuard.IsEngagementSafe(archive, "records-export.v2",
      "structured/records-export.v2.json", metadata, Payload(archive.ClientId, Guid.CreateVersion7(), false), out _));
    Assert.False(ArchiveStructuredExportGuard.IsEngagementSafe(archive, "records-export.v2",
      "structured/records-export.v2.json", metadata,
      Payload(archive.ClientId, archive.EngagementId, false, embeddedGroupRun: true), out _));
    Assert.False(ArchiveStructuredExportGuard.IsEngagementSafe(archive, "records-export.v2",
      "structured/records-export.v2.json", metadata,
      Payload(archive.ClientId, archive.EngagementId, false, embeddedRunManifest: true), out _));
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
