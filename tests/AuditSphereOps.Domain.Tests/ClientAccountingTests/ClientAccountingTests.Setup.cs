using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  private sealed record Scope(Guid FirmId, Guid ClientA, Guid ClientB, Guid EngagementA, Guid EngagementB,
    AppUser Preparer, AppUser Reviewer, AppUser Partner);

  private sealed class TestDbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }

  private static async Task<Scope> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientA = Guid.NewGuid();
    var clientB = Guid.NewGuid();
    var engagementA = Guid.NewGuid();
    var engagementB = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    var partner = User(firmId, "partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.AddRange(
      new PracticeClient { Id = clientA, FirmId = firmId, LegalName = "CLIENT A", CreatedAt = DateTimeOffset.UtcNow },
      new PracticeClient { Id = clientB, FirmId = firmId, LegalName = "CLIENT B", CreatedAt = DateTimeOffset.UtcNow });
    db.Engagements.AddRange(
      new Engagement { Id = engagementA, FirmId = firmId, PracticeClientId = clientA, ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow },
      new Engagement { Id = engagementB, FirmId = firmId, PracticeClientId = clientB, ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.AddRange(new ClientSafetyState { Id = clientA, FirmId = firmId }, new ClientSafetyState { Id = clientB, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer, partner);
    var partnerGrant = new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = partner.Id, Role = "Partner", ClientId = clientA,
      EngagementId = engagementA, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = reviewer.Id,
      Reason = "Engagement staffing: Engagement Partner"
    };
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"), Grant(firmId, reviewer, "AccountingReviewer"),
      Grant(firmId, reviewer, "Partner"), partnerGrant);
    db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientA, EngagementId = engagementA, UserId = partner.Id,
      StaffingLevel = StaffingLevels.EngagementPartner, RoleGrantId = partnerGrant.Id,
      AssignedByUserId = reviewer.Id, AssignedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    return new Scope(firmId, clientA, clientB, engagementA, engagementB, preparer, reviewer, partner);
  }

  private static async Task<(Guid PeriodId, Guid BookId)> CreateGlFixtureAsync(
    PgTestSchema pg, Scope scope, ActorContext preparer)
  {
    Guid periodId, chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "LEDGER-A", "A-1"))).Succeeded);
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      var book = await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"));
      Assert.True(book.Succeeded, book.Message);
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "LEDGER-A", new DateOnly(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
        new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
        new("revenue", "4000", "Revenue", "INCOME", "CREDIT", true)
      ])).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, Actor(scope.Reviewer, "AccountingReviewer"), chartId)).Succeeded);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var bookId = await verify.ClientReportingBooks.Where(x => x.ClientId == scope.ClientA && x.PeriodId == periodId)
      .Select(x => x.Id).SingleAsync();
    return (periodId, bookId);
  }

  private static async Task<Guid> AddPackageAsync(AuditSphereDbContext db, Scope scope, Guid clientId, Guid engagementId, decimal amount,
    string destination, string? suffix = null, string currency = "QAR")
  {
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var digest = Hashing.Sha256Hex($"package-{clientId:D}-{suffix ?? destination}");
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, SourceKind = "Raw",
      Revision = 1, LegalEntityKey = clientId.ToString("D"), Currency = currency, RawFileSha256Hex = digest,
      NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true, ValidationStatus = "Accepted",
      ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m, ImportedAt = now, ImportedByUserId = scope.Preparer.Id
    });
    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      Status = "Finalized", ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, DatasetId = datasetId,
      Version = 1, Generation = 1, TaxonomyVersion = "tax-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      Status = AccountingPackageStates.MappingApproved, CreatedByUserId = scope.Preparer.Id, ApprovedByUserId = scope.Reviewer.Id,
      ApprovedAt = now, CreatedAt = now
    });
    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      AdjustmentPlanId = planId, Currency = currency, ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, AdjustedDatasetId = adjustedId,
      MappingVersionId = mappingId, AdjustmentPlanId = planId, Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      TaxonomyVersion = "tax-v1", TemplateVersion = "template-v1", CalculationEngineVersion = "test-engine",
      CalculationHash = digest, Currency = currency, Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    db.FinancialPackageLines.Add(new FinancialPackageLine
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, FinancialPackageId = packageId,
      SourceAccountCode = destination == "CASH" ? "1000" : "4000", DestinationCode = destination, StatementSection = "STATEMENT",
      Amount = amount, Fraction = 1m, Currency = currency, AdjustedSnapshotId = adjustedId, CreatedAt = now
    });
    var artifactBytes = System.Text.Encoding.UTF8.GetBytes($"package-artifact|{packageId:D}|{digest}");
    db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId,
      FinancialPackageId = packageId, PackageRevision = 1, PackageGeneration = 1, PackageHash = digest,
      ArtifactVersion = FinancialPackageArtifactVersions.Text, FrameworkVersion = "IFRS", TemplateVersion = "template-v1",
      ArtifactSha256Hex = Hashing.Sha256Hex(artifactBytes), ArtifactBytes = artifactBytes,
      CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    AddCanonicalValidations(db, scope, packageId);
    return packageId;
  }

  /// <summary>A validated package carries every mandatory validation the build records; readiness requires the full population.</summary>
  private static void AddCanonicalValidations(AuditSphereDbContext db, Scope scope, Guid packageId)
  {
    foreach (var code in FinancialPackageValidationReadiness.RequiredCodes)
      db.FinancialPackageValidations.Add(new FinancialPackageValidation
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, FinancialPackageId = packageId, Code = code, Passed = true,
        Detail = "Seeded canonical validation for a validated package.", CreatedAt = DateTimeOffset.UtcNow
      });
  }

  private static async Task<Guid> AddMultiLinePackageAsync(AuditSphereDbContext db, Scope scope, Guid clientId, Guid engagementId,
    IReadOnlyList<(string Destination, string Section, decimal Amount)> lines,
    string? suffix = null, string currency = "USD")
  {
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var digest = Hashing.Sha256Hex($"package-{clientId:D}-{suffix ?? "multiline"}");
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, SourceKind = "Raw",
      Revision = 1, LegalEntityKey = clientId.ToString("D"), Currency = currency, RawFileSha256Hex = digest,
      NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true, ValidationStatus = "Accepted",
      ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m, ImportedAt = now, ImportedByUserId = scope.Preparer.Id
    });
    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      Status = "Finalized", ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, DatasetId = datasetId,
      Version = 1, Generation = 1, TaxonomyVersion = "tax-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      Status = AccountingPackageStates.MappingApproved, CreatedByUserId = scope.Preparer.Id, ApprovedByUserId = scope.Reviewer.Id,
      ApprovedAt = now, CreatedAt = now
    });
    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      AdjustmentPlanId = planId, Currency = currency, ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, AdjustedDatasetId = adjustedId,
      MappingVersionId = mappingId, AdjustmentPlanId = planId, Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      TaxonomyVersion = "tax-v1", TemplateVersion = "template-v1", CalculationEngineVersion = "test-engine",
      CalculationHash = digest, Currency = currency, Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    int accountIndex = 1000;
    foreach (var (destination, section, amount) in lines)
    {
      db.FinancialPackageLines.Add(new FinancialPackageLine
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, FinancialPackageId = packageId,
        SourceAccountCode = (accountIndex += 10).ToString(), DestinationCode = destination, StatementSection = section,
        Amount = amount, Fraction = 1m, Currency = currency, AdjustedSnapshotId = adjustedId, CreatedAt = now
      });
    }
    var artifactBytes = System.Text.Encoding.UTF8.GetBytes($"package-artifact|{packageId:D}|{digest}");
    db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId,
      FinancialPackageId = packageId, PackageRevision = 1, PackageGeneration = 1, PackageHash = digest,
      ArtifactVersion = FinancialPackageArtifactVersions.Text, FrameworkVersion = "IFRS", TemplateVersion = "template-v1",
      ArtifactSha256Hex = Hashing.Sha256Hex(artifactBytes), ArtifactBytes = artifactBytes,
      CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    AddCanonicalValidations(db, scope, packageId);
    return packageId;
  }

  private static async Task<Guid> AddReviewedAccountingProcedureResultAsync(
    AuditSphereDbContext db, Scope scope, string suffix, Guid preparerId, Guid reviewerId,
    Guid? clientId = null, Guid? engagementId = null)
  {
    var client = clientId ?? scope.ClientA;
    var engagement = engagementId ?? scope.EngagementA;
    Assert.NotEqual(preparerId,reviewerId);
    var preparer=await db.Users.AsNoTracking().SingleAsync(x=>x.FirmId==scope.FirmId && x.Id==preparerId);
    var reviewer=await db.Users.AsNoTracking().SingleAsync(x=>x.FirmId==scope.FirmId && x.Id==reviewerId);
    var originalEpoch=preparer.SessionEpoch;
    var temporaryGrant=new RoleGrant{Id=Guid.CreateVersion7(),FirmId=scope.FirmId,UserId=preparerId,Role="Staff",ClientId=client,EngagementId=engagement,
      GrantedAt=DateTimeOffset.UtcNow,GrantedByUserId=scope.Partner.Id,Reason="SYNTHETIC disposable fixture publication only; revoked before assertions."};
    db.RoleGrants.Add(temporaryGrant);
    var procedureId=Guid.CreateVersion7();
    db.AuditProcedures.Add(new AuditProcedure{Id=procedureId,FirmId=scope.FirmId,ClientId=client,EngagementId=engagement,
      SourceProcedureId="ACCT-"+suffix,SourceSectionNumber=1,SourceSectionTitle="Accounting evidence",SourceWording="Review the accounting evidence.",
      ApplicabilityStatus=AuditApplicabilityStatuses.Applicable,Title="Accounting evidence review",Status=AuditProcedureStatuses.Planned,CreatedAt=DateTimeOffset.UtcNow});
    await db.SaveChangesAsync();
    var planning = await MaterialityEngineService.GetLatestAsync(db, scope.FirmId, engagement);
    if (planning?.State != MaterialityCalculationStates.Approved)
      await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, client, engagement,
        new ActorContext(preparerId, scope.FirmId, preparer.SessionEpoch, ["Staff"]),
        new ActorContext(reviewerId, scope.FirmId, reviewer.SessionEpoch, ["Partner"]));
    Guid resultId;
    try
    {
      var generation=await db.ClientSafetyStates.AsNoTracking().Where(x=>x.FirmId==scope.FirmId && x.Id==client).Select(x=>x.InputGeneration).SingleAsync();
      var submitted=await AuditProgramService.SubmitResultAsync(db,new ActorContext(preparerId,scope.FirmId,preparer.SessionEpoch,["Staff"]),
        new(procedureId,generation,"Reviewed the supplied accounting evidence.","{\"result\":\"PASS\"}",["accounting-fixture"],"No exception noted."));
      Assert.True(submitted.Succeeded,submitted.Message);resultId=submitted.Value!.AuditProcedureResultId;
      var reviewed=await AuditProgramService.ReviewResultAsync(db,new ActorContext(reviewerId,scope.FirmId,reviewer.SessionEpoch,["Partner"]),
        new(resultId,AuditProcedureReviewDecisions.Reviewed,"Evidence and conclusion agree."));
      Assert.True(reviewed.Succeeded,reviewed.Message);
    }
    finally
    {
      await db.RoleGrants.Where(x=>x.Id==temporaryGrant.Id && x.FirmId==scope.FirmId && x.UserId==preparerId && x.ClientId==client && x.EngagementId==engagement)
        .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    }
    // Query persisted identities only after the temporary fixture authority has been removed.
    Assert.NotNull(await db.RoleGrants.AsNoTracking().Where(x=>x.Id==temporaryGrant.Id).Select(x=>x.RevokedAt).SingleAsync());
    Assert.Equal(originalEpoch,await db.Users.AsNoTracking().Where(x=>x.Id==preparerId).Select(x=>x.SessionEpoch).SingleAsync());
    Assert.False(await db.RoleGrants.AsNoTracking().AnyAsync(x=>x.FirmId==scope.FirmId && x.UserId==preparerId && x.Role=="Staff" && x.ClientId==client && x.EngagementId==engagement && x.RevokedAt==null));
    var result=await db.AuditProcedureResults.AsNoTracking().SingleAsync(x=>x.Id==resultId);
    var paper=await db.Workpapers.AsNoTracking().SingleAsync(x=>x.Id==result.WorkpaperId);
    var submission=await db.WorkpaperSubmissions.AsNoTracking().SingleAsync(x=>x.WorkpaperId==paper.Id);
    var review=await db.AuditProcedureReviews.AsNoTracking().SingleAsync(x=>x.AuditProcedureResultId==resultId);
    Assert.Equal(preparerId,result.PreparedByUserId);Assert.Equal(preparerId,paper.ActorId);Assert.Equal(preparerId,submission.ActorId);
    Assert.Equal(reviewerId,result.ReviewedByUserId);Assert.Equal(reviewerId,review.ReviewerUserId);Assert.NotEqual(result.PreparedByUserId,result.ReviewedByUserId);
    Assert.Equal(client,result.ClientId);Assert.Equal(engagement,result.EngagementId);
    Assert.Equal(client,paper.ClientId);Assert.Equal(engagement,paper.EngagementId);Assert.Equal(client,submission.ClientId);Assert.Equal(engagement,submission.EngagementId);
    Assert.Equal(1L,result.Revision);Assert.Equal(1L,paper.Revision);Assert.Equal(1L,submission.Revision);Assert.Equal(1L,review.ResultRevision);
    Assert.NotNull(result.ReviewedAt);Assert.True(result.ReviewedAt>=result.SubmittedAt);Assert.True(result.ReviewedAt>=submission.SubmittedAt);
    Assert.True(review.CreatedAt>=result.SubmittedAt);Assert.True(review.CreatedAt>=submission.SubmittedAt);
    return resultId;
  }

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role,
    Guid? clientId = null, Guid? engagementId = null) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = clientId, EngagementId = engagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static RoleGrant ClientPartnerGrant(Scope scope) => new()
  {
    Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = scope.Partner.Id, Role = "Partner",
    ClientId = scope.ClientA, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id,
    Reason = "Synthetic client-level Partner authorization fixture"
  };

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
