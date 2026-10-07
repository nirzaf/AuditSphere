using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
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
