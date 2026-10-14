using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorkerHost = AuditSphereOps.Worker.Worker;

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
    // Every movement is linked to its own accepted journal, so the lineage check passes and only the bridge rules are under test.
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "cash-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m, MovementDate: new DateOnly(2026, 6, 30),
           ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-cash-1"], ClassificationRationale: "Receipt agreed to the synthetic bank ledger.",
           SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -100m)]),
         new CashFlowLineInput("OPERATING", "Depreciation adjustment", 25m, IsNonCash: true, MovementDate: new DateOnly(2026, 6, 30),
           ActivityCode: "NON_CASH_ADJUSTMENT", SourceReferences: ["fixed-asset-register-1"], ClassificationRationale: "Noncash depreciation is an indirect-method adjustment.",
           SourceAllocations: [new CashFlowSourceAllocationInput(source.Depreciation, 25m)]),
         new CashFlowLineInput("INVESTING", "Equipment acquired under finance lease", 500m, IsNonCash: true, MovementDate: new DateOnly(2026, 6, 30),
           ActivityCode: "NON_CASH_ACQUISITION", SourceReferences: ["lease-contract-1"], ClassificationRationale: "Lease acquisition did not consume cash in the period.",
           SourceAllocations: [new CashFlowSourceAllocationInput(source.NoncashEquipment, 500m)])],
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
        // Without links the population check, not the lineage check, is the refusal under test.
        CashFlowLines = [request.SupplementaryInformation!.CashFlowLines[0] with { Amount = 99m, SourceAllocations = null }]
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
    // The receipt is 90 and the FX effect 10. The receipt is linked to its own cash journal, so the package is explained and validated.
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer, 90m);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "fx-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 90m, MovementDate: new DateOnly(2026, 6, 30),
          ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-cash-2"], ClassificationRationale: "Agreed to synthetic bank activity.",
          SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -90m)])],
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

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SourceLinkedMovements_ResolveLineageOnlyWhenFullyAllocatedToAcceptedGeneralLedger()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    // The ledger is accepted before the mapping and plan are fixed: accepting a source later changes the input generation.
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    BuildFinancialPackageRequest Request(string template, params CashFlowLineInput[] lines) => new(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", template)
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(0m, 100m, lines,
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-cash-1")],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-bank-statement-1"], ClosingCashEvidenceReferences: ["closing-bank-statement-1"])
    };
    static CashFlowLineInput Movement(string section, string description, decimal amount, bool nonCash, string activity,
      params CashFlowSourceAllocationInput[] allocations) => new(section, description, amount, IsNonCash: nonCash,
        MovementDate: new DateOnly(2026, 6, 30), ActivityCode: activity, SourceReferences: ["gl-source-linked"],
        ClassificationRationale: "Linked to accepted general-ledger lines.",
        SourceAllocations: allocations.Length == 0 ? null : allocations);

    // Fully allocated: the receipt and the noncash depreciation each total their own amounts from accepted lines.
    var complete = Request("source-linked-v1",
      Movement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, -100m)),
      Movement("OPERATING", "Depreciation adjustment", 25m, true, "NON_CASH_ADJUSTMENT", new CashFlowSourceAllocationInput(source.Depreciation, 25m)));
    Guid completePackageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, complete);
      Assert.True(built.Succeeded, built.Message);
      completePackageId = built.Value!.PackageId;
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value.Status);
      var lineage = await db.FinancialPackageValidations.AsNoTracking()
        .SingleAsync(x => x.FinancialPackageId == completePackageId && x.Code == "CASH_FLOW_SOURCE_LINEAGE");
      Assert.True(lineage.Passed, lineage.Detail);
      var allocations = await db.FinancialPackageCashFlowAllocations.AsNoTracking()
        .Where(x => x.FinancialPackageId == completePackageId).ToListAsync();
      Assert.Equal(2, allocations.Count);
      Assert.All(allocations, x => Assert.Matches("^[a-f0-9]{64}$", x.SourceLineHash));
      Assert.Equal(source.BatchId, allocations[0].SourceImportBatchId);
    }

    // Partial: the receipt has only 60 of its 100 resolved. The build still records a draft, but the lineage check fails and nothing is validated.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var partial = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, Request("source-partial-v1",
        Movement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, -60m))));
      Assert.True(partial.Succeeded, partial.Message);
      Assert.NotEqual(AccountingPackageStates.PackageValidated, partial.Value!.Status);
      Assert.False((await db.FinancialPackageValidations.AsNoTracking().SingleAsync(x => x.FinancialPackageId == partial.Value.PackageId &&
        x.Code == "CASH_FLOW_SOURCE_LINEAGE")).Passed);
    }

    // Refusals: over-allocation across movements, a repeated source line in one movement, a sign reversal, and a line that is not
    // in the accepted ledger. None of them stores a contribution.
    var storedBefore = await CountAllocationsAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var over = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, Request("source-over-v1",
        Movement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, -100m)),
        Movement("OPERATING", "Second depreciation adjustment", 25m, true, "NON_CASH_ADJUSTMENT", new CashFlowSourceAllocationInput(source.Depreciation, 25m)),
        Movement("OPERATING", "Depreciation adjustment", 25m, true, "NON_CASH_ADJUSTMENT", new CashFlowSourceAllocationInput(source.Depreciation, 25m))));
      Assert.Equal(ErrorCodes.Accounting.PackageInvalid, over.ErrorCode);

      var repeated = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, Request("source-repeat-v1",
        Movement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS",
          new CashFlowSourceAllocationInput(source.Receivable, -50m), new CashFlowSourceAllocationInput(source.Receivable, -50m))));
      Assert.Equal(ErrorCodes.Accounting.PackageSupplementaryInvalid, repeated.ErrorCode);

      var reversed = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, Request("source-sign-v1",
        Movement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, 100m))));
      Assert.Equal(ErrorCodes.Accounting.PackageSupplementaryInvalid, reversed.ErrorCode);

      var foreign = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, Request("source-foreign-v1",
        Movement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(Guid.NewGuid(), -100m))));
      Assert.Equal(ErrorCodes.GateBlocked, foreign.ErrorCode);
    }
    Assert.Equal(storedBefore, await CountAllocationsAsync(pg));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task IndependentReview_ApprovesOnlyCurrentFullyResolvedLinks_AndNeverByTheirBuilder()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    BuildFinancialPackageRequest Request(string template, params CashFlowLineInput[] lines) => new(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", template)
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(0m, 100m, lines,
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-cash-1")],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-bank-statement-1"], ClosingCashEvidenceReferences: ["closing-bank-statement-1"])
    };
    static CashFlowLineInput Movement(string section, string description, decimal amount, bool nonCash, string activity,
      params CashFlowSourceAllocationInput[] allocations) => new(section, description, amount, IsNonCash: nonCash,
        MovementDate: new DateOnly(2026, 6, 30), ActivityCode: activity, SourceReferences: ["gl-source-review"],
        ClassificationRationale: "Linked to accepted general-ledger lines.",
        SourceAllocations: allocations.Length == 0 ? null : allocations);
    var complete = Request("review-complete-v1",
      Movement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, -100m)),
      Movement("OPERATING", "Depreciation adjustment", 25m, true, "NON_CASH_ADJUSTMENT", new CashFlowSourceAllocationInput(source.Depreciation, 25m)));

    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, complete);
      Assert.True(built.Succeeded, built.Message);
      packageId = built.Value!.PackageId;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // The preparer who resolved the links cannot approve them, and nothing is recorded.
      Assert.False((await CashFlowSourceReviewCall.RecordAsync(db, preparer, packageId, "APPROVED", null)).Succeeded);
      Assert.False((await FinancialStatementService.GetCashFlowSourceReviewAsync(db, preparer, packageId)).Value?.Approved ?? false);

      var approved = await CashFlowSourceReviewCall.RecordAsync(db, reviewer, packageId, "APPROVED", null);
      Assert.True(approved.Succeeded, approved.Message);
      var state = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;
      Assert.True(state.Approved);
      Assert.Equal((2, 2, 2), (state.Movements, state.ResolvedMovements, state.SourceLinks));
      Assert.Equal(source.BatchId, state.CurrentSourceImportBatchId);
      Assert.Equal("APPROVED", Assert.Single(state.Reviews).Decision);
    }

    // A reviewer who also built the package cannot review it either.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var selfBuilt = await FinancialStatementService.BuildFinancialPackageAsync(db, reviewer, Request("review-self-v1",
        Movement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, -100m)),
        Movement("OPERATING", "Depreciation adjustment", 25m, true, "NON_CASH_ADJUSTMENT", new CashFlowSourceAllocationInput(source.Depreciation, 25m))));
      Assert.True(selfBuilt.Succeeded, selfBuilt.Message);
      var selfApproval = await CashFlowSourceReviewCall.RecordAsync(db, reviewer, selfBuilt.Value!.PackageId, "APPROVED", null);
      Assert.Equal(ErrorCodes.GateBlocked, selfApproval.ErrorCode);
    }

    // A partly resolved package cannot be approved, though it can be returned with a reason.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // A cash-account line is a transfer, not a movement, so it cannot be the source of one.
      var cashSourced = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, Request("review-cash-account-v1",
        Movement("OPERATING", "Cash receipts from customers", 95m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, -95m)),
        Movement("OPERATING", "Cash transfer", 5m, false, "CASH_TRANSFER", new CashFlowSourceAllocationInput(source.CashAccount, -5m))));
      Assert.Equal(ErrorCodes.Accounting.PackageInvalid, cashSourced.ErrorCode);
      var partialBuilt = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, Request("review-partial-v1",
        Movement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, -60m))));
      Assert.True(partialBuilt.Succeeded, partialBuilt.Message);
      Assert.Equal(ErrorCodes.GateBlocked, (await CashFlowSourceReviewCall.RecordAsync(db, reviewer, partialBuilt.Value!.PackageId, "APPROVED", null)).ErrorCode);
      Assert.Equal(ErrorCodes.Accounting.PackageInvalid, (await CashFlowSourceReviewCall.RecordAsync(db, reviewer, partialBuilt.Value.PackageId, "RETURNED", "")).ErrorCode);
      Assert.True((await CashFlowSourceReviewCall.RecordAsync(db, reviewer, partialBuilt.Value.PackageId, "RETURNED", "Receivable is only 60 supported.")).Succeeded);
    }

    // A return withdraws the approval; the next approval restores it for the same current ledger.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await CashFlowSourceReviewCall.RecordAsync(db, reviewer, packageId, "RETURNED", "Re-check the depreciation support.")).Succeeded);
      Assert.False((await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!.Approved);
      Assert.True((await CashFlowSourceReviewCall.RecordAsync(db, reviewer, packageId, "APPROVED", null)).Succeeded);
      Assert.True((await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!.Approved);
    }

    // Replacing the accepted ledger makes the approval stale: it no longer counts, and it cannot be renewed on the old source.
    var replacement = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg, fixture.FirmId, fixture.ClientId, fixture.EngagementId,
      fixture.PeriodId, fixture.BookId, fixture.Preparer.Id, reviewer, "contract-replacement");
    Assert.NotEqual(source.BatchId, replacement.BatchId);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var stale = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;
      Assert.False(stale.Approved);
      Assert.Equal(replacement.BatchId, stale.CurrentSourceImportBatchId);
      Assert.Equal(ErrorCodes.StaleRevision, (await CashFlowSourceReviewCall.RecordAsync(db, reviewer, packageId, "APPROVED", null)).ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SourceLinkedBuild_EnqueuesDurablyAndRestoresItsAllocationsOnTheWorker()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);
    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "durable-source-linked-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts from customers", 100m, IsNonCash: false, MovementDate: new DateOnly(2026, 6, 30),
           ActivityCode: "CASH_RECEIPTS", SourceReferences: ["gl-source-durable"], ClassificationRationale: "Linked to accepted general-ledger lines.",
           SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -100m)])],
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-cash-durable")],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-bank-statement-1"], ClosingCashEvidenceReferences: ["closing-bank-statement-1"])
    };
    var store = new PostgresOperationStore(new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)));
    var handler = new FinancialPackageBuildHandler();
    Guid operationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await FinancialStatementService.EnqueueFinancialPackageBuildAsync(db, preparer, request, store, handler);
      Assert.True(queued.Succeeded, queued.Message);
      operationId = queued.Value;
    }
    var workerOptions = new WorkerOptions(fixture.FirmId, "Test");
    var worker = new WorkerHost(new OperationDispatcher(store, new DurableOperationRegistry([handler], workerOptions), workerOptions),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      var package = await db.FinancialPackages.SingleAsync(x => x.TemplateVersion == "durable-source-linked-v1");
      Assert.Equal(package.Id.ToString("D"), operation.ResultIdentity);
      // The payload carried the contribution, so the worker stored it under the movement and recorded the preparer.
      var allocation = await db.FinancialPackageCashFlowAllocations.AsNoTracking().SingleAsync(x => x.FinancialPackageId == package.Id);
      Assert.Equal((source.Receivable, -100m, fixture.Preparer.Id), (allocation.GeneralLedgerLineId, allocation.SignedAmount, allocation.CreatedByUserId));
      Assert.True(await db.FinancialPackageValidations.AnyAsync(x => x.FinancialPackageId == package.Id &&
        x.Code == "CASH_FLOW_SOURCE_LINEAGE" && x.Passed));
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task CashEquipmentPayment_PreservesLedgerSignAndProjectsNegativeCash()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    // The equipment debit is +25 in the ledger. Its cash effect is an outflow of -25, which the bridge takes from 50 to 25.
    var payment = SourceRequest(planId, mappingId, "equipment-paid-v1", 125m, 100m,
      CashMovement("INVESTING", "Equipment paid in cash", -25m, false, "CAPEX_PAID", new CashFlowSourceAllocationInput(source.Equipment, 25m)));
    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, payment);
      Assert.True(built.Succeeded, built.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
      packageId = built.Value.PackageId;
      var movement = await db.FinancialPackageCashFlowLines.AsNoTracking().SingleAsync(x => x.FinancialPackageId == packageId);
      Assert.Equal(-25m, movement.Amount);
      // The stored contribution keeps the debit sign of the ledger line; the projection is derived, never written back.
      Assert.Equal(25m, (await db.FinancialPackageCashFlowAllocations.AsNoTracking().SingleAsync(x => x.FinancialPackageId == packageId)).SignedAmount);
    }

    // Matching the ledger sign to the outflow is a sign reversal: its projection would be +25 against a -25 movement.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var reversed = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId,
        "equipment-paid-reversed-v1", 125m, 100m,
        CashMovement("INVESTING", "Equipment paid in cash", -25m, false, "CAPEX_PAID", new CashFlowSourceAllocationInput(source.Equipment, -25m))));
      Assert.Equal(ErrorCodes.Accounting.PackageSupplementaryInvalid, reversed.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task CustomerCollection_UsesCashEventNotCounterpartSignAsReceiptSign()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    // The receivable is credited -100 in a journal whose cash leg is +100. The receipt is +100, with no debit revenue invented.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var collected = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId,
        "collection-v1", 0m, 100m,
        CashMovement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, -100m))));
      Assert.True(collected.Succeeded, collected.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, collected.Value!.Status);
      Assert.Equal(-100m, (await db.FinancialPackageCashFlowAllocations.AsNoTracking().SingleAsync(x => x.FinancialPackageId == collected.Value.PackageId)).SignedAmount);
    }

    // Taking the receivable's debit sign for a receipt is a sign reversal, and the build refuses it.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var reversed = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId,
        "collection-reversed-v1", 0m, 100m,
        CashMovement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, 100m))));
      Assert.Equal(ErrorCodes.Accounting.PackageSupplementaryInvalid, reversed.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task NoncashEquipmentPurchase_IsDisclosureOnly()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    // The credit side is a payable, so this journal moves no cash. It cannot be a cash payment, even with the cash bridge balanced.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var asCash = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId,
        "noncash-as-cash-v1", 600m, 100m,
        CashMovement("INVESTING", "Equipment acquired on credit as cash", -500m, false, "CAPEX_PAID", new CashFlowSourceAllocationInput(source.NoncashEquipment, 500m))));
      Assert.Equal(ErrorCodes.Accounting.PackageInvalid, asCash.ErrorCode);
    }

    // As a noncash disclosure the same line is recorded and excluded from the bridge: opening and closing cash are equal.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var disclosed = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId,
        "noncash-disclosure-v1", 100m, 100m,
        CashMovement("INVESTING", "Equipment acquired on credit", 500m, true, "NON_CASH_ACQUISITION", new CashFlowSourceAllocationInput(source.NoncashEquipment, 500m))));
      Assert.True(disclosed.Succeeded, disclosed.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, disclosed.Value!.Status);
      Assert.True((await db.FinancialPackageCashFlowLines.AsNoTracking().SingleAsync(x => x.FinancialPackageId == disclosed.Value.PackageId)).IsNonCash);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task CompoundCashJournal_PreservesAllocatedTotalAndFees()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    // The journal debits equipment 20 and fees 5 and credits cash 25. Both debits explain the single cash leg.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var complete = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId,
        "compound-v1", 125m, 100m,
        CashMovement("INVESTING", "Equipment part of compound payment", -20m, false, "CAPEX_PAID", new CashFlowSourceAllocationInput(source.CompoundEquipment, 20m)),
        CashMovement("OPERATING", "Bank fee part of compound payment", -5m, false, "BANK_CHARGES", new CashFlowSourceAllocationInput(source.CompoundFee, 5m))));
      Assert.True(complete.Succeeded, complete.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, complete.Value!.Status);
      Assert.Equal(2, await db.FinancialPackageCashFlowAllocations.CountAsync(x => x.FinancialPackageId == complete.Value.PackageId));
    }

    // Only the equipment part is linked. Its own movement totals, but the journal's cash leg is not fully explained, so the package stays a draft.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var partial = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId,
        "compound-partial-v1", 120m, 100m,
        CashMovement("INVESTING", "Equipment part of compound payment", -20m, false, "CAPEX_PAID", new CashFlowSourceAllocationInput(source.CompoundEquipment, 20m))));
      Assert.True(partial.Succeeded, partial.Message);
      Assert.NotEqual(AccountingPackageStates.PackageValidated, partial.Value!.Status);
      Assert.False((await db.FinancialPackageValidations.AsNoTracking().SingleAsync(x => x.FinancialPackageId == partial.Value.PackageId &&
        x.Code == "CASH_FLOW_SOURCE_LINEAGE")).Passed);
    }

    // The equipment debit holds 20, so drawing 25 over-consumes the source line and is refused.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var over = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId,
        "compound-over-v1", 125m, 100m,
        CashMovement("INVESTING", "Equipment part of compound payment", -25m, false, "CAPEX_PAID", new CashFlowSourceAllocationInput(source.CompoundEquipment, 25m))));
      Assert.Equal(ErrorCodes.Accounting.PackageInvalid, over.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task InternalCashTransfer_DoesNotInflateActivities()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    // Both legs of a cash-to-cash transfer sit on package cash accounts. Neither leg can become an activity movement.
    await using var db = new AuditSphereDbContext(pg.Options);
    var transfer = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId,
      "transfer-v1", 95m, 100m,
      CashMovement("OPERATING", "Transfer between bank accounts", 5m, false, "CASH_TRANSFER", new CashFlowSourceAllocationInput(source.CashAccount, -5m))));
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, transfer.ErrorCode);
  }

  private static CashFlowLineInput CashMovement(string section, string description, decimal amount, bool nonCash, string activity,
    params CashFlowSourceAllocationInput[] allocations) => new(section, description, amount, IsNonCash: nonCash,
      MovementDate: new DateOnly(2026, 6, 30), ActivityCode: activity, SourceReferences: ["gl-source-projection"],
      ClassificationRationale: "Linked to accepted general-ledger lines.", SourceAllocations: allocations.Length == 0 ? null : allocations);

  private static BuildFinancialPackageRequest SourceRequest(Guid planId, Guid mappingId, string template, decimal opening, decimal closing,
    params CashFlowLineInput[] lines) => new(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", template)
  {
    SupplementaryInformation = new FinancialSupplementaryInformation(opening, closing, lines,
      [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
       new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
      [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-cash-1")],
      CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-bank-statement-1"], ClosingCashEvidenceReferences: ["closing-bank-statement-1"])
  };

  [Fact]
  [Trait("Profile", "Database")]
  public async Task TwoPeriods_RetainTheirOwnAcceptedCashSources()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);

    // A prior period in the same engagement, with its own sealed ledger accepted after the current one.
    var priorPeriodId = Guid.NewGuid();
    var priorBookId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = priorPeriodId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, PeriodCode = "FY2025",
        StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = fixture.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingBooks.Add(new ClientReportingBook
      {
        Id = priorBookId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, PeriodId = priorPeriodId,
        Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
        Status = AccountingWorkflowStates.Active, CreatedByUserId = fixture.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    var prior = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg.Options, fixture.FirmId, fixture.ClientId, fixture.EngagementId,
      priorPeriodId, priorBookId, fixture.Preparer.Id, reviewer, "contract-prior", postingDate: new DateOnly(2025, 6, 30));
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    // The current period still resolves and approves against its own ledger, although the prior period was accepted last.
    var packageId = await BuildCompletePackageAsync(pg, planId, mappingId, preparer, source, "two-periods-v1");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await CashFlowSourceReviewCall.RecordAsync(db, reviewer, packageId, "APPROVED", null)).Succeeded);
      var approved = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;
      Assert.True(approved.Approved);
      Assert.Equal(source.BatchId, approved.CurrentSourceImportBatchId);
    }

    // Replacing the current period's ledger stales only the current period. The prior period keeps its own accepted ledger.
    var replacement = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var stale = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;
      Assert.False(stale.Approved);
      Assert.Equal(replacement.BatchId, stale.CurrentSourceImportBatchId);
      var priorSelected = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(db, reviewer,
        fixture.ClientId, fixture.EngagementId, priorPeriodId);
      Assert.Equal(prior.BatchId, priorSelected.Value!.ImportBatchId);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SourceReview_ReplayReturnsOriginalDecisionAndManifest()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);
    var packageId = await BuildCompletePackageAsync(pg, planId, mappingId, preparer, source, "intent-replay-v1");

    await using var db = new AuditSphereDbContext(pg.Options);
    var before = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;
    Assert.Null(before.LatestReviewId);
    Assert.Matches("^[a-f0-9]{64}$", before.LinkDigest);
    var intent = Guid.NewGuid();
    var first = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, reviewer, packageId, "APPROVED", null,
      intent, before.LinkDigest, before.LatestReviewId);
    Assert.True(first.Succeeded, first.Message);

    // A lost reply is retried with the same intent: the stored decision comes back and nothing new is written.
    var replay = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, reviewer, packageId, "APPROVED", null,
      intent, before.LinkDigest, before.LatestReviewId);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(first.Value, replay.Value);
    Assert.Equal(1, await db.FinancialPackageCashFlowSourceReviews.CountAsync(x => x.FinancialPackageId == packageId));

    // A retry after a reload, which now sees this approval as the latest decision, still returns the stored decision.
    var reloaded = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;
    Assert.Equal(first.Value, reloaded.LatestReviewId);
    var afterReload = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, reviewer, packageId, "APPROVED", null,
      intent, reloaded.LinkDigest, reloaded.LatestReviewId);
    Assert.Equal(first.Value, afterReload.Value);

    // Another reviewer presenting this identity is refused and gets none of the stored decision.
    var otherReviewer = User(fixture.FirmId, "second-reviewer");
    db.Users.Add(otherReviewer);
    db.RoleGrants.Add(Grant(fixture.FirmId, otherReviewer, "AccountingReviewer"));
    await db.SaveChangesAsync();
    var intruder = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, Actor(otherReviewer, "AccountingReviewer"), packageId,
      "APPROVED", null, intent, reloaded.LinkDigest, reloaded.LatestReviewId);
    Assert.Equal(ErrorCodes.IdempotencyConflict, intruder.ErrorCode);

    // The same identity with a different payload is a conflict, never an overwrite of the approval.
    var changed = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, reviewer, packageId, "RETURNED", "Changed my mind.",
      intent, before.LinkDigest, before.LatestReviewId);
    Assert.Equal(ErrorCodes.IdempotencyConflict, changed.ErrorCode);
    var after = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;
    Assert.True(after.Approved);
    Assert.Equal(first.Value, after.LatestReviewId);
    Assert.Equal(1, await db.FinancialPackageCashFlowSourceReviews.CountAsync(x => x.FinancialPackageId == packageId));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SourceReview_StaleDigestOrPreviousDecisionIsRefusedOnTheServer()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);
    var packageId = await BuildCompletePackageAsync(pg, planId, mappingId, preparer, source, "intent-stale-v1");

    await using var db = new AuditSphereDbContext(pg.Options);
    var opened = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;

    // A client that reviewed other links cannot approve these: the digest is checked on the server, not in the browser.
    var otherLinks = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, reviewer, packageId, "APPROVED", null,
      Guid.NewGuid(), new string('0', 64), opened.LatestReviewId);
    Assert.Equal(ErrorCodes.StaleRevision, otherLinks.ErrorCode);
    Assert.Equal(0, await db.FinancialPackageCashFlowSourceReviews.CountAsync(x => x.FinancialPackageId == packageId));

    // The reviewer returns the package. A second reviewer who loaded the review before that cannot approve over it.
    var returned = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, reviewer, packageId, "RETURNED",
      "Re-check the depreciation support.", Guid.NewGuid(), opened.LinkDigest, opened.LatestReviewId);
    Assert.True(returned.Succeeded, returned.Message);
    var stale = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, reviewer, packageId, "APPROVED", null,
      Guid.NewGuid(), opened.LinkDigest, opened.LatestReviewId);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);

    // An opposing decision made against the current latest review still needs the current previous identity.
    var current = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;
    Assert.Equal(returned.Value, current.LatestReviewId);
    var approved = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, reviewer, packageId, "APPROVED", null,
      Guid.NewGuid(), current.LinkDigest, current.LatestReviewId);
    Assert.True(approved.Succeeded, approved.Message);
    var opposing = await FinancialStatementService.RecordCashFlowSourceReviewAsync(db, reviewer, packageId, "RETURNED",
      "Opposing decision made from the older view.", Guid.NewGuid(), current.LinkDigest, current.LatestReviewId);
    Assert.Equal(ErrorCodes.StaleRevision, opposing.ErrorCode);
    Assert.True((await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!.Approved);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SourceReview_RevokedActorDuringLockWait_RecordsNothing()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await SeedAcceptedGeneralLedgerAsync(pg, fixture, reviewer);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);
    var packageId = await BuildCompletePackageAsync(pg, planId, mappingId, preparer, source, "intent-revoked-v1");

    CashFlowSourceReviewState opened;
    await using (var db = new AuditSphereDbContext(pg.Options))
      opened = (await FinancialStatementService.GetCashFlowSourceReviewAsync(db, reviewer, packageId)).Value!;

    // Another session holds the package lock and revokes the reviewer while the decision waits for it.
    await using var locker = new AuditSphereDbContext(pg.Options);
    await using var lockTx = await locker.Database.BeginTransactionAsync();
    await locker.FinancialPackages.FromSqlInterpolated($"SELECT * FROM financial_packages WHERE id = {packageId} FOR UPDATE")
      .AsNoTracking().ToListAsync();
    var intent = Guid.NewGuid();
    await using (var grants = new AuditSphereDbContext(pg.Options))
    {
      grants.RoleGrants.Add(Grant(fixture.FirmId, fixture.Reviewer, "AccountingPreparer"));
      await grants.SaveChangesAsync();
    }
    await using var pendingDb = new AuditSphereDbContext(pg.Options);
    var pending = FinancialStatementService.RecordCashFlowSourceReviewAsync(pendingDb, reviewer, packageId, "APPROVED", null,
      intent, opened.LinkDigest, opened.LatestReviewId);
    // The decision must be queued behind the lock before the revocation commits, or the test would not exercise the re-check.
    long waiting = 0;
    for (var attempt = 0; attempt < 50 && waiting == 0 && !pending.IsCompleted; attempt++)
    {
      waiting = await locker.Database.SqlQuery<long>(
        $"SELECT COUNT(*) AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'").SingleAsync();
      if (waiting == 0) await Task.Delay(100);
    }
    var sessions = await locker.Database.SqlQuery<string>(
      $"SELECT COALESCE(string_agg(COALESCE(wait_event_type,'-') || ':' || COALESCE(wait_event,'-') || ':' || LEFT(query, 70), ' | '), '') AS \"Value\" FROM pg_stat_activity WHERE datname = current_database()").SingleAsync();
    Assert.True(waiting > 0, $"The decision never reached the package lock. Pending: {(pending.IsCompleted ? DescribeOutcome(pending) : "still running")}. Sessions: {sessions}");
    var grant = await locker.RoleGrants.SingleAsync(x => x.UserId == fixture.Reviewer.Id && x.Role == "AccountingReviewer" && x.RevokedAt == null);
    // The revocation removes only the review role; the reviewer keeps a scoped preparer grant that a scope-only check would accept.
    grant.RevokedAt = DateTimeOffset.UtcNow;
    await locker.SaveChangesAsync();
    await lockTx.CommitAsync();

    var result = await pending;
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(0, await verify.FinancialPackageCashFlowSourceReviews.CountAsync(x => x.FinancialPackageId == packageId));
  }

  private static string DescribeOutcome(Task<CommandResult<Guid>> task) =>
    task.IsFaulted ? "exception " + task.Exception!.GetBaseException().Message
      : task.Result.Succeeded ? "succeeded" : $"refused {task.Result.ErrorCode}: {task.Result.Message}";

  private static async Task<Guid> BuildCompletePackageAsync(PgTestSchema pg, Guid planId, Guid mappingId, ActorContext preparer,
    CashFlowSourceSeed.Source source, string template)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, SourceRequest(planId, mappingId, template, 0m, 100m,
      CashMovement("OPERATING", "Cash receipts from customers", 100m, false, "CASH_RECEIPTS", new CashFlowSourceAllocationInput(source.Receivable, -100m)),
      CashMovement("OPERATING", "Depreciation adjustment", 25m, true, "NON_CASH_ADJUSTMENT", new CashFlowSourceAllocationInput(source.Depreciation, 25m))));
    Assert.True(built.Succeeded, built.Message);
    Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
    return built.Value.PackageId;
  }

  private static async Task<int> CountAllocationsAsync(PgTestSchema pg)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await db.FinancialPackageCashFlowAllocations.CountAsync();
  }

  private static Task<CashFlowSourceSeed.Source> SeedAcceptedGeneralLedgerAsync(PgTestSchema pg, Fixture f, ActorContext reviewer, decimal amount = 100m) =>
    CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg.Options, f.FirmId, f.ClientId, f.EngagementId, f.PeriodId, f.BookId, f.Preparer.Id, reviewer, "contract", amount);

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
