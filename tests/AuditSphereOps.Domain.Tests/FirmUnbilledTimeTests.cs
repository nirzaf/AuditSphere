using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class FirmUnbilledTimeTests
{
  private sealed record Fixture(PbcSeed.Fixture Base, ActorContext Actor, Guid TaskId,
    Guid QarRateCardId, Guid UsdRateCardId);

  [Fact]
  public async Task StandardValuesNormalizeEachEntryAndKeepCurrencyAndFeeBasisSubtotalsSeparate()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var date = new DateOnly(2026, 4, 15);
    var oneMinute = Enumerable.Range(0, 3)
      .Select(index => Time(fixture, fixture.QarRateCardId, index, date, 200m, "QAR", 1)).ToArray();
    var zero = Time(fixture, fixture.QarRateCardId, 4, date, 0m, "QAR", 60);
    var usd = Time(fixture, fixture.UsdRateCardId, 5, date, 200m, "USD", 60);
    var unattributed = Time(fixture, fixture.QarRateCardId, 7, date, 200m, "QAR", 30);
    unattributed.ClientId = null;
    unattributed.EngagementId = null;
    var invalidCurrency = Time(fixture, fixture.QarRateCardId, 8, date, 200m, "qar", 60);
    var nonBillable = Time(fixture, fixture.QarRateCardId, 6, date, 200m, "QAR", 60,
      billable: PracticeTimeStates.NonBillable);
    var draft = Time(fixture, fixture.QarRateCardId, 9, date, 200m, "QAR", 60,
      status: PracticeTimeStates.TimeDraft);
    var submitted = Time(fixture, fixture.QarRateCardId, 10, date, 200m, "QAR", 60,
      status: PracticeTimeStates.TimeSubmitted);
    var superseded = Time(fixture, fixture.QarRateCardId, 11, date, 200m, "QAR", 60,
      status: PracticeTimeStates.TimeSuperseded);
    var noCharge = Time(fixture, fixture.QarRateCardId, 12, date, 200m, "QAR", 60,
      billable: PracticeTimeStates.NoCharge);
    db.TimeEntries.AddRange(oneMinute.Concat([zero, usd, unattributed, invalidCurrency,
      nonBillable, draft, submitted, superseded, noCharge]));
    await db.SaveChangesAsync();

    var result = await FirmUnbilledTimeQuery.GetAsync(db, fixture.Actor,
      new FirmUnbilledTimeRequest(date, date));

    Assert.True(result.Succeeded, result.Message);
    Assert.Equal("7", result.Value!.TotalEntries);
    Assert.All(result.Value.Rows.Where(x => oneMinute.Any(e => e.Id == x.TimeEntryId)), row =>
    {
      Assert.Equal(FirmUnbilledTimeBuckets.Unallocated, row.Bucket);
      Assert.Equal(3.333333m, row.StandardValue!.Value);
      Assert.Equal(FirmUnbilledTimeFeeBases.FixedFeeReference, row.FeeBasis);
    });
    Assert.Equal(0m, Assert.Single(result.Value.Rows, x => x.TimeEntryId == zero.Id).StandardValue!.Value);
    Assert.Equal(200m, Assert.Single(result.Value.Rows, x => x.TimeEntryId == usd.Id).StandardValue!.Value);
    var missingClientRow = Assert.Single(result.Value.Rows, x => x.TimeEntryId == unattributed.Id);
    Assert.Equal(FirmUnbilledTimeBuckets.ReviewRequired, missingClientRow.Bucket);
    Assert.Equal("Client attribution is unavailable.", missingClientRow.Reason);
    Assert.Equal(FirmUnbilledTimeFeeBases.TimeSourceReference, missingClientRow.FeeBasis);
    var invalidCurrencyRow = Assert.Single(result.Value.Rows, x => x.TimeEntryId == invalidCurrency.Id);
    Assert.Null(invalidCurrencyRow.StandardValue);
    Assert.Equal("Captured rate or currency is unavailable.", invalidCurrencyRow.Reason);
    var invalidCurrencySubtotal = Assert.Single(result.Value.Subtotals,
      x => x.Currency == "qar" && x.Bucket == FirmUnbilledTimeBuckets.ReviewRequired);
    Assert.Null(invalidCurrencySubtotal.StandardValue);
    Assert.Equal("1", invalidCurrencySubtotal.UnavailableValueCount);
    Assert.DoesNotContain(result.Value.Rows, x => x.TimeEntryId == nonBillable.Id);
    Assert.DoesNotContain(result.Value.Rows, x => x.TimeEntryId == draft.Id);
    Assert.DoesNotContain(result.Value.Rows, x => x.TimeEntryId == submitted.Id);
    Assert.DoesNotContain(result.Value.Rows, x => x.TimeEntryId == superseded.Id);
    Assert.DoesNotContain(result.Value.Rows, x => x.TimeEntryId == noCharge.Id);
    Assert.Equal(9.999999m, Assert.Single(result.Value.Subtotals,
      x => x.Currency == "QAR" && x.FeeBasis == FirmUnbilledTimeFeeBases.FixedFeeReference).StandardValue!.Value);
    Assert.Equal(200m, Assert.Single(result.Value.Subtotals,
      x => x.Currency == "USD").StandardValue!.Value);
    Assert.Contains("not recognized WIP", result.Value.BasisNotice);
  }

  [Fact]
  public async Task BillingSourceStatusAndCorrectionHistoryAreClassifiedWithoutReleasingReservations()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var date = new DateOnly(2026, 4, 16);
    var draft = Time(fixture, fixture.QarRateCardId, 0, date, 200m, "QAR", 60);
    var posted = Time(fixture, fixture.QarRateCardId, 1, date, 200m, "QAR", 60);
    var cancelled = Time(fixture, fixture.QarRateCardId, 2, date, 200m, "QAR", 60);
    var mismatchedRevision = Time(fixture, fixture.QarRateCardId, 5, date, 200m, "QAR", 60);
    var mismatchedAmount = Time(fixture, fixture.QarRateCardId, 6, date, 200m, "QAR", 60);
    var predecessor = Time(fixture, fixture.QarRateCardId, 3, date, 200m, "QAR", 60,
      status: PracticeTimeStates.TimeSuperseded);
    var correction = Time(fixture, fixture.QarRateCardId, 4, date, 200m, "QAR", 75,
      revision: 2, supersedesId: predecessor.Id);
    db.TimeEntries.AddRange(draft, posted, cancelled, predecessor, correction, mismatchedRevision, mismatchedAmount);
    var account = new BillingAccount
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, PracticeClientId = fixture.Base.ClientId,
      Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
    };
    db.BillingAccounts.Add(account);
    AddReservation(db, fixture, account, draft, BillingStates.InvoiceDraft, "WIP-DRAFT");
    AddReservation(db, fixture, account, posted, BillingStates.InvoicePosted, "WIP-POSTED");
    AddReservation(db, fixture, account, cancelled, BillingStates.InvoiceCancelled, "WIP-CANCELLED");
    AddReservation(db, fixture, account, predecessor, BillingStates.InvoicePosted, "WIP-OLD-TIME");
    AddReservation(db, fixture, account, mismatchedRevision, BillingStates.InvoiceDraft, "WIP-REVISION", 2);
    AddReservation(db, fixture, account, mismatchedAmount, BillingStates.InvoiceDraft, "WIP-AMOUNT", linkedRate: 150m);
    await db.SaveChangesAsync();
    var postedInvoice = await db.Invoices.SingleAsync(x => x.InvoiceNumber == "WIP-POSTED");
    var receipt = new Receipt
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, BillingAccountId = account.Id,
      Amount = 10m, Currency = "QAR", Reference = "SYNTHETIC-WIP-RECEIPT",
      Status = BillingStates.ReceiptRecorded, RecordedByUserId = fixture.Base.Admin.Id,
      ReceivedAt = DateTimeOffset.UtcNow
    };
    db.Receipts.Add(receipt);
    db.ReceiptAllocations.Add(new ReceiptAllocation
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, ReceiptId = receipt.Id,
      InvoiceId = postedInvoice.Id, Amount = 10m, CreatedAt = DateTimeOffset.UtcNow
    });
    db.CreditNotes.Add(new CreditNote
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, BillingAccountId = account.Id,
      InvoiceId = postedInvoice.Id, NoteNumber = "SYNTHETIC-WIP-CREDIT", Currency = "QAR",
      Amount = 1m, Reason = "Synthetic credit does not release the source", Status = BillingStates.CreditIssued,
      CreatedByUserId = fixture.Base.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var result = await FirmUnbilledTimeQuery.GetAsync(db, fixture.Actor,
      new FirmUnbilledTimeRequest(date, date));

    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(FirmUnbilledTimeBuckets.Reserved, Assert.Single(result.Value!.Rows, x => x.TimeEntryId == draft.Id).Bucket);
    Assert.Equal(FirmUnbilledTimeBuckets.Invoiced, Assert.Single(result.Value.Rows, x => x.TimeEntryId == posted.Id).Bucket);
    var cancelledRow = Assert.Single(result.Value.Rows, x => x.TimeEntryId == cancelled.Id);
    Assert.Equal(FirmUnbilledTimeBuckets.ReviewRequired, cancelledRow.Bucket);
    Assert.Equal("A cancelled invoice still retains this source reservation.", cancelledRow.Reason);
    var correctedRow = Assert.Single(result.Value.Rows, x => x.TimeEntryId == correction.Id);
    Assert.Equal(FirmUnbilledTimeBuckets.ReviewRequired, correctedRow.Bucket);
    Assert.Equal("Corrected time requires review of its prior billing links.", correctedRow.Reason);
    Assert.Null(correctedRow.InvoiceId);
    var mismatchedRow = Assert.Single(result.Value.Rows, x => x.TimeEntryId == mismatchedRevision.Id);
    Assert.Equal(FirmUnbilledTimeBuckets.ReviewRequired, mismatchedRow.Bucket);
    Assert.Equal("Billing-source attribution requires finance review.", mismatchedRow.Reason);
    Assert.Equal("WIP-REVISION", mismatchedRow.InvoiceNumber);
    var mismatchedAmountRow = Assert.Single(result.Value.Rows, x => x.TimeEntryId == mismatchedAmount.Id);
    Assert.Equal(FirmUnbilledTimeBuckets.ReviewRequired, mismatchedAmountRow.Bucket);
    Assert.Equal("WIP-AMOUNT", mismatchedAmountRow.InvoiceNumber);
    Assert.Equal(BillingStates.InvoicePosted, await db.Invoices.Where(x => x.InvoiceNumber == "WIP-OLD-TIME")
      .Select(x => x.Status).SingleAsync());
    Assert.Equal(6, await db.BillingSourceAllocations.CountAsync());
    Assert.Single(await db.ReceiptAllocations.Where(x => x.InvoiceId == postedInvoice.Id).ToListAsync());
    Assert.Single(await db.CreditNotes.Where(x => x.InvoiceId == postedInvoice.Id).ToListAsync());
  }

  [Fact]
  public async Task CrossClientBillingParentFailsTheWholeReportAndClientScopedFinanceIsDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var date = new DateOnly(2026, 4, 17);
    var entry = Time(fixture, fixture.QarRateCardId, 0, date, 200m, "QAR", 60);
    db.TimeEntries.Add(entry);
    var foreignClient = new PracticeClient
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, LegalName = "Separate billing client",
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.PracticeClients.Add(foreignClient);
    var foreignAccount = new BillingAccount
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, PracticeClientId = foreignClient.Id,
      Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
    };
    db.BillingAccounts.Add(foreignAccount);
    AddReservation(db, fixture, foreignAccount, entry, BillingStates.InvoiceDraft, "WIP-FOREIGN-CLIENT");
    await db.SaveChangesAsync();

    var unsafeGraph = await FirmUnbilledTimeQuery.GetAsync(db, fixture.Actor,
      new FirmUnbilledTimeRequest(date, date));
    Assert.False(unsafeGraph.Succeeded);
    Assert.Equal("wip.source-integrity", unsafeGraph.ErrorCode);
    Assert.Null(unsafeGraph.Value);

    var scopedFinance = PbcSeed.User(fixture.Base.FirmId, "Staff");
    db.Users.Add(scopedFinance);
    db.RoleGrants.Add(PbcSeed.Grant(fixture.Base.FirmId, scopedFinance, "FinanceManager",
      fixture.Base.ClientId, fixture.Base.EngagementId));
    await db.SaveChangesAsync();
    var scopedActor = new ActorContext(scopedFinance.Id, scopedFinance.FirmId,
      scopedFinance.SessionEpoch, ["FinanceManager"]);
    var denied = await FirmUnbilledTimeQuery.GetAsync(db, scopedActor,
      new FirmUnbilledTimeRequest(date, date, ClientId: fixture.Base.ClientId));
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
  }

  [Fact]
  public async Task MoreThanTenThousandApprovedEntriesReturnsTheExactLimitFailureWithoutPartialTotals()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var firstDate = new DateOnly(2026, 1, 1);
    var rows = Enumerable.Range(0, 10_001).Select(index => new TimeEntry
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, ClientId = fixture.Base.ClientId,
      EngagementId = fixture.Base.EngagementId, TaskId = fixture.TaskId, UserId = fixture.Base.Staff.Id,
      WorkDate = firstDate.AddDays(index % 365), StartMinute = (index / 365) * 2, DurationMinutes = 1,
      Role = "Staff", Activity = "Synthetic WIP", BillableClassification = PracticeTimeStates.Billable,
      Narrative = "Synthetic bounded-limit fixture", NarrativeVisibility = PracticeTimeStates.NarrativeInternal,
      Status = PracticeTimeStates.TimeApproved, Currency = "QAR", Revision = 1,
      RateCardVersionId = fixture.QarRateCardId, RatePerHour = 200m,
      SubmittedAt = DateTimeOffset.UtcNow, ApprovedByUserId = fixture.Base.Admin.Id,
      ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    }).ToArray();
    db.TimeEntries.AddRange(rows);
    await db.SaveChangesAsync();

    var result = await FirmUnbilledTimeQuery.GetAsync(db, fixture.Actor,
      new FirmUnbilledTimeRequest(firstDate, firstDate.AddDays(364)));

    Assert.False(result.Succeeded);
    Assert.Equal("wip.report-limit", result.ErrorCode);
    Assert.Equal("More than 10,000 approved time entries match. Narrow the work-date or client filter.", result.Message);
    Assert.Null(result.Value);
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var baseFixture = await PbcSeed.SeedAsync(pg);
    var finance = PbcSeed.User(baseFixture.FirmId, "Staff");
    var taskId = Guid.NewGuid();
    var qarCard = RateCard(baseFixture.FirmId, baseFixture.Admin.Id, baseFixture.Reviewer.Id, "QAR", 200m);
    var usdCard = RateCard(baseFixture.FirmId, baseFixture.Admin.Id, baseFixture.Reviewer.Id, "USD", 200m);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(finance);
    db.RoleGrants.Add(PbcSeed.Grant(baseFixture.FirmId, finance, "FinanceManager"));
    db.WorkTasks.Add(new WorkTask
    {
      Id = taskId, FirmId = baseFixture.FirmId, ClientId = baseFixture.ClientId,
      EngagementId = baseFixture.EngagementId, Title = "Approved time query fixture", CreatedAt = DateTimeOffset.UtcNow
    });
    db.RateCardVersions.AddRange(qarCard, usdCard);
    AddFixedFeeAgreement(db, baseFixture);
    await db.SaveChangesAsync();
    return new Fixture(baseFixture,
      new ActorContext(finance.Id, finance.FirmId, finance.SessionEpoch, ["FinanceManager"]),
      taskId, qarCard.Id, usdCard.Id);
  }

  private static RateCardVersion RateCard(Guid firmId, Guid createdBy, Guid approvedBy, string currency, decimal rate)
  {
    var now = DateTimeOffset.UtcNow;
    return new RateCardVersion
    {
      Id = Guid.NewGuid(), FirmId = firmId, Role = "Staff", Activity = "Synthetic WIP", Currency = currency,
      RatePerHour = rate, Status = PracticeTimeStates.RateApproved, CreatedByUserId = createdBy,
      ApprovedByUserId = approvedBy, ApprovedAt = now, CreatedAt = now
    };
  }

  private static void AddFixedFeeAgreement(AuditSphereDbContext db, PbcSeed.Fixture fixture)
  {
    var now = DateTimeOffset.UtcNow;
    var lead = new Lead
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, Name = "Synthetic fee-basis lead",
      Source = "Referral", CreatedAt = now
    };
    var opportunity = new Opportunity
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, LeadId = lead.Id, PracticeClientId = fixture.ClientId,
      ServiceRoute = "AccountingOnly", EntityScope = "SYNTHETIC", PeriodStart = "2026-01-01",
      PeriodEnd = "2026-12-31", ExpectedFee = 200m, Currency = "QAR", CreatedAt = now
    };
    var proposal = new Proposal
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, OpportunityId = opportunity.Id,
      PracticeClientId = fixture.ClientId, Status = CrmStates.ProposalAccepted,
      ServiceProfileId = "SYNTHETIC", Scope = "Synthetic test scope", Deliverables = "Synthetic report",
      Fee = 200m, Currency = "QAR", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      CreatedAt = now
    };
    var quotation = new QuotationVersion
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ProposalId = proposal.Id, Currency = "QAR",
      BaseAmount = 200m, Fee = 200m, InputHash = new string('a', 64),
      CreatedByUserId = fixture.Admin.Id, CreatedAt = now
    };
    db.Leads.Add(lead);
    db.Opportunities.Add(opportunity);
    db.Proposals.Add(proposal);
    db.QuotationVersions.Add(quotation);
    db.EngagementFeeAgreements.Add(new EngagementFeeAgreement
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ProposalId = proposal.Id,
      QuotationVersionId = quotation.Id, PracticeClientId = fixture.ClientId,
      EngagementId = fixture.EngagementId, Currency = "QAR", AgreedFee = 200m,
      AdvancePercent = 50m, CreatedByUserId = fixture.Admin.Id, CreatedAt = now
    });
  }

  private static TimeEntry Time(Fixture fixture, Guid rateCardId, int index, DateOnly date, decimal rate,
    string? currency, int minutes, string billable = PracticeTimeStates.Billable,
    string status = PracticeTimeStates.TimeApproved, long revision = 1, Guid? supersedesId = null)
  {
    var isUsd = rateCardId == fixture.UsdRateCardId;
    return new TimeEntry
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, ClientId = fixture.Base.ClientId,
      EngagementId = fixture.Base.EngagementId, TaskId = fixture.TaskId, UserId = fixture.Base.Staff.Id,
      WorkDate = date, StartMinute = index * 90, DurationMinutes = minutes,
      Role = "Staff", Activity = "Synthetic WIP", BillableClassification = billable,
      Narrative = "Synthetic test row", NarrativeVisibility = PracticeTimeStates.NarrativeInternal,
      Status = status, Currency = currency ?? (isUsd ? "USD" : "QAR"), Revision = revision,
      RateCardVersionId = rateCardId, RatePerHour = rate, SupersedesId = supersedesId,
      SubmittedAt = DateTimeOffset.UtcNow, ApprovedByUserId = fixture.Base.Admin.Id,
      ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    };
  }

  private static void AddReservation(AuditSphereDbContext db, Fixture fixture, BillingAccount account,
    TimeEntry source, string status, string invoiceNumber, long? linkedRevision = null,
    decimal? linkedRate = null)
  {
    var now = DateTimeOffset.UtcNow;
    var rate = linkedRate ?? source.RatePerHour!.Value;
    var amount = source.DurationMinutes * rate / 60m;
    var invoice = new Invoice
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, BillingAccountId = account.Id,
      InvoiceNumber = invoiceNumber, Currency = "QAR", Subtotal = amount, Total = amount,
      Status = status, CreatedByUserId = fixture.Base.Admin.Id,
      CancelledAt = status == BillingStates.InvoiceCancelled ? now : null,
      CancellationReason = status == BillingStates.InvoiceCancelled ? "Retain immutable source reservation" : null,
      CreatedAt = now
    };
    var line = new InvoiceLine
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, InvoiceId = invoice.Id,
      Description = "Approved time source", Quantity = source.DurationMinutes / 60m,
      UnitPrice = rate, LineTotal = amount,
      SourceKind = "TIME", SourceId = source.Id, SourceRevision = linkedRevision ?? source.Revision
    };
    db.Invoices.Add(invoice);
    db.InvoiceLines.Add(line);
    db.BillingSourceAllocations.Add(new BillingSourceAllocation
    {
      Id = Guid.NewGuid(), FirmId = fixture.Base.FirmId, InvoiceLineId = line.Id,
      SourceKind = "TIME", SourceId = source.Id, SourceRevision = linkedRevision ?? source.Revision,
      Quantity = source.DurationMinutes / 60m, Amount = amount, CreatedAt = now
    });
  }
}
