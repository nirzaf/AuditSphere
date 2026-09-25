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
  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReconciliationApproval_StalesWhenSourceDigestChanges()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, scope, preparer);
    var sourceHash = Hashing.Sha256Hex("reconciliation-source");
    var datasetId = Guid.CreateVersion7();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY", SourceKind = "Raw", LegalEntityKey = "CLIENT-A",
        Currency = "QAR", RawFileSha256Hex = sourceHash, NormalizedDatasetDigest = sourceHash, Sha256Hex = sourceHash,
        Balanced = true, ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = scope.Preparer.Id
      });
      db.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash",
        Amount = 100m, Currency = "QAR", Entity = "CLIENT-A"
      });
      await db.SaveChangesAsync();
      var sealedDataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      sealedDataset.ValidationStatus = "Accepted";
      sealedDataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();
      var reconciliation = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "CASH",
          datasetId, null, ["1000"], new DateOnly(2026, 12, 31)));
      Assert.True(reconciliation.Succeeded, reconciliation.Message);
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      dataset.NormalizedDatasetDigest = Hashing.Sha256Hex("reconciliation-source-replaced");
      await db.SaveChangesAsync();
      var approval = await AccountingAnalysisService.ApproveReconciliationAsync(db, reviewer, reconciliation.Value);
      Assert.False(approval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, approval.ErrorCode);
      Assert.Equal(AccountingWorkflowStates.Stale,
        await db.AccountingReconciliations.Where(x => x.Id == reconciliation.Value).Select(x => x.Status).SingleAsync());
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReceivableAging_RetainsBasisBucketsCreditTreatmentAndSettlementLinks()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var fixture = await CreateGlFixtureAsync(pg, scope, preparer);
    var sourceHash = Hashing.Sha256Hex("aging-source");
    Guid datasetId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      datasetId = Guid.CreateVersion7();
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = fixture.PeriodId, BookId = fixture.BookId, Basis = "STATUTORY", SourceKind = "Raw",
        LegalEntityKey = "CLIENT-A", Currency = "QAR", RawFileSha256Hex = sourceHash,
        NormalizedDatasetDigest = sourceHash, Sha256Hex = sourceHash, Balanced = true,
        ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = scope.Preparer.Id
      });
      db.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Receivables",
        Amount = 100m, Currency = "QAR", Entity = "CLIENT-A"
      });
      await db.SaveChangesAsync();
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      dataset.ValidationStatus = "Accepted";
      dataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();

      var missingPolicy = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, fixture.BookId,
          "RECEIVABLES", datasetId, null, ["1000"], new DateOnly(2026, 12, 31)));
      Assert.False(missingPolicy.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, missingPolicy.ErrorCode);

      var created = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, fixture.BookId,
          "RECEIVABLES", datasetId, null, ["1000"], new DateOnly(2026, 12, 31),
          AccountingAgingRules.DueDateBasis, AccountingAgingRules.StandardRuleVersion));
      Assert.True(created.Succeeded, created.Message);

      var wrongBucket = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, created.Value,
        [new ReconciliationItemInput("AR-001", 100m, "QAR", new DateOnly(2026, 11, 30), "open invoice", "invoice-001", "OPEN",
          DateBasis: AccountingAgingRules.DueDateBasis, AgingBucket: "CURRENT", IsCredit: false)]);
      Assert.False(wrongBucket.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, wrongBucket.ErrorCode);

      var added = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, created.Value,
        [new ReconciliationItemInput("AR-001", 100m, "QAR", new DateOnly(2026, 11, 30), "open invoice", "invoice-001", "OPEN",
            DateBasis: AccountingAgingRules.DueDateBasis, AgingBucket: "31_60", IsCredit: false,
            SettlementDate: new DateOnly(2027, 1, 5), SettlementReference: "receipt-001"),
         new ReconciliationItemInput("AR-002", -25m, "QAR", new DateOnly(2026, 12, 31), "credit balance", "credit-001", "OPEN",
            DateBasis: AccountingAgingRules.DueDateBasis, AgingBucket: "CURRENT", IsCredit: true)]);
      Assert.True(added.Succeeded, added.Message);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var items = await verify.AccountingReconciliationItems.OrderBy(x => x.StableItemId).ToListAsync();
    Assert.Equal(2, items.Count);
    Assert.Equal(31, items[0].AgeDays);
    Assert.Equal("DUE_DATE", items[0].DateBasis);
    Assert.Equal("31_60", items[0].AgingBucket);
    Assert.False(items[0].IsCredit);
    Assert.Equal(new DateOnly(2027, 1, 5), items[0].SettlementDate);
    Assert.Equal("receipt-001", items[0].SettlementReference);
    Assert.True(items[1].IsCredit);
    Assert.Equal("CURRENT", items[1].AgingBucket);
  }
}
