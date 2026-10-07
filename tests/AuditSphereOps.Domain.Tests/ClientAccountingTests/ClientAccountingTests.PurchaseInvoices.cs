using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  public async Task ClientPurchaseInvoicePostsOnlyAfterIndependentDuplicateAwareApReview()
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
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, maker, scope.ClientA, "PURCHASE-QA", new(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, maker, chartId,
      [
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
    var request = new ClientPurchaseInvoiceDraftRequest(Guid.CreateVersion7(), null, 0, periodId, supplierId,
      "PV-001", "supplier- 001", new(2026, 1, 20), new(2026, 1, 10), new(2026, 1, 20), new(2026, 1, 10),
      new(2026, 2, 10), "QAR", new("QAR", 2, "AWAY_FROM_ZERO", "REJECT", 0, ""),
      [new("Office supplies", "6000", 1m, 100m, 0m, "NONE", [])], 100m, 0m, 100m, null, "Synthetic supplier invoice source");
    Guid invoiceId, submissionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var saved = await ClientPurchaseInvoiceWorkflow.SaveDraftAsync(db, maker, scope.ClientA, request);
      Assert.True(saved.Succeeded, saved.Message); invoiceId = saved.Value!.InvoiceId;
      Assert.True(saved.Value.Snapshot.LateArrival);
      var submit = new ClientPurchaseInvoiceSubmitRequest(Guid.CreateVersion7(), invoiceId, "1", apRoleId, "");
      var preview = await ClientPurchaseInvoiceWorkflow.PreviewAsync(db, maker, scope.ClientA, submit);
      Assert.True(preview.Succeeded, preview.Message); Assert.Empty(preview.Value!.DuplicateWarnings);
      Assert.Equal("100.000000", preview.Value.Gross);
      var submitted = await ClientPurchaseInvoiceWorkflow.SubmitAsync(db, maker, scope.ClientA, submit with { PreviewDigest = preview.Value.Digest });
      Assert.True(submitted.Succeeded, submitted.Message); submissionId = submitted.Value!.SubmissionId;
      Assert.Empty(await db.ClientPurchaseInvoiceOpenItems.ToListAsync());
      var review = await ClientPurchaseInvoiceWorkflow.PreviewReviewAsync(db, reviewer, scope.ClientA, submissionId);
      Assert.True(review.Succeeded, review.Message); Assert.True(review.Value!.CanPost); Assert.False(review.Value.DuplicateWarning);
      var posted = await ClientPurchaseInvoiceWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), submissionId, "APPROVE", "Checked supplier, late evidence and exact expense/AP journal", "", review.Value.Digest));
      Assert.True(posted.Succeeded, posted.Message);
      var open = await db.ClientPurchaseInvoiceOpenItems.SingleAsync(x => x.InvoiceId == invoiceId);
      Assert.Equal(supplierId, open.SupplierId); Assert.Equal(100m, open.OriginalAmount); Assert.Equal(new DateOnly(2026, 2, 10), open.DueDate);
      var journal = await db.ClientOperationalJournalLines.Where(x => x.JournalId == open.JournalId).ToListAsync();
      Assert.Equal(2, journal.Count); Assert.Equal(100m, journal.Sum(x => x.Debit)); Assert.Equal(100m, journal.Sum(x => x.Credit));
      Assert.Equal(2, (await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, periodId)).Value!.TotalEntries);
      Assert.Empty(await db.FirmJournals.ToListAsync());
    }
  }
}
