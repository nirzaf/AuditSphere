using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class RecordsArchiveEngagementPeriodAudienceTests
{
  [Fact]
  public async Task EngagementArchive_EmitsOnlyPeriodContextReferencedByItsEngagementEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, "Partner");
    var scope = fixture.Primary;
    var otherEngagementId = Guid.CreateVersion7();
    var firmActorId = Guid.CreateVersion7();
    var archiveId = Guid.CreateVersion7();
    var profileCode = "ENGAGEMENT-PERIOD-AUDIENCE-TEST";
    var a1PeriodId = Guid.CreateVersion7();
    var a2PeriodId = Guid.CreateVersion7();
    var a2PriorPeriodId = Guid.CreateVersion7();
    var periodOnlyContextId = Guid.CreateVersion7();
    var a1BookId = Guid.CreateVersion7();
    var a2BookId = Guid.CreateVersion7();
    var misboundBookId = Guid.CreateVersion7();
    var a1DatasetId = Guid.CreateVersion7();
    var a2DatasetId = Guid.CreateVersion7();
    var misboundDatasetId = Guid.CreateVersion7();
    var a1AmendmentId = Guid.CreateVersion7();
    var a2AmendmentId = Guid.CreateVersion7();
    var a2BridgeId = Guid.CreateVersion7();
    var a2BridgeReviewId = Guid.CreateVersion7();
    var now = DateTimeOffset.UtcNow;
    var firmActor = new ActorContext(firmActorId, scope.FirmId, 1, ["Partner"]);

    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.Users.Add(new AppUser
      {
        Id = firmActorId, FirmId = scope.FirmId, Subject = "archive-period-firm-" + firmActorId.ToString("N"),
        TenantId = "tenant-planning", Email = "archive-period-firm@example.test", DisplayName = "Firm Partner",
        UserKind = "Staff", SessionEpoch = 1, CreatedAt = now
      });
      seed.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, UserId = firmActorId,
        Role = "Partner", GrantedAt = now, GrantedByUserId = scope.Actor.UserId
      });
      seed.Engagements.Add(new Engagement
      {
        Id = otherEngagementId, FirmId = scope.FirmId, PracticeClientId = scope.ClientId,
        CreatedAt = now, ProfessionalWorkBlocked = false
      });
      seed.RecordsProfiles.Add(new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ProfileCode = profileCode,
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
        EngagementId = scope.EngagementId, ProfileId = profileCode, ProfileVersion = 1,
        Status = ArchiveStates.Issued, CreatedAt = now
      });
      seed.ClientReportingPeriods.AddRange(
        new ClientReportingPeriod
        {
          Id = a1PeriodId, FirmId = scope.FirmId, ClientId = scope.ClientId,
          PeriodCode = "A1-ENGAGEMENT-PERIOD", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31),
          Basis = "IFRS", Currency = "QAR", Status = "CLOSED", Revision = 1,
          ClosedAt = now, CloseReason = "A1-period-context", CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        },
        new ClientReportingPeriod
        {
          Id = a2PriorPeriodId, FirmId = scope.FirmId, ClientId = scope.ClientId,
          PeriodCode = "A2-PRIOR-PERIOD", StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31),
          Basis = "IFRS", Currency = "QAR", Status = "CLOSED", Revision = 2,
          ClosedAt = now, CloseReason = "A2-prior-period-private", CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        },
        new ClientReportingPeriod
        {
          Id = a2PeriodId, FirmId = scope.FirmId, ClientId = scope.ClientId,
          PeriodCode = "A2-OTHER-ENGAGEMENT-PERIOD-SENTINEL", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31),
          Basis = "IFRS", Currency = "QAR", Status = "CLOSED", PriorPeriodId = a2PriorPeriodId, Revision = 1,
          ClosedAt = now, CloseReason = "A2-period-private-sentinel", CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        },
        new ClientReportingPeriod
        {
          Id = periodOnlyContextId, FirmId = scope.FirmId, ClientId = scope.ClientId,
          PeriodCode = "A1-PERIOD-ONLY-CONTEXT", StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31),
          Basis = "IFRS", Currency = "QAR", Status = "CLOSED", Revision = 1,
          ClosedAt = now, CloseReason = "A1-period-only-context", CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        });
      seed.ClientReportingBooks.AddRange(
        new ClientReportingBook
        {
          Id = a1BookId, FirmId = scope.FirmId, ClientId = scope.ClientId, PeriodId = a1PeriodId,
          Code = "A1-BOOK", Basis = "IFRS", InclusionRule = "ALL", Currency = "QAR", Status = "ACTIVE",
          CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        },
        new ClientReportingBook
        {
          Id = a2BookId, FirmId = scope.FirmId, ClientId = scope.ClientId, PeriodId = a2PeriodId,
          Code = "A2-OTHER-ENGAGEMENT-BOOK-SENTINEL", Basis = "IFRS", InclusionRule = "ALL", Currency = "QAR", Status = "ACTIVE",
          CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        },
        new ClientReportingBook
        {
          Id = misboundBookId, FirmId = scope.FirmId, ClientId = scope.ClientId, PeriodId = periodOnlyContextId,
          Code = "A2-BOOK-PERIOD-MISMATCH-SENTINEL", Basis = "IFRS", InclusionRule = "ALL", Currency = "QAR", Status = "ACTIVE",
          CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        });
      seed.ClientPeriodAmendments.AddRange(
        new ClientPeriodAmendment
        {
          Id = a1AmendmentId, FirmId = scope.FirmId, ClientId = scope.ClientId, PeriodId = a1PeriodId,
          PreviousRevision = 1, AmendmentRevision = 2, Reason = "A1-amendment-context",
          CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        },
        new ClientPeriodAmendment
        {
          Id = a2AmendmentId, FirmId = scope.FirmId, ClientId = scope.ClientId, PeriodId = a2PeriodId,
          PreviousRevision = 1, AmendmentRevision = 2, Reason = "A2-other-engagement-amendment-sentinel",
          CreatedByUserId = scope.Actor.UserId, CreatedAt = now
        });
      seed.TrialBalanceDatasets.AddRange(
        new TrialBalanceDataset
        {
          Id = a1DatasetId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
          PeriodId = a1PeriodId, BookId = a1BookId, SourceKind = "Raw", Revision = 1,
          LegalEntityKey = "A1-ENTITY", Currency = "QAR", RawFileSha256Hex = Hashing.Sha256Hex("a1-source"),
          NormalizedDatasetDigest = Hashing.Sha256Hex("a1-normalized"), Sha256Hex = Hashing.Sha256Hex("a1-normalized"),
          Balanced = false, ValidationStatus = "Pending", ImportState = "LOADING", ImportedAt = now,
          ImportedByUserId = scope.Actor.UserId
        },
        new TrialBalanceDataset
        {
          Id = a2DatasetId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = otherEngagementId,
          PeriodId = a2PeriodId, BookId = a2BookId, SourceKind = "Raw", Revision = 1,
          LegalEntityKey = "A2-ENTITY", Currency = "QAR", RawFileSha256Hex = Hashing.Sha256Hex("a2-source"),
          NormalizedDatasetDigest = Hashing.Sha256Hex("a2-normalized"), Sha256Hex = Hashing.Sha256Hex("a2-normalized"),
          Balanced = false, ValidationStatus = "Pending", ImportState = "LOADING", ImportedAt = now,
          ImportedByUserId = scope.Actor.UserId
        },
        new TrialBalanceDataset
        {
          Id = misboundDatasetId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
          PeriodId = a1PeriodId, BookId = misboundBookId, SourceKind = "Raw", Revision = 1,
          LegalEntityKey = "A1-MISBOUND-BOOK-ENTITY", Currency = "QAR", RawFileSha256Hex = Hashing.Sha256Hex("a1-misbound-source"),
          NormalizedDatasetDigest = Hashing.Sha256Hex("a1-misbound-normalized"), Sha256Hex = Hashing.Sha256Hex("a1-misbound-normalized"),
          Balanced = false, ValidationStatus = "Pending", ImportState = "LOADING", ImportedAt = now,
          ImportedByUserId = scope.Actor.UserId
        });
      seed.AccountingReconciliations.Add(new AccountingReconciliation
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
        PeriodId = periodOnlyContextId, Area = "A1-period-only-context", AccountSelection = "TEST",
        AsOfDate = new DateOnly(2024, 12, 31), AgingBasis = "TEST", AgingBucketRuleVersion = "test.v1",
        SourceTotal = 0m, GlTotal = 0m, Residual = 0m, SourceHash = Hashing.Sha256Hex("a1-period-only-reconciliation"),
        Status = "Draft", CreatedByUserId = scope.Actor.UserId, CreatedAt = now
      });
      seed.OpeningBalanceBridges.Add(new OpeningBalanceBridge
      {
        Id = a2BridgeId, FirmId = scope.FirmId, ClientId = scope.ClientId, CurrentPeriodId = a2PeriodId,
        PriorPeriodId = a2PriorPeriodId, SourcePackageId = null, SourceHash = Hashing.Sha256Hex("a2-bridge"),
        PriorClosingAmount = 987654.321m, CurrentOpeningAmount = 987654.321m, Residual = 0m,
        Status = "DRAFT", EvidenceReference = "A2-other-engagement-bridge-sentinel", CreatedAt = now
      });
      seed.OpeningBalanceBridgeReviews.Add(new OpeningBalanceBridgeReview
      {
        Id = a2BridgeReviewId, FirmId = scope.FirmId, ClientId = scope.ClientId, BridgeId = a2BridgeId,
        PriorPeriodId = a2PriorPeriodId, PriorPeriodRevision = 2, EvidenceHash = Hashing.Sha256Hex("a2-bridge-review"),
        Reason = "A2-other-engagement-bridge-review-sentinel", ReviewerId = scope.Actor.UserId, CreatedAt = now
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
    using var payload = JsonDocument.Parse(narrowPayload);
    var root = payload.RootElement;
    var accounting = root.GetProperty("accounting");
    var periodIds = accounting.GetProperty("reportingPeriods").EnumerateArray()
      .Select(x => x.GetProperty("Id").GetGuid()).ToArray();
    var amendmentIds = accounting.GetProperty("periodAmendments").EnumerateArray()
      .Select(x => x.GetProperty("Id").GetGuid()).ToArray();
    var bookIds = accounting.GetProperty("reportingBooks").EnumerateArray()
      .Select(x => x.GetProperty("Id").GetGuid()).ToArray();
    var datasetIds = accounting.GetProperty("trialBalanceDatasets").EnumerateArray()
      .Select(x => x.GetProperty("Id").GetGuid()).ToArray();

    Assert.Equal(new[] { a1PeriodId, periodOnlyContextId }.OrderBy(x => x), periodIds.OrderBy(x => x));
    Assert.Equal(new[] { a1AmendmentId }, amendmentIds);
    Assert.Equal(new[] { a1BookId }, bookIds);
    Assert.Equal(new[] { a1DatasetId, misboundDatasetId }.OrderBy(x => x), datasetIds.OrderBy(x => x));
    Assert.Empty(accounting.GetProperty("openingBalanceBridges").EnumerateArray());
    Assert.Empty(accounting.GetProperty("openingBalanceBridgeReviews").EnumerateArray());
    foreach (var sentinel in new[]
    {
      "A2-OTHER-ENGAGEMENT-PERIOD-SENTINEL",
      "A2-period-private-sentinel",
      "A2-OTHER-ENGAGEMENT-BOOK-SENTINEL",
      "A2-BOOK-PERIOD-MISMATCH-SENTINEL",
      "A2-other-engagement-amendment-sentinel",
      "A2-other-engagement-bridge-sentinel",
      "A2-other-engagement-bridge-review-sentinel",
      "987654.321"
    })
      Assert.DoesNotContain(sentinel, narrowPayload, StringComparison.Ordinal);
    Assert.Contains("A1-ENGAGEMENT-PERIOD", narrowPayload, StringComparison.Ordinal);
    Assert.Contains("A1-amendment-context", narrowPayload, StringComparison.Ordinal);
  }
}
