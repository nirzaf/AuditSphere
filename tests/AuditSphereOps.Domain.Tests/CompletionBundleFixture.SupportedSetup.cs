using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Completion;

using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NativeSourceWorker = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public static partial class CompletionBundleFixture
{
  public static async Task<(Guid PackageId, Guid ReleaseId)> ReleasedSupportedFinancialPackageAsync(
    DbContextOptions<AuditSphereDbContext> options, CompletionBundleScope w, string? checkpointRoot = null)
  {
    var preparer = w.A("accounting-preparer", "AccountingPreparer");
    var reviewer = w.A("accounting-reviewer", "AccountingReviewer");
    var partner = w.A("partner", "Partner");
    Guid periodId, bookId, datasetId;
    await using (var db = new AuditSphereDbContext(options))
    {
      var period = await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new(w.ClientId, "BUNDLE-" + Guid.NewGuid().ToString("N")[..8], new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
      var book = await ClientAccountingService.CreateBookAsync(db, preparer,
        new(w.ClientId, periodId, "STATUTORY", "STATUTORY", "ALL_ENTITIES", "QAR"));
      Assert.True(book.Succeeded, book.Message);
      bookId = book.Value;
      const string csv = "AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\n" +
        "1000,Cash,120,QAR,TEST,CASH\n1500,Equipment,35,QAR,TEST,PPE\n2500,Borrowings,-40,QAR,TEST,BORROWINGS\n" +
        "3000,Share capital,-5,QAR,TEST,SHARE_CAPITAL\n4000,Revenue,-100,QAR,TEST,REVENUE\n4900,Revaluation surplus,-10,QAR,TEST,REVALUATION\n";
      var imported = await TrialBalanceImportService.ImportAsync(db, preparer, w.ClientId, w.EngagementId,
        csv, new(periodId, bookId, "STATUTORY"));
      Assert.True(imported.Succeeded, imported.Message);
      datasetId = imported.Value;
    }
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(options));
    var store = new PostgresOperationStore(factory);
    var workerOptions = new WorkerOptions(w.FirmId, "Test");
    var handler = new TrialBalanceValidationHandler();
    var worker = new NativeSourceWorker(new OperationDispatcher(store,
      new DurableOperationRegistry([handler], workerOptions), workerOptions),
      [new TrialBalanceDiscovery(factory, store, handler, workerOptions)], NullLogger<NativeSourceWorker>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    await using (var db = new AuditSphereDbContext(options))
    {
      var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
        new(w.ClientId, w.EngagementId, AccountingSourceKinds.TrialBalance, datasetId, null,
          "Synthetic independent six-row statement source review", Reviewed: true));
      Assert.True(accepted.Succeeded, accepted.Message);
    }
    var (source, chartId) = await ImportSupportedBundleLedgerAsync(options, w, periodId, bookId, preparer, reviewer);
    return await BuildReleasedFinancialPackageAsync(options, w, datasetId, source, preparer, reviewer, partner, checkpointRoot, chartId);
  }

  private static async Task<(CashFlowSourceSeed.Source Source, Guid ChartId)> ImportSupportedBundleLedgerAsync(
    DbContextOptions<AuditSphereDbContext> options, CompletionBundleScope w, Guid periodId, Guid bookId,
    ActorContext preparer, ActorContext reviewer)
  {
    const string tag = "completion-bundle";
    (int Journal, string Account, string Stable, decimal Amount)[] lines =
    [
      (1, "1000", "L1-cash", 100m), (1, "1100", "L1-receivable", -100m),
      (2, "1600", "L2-depreciation", 25m), (2, "1650", "L2-accumulated-depreciation", -25m),
      (3, "1000", "L3-cash-in", 5m), (3, "1000", "L3-cash-out", -5m),
      (5, "1500", "L5-equipment-on-credit", 500m), (5, "2000", "L5-payables", -500m),
      (4, "1500", "L4-equipment", 25m), (4, "1000", "L4-cash", -25m),
      (7, "1000", "L7-cash", 40m), (7, "2500", "L7-borrowing", -40m)
    ];
    var journals = lines.GroupBy(x => x.Journal).Select(g => new GeneralLedgerTransactionInput(
      "SYN-CF-J" + g.Key + "-" + tag, "SYN-CF-DOC" + g.Key + "-" + tag, new(2026, 6, 30), null,
      "Synthetic source preparer", "Synthetic cash-flow source", null, false, false,
      g.Select(x => new GeneralLedgerLineInput(x.Stable + "-" + tag, x.Account,
        Math.Max(x.Amount, 0m), Math.Max(-x.Amount, 0m), "QAR", x.Amount, x.Amount)).ToArray())).ToArray();
    await using var db = new AuditSphereDbContext(options);
    var chart = await ClientAccountingService.CreateChartVersionAsync(db, preparer, w.ClientId,
      "Synthetic completion-bundle ledger accounts", new(2026, 1, 1));
    Assert.True(chart.Succeeded, chart.Message);
    var accounts = await ClientAccountingService.AddAccountsAsync(db, preparer, chart.Value,
    [
      new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
      new("receivable", "1100", "Receivable", "ASSET", "DEBIT", true),
      new("equipment", "1500", "Equipment", "ASSET", "DEBIT", true),
      new("depreciation", "1600", "Depreciation", "EXPENSE", "DEBIT", true),
      new("accumulated-depreciation", "1650", "Accumulated depreciation", "ASSET", "CREDIT", true),
      new("payables", "2000", "Payables", "LIABILITY", "CREDIT", true),
      new("borrowings", "2500", "Borrowings", "LIABILITY", "CREDIT", true),
      new("share-capital", "3000", "Share capital", "EQUITY", "CREDIT", true),
      new("revenue", "4000", "Revenue", "REVENUE", "CREDIT", true),
      new("revaluation", "4900", "Revaluation surplus", "EQUITY", "CREDIT", true)
    ]);
    Assert.True(accounts.Succeeded, accounts.Message);
    var published = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chart.Value);
    Assert.True(published.Succeeded, published.Message);
    var imported = await ClientAccountingService.ImportGeneralLedgerAsync(db, preparer,
      new(w.ClientId, w.EngagementId, periodId, bookId, "cash-flow-source-v1", "synthetic-parser-v1",
        Hashing.Sha256Hex(JsonSerializer.Serialize(journals)), "SYNTHETIC-CASH-FLOW", "QAR",
        "Synthetic cash-flow source " + tag, journals));
    Assert.True(imported.Succeeded, imported.Message);
    var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
      new(w.ClientId, w.EngagementId, AccountingSourceKinds.GeneralLedger, null, imported.Value,
        "Synthetic cash-flow source review " + tag, Reviewed: true));
    Assert.True(accepted.Succeeded, accepted.Message);
    var stored = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.ImportBatchId == imported.Value)
      .ToDictionaryAsync(x => x.StableLineId, x => x.Id);
    Assert.Equal(lines.Length, stored.Count);
    Guid Id(string stable) => stored[stable + "-" + tag];
    return (new(imported.Value, Id("L1-receivable"), Id("L2-depreciation"), Id("L3-cash-out"),
      Id("L4-equipment"), Id("L5-equipment-on-credit"), Guid.Empty, Guid.Empty, Id("L7-borrowing")), chart.Value);
  }

  public static async Task PrepareSupportedBundleContactAsync(DbContextOptions<AuditSphereDbContext> options, CompletionBundleScope w)
  {
    var partner = w.A("partner", "Partner");
    await using var db = new AuditSphereDbContext(options);
    var contact = await PracticeCrmService.CreateClientContactAsync(db, partner,
      new(w.ClientId, "Synthetic bundle director", "director@bundle.example.test", "Director", Primary: true));
    Assert.True(contact.Succeeded, contact.Message);
    Assert.True((await PracticeCrmService.UpdateClientContactAsync(db, partner,
      new(contact.Value, "Synthetic bundle director", "director@bundle.example.test", "Director",
        Title: "Finance Director", SignatoryAuthority: "Authorized commercial contact", Primary: true))).Succeeded);
    Assert.True((await PracticeCrmService.AssignContactRoutingAsync(db, partner,
      new(w.ClientId, contact.Value, CorrespondencePurposes.Commercial))).Succeeded);
  }

  public static async Task PostSupportedBalanceFeeAsync(DbContextOptions<AuditSphereDbContext> options,
    CompletionBundleScope w, bool automateBalance = false)
  {
    var fm = w.A("finance-maker", "FinanceManager");
    var fr = w.A("finance-reviewer", "FinanceReviewer");
    var admin = w.A("administrator", "Administrator");
    var partner = w.A("partner", "Partner");
    await using var db = new AuditSphereDbContext(options);
    var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId);
    Assert.True(await db.ClientContacts.AsNoTracking().AnyAsync(x => x.FirmId == w.FirmId && x.PracticeClientId == w.ClientId &&
      x.Email == "director@bundle.example.test" && x.IsActive));
    var advance = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, fm, agreement.Id);
    Assert.True(advance.Succeeded, advance.Message);
    var advanceRow = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == advance.Value);
    Assert.Equal(12500m, advanceRow.Total);
    Assert.Equal(BillingStates.InvoicePosted, advanceRow.Status);
    if (automateBalance)
    {
      var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(options));
      var store = new PostgresOperationStore(factory);
      var worker = new WorkerOptions(w.FirmId, "Test");
      var policy = new AutomaticFeeInvoicePolicy(true, fm.UserId, admin.UserId);
      var handler = new AutomaticFeeInvoiceHandler(policy);
      var discovery = new AutomaticFeeInvoiceDiscovery(factory, store, handler, worker, policy);
      Assert.Equal(1, await discovery.EnqueuePendingAsync(default));
      var dispatcher = new OperationDispatcher(store, new DurableOperationRegistry([handler], worker), worker);
      Assert.True(await dispatcher.ProcessNextAsync());
      Assert.False(await dispatcher.ProcessNextAsync());
      var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.OperationKind == AutomaticFeeInvoiceHandler.Kind);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      Assert.Equal(0, await discovery.EnqueuePendingAsync(default));
      var invoiceId = Guid.Parse(operation.ResultIdentity!);
      Assert.Equal(BillingStates.InvoiceDraft, (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoiceId)).Status);
    }
    var balance = await FeeAgreementService.IssueBalanceInvoiceAsync(db, fm, agreement.Id);
    Assert.True(balance.Succeeded, balance.Message);
    Assert.Equal(12500m, (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == balance.Value)).Total);
    Assert.True((await BillingService.SubmitInvoiceAsync(db, fm, balance.Value)).Succeeded);
    Assert.True((await BillingService.ApproveInvoiceAsync(db, fr, balance.Value)).Succeeded);
    Assert.True((await BillingService.PostInvoiceAsync(db, fm, balance.Value)).Succeeded);
  }
}
