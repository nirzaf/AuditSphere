using AuditSphereOps.Application.Accounting;
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
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"), Grant(firmId, reviewer, "AccountingReviewer"),
      Grant(firmId, reviewer, "Partner"), Grant(firmId, partner, "Partner"));
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
    return packageId;
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
    return packageId;
  }

  private static async Task<Guid> AddReviewedAccountingProcedureResultAsync(
    AuditSphereDbContext db, Scope scope, string suffix, Guid preparerId, Guid reviewerId,
    Guid? clientId = null, Guid? engagementId = null)
  {
    var client = clientId ?? scope.ClientA;
    var engagement = engagementId ?? scope.EngagementA;
    var now = DateTimeOffset.UtcNow;
    var procedureId = Guid.CreateVersion7();
    var workpaperId = Guid.CreateVersion7();
    var resultId = Guid.CreateVersion7();
    var sourceProcedureId = "ACCT-" + suffix;
    db.AuditProcedures.Add(new AuditProcedure
    {
      Id = procedureId, FirmId = scope.FirmId, ClientId = client, EngagementId = engagement,
      SourceProcedureId = sourceProcedureId, SourceSectionNumber = 1, SourceSectionTitle = "Accounting evidence",
      SourceWording = "Review the accounting evidence.", ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
      CurrentResultRevision = 1, Title = "Accounting evidence review", Status = AuditProcedureStatuses.Reviewed, CreatedAt = now
    });
    db.Workpapers.Add(new Workpaper
    {
      Id = workpaperId, FirmId = scope.FirmId, ClientId = client, EngagementId = engagement, ProcedureId = procedureId,
      ActorId = preparerId, Index = sourceProcedureId, Title = "Accounting evidence review", Objective = "Support accounting evidence",
      TemplateVersion = "accounting-fixture-v1", Procedure = "Review the accounting evidence.", WorkPerformed = "Reviewed the supplied accounting evidence.",
      Conclusion = "No exception noted.", Revision = 1, Status = WorkpaperStatuses.SubmittedSnapshot, SubmittedAt = now, CreatedAt = now
    });
    db.WorkpaperSubmissions.Add(new WorkpaperSubmission
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = client, EngagementId = engagement,
      WorkpaperId = workpaperId, ActorId = preparerId, Revision = 1,
      WorkPerformed = "Reviewed the supplied accounting evidence.", Conclusion = "No exception noted.", SubmittedAt = now
    });
    db.AuditProcedureResults.Add(new AuditProcedureResult
    {
      Id = resultId, FirmId = scope.FirmId, ClientId = client, EngagementId = engagement, AuditProcedureId = procedureId,
      WorkpaperId = workpaperId, Revision = 1, InputGeneration = 1, WorkPerformed = "Reviewed the supplied accounting evidence.",
      StructuredResultJson = "{\"result\":\"PASS\"}", EvidenceReferencesJson = "[\"accounting-fixture\"]",
      Conclusion = "No exception noted.", Status = AuditProcedureResultStatuses.Reviewed, PreparedByUserId = preparerId,
      ReviewedByUserId = reviewerId, ReviewComment = "Evidence and conclusion agree.", SubmittedAt = now, ReviewedAt = now
    });
    db.AuditProcedureReviews.Add(new AuditProcedureReview
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = client, EngagementId = engagement,
      AuditProcedureResultId = resultId, AuditProcedureId = procedureId, ResultRevision = 1,
      Decision = AuditProcedureReviewDecisions.Reviewed, Comment = "Evidence and conclusion agree.", ReviewerUserId = reviewerId, CreatedAt = now
    });
    await db.SaveChangesAsync();
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

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
