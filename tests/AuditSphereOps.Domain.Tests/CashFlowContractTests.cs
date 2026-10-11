using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Module 24 cash-flow contract: the reconciliation bridge covers cash
/// movements plus separately stated FX cash effects, while noncash investing and
/// financing movements are disclosed without entering the cash bridge.</summary>
public sealed class CashFlowContractTests
{
  private sealed record Fixture(
    Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId, Guid PeriodId, Guid BookId,
    AppUser Preparer, AppUser Reviewer);

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SourceBackedNonCashOperatingAndInvestingMovements_AreRetainedButExcludedFromCashBridge()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "cash-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m, MovementDate: new DateOnly(2026, 6, 30),
           ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-cash-1"], ClassificationRationale: "Receipt agreed to the synthetic bank ledger."),
         new CashFlowLineInput("OPERATING", "Depreciation adjustment", 25m, IsNonCash: true, MovementDate: new DateOnly(2026, 6, 30),
           ActivityCode: "NON_CASH_ADJUSTMENT", SourceReferences: ["fixed-asset-register-1"], ClassificationRationale: "Noncash depreciation is an indirect-method adjustment."),
         new CashFlowLineInput("INVESTING", "Equipment acquired under finance lease", 500m, IsNonCash: true, MovementDate: new DateOnly(2026, 6, 30),
           ActivityCode: "NON_CASH_ACQUISITION", SourceReferences: ["lease-contract-1"], ClassificationRationale: "Lease acquisition did not consume cash in the period.")],
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-cash-1")],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-bank-statement-1"], ClosingCashEvidenceReferences: ["closing-bank-statement-1"])
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      var builtPackage = built.Value!;
      var failedCodes = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == builtPackage.PackageId && !x.Passed)
        .Select(x => x.Code).ToListAsync();
      Assert.True(builtPackage.Status == AccountingPackageStates.PackageValidated,
        $"Failed: {string.Join(",", failedCodes)}");
      Assert.Equal(3, await db.FinancialPackageCashFlowLines.CountAsync(x => x.FinancialPackageId == builtPackage.PackageId));
      var line = await db.FinancialPackageCashFlowLines.SingleAsync(x => x.Description.Contains("finance lease"));
      Assert.True(line.IsNonCash);
      Assert.Equal(new DateOnly(2026, 6, 30), line.MovementDate);
      Assert.Equal(["lease-contract-1"], line.SourceReferences);
      var operating = await db.FinancialPackageCashFlowLines.SingleAsync(x => x.ActivityCode == "CASH_RECEIPTS");
      Assert.False(operating.IsNonCash);
      Assert.Equal("CASH_RECEIPTS", operating.ActivityCode);
    }

    // An unreferenced movement is refused, while the noncash operating adjustment above is valid.
    var invalid = request with
    {
      TemplateVersion = "cash-v1-invalid",
      SupplementaryInformation = request.SupplementaryInformation with
      {
        CashFlowLines = [new CashFlowLineInput("OPERATING", "Unreferenced cash flow", 100m,
          MovementDate: new DateOnly(2026, 6, 30), ActivityCode: "OTHER", SourceReferences: [],
          ClassificationRationale: "No evidence was supplied.")]
      }
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rejected = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, invalid);
      Assert.False(rejected.Succeeded);
      Assert.Contains("evidence reference", rejected.Message!, StringComparison.OrdinalIgnoreCase);
    }

    var mismatchedCashPopulation = request with
    {
      TemplateVersion = "cash-v1-cash-population-mismatch",
      SupplementaryInformation = request.SupplementaryInformation with
      {
        CashEnding = 99m,
        CashFlowLines = [request.SupplementaryInformation!.CashFlowLines[0] with { Amount = 99m }]
      }
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rejected = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, mismatchedCashPopulation);
      Assert.False(rejected.Succeeded);
      Assert.Contains("does not cross-cast", rejected.Message!, StringComparison.OrdinalIgnoreCase);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task FxCashEffect_ReconcilesSeparatelyAndPersistsEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "fx-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 110m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m, MovementDate: new DateOnly(2026, 6, 30),
          ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-cash-2"], ClassificationRationale: "Agreed to synthetic bank activity.")],
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-fx-1")],
        FxCashEffects: [new FxCashEffectInput("USD/QAR", 10m, "fx-rate-evidence-1")],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-bank-statement-2"], ClosingCashEvidenceReferences: ["closing-bank-statement-2"])
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
      var fx = await db.FinancialPackageFxEffects.SingleAsync(x => x.FinancialPackageId == built.Value.PackageId);
      Assert.Equal("USD/QAR", fx.CurrencyPair);
      Assert.Equal(10m, fx.Amount);
      Assert.Equal("fx-rate-evidence-1", fx.EvidenceReference);
    }

    // Removing the FX effect leaves the bridge unexplained: fail closed.
    var unexplained = request with
    {
      TemplateVersion = "fx-v1-unexplained",
      SupplementaryInformation = request.SupplementaryInformation! with { FxCashEffects = null }
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rejected = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, unexplained);
      Assert.False(rejected.Succeeded);
      Assert.Contains("bridge", rejected.Message!, StringComparison.OrdinalIgnoreCase);
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
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "CASH FLOW CONTRACT TEST", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new AuditSphereOps.Domain.Completion.FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"), Grant(firmId, reviewer, "AccountingReviewer"));
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
      Id = taxonomyId, FirmId = firmId, Code = "tax-v1", Framework = "IFRS", Name = "Cash contract taxonomy",
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
    return new Fixture(firmId, clientId, engagementId, datasetId, periodId, bookId, preparer, reviewer);
  }
}
