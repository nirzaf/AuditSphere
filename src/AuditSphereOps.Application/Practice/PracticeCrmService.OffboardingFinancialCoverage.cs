using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class PracticeCrmService
{
  private const int OffboardingReceiptLimit = 5_000;
  private const int OffboardingReceiptAllocationLimit = 20_000;
  private const int OffboardingTimeLimit = 10_000;
  private const int OffboardingFeeAgreementLimit = 100;

  private sealed record ReceiptAllocationFact(ReceiptAllocation Allocation, Invoice Invoice, string InvoiceAccountCurrency,
    Guid InvoiceClientId);

  private static async Task<ClientOffboardingItem> UnallocatedReceiptsItemAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct)
  {
    const string label = "Recorded receipts with unapplied value";
    var receipts = await (from receipt in db.Receipts.AsNoTracking()
      join account in db.BillingAccounts.AsNoTracking()
        on new { receipt.FirmId, Id = receipt.BillingAccountId } equals new { account.FirmId, Id = account.Id }
      where receipt.FirmId == actor.FirmId && account.PracticeClientId == clientId
      orderby receipt.ReceivedAt, receipt.Id
      select new { Receipt = receipt, AccountCurrency = account.Currency })
      .Take(OffboardingReceiptLimit + 1).ToListAsync(ct);
    if (receipts.Count > OffboardingReceiptLimit)
      return Unavailable("UNAPPLIED_RECEIPTS", label, "The recorded-receipt population exceeds the supported assessment limit.");
    if (receipts.Count == 0) return Counted("UNAPPLIED_RECEIPTS", label, 0);

    if (receipts.Any(row => row.Receipt.Status != BillingStates.ReceiptRecorded ||
        row.Receipt.Amount <= 0m || MoneyPolicy.Normalize(row.Receipt.Amount) != row.Receipt.Amount ||
        !IsCurrency(row.Receipt.Currency) || !IsCurrency(row.AccountCurrency) ||
        !string.Equals(row.Receipt.Currency, row.AccountCurrency, StringComparison.OrdinalIgnoreCase)))
      return Unavailable("UNAPPLIED_RECEIPTS", label, "Recorded receipt amount, state or account currency could not be reconciled safely.");

    var byReceipt = receipts.ToDictionary(row => row.Receipt.Id);
    var receiptIds = byReceipt.Keys.ToArray();
    var allocations = await db.ReceiptAllocations.AsNoTracking().Where(allocation =>
        allocation.FirmId == actor.FirmId && receiptIds.Contains(allocation.ReceiptId))
      .OrderBy(allocation => allocation.ReceiptId).ThenBy(allocation => allocation.Id)
      .Take(OffboardingReceiptAllocationLimit + 1).ToListAsync(ct);
    if (allocations.Count > OffboardingReceiptAllocationLimit)
      return Unavailable("UNAPPLIED_RECEIPTS", label, "Receipt allocation history exceeds the supported assessment limit.");
    if (allocations.Any(allocation => !byReceipt.ContainsKey(allocation.ReceiptId) || allocation.Amount <= 0m ||
        MoneyPolicy.Normalize(allocation.Amount) != allocation.Amount))
      return Unavailable("UNAPPLIED_RECEIPTS", label, "Receipt allocation history requires finance reconciliation.");

    var allocationIds = allocations.Select(allocation => allocation.Id).ToArray();
    List<ReceiptAllocationFact> allocationFacts = allocations.Count == 0 ? [] : await (from allocation in db.ReceiptAllocations.AsNoTracking()
      join invoice in db.Invoices.AsNoTracking()
        on new { allocation.FirmId, Id = allocation.InvoiceId } equals new { invoice.FirmId, Id = invoice.Id }
      join account in db.BillingAccounts.AsNoTracking()
        on new { invoice.FirmId, Id = invoice.BillingAccountId } equals new { account.FirmId, Id = account.Id }
      where allocationIds.Contains(allocation.Id)
      select new ReceiptAllocationFact(allocation, invoice, account.Currency, account.PracticeClientId))
      .ToListAsync(ct);
    if (allocationFacts.Count != allocations.Count || allocationFacts.Any(fact =>
        !byReceipt.TryGetValue(fact.Allocation.ReceiptId, out var receipt) ||
        fact.Invoice.BillingAccountId != receipt.Receipt.BillingAccountId || fact.InvoiceClientId != clientId ||
        fact.Invoice.Status is not (BillingStates.InvoicePosted or BillingStates.InvoiceSent) ||
        !IsCurrency(fact.Invoice.Currency) ||
        !string.Equals(fact.Invoice.Currency, receipt.Receipt.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(fact.InvoiceAccountCurrency, receipt.AccountCurrency, StringComparison.OrdinalIgnoreCase)))
      return Unavailable("UNAPPLIED_RECEIPTS", label, "Receipt allocation source links require finance reconciliation.");

    var reversals = allocationIds.Length == 0 ? [] : await db.ReceiptAllocationReversals.AsNoTracking()
      .Where(reversal => reversal.FirmId == actor.FirmId && allocationIds.Contains(reversal.ReceiptAllocationId))
      .OrderBy(reversal => reversal.ReceiptAllocationId).ThenBy(reversal => reversal.Id)
      .Take(OffboardingReceiptAllocationLimit + 1).ToListAsync(ct);
    if (reversals.Count > OffboardingReceiptAllocationLimit)
      return Unavailable("UNAPPLIED_RECEIPTS", label, "Receipt reversal history exceeds the supported assessment limit.");
    var allocationById = allocations.ToDictionary(allocation => allocation.Id);
    if (reversals.Any(reversal => !allocationById.TryGetValue(reversal.ReceiptAllocationId, out var allocation) ||
        reversal.Amount <= 0m || MoneyPolicy.Normalize(reversal.Amount) != reversal.Amount ||
        reversal.Status is not (ReceiptAllocationReversalStates.PendingReview or
          ReceiptAllocationReversalStates.Approved or ReceiptAllocationReversalStates.Rejected) ||
        reversal.Amount > allocation!.Amount))
      return Unavailable("UNAPPLIED_RECEIPTS", label, "Receipt allocation reversal history requires finance reconciliation.");
    if (reversals.Where(reversal => reversal.Status == ReceiptAllocationReversalStates.Approved)
        .GroupBy(reversal => reversal.ReceiptAllocationId)
        .Any(group => group.Sum(reversal => reversal.Amount) > allocationById[group.Key].Amount))
      return Unavailable("UNAPPLIED_RECEIPTS", label, "Approved receipt reversals exceed their retained allocation.");

    var netApplied = await BillingService.GetNetAppliedToReceiptsAsync(db, actor.FirmId, receiptIds, ct);
    var unallocated = new Dictionary<string, (int Count, decimal Amount)>(StringComparer.OrdinalIgnoreCase);
    foreach (var row in receipts)
    {
      var applied = netApplied.GetValueOrDefault(row.Receipt.Id);
      if (applied < 0m || applied > row.Receipt.Amount)
        return Unavailable("UNAPPLIED_RECEIPTS", label, "Net receipt application is outside the retained receipt amount.");
      var remaining = MoneyPolicy.Normalize(row.Receipt.Amount - applied);
      if (remaining <= 0m) continue;
      var currency = row.AccountCurrency.ToUpperInvariant();
      var current = unallocated.GetValueOrDefault(currency);
      try
      {
        unallocated[currency] = (current.Count + 1, MoneyPolicy.Normalize(checked(current.Amount + remaining)));
      }
      catch (OverflowException)
      {
        return Unavailable("UNAPPLIED_RECEIPTS", label, "Unapplied receipt totals exceed the supported monetary range.");
      }
    }

    if (unallocated.Count == 0) return Counted("UNAPPLIED_RECEIPTS", label, 0);
    var detail = string.Join("; ", unallocated.OrderBy(pair => pair.Key, StringComparer.Ordinal)
      .Select(pair => $"{pair.Key} {pair.Value.Count.ToString(CultureInfo.InvariantCulture)} receipt(s), {ExactMoney(pair.Value.Amount)} unapplied"));
    return new("UNAPPLIED_RECEIPTS", label, ClientOffboardingStates.ActionRequired,
      unallocated.Values.Sum(value => (long)value.Count), detail);
  }

  private static async Task<ClientOffboardingItem> ApprovedUnbilledTimeItemAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, bool lockedSnapshot, CancellationToken ct)
  {
    const string label = "Approved billable time not yet invoiced";
    var engagementIds = await db.Engagements.AsNoTracking().Where(engagement =>
        engagement.FirmId == actor.FirmId && engagement.PracticeClientId == clientId)
      .Select(engagement => engagement.Id).ToArrayAsync(ct);
    var entries = db.TimeEntries.AsNoTracking().Where(entry => entry.FirmId == actor.FirmId &&
      entry.Status == PracticeTimeStates.TimeApproved && entry.BillableClassification == PracticeTimeStates.Billable &&
      (entry.ClientId == clientId || entry.EngagementId.HasValue && engagementIds.Contains(entry.EngagementId.Value)));
    var count = await entries.CountAsync(ct);
    if (count == 0) return Counted("APPROVED_UNBILLED_TIME", label, 0);
    if (count > OffboardingTimeLimit)
      return Unavailable("APPROVED_UNBILLED_TIME", label, "Approved-time population exceeds the supported 10,000-entry assessment limit.");
    var firstDate = await entries.MinAsync(entry => (DateOnly?)entry.WorkDate, ct);
    var lastDate = await entries.MaxAsync(entry => (DateOnly?)entry.WorkDate, ct);
    if (firstDate is null || lastDate is null || lastDate.Value.DayNumber - firstDate.Value.DayNumber + 1 > 366)
      return Unavailable("APPROVED_UNBILLED_TIME", label,
        "Approved-time history spans more than the supported 366-day report window; no lifetime classification is asserted.");
    if (await entries.AnyAsync(entry => entry.ClientId != clientId || entry.EngagementId.HasValue &&
        !engagementIds.Contains(entry.EngagementId.Value), ct))
      return Unavailable("APPROVED_UNBILLED_TIME", label,
        "Approved-time client or engagement attribution requires finance reconciliation.");

    var request = new FirmUnbilledTimeRequest(firstDate.Value, lastDate.Value, ClientId: clientId, PageSize: 100);
    var result = lockedSnapshot
      ? await FirmUnbilledTimeQuery.GetForLockedSnapshotAsync(db, actor, request, ct)
      : await FirmUnbilledTimeQuery.GetAsync(db, actor, request, ct);
    if (!result.Succeeded || result.Value is null)
      return Unavailable("APPROVED_UNBILLED_TIME", label,
        result.ErrorCode == ErrorCodes.ScopeDenied ? "Your finance access no longer includes this client's approved-time evidence."
          : "Approved-time billing links could not be fully classified.");
    if (!long.TryParse(result.Value.TotalEntries, NumberStyles.None, CultureInfo.InvariantCulture, out var reportedCount) ||
        reportedCount != count)
      return Unavailable("APPROVED_UNBILLED_TIME", label, "Approved-time population changed or is incomplete for this assessment.");

    var subtotals = new List<(string Bucket, string FeeBasis, long Count)>();
    foreach (var subtotal in result.Value.Subtotals)
    {
      if (!long.TryParse(subtotal.EntryCount, NumberStyles.None, CultureInfo.InvariantCulture, out var subtotalCount) ||
          subtotalCount < 0 || subtotal.Bucket is not (FirmUnbilledTimeBuckets.Unallocated or FirmUnbilledTimeBuckets.Reserved or
            FirmUnbilledTimeBuckets.Invoiced or FirmUnbilledTimeBuckets.ReviewRequired))
        return Unavailable("APPROVED_UNBILLED_TIME", label, "Approved-time classification contains an unsupported category.");
      subtotals.Add((subtotal.Bucket, subtotal.FeeBasis, subtotalCount));
    }
    if (subtotals.Sum(item => item.Count) != count)
      return Unavailable("APPROVED_UNBILLED_TIME", label, "Approved-time category totals do not reconcile to the observed population.");

    var unresolved = subtotals.Where(item => item.Bucket is FirmUnbilledTimeBuckets.Unallocated or
      FirmUnbilledTimeBuckets.Reserved or FirmUnbilledTimeBuckets.ReviewRequired).ToArray();
    var unresolvedCount = unresolved.Sum(item => item.Count);
    var detailParts = unresolved.OrderBy(item => item.Bucket, StringComparer.Ordinal)
      .ThenBy(item => item.FeeBasis, StringComparer.Ordinal)
      .Select(item => $"{item.Bucket}/{item.FeeBasis} {item.Count.ToString(CultureInfo.InvariantCulture)}").ToList();
    var invoicedCount = subtotals.Where(item => item.Bucket == FirmUnbilledTimeBuckets.Invoiced).Sum(item => item.Count);
    if (invoicedCount > 0)
      detailParts.Add($"INVOICED {invoicedCount.ToString(CultureInfo.InvariantCulture)} (excluded from unbilled handover count)");
    return new("APPROVED_UNBILLED_TIME", label,
      unresolvedCount > 0 ? ClientOffboardingStates.ActionRequired : ClientOffboardingStates.NoFindings,
      unresolvedCount, detailParts.Count == 0 ? null : string.Join("; ", detailParts));
  }

  private static async Task<ClientOffboardingSection> FeeMilestoneSectionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct)
  {
    const string label = "Unresolved fee-agreement milestones";
    var request = new AuthorizationRequest(actor.FirmId, ClientId: clientId,
      RequiredRoles: OffboardingFinanceRoles, InternalOnly: true);
    if (!await CanReadAsync(db, actor, request, ct))
      return UnavailableFeeMilestonesSection("Your access does not include firm finance for this client.");

    var agreements = await db.EngagementFeeAgreements.AsNoTracking()
      .Where(agreement => agreement.FirmId == actor.FirmId && agreement.PracticeClientId == clientId)
      .OrderBy(agreement => agreement.Id).Select(agreement => agreement.Id)
      .Take(OffboardingFeeAgreementLimit + 1).ToListAsync(ct);
    if (agreements.Count > OffboardingFeeAgreementLimit)
      return Section("FEE_MILESTONES", "Fee agreement", "/app/finance/fee-agreements",
        Unavailable("FEE_MILESTONES", label, "The fee-agreement population exceeds the supported assessment limit."));
    if (agreements.Count == 0)
      return Section("FEE_MILESTONES", "Fee agreement", "/app/finance/fee-agreements",
        Counted("FEE_MILESTONES", label, 0));

    var observations = new List<(string Currency, string State, decimal Amount)>();
    var reconciliationFindings = new List<string>();
    foreach (var agreementId in agreements)
    {
      var result = await FeeSettlementWorkspaceQuery.GetAsync(db, actor, agreementId,
        automaticDraftingEnabled: false, ct: ct);
      var workspace = result.Value;
      if (!result.Succeeded || workspace is null || workspace.AgreementId != agreementId ||
          workspace.ClientId != clientId || !IsCurrency(workspace.Currency) || workspace.Milestones.Count != 2)
        return Section("FEE_MILESTONES", "Fee agreement", "/app/finance/fee-agreements",
          Unavailable("FEE_MILESTONES", label, "A fee agreement or its milestone source links require finance reconciliation."));

      foreach (var milestone in workspace.Milestones)
      {
        if (milestone.State is not (FeeMilestoneStates.Planned or FeeMilestoneStates.Invoiced or FeeMilestoneStates.Paid))
          return Section("FEE_MILESTONES", "Fee agreement", "/app/finance/fee-agreements",
            Unavailable("FEE_MILESTONES", label, "A fee milestone has an unsupported settlement state."));
        var amountText = milestone.State == FeeMilestoneStates.Planned ? milestone.Amount : milestone.Outstanding;
        if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount < 0m || MoneyPolicy.Normalize(amount) != amount ||
            milestone.State == FeeMilestoneStates.Paid && amount != 0m)
          return Section("FEE_MILESTONES", "Fee agreement", "/app/finance/fee-agreements",
            Unavailable("FEE_MILESTONES", label, "Fee milestone amounts could not be reconciled safely."));
        observations.Add((workspace.Currency.ToUpperInvariant(), milestone.State, amount));
      }
      if (workspace.AdvancePreparation.ReconciliationRequired)
      {
        var advance = workspace.Milestones.SingleOrDefault(item => item.Kind == FeeMilestoneKinds.Advance);
        if (advance is null || string.IsNullOrWhiteSpace(advance.InvoiceNumber) ||
            string.IsNullOrWhiteSpace(advance.InvoiceStatus))
          return Section("FEE_MILESTONES", "Fee agreement", "/app/finance/fee-agreements",
            Unavailable("FEE_MILESTONES", label,
              "A retained advance invoice has incomplete source identity for reconciliation."));

        reconciliationFindings.Add(
          $"Agreement {agreementId:D}, advance milestone {advance.Id:D}, retained invoice {advance.InvoiceNumber} ({advance.InvoiceStatus}): milestone linkage requires reconciliation.");
      }
    }

    var pending = observations.Where(item => item.State is FeeMilestoneStates.Planned or FeeMilestoneStates.Invoiced).ToArray();
    if (pending.Length == 0 && reconciliationFindings.Count == 0)
      return Section("FEE_MILESTONES", "Fee agreement", "/app/finance/fee-agreements",
        Counted("FEE_MILESTONES", label, 0));
    string detail;
    try
    {
      detail = string.Join("; ", observations.GroupBy(item => item.Currency)
        .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group =>
        {
          var planned = group.Where(item => item.State == FeeMilestoneStates.Planned).ToArray();
          var invoiced = group.Where(item => item.State == FeeMilestoneStates.Invoiced).ToArray();
          var paid = group.Count(item => item.State == FeeMilestoneStates.Paid);
          var parts = new List<string>();
          if (planned.Length > 0)
            parts.Add($"PLANNED {planned.Length.ToString(CultureInfo.InvariantCulture)} ({ExactMoney(planned.Sum(item => item.Amount))}; not posted debt)");
          if (invoiced.Length > 0)
            parts.Add($"INVOICED {invoiced.Length.ToString(CultureInfo.InvariantCulture)} ({ExactMoney(invoiced.Sum(item => item.Amount))} owner-calculated outstanding; do not add to invoice ageing)");
          if (paid > 0) parts.Add($"PAID {paid.ToString(CultureInfo.InvariantCulture)}");
          return $"{group.Key}: {string.Join(", ", parts)}";
        }));
    }
    catch (OverflowException)
    {
      return Section("FEE_MILESTONES", "Fee agreement", "/app/finance/fee-agreements",
        Unavailable("FEE_MILESTONES", label, "Fee milestone totals exceed the supported monetary range."));
    }
    var items = new List<ClientOffboardingItem>();
    if (pending.Length > 0)
      items.Add(new ClientOffboardingItem("FEE_MILESTONES", label, ClientOffboardingStates.ActionRequired,
        pending.LongLength, detail));
    if (reconciliationFindings.Count > 0)
      items.Add(new ClientOffboardingItem("FEE_RECONCILIATION",
        "Advance invoice awaiting milestone reconciliation", ClientOffboardingStates.ActionRequired,
        reconciliationFindings.Count, string.Join("; ", reconciliationFindings)));
    return Section("FEE_MILESTONES", "Fee agreement", "/app/finance/fee-agreements", items.ToArray());
  }

  private static bool IsCurrency(string? currency) => currency is { Length: 3 } &&
    currency.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

  private static string ExactMoney(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
