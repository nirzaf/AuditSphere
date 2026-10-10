using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  public async Task CurrentSettlementSchemaRetainsItsGuardAndPermanentFreezeBlocksDowngrade()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using var db = new AuditSphereDbContext(pg.Options);
    var migrations = db.Database.GetMigrations().ToArray();
    var settlementIndex = Array.FindIndex(migrations, x => x.EndsWith("_ClientOpenItemAllocations", StringComparison.Ordinal));
    Assert.True(settlementIndex > 0);
    Assert.Contains(migrations[settlementIndex], await db.Database.GetAppliedMigrationsAsync());
    var tableCount = await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema=current_schema() AND table_name='client_manual_settlement_origins'").SingleAsync();
    Assert.Equal(1, tableCount);
    var guard = await db.Database.SqlQuery<string>($"SELECT pg_get_functiondef('guard_client_generic_control_post()'::regprocedure) AS \"Value\"").SingleAsync();
    Assert.Contains("client_manual_settlement_origins", guard, StringComparison.Ordinal);
    Assert.Contains("client_purchase_invoice_submissions", guard, StringComparison.Ordinal);

    var freezeIndex = Array.FindIndex(migrations, x => x.EndsWith("_PermanentFileFreeze", StringComparison.Ordinal));
    Assert.True(freezeIndex > 0);
    var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
      db.GetService<IMigrator>().MigrateAsync(migrations[freezeIndex - 1]));
    Assert.Equal(PostgresErrorCodes.RaiseException, refusal.SqlState);
    Assert.Equal("PermanentFileFreeze is a compliance safety boundary and cannot be downgraded automatically.", refusal.MessageText);
    Assert.Contains(migrations[freezeIndex], await db.Database.GetAppliedMigrationsAsync());
  }

  [Fact]
  public async Task ManualCustomerReceiptPostsOnceAndCanBeAllocatedToClientInvoice()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var seed = await SeedSalesWorkflow(pg, scope);
    var maker = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid invoiceItemId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var submit = new ClientSalesInvoiceSubmitRequest(Guid.CreateVersion7(), seed.Draft.InvoiceId, 1, seed.Role,
        "No-tax bookkeeping service", null, "Accepted client service record", "");
      var preview = await ClientSalesInvoiceWorkflow.PreviewAsync(db, maker, scope.ClientA, submit);
      Assert.True(preview.Succeeded, preview.Message);
      var submitted = await ClientSalesInvoiceWorkflow.SubmitAsync(db, maker, scope.ClientA, submit with { PreviewDigest = preview.Value!.Digest });
      Assert.True(submitted.Succeeded, submitted.Message);
      var review = await ClientSalesInvoiceWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, submitted.Value!.SubmissionId);
      Assert.True(review.Succeeded, review.Message);
      Assert.True((await ClientSalesInvoiceWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), submitted.Value.SubmissionId, "APPROVE", "Reviewed client invoice", review.Value!.Digest))).Succeeded);
      invoiceItemId = (await db.ClientSalesInvoiceOpenItems.SingleAsync(x => x.InvoiceId == seed.Draft.InvoiceId)).Id;
    }

    var request = new ClientManualSettlementDraftRequest(Guid.CreateVersion7(), scope.ClientA, seed.Period,
      Guid.Empty, "SALES_RECEIPT", "RCT-001", "Customer receipt", new(2026, 1, 25), "1000", 30m,
      "BANK-REF-001", "client evidence receipt 1");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var party = await db.ClientBookkeepingCounterparties.SingleAsync(x => x.ClientId == scope.ClientA && x.Role == "CUSTOMER");
      request = request with { CounterpartyId = party.Id };
      var preview = await ClientOperationalLedgerWorkspace.PreviewSettlementAsync(db, maker, request);
      Assert.True(preview.Succeeded, preview.Message);
      var created = await ClientOperationalLedgerWorkspace.CreateSettlementDraftAsync(db, maker, request, preview.Value!.Digest);
      Assert.True(created.Succeeded, created.Message);
      var replay = await ClientOperationalLedgerWorkspace.CreateSettlementDraftAsync(db, maker, request, preview.Value.Digest);
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(created.Value!.JournalId, replay.Value!.JournalId);
      var journal = await ClientOperationalLedgerWorkspace.GetAsync(db, maker, scope.ClientA, created.Value.JournalId);
      Assert.True(journal.Succeeded, journal.Message);
      Assert.Equal("DRAFT", journal.Value!.Status);
      Assert.Equal(2, journal.Value.Lines.Count);
      Assert.Equal("30.000000", journal.Value.Lines.Sum(x => decimal.Parse(x.Debit, System.Globalization.CultureInfo.InvariantCulture)).ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
      Assert.False((await ClientOperationalLedgerWorkspace.PreviewSettlementAsync(db, maker, request with { Reference = "BANK-REF-001" })).Succeeded);
    }

    Guid journalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      journalId = await db.ClientManualSettlementOrigins.Where(x => x.ClientId == scope.ClientA && x.Reference == "BANK-REF-001").Select(x => x.JournalId).SingleAsync();
      var preview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, maker, scope.ClientA, journalId);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.True((await ClientOperationalLedgerWorkspace.SubmitAsync(db, maker, scope.ClientA, journalId, 1, previewDigest: preview.Value!.Digest)).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(1, await db.ClientManualSettlementOrigins.CountAsync(x => x.JournalId == journalId));
      var postingGuard = await db.Database.SqlQuery<string>($"SELECT pg_get_functiondef('guard_client_generic_control_post()'::regprocedure) AS \"Value\"").SingleAsync();
      Assert.Contains("client_manual_settlement_origins", postingGuard, StringComparison.Ordinal);
      Assert.False((await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, maker, scope.ClientA, journalId,
        new(2, "APPROVE", "Self review", "", Guid.CreateVersion7()))).Succeeded);
      var preview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, scope.ClientA, journalId);
      Assert.True(preview.Succeeded, preview.Message);
      var posted = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, journalId,
        new(2, "APPROVE", "Verified externally completed receipt and source evidence", preview.Value!.Digest, Guid.CreateVersion7()));
      Assert.True(posted.Succeeded, posted.Message);
      Assert.Equal("POSTED", (await ClientOperationalLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, journalId)).Value!.Status);
      Assert.Equal(1, await db.ClientOperationalJournalDecisions.CountAsync(x => x.JournalId == journalId && x.Decision == "APPROVE"));
      Assert.Equal(2, await db.ClientOperationalJournalLines.CountAsync(x => x.JournalId == journalId));
      Assert.Empty(await db.FirmJournals.ToListAsync());
      var source = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2026, 2, 1));
      Assert.True(source.Succeeded, source.Message);
      var reconciliation = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, reviewer, scope.ClientA, seed.Period, new(2026, 2, 1));
      Assert.True(reconciliation.Succeeded, reconciliation.Message);
      Assert.True(reconciliation.Value!.Reconciled);
      Assert.Equal("NO_APPROVED_OPENING", reconciliation.Value.OpeningDetailStatus);
      Assert.Equal("95.000000", reconciliation.Value.Accounts.Single(x => x.Role == "AR").LedgerBalance);
      Assert.Equal("95.000000", reconciliation.Value.Accounts.Single(x => x.Role == "AR").OpenItemBalance);
      var receipt = source.Value!.Single(x => x.Kind == "SALES_RECEIPT" && x.SourceDocumentId == journalId);
      var allocation = new ClientOpenItemAllocationRequest(Guid.CreateVersion7(), "SALES_RECEIPT", receipt.OpenItemId,
        "ALLOCATE", "BANK-REF-001-A", "Apply reviewed manual receipt", [new(1, "SALES_INVOICE", invoiceItemId, 20m)], "");
      var allocationPreview = await ClientOpenItemAllocationWorkflow.PreviewAsync(db, maker, scope.ClientA, allocation);
      Assert.True(allocationPreview.Succeeded, allocationPreview.Message);
      var submitted = await ClientOpenItemAllocationWorkflow.SubmitAsync(db, maker, scope.ClientA, allocation with { PreviewDigest = allocationPreview.Value!.Digest });
      Assert.True(submitted.Succeeded, submitted.Message);
      var review = await ClientOpenItemAllocationWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, submitted.Value!.SubmissionId);
      Assert.True(review.Succeeded, review.Message);
      Assert.True((await ClientOpenItemAllocationWorkflow.ReviewAsync(db, reviewer, scope.ClientA, submitted.Value.SubmissionId,
        Guid.CreateVersion7(), "APPROVE", "Independent manual receipt allocation", review.Value!.Digest)).Succeeded);
      Assert.Equal(2, await db.ClientOperationalJournalLines.CountAsync(x => x.JournalId == journalId));
      Assert.Equal("10.000000", (await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2026, 2, 1))).Value!
        .Single(x => x.OpenItemId == receipt.OpenItemId).OpenAmount);
      var beforeReceipt = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, reviewer, scope.ClientA, seed.Period, new(2026, 1, 24));
      Assert.True(beforeReceipt.Succeeded, beforeReceipt.Message);
      Assert.False(beforeReceipt.Value!.Reconciled);
      Assert.Equal("125.000000", beforeReceipt.Value.Accounts.Single(x => x.Role == "AR").LedgerBalance);
      Assert.Equal("105.000000", beforeReceipt.Value.Accounts.Single(x => x.Role == "AR").OpenItemBalance);
      Assert.Equal("20.000000", beforeReceipt.Value.Accounts.Single(x => x.Role == "AR").Difference);
    }
  }

  [Fact]
  public async Task ManualSupplierPaymentPostsAgainstClientPayablesWithoutInitiatingPayment()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var maker = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid periodId, supplierId, apRoleId, chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new() { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1, Rationale = "Synthetic purchase service",
        EvaluationTemplateVersion = "TEST-1", EvaluationSnapshotDigest = new string('f', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, reviewer, new(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE", ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, maker, new(scope.ClientA, "2026", new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, maker, scope.ClientA, "SUPPLIER-PAYMENT-QA", new(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, maker, chartId,
      [
        new("cash", "1000", "Client bank", "ASSET", "DEBIT", true),
        new("ap", "2100", "Supplier payables", "LIABILITY", "CREDIT", true),
        new("expense", "6000", "Office expense", "EXPENSE", "DEBIT", true)
      ])).Succeeded);
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId)).Succeeded);
      var ap = await db.ClientAccounts.SingleAsync(x => x.ChartVersionId == chartId && x.AccountCode == "2100");
      apRoleId = (await ClientAccountRoleWorkspace.ProposeAsync(db, maker, scope.ClientA, new(chartId, ap.Id, "AP", new(2026, 1, 1), null, "Reviewed client supplier control"))).Value;
      Assert.True((await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, scope.ClientA, apRoleId, "APPROVE", "Independent AP review")).Succeeded);
      supplierId = (await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, maker, scope.ClientA,
        new("Example supplier", "Example supplier", "SUPPLIER", "QA address", "QA", "", "", "", "", "", ""))).Value;
    }
    var purchase = new ClientPurchaseInvoiceDraftRequest(Guid.CreateVersion7(), null, 0, periodId, supplierId,
      "PV-PAY-001", "SUP-INV-PAY-001", new(2026, 1, 20), new(2026, 1, 10), new(2026, 1, 20), new(2026, 1, 10),
      new(2026, 2, 10), "QAR", new("QAR", 2, "AWAY_FROM_ZERO", "REJECT", 0, ""),
      [new("Office supplies", "6000", 1m, 100m, 0m, "NONE", [])], 100m, 0m, 100m, null, "Synthetic supplier evidence");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var saved = await ClientPurchaseInvoiceWorkflow.SaveDraftAsync(db, maker, scope.ClientA, purchase);
      Assert.True(saved.Succeeded, saved.Message);
      var command = new ClientPurchaseInvoiceSubmitRequest(Guid.CreateVersion7(), saved.Value!.InvoiceId, "1", apRoleId, "");
      var preview = await ClientPurchaseInvoiceWorkflow.PreviewAsync(db, maker, scope.ClientA, command);
      Assert.True(preview.Succeeded, preview.Message);
      var submitted = await ClientPurchaseInvoiceWorkflow.SubmitAsync(db, maker, scope.ClientA, command with { PreviewDigest = preview.Value!.Digest });
      Assert.True(submitted.Succeeded, submitted.Message);
      var review = await ClientPurchaseInvoiceWorkflow.PreviewReviewAsync(db, reviewer, scope.ClientA, submitted.Value!.SubmissionId);
      Assert.True(review.Succeeded, review.Message);
      Assert.True((await ClientPurchaseInvoiceWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), submitted.Value.SubmissionId, "APPROVE", "Reviewed supplier invoice", "", review.Value!.Digest))).Succeeded);
    }
    var settlement = new ClientManualSettlementDraftRequest(Guid.CreateVersion7(), scope.ClientA, periodId, supplierId,
      "SUPPLIER_PAYMENT", "PAY-001", "Record completed supplier payment", new(2026, 1, 25), "1000", 40m,
      "BANK-PAY-001", "client payment evidence");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preview = await ClientOperationalLedgerWorkspace.PreviewSettlementAsync(db, maker, settlement);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.Equal("2100", preview.Value!.ControlAccountCode);
      Assert.Equal("1000", preview.Value.CashAccountCode);
      var created = await ClientOperationalLedgerWorkspace.CreateSettlementDraftAsync(db, maker, settlement, preview.Value.Digest);
      Assert.True(created.Succeeded, created.Message);
      var journal = await ClientOperationalLedgerWorkspace.PreviewAsync(db, maker, scope.ClientA, created.Value!.JournalId);
      Assert.True(journal.Succeeded, journal.Message);
      Assert.True((await ClientOperationalLedgerWorkspace.SubmitAsync(db, maker, scope.ClientA, created.Value.JournalId, 1, previewDigest: journal.Value!.Digest)).Succeeded);
      var review = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, scope.ClientA, created.Value.JournalId);
      Assert.True(review.Succeeded, review.Message);
      Assert.True((await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, created.Value.JournalId,
        new(2, "APPROVE", "Verified already completed supplier payment", review.Value!.Digest, Guid.CreateVersion7()))).Succeeded);
      var lines = await db.ClientOperationalJournalLines.Where(x => x.JournalId == created.Value.JournalId).ToListAsync();
      Assert.Equal(2, lines.Count); Assert.Equal(40m, lines.Single(x => x.AccountCode == "2100").Debit);
      Assert.Equal(40m, lines.Single(x => x.AccountCode == "1000").Credit);
      var reconciliation = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, reviewer, scope.ClientA, periodId, new(2026, 2, 1));
      Assert.True(reconciliation.Succeeded, reconciliation.Message);
      Assert.True(reconciliation.Value!.Reconciled);
      Assert.Equal("60.000000", reconciliation.Value.Accounts.Single(x => x.Role == "AP").LedgerBalance);
      Assert.Equal("60.000000", reconciliation.Value.Accounts.Single(x => x.Role == "AP").OpenItemBalance);
      Assert.Empty(await db.FirmJournals.ToListAsync());
    }
  }

  [Fact]
  public async Task SealedCustomerReceiptLinksToInvoiceWithoutCreatingDuplicateCashPosting()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var seed = await SeedSalesWorkflow(pg, scope);
    var maker = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid invoiceItemId, receiptLineId, receiptTransactionId;
    Guid journalId, bookId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var invoiceRequest = new ClientSalesInvoiceSubmitRequest(Guid.CreateVersion7(), seed.Draft.InvoiceId, 1, seed.Role,
        "No-tax bookkeeping service", null, "Accepted client service record", "");
      var invoicePreview = await ClientSalesInvoiceWorkflow.PreviewAsync(db, maker, scope.ClientA, invoiceRequest);
      Assert.True(invoicePreview.Succeeded, invoicePreview.Message);
      var invoiceSubmission = await ClientSalesInvoiceWorkflow.SubmitAsync(db, maker, scope.ClientA,
        invoiceRequest with { PreviewDigest = invoicePreview.Value!.Digest });
      Assert.True(invoiceSubmission.Succeeded, invoiceSubmission.Message);
      var invoiceReview = await ClientSalesInvoiceWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, invoiceSubmission.Value!.SubmissionId);
      Assert.True(invoiceReview.Succeeded, invoiceReview.Message);
      Assert.True((await ClientSalesInvoiceWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), invoiceSubmission.Value.SubmissionId, "APPROVE", "Reviewed invoice", invoiceReview.Value!.Digest))).Succeeded);
      invoiceItemId = (await db.ClientSalesInvoiceOpenItems.SingleAsync(x => x.InvoiceId == seed.Draft.InvoiceId)).Id;
      journalId = (await db.ClientSalesInvoiceOpenItems.SingleAsync(x => x.Id == invoiceItemId)).JournalId;

      var book = await ClientAccountingService.CreateBookAsync(db, maker,
        new(scope.ClientA, seed.Period, "SETTLEMENT", "STATUTORY", "STATUTORY_ONLY", "QAR"));
      Assert.True(book.Succeeded, book.Message); bookId = book.Value;
      var imported = await ClientAccountingService.ImportGeneralLedgerAsync(db, maker,
        new(scope.ClientA, scope.EngagementA, seed.Period, bookId, "csv-v1", "gl-v1", new string('a', 64), "CLIENT-A", "QAR", "receipt-import",
          [new("RCPT-1", "RECEIPT-1", new(2026, 1, 25), null, "client-user", "TEST-SYSTEM", null, false, false,
            [new("RCPT-1-BANK", "1000", 30m, 0m, "QAR", 30m, 30m),
             new("RCPT-1-AR", "1100", 0m, 30m, "QAR", -30m, -30m, "CUSTOMER-1")]) ]));
      Assert.True(imported.Succeeded, imported.Message);
      receiptLineId = await db.GeneralLedgerLines.Where(x => x.FirmId == scope.FirmId && x.ClientId == scope.ClientA && x.PartyIdentifier == "CUSTOMER-1")
        .Select(x => x.Id).SingleAsync();
      receiptTransactionId = await db.GeneralLedgerLines.Where(x => x.Id == receiptLineId).Select(x => x.TransactionId).SingleAsync();
      Assert.Equal("SEALED", await db.SourceImportBatches.Where(x => x.Id == imported.Value).Select(x => x.Status).SingleAsync());
      Assert.Equal(2, await db.GeneralLedgerLines.CountAsync(x => x.TransactionId == receiptTransactionId));
    }

    var allocation = new ClientOpenItemAllocationRequest(Guid.CreateVersion7(), "SALES_RECEIPT", receiptLineId, "ALLOCATE",
      "RECEIPT-1", "Apply an existing imported customer receipt to the invoice", [new(1, "SALES_INVOICE", invoiceItemId, 20m)], "");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var before = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, maker, scope.ClientA, new(2026, 2, 1));
      Assert.True(before.Succeeded, before.Message);
      Assert.Equal("30.000000", before.Value!.Single(x => x.OpenItemId == receiptLineId).OpenAmount);
      var preview = await ClientOpenItemAllocationWorkflow.PreviewAsync(db, maker, scope.ClientA, allocation);
      Assert.True(preview.Succeeded, preview.Message); Assert.Equal("30.000000", preview.Value!.SourceAvailableAmount);
      var submitted = await ClientOpenItemAllocationWorkflow.SubmitAsync(db, maker, scope.ClientA,
        allocation with { PreviewDigest = preview.Value.Digest });
      Assert.True(submitted.Succeeded, submitted.Message);
      var review = await ClientOpenItemAllocationWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, submitted.Value!.SubmissionId);
      Assert.True(review.Succeeded, review.Message);
      Assert.True((await ClientOpenItemAllocationWorkflow.ReviewAsync(db, reviewer, scope.ClientA, submitted.Value.SubmissionId,
        Guid.CreateVersion7(), "APPROVE", "Independent receipt allocation approval", review.Value!.Digest)).Succeeded);
      var after = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2026, 2, 1));
      Assert.Equal("105.000000", after.Value!.Single(x => x.OpenItemId == invoiceItemId).OpenAmount);
      Assert.Equal("10.000000", after.Value!.Single(x => x.OpenItemId == receiptLineId).OpenAmount);
      Assert.Equal(2, await db.GeneralLedgerLines.CountAsync(x => x.TransactionId == receiptTransactionId));
      Assert.Equal(journalId, await db.ClientSalesInvoiceOpenItems.Where(x => x.Id == invoiceItemId).Select(x => x.JournalId).SingleAsync());
      Assert.Equal(1, await db.ClientOperationalJournals.CountAsync());
      Assert.Empty(await db.FirmJournals.ToListAsync());
      var generalLedger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, seed.Period);
      Assert.True(generalLedger.Succeeded, generalLedger.Message);
      Assert.Equal(2, generalLedger.Value!.TotalEntries);
    }
  }

  [Fact]
  public async Task ApprovedClientCreditAllocationDerivesBalancesAndSupportsAuditedUnallocation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var seed = await SeedSalesWorkflow(pg, scope);
    var maker = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid creditItemId, invoiceItemId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var invoiceRequest = new ClientSalesInvoiceSubmitRequest(Guid.CreateVersion7(), seed.Draft.InvoiceId, 1, seed.Role,
        "No-tax bookkeeping service", null, "Accepted client service record", "");
      var invoicePreview = await ClientSalesInvoiceWorkflow.PreviewAsync(db, maker, scope.ClientA, invoiceRequest);
      Assert.True(invoicePreview.Succeeded, invoicePreview.Message);
      var invoiceSubmission = await ClientSalesInvoiceWorkflow.SubmitAsync(db, maker, scope.ClientA,
        invoiceRequest with { PreviewDigest = invoicePreview.Value!.Digest });
      Assert.True(invoiceSubmission.Succeeded, invoiceSubmission.Message);
      var invoiceReview = await ClientSalesInvoiceWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, invoiceSubmission.Value!.SubmissionId);
      Assert.True(invoiceReview.Succeeded, invoiceReview.Message);
      Assert.True((await ClientSalesInvoiceWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), invoiceSubmission.Value.SubmissionId, "APPROVE", "Reviewed invoice", invoiceReview.Value!.Digest))).Succeeded);

      var creditRequest = new ClientSalesCreditPreviewRequest(Guid.CreateVersion7(), "ALLOC-CN-001", seed.Draft.InvoiceId,
        seed.Period, new(2026, 1, 20), "Partial service cancellation", null, "Approved credit source", [new(1, 25m)]);
      var creditPreview = await ClientSalesInvoiceWorkflow.PreviewCreditNoteAsync(db, maker, scope.ClientA, creditRequest);
      Assert.True(creditPreview.Succeeded, creditPreview.Message);
      var creditSubmission = await ClientSalesInvoiceWorkflow.SubmitCreditNoteAsync(db, maker, scope.ClientA,
        new(Guid.CreateVersion7(), creditRequest, creditPreview.Value!.Digest));
      Assert.True(creditSubmission.Succeeded, creditSubmission.Message);
      var creditReview = await ClientSalesInvoiceWorkflow.PreviewCreditNoteReviewAsync(db, reviewer, scope.ClientA, creditSubmission.Value!.SubmissionId);
      Assert.True(creditReview.Succeeded, creditReview.Message);
      Assert.True((await ClientSalesInvoiceWorkflow.ReviewCreditNoteAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), creditSubmission.Value.SubmissionId, "APPROVE", "Reviewed credit", creditReview.Value!.Digest))).Succeeded);
      creditItemId = (await db.ClientSalesCreditNoteOpenItems.SingleAsync(x => x.SubmissionId == creditSubmission.Value.SubmissionId)).Id;
      invoiceItemId = (await db.ClientSalesInvoiceOpenItems.SingleAsync(x => x.InvoiceId == seed.Draft.InvoiceId)).Id;

      var asOf = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, maker, scope.ClientA, new(2026, 2, 1));
      Assert.True(asOf.Succeeded, asOf.Message);
      Assert.Equal("125.000000", asOf.Value!.Single(x => x.OpenItemId == invoiceItemId).OpenAmount);
      Assert.Equal("25.000000", asOf.Value!.Single(x => x.OpenItemId == creditItemId).OpenAmount);
    }

    var allocate = new ClientOpenItemAllocationRequest(Guid.CreateVersion7(), "SALES_CREDIT", creditItemId, "ALLOCATE",
      "RCPT-CREDIT-1", "Apply approved customer credit to invoice", [new(1, "SALES_INVOICE", invoiceItemId, 20m)], "");
    Guid allocationSubmissionId, allocationLineId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preview = await ClientOpenItemAllocationWorkflow.PreviewAsync(db, maker, scope.ClientA, allocate);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.Equal("25.000000", preview.Value!.SourceAvailableAmount);
      Assert.Equal("125.000000", preview.Value.TargetAvailableAmounts.Single());
      Assert.False((await ClientOpenItemAllocationWorkflow.PreviewAsync(db, maker, scope.ClientB, allocate)).Succeeded);
      Assert.False((await ClientOpenItemAllocationWorkflow.PreviewAsync(db, maker, scope.ClientA,
        allocate with { Lines = [new(1, "SALES_INVOICE", invoiceItemId, 26m)] })).Succeeded);
      var submitted = await ClientOpenItemAllocationWorkflow.SubmitAsync(db, maker, scope.ClientA,
        allocate with { PreviewDigest = preview.Value.Digest });
      Assert.True(submitted.Succeeded, submitted.Message); allocationSubmissionId = submitted.Value!.SubmissionId;
      var pending = await ClientOpenItemAllocationWorkflow.PendingAsync(db, reviewer, scope.ClientA);
      Assert.True(pending.Succeeded, pending.Message); Assert.Single(pending.Value!);
      var review = await ClientOpenItemAllocationWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, allocationSubmissionId);
      Assert.True(review.Succeeded, review.Message); Assert.Equal(allocationSubmissionId, review.Value!.SubmissionId);
      Assert.False((await ClientOpenItemAllocationWorkflow.ReviewAsync(db, maker, scope.ClientA, allocationSubmissionId,
        Guid.CreateVersion7(), "APPROVE", "Self review", review.Value.Digest)).Succeeded);
      var approved = await ClientOpenItemAllocationWorkflow.ReviewAsync(db, reviewer, scope.ClientA, allocationSubmissionId,
        Guid.CreateVersion7(), "APPROVE", "Independent allocation approval", review.Value.Digest);
      Assert.True(approved.Succeeded, approved.Message);
      allocationLineId = await db.ClientOpenItemAllocationLines.Where(x => x.SubmissionId == allocationSubmissionId).Select(x => x.Id).SingleAsync();
      var balances = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2026, 2, 1));
      Assert.Equal("105.000000", balances.Value!.Single(x => x.OpenItemId == invoiceItemId).OpenAmount);
      Assert.Equal("5.000000", balances.Value!.Single(x => x.OpenItemId == creditItemId).OpenAmount);
      var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM client_open_item_allocation_lines"));
      Assert.Equal("23514", immutable.SqlState);
    }

    var reverse = new ClientOpenItemAllocationRequest(Guid.CreateVersion7(), "SALES_CREDIT", creditItemId, "UNALLOCATE",
      "RCPT-CREDIT-REV-1", "Correct an allocation made in error", [new(1, "SALES_INVOICE", invoiceItemId, 5m, allocationLineId)], "");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preview = await ClientOpenItemAllocationWorkflow.PreviewAsync(db, maker, scope.ClientA, reverse);
      Assert.True(preview.Succeeded, preview.Message);
      var submitted = await ClientOpenItemAllocationWorkflow.SubmitAsync(db, maker, scope.ClientA,
        reverse with { PreviewDigest = preview.Value!.Digest });
      Assert.True(submitted.Succeeded, submitted.Message);
      var review = await ClientOpenItemAllocationWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, submitted.Value!.SubmissionId);
      Assert.True(review.Succeeded, review.Message);
      Assert.True((await ClientOpenItemAllocationWorkflow.ReviewAsync(db, reviewer, scope.ClientA, submitted.Value.SubmissionId,
        Guid.CreateVersion7(), "APPROVE", "Approved exact allocation reversal", review.Value!.Digest)).Succeeded);
      var balances = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2026, 2, 1));
      Assert.Equal("110.000000", balances.Value!.Single(x => x.OpenItemId == invoiceItemId).OpenAmount);
      Assert.Equal("10.000000", balances.Value!.Single(x => x.OpenItemId == creditItemId).OpenAmount);
      Assert.Equal(2, await db.ClientOpenItemAllocationSubmissions.CountAsync());
      Assert.Equal(2, await db.ClientOpenItemAllocationDecisions.CountAsync());
      Assert.Empty(await db.FirmJournals.ToListAsync());
    }
  }
}
