using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class FirmReceivablesAgingQueryTests
{
  [Fact]
  public async Task ReportUsesApprovedTermsAndOnlyReceiptsCreditsAndAllocationsEffectiveByUtcAsOfDate()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.Actor(seed.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(seed.Reviewer, "FinanceReviewer");
    var adminReviewer = PbcSeed.Actor(seed.Admin, "FinanceReviewer");
    var now = DateTimeOffset.UtcNow;
    var asOf = DateOnly.FromDateTime(now.UtcDateTime).AddDays(1);
    Guid invoiceId, accountId;
    Guid termsRevisionId, oldAllocationId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(seed.FirmId, seed.Admin, "FinanceManager"),
        PbcSeed.Grant(seed.FirmId, seed.Admin, "FinanceReviewer"),
        PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"),
        PbcSeed.Grant(seed.FirmId, seed.Reviewer, "FinanceReviewer"));
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = seed.Reviewer.Id, ApprovedAt = now, CreatedAt = now
      });
      db.ClientContacts.Add(new ClientContact
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        FullName = "Synthetic Finance Contact", Email = "finance@example.test", Role = "Finance contact",
        ValidFrom = now.AddDays(-10), Primary = true
      });
      await db.SaveChangesAsync();

      var lead = new Lead
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, Name = "Synthetic ageing client",
        Source = "Referral", CreatedAt = now
      };
      var opportunity = new Opportunity
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, LeadId = lead.Id, PracticeClientId = seed.ClientId,
        ServiceRoute = "FinancialStatementAudit", EntityScope = "TEST", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now
      };
      var proposal = new Proposal
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, OpportunityId = opportunity.Id, PracticeClientId = seed.ClientId,
        Status = CrmStates.ProposalAccepted, Currency = "QAR", Fee = 200m,
        ServiceProfileId = "AUDIT-2026", Scope = "Synthetic audit scope", Deliverables = "Synthetic audit report",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = now, ResponseAt = now
      };
      db.Leads.Add(lead);
      db.Opportunities.Add(opportunity);
      db.Proposals.Add(proposal);
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ProposalId = proposal.Id, Currency = "QAR",
        BaseAmount = 200m, Fee = 200m, InputHash = Hashing.Sha256Hex("synthetic ageing quotation"),
        Status = QuotationStates.Approved, CreatedByUserId = seed.Admin.Id, ApprovedAt = now, CreatedAt = now
      });
      await db.SaveChangesAsync();

      var agreement = await FeeAgreementService.CreateAgreementAsync(db, PbcSeed.Actor(seed.Admin, "Partner"), proposal.Id);
      Assert.True(agreement.Succeeded, agreement.Message);
      var advanceInvoice = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, manager, agreement.Value);
      Assert.True(advanceInvoice.Succeeded, advanceInvoice.Message);
      invoiceId = advanceInvoice.Value;
      accountId = (await db.BillingAccounts.Where(x => x.FirmId == seed.FirmId && x.PracticeClientId == seed.ClientId)
        .Select(x => x.Id).ToArrayAsync()).Single();
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewer, invoiceId)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, invoiceId)).Succeeded);
      var unrelatedInvoice = (await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-AGING-UNLINKED", [new InvoiceLineRequest("Unlinked non-fee item", 1m, 500m)]))).Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, unrelatedInvoice)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewer, unrelatedInvoice)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, unrelatedInvoice)).Succeeded);

      var termsRequest = new SetInvoicePaymentTermsRequest(asOf.AddDays(-31),
        InvoicePaymentTermsKinds.ContractualDueDate, "Due 31 calendar days before the report date",
        "SYN-ENGAGEMENT-LETTER-R1", 0, true);
      var first = await InvoicePaymentTermsService.SubmitAsync(db, manager, invoiceId, termsRequest);
      Assert.True(first.Succeeded, first.ErrorCode);
      termsRevisionId = first.Value;
      var retry = await InvoicePaymentTermsService.SubmitAsync(db, manager, invoiceId, termsRequest);
      Assert.Equal(termsRevisionId, retry.Value);

      var selfReview = await InvoicePaymentTermsService.ReviewAsync(db, adminReviewer, termsRevisionId,
        new ReviewInvoicePaymentTermsRequest(true, "Self review is prohibited.", true));
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.GateBlocked, selfReview.ErrorCode);
      Assert.True((await InvoicePaymentTermsService.ReviewAsync(db, reviewer, termsRevisionId,
        new ReviewInvoicePaymentTermsRequest(true, "Verified against the synthetic engagement letter.", true))).Succeeded);

      // A later reviewed terms change must not rewrite what was known at the as-of date.
      db.InvoicePaymentTermsRevisions.Add(new InvoicePaymentTermsRevision
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, InvoiceId = invoiceId, Revision = 2,
        DueDate = asOf.AddDays(20), Basis = InvoicePaymentTermsKinds.ReviewedTermsSnapshot,
        TermsDescription = "Future reviewed extension", EvidenceReference = "SYN-AMENDMENT-R2",
        Status = InvoicePaymentTermsStates.Approved, SubmittedByUserId = seed.Admin.Id,
        SubmittedAt = now.AddDays(5), ReviewedByUserId = seed.Reviewer.Id,
        ReviewedAt = now.AddDays(5), ReviewReason = "Approved after the report date."
      });

      var receivedBefore = now.AddDays(-1);
      var oldReceipt = new Receipt
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, BillingAccountId = accountId, Amount = 30m,
        Currency = "QAR", Reference = "SYN-AGING-RECEIPT-OLD", Status = BillingStates.ReceiptRecorded,
        RecordedByUserId = seed.Admin.Id, ReceivedAt = receivedBefore
      };
      var futureReceipt = new Receipt
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, BillingAccountId = accountId, Amount = 40m,
        Currency = "QAR", Reference = "SYN-AGING-RECEIPT-FUTURE", Status = BillingStates.ReceiptRecorded,
        RecordedByUserId = seed.Admin.Id, ReceivedAt = now.AddDays(5)
      };
      db.Receipts.AddRange(oldReceipt, futureReceipt);
      var oldAllocation = new ReceiptAllocation { Id = Guid.NewGuid(), FirmId = seed.FirmId, ReceiptId = oldReceipt.Id,
        InvoiceId = invoiceId, Amount = 20m, CreatedAt = receivedBefore };
      oldAllocationId = oldAllocation.Id;
      db.ReceiptAllocations.AddRange(oldAllocation,
        new ReceiptAllocation { Id = Guid.NewGuid(), FirmId = seed.FirmId, ReceiptId = futureReceipt.Id,
          InvoiceId = invoiceId, Amount = 40m, CreatedAt = now.AddDays(5) });
      db.CreditNotes.AddRange(
        new CreditNote { Id = Guid.NewGuid(), FirmId = seed.FirmId, BillingAccountId = accountId,
          InvoiceId = invoiceId, NoteNumber = "SYN-AGING-CREDIT-OLD", Currency = "QAR", Amount = 10m,
          Reason = "Synthetic historical credit", Status = BillingStates.CreditIssued,
          CreatedByUserId = seed.Admin.Id, CreatedAt = receivedBefore },
        new CreditNote { Id = Guid.NewGuid(), FirmId = seed.FirmId, BillingAccountId = accountId,
          InvoiceId = invoiceId, NoteNumber = "SYN-AGING-CREDIT-FUTURE", Currency = "QAR", Amount = 5m,
          Reason = "Synthetic future credit", Status = BillingStates.CreditIssued,
          CreatedByUserId = seed.Admin.Id, CreatedAt = now.AddDays(5) });
      await db.SaveChangesAsync();

      var reversalRequest = new RequestReceiptAllocationReversal(5m, "SYN-BANK-CORRECTION-R1",
        "Unapply the incorrectly applied portion.", 0, true);
      var reversal = await ReceiptAllocationReversalService.SubmitAsync(db, manager, oldAllocationId, reversalRequest);
      Assert.True(reversal.Succeeded, reversal.ErrorCode);
      Assert.Equal(reversal.Value, (await ReceiptAllocationReversalService.SubmitAsync(db, manager, oldAllocationId, reversalRequest)).Value);
      var selfReversalReview = await ReceiptAllocationReversalService.ReviewAsync(db, adminReviewer, reversal.Value,
        new ReviewReceiptAllocationReversal(true, "Self review is prohibited.", true));
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.GateBlocked, selfReversalReview.ErrorCode);
      Assert.True((await ReceiptAllocationReversalService.ReviewAsync(db, reviewer, reversal.Value,
        new ReviewReceiptAllocationReversal(true, "Verified the allocation and bank correction evidence.", true))).Succeeded);
      Assert.Equal(55m, await BillingService.GetNetAppliedToInvoiceAsync(db, seed.FirmId, invoiceId));
      Assert.Equal(15m, await BillingService.GetNetAppliedToReceiptAsync(db, seed.FirmId, oldReceipt.Id));
      var excessiveReversal = await ReceiptAllocationReversalService.SubmitAsync(db, manager, oldAllocationId,
        new RequestReceiptAllocationReversal(16m, "SYN-OVER-REVERSAL", "Must exceed the unapplied amount.", 1, true));
      Assert.Equal("billing.reversal-over-allocation", excessiveReversal.ErrorCode);

      db.ReceiptAllocationReversals.Add(new ReceiptAllocationReversal
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ReceiptAllocationId = oldAllocationId,
        Revision = 2, Amount = 3m, Reference = "SYN-FUTURE-CORRECTION-R2", Reason = "Approved after the as-of date.",
        Status = ReceiptAllocationReversalStates.Approved, SubmittedByUserId = seed.Admin.Id,
        SubmittedAt = now.AddDays(4), ReviewedByUserId = seed.Reviewer.Id,
        ReviewedAt = now.AddDays(5), ReviewReason = "Future approval must not alter history."
      });
      await db.SaveChangesAsync();

      var report = await FirmReceivablesAgingQuery.GetAsync(db, manager, asOf);
      Assert.True(report.Succeeded, report.ErrorCode);
      var row = Assert.Single(report.Value!.Rows);
      Assert.Equal(invoiceId, row.InvoiceId);
      Assert.DoesNotContain(report.Value.Rows, x => x.InvoiceNumber == "SYN-AGING-UNLINKED");
      Assert.Equal("PBC TEST CLIENT", row.LegalClientName);
      Assert.Null(row.EngagementId);
      Assert.Equal(asOf.AddDays(-31), row.DueDate);
      Assert.Equal(100m, row.OriginalAmount);
      Assert.Equal(20m, row.AppliedReceipts);
      Assert.Equal(5m, row.ReversedReceipts);
      Assert.Equal(10m, row.AppliedCredits);
      Assert.Equal(75m, row.Outstanding);
      Assert.Equal(31, row.DaysOverdue);
      Assert.Equal(FirmReceivablesAgingPolicy.Overdue31To60, row.Bucket);
      Assert.Equal("QAR", row.Currency);
      Assert.Equal("APPROVED", row.PaymentTermsStatus);
      Assert.Contains("Synthetic Finance Contact <finance@example.test>", row.FinanceRecipients);
      Assert.Equal(5m, Assert.Single(report.Value.CurrencySubtotals).ReversedReceipts);
      Assert.Equal(75m, Assert.Single(report.Value.CurrencySubtotals).Outstanding);
      Assert.Equal(75m, Assert.Single(report.Value.ClientCurrencySubtotals).Outstanding);
      var export = await FirmReceivablesAgingQuery.ExportAsync(db, manager, asOf);
      Assert.True(export.Succeeded, export.ErrorCode);
      Assert.Contains("firm-receivables-aging-", export.Value!.FileName);
      Assert.Contains("75", export.Value.Csv);
      Assert.Contains("CLIENT CURRENCY TOTAL", export.Value.Csv);
      Assert.Contains("CURRENCY TOTAL", export.Value.Csv);

      var scopedActor = PbcSeed.Actor(seed.Staff, "FinanceManager");
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Staff, "FinanceManager", clientId: seed.ClientId));
      await db.SaveChangesAsync();
      var scopedReport = await FirmReceivablesAgingQuery.GetAsync(db, scopedActor, asOf);
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied, scopedReport.ErrorCode);
    }
  }
}
