using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Module 24/25 fail-closed gates: blocking package validations refuse the
/// VALIDATED state, package decisions and release readiness, and review stages run in
/// order with one person per stage per package version.</summary>
public sealed class FinancialPackageGateTests
{
  private sealed record Fixture(
    Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId, Guid PeriodId, Guid BookId,
    AppUser Preparer, AppUser Reviewer, AppUser Partner);

  [Fact]
  [Trait("Profile", "Database")]
  public async Task BlockingValidationFailure_DemotesPackageAndBlocksDecisionsAndReadiness()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    // Equity profit/OCI amounts deliberately disagree with the mapped income totals,
    // while the per-line rollforward still reconciles, so only EQUITY_PROFIT blocks.
    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "gate-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m)],
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 60m, 0m, 0m, 0m, 60m, "equity-gate-1")])
    };
    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      packageId = built.Value!.PackageId;
      Assert.Equal(AccountingPackageStates.PackageReviewRequired, built.Value.Status);
      var validations = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId).ToListAsync();
      Assert.Contains(validations, x => x.Code == "EQUITY_PROFIT" && !x.Passed);
      Assert.DoesNotContain(validations, x => !x.Passed && x.Code != "EQUITY_PROFIT" &&
        FinancialPackageReviewService.BlockingValidationCodes.Contains(x.Code));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var management = await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "gate-management", "Management approval attempt."));
      Assert.False(management.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, management.ErrorCode);
      Assert.Contains("EQUITY_PROFIT", management.Message, StringComparison.Ordinal);
    }

    // Defense in depth: a pre-gate legacy row already carrying VALIDATED with a
    // failed blocking validation (financial_packages is append-only, so clone it in)
    // is still refused by the decision, readiness and queue paths.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var legacyId = Guid.CreateVersion7();
      var legacyHash = Hashing.Sha256Hex("legacy-gate-package");
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO financial_packages (id, firm_id, client_id, engagement_id, adjusted_dataset_id, mapping_version_id,
          adjustment_plan_id, period_id, book_id, basis, framework, period_start, period_end, taxonomy_version,
          template_version, calculation_engine_version, calculation_hash, currency, revision, generation, status, created_at)
        SELECT {legacyId}, firm_id, client_id, engagement_id, adjusted_dataset_id, mapping_version_id,
          adjustment_plan_id, period_id, book_id, basis, framework, period_start, period_end, taxonomy_version,
          'legacy-gate-template', calculation_engine_version, {legacyHash}, currency, revision, generation,
          'VALIDATED', created_at
        FROM financial_packages WHERE id = {packageId}
        """);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO financial_package_validations (id, firm_id, financial_package_id, code, passed, detail, created_at)
        SELECT {Guid.CreateVersion7()}, firm_id, {legacyId}, 'ACCOUNTING_EQUATION', false,
          'Legacy validation failure.', created_at
        FROM financial_package_validations WHERE financial_package_id = {packageId} LIMIT 1
        """);
      var partner = Actor(fixture.Partner, "Partner");
      var queue = await FinancialPackageReviewService.GetStaffQueueAsync(db, partner);
      Assert.True(queue.Succeeded, queue.Message);
      Assert.DoesNotContain(queue.Value!, x => x.PackageId == legacyId);

      var reviewable = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, legacyId, requirePartner: true);
      Assert.False(reviewable.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, reviewable.ErrorCode);
      Assert.Contains("ACCOUNTING_EQUATION", reviewable.Message!, StringComparison.Ordinal);

      var decision = await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(legacyId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "gate-partner", "Partner attempt on a legacy blocked package."));
      Assert.False(decision.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, decision.ErrorCode);
      Assert.Contains("ACCOUNTING_EQUATION", decision.Message!, StringComparison.Ordinal);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task PackageDecisions_RequireStageOrderAndDistinctPersons()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var partner = Actor(fixture.Partner, "Partner");
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "stage-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m)],
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-stage-1")])
    };
    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      packageId = built.Value!.PackageId;
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value.Status);
      var rendered = await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId);
      Assert.True(rendered.Succeeded, rendered.Message);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var skippedManagement = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "stage-accounting", "Accounting review before management approval."));
      Assert.False(skippedManagement.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, skippedManagement.ErrorCode);
      Assert.Contains("MANAGEMENT_APPROVAL", skippedManagement.Message!, StringComparison.Ordinal);

      var management = await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "stage-management", "Management approved the exact package."));
      Assert.True(management.Succeeded, management.Message);

      var accounting = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "stage-accounting", "Accounting reviewed the exact package."));
      Assert.True(accounting.Succeeded, accounting.Message);

      var selfPartner = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "stage-partner", "Same person switching roles to partner."));
      Assert.False(selfPartner.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, selfPartner.ErrorCode);
      Assert.Contains("two different stages", selfPartner.Message!, StringComparison.Ordinal);

      var partnerApproval = await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "stage-partner", "Independent partner approval."));
      Assert.True(partnerApproval.Succeeded, partnerApproval.Message);

      var ready = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, packageId, requirePartner: true);
      Assert.True(ready.Succeeded, ready.Message);
    }
  }

  private static async Task<(Guid MappingId, Guid PlanId)> CreateApprovedMappingAndPlanAsync(
    PgTestSchema pg, Fixture fixture, ActorContext preparer, ActorContext reviewer)
  {
    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      mappingId = (await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new MappingAllocationInput("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
          new MappingAllocationInput("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
        ]))).Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId, [])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }
    return (mappingId, planId);
  }

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = null, EngagementId = null, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    var partner = User(firmId, "partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "PACKAGE GATE TEST", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer, partner);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"),
      Grant(firmId, reviewer, "AccountingReviewer"),
      Grant(firmId, reviewer, "Partner"),
      Grant(firmId, partner, "Partner"));
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "FY2026",
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
      Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
      CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientReportingBooks.Add(new ClientReportingBook
    {
      Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId,
      Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
      Status = AccountingWorkflowStates.Active, CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    var taxonomyId = Guid.NewGuid();
    db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
    {
      Id = taxonomyId, FirmId = firmId, Code = "tax-v1", Framework = "IFRS", Name = "Gate taxonomy",
      Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
      CreatedByUserId = preparer.Id, ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.ReportingTaxonomyNodes.AddRange(
      new ReportingTaxonomyNode
      {
        Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "CASH", Name = "Cash",
        StatementSection = "ASSETS", DisplaySign = "SIGNED", NormalBalance = "DEBIT", IsPosting = true,
        Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
      },
      new ReportingTaxonomyNode
      {
        Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "REVENUE", Name = "Revenue",
        StatementSection = "INCOME", DisplaySign = "SIGNED", NormalBalance = "CREDIT", IsPosting = true,
        Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
      });
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      PeriodId = periodId, BookId = bookId, Basis = "STATUTORY",
      SourceKind = "Raw", Revision = 1, Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.Id
    });
    db.TrialBalanceRows.AddRange(
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000",
        AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST"
      },
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
        AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST"
      });
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, datasetId, periodId, bookId, preparer, reviewer, partner);
  }
}
