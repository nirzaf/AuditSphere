using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
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
using System.Data;
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

    var reviewRequest = new ArchiveManifestReviewRequest(build.Value.ManifestId, build.Value.ManifestVersion,
      build.Value.Digest, "review-request-1");
    var reviewed = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, first.Value.ArchiveId, reviewRequest);
    Assert.True(reviewed.Succeeded, reviewed.Message);
    var reviewReplay = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, first.Value.ArchiveId, reviewRequest);
    Assert.True(reviewReplay.Succeeded, reviewReplay.Message);
    Assert.Equal(build.Value.ManifestId, reviewReplay.Value!.ManifestId);
    Assert.Equal("review-request-1", await db.ArchiveManifests.Where(x => x.Id == build.Value.ManifestId)
      .Select(x => x.ReviewRequestKey).SingleAsync());

    Assert.True((await RecordsArchiveService.RequestRecordsActionAsync(db, scope.Actor,
      new RequestRecordsActionRequest(first.Value.ArchiveId, "purview-request-replay-test"))).Succeeded);
    var historicalReviewReplay = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor,
      first.Value.ArchiveId, reviewRequest);
    Assert.True(historicalReviewReplay.Succeeded, historicalReviewReplay.Message);
    Assert.Equal(build.Value.ManifestId, historicalReviewReplay.Value!.ManifestId);
    Assert.Equal(build.Value.ManifestVersion, historicalReviewReplay.Value.ManifestVersion);
    Assert.Equal(build.Value.Digest, historicalReviewReplay.Value.Digest);
    Assert.Equal(ArchiveStates.RecordsActionRequested,
      await db.Archives.Where(x => x.Id == first.Value.ArchiveId).Select(x => x.Status).SingleAsync());
    var differentReviewAfterTransition = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor,
      first.Value.ArchiveId, reviewRequest with { RequestKey = "review-request-after-transition" });
    Assert.False(differentReviewAfterTransition.Succeeded);
    Assert.Equal(ErrorCodes.ProtectedState, differentReviewAfterTransition.ErrorCode);
  }

  [Fact]
  public async Task BuildManifest_WaitsBehindRevocationAndDoesNotPublishWithOldSession()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var ct = timeout.Token;
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    var archiveId = await SeedArchiveAuthorityScenarioAsync(pg, scope);
    Guid custodianGrantId;
    ActorContext administrator;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      custodianGrantId = await setup.RoleGrants.AsNoTracking().Where(x => x.FirmId == scope.FirmId &&
        x.UserId == scope.Actor.UserId && x.Role == "Partner" && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync(ct);
      administrator = await StaffingCertificationTestEvidence.CreateAdministratorAsync(setup, scope.FirmId);
    }

    await using var fenceHolder = new AuditSphereDbContext(pg.Options);
    await using var fenceTransaction = await fenceHolder.Database.BeginTransactionAsync(ct);
    await fenceHolder.Database.SqlQuery<Guid>($"""
      SELECT id AS "Value" FROM firm_safety_states WHERE id = {scope.FirmId} FOR UPDATE
      """).SingleAsync(ct);
    var fenceHolderPid = await fenceHolder.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
      .SingleAsync(ct);

    var revokerApplication = "archive-revoke-first-" + Guid.NewGuid().ToString("N");
    var builderApplication = "archive-build-after-revoke-" + Guid.NewGuid().ToString("N");
    await using var observer = new AuditSphereDbContext(pg.Options);
    await using var revoker = CreateApplicationNamedContext(pg, revokerApplication);
    await using var builder = CreateApplicationNamedContext(pg, builderApplication);
    Task<CommandResult>? revokeTask = null;
    Task<CommandResult<ArchiveManifestResult>>? buildTask = null;
    var revocationQueuedFirst = false;
    var buildQueuedBehindRevocation = false;
    try
    {
      revokeTask = RoleAdministrationService.RevokeRoleGrantAsync(revoker, administrator,
        new RevokeRoleGrantRequest(custodianGrantId), ct);
      revocationQueuedFirst = await WaitForApplicationBlockedByOrCompletedAsync(
        observer, revokerApplication, fenceHolderPid, revokeTask, ct);
      var revokerPid = await GetApplicationPidAsync(observer, revokerApplication, ct);

      buildTask = RecordsArchiveService.BuildManifestWithRequestAsync(builder, scope.Actor,
        archiveId, null, "revoked-session-build-request", ct);
      buildQueuedBehindRevocation = await WaitForApplicationBlockedByOrCompletedAsync(
        observer, builderApplication, revokerPid, buildTask, ct);
    }
    finally
    {
      await fenceTransaction.CommitAsync(CancellationToken.None);
    }

    var revoked = await revokeTask!.WaitAsync(ct);
    var built = await buildTask!.WaitAsync(ct);
    Assert.True(revocationQueuedFirst, "Role revocation did not acquire the firm fence queue first.");
    Assert.True(buildQueuedBehindRevocation,
      "The archive build must wait behind the normal firm role-revocation fence before it authorizes.");
    Assert.True(revoked.Succeeded, revoked.Message);
    Assert.False(built.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, built.ErrorCode);

    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await proof.ArchiveManifests.AsNoTracking().Where(x =>
      x.FirmId == scope.FirmId && x.ArchiveId == archiveId).ToListAsync(ct));
    Assert.Equal(ArchiveStates.Issued,
      await proof.Archives.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.Id == archiveId)
        .Select(x => x.Status).SingleAsync(ct));
    Assert.Equal(scope.Actor.SessionEpoch + 1,
      await proof.Users.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.Id == scope.Actor.UserId)
        .Select(x => x.SessionEpoch).SingleAsync(ct));
  }

  [Fact]
  public async Task EnsureArchive_WaitsBehindRevocationAndDoesNotCreateWithOldSession()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var ct = timeout.Token;
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    await SeedArchiveAuthorityScenarioAsync(pg, scope, createArchive: false);
    Guid custodianGrantId;
    ActorContext administrator;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      custodianGrantId = await setup.RoleGrants.AsNoTracking().Where(x => x.FirmId == scope.FirmId &&
        x.UserId == scope.Actor.UserId && x.Role == "Partner" && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync(ct);
      administrator = await StaffingCertificationTestEvidence.CreateAdministratorAsync(setup, scope.FirmId);
    }

    await using var fenceHolder = new AuditSphereDbContext(pg.Options);
    await using var fenceTransaction = await fenceHolder.Database.BeginTransactionAsync(ct);
    await fenceHolder.Database.SqlQuery<Guid>($"""
      SELECT id AS "Value" FROM firm_safety_states WHERE id = {scope.FirmId} FOR UPDATE
      """).SingleAsync(ct);
    var fenceHolderPid = await fenceHolder.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
      .SingleAsync(ct);

    var revokerApplication = "archive-ensure-revoke-first-" + Guid.NewGuid().ToString("N");
    var ensureApplication = "archive-ensure-after-revoke-" + Guid.NewGuid().ToString("N");
    await using var observer = new AuditSphereDbContext(pg.Options);
    await using var revoker = CreateApplicationNamedContext(pg, revokerApplication);
    await using var assurer = CreateApplicationNamedContext(pg, ensureApplication);
    Task<CommandResult>? revokeTask = null;
    Task<CommandResult<EnsureArchiveResult>>? ensureTask = null;
    var revocationQueuedFirst = false;
    var ensureQueuedBehindRevocation = false;
    try
    {
      revokeTask = RoleAdministrationService.RevokeRoleGrantAsync(revoker, administrator,
        new RevokeRoleGrantRequest(custodianGrantId), ct);
      revocationQueuedFirst = await WaitForApplicationBlockedByOrCompletedAsync(
        observer, revokerApplication, fenceHolderPid, revokeTask, ct);
      var revokerPid = await GetApplicationPidAsync(observer, revokerApplication, ct);

      ensureTask = RecordsArchiveService.EnsureArchiveAsync(assurer, scope.Actor,
        new EnsureArchiveRequest(scope.ClientId, scope.EngagementId, "AUTHORITY-FENCE-TEST", 1), ct);
      ensureQueuedBehindRevocation = await WaitForApplicationBlockedByOrCompletedAsync(
        observer, ensureApplication, revokerPid, ensureTask, ct);
    }
    finally
    {
      await fenceTransaction.CommitAsync(CancellationToken.None);
    }

    var revoked = await revokeTask!.WaitAsync(ct);
    var ensured = await ensureTask!.WaitAsync(ct);
    Assert.True(revocationQueuedFirst, "Role revocation did not acquire the firm fence queue first.");
    Assert.True(ensureQueuedBehindRevocation,
      "Archive creation must wait behind the normal firm role-revocation fence before it authorizes.");
    Assert.True(revoked.Succeeded, revoked.Message);
    Assert.False(ensured.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, ensured.ErrorCode);

    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await proof.Archives.AsNoTracking().Where(x =>
      x.FirmId == scope.FirmId && x.ClientId == scope.ClientId && x.EngagementId == scope.EngagementId).ToListAsync(ct));
    Assert.Equal(scope.Actor.SessionEpoch + 1,
      await proof.Users.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.Id == scope.Actor.UserId)
        .Select(x => x.SessionEpoch).SingleAsync(ct));
  }

  [Fact]
  public async Task BuildManifest_QueuedFirstCompletesBeforeRevocationForAnotherCustodian()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var ct = timeout.Token;
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    var archiveId = await SeedArchiveAuthorityScenarioAsync(pg, scope);
    Guid custodianGrantId;
    ActorContext administrator;
    ActorContext competingCustodian;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      custodianGrantId = await setup.RoleGrants.AsNoTracking().Where(x => x.FirmId == scope.FirmId &&
        x.UserId == scope.Actor.UserId && x.Role == "Partner" && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync(ct);
      administrator = await StaffingCertificationTestEvidence.CreateAdministratorAsync(setup, scope.FirmId);
      var other = PbcSeed.User(scope.FirmId, "Staff");
      setup.Users.Add(other);
      setup.RoleGrants.Add(PbcSeed.Grant(scope.FirmId, other, "RecordsCustodian", scope.ClientId, scope.EngagementId));
      await setup.SaveChangesAsync(ct);
      competingCustodian = PbcSeed.Actor(other, "RecordsCustodian");
    }

    await using var fenceHolder = new AuditSphereDbContext(pg.Options);
    await using var fenceTransaction = await fenceHolder.Database.BeginTransactionAsync(ct);
    await fenceHolder.Database.SqlQuery<Guid>($"""
      SELECT id AS "Value" FROM firm_safety_states WHERE id = {scope.FirmId} FOR UPDATE
      """).SingleAsync(ct);
    var fenceHolderPid = await fenceHolder.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
      .SingleAsync(ct);

    var builderApplication = "archive-build-command-first-" + Guid.NewGuid().ToString("N");
    var revokerApplication = "archive-revoke-command-first-" + Guid.NewGuid().ToString("N");
    await using var observer = new AuditSphereDbContext(pg.Options);
    await using var builder = CreateApplicationNamedContext(pg, builderApplication);
    await using var revoker = CreateApplicationNamedContext(pg, revokerApplication);
    Task<CommandResult<ArchiveManifestResult>>? buildTask = null;
    Task<CommandResult>? revokeTask = null;
    var buildQueuedFirst = false;
    var revocationQueuedBehindBuild = false;
    try
    {
      buildTask = RecordsArchiveService.BuildManifestAsync(builder, competingCustodian, archiveId, ct);
      buildQueuedFirst = await WaitForApplicationBlockedByOrCompletedAsync(
        observer, builderApplication, fenceHolderPid, buildTask, ct);
      var builderPid = await GetApplicationPidAsync(observer, builderApplication, ct);

      revokeTask = RoleAdministrationService.RevokeRoleGrantAsync(revoker, administrator,
        new RevokeRoleGrantRequest(custodianGrantId), ct);
      revocationQueuedBehindBuild = await WaitForApplicationBlockedByOrCompletedAsync(
        observer, revokerApplication, builderPid, revokeTask, ct);
    }
    finally
    {
      await fenceTransaction.CommitAsync(CancellationToken.None);
    }

    var built = await buildTask!.WaitAsync(ct);
    var revoked = await revokeTask!.WaitAsync(ct);
    Assert.True(buildQueuedFirst, "The authorized custodian did not acquire the firm fence queue first.");
    Assert.True(revocationQueuedBehindBuild,
      "Role revocation must serialize behind the archive command that acquired the firm fence first.");
    Assert.True(built.Succeeded, built.Message);
    Assert.True(revoked.Succeeded, revoked.Message);
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Equal(ArchiveStates.ManifestBuilt,
      await proof.Archives.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.Id == archiveId)
        .Select(x => x.Status).SingleAsync(ct));
    var manifest = await proof.ArchiveManifests.AsNoTracking().SingleAsync(x =>
      x.FirmId == scope.FirmId && x.ArchiveId == archiveId, ct);
    Assert.Equal(archiveId, built.Value!.ArchiveId);
    Assert.Equal(manifest.Id, built.Value.ManifestId);
    Assert.Equal("COMPLETE", built.Value.CompletenessStatus);
    Assert.Equal(1, manifest.Version);
    Assert.Equal(scope.Actor.SessionEpoch + 1,
      await proof.Users.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.Id == scope.Actor.UserId)
        .Select(x => x.SessionEpoch).SingleAsync(ct));
  }

  [Fact]
  public async Task ReviewManifest_WaitsBehindRevocationAndDeniesRetainedBuildReplay()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var ct = timeout.Token;
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    var archiveId = await SeedArchiveAuthorityScenarioAsync(pg, scope);
    const string buildRequestKey = "review-revoke-race-build";
    Guid manifestId;
    ArchiveManifestReviewRequest reviewRequest;
    Guid custodianGrantId;
    ActorContext administrator;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var built = await RecordsArchiveService.BuildManifestWithRequestAsync(setup, scope.Actor,
        archiveId, null, buildRequestKey, ct);
      Assert.True(built.Succeeded, built.Message);
      manifestId = built.Value!.ManifestId;
      reviewRequest = new ArchiveManifestReviewRequest(built.Value.ManifestId, built.Value.ManifestVersion,
        built.Value.Digest, "review-after-revocation");
      custodianGrantId = await setup.RoleGrants.AsNoTracking().Where(x => x.FirmId == scope.FirmId &&
        x.UserId == scope.Actor.UserId && x.Role == "Partner" && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync(ct);
      administrator = await StaffingCertificationTestEvidence.CreateAdministratorAsync(setup, scope.FirmId);
    }

    await using var fenceHolder = new AuditSphereDbContext(pg.Options);
    await using var fenceTransaction = await fenceHolder.Database.BeginTransactionAsync(ct);
    await fenceHolder.Database.SqlQuery<Guid>($"""
      SELECT id AS "Value" FROM firm_safety_states WHERE id = {scope.FirmId} FOR UPDATE
      """).SingleAsync(ct);
    var fenceHolderPid = await fenceHolder.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
      .SingleAsync(ct);

    var revokerApplication = "archive-review-revoke-first-" + Guid.NewGuid().ToString("N");
    var reviewerApplication = "archive-review-after-revoke-" + Guid.NewGuid().ToString("N");
    await using var observer = new AuditSphereDbContext(pg.Options);
    await using var revoker = CreateApplicationNamedContext(pg, revokerApplication);
    await using var reviewer = CreateApplicationNamedContext(pg, reviewerApplication);
    Task<CommandResult>? revokeTask = null;
    Task<CommandResult<ArchiveManifestResult>>? reviewTask = null;
    var revocationQueuedFirst = false;
    var reviewQueuedBehindRevocation = false;
    try
    {
      revokeTask = RoleAdministrationService.RevokeRoleGrantAsync(revoker, administrator,
        new RevokeRoleGrantRequest(custodianGrantId), ct);
      revocationQueuedFirst = await WaitForApplicationBlockedByOrCompletedAsync(
        observer, revokerApplication, fenceHolderPid, revokeTask, ct);
      var revokerPid = await GetApplicationPidAsync(observer, revokerApplication, ct);

      reviewTask = RecordsArchiveService.ReviewManifestAsync(reviewer, scope.Actor, archiveId, reviewRequest, ct);
      reviewQueuedBehindRevocation = await WaitForApplicationBlockedByOrCompletedAsync(
        observer, reviewerApplication, revokerPid, reviewTask, ct);
    }
    finally
    {
      await fenceTransaction.CommitAsync(CancellationToken.None);
    }

    var revoked = await revokeTask!.WaitAsync(ct);
    var reviewed = await reviewTask!.WaitAsync(ct);
    Assert.True(revocationQueuedFirst, "Role revocation did not acquire the firm fence queue first.");
    Assert.True(reviewQueuedBehindRevocation,
      "Manifest review must wait behind the normal firm role-revocation fence before it authorizes.");
    Assert.True(revoked.Succeeded, revoked.Message);
    Assert.False(reviewed.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, reviewed.ErrorCode);

    await using var replay = CreateApplicationNamedContext(pg, "archive-build-replay-after-revoke-" + Guid.NewGuid().ToString("N"));
    var replayed = await RecordsArchiveService.BuildManifestWithRequestAsync(replay, scope.Actor,
      archiveId, null, buildRequestKey, ct);
    Assert.False(replayed.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, replayed.ErrorCode);
    await using var proof = new AuditSphereDbContext(pg.Options);
    var retained = await proof.ArchiveManifests.AsNoTracking().SingleAsync(x =>
      x.FirmId == scope.FirmId && x.Id == manifestId, ct);
    Assert.Equal("BUILT", retained.Status);
    Assert.Null(retained.ReviewedAt);
    Assert.Null(retained.ReviewedByUserId);
    Assert.Null(retained.ReviewRequestKey);
    Assert.Equal(ArchiveStates.ManifestBuilt,
      await proof.Archives.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.Id == archiveId)
        .Select(x => x.Status).SingleAsync(ct));
    Assert.Equal(scope.Actor.SessionEpoch + 1,
      await proof.Users.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.Id == scope.Actor.UserId)
        .Select(x => x.SessionEpoch).SingleAsync(ct));
  }

  [Fact]
  public async Task ArchiveMutationsRequireFreshCallerSnapshotAndAcceptReadCommittedTransaction()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    var archiveId = await SeedArchiveAuthorityScenarioAsync(pg, scope);

    await using (var snapshotDb = new AuditSphereDbContext(pg.Options))
    await using (var snapshot = await snapshotDb.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
    {
      var ensured = await RecordsArchiveService.EnsureArchiveAsync(snapshotDb, scope.Actor,
        new EnsureArchiveRequest(scope.ClientId, scope.EngagementId, "AUTHORITY-FENCE-TEST", 1));
      var built = await RecordsArchiveService.BuildManifestWithRequestAsync(snapshotDb, scope.Actor,
        archiveId, null, "snapshot-transaction-build");
      var reviewed = await RecordsArchiveService.ReviewManifestAsync(snapshotDb, scope.Actor, archiveId, null);
      Assert.Equal(ErrorCodes.GenerationStale, ensured.ErrorCode);
      Assert.Equal(ErrorCodes.GenerationStale, built.ErrorCode);
      Assert.Equal(ErrorCodes.GenerationStale, reviewed.ErrorCode);
      await snapshot.RollbackAsync();
    }

    await using (var currentDb = new AuditSphereDbContext(pg.Options))
    await using (var current = await currentDb.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
      var ensured = await RecordsArchiveService.EnsureArchiveAsync(currentDb, scope.Actor,
        new EnsureArchiveRequest(scope.ClientId, scope.EngagementId, "AUTHORITY-FENCE-TEST", 1));
      Assert.True(ensured.Succeeded, ensured.Message);
      Assert.False(ensured.Value!.Created);
      Assert.Equal(archiveId, ensured.Value.ArchiveId);
      var built = await RecordsArchiveService.BuildManifestWithRequestAsync(currentDb, scope.Actor,
        archiveId, null, "caller-transaction-build");
      Assert.True(built.Succeeded, built.Message);
      var reviewed = await RecordsArchiveService.ReviewManifestAsync(currentDb, scope.Actor, archiveId,
        new ArchiveManifestReviewRequest(built.Value!.ManifestId, built.Value.ManifestVersion, built.Value.Digest,
          "caller-transaction-review"));
      Assert.True(reviewed.Succeeded, reviewed.Message);
      await current.CommitAsync();
    }

    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Equal(ArchiveStates.AssemblyReviewed,
      await proof.Archives.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.Id == archiveId)
        .Select(x => x.Status).SingleAsync());
    var manifest = await proof.ArchiveManifests.AsNoTracking().SingleAsync(x =>
      x.FirmId == scope.FirmId && x.ArchiveId == archiveId);
    Assert.Equal("REVIEWED", manifest.Status);
    Assert.Equal("caller-transaction-review", manifest.ReviewRequestKey);
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

    var reviewManifest = await db.ArchiveManifests.AsNoTracking().SingleAsync(x => x.FirmId == scope.FirmId && x.ArchiveId == archiveId);
    var reviewed = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId,
      new ArchiveManifestReviewRequest(reviewManifest.Id, reviewManifest.Version, reviewManifest.ManifestDigest,
        Guid.NewGuid().ToString("N")));
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

    var reviewed = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId,
      new ArchiveManifestReviewRequest(manifest.Id, manifest.Version, manifest.ManifestDigest, Guid.NewGuid().ToString("N")));
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
      var reviewed = await RecordsArchiveService.ReviewManifestAsync(reviewDb, scope.Actor, archiveId,
        new ArchiveManifestReviewRequest(manifestId, 1, manifestDigest, Guid.NewGuid().ToString("N")));
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
    var reviewSuperseded = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId,
      new ArchiveManifestReviewRequest(manifest1.Id, manifest1.Version, manifest1.ManifestDigest, "review-superseded-v1"));
    Assert.False(reviewSuperseded.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, reviewSuperseded.ErrorCode);
    var retainedDigest = manifest2.ManifestDigest;
    var wrongManifest = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId,
      new ArchiveManifestReviewRequest(Guid.NewGuid(), manifest2.Version, manifest2.ManifestDigest, "review-wrong-id"));
    Assert.False(wrongManifest.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, wrongManifest.ErrorCode);
    var staleVersion = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId,
      new ArchiveManifestReviewRequest(manifest2.Id, manifest2.Version + 1, manifest2.ManifestDigest, "review-stale-version"));
    Assert.False(staleVersion.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleVersion.ErrorCode);
    var staleDigest = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId,
      new ArchiveManifestReviewRequest(manifest2.Id, manifest2.Version, new string('0', 64), "review-stale-digest"));
    Assert.False(staleDigest.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleDigest.ErrorCode);
    Assert.Equal("BUILT", await db.ArchiveManifests.Where(x => x.Id == manifest2.Id).Select(x => x.Status).SingleAsync());
    Assert.Null(await db.ArchiveManifests.Where(x => x.Id == manifest2.Id).Select(x => x.ReviewedAt).SingleAsync());

    var currentReview = new ArchiveManifestReviewRequest(manifest2.Id, manifest2.Version, manifest2.ManifestDigest, "review-current-v2");
    Assert.True((await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId, currentReview)).Succeeded);
    var currentReplay = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId, currentReview);
    Assert.True(currentReplay.Succeeded, currentReplay.Message);
    Assert.Equal(manifest2.Id, currentReplay.Value!.ManifestId);
    Assert.Equal(manifest2.Version, currentReplay.Value.ManifestVersion);
    Assert.Equal(manifest2.ManifestDigest, currentReplay.Value.Digest);
    var newAssent = await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId,
      currentReview with { RequestKey = "review-current-v2-new-key" });
    Assert.False(newAssent.Succeeded);
    Assert.Equal(ErrorCodes.ProtectedState, newAssent.ErrorCode);

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

    var structuredExportId = await db.ArchiveStructuredExports.AsNoTracking()
      .Where(x => x.FirmId == scope.FirmId && x.ArchiveId == archiveId && x.ArchiveManifestId == manifest2.Id)
      .Select(x => x.Id).SingleAsync();
    var retainedStructuredExport = await db.ArchiveStructuredExports.AsNoTracking()
      .Where(x => x.Id == structuredExportId)
      .Select(x => new
      {
        x.Id, x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId, x.ArchiveManifestId,
        x.Version, x.Schema, x.PayloadJson, x.ContentHash, x.ByteCount, x.CreatedAt
      }).SingleAsync();
    await using (var structuredRaw = new AuditSphereDbContext(pg.Options))
    {
      var otherId = Guid.CreateVersion7();
      var tamperedPayload = "{\"controlledTamper\":true}";
      var refusedStructuredMutations = new Func<Task>[]
      {
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET payload_json = {tamperedPayload} WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET schema = 'records-export.v1' WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET content_hash = repeat('a', 64) WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET byte_count = byte_count + 1 WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET firm_id = {otherId} WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET client_id = {otherId} WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET engagement_id = {otherId} WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET archive_id = {otherId} WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET archive_manifest_id = {otherId} WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET id = {otherId} WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET version = version + 1 WHERE id = {structuredExportId}"),
        () => structuredRaw.Database.ExecuteSqlInterpolatedAsync($"UPDATE archive_structured_exports SET created_at = created_at + interval '1 minute' WHERE id = {structuredExportId}")
      };

      foreach (var mutation in refusedStructuredMutations)
      {
        var refused = await Assert.ThrowsAsync<PostgresException>(mutation);
        Assert.Equal("55000", refused.SqlState);
        var afterRefusal = await structuredRaw.ArchiveStructuredExports.AsNoTracking()
          .Where(x => x.Id == structuredExportId)
          .Select(x => new
          {
            x.Id, x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId, x.ArchiveManifestId,
            x.Version, x.Schema, x.PayloadJson, x.ContentHash, x.ByteCount, x.CreatedAt
          }).SingleAsync();
        Assert.Equal(retainedStructuredExport, afterRefusal);
      }

      var refusedStructuredDelete = await Assert.ThrowsAsync<PostgresException>(() =>
        structuredRaw.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM archive_structured_exports WHERE id = {structuredExportId}"));
      Assert.Equal("55000", refusedStructuredDelete.SqlState);
      var refusedStructuredTruncate = await Assert.ThrowsAsync<PostgresException>(() =>
        structuredRaw.Database.ExecuteSqlRawAsync("TRUNCATE TABLE archive_structured_exports"));
      Assert.Equal("55000", refusedStructuredTruncate.SqlState);
      var afterDeleteAndTruncate = await structuredRaw.ArchiveStructuredExports.AsNoTracking()
        .Where(x => x.Id == structuredExportId)
        .Select(x => new
        {
          x.Id, x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId, x.ArchiveManifestId,
          x.Version, x.Schema, x.PayloadJson, x.ContentHash, x.ByteCount, x.CreatedAt
        }).SingleAsync();
      Assert.Equal(retainedStructuredExport, afterDeleteAndTruncate);
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

    await using var migrationDb = new AuditSphereDbContext(pg.Options);
    var migrator = migrationDb.GetService<IMigrator>();
    var migrations = migrationDb.Database.GetMigrations().ToArray();
    const string structuredExportGuardMigration = "20261107095608_ArchiveStructuredExportAppendOnly";
    var structuredExportBeforeUpgrade = await migrationDb.ArchiveStructuredExports.AsNoTracking()
      .Where(x => x.Id == structuredExportId)
      .Select(x => new
      {
        x.Id, x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId, x.ArchiveManifestId,
        x.Version, x.Schema, x.PayloadJson, x.ContentHash, x.ByteCount, x.CreatedAt
      }).SingleAsync();
    await migrationDb.Database.ExecuteSqlRawAsync("""
      DROP TRIGGER IF EXISTS trg_archive_structured_export_truncate ON archive_structured_exports;
      DROP TRIGGER IF EXISTS trg_archive_structured_export_mutation ON archive_structured_exports;
      DROP TRIGGER IF EXISTS trg_archive_structured_export_insert ON archive_structured_exports;
      DROP FUNCTION IF EXISTS reject_archive_structured_export_truncate();
      DROP FUNCTION IF EXISTS guard_archive_structured_export_mutation();
      DROP FUNCTION IF EXISTS guard_archive_structured_export_insert();
      """);
    await migrationDb.Database.ExecuteSqlInterpolatedAsync($"""
      DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = {structuredExportGuardMigration}
      """);
    await migrator.MigrateAsync();
    var structuredExportAfterUpgrade = await migrationDb.ArchiveStructuredExports.AsNoTracking()
      .Where(x => x.Id == structuredExportId)
      .Select(x => new
      {
        x.Id, x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId, x.ArchiveManifestId,
        x.Version, x.Schema, x.PayloadJson, x.ContentHash, x.ByteCount, x.CreatedAt
      }).SingleAsync();
    Assert.Equal(structuredExportBeforeUpgrade, structuredExportAfterUpgrade);

    var structuredExportGuardIndex = Array.IndexOf(migrations, structuredExportGuardMigration);
    Assert.True(structuredExportGuardIndex > 0, $"Migration {structuredExportGuardMigration} must be present after its baseline.");
    var refusedStructuredExportDowngrade = await Assert.ThrowsAnyAsync<Exception>(
      () => migrator.MigrateAsync(migrations[structuredExportGuardIndex - 1]));
    var structuredExportDowngradeRefusal = Assert.IsType<PostgresException>(refusedStructuredExportDowngrade.GetBaseException());
    Assert.Equal("55000", structuredExportDowngradeRefusal.SqlState);
    Assert.Contains("retained structured exports exist", structuredExportDowngradeRefusal.Message, StringComparison.OrdinalIgnoreCase);

    // This disposable downgrade fixture deliberately removes its protected rows only
    // after proving ordinary DML and populated rollback are refused. It isolates the
    // older manifest-migration rollback refusal without rewriting retained history.
    await migrationDb.Database.ExecuteSqlRawAsync("""
      DROP TRIGGER IF EXISTS trg_archive_structured_export_truncate ON archive_structured_exports;
      DROP TRIGGER IF EXISTS trg_archive_structured_export_mutation ON archive_structured_exports;
      DROP TRIGGER IF EXISTS trg_archive_structured_export_insert ON archive_structured_exports;
      """);
    await migrationDb.Database.ExecuteSqlInterpolatedAsync($"""
      DELETE FROM archive_structured_exports WHERE firm_id = {scope.FirmId} AND archive_id = {archiveId}
      """);

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
  }

  [Fact]
  public async Task ReviewManifest_RejectsStructuredExportInsertWaitingOnReviewLock()
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

    await using var reviewer = new AuditSphereDbContext(pg.Options);
    await using var reviewTransaction = await reviewer.Database.BeginTransactionAsync();
    var reviewed = await RecordsArchiveService.ReviewManifestAsync(reviewer, scope.Actor, archiveId,
      new ArchiveManifestReviewRequest(built.ManifestId, built.ManifestVersion, built.Digest, Guid.NewGuid().ToString("N")));
    Assert.True(reviewed.Succeeded, reviewed.Message);
    var original = await reviewer.ArchiveStructuredExports.AsNoTracking()
      .SingleAsync(x => x.FirmId == scope.FirmId && x.ArchiveId == archiveId && x.ArchiveManifestId == built.ManifestId);

    await using var insert = new AuditSphereDbContext(pg.Options);
    await insert.Database.OpenConnectionAsync();
    var insertConnection = (NpgsqlConnection)insert.Database.GetDbConnection();
    await using var pidCommand = insertConnection.CreateCommand();
    pidCommand.CommandText = "SELECT pg_backend_pid()";
    var insertPid = Convert.ToInt32(await pidCommand.ExecuteScalarAsync());
    var lateId = Guid.CreateVersion7();
    var insertTask = insert.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO archive_structured_exports
        (id, firm_id, client_id, engagement_id, archive_id, archive_manifest_id, version,
         schema, payload_json, content_hash, byte_count, created_at)
      VALUES
        ({lateId}, {original.FirmId}, {original.ClientId}, {original.EngagementId}, {original.ArchiveId},
         {original.ArchiveManifestId}, {original.Version}, {original.Schema}, {original.PayloadJson},
         {original.ContentHash}, {original.ByteCount}, {original.CreatedAt})
      """);

    await using var observer = new AuditSphereDbContext(pg.Options);
    await observer.Database.OpenConnectionAsync();
    var observerConnection = (NpgsqlConnection)observer.Database.GetDbConnection();
    await using var waitCommand = observerConnection.CreateCommand();
    waitCommand.CommandText = "SELECT wait_event_type FROM pg_stat_activity WHERE pid = @pid";
    waitCommand.Parameters.AddWithValue("pid", insertPid);

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
      await reviewTransaction.CommitAsync();
    }

    Assert.Equal("Lock", waitEvent);
    var refusedLateInsert = await Assert.ThrowsAsync<PostgresException>(() => insertTask);
    Assert.Equal("55000", refusedLateInsert.SqlState);

    await using var verification = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await verification.ArchiveStructuredExports.CountAsync(x =>
      x.FirmId == scope.FirmId && x.ArchiveId == archiveId && x.ArchiveManifestId == built.ManifestId));
    var retained = await verification.ArchiveStructuredExports.AsNoTracking().SingleAsync(x => x.Id == original.Id);
    Assert.Equal(original.PayloadJson, retained.PayloadJson);
    Assert.Equal(original.Schema, retained.Schema);
    Assert.Equal(original.ContentHash, retained.ContentHash);
    Assert.Equal(original.ByteCount, retained.ByteCount);
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

    var reviewTask = RecordsArchiveService.ReviewManifestAsync(reviewer, scope.Actor, archiveId,
      new ArchiveManifestReviewRequest(built.ManifestId, built.ManifestVersion, built.Digest, Guid.NewGuid().ToString("N")));
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
    var built = await RecordsArchiveService.BuildManifestAsync(db, scope.Actor, archiveId);
    Assert.True(built.Succeeded, built.Message);
    Assert.True((await RecordsArchiveService.ReviewManifestAsync(db, scope.Actor, archiveId,
      new ArchiveManifestReviewRequest(built.Value!.ManifestId, built.Value.ManifestVersion, built.Value.Digest,
        Guid.NewGuid().ToString("N")))).Succeeded);
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

  private static async Task<Guid> SeedArchiveAuthorityScenarioAsync(
    PgTestSchema pg, PlanningScope scope, bool createArchive = true)
  {
    var archiveId = Guid.CreateVersion7();
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Engagements.Where(x => x.FirmId == scope.FirmId && x.Id == scope.EngagementId)
      .ExecuteUpdateAsync(update => update
        .SetProperty(x => x.ServiceRoute, "STATUTORY_AUDIT")
        .SetProperty(x => x.Status, "Completed"));
    db.RecordsProfiles.Add(new RecordsProfile
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ProfileCode = "AUTHORITY-FENCE-TEST",
      Version = 1, RecordClass = "Synthetic authority-fence archive", Jurisdiction = "TEST",
      ServiceRoute = "STATUTORY_AUDIT", RetentionTrigger = "REPORT_DATE", ProtectionMode = "LOCAL_ONLY",
      LabelId = "test-only", LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
      DispositionOwner = "Records Custodian", BackupRequirements = "Synthetic fixture",
      CreatedByUserId = scope.Actor.UserId, Approved = true, ApprovedAt = now,
      ApprovedByUserId = scope.Actor.UserId, CreatedAt = now
    });
    if (createArchive) db.Archives.Add(new Archive
    {
      Id = archiveId, FirmId = scope.FirmId, ClientId = scope.ClientId,
      EngagementId = scope.EngagementId, ProfileId = "AUTHORITY-FENCE-TEST", ProfileVersion = 1,
      Status = ArchiveStates.Issued, CreatedAt = now
    });
    await db.SaveChangesAsync();
    return archiveId;
  }

  private static AuditSphereDbContext CreateApplicationNamedContext(PgTestSchema pg, string applicationName)
  {
    var connection = new NpgsqlConnectionStringBuilder(pg.ConnectionString)
    {
      ApplicationName = applicationName
    }.ConnectionString;
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(connection).Options;
    return new AuditSphereDbContext(options);
  }

  private static async Task<int> GetApplicationPidAsync(
    AuditSphereDbContext observer, string applicationName, CancellationToken ct)
  {
    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
    bounded.CancelAfter(TimeSpan.FromSeconds(10));
    try
    {
      while (true)
      {
        var pid = await observer.Database.SqlQuery<int?>($"""
          SELECT (
            SELECT pid FROM pg_stat_activity
            WHERE datname = current_database() AND application_name = {applicationName}
            ORDER BY backend_start DESC LIMIT 1
          ) AS "Value"
          """).SingleAsync(bounded.Token);
        if (pid.HasValue) return pid.Value;
        await Task.Delay(TimeSpan.FromMilliseconds(20), bounded.Token);
      }
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      throw new TimeoutException($"The PostgreSQL backend for {applicationName} did not start.");
    }
  }

  private static async Task<bool> WaitForApplicationBlockedByOrCompletedAsync(
    AuditSphereDbContext observer, string waitingApplicationName, int blockingPid, Task waitingTask,
    CancellationToken ct)
  {
    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
    bounded.CancelAfter(TimeSpan.FromSeconds(10));
    try
    {
      while (!waitingTask.IsCompleted)
      {
        var blocked = await observer.Database.SqlQuery<bool>($"""
          SELECT EXISTS (
            SELECT 1 FROM pg_stat_activity AS waiting
            WHERE waiting.datname = current_database() AND waiting.application_name = {waitingApplicationName}
              AND waiting.wait_event_type = 'Lock'
              AND {blockingPid} = ANY(pg_blocking_pids(waiting.pid))
          ) AS "Value"
          """).SingleAsync(bounded.Token);
        if (blocked) return true;
        await Task.Delay(TimeSpan.FromMilliseconds(20), bounded.Token);
      }
      return false;
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      return false;
    }
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
