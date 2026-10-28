using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class FirmReceivablesAgingQueryTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ConcurrentAllocationAndReversal_UseOneSnapshotAndRecheckCurrentAuthority(bool revoke)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.Actor(seed.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(seed.Reviewer, "FinanceReviewer");
    Guid invoiceId, receiptId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.RoleGrants.AddRange(PbcSeed.Grant(seed.FirmId, seed.Admin, "FinanceManager"),
        PbcSeed.Grant(seed.FirmId, seed.Reviewer, "FinanceReviewer"));
      setup.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true, ApprovedByUserId = seed.Reviewer.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await setup.SaveChangesAsync();
      var account = await BillingService.CreateBillingAccountAsync(setup, manager, new(seed.ClientId, "QAR"));
      Assert.True(account.Succeeded, account.Message);
      var invoice = await BillingService.CreateInvoiceDraftAsync(setup, manager,
        new(account.Value, "SNAPSHOT-100", [new("Synthetic services", 1m, 100m)]));
      Assert.True(invoice.Succeeded, invoice.Message);
      invoiceId = invoice.Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(setup, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(setup, reviewer, invoiceId)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(setup, manager, invoiceId)).Succeeded);
      var receipt = await BillingService.RecordReceiptAsync(setup, manager,
        new(account.Value, 50m, "SYN-SNAPSHOT-BANK", RequestId: Guid.CreateVersion7()));
      Assert.True(receipt.Succeeded, receipt.Message);
      receiptId = receipt.Value;
    }
    var barrier = new AllocationReadBarrier(async () =>
    {
      await using var writer = new AuditSphereDbContext(pg.Options);
      var allocated = await BillingService.AllocateReceiptCommandAsync(writer, manager,
        new(receiptId, invoiceId, 50m, Guid.CreateVersion7()));
      Assert.True(allocated.Succeeded, allocated.Message);
      var requested = await ReceiptAllocationReversalService.SubmitAsync(writer, manager, allocated.Value!.AllocationId,
        new(50m, "SYN-SNAPSHOT-CORRECTION", "Independent allocation correction.", 0, true));
      Assert.True(requested.Succeeded, requested.Message);
      var approved = await ReceiptAllocationReversalService.ReviewAsync(writer, reviewer, requested.Value,
        new(true, "Verified the exact correction.", true));
      Assert.True(approved.Succeeded, approved.Message);
      if (revoke)
      {
        var grant = await writer.RoleGrants.AsNoTracking().SingleAsync(x => x.FirmId == seed.FirmId &&
          x.UserId == seed.Admin.Id && x.Role == "FinanceManager" && x.RevokedAt == null);
        var removed = await RoleAdministrationService.RevokeRoleGrantAsync(writer,
          PbcSeed.Actor(seed.Admin, "Administrator"), new(grant.Id));
        Assert.True(removed.Succeeded, removed.Message);
      }
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(pg.ConnectionString)
      .AddInterceptors(barrier).Options;
    var asOf = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
    await using var reader = new AuditSphereDbContext(options);
    var report = await FirmReceivablesAgingQuery.GetAsync(reader, manager, asOf);
    Assert.True(barrier.Invoked, report.ErrorCode + ": " + report.Message);
    Assert.Null(reader.Database.CurrentTransaction);
    if (revoke)
    {
      Assert.False(report.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, report.ErrorCode);
      return;
    }
    Assert.True(report.Succeeded, report.Message);
    var row = Assert.Single(report.Value!.Rows);
    Assert.Equal(100m, row.Outstanding);
    Assert.Equal(0m, row.AppliedReceipts);
    Assert.Equal(0m, row.ReversedReceipts);
    Assert.Equal(100m, Assert.Single(report.Value.CurrencySubtotals).Outstanding);
    var subsequent = await FirmReceivablesAgingQuery.GetAsync(reader, manager, asOf);
    Assert.True(subsequent.Succeeded, subsequent.Message);
    var current = Assert.Single(subsequent.Value!.Rows);
    Assert.Equal(50m, current.AppliedReceipts);
    Assert.Equal(50m, current.ReversedReceipts);
    Assert.Equal(100m, current.Outstanding);
    Assert.True((await FirmReceivablesAgingQuery.ExportAsync(reader, manager, asOf)).Succeeded);
    await using var ambient = await reader.Database.BeginTransactionAsync();
    var refused = await FirmReceivablesAgingQuery.GetAsync(reader, manager, asOf);
    Assert.False(refused.Succeeded);
    Assert.Same(ambient, reader.Database.CurrentTransaction);
    await ambient.RollbackAsync();
  }

  private sealed class AllocationReadBarrier(Func<Task> write) : DbCommandInterceptor
  {
    private int invoked;
    public bool Invoked => Volatile.Read(ref invoked) != 0;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("FROM allocations AS", StringComparison.Ordinal) &&
          !command.CommandText.Contains("receipt_allocation_reversals", StringComparison.Ordinal) &&
          Interlocked.CompareExchange(ref invoked, 1, 0) == 0) await write();
      return result;
    }
  }

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
    Guid invoiceId, accountId, agreementId;
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
      var quotationId = Guid.NewGuid();
      var offerHash = Hashing.Sha256Hex("synthetic dispatched receivables offer");
      var proposal = new Proposal
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, OpportunityId = opportunity.Id, PracticeClientId = seed.ClientId,
        Status = CrmStates.ProposalAccepted, Currency = "QAR", Fee = 200m,
        ServiceProfileId = "AUDIT-2026", Scope = "Synthetic audit scope", Deliverables = "Synthetic audit report",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = now, SentAt = now, ResponseAt = now,
        SentOfferSha256 = offerHash, ResponseOfferSha256 = offerHash
      };
      db.Leads.Add(lead);
      db.Opportunities.Add(opportunity);
      db.Proposals.Add(proposal);
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = quotationId, FirmId = seed.FirmId, ProposalId = proposal.Id, Revision = 1, Currency = "QAR",
        BaseAmount = 200m, Fee = 200m, InputHash = Hashing.Sha256Hex("synthetic ageing quotation"),
        Status = QuotationStates.Approved, CreatedByUserId = seed.Admin.Id, ApprovedAt = now, CreatedAt = now
      });
      await db.SaveChangesAsync();

      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, PbcSeed.Actor(seed.Admin, "Partner"), proposal.Id);
      var renderedOffer = await db.CommercialDocuments.AsNoTracking()
        .SingleAsync(x => x.FirmId == seed.FirmId && x.ProposalId == proposal.Id && x.Kind == CommercialDocumentKinds.Quotation);
      const string recipient = "owner@receivables.example.test";
      var dispatchId = Guid.NewGuid();
      proposal.SentOfferSha256 = renderedOffer.Sha256Hex;
      proposal.ResponseOfferSha256 = renderedOffer.Sha256Hex;
      proposal.RespondentName = "Synthetic client owner";
      proposal.RespondentEmail = recipient;
      proposal.ResponseEvidenceReference = "SYN-CLIENT-ACCEPTANCE";
      proposal.ResponseChannel = ProposalResponseChannels.Email;
      db.CommercialNotifications.Add(new CommercialNotification
      {
        Id = dispatchId, FirmId = seed.FirmId, Kind = CommercialNotificationKinds.Proposal,
        ProposalId = proposal.Id, PracticeClientId = seed.ClientId,
        QuotationVersionId = quotationId, DocumentId = renderedOffer.Id, OfferSha256 = renderedOffer.Sha256Hex,
        RecipientAuthorityBasis = ProposalRecipientAuthorityBases.LegacyDispatchSnapshot,
        Recipient = recipient, Subject = "Synthetic dispatched offer", Body = "Synthetic test evidence", CreatedAt = now
      });
      db.ProposalResponseRecords.Add(new ProposalResponseRecord
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ProposalId = proposal.Id, CommercialNotificationId = dispatchId,
        Decision = CrmStates.ProposalAccepted, OfferSha256 = renderedOffer.Sha256Hex,
        DispatchRecipient = recipient, AuthorityBasis = ProposalRecipientAuthorityBases.LegacyDispatchSnapshot,
        RespondentName = "Synthetic client owner", RespondentEmail = recipient,
        EvidenceReference = "SYN-CLIENT-ACCEPTANCE", Channel = ProposalResponseChannels.Email,
        RecordedByUserId = seed.Admin.Id, RecordedAt = now
      });
      await db.SaveChangesAsync();

      var agreement = await FeeAgreementService.CreateAgreementAsync(db, PbcSeed.Actor(seed.Admin, "Partner"), proposal.Id);
      Assert.True(agreement.Succeeded, agreement.Message);
      agreementId = agreement.Value;
      Assert.True((await FeeAgreementService.LinkEngagementAsync(db, PbcSeed.Actor(seed.Admin, "Partner"),
        agreementId, seed.EngagementId)).Succeeded);
      var advanceInvoice = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, manager, agreement.Value);
      Assert.True(advanceInvoice.Succeeded, advanceInvoice.Message);
      invoiceId = advanceInvoice.Value;
      accountId = (await db.BillingAccounts.Where(x => x.FirmId == seed.FirmId && x.PracticeClientId == seed.ClientId)
        .Select(x => x.Id).ToArrayAsync()).Single();
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewer, invoiceId)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, invoiceId)).Succeeded);

      // This query fixture models an already-posted final milestone, without invoking the service that
      // correctly refuses balance invoicing until the advance is paid and a release exists.
      var balanceMilestone = await db.FeeMilestones.SingleAsync(x => x.FirmId == seed.FirmId &&
        x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Balance);
      var finalInvoiceId = Guid.NewGuid();
      balanceMilestone.InvoiceId = finalInvoiceId;
      balanceMilestone.State = FeeMilestoneStates.Invoiced;
      db.Invoices.Add(new Invoice
      {
        Id = finalInvoiceId, FirmId = seed.FirmId, BillingAccountId = accountId,
        InvoiceNumber = "SYN-AGING-FINAL", Currency = "QAR", Subtotal = 60m, Total = 60m,
        Status = BillingStates.InvoicePosted, CreatedByUserId = seed.Admin.Id, PostedAt = now, CreatedAt = now
      });
      db.InvoiceLines.Add(new InvoiceLine
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, InvoiceId = finalInvoiceId,
        Description = "Synthetic final fee milestone", Quantity = 1m, UnitPrice = 60m, LineTotal = 60m,
        SourceKind = FeeAgreementService.MilestoneSourceKind, SourceId = balanceMilestone.Id, SourceRevision = 1
      });

      var timeTaskId = Guid.NewGuid();
      var rateCardId = Guid.NewGuid();
      var timeEntryId = Guid.NewGuid();
      db.WorkTasks.Add(new WorkTask
      {
        Id = timeTaskId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        Title = "Synthetic approved time invoice", CreatedAt = now
      });
      db.RateCardVersions.Add(new RateCardVersion
      {
        Id = rateCardId, FirmId = seed.FirmId, Version = 1, Role = "Staff", Activity = "Audit", Currency = "QAR",
        RatePerHour = 100m, Status = PracticeTimeStates.RateApproved, CreatedByUserId = seed.Admin.Id,
        ApprovedByUserId = seed.Reviewer.Id, ApprovedAt = now, CreatedAt = now
      });
      db.TimeEntries.Add(new TimeEntry
      {
        Id = timeEntryId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        TaskId = timeTaskId, UserId = seed.Staff.Id, WorkDate = DateOnly.FromDateTime(now.UtcDateTime),
        StartMinute = 540, DurationMinutes = 60, Role = "Staff", Activity = "Audit",
        Narrative = "Synthetic approved time", Status = PracticeTimeStates.TimeApproved, Currency = "QAR",
        Revision = 1, RateCardVersionId = rateCardId, RatePerHour = 100m,
        ApprovedByUserId = seed.Reviewer.Id, ApprovedAt = now, CreatedAt = now
      });
      await db.SaveChangesAsync();
      var timeInvoice = await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-AGING-TIME", [
          new InvoiceLineRequest("Approved audit time", 1m, 100m, "TIME", timeEntryId, 1)
        ]));
      Assert.True(timeInvoice.Succeeded, timeInvoice.Message);
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, timeInvoice.Value)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewer, timeInvoice.Value)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, timeInvoice.Value)).Succeeded);

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
      // ADR-0018: all posted firm invoices are typed as ADVANCE, FINAL, TIME or OTHER.
      Assert.Equal(4, report.Value!.Rows.Count);
      var row = report.Value.Rows.Single(x => x.InvoiceId == invoiceId);
      var other = report.Value.Rows.Single(x => x.InvoiceNumber == "SYN-AGING-UNLINKED");
      var final = report.Value.Rows.Single(x => x.InvoiceNumber == "SYN-AGING-FINAL");
      var time = report.Value.Rows.Single(x => x.InvoiceNumber == "SYN-AGING-TIME");
      Assert.Equal("ADVANCE", row.InvoiceType);
      Assert.Equal(("OTHER", 500m, 500m), (other.InvoiceType, other.OriginalAmount, other.Outstanding));
      Assert.Null(other.EngagementId);
      Assert.Null(other.DueDate);
      Assert.Equal(("FINAL", 60m, 60m, seed.EngagementId),
        (final.InvoiceType, final.OriginalAmount, final.Outstanding, final.EngagementId));
      Assert.Equal(("TIME", 100m, 100m, seed.EngagementId),
        (time.InvoiceType, time.OriginalAmount, time.Outstanding, time.EngagementId));
      Assert.Equal("PBC TEST CLIENT", row.LegalClientName);
      Assert.Equal(seed.EngagementId, row.EngagementId);
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
      Assert.Equal(735m, Assert.Single(report.Value.CurrencySubtotals).Outstanding);
      Assert.Equal(735m, Assert.Single(report.Value.ClientCurrencySubtotals).Outstanding);
      Assert.Equal((3, 235m), (Assert.Single(report.Value.EngagementCurrencySubtotals).InvoiceCount,
        Assert.Single(report.Value.EngagementCurrencySubtotals).Outstanding));
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
