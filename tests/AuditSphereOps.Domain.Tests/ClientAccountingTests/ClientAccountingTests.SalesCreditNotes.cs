using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  public async Task ClientSalesCreditNoteIsPositiveLinkedLimitedAndPostsAsUnappliedCredit()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var seed = await SeedSalesWorkflow(pg, scope);
    var maker = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    ClientSalesInvoiceCommandReceipt invoiceSubmission;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = new ClientSalesInvoiceSubmitRequest(Guid.CreateVersion7(), seed.Draft.InvoiceId, 1, seed.Role,
        "Untaxed client service", null, "Accepted client service record", "");
      var preview = await ClientSalesInvoiceWorkflow.PreviewAsync(db, maker, scope.ClientA, request);
      Assert.True(preview.Succeeded, preview.Message);
      var submitted = await ClientSalesInvoiceWorkflow.SubmitAsync(db, maker, scope.ClientA, request with { PreviewDigest = preview.Value!.Digest });
      Assert.True(submitted.Succeeded, submitted.Message); invoiceSubmission = submitted.Value!;
      var reviewPreview = await ClientSalesInvoiceWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, invoiceSubmission.SubmissionId);
      Assert.True(reviewPreview.Succeeded, reviewPreview.Message);
      var posted = await ClientSalesInvoiceWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), invoiceSubmission.SubmissionId, "APPROVE", "Reviewed invoice", reviewPreview.Value!.Digest));
      Assert.True(posted.Succeeded, posted.Message);
    }

    var credit = new ClientSalesCreditPreviewRequest(Guid.CreateVersion7(), "CN-001", seed.Draft.InvoiceId,
      seed.Period, new DateOnly(2026, 1, 20), "Partial service cancellation", null,
      "Client-approved cancellation evidence", [new(1, 25m)]);
    ClientSalesCreditCommandReceipt creditSubmission;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preview = await ClientSalesInvoiceWorkflow.PreviewCreditNoteAsync(db, maker, scope.ClientA, credit);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.Equal("25.000000", preview.Value!.TotalCredit);
      Assert.Equal("125.000000", preview.Value.OriginalAmount);
      Assert.Equal("125.000000", preview.Value.RemainingCreditLimit);
      Assert.False((await ClientSalesInvoiceWorkflow.PreviewCreditNoteAsync(db, maker, scope.ClientB, credit)).Succeeded);
      Assert.False((await ClientSalesInvoiceWorkflow.PreviewCreditNoteAsync(db, maker, scope.ClientA,
        credit with { Lines = [new(1, 125.01m)] })).Succeeded);
      var submitted = await ClientSalesInvoiceWorkflow.SubmitCreditNoteAsync(db, maker, scope.ClientA,
        new(Guid.CreateVersion7(), credit, preview.Value.Digest));
      Assert.True(submitted.Succeeded, submitted.Message); creditSubmission = submitted.Value!;
      var review = await ClientSalesInvoiceWorkflow.PreviewCreditNoteReviewAsync(db, reviewer, scope.ClientA, creditSubmission.SubmissionId);
      Assert.True(review.Succeeded, review.Message); Assert.True(review.Value!.CanPost);
      Assert.False((await ClientSalesInvoiceWorkflow.ReviewCreditNoteAsync(db, maker, scope.ClientA,
        new(Guid.CreateVersion7(), creditSubmission.SubmissionId, "APPROVE", "Self approval", review.Value.Digest))).Succeeded);
      var posted = await ClientSalesInvoiceWorkflow.ReviewCreditNoteAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), creditSubmission.SubmissionId, "APPROVE", "Reviewed partial credit", review.Value.Digest));
      Assert.True(posted.Succeeded, posted.Message);

      var item = await db.ClientSalesCreditNoteOpenItems.SingleAsync(x => x.SubmissionId == creditSubmission.SubmissionId);
      Assert.Equal("CREDIT", item.Direction); Assert.Equal(25m, item.OriginalAmount);
      Assert.Equal(125m, (await db.ClientSalesInvoiceOpenItems.SingleAsync(x => x.InvoiceId == seed.Draft.InvoiceId)).OriginalAmount);
      var journalLines = await db.ClientOperationalJournalLines.Where(x => x.JournalId == item.JournalId).ToListAsync();
      Assert.Equal(2, journalLines.Count); Assert.Equal(25m, journalLines.Sum(x => x.Debit)); Assert.Equal(25m, journalLines.Sum(x => x.Credit));
      Assert.DoesNotContain(journalLines, x => x.AccountCode == "1100" && x.Debit > 0);
      Assert.Single(await db.ClientSalesCreditNoteDecisions.ToListAsync());
      Assert.Equal(4, (await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, seed.Period)).Value!.TotalEntries);
      var cumulative = credit with { CreditNoteId = Guid.CreateVersion7(), CreditNoteReference = "CN-002", Lines = [new(1, 100.01m)] };
      Assert.False((await ClientSalesInvoiceWorkflow.PreviewCreditNoteAsync(db, maker, scope.ClientA, cumulative)).Succeeded);
      var delete = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM client_sales_credit_note_open_items"));
      Assert.Equal("23514", delete.SqlState);
    }
  }
}
