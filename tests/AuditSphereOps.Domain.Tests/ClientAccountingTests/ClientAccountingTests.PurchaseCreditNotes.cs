using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  public async Task ClientPurchaseCreditNoteIsCumulativeAndPostsOnlyAfterIndependentReview()
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
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, maker, scope.ClientA, "PURCHASE-CREDIT-QA", new(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, maker, chartId,
      [
        new("ap", "2100", "Supplier payables", "LIABILITY", "CREDIT", true),
        new("expense", "6000", "Office expense", "EXPENSE", "DEBIT", true)
      ])).Succeeded);
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId)).Succeeded);
      var ap = await db.ClientAccounts.SingleAsync(x => x.ChartVersionId == chartId && x.AccountCode == "2100");
      apRoleId = (await ClientAccountRoleWorkspace.ProposeAsync(db, maker, scope.ClientA, new(chartId, ap.Id, "AP", new(2026, 1, 1), null, "Reviewed supplier control"))).Value;
      Assert.True((await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, scope.ClientA, apRoleId, "APPROVE", "Independent AP review")).Succeeded);
      supplierId = (await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, maker, scope.ClientA,
        new("Example supplier", "Example supplier", "SUPPLIER", "QA address", "QA", "", "", "", "", "", ""))).Value;
    }

    var purchaseRequest = new ClientPurchaseInvoiceDraftRequest(Guid.CreateVersion7(), null, 0, periodId, supplierId,
      "PV-CREDIT-001", "SUP-INV-CREDIT-001", new(2026, 1, 20), new(2026, 1, 10), new(2026, 1, 20), new(2026, 1, 10),
      new(2026, 2, 10), "QAR", new("QAR", 2, "AWAY_FROM_ZERO", "REJECT", 0, ""),
      [new("Office supplies", "6000", 1m, 100m, 0m, "NONE", [])], 100m, 0m, 100m, null, "Synthetic supplier purchase evidence");
    Guid invoiceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var draft = await ClientPurchaseInvoiceWorkflow.SaveDraftAsync(db, maker, scope.ClientA, purchaseRequest);
      Assert.True(draft.Succeeded, draft.Message); invoiceId = draft.Value!.InvoiceId;
      var request = new ClientPurchaseInvoiceSubmitRequest(Guid.CreateVersion7(), invoiceId, "1", apRoleId, "");
      var preview = await ClientPurchaseInvoiceWorkflow.PreviewAsync(db, maker, scope.ClientA, request);
      Assert.True(preview.Succeeded, preview.Message);
      var submitted = await ClientPurchaseInvoiceWorkflow.SubmitAsync(db, maker, scope.ClientA, request with { PreviewDigest = preview.Value!.Digest });
      Assert.True(submitted.Succeeded, submitted.Message);
      var review = await ClientPurchaseInvoiceWorkflow.PreviewReviewAsync(db, reviewer, scope.ClientA, submitted.Value!.SubmissionId);
      Assert.True(review.Succeeded, review.Message); Assert.True(review.Value!.CanPost);
      var posted = await ClientPurchaseInvoiceWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), submitted.Value.SubmissionId, "APPROVE", "Reviewed supplier purchase", "", review.Value.Digest));
      Assert.True(posted.Succeeded, posted.Message);
    }

    var credit = new ClientPurchaseCreditPreviewRequest(Guid.CreateVersion7(), "SUP-CN-001", invoiceId, supplierId, periodId, apRoleId,
      new(2026, 1, 25), "Return of part of the purchased supplies", "", null, "Supplier credit evidence SUP-CN-001", [new(1, "6000", 40m)]);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preview = await ClientPurchaseCreditNoteWorkflow.PreviewAsync(db, maker, scope.ClientA, credit);
      Assert.True(preview.Succeeded, preview.Message); Assert.Equal("40.000000", preview.Value!.TotalCredit);
      Assert.Equal("100.000000", preview.Value.RemainingCreditLimit);
      Assert.False((await ClientPurchaseCreditNoteWorkflow.PreviewAsync(db, maker, scope.ClientB, credit)).Succeeded);
      Assert.False((await ClientPurchaseCreditNoteWorkflow.PreviewAsync(db, maker, scope.ClientA,
        credit with { CreditNoteId = Guid.CreateVersion7(), CreditNoteReference = "SUP-CN-OVER", Lines = [new(1, "6000", 100.01m)] })).Succeeded);
      var submitted = await ClientPurchaseCreditNoteWorkflow.SubmitAsync(db, maker, scope.ClientA,
        new(Guid.CreateVersion7(), credit, preview.Value.Digest));
      Assert.True(submitted.Succeeded, submitted.Message);
      Assert.Empty(await db.ClientPurchaseCreditNoteOpenItems.ToListAsync());
      var review = await ClientPurchaseCreditNoteWorkflow.PreviewReviewAsync(db, reviewer, scope.ClientA, submitted.Value!.SubmissionId);
      Assert.True(review.Succeeded, review.Message); Assert.True(review.Value!.CanPost);
      Assert.False((await ClientPurchaseCreditNoteWorkflow.ReviewAsync(db, maker, scope.ClientA,
        new(Guid.CreateVersion7(), submitted.Value.SubmissionId, "APPROVE", "Self approval", "", review.Value.Digest))).Succeeded);
      var posted = await ClientPurchaseCreditNoteWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), submitted.Value.SubmissionId, "APPROVE", "Reviewed supplier credit and linked purchase", "", review.Value.Digest));
      Assert.True(posted.Succeeded, posted.Message);
      var debit = await db.ClientPurchaseCreditNoteOpenItems.SingleAsync(x => x.SubmissionId == submitted.Value.SubmissionId);
      Assert.Equal("DEBIT", debit.Direction); Assert.Equal(40m, debit.OriginalAmount); Assert.Equal(invoiceId, debit.OriginalInvoiceId);
      var journalLines = await db.ClientOperationalJournalLines.Where(x => x.JournalId == debit.JournalId).ToListAsync();
      Assert.Equal(2, journalLines.Count); Assert.Equal(40m, journalLines.Sum(x => x.Debit)); Assert.Equal(40m, journalLines.Sum(x => x.Credit));
      Assert.Contains(journalLines, x => x.AccountCode == "2100" && x.Debit == 40m);
      Assert.Contains(journalLines, x => x.AccountCode == "6000" && x.Credit == 40m);
      var overCumulative = credit with { CreditNoteId = Guid.CreateVersion7(), CreditNoteReference = "SUP-CN-002", Lines = [new(1, "6000", 61m)] };
      Assert.False((await ClientPurchaseCreditNoteWorkflow.PreviewAsync(db, maker, scope.ClientA, overCumulative)).Succeeded);
      var unlinked = new ClientPurchaseCreditPreviewRequest(Guid.CreateVersion7(), "SUP-CN-UNLINKED-001", null, supplierId, periodId, apRoleId,
        new(2026, 1, 26), "Supplier issued a credit without an original purchase reference", "", null,
        "Supplier credit evidence SUP-CN-UNLINKED-001", [new(null, "6000", 5m)]);
      Assert.False((await ClientPurchaseCreditNoteWorkflow.PreviewAsync(db, maker, scope.ClientA, unlinked)).Succeeded);
      unlinked = unlinked with { UnlinkedExceptionRationale = "Supplier source document has no purchase reference; independent reviewer requested this exception." };
      var unlinkedPreview = await ClientPurchaseCreditNoteWorkflow.PreviewAsync(db, maker, scope.ClientA, unlinked);
      Assert.True(unlinkedPreview.Succeeded, unlinkedPreview.Message);
      var unlinkedSubmission = await ClientPurchaseCreditNoteWorkflow.SubmitAsync(db, maker, scope.ClientA,
        new(Guid.CreateVersion7(), unlinked, unlinkedPreview.Value!.Digest));
      Assert.True(unlinkedSubmission.Succeeded, unlinkedSubmission.Message);
      var unlinkedReview = await ClientPurchaseCreditNoteWorkflow.PreviewReviewAsync(db, reviewer, scope.ClientA, unlinkedSubmission.Value!.SubmissionId);
      Assert.True(unlinkedReview.Succeeded, unlinkedReview.Message); Assert.True(unlinkedReview.Value!.CanPost);
      Assert.True((await ClientPurchaseCreditNoteWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), unlinkedSubmission.Value.SubmissionId, "APPROVE", "Approved the reasoned unlinked supplier exception", "", unlinkedReview.Value.Digest))).Succeeded);
      Assert.Null((await db.ClientPurchaseCreditNoteOpenItems.SingleAsync(x => x.SubmissionId == unlinkedSubmission.Value.SubmissionId)).OriginalInvoiceId);
      Assert.Equal(2, await db.ClientPurchaseCreditNoteOpenItems.CountAsync());
      Assert.Empty(await db.FirmJournals.ToListAsync());
      var delete = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM client_purchase_credit_note_open_items"));
      Assert.Equal("23514", delete.SqlState);
    }
  }
}
