using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
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

[Trait("Profile", "Database")]
public sealed class FinancialStatementTests
{
  private sealed record Fixture(
    Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId, Guid PeriodId, Guid BookId,
    AppUser Preparer, AppUser Reviewer);

  private sealed class TestDbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }

  [Fact]
  public async Task MappingPlanAndPackage_AreScopedDeterministicAndReviewGated()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var incomplete = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping")
        ]));
      Assert.False(incomplete.Succeeded);
      Assert.Equal("mapping.incomplete", incomplete.ErrorCode);

      var sectionMismatch = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "LIABILITIES", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
        ]));
      Assert.False(sectionMismatch.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, sectionMismatch.ErrorCode);
      Assert.Contains("statement sections", sectionMismatch.Message!, StringComparison.OrdinalIgnoreCase);
    }

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
        ]));
      Assert.True(created.Succeeded);
      mappingId = created.Value;
      db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Preparer, "AccountingReviewer"));
      await db.SaveChangesAsync();
      var selfApproval = await FinancialStatementService.ApproveMappingAsync(db, preparer, mappingId, 1);
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, selfApproval.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-001", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-001", 1, ReflectionStates.NotApplicable, string.Empty)).Succeeded);
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-001", 1)])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1");
    FinancialPackageBuildResult first;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded);
      first = built.Value!;
      Assert.Equal(AccountingPackageStates.PackageReviewRequired, first.Status);
      Assert.Equal(100m, first.StatementTotals["ASSETS"]);
      Assert.Equal(-100m, first.StatementTotals["INCOME"]);
      var package = await db.FinancialPackages.SingleAsync(x => x.Id == first.PackageId);
      Assert.Equal(fixture.PeriodId, package.PeriodId);
      Assert.Equal(fixture.BookId, package.BookId);
      Assert.Equal("STATUTORY", package.Basis);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var periodMismatch = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        request with { TemplateVersion = "template-period-mismatch", PeriodStart = "2026-02-01" });
      Assert.False(periodMismatch.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, periodMismatch.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var repeat = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(repeat.Succeeded);
      Assert.Equal(first.PackageId, repeat.Value!.PackageId);
      Assert.Equal(first.CalculationHash, repeat.Value.CalculationHash);
      Assert.Equal(2, await db.AdjustedTrialBalanceRows.CountAsync());
      var checks = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == first.PackageId).ToListAsync();
      Assert.Contains(checks, x => x.Code == "SUPPLEMENTARY_INFORMATION" && !x.Passed);
    }

    var durableRequest = request with { TemplateVersion = "template-durable-v1" };
    var operationFactory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var operationStore = new PostgresOperationStore(operationFactory);
    var operationHandler = new FinancialPackageBuildHandler();
    var workerOptions = new WorkerOptions(fixture.FirmId, "Test");
    Guid operationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await FinancialStatementService.EnqueueFinancialPackageBuildAsync(
        db, preparer, durableRequest, operationStore, operationHandler);
      Assert.True(queued.Succeeded, queued.Message);
      operationId = queued.Value;
    }
    var worker = new WorkerHost(
      new OperationDispatcher(operationStore, new DurableOperationRegistry([operationHandler], workerOptions), workerOptions),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      var durablePackage = await db.FinancialPackages.SingleAsync(x => x.TemplateVersion == "template-durable-v1");
      Assert.Equal(durablePackage.Id.ToString("D"), operation.ResultIdentity);
      Assert.True(await db.OperationEvents.AnyAsync(x => x.OperationId == operationId && x.Kind == "financial.package-built.v1"));

      var retry = await FinancialStatementService.EnqueueFinancialPackageBuildAsync(
        db, preparer, durableRequest, operationStore, operationHandler);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(operationId, retry.Value);
    }

    var completeRequest = request with
    {
      TemplateVersion = "template-v2",
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m)],
        [new DisclosureInput("CASH_POLICY", "Cash and cash equivalents are presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "No commitments were identified in the supplied management information.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-schedule-1")],
        NoteLines: [new NoteLineInput("CASH_NOTE", "CASH", 100m, "note-schedule-1")])
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var complete = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, completeRequest);
      Assert.True(complete.Succeeded);
      Assert.Equal(AccountingPackageStates.PackageValidated, complete.Value!.Status);
      Assert.NotEqual(first.CalculationHash, complete.Value.CalculationHash);
      Assert.Equal(1, await db.FinancialPackageCashFlowLines.CountAsync(x => x.FinancialPackageId == complete.Value.PackageId));
      Assert.Equal(2, await db.FinancialPackageDisclosures.CountAsync(x => x.FinancialPackageId == complete.Value.PackageId));
      Assert.Equal(1, await db.FinancialPackageEquityLines.CountAsync(x => x.FinancialPackageId == complete.Value.PackageId));
      Assert.Equal(1, await db.FinancialPackageNoteLines.CountAsync(x => x.FinancialPackageId == complete.Value.PackageId));
      var checks = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == complete.Value.PackageId).ToListAsync();
      Assert.All(checks.Where(x => x.Code is "CASH_FLOW_RECONCILED" or "DISCLOSURES_COMPLETE" or "SUPPLEMENTARY_INFORMATION"), x => Assert.True(x.Passed));
      Assert.Contains(checks, x => x.Code == "STATEMENT_CROSS_CAST" && x.Passed);
      Assert.Contains(checks, x => x.Code == "ACCOUNTING_EQUATION" && x.Passed);
      Assert.Contains(checks, x => x.Code == "EQUITY_ROLLFORWARD" && x.Passed);
      Assert.Contains(checks, x => x.Code == "EQUITY_PROFIT" && x.Passed);
      Assert.Contains(checks, x => x.Code == "COMPARATIVE_CONSISTENCY" && x.Passed);
      Assert.Contains(checks, x => x.Code == "NOTE_TO_FACE_TOTALS" && x.Passed);

      var cashFlowLineId = await db.FinancialPackageCashFlowLines.Where(x => x.FinancialPackageId == complete.Value.PackageId).Select(x => x.Id).SingleAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_cash_flow_lines SET amount = amount + 1 WHERE id = {cashFlowLineId}"));
      var equityLineId = await db.FinancialPackageEquityLines.Where(x => x.FinancialPackageId == complete.Value.PackageId).Select(x => x.Id).SingleAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_equity_lines SET closing_amount = closing_amount + 1 WHERE id = {equityLineId}"));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var invalidSupplement = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        completeRequest with { TemplateVersion = "template-v3", SupplementaryInformation = completeRequest.SupplementaryInformation! with { CashEnding = 200m } });
      Assert.False(invalidSupplement.Succeeded);
      Assert.Equal("package.supplementary.invalid", invalidSupplement.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var lineId = await db.FinancialPackageLines.Select(x => x.Id).FirstAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_lines SET amount = amount + 1 WHERE id = {lineId}"));
    }
  }

  [Fact]
  public async Task ZeroAdjustmentPlan_ProducesSourceEquivalentPackage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      mappingId = (await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")]))).Value;
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);
    }

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId, [])).Value;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var finalized = await AdjustmentPlanService.FinalizeAsync(db, preparer, planId);
      Assert.True(finalized.Succeeded);
      Assert.Equal(0, finalized.Value!.AppliedJournalCount);
      Assert.Equal(100m, finalized.Value.Balances["1000"]);
      Assert.Equal(-100m, finalized.Value.Balances["4000"]);
    }

    await using var packageDb = new AuditSphereDbContext(pg.Options);
    var package = await FinancialStatementService.BuildFinancialPackageAsync(packageDb, preparer,
      new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1"));
    Assert.True(package.Succeeded);
    Assert.Equal(100m, package.Value!.StatementTotals["ASSETS"]);
    Assert.Equal(-100m, package.Value.StatementTotals["INCOME"]);
    Assert.Equal(2, await packageDb.AdjustedTrialBalanceRows.CountAsync());
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void PackageRounding_ConservesEachSourceBalanceAndRecordsResidual()
  {
    var lines = FinancialStatementCalculator.BuildPackageLines(
      new Dictionary<string, decimal> { ["1000"] = 0.01m },
      [new("1000", "A", "ASSETS", 0.333333m, "split"),
       new("1000", "B", "ASSETS", 0.333333m, "split"),
       new("1000", "C", "ASSETS", 0.333334m, "split")], "QAR");

    Assert.Equal(0.01m, lines.Sum(x => x.Amount));
    Assert.Equal(0.003334m, lines.Single(x => x.DestinationCode == "C").Amount);
    Assert.Equal(0.000001m, lines.Sum(x => x.RoundingResidual));
  }

  [Fact]
  public async Task FinancialPackage_RendersDeterministicArtifact_WithVerifiableSha256()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(
          fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31",
          [new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
           new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")]));
      mappingId = created.Value;
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);
    }

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-001", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-001", 1, ReflectionStates.NotApplicable, string.Empty)).Succeeded);
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-001", 1)])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }

    var request = new BuildFinancialPackageRequest(
      planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1",
      new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash collections", 100m)],
        [new DisclosureInput("NOTE_1", "Summary of significant accounting policies."),
         new DisclosureInput("NOTE_2", string.Empty, NotApplicable: true, Rationale: "No discontinued operations.")]));

    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded);
      packageId = built.Value!.PackageId;
    }

    var expectedArtifactSha = string.Empty;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var artifact1 = await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId);
      Assert.True(artifact1.Succeeded);
      Assert.NotNull(artifact1.Value);
      expectedArtifactSha = artifact1.Value.ArtifactSha256Hex;
      Assert.NotEmpty(artifact1.Value.ArtifactBytes);
      Assert.Equal(artifact1.Value.ArtifactSha256Hex, Hashing.Sha256Hex(artifact1.Value.ArtifactBytes));
      Assert.Contains("=== AUDITSPHEREOPS FINANCIAL STATEMENT PACKAGE ===", artifact1.Value.RenderedText);
      Assert.Contains("Mapping Version ID:", artifact1.Value.RenderedText);
      Assert.Contains("NOTE_1: Summary of significant accounting policies.", artifact1.Value.RenderedText);
      Assert.Contains("[NOT APPLICABLE: No discontinued operations.]", artifact1.Value.RenderedText);

      // Repeat render produces byte-for-byte identical output and digest
      var artifact2 = await FinancialStatementService.RenderPackageArtifactAsync(db, reviewer, packageId);
      Assert.True(artifact2.Succeeded);
      Assert.Equal(artifact1.Value.ArtifactSha256Hex, artifact2.Value!.ArtifactSha256Hex);
      Assert.Equal(artifact1.Value.ArtifactBytes, artifact2.Value.ArtifactBytes);

      // Denied to client without accounting roles
      var clientUser = User(fixture.FirmId);
      db.Users.Add(clientUser);
      db.RoleGrants.Add(Grant(fixture.FirmId, clientUser, "ClientUser"));
      await db.SaveChangesAsync();
      var clientActor = Actor(clientUser, "ClientUser");

      var denied = await FinancialStatementService.RenderPackageArtifactAsync(db, clientActor, packageId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }

    var renderFactory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var renderStore = new PostgresOperationStore(renderFactory);
    var renderHandler = new FinancialPackageRenderHandler();
    var renderOptions = new WorkerOptions(fixture.FirmId, "Test");
    Guid renderOperationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await FinancialStatementService.EnqueueFinancialPackageRenderAsync(
        db, preparer, packageId, renderStore, renderHandler);
      Assert.True(queued.Succeeded, queued.Message);
      renderOperationId = queued.Value;
    }
    var renderWorker = new WorkerHost(
      new OperationDispatcher(renderStore, new DurableOperationRegistry([renderHandler], renderOptions), renderOptions),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance);
    Assert.True(await renderWorker.ProcessNextAsync());
    Assert.False(await renderWorker.ProcessNextAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == renderOperationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      Assert.Equal(packageId.ToString("D"), operation.ResultIdentity);
      Assert.Equal(expectedArtifactSha, operation.ResultDigest);
      Assert.True(await db.OperationEvents.AnyAsync(x => x.OperationId == renderOperationId && x.Kind == "financial.package-rendered.v1"));

      var retry = await FinancialStatementService.EnqueueFinancialPackageRenderAsync(
        db, preparer, packageId, renderStore, renderHandler);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(renderOperationId, retry.Value);
    }
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var preparer = User(firmId);
    var reviewer = User(firmId);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "FINANCIAL STATEMENT TEST",
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"),
      Grant(firmId, reviewer, "AccountingReviewer"));
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
      Id = taxonomyId, FirmId = firmId, Code = "tax-v1", Framework = "IFRS", Name = "Test taxonomy",
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

  private static AppUser User(Guid firmId) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId,
    Subject = "sub-" + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = $"{Guid.NewGuid():N}@example.test",
    DisplayName = "Synthetic", CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
