using System.Globalization;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record FirmReceivableAgingRow(
  Guid InvoiceId, Guid ClientId, string LegalClientName, Guid? EngagementId, string? EngagementName,
  string InvoiceType, string InvoiceNumber, DateOnly? DueDate, decimal OriginalAmount,
  decimal AppliedReceipts, decimal ReversedReceipts, decimal AppliedCredits, decimal Outstanding,
  int? DaysOverdue, string Bucket, string Currency, string PaymentTermsStatus,
  IReadOnlyList<string> FinanceRecipients);

public sealed record FirmReceivableAgingSubtotal(
  Guid? ClientId, string? LegalClientName, Guid? EngagementId, string? EngagementName,
  string Currency, int InvoiceCount,
  decimal OriginalAmount, decimal AppliedReceipts, decimal ReversedReceipts, decimal AppliedCredits, decimal Outstanding);

public sealed record FirmReceivablesAgingReport(
  DateOnly AsOfDate, string DateBasis, string BucketPolicy,
  IReadOnlyList<FirmReceivableAgingRow> Rows,
  IReadOnlyList<FirmReceivableAgingSubtotal> ClientCurrencySubtotals,
  IReadOnlyList<FirmReceivableAgingSubtotal> EngagementCurrencySubtotals,
  IReadOnlyList<FirmReceivableAgingSubtotal> CurrencySubtotals);

public sealed record FirmReceivablesAgingExport(string FileName, string Csv);

/// <summary>
/// Firm-only fee receivables report. Historical balances use immutable posting, receipt-allocation and credit dates;
/// terms are selected from the last independently approved revision available on the requested UTC as-of date.
/// </summary>
public static class FirmReceivablesAgingQuery
{
  private const int MaxInvoices = 10_000;
  private const int MaxTermsRevisions = 50_000;
  private static readonly string[] FinanceRoles = ["FinanceManager", "FinanceReviewer"];

  public static async Task<CommandResult<FirmReceivablesAgingReport>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, DateOnly asOfDate, CancellationToken ct = default)
  {
    if (asOfDate == DateOnly.MaxValue)
      return CommandResult<FirmReceivablesAgingReport>.Fail("finance.as-of-invalid", "Choose a valid report date.");
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<FirmReceivablesAgingReport>.Fail(auth.ErrorCode!, auth.Message!);

    var cutoff = new DateTimeOffset(asOfDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    var invoiceRows = await (from invoice in db.Invoices.AsNoTracking()
      join account in db.BillingAccounts.AsNoTracking()
        on new { invoice.FirmId, invoice.BillingAccountId } equals new { account.FirmId, BillingAccountId = account.Id }
      join client in db.PracticeClients.AsNoTracking()
        on new { account.FirmId, account.PracticeClientId } equals new { client.FirmId, PracticeClientId = client.Id }
      where invoice.FirmId == actor.FirmId && invoice.PostedAt != null && invoice.PostedAt < cutoff &&
        (invoice.Status == BillingStates.InvoicePosted || invoice.Status == BillingStates.InvoiceSent ||
         (invoice.Status == BillingStates.InvoiceCancelled && invoice.CancelledAt >= cutoff))
      orderby invoice.PostedAt, invoice.Id
      select new
      {
        invoice.Id, account.PracticeClientId, ClientName = client.LegalName,
        Currency = invoice.Currency ?? account.Currency,
        invoice.InvoiceNumber, invoice.Total, invoice.Status, invoice.CancelledAt
      }).Take(MaxInvoices + 1).ToListAsync(ct);
    if (invoiceRows.Count > MaxInvoices)
      return CommandResult<FirmReceivablesAgingReport>.Fail("finance.report-limit",
        "This report exceeds the 10,000-invoice interactive limit. Narrow the report date range and try again.");

    var invoiceIds = invoiceRows.Select(x => x.Id).ToArray();
    var clientIds = invoiceRows.Select(x => x.PracticeClientId).Distinct().ToArray();
    var termsRows = invoiceIds.Length == 0 ? [] : await db.InvoicePaymentTermsRevisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && invoiceIds.Contains(x.InvoiceId) && x.SubmittedAt < cutoff)
      .OrderBy(x => x.InvoiceId).ThenBy(x => x.Revision)
      .Take(MaxTermsRevisions + 1)
      .Select(x => new
      {
        x.InvoiceId, x.Revision, x.DueDate, x.Status, x.SubmittedAt, x.ReviewedAt
      }).ToListAsync(ct);
    if (termsRows.Count > MaxTermsRevisions)
      return CommandResult<FirmReceivablesAgingReport>.Fail("finance.report-limit",
        "This report has too much retained payment-terms history to calculate safely. Contact a firm administrator.");

    var termByInvoice = termsRows.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g =>
    {
      var revisions = g.OrderBy(x => x.Revision).ToArray();
      var latest = revisions[^1];
      var approved = revisions.Where(x => x.Status == InvoicePaymentTermsStates.Approved && x.ReviewedAt < cutoff)
        .OrderByDescending(x => x.Revision).FirstOrDefault();
      var status = latest.ReviewedAt is null || latest.ReviewedAt >= cutoff
        ? InvoicePaymentTermsStates.PendingReview
        : latest.Status;
      return (DueDate: approved?.DueDate, Status: status);
    });

    var receiptRows = invoiceIds.Length == 0 ? [] : await (from allocation in db.ReceiptAllocations.AsNoTracking()
      join receipt in db.Receipts.AsNoTracking()
        on new { allocation.FirmId, allocation.ReceiptId } equals new { receipt.FirmId, ReceiptId = receipt.Id }
      where allocation.FirmId == actor.FirmId && invoiceIds.Contains(allocation.InvoiceId) &&
        allocation.CreatedAt < cutoff && receipt.Status == BillingStates.ReceiptRecorded && receipt.ReceivedAt < cutoff
      group allocation by allocation.InvoiceId into allocations
      select new { InvoiceId = allocations.Key, Amount = allocations.Sum(x => x.Amount) }).ToListAsync(ct);
    var appliedReceipts = receiptRows.ToDictionary(x => x.InvoiceId, x => x.Amount);
    var reversalRows = invoiceIds.Length == 0 ? [] : await (
      from reversal in db.ReceiptAllocationReversals.AsNoTracking()
      join allocation in db.ReceiptAllocations.AsNoTracking()
        on new { reversal.FirmId, reversal.ReceiptAllocationId } equals new { allocation.FirmId, ReceiptAllocationId = allocation.Id }
      join receipt in db.Receipts.AsNoTracking()
        on new { allocation.FirmId, allocation.ReceiptId } equals new { receipt.FirmId, ReceiptId = receipt.Id }
      where reversal.FirmId == actor.FirmId && invoiceIds.Contains(allocation.InvoiceId) &&
        reversal.Status == ReceiptAllocationReversalStates.Approved && reversal.ReviewedAt < cutoff &&
        allocation.CreatedAt < cutoff && receipt.Status == BillingStates.ReceiptRecorded && receipt.ReceivedAt < cutoff
      group reversal by allocation.InvoiceId into reversals
      select new { InvoiceId = reversals.Key, Amount = reversals.Sum(x => x.Amount) }).ToListAsync(ct);
    var reversedReceipts = reversalRows.ToDictionary(x => x.InvoiceId, x => x.Amount);
    var creditRows = invoiceIds.Length == 0 ? [] : await db.CreditNotes.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && invoiceIds.Contains(x.InvoiceId) &&
        x.Status == BillingStates.CreditIssued && x.CreatedAt < cutoff)
      .GroupBy(x => x.InvoiceId)
      .Select(g => new { InvoiceId = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
    var appliedCredits = creditRows.ToDictionary(x => x.InvoiceId, x => x.Amount);

    var milestoneLinks = invoiceIds.Length == 0 ? [] : await (
      from line in db.InvoiceLines.AsNoTracking()
      join milestone in db.FeeMilestones.AsNoTracking()
        on new { line.FirmId, MilestoneId = line.SourceId } equals new { milestone.FirmId, MilestoneId = (Guid?)milestone.Id }
      join agreement in db.EngagementFeeAgreements.AsNoTracking()
        on new { milestone.FirmId, milestone.AgreementId } equals new { agreement.FirmId, AgreementId = agreement.Id }
      where line.FirmId == actor.FirmId && invoiceIds.Contains(line.InvoiceId) && line.SourceKind == FeeAgreementService.MilestoneSourceKind
      select new { line.InvoiceId, milestone.Kind, agreement.EngagementId }
    ).ToListAsync(ct);
    var milestoneByInvoice = milestoneLinks.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.First());
    var engagementIds = milestoneLinks.Where(x => x.EngagementId.HasValue).Select(x => x.EngagementId!.Value).Distinct().ToArray();
    var engagementNames = engagementIds.Length == 0 ? new Dictionary<Guid, string>() : await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && engagementIds.Contains(x.Id))
      .Select(x => new { x.Id, Name = x.ServiceRoute + " · " + x.PeriodStart + "–" + x.PeriodEnd })
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    var contacts = clientIds.Length == 0 ? [] : await db.ClientContacts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.PracticeClientId) &&
        (x.ValidFrom == null || x.ValidFrom < cutoff) && (x.ValidTo == null || x.ValidTo >= new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))) &&
        (x.Role.ToUpper().Contains("BILLING") || x.Role.ToUpper().Contains("FINANCE") || x.Role.ToUpper().Contains("ACCOUNTS")))
      .Select(x => new { x.PracticeClientId, x.FullName, x.Email })
      .ToListAsync(ct);
    var contactsByClient = contacts.GroupBy(x => x.PracticeClientId).ToDictionary(g => g.Key,
      g => (IReadOnlyList<string>)g.Select(x => $"{x.FullName} <{x.Email}>").Distinct().Order().ToArray());

    var rows = invoiceRows.Select(invoice =>
    {
      var terms = termByInvoice.GetValueOrDefault(invoice.Id);
      var dueDate = terms.DueDate;
      var receipts = appliedReceipts.GetValueOrDefault(invoice.Id);
      var reversals = reversedReceipts.GetValueOrDefault(invoice.Id);
      var credits = appliedCredits.GetValueOrDefault(invoice.Id);
      var outstanding = Math.Max(0m, invoice.Total - receipts + reversals - credits);
      var daysOverdue = FirmReceivablesAgingPolicy.DaysOverdue(dueDate, asOfDate);
      var bucket = FirmReceivablesAgingPolicy.Bucket(dueDate, asOfDate, outstanding);
      var milestone = milestoneByInvoice.GetValueOrDefault(invoice.Id);
      var type = milestone?.Kind switch
      {
        FeeMilestoneKinds.Advance => "ADVANCE",
        FeeMilestoneKinds.Balance => "FINAL",
        _ => "OTHER"
      };
      return new FirmReceivableAgingRow(invoice.Id, invoice.PracticeClientId, invoice.ClientName,
        milestone?.EngagementId, milestone?.EngagementId is { } engagementId ? engagementNames.GetValueOrDefault(engagementId) : null,
        type, invoice.InvoiceNumber, dueDate, invoice.Total,
        receipts, reversals, credits, outstanding, dueDate is null ? null : daysOverdue,
        bucket, invoice.Currency, terms.Status ?? "NOT_CONFIGURED",
        contactsByClient.GetValueOrDefault(invoice.PracticeClientId, []));
    }).ToArray();

    var clientSubtotals = rows.GroupBy(x => (x.ClientId, x.LegalClientName, x.Currency))
      .OrderBy(x => x.Key.LegalClientName, StringComparer.Ordinal).ThenBy(x => x.Key.Currency, StringComparer.Ordinal)
      .Select(g => Subtotal(g.Key.ClientId, g.Key.LegalClientName, null, null, g.Key.Currency, g)).ToArray();
    var engagementSubtotals = rows.Where(x => x.EngagementId.HasValue)
      .GroupBy(x => (x.ClientId, x.LegalClientName, x.EngagementId, x.EngagementName, x.Currency))
      .OrderBy(x => x.Key.LegalClientName, StringComparer.Ordinal).ThenBy(x => x.Key.EngagementName, StringComparer.Ordinal)
      .ThenBy(x => x.Key.Currency, StringComparer.Ordinal)
      .Select(g => Subtotal(g.Key.ClientId, g.Key.LegalClientName, g.Key.EngagementId,
        g.Key.EngagementName, g.Key.Currency, g)).ToArray();
    var currencySubtotals = rows.GroupBy(x => x.Currency).OrderBy(g => g.Key, StringComparer.Ordinal)
      .Select(g => Subtotal(null, null, null, null, g.Key, g)).ToArray();

    auth = await AuthorizeAsync(db, actor, ct);
    return auth.Succeeded
      ? CommandResult<FirmReceivablesAgingReport>.Ok(new FirmReceivablesAgingReport(asOfDate,
          "UTC invoice posting, receipt received/allocated, approved allocation-reversal review, credit issue, and terms-review dates; exclusive cutoff is midnight after the selected date. Reopened accounting periods do not rewrite these billing event timestamps.",
          "Due on or after the as-of date is Current / Not Yet Due; overdue day 1–30, 31–60, 61–90, and >90. Zero balance is Settled. Invoices without approved terms are undated; a pending or rejected change does not replace the last approved due date. There is no separate billing-dispute state; an outstanding balance remains aged until a credit or allocation reversal is approved. Unallocated cash is excluded. Currency totals are never combined.",
          rows, clientSubtotals, engagementSubtotals, currencySubtotals))
      : CommandResult<FirmReceivablesAgingReport>.Fail(auth.ErrorCode!, auth.Message!);
  }

  public static async Task<CommandResult<FirmReceivablesAgingExport>> ExportAsync(
    IAuditSphereDbContext db, ActorContext actor, DateOnly asOfDate, CancellationToken ct = default)
  {
    var result = await GetAsync(db, actor, asOfDate, ct);
    if (!result.Succeeded || result.Value is null)
      return CommandResult<FirmReceivablesAgingExport>.Fail(result.ErrorCode!, result.Message!);
    var csv = new StringBuilder("As of date,Invoice ID,Legal client,Client ID,Engagement,Engagement ID,Invoice type,Invoice number,Due date,Original amount,Applied receipts,Reversed receipt allocations,Applied credits,Outstanding,Days overdue,Bucket,Currency,Terms status,Finance recipients\r\n");
    foreach (var row in result.Value.Rows)
      csv.AppendJoin(',',
        Cell(asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Cell(row.InvoiceId.ToString("D")),
        Cell(row.LegalClientName), Cell(row.ClientId.ToString("D")), Cell(row.EngagementName ?? ""),
        Cell(row.EngagementId?.ToString("D") ?? ""), Cell(row.InvoiceType), Cell(row.InvoiceNumber),
        Cell(row.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""),
        Number(row.OriginalAmount), Number(row.AppliedReceipts), Number(row.ReversedReceipts), Number(row.AppliedCredits), Number(row.Outstanding),
        Cell(row.DaysOverdue?.ToString(CultureInfo.InvariantCulture) ?? ""), Cell(row.Bucket), Cell(row.Currency),
        Cell(row.PaymentTermsStatus), Cell(string.Join("; ", row.FinanceRecipients))).Append("\r\n");
    foreach (var subtotal in result.Value.ClientCurrencySubtotals)
      csv.AppendJoin(',', Cell(asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Cell("CLIENT CURRENCY TOTAL"),
        Cell(subtotal.LegalClientName ?? ""), Cell(subtotal.ClientId?.ToString("D") ?? ""), Cell(""), Cell(""), Cell(""), Cell(""), Cell(""),
        Number(subtotal.OriginalAmount), Number(subtotal.AppliedReceipts), Number(subtotal.ReversedReceipts), Number(subtotal.AppliedCredits),
        Number(subtotal.Outstanding), Cell(""), Cell(""), Cell(subtotal.Currency), Cell(""), Cell("")).Append("\r\n");
    foreach (var subtotal in result.Value.EngagementCurrencySubtotals)
      csv.AppendJoin(',', Cell(asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Cell("ENGAGEMENT CURRENCY TOTAL"),
        Cell(subtotal.LegalClientName ?? ""), Cell(subtotal.ClientId?.ToString("D") ?? ""), Cell(subtotal.EngagementName ?? ""),
        Cell(subtotal.EngagementId?.ToString("D") ?? ""), Cell(""), Cell(""), Cell(""), Number(subtotal.OriginalAmount),
        Number(subtotal.AppliedReceipts), Number(subtotal.ReversedReceipts), Number(subtotal.AppliedCredits), Number(subtotal.Outstanding), Cell(""), Cell(""),
        Cell(subtotal.Currency), Cell(""), Cell("")).Append("\r\n");
    foreach (var subtotal in result.Value.CurrencySubtotals)
      csv.AppendJoin(',', Cell(asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Cell("CURRENCY TOTAL"),
        Cell(""), Cell(""), Cell(""), Cell(""), Cell(""), Cell(""), Cell(""),
        Number(subtotal.OriginalAmount), Number(subtotal.AppliedReceipts), Number(subtotal.ReversedReceipts), Number(subtotal.AppliedCredits),
        Number(subtotal.Outstanding), Cell(""), Cell(""), Cell(subtotal.Currency), Cell(""), Cell("")).Append("\r\n");
    return CommandResult<FirmReceivablesAgingExport>.Ok(new(
      $"firm-receivables-aging-{asOfDate:yyyy-MM-dd}.csv", csv.ToString()));
  }

  private static FirmReceivableAgingSubtotal Subtotal(
    Guid? clientId, string? clientName, Guid? engagementId, string? engagementName,
    string currency, IEnumerable<FirmReceivableAgingRow> rows)
  {
    var list = rows.ToArray();
    return new(clientId, clientName, engagementId, engagementName, currency, list.Length,
      list.Sum(x => x.OriginalAmount), list.Sum(x => x.AppliedReceipts),
      list.Sum(x => x.ReversedReceipts), list.Sum(x => x.AppliedCredits), list.Sum(x => x.Outstanding));
  }

  private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
  private static string Cell(string value)
  {
    var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
    return "\"" + safe.Replace("\"", "\"\"") + "\"";
  }

  private static Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: FinanceRoles,
        InternalOnly: true, RequireFirmWide: true), ct);
}
