using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class ClientProfileWorkspaceTests
{
  private sealed class ArchiveClock(DateTimeOffset now) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => now;
  }

  [Fact]
  public async Task OffboardingReadiness_ReadsEachSectionUnderItsOwnAuthority_ShowsUnreadableEvidenceAsUnknown_AndChangesNothing()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var relationshipManager = PbcSeed.User(f.FirmId, "Staff");
    var siblingManager = PbcSeed.User(f.FirmId, "Staff");
    var siblingClientId = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient { Id = siblingClientId, FirmId = f.FirmId, LegalName = "Sibling Client", CreatedAt = DateTimeOffset.UtcNow });
    db.Users.AddRange(manager, relationshipManager, siblingManager);
    db.RoleGrants.AddRange(
      PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId),
      PbcSeed.Grant(f.FirmId, relationshipManager, "RelationshipManager", f.ClientId),
      PbcSeed.Grant(f.FirmId, siblingManager, "Manager", siblingClientId),
      PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
      PbcSeed.Grant(f.FirmId, f.Admin, "RecordsCustodian"));
    var accountId = Guid.NewGuid(); var archiveId = Guid.NewGuid();
    db.BillingAccounts.Add(new BillingAccount { Id = accountId, FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "USD", CreatedAt = DateTimeOffset.UtcNow });
    db.Invoices.AddRange(
      new Invoice { Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = accountId, InvoiceNumber = "INV-OFF-1", Currency = "USD",
        Subtotal = 100.25m, Total = 100.25m, Status = BillingStates.InvoicePosted, PostedAt = DateTimeOffset.UtcNow.AddDays(-2), CreatedAt = DateTimeOffset.UtcNow.AddDays(-3) },
      new Invoice { Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = accountId, InvoiceNumber = "INV-OFF-2", Currency = "USD",
        Subtotal = 10m, Total = 10m, Status = BillingStates.InvoiceDraft, CreatedAt = DateTimeOffset.UtcNow });
    db.WorkTasks.Add(new WorkTask { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, Title = "Open", CreatedAt = DateTimeOffset.UtcNow });
    db.Archives.Add(new AuditSphereOps.Domain.Completion.Archive { Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ProfileId = "profile", CreatedAt = DateTimeOffset.UtcNow });
    db.LegalHolds.Add(new AuditSphereOps.Domain.Records.LegalHold { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ArchiveId = archiveId,
      HoldReference = "HOLD-1", RequestedByUserId = f.Admin.Id, RequestedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var grantsBefore = await db.RoleGrants.CountAsync(); var invoicesBefore = await db.Invoices.CountAsync();

    static ClientOffboardingItem Item(ClientOffboardingReadiness r, string key) => r.Sections.SelectMany(s => s.Items).Single(i => i.Key == key);

    // Full authority: every section is read and counted, and nothing is claimed to be complete.
    var full = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);
    Assert.True(full.Succeeded, full.Message);
    Assert.Equal(1, Item(full.Value!, "OPEN_ENGAGEMENTS").Count);
    Assert.Equal(1, Item(full.Value!, "OPEN_TASKS").Count);
    var owed = Item(full.Value!, "OUTSTANDING_INVOICES");
    Assert.Equal(1, owed.Count); Assert.Equal("USD 100.25", owed.Detail);
    Assert.Equal(1, Item(full.Value!, "UNPOSTED_INVOICES").Count);
    Assert.Equal(5, Item(full.Value!, "ACTIVE_GRANTS").Count); // staff, reviewer, client, manager, relationshipManager
    Assert.Equal(1, Item(full.Value!, "UNVERIFIED_ARCHIVES").Count);
    Assert.Equal(1, Item(full.Value!, "UNRELEASED_LEGAL_HOLDS").Count);
    Assert.Equal(ClientOffboardingStates.Unknown, Item(full.Value!, "EXTERNAL_ACCESS").State);
    Assert.Null(Item(full.Value!, "EXTERNAL_ACCESS").Count);
    Assert.Equal(ClientOffboardingStates.ActionRequired, full.Value!.OverallState);

    // A client Manager reads work, but finance, access and records are UNKNOWN rather than zero or clear.
    var partial = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, PbcSeed.Actor(manager, "Manager"), f.ClientId);
    Assert.True(partial.Succeeded, partial.Message);
    Assert.Equal(1, Item(partial.Value!, "OPEN_ENGAGEMENTS").Count);
    foreach (var key in new[] { "OUTSTANDING_INVOICES", "UNPOSTED_INVOICES", "UNAPPLIED_RECEIPTS",
        "APPROVED_UNBILLED_TIME", "ACTIVE_GRANTS", "UNVERIFIED_ARCHIVES", "UNRELEASED_LEGAL_HOLDS" })
    {
      Assert.Equal(ClientOffboardingStates.Unknown, Item(partial.Value!, key).State);
      Assert.Null(Item(partial.Value!, key).Count);
    }
    Assert.Equal(ClientOffboardingStates.Unknown, Item(partial.Value!, "FEE_MILESTONES").State);
    Assert.Null(Item(partial.Value!, "FEE_MILESTONES").Count);
    Assert.All(partial.Value!.Sections.Where(s => s.State == ClientOffboardingStates.Unknown), s => Assert.Null(s.OwnerRoute));

    // Commercial authority alone grants no operational visibility: everything is UNKNOWN, never NO_FINDINGS.
    var commercial = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, PbcSeed.Actor(relationshipManager, "RelationshipManager"), f.ClientId);
    Assert.True(commercial.Succeeded, commercial.Message);
    Assert.All(commercial.Value!.Sections.SelectMany(s => s.Items), i => { Assert.Equal(ClientOffboardingStates.Unknown, i.State); Assert.Null(i.Count); });
    Assert.Equal(ClientOffboardingStates.Unknown, commercial.Value.OverallState);

    // Client users, sibling-client managers and engagement-scoped staff cannot open the assessment.
    foreach (var denied in new[] { PbcSeed.Actor(f.Client, "ClientUser"), PbcSeed.Actor(siblingManager, "Manager"), PbcSeed.Actor(f.Staff, "Staff") })
      Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeCrmService.GetClientOffboardingReadinessAsync(db, denied, f.ClientId)).ErrorCode);

    // Read-only: no archive event, grant or invoice was written.
    Assert.Equal(0, await db.ClientLifecycleEvents.CountAsync());
    Assert.Equal(grantsBefore, await db.RoleGrants.CountAsync());
    Assert.Equal(invoicesBefore, await db.Invoices.CountAsync());
  }

  [Fact]
  public async Task OffboardingReadinessAddsCashTimeAndFeeObservationsWithoutChangingFinancialSources()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var now = DateTimeOffset.UtcNow;
    var siblingClientId = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient
      { Id = siblingClientId, FirmId = f.FirmId, LegalName = "Synthetic sibling", CreatedAt = now });
    var manager = PbcSeed.User(f.FirmId, "Staff");
    db.Users.Add(manager);
    db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId),
      PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
    var targetAccount = new BillingAccount
      { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "USD", CreatedAt = now };
    var siblingAccount = new BillingAccount
      { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = siblingClientId, Currency = "QAR", CreatedAt = now };
    db.BillingAccounts.AddRange(targetAccount, siblingAccount);

    var paidInvoice = new Invoice
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = targetAccount.Id,
      InvoiceNumber = "SYN-OFF-PAID", Currency = "USD", Subtotal = 100.25m, Total = 100.25m,
      Status = BillingStates.InvoicePosted, PostedAt = now, CreatedAt = now.AddMinutes(-1)
    };
    db.Invoices.Add(paidInvoice);
    var fullReceipt = new Receipt
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = targetAccount.Id,
      Amount = 100.25m, Currency = "USD", Reference = "SYN-OFF-FULL-PAYMENT",
      Status = BillingStates.ReceiptRecorded, RecordedByUserId = f.Admin.Id, ReceivedAt = now
    };
    var unappliedReceipt = new Receipt
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = targetAccount.Id,
      Amount = 30m, Currency = "USD", Reference = "SYN-OFF-UNAPPLIED",
      Status = BillingStates.ReceiptRecorded, RecordedByUserId = f.Admin.Id, ReceivedAt = now.AddSeconds(1)
    };
    db.Receipts.AddRange(fullReceipt, unappliedReceipt);
    db.ReceiptAllocations.Add(new ReceiptAllocation
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptId = fullReceipt.Id,
      InvoiceId = paidInvoice.Id, Amount = fullReceipt.Amount, CreatedAt = now
    });

    var task = new WorkTask
      { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        Title = "Synthetic approved-time work", CreatedAt = now };
    db.WorkTasks.Add(task);
    var rateCard = new RateCardVersion
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, Role = "Staff", Activity = "Offboarding observation",
      Currency = "USD", RatePerHour = 120m, Status = PracticeTimeStates.RateApproved,
      CreatedByUserId = f.Admin.Id, ApprovedByUserId = f.Reviewer.Id,
      ApprovedAt = now, CreatedAt = now
    };
    db.RateCardVersions.Add(rateCard);
    var workDate = DateOnly.FromDateTime(now.UtcDateTime);
    var unallocatedTime = ApprovedTime(f, task, rateCard, workDate, 0);
    var reservedTime = ApprovedTime(f, task, rateCard, workDate, 1);
    var invoicedTime = ApprovedTime(f, task, rateCard, workDate, 2);
    var predecessor = ApprovedTime(f, task, rateCard, workDate, 3,
      status: PracticeTimeStates.TimeSuperseded);
    var correction = ApprovedTime(f, task, rateCard, workDate, 4,
      revision: 2, supersedesId: predecessor.Id);
    db.TimeEntries.AddRange(unallocatedTime, reservedTime, invoicedTime, predecessor, correction);
    AddTimeInvoiceSource(db, f, targetAccount, reservedTime, "SYN-OFF-TIME-DRAFT", BillingStates.InvoiceDraft, now);
    AddTimeInvoiceSource(db, f, targetAccount, invoicedTime, "SYN-OFF-TIME-POSTED", BillingStates.InvoicePosted, now);

    var plannedFees = AddFeeAgreement(db, f, f.ClientId, f.EngagementId, "SYN-OFF-FEE-PLANNED", 100m, now);
    var invoicedFees = AddFeeAgreement(db, f, f.ClientId, null, "SYN-OFF-FEE-INVOICED", 100m, now);
    var reversedFees = AddFeeAgreement(db, f, f.ClientId, null, "SYN-OFF-FEE-REVERSED", 100m, now);
    AddFeeInvoiceSource(db, f, targetAccount, invoicedFees.Advance, "SYN-OFF-FEE-INVOICE", now);
    var reversedInvoice = AddFeeInvoiceSource(db, f, targetAccount, reversedFees.Advance,
      "SYN-OFF-FEE-REVERSED-INVOICE", now);
    var reversedReceipt = new Receipt
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = targetAccount.Id,
      Amount = 50m, Currency = "USD", Reference = "SYN-OFF-FEE-RECEIPT",
      Status = BillingStates.ReceiptRecorded, RecordedByUserId = f.Admin.Id, ReceivedAt = now.AddSeconds(2)
    };
    db.Receipts.Add(reversedReceipt);
    var reversedAllocation = new ReceiptAllocation
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptId = reversedReceipt.Id,
      InvoiceId = reversedInvoice.Id, Amount = 50m, CreatedAt = now
    };
    db.ReceiptAllocations.Add(reversedAllocation);
    reversedFees.Advance.State = FeeMilestoneStates.Paid;
    reversedFees.Advance.ReceiptId = reversedReceipt.Id;
    reversedFees.Advance.PaidAt = now;
    db.ReceiptAllocationReversals.Add(new ReceiptAllocationReversal
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptAllocationId = reversedAllocation.Id,
      Revision = 1, Amount = 10m, Reference = "SYN-OFF-FEE-REVERSAL",
      Reason = "Synthetic approved correction", Status = ReceiptAllocationReversalStates.Approved,
      SubmittedByUserId = f.Admin.Id, SubmittedAt = now, ReviewedByUserId = f.Reviewer.Id,
      ReviewedAt = now.AddSeconds(1), ReviewReason = "Approved synthetic correction"
    });

    var siblingInvoice = new Invoice
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = siblingAccount.Id,
      InvoiceNumber = "SYN-OFF-SIBLING", Currency = "QAR", Subtotal = 120m, Total = 120m,
      Status = BillingStates.InvoicePosted, PostedAt = now, CreatedAt = now
    };
    db.Invoices.Add(siblingInvoice);
    var pendingReceipt = ReceiptFor(f, siblingAccount, "SYN-OFF-PENDING", 50m, now);
    var approvedReceipt = ReceiptFor(f, siblingAccount, "SYN-OFF-APPROVED", 20m, now.AddSeconds(1));
    var siblingFullyAllocated = ReceiptFor(f, siblingAccount, "SYN-OFF-SIBLING-FULL", 70m, now.AddSeconds(2));
    db.Receipts.AddRange(pendingReceipt, approvedReceipt, siblingFullyAllocated);
    var pendingAllocation = AllocationFor(f, pendingReceipt, siblingInvoice, 20m, now);
    var approvedAllocation = AllocationFor(f, approvedReceipt, siblingInvoice, 20m, now);
    var siblingFullAllocation = AllocationFor(f, siblingFullyAllocated, siblingInvoice, 70m, now);
    db.ReceiptAllocations.AddRange(pendingAllocation, approvedAllocation, siblingFullAllocation);
    db.ReceiptAllocationReversals.AddRange(
      new ReceiptAllocationReversal
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptAllocationId = pendingAllocation.Id,
        Revision = 1, Amount = 10m, Reference = "SYN-OFF-PENDING-REVERSAL",
        Reason = "Synthetic pending review", Status = ReceiptAllocationReversalStates.PendingReview,
        SubmittedByUserId = f.Admin.Id, SubmittedAt = now
      },
      new ReceiptAllocationReversal
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptAllocationId = approvedAllocation.Id,
        Revision = 1, Amount = 5m, Reference = "SYN-OFF-APPROVED-REVERSAL",
        Reason = "Synthetic approved correction", Status = ReceiptAllocationReversalStates.Approved,
        SubmittedByUserId = f.Admin.Id, SubmittedAt = now, ReviewedByUserId = f.Reviewer.Id,
        ReviewedAt = now.AddSeconds(1), ReviewReason = "Approved synthetic correction"
      });
    await db.SaveChangesAsync();

    var sourceCounts = (await db.Invoices.CountAsync(), await db.Receipts.CountAsync(),
      await db.ReceiptAllocations.CountAsync(), await db.ReceiptAllocationReversals.CountAsync(),
      await db.TimeEntries.CountAsync(), await db.FeeMilestones.CountAsync(),
      await db.BillingSourceAllocations.CountAsync());
    static ClientOffboardingItem Item(ClientOffboardingReadiness readiness, string key) =>
      readiness.Sections.SelectMany(section => section.Items).Single(item => item.Key == key);

    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    var result = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, admin, f.ClientId);
    Assert.True(result.Succeeded, result.Message);
    var receipts = Item(result.Value!, "UNAPPLIED_RECEIPTS");
    Assert.Equal(ClientOffboardingStates.ActionRequired, receipts.State);
    Assert.Equal(2L, receipts.Count);
    Assert.Equal("USD 2 receipt(s), 40 unapplied", receipts.Detail);
    var time = Item(result.Value!, "APPROVED_UNBILLED_TIME");
    Assert.Equal(ClientOffboardingStates.ActionRequired, time.State);
    Assert.Equal(3L, time.Count);
    Assert.Contains("UNALLOCATED/FIXED_FEE_REFERENCE 1", time.Detail!);
    Assert.Contains("RESERVED/FIXED_FEE_REFERENCE 1", time.Detail!);
    Assert.Contains("REVIEW_REQUIRED/FIXED_FEE_REFERENCE 1", time.Detail!);
    Assert.Contains("INVOICED 1 (excluded from unbilled handover count)", time.Detail!);
    var fee = Item(result.Value!, "FEE_MILESTONES");
    Assert.Equal(ClientOffboardingStates.ActionRequired, fee.State);
    Assert.Equal(6L, fee.Count);
    Assert.Contains("PLANNED 4 (200; not posted debt)", fee.Detail!);
    Assert.Contains("INVOICED 2 (60 owner-calculated outstanding; do not add to invoice ageing)", fee.Detail!);
    Assert.Equal(FeeMilestoneStates.Paid, await db.FeeMilestones.Where(row => row.Id == reversedFees.Advance.Id)
      .Select(row => row.State).SingleAsync());

    var restricted = await PracticeCrmService.GetClientOffboardingReadinessAsync(db,
      PbcSeed.Actor(manager, "Manager"), f.ClientId);
    Assert.True(restricted.Succeeded, restricted.Message);
    foreach (var key in new[] { "UNAPPLIED_RECEIPTS", "APPROVED_UNBILLED_TIME", "FEE_MILESTONES" })
    {
      var item = Item(restricted.Value!, key);
      Assert.Equal(ClientOffboardingStates.Unknown, item.State);
      Assert.Null(item.Count);
      Assert.Null(restricted.Value!.Sections.Single(section => section.Items.Contains(item)).OwnerRoute);
      Assert.DoesNotContain("40 unapplied", item.Detail ?? string.Empty);
    }

    var sibling = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, admin, siblingClientId);
    Assert.True(sibling.Succeeded, sibling.Message);
    Assert.Equal("QAR 2 receipt(s), 35 unapplied", Item(sibling.Value!, "UNAPPLIED_RECEIPTS").Detail);
    Assert.DoesNotContain("QAR", receipts.Detail);

    Assert.Equal(sourceCounts, (await db.Invoices.CountAsync(), await db.Receipts.CountAsync(),
      await db.ReceiptAllocations.CountAsync(), await db.ReceiptAllocationReversals.CountAsync(),
      await db.TimeEntries.CountAsync(), await db.FeeMilestones.CountAsync(),
      await db.BillingSourceAllocations.CountAsync()));
    Assert.Equal(100.25m, await db.Invoices.Where(invoice => invoice.Id == paidInvoice.Id)
      .Select(invoice => invoice.Total).SingleAsync());
    Assert.Equal(FeeMilestoneStates.Paid, await db.FeeMilestones.Where(row => row.Id == reversedFees.Advance.Id)
      .Select(row => row.State).SingleAsync());
  }

  [Fact]
  public async Task OffboardingReadinessAllowsClientScopedFinanceForFeeMilestonesOnly()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceReviewer", f.ClientId));
    var account = new BillingAccount
      { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "USD", CreatedAt = now };
    db.BillingAccounts.Add(account);
    var fees = AddFeeAgreement(db, f, f.ClientId, null, "SYN-OFF-SCOPED-FEE-READ", 100m, now);
    var retainedInvoice = AddFeeInvoiceSource(db, f, account, fees.Advance,
      "SYN-OFF-SCOPED-RETAINED-ADVANCE", now);
    fees.Advance.InvoiceId = null;
    fees.Advance.State = FeeMilestoneStates.Planned;
    await db.SaveChangesAsync();

    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(
      db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);

    Assert.True(readiness.Succeeded, readiness.Message);
    var finance = readiness.Value!.Sections.Single(section => section.Key == "FINANCE");
    Assert.Equal(ClientOffboardingStates.Unknown, finance.State);
    Assert.Null(finance.OwnerRoute);
    Assert.All(finance.Items, item =>
    {
      Assert.Equal(ClientOffboardingStates.Unknown, item.State);
      Assert.Null(item.Count);
      Assert.DoesNotContain("USD", item.Detail ?? string.Empty, StringComparison.Ordinal);
    });
    var feeSection = readiness.Value.Sections.Single(section => section.Key == "FEE_MILESTONES");
    var fee = Assert.Single(feeSection.Items, item => item.Key == "FEE_MILESTONES");
    Assert.Equal(ClientOffboardingStates.ActionRequired, fee.State);
    Assert.Equal(2L, fee.Count);
    Assert.Equal("/app/finance/fee-agreements", feeSection.OwnerRoute);
    Assert.Contains("PLANNED 2", fee.Detail!);
    var reconciliation = Assert.Single(feeSection.Items, item => item.Key == "FEE_RECONCILIATION");
    Assert.Contains(retainedInvoice.InvoiceNumber, reconciliation.Detail!, StringComparison.Ordinal);
    Assert.Contains(BillingStates.InvoicePosted, reconciliation.Detail!, StringComparison.Ordinal);
  }

  [Fact]
  public async Task OffboardingReadinessScopesAgeingBeforeLimitAndRetainsReceiptAndTimeInBlockedReview()
  {
    const int invoiceLimit = 10_000;
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var now = DateTimeOffset.UtcNow;
    var siblingClientId = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient
      { Id = siblingClientId, FirmId = f.FirmId, LegalName = "Synthetic sibling", CreatedAt = now });
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
    var targetAccount = new BillingAccount
      { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "USD", CreatedAt = now };
    var siblingAccount = new BillingAccount
      { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = siblingClientId, Currency = "QAR", CreatedAt = now };
    db.BillingAccounts.AddRange(targetAccount, siblingAccount);
    db.Invoices.AddRange(Enumerable.Range(0, invoiceLimit + 1).Select(index => new Invoice
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = siblingAccount.Id,
      InvoiceNumber = $"SYN-OFF-B-{index:D5}", Currency = "QAR", Subtotal = 1m, Total = 1m,
      Status = BillingStates.InvoicePosted, PostedAt = now, CreatedAt = now
    }));
    var targetReceipt = ReceiptFor(f, targetAccount, "SYN-OFF-SCOPED-LIMIT-RECEIPT", 30m, now);
    db.Receipts.Add(targetReceipt);
    var task = new WorkTask
      { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        Title = "Synthetic scoped-limit time", CreatedAt = now };
    db.WorkTasks.Add(task);
    var rateCard = new RateCardVersion
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, Role = "Staff", Activity = "Offboarding limit observation",
      Currency = "USD", RatePerHour = 120m, Status = PracticeTimeStates.RateApproved,
      CreatedByUserId = f.Admin.Id, ApprovedByUserId = f.Reviewer.Id, ApprovedAt = now, CreatedAt = now
    };
    db.RateCardVersions.Add(rateCard);
    var approvedTime = ApprovedTime(f, task, rateCard, DateOnly.FromDateTime(now.UtcDateTime), 0);
    db.TimeEntries.Add(approvedTime);
    await db.SaveChangesAsync();

    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var firmWideAging = await FirmReceivablesAgingQuery.GetAsync(db, actor,
      DateOnly.FromDateTime(now.UtcDateTime), CancellationToken.None);
    Assert.False(firmWideAging.Succeeded);
    Assert.Equal("finance.report-limit", firmWideAging.ErrorCode);

    static ClientOffboardingItem Item(ClientOffboardingReadiness readiness, string key) =>
      readiness.Sections.SelectMany(section => section.Items).Single(item => item.Key == key);

    var siblingLoadReadiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
    Assert.True(siblingLoadReadiness.Succeeded, siblingLoadReadiness.Message);
    Assert.Equal(ClientOffboardingStates.NoFindings, Item(siblingLoadReadiness.Value!, "OUTSTANDING_INVOICES").State);
    Assert.Equal(0L, Item(siblingLoadReadiness.Value!, "OUTSTANDING_INVOICES").Count);
    Assert.Equal(1L, Item(siblingLoadReadiness.Value!, "UNAPPLIED_RECEIPTS").Count);
    Assert.Equal(1L, Item(siblingLoadReadiness.Value!, "APPROVED_UNBILLED_TIME").Count);

    db.Invoices.AddRange(Enumerable.Range(0, invoiceLimit + 1).Select(index => new Invoice
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = targetAccount.Id,
      InvoiceNumber = $"SYN-OFF-A-{index:D5}", Currency = "USD", Subtotal = 1m, Total = 1m,
      Status = BillingStates.InvoicePosted, PostedAt = now, CreatedAt = now
    }));
    await db.SaveChangesAsync();

    var ownLimitReadiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
    Assert.True(ownLimitReadiness.Succeeded, ownLimitReadiness.Message);
    var ageing = Item(ownLimitReadiness.Value!, "OUTSTANDING_INVOICES");
    Assert.Equal(ClientOffboardingStates.Unknown, ageing.State);
    Assert.Null(ageing.Count);
    Assert.Contains("10,000-invoice interactive limit", ageing.Detail!, StringComparison.Ordinal);
    Assert.Equal(0L, Item(ownLimitReadiness.Value!, "UNPOSTED_INVOICES").Count);
    Assert.Equal(1L, Item(ownLimitReadiness.Value!, "UNAPPLIED_RECEIPTS").Count);
    Assert.Equal(1L, Item(ownLimitReadiness.Value!, "APPROVED_UNBILLED_TIME").Count);

    var request = new ClientOffboardingCloseoutRequest(f.ClientId,
      "Reviewed limited ageing coverage with the independent receipt and time facts.", [], 0,
      ExpectedReadinessDigest: ownLimitReadiness.Value!.ReadinessDigest,
      RequestKey: "88888888-8888-4888-8888-888888888888");
    var recorded = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, actor, request);
    Assert.True(recorded.Succeeded, recorded.Message);
    Assert.Equal(ClientOffboardingRecordStates.BlockedDecision, recorded.Value!.Status);
    Assert.False(recorded.Value!.IsArchived);
    Assert.Equal(ownLimitReadiness.Value!.ReadinessDigest, recorded.Value!.ReadinessDigest);

    var retained = await db.ClientOffboardingRecords.AsNoTracking()
      .SingleAsync(row => row.Id == recorded.Value!.RecordId);
    var snapshot = JsonSerializer.Deserialize<ClientOffboardingReadiness>(retained.ReadinessSnapshotJson,
      new JsonSerializerOptions(JsonSerializerDefaults.Web));
    Assert.NotNull(snapshot);
    Assert.Equal(ClientOffboardingStates.Unknown, Item(snapshot!, "OUTSTANDING_INVOICES").State);
    Assert.Equal(1L, Item(snapshot!, "UNAPPLIED_RECEIPTS").Count);
    Assert.Equal(1L, Item(snapshot!, "APPROVED_UNBILLED_TIME").Count);

    db.Receipts.Add(ReceiptFor(f, targetAccount, "SYN-OFF-SCOPED-LIMIT-LATER-RECEIPT", 1m, now.AddSeconds(1)));
    await db.SaveChangesAsync();
    var replay = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, actor, request);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(recorded.Value!.RecordId, replay.Value!.RecordId);
    Assert.Equal(recorded.Value!.ReadinessDigest, replay.Value!.ReadinessDigest);
  }

  [Fact]
  public async Task OffboardingFinanceReadIsUnknownWhenFinanceGrantIsRevokedDuringComposition()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    var financeGrant = PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager");
    var account = new BillingAccount
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
      Currency = "USD", CreatedAt = now
    };
    var receipt = new Receipt
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = account.Id,
      Amount = 123.45m, Currency = "USD", Reference = "SYN-OFF-REVOKED-DURING-READ",
      Status = BillingStates.ReceiptRecorded, RecordedByUserId = f.Admin.Id, ReceivedAt = now
    };
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.RoleGrants.Add(financeGrant);
      seed.BillingAccounts.Add(account);
      seed.Receipts.Add(receipt);
      await seed.SaveChangesAsync();
    }

    var interceptor = new RevokeFinanceBeforeReceiptRead(async () =>
    {
      await using var writer = new AuditSphereDbContext(pg.Options);
      await writer.RoleGrants.Where(grant => grant.Id == financeGrant.Id)
        .ExecuteUpdateAsync(update => update.SetProperty(grant => grant.RevokedAt, DateTimeOffset.UtcNow));
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(interceptor).Options;
    await using var db = new AuditSphereDbContext(options);
    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(
      db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);

    Assert.True(readiness.Succeeded, readiness.Message);
    Assert.True(interceptor.Fired);
    var finance = readiness.Value!.Sections.Single(section => section.Key == "FINANCE");
    foreach (var key in new[] { "OUTSTANDING_INVOICES", "UNPOSTED_INVOICES", "UNAPPLIED_RECEIPTS", "APPROVED_UNBILLED_TIME" })
    {
      var item = finance.Items.Single(candidate => candidate.Key == key);
      Assert.Equal(ClientOffboardingStates.Unknown, item.State);
      Assert.Null(item.Count);
      Assert.DoesNotContain("123.45", item.Detail ?? string.Empty);
    }
    Assert.Null(finance.OwnerRoute);
  }

  [Fact]
  public async Task OffboardingFinanceAndFeeSectionsAreScrubbedWhenGrantIsRevokedAfterComposition()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    var financeGrant = PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager");
    var account = new BillingAccount
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
      Currency = "USD", CreatedAt = now
    };
    var receipt = new Receipt
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = account.Id,
      Amount = 123.45m, Currency = "USD", Reference = "SYN-OFF-LATE-REVOKE",
      Status = BillingStates.ReceiptRecorded, RecordedByUserId = f.Admin.Id, ReceivedAt = now
    };
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.RoleGrants.Add(financeGrant);
      seed.BillingAccounts.Add(account);
      seed.Receipts.Add(receipt);
      AddFeeAgreement(seed, f, f.ClientId, null, "SYN-OFF-LATE-REVOKE", 100m, now);
      await seed.SaveChangesAsync();
    }

    var interceptor = new RevokeFinanceBeforeArchiveRead(async () =>
    {
      await using var writer = new AuditSphereDbContext(pg.Options);
      await writer.RoleGrants.Where(grant => grant.Id == financeGrant.Id)
        .ExecuteUpdateAsync(update => update.SetProperty(grant => grant.RevokedAt, DateTimeOffset.UtcNow));
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(interceptor).Options;
    await using var db = new AuditSphereDbContext(options);
    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(
      db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);

    Assert.True(readiness.Succeeded, readiness.Message);
    Assert.True(interceptor.Fired);
    var finance = readiness.Value!.Sections.Single(section => section.Key == "FINANCE");
    foreach (var key in new[] { "OUTSTANDING_INVOICES", "UNPOSTED_INVOICES", "UNAPPLIED_RECEIPTS", "APPROVED_UNBILLED_TIME" })
    {
      var item = finance.Items.Single(candidate => candidate.Key == key);
      Assert.Equal(ClientOffboardingStates.Unknown, item.State);
      Assert.Null(item.Count);
      Assert.DoesNotContain("123.45", item.Detail ?? string.Empty);
    }
    Assert.Null(finance.OwnerRoute);
    var feeSection = readiness.Value.Sections.Single(section => section.Key == "FEE_MILESTONES");
    var fee = Assert.Single(feeSection.Items);
    Assert.Equal(ClientOffboardingStates.Unknown, fee.State);
    Assert.Null(fee.Count);
    Assert.Null(feeSection.OwnerRoute);
    Assert.DoesNotContain("PLANNED", fee.Detail ?? string.Empty);
  }

  [Fact]
  public async Task OffboardingReadinessMarksFeeSourceMismatchUnavailableWithoutChangingIt()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var now = DateTimeOffset.UtcNow;
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
    var account = new BillingAccount
      { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "USD", CreatedAt = now };
    db.BillingAccounts.Add(account);
    var fees = AddFeeAgreement(db, f, f.ClientId, null, "SYN-OFF-FEE-MISMATCH", 100m, now);
    var wrongSourceId = Guid.NewGuid();
    var invoice = AddFeeInvoiceSource(db, f, account, fees.Advance, "SYN-OFF-FEE-MISMATCH-INVOICE", now,
      sourceIdOverride: wrongSourceId);
    await db.SaveChangesAsync();
    var countsBefore = (await db.Invoices.CountAsync(), await db.InvoiceLines.CountAsync(),
      await db.BillingSourceAllocations.CountAsync(), await db.FeeMilestones.CountAsync());

    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db,
      PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);
    Assert.True(readiness.Succeeded, readiness.Message);
    var feeSection = Assert.Single(readiness.Value!.Sections, section => section.Key == "FEE_MILESTONES");
    var feeItem = Assert.Single(feeSection.Items);
    Assert.Equal(ClientOffboardingStates.Unknown, feeItem.State);
    Assert.Null(feeItem.Count);
    Assert.Contains("reconciliation", feeItem.Detail!, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(FeeMilestoneStates.Invoiced, await db.FeeMilestones.Where(row => row.Id == fees.Advance.Id)
      .Select(row => row.State).SingleAsync());
    Assert.Equal(invoice.Id, await db.FeeMilestones.Where(row => row.Id == fees.Advance.Id)
      .Select(row => row.InvoiceId).SingleAsync());
    Assert.Equal(countsBefore, (await db.Invoices.CountAsync(), await db.InvoiceLines.CountAsync(),
      await db.BillingSourceAllocations.CountAsync(), await db.FeeMilestones.CountAsync()));
  }

  [Fact]
  public async Task OffboardingReadinessPreservesASixDecimalOutstandingBalanceInTheDigest()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var now = DateTimeOffset.UtcNow;
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
    var account = new BillingAccount
      { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "USD", CreatedAt = now };
    var usdInvoice = new Invoice
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = account.Id, InvoiceNumber = "SYN-OFF-SIX-DECIMAL",
      Currency = "USD", Subtotal = 10m, Total = 10m, Status = BillingStates.InvoicePosted,
      PostedAt = now, CreatedAt = now
    };
    var qarInvoice = new Invoice
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = account.Id, InvoiceNumber = "SYN-OFF-MILLI-QAR",
      Currency = "QAR", Subtotal = 0.001m, Total = 0.001m, Status = BillingStates.InvoicePosted,
      PostedAt = now, CreatedAt = now
    };
    var credit = new CreditNote
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = account.Id, InvoiceId = usdInvoice.Id,
      NoteNumber = "SYN-OFF-SIX-DECIMAL-CREDIT", Currency = "USD", Amount = 0.000001m,
      Reason = "Synthetic exact six-decimal credit", Status = BillingStates.CreditIssued,
      CreatedByUserId = f.Admin.Id, CreatedAt = now
    };
    db.BillingAccounts.Add(account);
    db.Invoices.AddRange(usdInvoice, qarInvoice);
    await db.SaveChangesAsync();

    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var beforeCredit = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
    Assert.True(beforeCredit.Succeeded, beforeCredit.Message);
    Assert.Equal("QAR 0.001; USD 10", beforeCredit.Value!.Sections.SelectMany(section => section.Items)
      .Single(item => item.Key == "OUTSTANDING_INVOICES").Detail);

    db.CreditNotes.Add(credit);
    await db.SaveChangesAsync();
    var priorCulture = CultureInfo.CurrentCulture;
    var priorUiCulture = CultureInfo.CurrentUICulture;
    ClientOffboardingReadiness french;
    ClientOffboardingReadiness english;
    try
    {
      CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
      CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
      var frenchResult = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
      Assert.True(frenchResult.Succeeded, frenchResult.Message);
      french = frenchResult.Value!;

      CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
      CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
      var englishResult = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
      Assert.True(englishResult.Succeeded, englishResult.Message);
      english = englishResult.Value!;
    }
    finally
    {
      CultureInfo.CurrentCulture = priorCulture;
      CultureInfo.CurrentUICulture = priorUiCulture;
    }

    var outstanding = english.Sections.SelectMany(section => section.Items)
      .Single(item => item.Key == "OUTSTANDING_INVOICES");
    Assert.Equal(ClientOffboardingStates.ActionRequired, outstanding.State);
    Assert.Equal(2L, outstanding.Count);
    Assert.Equal("QAR 0.001; USD 9.999999", outstanding.Detail);
    Assert.Equal(outstanding.Detail, french.Sections.SelectMany(section => section.Items)
      .Single(item => item.Key == "OUTSTANDING_INVOICES").Detail);
    Assert.Equal(english.ReadinessDigest, french.ReadinessDigest);
    Assert.NotEqual(beforeCredit.Value.ReadinessDigest, english.ReadinessDigest);
    Assert.Matches("^[0-9a-f]{64}$", english.ReadinessDigest);

    var request = new ClientOffboardingCloseoutRequest(f.ClientId,
      "Reviewed exact per-currency balances.", [], 0,
      ExpectedReadinessDigest: english.ReadinessDigest,
      RequestKey: "99999999-9999-4999-8999-999999999999");
    var stale = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, actor,
      request with { ExpectedReadinessDigest = beforeCredit.Value.ReadinessDigest });
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);

    var recorded = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, actor, request);
    Assert.True(recorded.Succeeded, recorded.Message);
    Assert.Equal(english.ReadinessDigest, recorded.Value!.ReadinessDigest);

    db.Invoices.Add(new Invoice
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = account.Id,
      InvoiceNumber = "SYN-OFF-MILLI-QAR-AFTER-REVIEW", Currency = "QAR", Subtotal = 0.002m, Total = 0.002m,
      Status = BillingStates.InvoicePosted, PostedAt = now.AddSeconds(1), CreatedAt = now.AddSeconds(1)
    });
    await db.SaveChangesAsync();
    var replay = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, actor, request);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(recorded.Value!.RecordId, replay.Value!.RecordId);
    Assert.Equal(recorded.Value.ReadinessDigest, replay.Value.ReadinessDigest);
  }

  [Fact]
  public async Task OffboardingReadinessCarriesRetainedAdvanceReconciliationWithoutChangingTheSource()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var now = DateTimeOffset.UtcNow;
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
    var account = new BillingAccount
      { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "USD", CreatedAt = now };
    db.BillingAccounts.Add(account);
    var orphanInvoices = new List<(FeeFixture Fees, Invoice Invoice)>();
    for (var index = 0; index < 20; index++)
    {
      var fees = AddFeeAgreement(db, f, f.ClientId, null,
        $"SYN-OFF-ADVANCE-ORPHAN-{index:D2}", 100m + index, now);
      var invoice = AddFeeInvoiceSource(db, f, account, fees.Advance,
        $"SYN-OFF-ADVANCE-INVOICE-{index:D2}", now);
      fees.Advance.InvoiceId = null;
      fees.Advance.State = FeeMilestoneStates.Planned;
      orphanInvoices.Add((fees, invoice));
    }
    var orderedOrphans = orphanInvoices.OrderBy(item => item.Fees.Agreement.Id).ToArray();
    orderedOrphans[1].Invoice.Status = BillingStates.InvoiceDraft;
    orderedOrphans[1].Invoice.PostedAt = null;
    var ordinaryPlan = AddFeeAgreement(db, f, f.ClientId, null, "SYN-OFF-ADVANCE-ORDINARY-PLAN", 80m, now);
    await db.SaveChangesAsync();
    var countsBefore = (await db.Invoices.CountAsync(), await db.InvoiceLines.CountAsync(),
      await db.BillingSourceAllocations.CountAsync(), await db.FeeMilestones.CountAsync());

    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db,
      PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);
    Assert.True(readiness.Succeeded, readiness.Message);
    var feeSection = Assert.Single(readiness.Value!.Sections, section => section.Key == "FEE_MILESTONES");
    var reconciliation = Assert.Single(feeSection.Items, item => item.Key == "FEE_RECONCILIATION");
    Assert.Equal(ClientOffboardingStates.ActionRequired, reconciliation.State);
    Assert.Equal(20L, reconciliation.Count);
    Assert.True(reconciliation.Detail!.Length <= 2_000);
    Assert.Contains("Preview limited to 2,000 characters", reconciliation.Detail, StringComparison.Ordinal);
    Assert.Contains("showing ", reconciliation.Detail, StringComparison.Ordinal);
    Assert.Contains("of 20 reconciliation findings", reconciliation.Detail, StringComparison.Ordinal);
    Assert.Contains(orderedOrphans[0].Invoice.InvoiceNumber, reconciliation.Detail, StringComparison.Ordinal);
    Assert.Contains(BillingStates.InvoicePosted, reconciliation.Detail, StringComparison.Ordinal);
    Assert.Contains(orderedOrphans[1].Invoice.InvoiceNumber, reconciliation.Detail, StringComparison.Ordinal);
    Assert.Contains(BillingStates.InvoiceDraft, reconciliation.Detail, StringComparison.Ordinal);
    Assert.DoesNotContain(orderedOrphans[^1].Invoice.InvoiceNumber, reconciliation.Detail, StringComparison.Ordinal);
    Assert.DoesNotContain(ordinaryPlan.Advance.Id.ToString("D"), reconciliation.Detail!, StringComparison.Ordinal);
    var milestones = Assert.Single(feeSection.Items, item => item.Key == "FEE_MILESTONES");
    Assert.Equal(42L, milestones.Count);
    Assert.Contains("PLANNED 42", milestones.Detail!, StringComparison.Ordinal);
    Assert.Equal("/app/finance/fee-agreements", feeSection.OwnerRoute);
    Assert.Contains(feeSection.Items, item => item.Key == "FEE_MILESTONES" &&
      item.State == ClientOffboardingStates.ActionRequired);
    Assert.All(orderedOrphans, item =>
    {
      Assert.Null(item.Fees.Advance.InvoiceId);
      Assert.Equal(FeeMilestoneStates.Planned, item.Fees.Advance.State);
    });
    Assert.Equal(countsBefore, (await db.Invoices.CountAsync(), await db.InvoiceLines.CountAsync(),
      await db.BillingSourceAllocations.CountAsync(), await db.FeeMilestones.CountAsync()));

    var request = new ClientOffboardingCloseoutRequest(f.ClientId,
      "Reviewed retained advance reconciliation.", [], 0,
      ExpectedReadinessDigest: readiness.Value.ReadinessDigest,
      RequestKey: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    var recorded = await PracticeCrmService.RecordOffboardingCloseoutAsync(db,
      PbcSeed.Actor(f.Admin, "Administrator"), request);
    Assert.True(recorded.Succeeded, recorded.Message);

    // Change a source fact whose invoice identity was omitted from the bounded preview.
    // The private canonical facts must invalidate a new review without changing the old receipt.
    var sent = await BillingService.SendInvoiceAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
      orderedOrphans[^1].Invoice.Id);
    Assert.True(sent.Succeeded, sent.Message);
    var changedReadiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db,
      PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);
    Assert.True(changedReadiness.Succeeded, changedReadiness.Message);
    Assert.NotEqual(readiness.Value.ReadinessDigest, changedReadiness.Value!.ReadinessDigest);
    var changedReconciliation = Assert.Single(Assert.Single(changedReadiness.Value.Sections,
      section => section.Key == "FEE_MILESTONES").Items, item => item.Key == "FEE_RECONCILIATION");
    Assert.Equal(reconciliation.Detail, changedReconciliation.Detail);

    var staleRequest = request with
    {
      DispositionNotes = "Fresh review using the prior bounded preview.",
      RequestKey = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    };
    var stale = await PracticeCrmService.RecordOffboardingCloseoutAsync(db,
      PbcSeed.Actor(f.Admin, "Administrator"), staleRequest);
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);

    var replayAfterChange = await PracticeCrmService.RecordOffboardingCloseoutAsync(db,
      PbcSeed.Actor(f.Admin, "Administrator"), request);
    Assert.True(replayAfterChange.Succeeded, replayAfterChange.Message);
    Assert.Equal(recorded.Value!.RecordId, replayAfterChange.Value!.RecordId);
    Assert.Equal(recorded.Value.ReadinessDigest, replayAfterChange.Value.ReadinessDigest);

    db.WorkTasks.Add(new WorkTask { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
      EngagementId = f.EngagementId, Title = "Changed after retained advance review", CreatedAt = now });
    await db.SaveChangesAsync();
    var replay = await PracticeCrmService.RecordOffboardingCloseoutAsync(db,
      PbcSeed.Actor(f.Admin, "Administrator"), request);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(recorded.Value.RecordId, replay.Value!.RecordId);
    Assert.Equal(recorded.Value.ReadinessDigest, replay.Value.ReadinessDigest);

    // Add a newly retained source with an over-limit legacy identity after the old receipt.
    // Posted invoice history stays immutable while a fresh readiness read still fails closed.
    var oversizedIdentity = AddFeeAgreement(db, f, f.ClientId, null,
      "SYN-OFF-ADVANCE-OVERLIMIT-IDENTITY", 100m, now);
    AddFeeInvoiceSource(db, f, account, oversizedIdentity.Advance, new string('L', 257), now);
    oversizedIdentity.Advance.InvoiceId = null;
    oversizedIdentity.Advance.State = FeeMilestoneStates.Planned;
    await db.SaveChangesAsync();
    var unsupportedIdentity = await PracticeCrmService.GetClientOffboardingReadinessAsync(db,
      PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);
    Assert.True(unsupportedIdentity.Succeeded, unsupportedIdentity.Message);
    var unavailable = Assert.Single(Assert.Single(unsupportedIdentity.Value!.Sections,
      section => section.Key == "FEE_MILESTONES").Items, item => item.Key == "FEE_MILESTONES");
    Assert.Equal(ClientOffboardingStates.Unknown, unavailable.State);
    Assert.Null(unavailable.Count);
    Assert.Contains("256-character limit", unavailable.Detail!, StringComparison.Ordinal);
  }

  [Fact]
  public async Task OffboardingFeeReadinessSupportsExactlyOneHundredAgreements()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var now = DateTimeOffset.UtcNow;
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
    for (var index = 0; index < 100; index++)
      AddFeeAgreement(db, f, f.ClientId, null, $"SYN-OFF-LIMIT-{index:D3}", 100m, now);
    await db.SaveChangesAsync();

    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var supported = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
    Assert.True(supported.Succeeded, supported.Message);
    var supportedSection = Assert.Single(supported.Value!.Sections, section => section.Key == "FEE_MILESTONES");
    Assert.Equal("/app/finance/fee-agreements", supportedSection.OwnerRoute);
    var supportedItem = Assert.Single(supportedSection.Items, item => item.Key == "FEE_MILESTONES");
    Assert.Equal(ClientOffboardingStates.ActionRequired, supportedItem.State);
    Assert.Equal(200L, supportedItem.Count);
    Assert.True(supportedItem.Detail!.Length <= 2_000);

    AddFeeAgreement(db, f, f.ClientId, null, "SYN-OFF-LIMIT-101", 100m, now);
    await db.SaveChangesAsync();
    var overLimit = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
    Assert.True(overLimit.Succeeded, overLimit.Message);
    var overLimitSection = Assert.Single(overLimit.Value!.Sections, section => section.Key == "FEE_MILESTONES");
    Assert.Equal("/app/finance/fee-agreements", overLimitSection.OwnerRoute);
    var unavailable = Assert.Single(overLimitSection.Items, item => item.Key == "FEE_MILESTONES");
    Assert.Equal(ClientOffboardingStates.Unknown, unavailable.State);
    Assert.Null(unavailable.Count);
    Assert.Contains("exceeds the supported assessment limit", unavailable.Detail!, StringComparison.Ordinal);
  }

  [Fact]
  public async Task OffboardingReadinessDoesNotClaimCompleteTimeCoverageBeyond366Days()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var now = DateTimeOffset.UtcNow;
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
    var task = new WorkTask
      { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        Title = "Synthetic time coverage", CreatedAt = now };
    var rateCard = new RateCardVersion
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, Role = "Staff", Activity = "Offboarding coverage",
      Currency = "USD", RatePerHour = 100m, Status = PracticeTimeStates.RateApproved,
      CreatedByUserId = f.Admin.Id, ApprovedByUserId = f.Reviewer.Id, ApprovedAt = now, CreatedAt = now
    };
    db.WorkTasks.Add(task);
    db.RateCardVersions.Add(rateCard);
    db.TimeEntries.AddRange(
      ApprovedTime(f, task, rateCard, new DateOnly(2024, 1, 1), 0),
      ApprovedTime(f, task, rateCard, new DateOnly(2025, 1, 1), 1));
    await db.SaveChangesAsync();

    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db,
      PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);
    Assert.True(readiness.Succeeded, readiness.Message);
    var time = readiness.Value!.Sections.SelectMany(section => section.Items)
      .Single(item => item.Key == "APPROVED_UNBILLED_TIME");
    Assert.Equal(ClientOffboardingStates.Unknown, time.State);
    Assert.Null(time.Count);
    Assert.Contains("366-day report window", time.Detail);
  }

  [Fact]
  public async Task OffboardingCloseout_RecordsExactReplayableBlockedReview_WithoutArchivingOrOverstatingUnknowns()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var manager = PbcSeed.User(f.FirmId, "Staff");
    db.Users.Add(manager);
    db.RoleGrants.AddRange(
      PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId),
      PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
      PbcSeed.Grant(f.FirmId, f.Admin, "RecordsCustodian"));
    var accountId = Guid.NewGuid();
    db.BillingAccounts.Add(new BillingAccount { Id = accountId, FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "USD", CreatedAt = DateTimeOffset.UtcNow });
    var paidInvoice = new Invoice { Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = accountId, InvoiceNumber = "INV-OFF-1", Currency = "USD",
      Subtotal = 50m, Total = 50m, Status = BillingStates.InvoicePosted, PostedAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
    db.Invoices.Add(paidInvoice);
    var paymentReceipt = new Receipt
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = accountId, Amount = 50m,
      Currency = "USD", Reference = "SYN-OFF-CLOSEOUT-PAYMENT", Status = BillingStates.ReceiptRecorded,
      RecordedByUserId = f.Admin.Id, ReceivedAt = DateTimeOffset.UtcNow
    };
    var paymentAllocation = new ReceiptAllocation
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptId = paymentReceipt.Id,
      InvoiceId = paidInvoice.Id, Amount = 50m, CreatedAt = DateTimeOffset.UtcNow
    };
    db.Receipts.Add(paymentReceipt);
    db.ReceiptAllocations.Add(paymentAllocation);
    db.WorkTasks.Add(new WorkTask { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, Title = "Open fieldwork item", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();

    var managerActor = PbcSeed.Actor(manager, "Manager");
    var adminActor = PbcSeed.Actor(f.Admin, "Administrator");

    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, managerActor, f.ClientId);
    Assert.True(readiness.Succeeded, readiness.Message);
    Assert.Equal(ClientOffboardingStates.ActionRequired, readiness.Value!.OverallState);
    Assert.Contains(readiness.Value.Sections,
      section => section.Key == "WORK" && section.State == ClientOffboardingStates.ActionRequired);
    Assert.Contains(readiness.Value.Sections,
      section => section.Key == "FINANCE" && section.State == ClientOffboardingStates.Unknown);
    Assert.Equal(64, readiness.Value.ReadinessDigest.Length);

    var key = "33333333-3333-4333-8333-333333333333";
    var malformedKey = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, managerActor,
      new ClientOffboardingCloseoutRequest(f.ClientId, "Reviewed limited-visibility state.", [], 0,
        ExpectedReadinessDigest: readiness.Value.ReadinessDigest,
        RequestKey: "00000000-0000-4000-7000-000000000001"));
    Assert.False(malformedKey.Succeeded);
    Assert.Equal("crm.invalid", malformedKey.ErrorCode);

    var archiveAttempt = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, managerActor,
      new ClientOffboardingCloseoutRequest(f.ClientId, "Reviewed limited-visibility state.", [], 0, ArchiveClientOnCloseout: true,
        ExpectedReadinessDigest: readiness.Value.ReadinessDigest, RequestKey: key));
    Assert.False(archiveAttempt.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, archiveAttempt.ErrorCode);

    var staleDigestAttempt = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, managerActor,
      new ClientOffboardingCloseoutRequest(f.ClientId, "Reviewed limited-visibility state.", [], 0,
        ExpectedReadinessDigest: new string('0', 64), RequestKey: key));
    Assert.False(staleDigestAttempt.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleDigestAttempt.ErrorCode);

    db.WorkTasks.Add(new WorkTask { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
      EngagementId = f.EngagementId, Title = "Added after readiness review", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var changedReadiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, managerActor, f.ClientId);
    Assert.True(changedReadiness.Succeeded, changedReadiness.Message);
    Assert.NotEqual(readiness.Value!.ReadinessDigest, changedReadiness.Value!.ReadinessDigest);
    var staleReadinessAttempt = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, managerActor,
      new ClientOffboardingCloseoutRequest(f.ClientId, "Reviewed limited-visibility state.", [], 0,
        ExpectedReadinessDigest: readiness.Value!.ReadinessDigest, RequestKey: key));
    Assert.False(staleReadinessAttempt.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleReadinessAttempt.ErrorCode);

    var request = new ClientOffboardingCloseoutRequest(f.ClientId, "Reviewed limited-visibility state.", [], 0,
      ExpectedReadinessDigest: changedReadiness.Value!.ReadinessDigest, RequestKey: key);
    var recorded = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, managerActor, request);
    Assert.True(recorded.Succeeded, recorded.Message);
    Assert.Equal(ClientOffboardingRecordStates.BlockedDecision, recorded.Value!.Status);
    Assert.Equal(ClientOffboardingStates.ActionRequired, recorded.Value.ReadinessOverallState);
    Assert.False(recorded.Value.IsArchived);
    Assert.Equal("0", recorded.Value.ArchiveRevision);
    Assert.Empty(recorded.Value.AcknowledgedActionItems);

    // A retry returns the original point-in-time receipt even after the checklist changes.
    db.WorkTasks.Add(new WorkTask { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
      EngagementId = f.EngagementId, Title = "Added after reviewed snapshot", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var replay = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, managerActor, request);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(recorded.Value.RecordId, replay.Value!.RecordId);
    Assert.Equal(recorded.Value.ReadinessDigest, replay.Value.ReadinessDigest);
    Assert.Equal(recorded.Value.DecidedAt, replay.Value.DecidedAt);
    var persistedDecisionAt = await db.ClientOffboardingRecords
      .Where(x => x.FirmId == f.FirmId && x.Id == recorded.Value.RecordId).Select(x => x.DecidedAt).SingleAsync();
    Assert.Equal(recorded.Value.DecidedAt, persistedDecisionAt);
    Assert.Equal(persistedDecisionAt, replay.Value.DecidedAt);

    var changedNotes = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, managerActor,
      request with { DispositionNotes = "Different review intent." });
    Assert.False(changedNotes.Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict, changedNotes.ErrorCode);
    var changedActor = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, adminActor, request);
    Assert.False(changedActor.Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict, changedActor.ErrorCode);

    Assert.Equal(1, await db.ClientOffboardingRecords.CountAsync(x => x.ClientId == f.ClientId));
    Assert.Equal(0, await db.ClientLifecycleEvents.CountAsync(x => x.ClientId == f.ClientId));
    Assert.Equal(1, await db.Invoices.CountAsync(x => x.BillingAccountId == accountId));
    Assert.Equal(3, await db.WorkTasks.CountAsync(x => x.ClientId == f.ClientId));

    var financeReadiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, adminActor, f.ClientId);
    Assert.True(financeReadiness.Succeeded, financeReadiness.Message);
    Assert.Contains(financeReadiness.Value!.Sections, section => section.Key == "FINANCE" &&
      section.Items.Any(item => item.Key == "OUTSTANDING_INVOICES" && item.Count == 0));
    var financeRequest = new ClientOffboardingCloseoutRequest(f.ClientId, "Reviewed finance-visible state.", [], 0,
      ExpectedReadinessDigest: financeReadiness.Value.ReadinessDigest, RequestKey: "44444444-4444-4444-8444-444444444444");
    db.Invoices.Add(new Invoice { Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = accountId,
      InvoiceNumber = "INV-OFF-DRAFT", Currency = "USD", Subtotal = 10m, Total = 10m,
      Status = BillingStates.InvoiceDraft, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var staleFinance = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, adminActor, financeRequest);
    Assert.False(staleFinance.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleFinance.ErrorCode);
    financeReadiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, adminActor, f.ClientId);
    Assert.True(financeReadiness.Succeeded, financeReadiness.Message);
    financeRequest = financeRequest with { ExpectedReadinessDigest = financeReadiness.Value!.ReadinessDigest };
    var financeRecord = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, adminActor, financeRequest);
    Assert.True(financeRecord.Succeeded, financeRecord.Message);
    Assert.Equal(financeReadiness.Value.ReadinessDigest, financeRecord.Value!.ReadinessDigest);
    var financeReplay = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, adminActor, financeRequest);
    Assert.True(financeReplay.Succeeded, financeReplay.Message);
    Assert.Equal(financeRecord.Value.RecordId, financeReplay.Value!.RecordId);

    db.ReceiptAllocationReversals.Add(new ReceiptAllocationReversal
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptAllocationId = paymentAllocation.Id,
      Revision = 1, Amount = 10m, Reference = "SYN-OFF-CLOSEOUT-REVERSAL",
      Reason = "Synthetic approved payment correction", Status = ReceiptAllocationReversalStates.Approved,
      SubmittedByUserId = f.Admin.Id, SubmittedAt = DateTimeOffset.UtcNow,
      ReviewedByUserId = f.Reviewer.Id, ReviewedAt = DateTimeOffset.UtcNow,
      ReviewReason = "Approved synthetic payment correction"
    });
    await db.SaveChangesAsync();
    var oldIntentReplay = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, adminActor, financeRequest);
    Assert.True(oldIntentReplay.Succeeded, oldIntentReplay.Message);
    Assert.Equal(financeRecord.Value.RecordId, oldIntentReplay.Value!.RecordId);
    Assert.Equal(financeRecord.Value.ReadinessDigest, oldIntentReplay.Value.ReadinessDigest);
    var staleAfterPayment = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, adminActor,
      financeRequest with { RequestKey = "55555555-5555-4555-8555-555555555555" });
    Assert.False(staleAfterPayment.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleAfterPayment.ErrorCode);
    var afterPayment = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, adminActor, f.ClientId);
    Assert.True(afterPayment.Succeeded, afterPayment.Message);
    Assert.NotEqual(financeRecord.Value.ReadinessDigest, afterPayment.Value!.ReadinessDigest);
    Assert.Equal("USD 1 receipt(s), 10 unapplied", afterPayment.Value.Sections.SelectMany(section => section.Items)
      .Single(item => item.Key == "UNAPPLIED_RECEIPTS").Detail);
    Assert.Equal(1L, afterPayment.Value.Sections.SelectMany(section => section.Items)
      .Single(item => item.Key == "OUTSTANDING_INVOICES").Count);

    await using (var update = new AuditSphereDbContext(pg.Options))
      await Assert.ThrowsAsync<PostgresException>(() => update.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE client_offboarding_records SET disposition_notes = 'changed' WHERE id = {recorded.Value.RecordId}"));
    await using (var delete = new AuditSphereDbContext(pg.Options))
      await Assert.ThrowsAsync<PostgresException>(() => delete.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM client_offboarding_records WHERE id = {recorded.Value.RecordId}"));
    await using (var truncate = new AuditSphereDbContext(pg.Options))
      await Assert.ThrowsAsync<PostgresException>(() => truncate.Database.ExecuteSqlRawAsync("TRUNCATE TABLE client_offboarding_records"));

    var migrations = db.Database.GetMigrations().ToArray();
    var index = Array.FindIndex(migrations, name => name.EndsWith("_LifecycleAuditRemediationIntegrity", StringComparison.Ordinal));
    Assert.True(index > 0, "The lifecycle integrity migration must be discoverable.");
    var downgrade = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
    Assert.Equal("55000", Assert.IsType<PostgresException>(downgrade.GetBaseException()).SqlState);
    Assert.Contains(migrations[index], await db.Database.GetAppliedMigrationsAsync());
    Assert.Equal(2, await db.ClientOffboardingRecords.CountAsync(x => x.ClientId == f.ClientId));
  }

  [Fact]
  public async Task OffboardingCloseout_ResolvesSupersededRequestAndFreshReviewUsesCurrentRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var manager = PbcSeed.User(f.FirmId, "Staff");
    db.Users.Add(manager);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId));
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(manager, "Manager");
    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
    Assert.True(readiness.Succeeded, readiness.Message);
    var original = new ClientOffboardingCloseoutRequest(f.ClientId, "Original blocked review.", [], 0,
      ExpectedReadinessDigest: readiness.Value!.ReadinessDigest, RequestKey: Guid.NewGuid().ToString("D"));

    var archived = await PracticeCrmService.SetClientArchivedAsync(db, actor, new(f.ClientId, true, 0));
    Assert.True(archived.Succeeded, archived.Message);
    var staleRetry = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, actor, original,
      snapshotFactory: CreateOffboardingSnapshotFactory(pg));
    Assert.False(staleRetry.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleRetry.ErrorCode);
    Assert.Empty(await db.ClientOffboardingRecords.Where(x => x.ClientId == f.ClientId).ToListAsync());

    var unauthorized = await PracticeCrmService.ResolveOffboardingCloseoutAsync(db,
      PbcSeed.Actor(f.Client, "ClientUser"), new(f.ClientId, original.RequestKey!, original.ExpectedRevision!.Value,
        original.ExpectedReadinessDigest!, original.DispositionNotes, original.AcknowledgedActionItems));
    Assert.False(unauthorized.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, unauthorized.ErrorCode);

    var resolved = await PracticeCrmService.ResolveOffboardingCloseoutAsync(db, actor,
      new(f.ClientId, original.RequestKey!, original.ExpectedRevision!.Value,
        original.ExpectedReadinessDigest!, original.DispositionNotes, original.AcknowledgedActionItems));
    Assert.True(resolved.Succeeded, resolved.Message);
    Assert.Equal(ClientOffboardingCloseoutRecoveryOutcomes.SupersededWithoutCommit, resolved.Value!.Outcome);
    Assert.Equal(original.RequestKey, resolved.Value.RequestKey);
    Assert.Equal(original.ExpectedReadinessDigest, resolved.Value.ReadinessDigest);
    Assert.Equal("0", resolved.Value.ExpectedArchiveRevision);
    Assert.Equal("1", resolved.Value.CurrentArchiveRevision);
    Assert.Null(resolved.Value.Receipt);

    var fresh = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
    Assert.True(fresh.Succeeded, fresh.Message);
    Assert.Equal("1", fresh.Value!.ArchiveRevision);
    var newReview = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, actor,
      original with { ExpectedRevision = 1, ExpectedReadinessDigest = fresh.Value.ReadinessDigest, RequestKey = Guid.NewGuid().ToString("D") },
      snapshotFactory: CreateOffboardingSnapshotFactory(pg));
    Assert.True(newReview.Succeeded, newReview.Message);
    Assert.Equal("1", newReview.Value!.ArchiveRevision);
    Assert.True(newReview.Value.IsArchived);
    Assert.Equal(1, await db.ClientOffboardingRecords.CountAsync(x => x.ClientId == f.ClientId));
    Assert.Equal(1, await db.ClientLifecycleEvents.CountAsync(x => x.ClientId == f.ClientId));
  }

  [Fact]
  public async Task OffboardingCloseout_RecoversCommittedReceiptBeforeArchiveRevisionDrift()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var manager = PbcSeed.User(f.FirmId, "Staff");
    db.Users.Add(manager);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId));
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(manager, "Manager");
    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, actor, f.ClientId);
    Assert.True(readiness.Succeeded, readiness.Message);
    var original = new ClientOffboardingCloseoutRequest(f.ClientId, "Exact committed review.", [], 0,
      ExpectedReadinessDigest: readiness.Value!.ReadinessDigest, RequestKey: Guid.NewGuid().ToString("D"));
    var committed = await PracticeCrmService.RecordOffboardingCloseoutAsync(db, actor, original,
      snapshotFactory: CreateOffboardingSnapshotFactory(pg));
    Assert.True(committed.Succeeded, committed.Message);
    var archived = await PracticeCrmService.SetClientArchivedAsync(db, actor, new(f.ClientId, true, 0));
    Assert.True(archived.Succeeded, archived.Message);

    var recovered = await PracticeCrmService.ResolveOffboardingCloseoutAsync(db, actor,
      new(f.ClientId, original.RequestKey!, original.ExpectedRevision!.Value,
        original.ExpectedReadinessDigest!, original.DispositionNotes, original.AcknowledgedActionItems));
    Assert.True(recovered.Succeeded, recovered.Message);
    Assert.Equal(ClientOffboardingCloseoutRecoveryOutcomes.CommittedReceipt, recovered.Value!.Outcome);
    Assert.Equal("1", recovered.Value.CurrentArchiveRevision);
    Assert.Equal(committed.Value!.RecordId, recovered.Value.Receipt!.RecordId);
    Assert.Equal(committed.Value.DecidedAt, recovered.Value.Receipt.DecidedAt);
    Assert.Equal("0", recovered.Value.Receipt.ArchiveRevision);
    Assert.False(recovered.Value.Receipt.IsArchived);
    Assert.Equal(1, await db.ClientOffboardingRecords.CountAsync(x => x.ClientId == f.ClientId));
  }

  [Fact]
  public async Task OffboardingCloseout_RecoverySerializesAgainstDelayedOriginalAndFencesLatePublication()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var setup = new AuditSphereDbContext(pg.Options);
    var manager = PbcSeed.User(f.FirmId, "Staff");
    setup.Users.Add(manager);
    setup.RoleGrants.Add(PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId));
    await setup.SaveChangesAsync();
    var actor = PbcSeed.Actor(manager, "Manager");
    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(setup, actor, f.ClientId);
    Assert.True(readiness.Succeeded, readiness.Message);
    var original = new ClientOffboardingCloseoutRequest(f.ClientId, "Delayed original review.", [], 0,
      ExpectedReadinessDigest: readiness.Value!.ReadinessDigest, RequestKey: Guid.NewGuid().ToString("D"));
    await using var writer = new AuditSphereDbContext(pg.Options);
    await using var mutation = await writer.Database.BeginTransactionAsync();
    await writer.Database.ExecuteSqlInterpolatedAsync(
      $"SELECT * FROM firm_safety_states WHERE id = {f.FirmId} FOR UPDATE");
    writer.ClientLifecycleEvents.Add(new ClientLifecycleEvent
    {
      Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, Revision = 1,
      EventKind = ClientLifecycleEventKinds.Archived, ActorUserId = manager.Id, OccurredAt = DateTimeOffset.UtcNow
    });
    await writer.SaveChangesAsync();

    await using var resolutionDb = new AuditSphereDbContext(pg.Options);
    await using var originalDb = new AuditSphereDbContext(pg.Options);
    await resolutionDb.Database.OpenConnectionAsync();
    await originalDb.Database.OpenConnectionAsync();
    var resolutionPid = ((NpgsqlConnection)resolutionDb.Database.GetDbConnection()).ProcessID;
    var originalPid = ((NpgsqlConnection)originalDb.Database.GetDbConnection()).ProcessID;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var resolution = PracticeCrmService.ResolveOffboardingCloseoutAsync(resolutionDb, actor,
      new(f.ClientId, original.RequestKey!, original.ExpectedRevision!.Value,
        original.ExpectedReadinessDigest!, original.DispositionNotes, original.AcknowledgedActionItems), deadline.Token);
    Task<CommandResult<ClientOffboardingCloseoutResult>>? originalPublication = null;
    try
    {
      await WaitUntilBlocked(resolutionPid, deadline.Token);
      originalPublication = PracticeCrmService.RecordOffboardingCloseoutAsync(originalDb, actor, original, deadline.Token,
        CreateOffboardingSnapshotFactory(pg));
      await WaitUntilBlocked(originalPid, deadline.Token);
      await mutation.CommitAsync(deadline.Token);
      var resolved = await resolution;
      var published = await originalPublication!;
      Assert.True(resolved.Succeeded, resolved.Message);
      Assert.Equal(ClientOffboardingCloseoutRecoveryOutcomes.SupersededWithoutCommit, resolved.Value!.Outcome);
      Assert.False(published.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, published.ErrorCode);
    }
    finally
    {
      deadline.Cancel();
      if (writer.Database.CurrentTransaction is not null) await mutation.RollbackAsync();
    }
    Assert.Empty(await setup.ClientOffboardingRecords.Where(x => x.ClientId == f.ClientId).ToListAsync());
    Assert.Equal(1, await setup.ClientLifecycleEvents.CountAsync(x => x.ClientId == f.ClientId));

    async Task WaitUntilBlocked(int pid, CancellationToken cancellationToken)
    {
      await using var observer = new NpgsqlConnection(pg.ConnectionString);
      await observer.OpenAsync(cancellationToken);
      await using var blocked = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid)) > 0", observer);
      blocked.Parameters.AddWithValue("pid", pid);
      while (!(bool)(await blocked.ExecuteScalarAsync(cancellationToken))!)
        await Task.Delay(10, cancellationToken);
    }
  }

  [Fact]
  public async Task OffboardingCloseout_RefusesArchiveRevisionChangedWhileWaitingForFirmFence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var writer = new AuditSphereDbContext(pg.Options);
    var manager = PbcSeed.User(f.FirmId, "Staff");
    writer.Users.Add(manager);
    writer.RoleGrants.Add(PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId));
    await writer.SaveChangesAsync();
    var actor = PbcSeed.Actor(manager, "Manager");
    await using var reader = new AuditSphereDbContext(pg.Options);
    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(reader, actor, f.ClientId);
    Assert.True(readiness.Succeeded, readiness.Message);
    var request = new ClientOffboardingCloseoutRequest(f.ClientId, "Reviewed before archive change.", [], 0,
      ExpectedReadinessDigest: readiness.Value!.ReadinessDigest, RequestKey: Guid.NewGuid().ToString("D"));

    await using var mutation = await writer.Database.BeginTransactionAsync();
    await writer.Database.ExecuteSqlInterpolatedAsync(
      $"SELECT * FROM firm_safety_states WHERE id = {f.FirmId} FOR UPDATE");
    await reader.Database.OpenConnectionAsync();
    var readerPid = ((NpgsqlConnection)reader.Database.GetDbConnection()).ProcessID;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var publication = PracticeCrmService.RecordOffboardingCloseoutAsync(reader, actor, request, deadline.Token,
      CreateOffboardingSnapshotFactory(pg));
    try
    {
      await using var observer = new NpgsqlConnection(pg.ConnectionString);
      await observer.OpenAsync(deadline.Token);
      await using var blocked = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid)) > 0", observer);
      blocked.Parameters.AddWithValue("pid", readerPid);
      while (!(bool)(await blocked.ExecuteScalarAsync(deadline.Token))!)
      {
        Assert.False(publication.IsCompleted, "Closeout must wait for the firm fence before publication.");
        await Task.Delay(10, deadline.Token);
      }

      writer.ClientLifecycleEvents.Add(new ClientLifecycleEvent
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, Revision = 1,
        EventKind = ClientLifecycleEventKinds.Archived, ActorUserId = manager.Id, OccurredAt = DateTimeOffset.UtcNow
      });
      await writer.SaveChangesAsync(deadline.Token);
      await mutation.CommitAsync(deadline.Token);
      var refused = await publication;
      Assert.False(refused.Succeeded, refused.Message);
      Assert.Equal(ErrorCodes.GenerationStale, refused.ErrorCode);
    }
    finally
    {
      deadline.Cancel();
      if (writer.Database.CurrentTransaction is not null) await mutation.RollbackAsync();
    }
    Assert.Empty(await writer.ClientOffboardingRecords.ToListAsync());
    Assert.Equal(1, await writer.ClientLifecycleEvents.CountAsync(e => e.ClientId == f.ClientId));
  }

  [Fact]
  public async Task OffboardingCloseout_RefusesEpochRevokedWhileWaitingForFirmFence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var writer = new AuditSphereDbContext(pg.Options);
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var grant = PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId);
    writer.Users.Add(manager); writer.RoleGrants.Add(grant);
    await writer.SaveChangesAsync();
    var actor = PbcSeed.Actor(manager, "Manager");
    await using var reader = new AuditSphereDbContext(pg.Options);
    var readiness = await PracticeCrmService.GetClientOffboardingReadinessAsync(reader, actor, f.ClientId);
    Assert.True(readiness.Succeeded, readiness.Message);
    var invoiceCount = await writer.Invoices.CountAsync();
    var archiveCount = await writer.ClientLifecycleEvents.CountAsync();

    await using var mutation = await writer.Database.BeginTransactionAsync();
    await writer.Database.ExecuteSqlInterpolatedAsync(
      $"SELECT * FROM firm_safety_states WHERE id = {f.FirmId} FOR UPDATE");
    await reader.Database.OpenConnectionAsync();
    var readerPid = ((NpgsqlConnection)reader.Database.GetDbConnection()).ProcessID;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var publication = PracticeCrmService.RecordOffboardingCloseoutAsync(reader, actor,
      new ClientOffboardingCloseoutRequest(f.ClientId, "Reviewed before access revocation.", [], 0,
        ExpectedReadinessDigest: readiness.Value!.ReadinessDigest, RequestKey: Guid.NewGuid().ToString("D")), deadline.Token,
      CreateOffboardingSnapshotFactory(pg));
    try
    {
      // Observe the real server-side wait, not just dispatch: its RepeatableRead snapshot
      // now predates the epoch mutation that the firm's lock will publish first.
      await using var observer = new NpgsqlConnection(pg.ConnectionString);
      await observer.OpenAsync(deadline.Token);
      await using var blocked = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid)) > 0", observer);
      blocked.Parameters.AddWithValue("pid", readerPid);
      while (!(bool)(await blocked.ExecuteScalarAsync(deadline.Token))!)
      {
        Assert.False(publication.IsCompleted, "Closeout must wait for the firm fence before publication.");
        await Task.Delay(10, deadline.Token);
      }
      // Match the persisted effects of RoleAdministrationService's serialized revocation.
      grant.RevokedAt = DateTimeOffset.UtcNow;
      manager.SessionEpoch++;
      await writer.SaveChangesAsync(deadline.Token);
      await mutation.CommitAsync(deadline.Token);
      var refused = await publication;
      Assert.False(refused.Succeeded, refused.Message);
      Assert.Equal(ErrorCodes.GenerationStale, refused.ErrorCode);
    }
    finally
    {
      deadline.Cancel();
      if (writer.Database.CurrentTransaction is not null) await mutation.RollbackAsync();
    }
    Assert.Empty(await writer.ClientOffboardingRecords.ToListAsync());
    Assert.Equal(invoiceCount, await writer.Invoices.CountAsync());
    Assert.Equal(archiveCount, await writer.ClientLifecycleEvents.CountAsync());
  }

  [Fact]
  public async Task ClientArchiveIsReversibleSearchableScopedAndAppendOnly()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ClientProfileWorkspaceSeed.PopulateAsync(db, f, count: 1);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId));
    await db.SaveChangesAsync();
    var manager = PbcSeed.Actor(f.Staff, "Manager");

    var before = await WorkspaceQuery.ClientAsync(db, manager, f.ClientId);
    Assert.True(before.Succeeded, before.Message);
    Assert.False(before.Value!.IsArchived);
    Assert.Equal("0", before.Value.LifecycleRevision);

    var nonMicrosecondTime = new DateTimeOffset(2026, 11, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1_234_567);
    var archived = await PracticeCrmService.SetClientArchivedAsync(db, manager, new(f.ClientId, true, 0), new ArchiveClock(nonMicrosecondTime));
    Assert.True(archived.Succeeded, archived.Message);
    Assert.True(archived.Value!.IsArchived);
    Assert.Equal(1, archived.Value.Revision);
    Assert.NotNull(archived.Value.LastArchivedAt);

    var replay = await PracticeCrmService.SetClientArchivedAsync(db, manager, new(f.ClientId, true, 1));
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(1, replay.Value!.Revision);
    var persistedArchivedAt = await db.ClientLifecycleEvents.AsNoTracking()
      .Where(e => e.ClientId == f.ClientId && e.EventKind == ClientLifecycleEventKinds.Archived)
      .Select(e => e.OccurredAt).SingleAsync();
    Assert.Equal(persistedArchivedAt, archived.Value.LastArchivedAt);
    Assert.Equal(persistedArchivedAt, replay.Value.LastArchivedAt);
    Assert.Single(await db.ClientLifecycleEvents.Where(e => e.ClientId == f.ClientId).ToListAsync());
    Assert.Equal(ErrorCodes.GenerationStale,
      (await PracticeCrmService.SetClientArchivedAsync(db, manager, new(f.ClientId, false, 0))).ErrorCode);

    var visible = await PortfolioQuery.ListAsync(db, manager, "PBC TEST CLIENT");
    Assert.True(visible.Succeeded, visible.Message);
    Assert.True(Assert.Single(visible.Value!.Items).IsArchived);
    var profile = await WorkspaceQuery.ClientAsync(db, manager, f.ClientId);
    Assert.True(profile.Succeeded, profile.Message);
    Assert.True(profile.Value!.IsArchived);
    Assert.Single(profile.Value.Engagements);

    var denied = await PracticeCrmService.SetClientArchivedAsync(db,
      PbcSeed.Actor(f.Client, "ClientUser"), new(f.ClientId, false, 1));
    Assert.False(denied.Succeeded);
    Assert.Equal(1, await db.ClientLifecycleEvents.CountAsync(e => e.ClientId == f.ClientId));

    var restored = await PracticeCrmService.SetClientArchivedAsync(db, manager, new(f.ClientId, false, 1));
    Assert.True(restored.Succeeded, restored.Message);
    Assert.False(restored.Value!.IsArchived);
    Assert.Equal(2, restored.Value.Revision);
    Assert.NotNull(restored.Value.LastArchivedAt);
    Assert.Equal(2, await db.ClientLifecycleEvents.CountAsync(e => e.ClientId == f.ClientId));
    var history = await db.ClientLifecycleEvents.Where(e => e.ClientId == f.ClientId).OrderBy(e => e.Revision).ToListAsync();
    Assert.Equal(new[] { ClientLifecycleEventKinds.Archived, ClientLifecycleEventKinds.Restored }, history.Select(e => e.EventKind).ToArray());
    var refusal = await Assert.ThrowsAnyAsync<Exception>(async () =>
      await db.ClientLifecycleEvents.Where(e => e.ClientId == f.ClientId)
        .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.EventKind, ClientLifecycleEventKinds.Archived)));
    Assert.Equal("55000", Assert.IsType<PostgresException>(refusal.GetBaseException()).SqlState);
    var migrations = db.Database.GetMigrations().ToArray();
    var archiveMigration = Array.FindIndex(migrations, name => name.Contains("_ClientLifecycleArchiveHistory", StringComparison.Ordinal));
    Assert.True(archiveMigration > 0, "The archive history migration must be discoverable.");
    var downgrade = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(migrations[archiveMigration - 1]));
    Assert.Equal("55000", Assert.IsType<PostgresException>(downgrade.GetBaseException()).SqlState);
    Assert.Equal(2, await db.ClientLifecycleEvents.CountAsync(e => e.ClientId == f.ClientId));
  }

  [Fact]
  public async Task MetadataFullScopedCountsAndIndependentPagesNeverTruncateAtTheOldWindow()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ClientProfileWorkspaceSeed.PopulateAsync(db, f, 105);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    var result = await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new());
    Assert.True(result.Succeeded, result.Message); var v = result.Value!;
    Assert.Equal(new ClientWorkspaceMetrics(105, 52, 105), v.Metrics);
    Assert.Equal(10, v.Engagements.Count); Assert.Equal(10, v.Contacts.Count);
    var retainedWindow = (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Value!;
    Assert.Equal(100, retainedWindow.Engagements.Count); Assert.Equal(100, retainedWindow.Contacts.Count);
    Assert.Equal(v.Metrics, retainedWindow.Metrics);
    Assert.Equal("Synthetic trading name", v.CommercialName); Assert.Equal("SYNTHETIC-REG-001", v.RegistrationNumber);
    Assert.Equal("QA", v.Jurisdiction); Assert.Equal("9007199254740993", v.SafetyGeneration);
    Assert.Equal("AWAITING_ACCEPTANCE", v.PortalIntent!.State); Assert.False(v.PortalIntent.ContactHasClientAccess);
    var last = (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new(2, 50, 4, 25))).Value!;
    Assert.Equal(5, last.Engagements.Count); Assert.Equal(5, last.Contacts.Count); Assert.Equal(v.Metrics, last.Metrics);
    Assert.Empty(last.Engagements.Select(e => e.Id).Intersect(v.Engagements.Select(e => e.Id)));
    Assert.Empty(last.Contacts.Select(c => c.Id).Intersect(v.Contacts.Select(c => c.Id)));
    Assert.Equal("request.invalid", (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new(-1))).ErrorCode);
    Assert.Equal("request.invalid", (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new(ContactPageSize: 100))).ErrorCode);
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, foreign.ClientId)).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, Guid.NewGuid())).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, PbcSeed.Actor(f.Client, "ClientUser"), f.ClientId)).Succeeded);
    Assert.DoesNotContain("EXCLUDED PRIVATE PROFILE", System.Text.Json.JsonSerializer.Serialize(v));
    await db.Users.Where(u => u.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Disabled, true));
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded);
  }

  [Fact]
  public async Task ClientCommercialAuthorityDoesNotWidenEngagementRoleOrAggregateSiblingData()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ClientProfileWorkspaceSeed.PopulateAsync(db, f, grantClientStaff: false);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "CommercialManager", f.ClientId)); await db.SaveChangesAsync();
    var result = await WorkspaceQuery.ClientAsync(db, actor, f.ClientId);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(new ClientWorkspaceMetrics(1, 0, 27), result.Value!.Metrics);
    Assert.Equal(f.EngagementId, Assert.Single(result.Value.Engagements).Id);
    Assert.False(result.Value.CanCreateEngagement); Assert.False(result.Value.CanManageContacts);
    await db.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.Role == "Staff")
      .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    var expired = PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId, f.EngagementId);
    expired.GrantedAt = DateTimeOffset.UtcNow.AddHours(-2);
    expired.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
    db.RoleGrants.Add(expired); await db.SaveChangesAsync();
    var withoutEngagement = (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Value!;
    Assert.Equal(0, withoutEngagement.Metrics.Engagements); Assert.Empty(withoutEngagement.Engagements);
  }

  [Fact]
  public async Task MidProjectionEngagementRevocationRefusesOldCountsWhileClientGrantRemains()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      await ClientProfileWorkspaceSeed.PopulateAsync(setup, f, grantClientStaff: false);
      setup.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "CommercialManager", f.ClientId)); await setup.SaveChangesAsync();
    }
    var interceptor = new RevokeDuringContacts(async () => {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.EngagementId == f.EngagementId)
        .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded); Assert.True(interceptor.Fired);
    Assert.Equal(0, (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Value!.Metrics.Engagements);
  }

  private sealed record FeeFixture(EngagementFeeAgreement Agreement, FeeMilestone Advance, FeeMilestone Balance);

  private static FeeFixture AddFeeAgreement(AuditSphereDbContext db, PbcSeed.Fixture fixture,
    Guid clientId, Guid? engagementId, string suffix, decimal fee, DateTimeOffset now)
  {
    var lead = new Lead
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, Name = suffix,
      Source = "SYNTHETIC_TEST", Status = CrmStates.LeadQualified, CreatedAt = now
    };
    var opportunity = new Opportunity
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, LeadId = lead.Id, PracticeClientId = clientId,
      ServiceRoute = "AccountingOnly", EntityScope = "Synthetic test scope",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ExpectedFee = fee,
      Currency = "USD", Stage = CrmStates.OpportunityProposal, CreatedAt = now
    };
    var proposal = new Proposal
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, OpportunityId = opportunity.Id,
      PracticeClientId = clientId, Status = CrmStates.ProposalDraft, ServiceProfileId = "SYNTHETIC",
      Scope = "Synthetic fee observation", Exclusions = "None", Deliverables = "Synthetic deliverable",
      Dependencies = "None", Fee = fee, Currency = "USD", PeriodStart = "2026-01-01",
      PeriodEnd = "2026-12-31", CreatedAt = now
    };
    var quotation = new QuotationVersion
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ProposalId = proposal.Id, Currency = "USD",
      BaseAmount = fee, Fee = fee, InputHash = new string('a', 64), Status = QuotationStates.Approved,
      CreatedByUserId = fixture.Admin.Id, CreatedAt = now, ApprovedAt = now
    };
    var agreement = new EngagementFeeAgreement
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ProposalId = proposal.Id,
      QuotationVersionId = quotation.Id, PracticeClientId = clientId, EngagementId = engagementId,
      Currency = "USD", AgreedFee = fee, AdvancePercent = 50m,
      CreatedByUserId = fixture.Admin.Id, CreatedAt = now
    };
    var advance = new FeeMilestone
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, AgreementId = agreement.Id,
      Kind = FeeMilestoneKinds.Advance, Amount = fee / 2m, State = FeeMilestoneStates.Planned, CreatedAt = now
    };
    var balance = new FeeMilestone
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, AgreementId = agreement.Id,
      Kind = FeeMilestoneKinds.Balance, Amount = fee - advance.Amount,
      State = FeeMilestoneStates.Planned, CreatedAt = now
    };
    db.Leads.Add(lead);
    db.Opportunities.Add(opportunity);
    db.Proposals.Add(proposal);
    db.QuotationVersions.Add(quotation);
    db.EngagementFeeAgreements.Add(agreement);
    db.FeeMilestones.AddRange(advance, balance);
    return new(agreement, advance, balance);
  }

  private static Invoice AddFeeInvoiceSource(AuditSphereDbContext db, PbcSeed.Fixture fixture,
    BillingAccount account, FeeMilestone milestone, string invoiceNumber, DateTimeOffset now,
    Guid? sourceIdOverride = null)
  {
    var invoice = new Invoice
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, BillingAccountId = account.Id,
      InvoiceNumber = invoiceNumber, Currency = account.Currency, Subtotal = milestone.Amount,
      Total = milestone.Amount, Status = BillingStates.InvoicePosted, PostedAt = now, CreatedAt = now
    };
    var line = new InvoiceLine
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, InvoiceId = invoice.Id,
      Description = "Synthetic fee milestone", Quantity = 1m, UnitPrice = milestone.Amount,
      LineTotal = milestone.Amount, SourceKind = FeeAgreementService.MilestoneSourceKind,
      SourceId = sourceIdOverride ?? milestone.Id, SourceRevision = 1
    };
    db.Invoices.Add(invoice);
    db.InvoiceLines.Add(line);
    db.BillingSourceAllocations.Add(new BillingSourceAllocation
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, InvoiceLineId = line.Id,
      SourceKind = FeeAgreementService.MilestoneSourceKind, SourceId = milestone.Id,
      SourceRevision = 1, Quantity = 1m, Amount = milestone.Amount, CreatedAt = now
    });
    milestone.InvoiceId = invoice.Id;
    milestone.State = FeeMilestoneStates.Invoiced;
    return invoice;
  }

  private static TimeEntry ApprovedTime(PbcSeed.Fixture fixture, WorkTask task,
    RateCardVersion rateCard, DateOnly workDate, int index,
    string status = PracticeTimeStates.TimeApproved, long revision = 1, Guid? supersedesId = null)
  {
    var now = DateTimeOffset.UtcNow;
    return new TimeEntry
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
      EngagementId = fixture.EngagementId, TaskId = task.Id, UserId = fixture.Staff.Id,
      WorkDate = workDate, StartMinute = index * 60, DurationMinutes = 60,
      Role = rateCard.Role, Activity = rateCard.Activity, Phase = BudgetPhases.Unassigned,
      BillableClassification = PracticeTimeStates.Billable, Narrative = "Synthetic approved time",
      NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Status = status, Currency = rateCard.Currency,
      Revision = revision, SupersedesId = supersedesId, RateCardVersionId = rateCard.Id,
      RatePerHour = rateCard.RatePerHour, SubmittedAt = now, ApprovedByUserId = fixture.Admin.Id,
      ApprovedAt = now, CreatedAt = now
    };
  }

  private static void AddTimeInvoiceSource(AuditSphereDbContext db, PbcSeed.Fixture fixture,
    BillingAccount account, TimeEntry entry, string invoiceNumber, string status, DateTimeOffset now)
  {
    var amount = entry.DurationMinutes * entry.RatePerHour!.Value / 60m;
    var invoice = new Invoice
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, BillingAccountId = account.Id,
      InvoiceNumber = invoiceNumber, Currency = account.Currency, Subtotal = amount, Total = amount,
      Status = status, PostedAt = status == BillingStates.InvoicePosted ? now : null, CreatedAt = now
    };
    var line = new InvoiceLine
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, InvoiceId = invoice.Id,
      Description = "Synthetic approved time", Quantity = entry.DurationMinutes / 60m,
      UnitPrice = entry.RatePerHour.Value, LineTotal = amount, SourceKind = "TIME",
      SourceId = entry.Id, SourceRevision = entry.Revision
    };
    db.Invoices.Add(invoice);
    db.InvoiceLines.Add(line);
    db.BillingSourceAllocations.Add(new BillingSourceAllocation
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, InvoiceLineId = line.Id,
      SourceKind = "TIME", SourceId = entry.Id, SourceRevision = entry.Revision,
      Quantity = entry.DurationMinutes / 60m, Amount = amount, CreatedAt = now
    });
  }

  private static Receipt ReceiptFor(PbcSeed.Fixture fixture, BillingAccount account,
    string reference, decimal amount, DateTimeOffset receivedAt) => new()
  {
    Id = Guid.NewGuid(), FirmId = fixture.FirmId, BillingAccountId = account.Id,
    Amount = amount, Currency = account.Currency, Reference = reference,
    Status = BillingStates.ReceiptRecorded, RecordedByUserId = fixture.Admin.Id, ReceivedAt = receivedAt
  };

  private static ReceiptAllocation AllocationFor(PbcSeed.Fixture fixture, Receipt receipt,
    Invoice invoice, decimal amount, DateTimeOffset createdAt) => new()
  {
    Id = Guid.NewGuid(), FirmId = fixture.FirmId, ReceiptId = receipt.Id,
    InvoiceId = invoice.Id, Amount = amount, CreatedAt = createdAt
  };

  private static IAuditSphereDbContextFactory CreateOffboardingSnapshotFactory(PgTestSchema pg) =>
    new OffboardingSnapshotContextFactory(pg.Options);

  private sealed class OffboardingSnapshotContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IAuditSphereDbContextFactory
  {
    public Task<IAuditSphereDbContext> CreateAsync(CancellationToken ct = default)
    {
      ct.ThrowIfCancellationRequested();
      return Task.FromResult<IAuditSphereDbContext>(new AuditSphereDbContext(options));
    }
  }

  private sealed class RevokeDuringContacts(Func<Task> revoke) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (!Fired && command.CommandText.Contains("FROM client_contacts", StringComparison.Ordinal))
      { Fired = true; await revoke(); }
      return result;
    }
  }

  private sealed class RevokeFinanceBeforeReceiptRead(Func<Task> revoke) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      var sql = command.CommandText;
      if (!Fired && sql.Contains("receipts", StringComparison.OrdinalIgnoreCase) &&
          sql.Contains("billing_accounts", StringComparison.OrdinalIgnoreCase))
      {
        Fired = true;
        await revoke();
      }
      return result;
    }
  }

  private sealed class RevokeFinanceBeforeArchiveRead(Func<Task> revoke) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (!Fired && command.CommandText.Contains("archives", StringComparison.OrdinalIgnoreCase))
      {
        Fired = true;
        await revoke();
      }
      return result;
    }
  }
}
