using System.Data;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalLedgerAccountView(Guid AccountId, string AccountCode, string AccountName,
  string DebitMovement, string CreditMovement, string NetMovement);
public sealed record ClientOperationalTrialBalanceRow(Guid AccountId, string AccountCode, string AccountName,
  string OpeningDebit, string OpeningCredit, string PeriodDebit, string PeriodCredit, string ClosingDebit, string ClosingCredit);
public sealed record ClientOperationalTrialBalanceView(string FromDate, string ToDate, string Source,
  string OpeningDebit, string OpeningCredit, string PeriodDebit, string PeriodCredit, string ClosingDebit, string ClosingCredit,
  IReadOnlyList<ClientOperationalTrialBalanceRow> Rows);
public sealed record ClientOperationalLedgerEntryView(Guid JournalId, string JournalNumber, string PostingDate,
  int LineNumber, string AccountCode, string AccountName, string Description, string Debit, string Credit, Guid? ReversesJournalId = null, Guid? ReversedByJournalId = null, string? ReversedByStatus = null);
public sealed record ClientOperationalGeneralLedgerView(Guid ClientId, Guid PeriodId, string PeriodCode, string Currency,
  string Basis, int Page, int PageSize, int TotalEntries, int PeriodTotalEntries, string PostingSnapshotThrough, IReadOnlyList<ClientOperationalLedgerAccountView> Accounts,
  IReadOnlyList<ClientOperationalLedgerEntryView> Entries, ClientOperationalTrialBalanceView TrialBalance, bool BookkeepingActive);

/// <summary>Read-only GL projection of posted native client journals. It is not an imported or adjusted audit ledger.</summary>
public static class ClientOperationalGeneralLedgerWorkspace
{
  private static readonly string[] ReaderRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<ClientOperationalGeneralLedgerView>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid periodId, int page = 0, int pageSize = 100, CancellationToken ct = default,
    DateOnly? fromDate = null, DateOnly? toDate = null, bool includeZeroAccounts = false, long? postingSnapshotThrough = null,
    string? accountCodeFrom = null, string? accountCodeTo = null, string? sourceType = null, string? reference = null, Guid? counterpartyId = null)
  {
    accountCodeFrom = NormalizeFilter(accountCodeFrom);
    accountCodeTo = NormalizeFilter(accountCodeTo);
    sourceType = NormalizeFilter(sourceType)?.ToUpperInvariant();
    reference = NormalizeFilter(reference);
    if (page is < 0 or > 10000 || pageSize is < 1 or > 200 ||
        accountCodeFrom?.Length > 100 || accountCodeTo?.Length > 100 || reference?.Length > 100 ||
        (accountCodeFrom is not null && accountCodeTo is not null && string.CompareOrdinal(accountCodeFrom, accountCodeTo) > 0) ||
        (sourceType is not null && sourceType is not ("NATIVE_JOURNAL" or "REVERSAL" or "SALES_INVOICE" or "PURCHASE_INVOICE" or
          "SALES_CREDIT_NOTE" or "PURCHASE_CREDIT_NOTE" or "SALES_RECEIPT" or "SUPPLIER_PAYMENT")))
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a supported ledger page and page size.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: ReaderRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<ClientOperationalGeneralLedgerView>.Fail(auth.ErrorCode!, "Access denied.");
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct);
    if (profile is null)
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.GateBlocked, "This client has no configured native bookkeeping workspace.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == periodId, ct);
    if (period is null)
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var rangeStart = fromDate ?? period.StartDate;
    var rangeEnd = toDate ?? period.EndDate;
    if (rangeStart < period.StartDate || rangeEnd > period.EndDate || rangeStart > rangeEnd)
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose dates within the selected reporting period.");

    await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    var latestPostingSequence = await db.ClientOperationalJournals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.PeriodId == periodId && x.Status == "POSTED")
      .Select(x => x.PostingSequence).MaxAsync(ct) ?? 0L;
    if ((page > 0 && postingSnapshotThrough is null) || postingSnapshotThrough is < 0 ||
        (postingSnapshotThrough.HasValue && postingSnapshotThrough.Value > latestPostingSequence))
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Refresh the first ledger page to establish a valid posting snapshot before paging.");
    var snapshotThrough = postingSnapshotThrough ?? latestPostingSequence;
    var posted = from line in db.ClientOperationalJournalLines.AsNoTracking()
      join journal in db.ClientOperationalJournals.AsNoTracking()
        on new { line.FirmId, line.ClientId, Id = line.JournalId } equals new { journal.FirmId, journal.ClientId, Id = journal.Id }
      where line.FirmId == actor.FirmId && line.ClientId == clientId && journal.PeriodId == periodId &&
        journal.Status == "POSTED" && journal.PostingSequence <= snapshotThrough
      select new { Line = line, Journal = journal };
    var accountScoped = posted;
    if (accountCodeFrom is not null) accountScoped = accountScoped.Where(x => string.Compare(x.Line.AccountCode, accountCodeFrom) >= 0);
    if (accountCodeTo is not null) accountScoped = accountScoped.Where(x => string.Compare(x.Line.AccountCode, accountCodeTo) <= 0);
    var unfilteredPeriodPosted = posted.Where(x => x.Journal.PostingDate >= rangeStart && x.Journal.PostingDate <= rangeEnd);
    var allPeriodPosted = accountScoped.Where(x => x.Journal.PostingDate >= rangeStart && x.Journal.PostingDate <= rangeEnd);
    var periodPosted = allPeriodPosted;
    if (reference is not null) periodPosted = periodPosted.Where(x =>
      x.Journal.JournalNumber.Contains(reference) || x.Journal.Description.Contains(reference) ||
      db.ClientSalesInvoiceSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id &&
        db.ClientSalesInvoiceDrafts.Any(d => d.FirmId == actor.FirmId && d.ClientId == clientId && d.Id == s.DraftId && (d.DraftReference.Contains(reference) || d.SourceReference.Contains(reference)))) ||
      db.ClientPurchaseInvoiceSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.SupplierInvoiceReference.Contains(reference)) ||
      db.ClientSalesCreditNoteSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.CreditNoteReference.Contains(reference)) ||
      db.ClientPurchaseCreditNoteSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.CreditNoteReference.Contains(reference)) ||
      db.ClientManualSettlementOrigins.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id &&
        (s.Reference.Contains(reference) || s.EvidenceReference.Contains(reference))) ||
      db.ClientOperationalJournalReversals.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.ReversalJournalId == x.Journal.Id &&
        (s.Reason.Contains(reference) || s.EvidenceReference.Contains(reference))));
    if (sourceType is not null)
    {
      periodPosted = sourceType switch
      {
        "REVERSAL" => periodPosted.Where(x => db.ClientOperationalJournalReversals.Any(r => r.FirmId == actor.FirmId && r.ClientId == clientId && r.ReversalJournalId == x.Journal.Id)),
        "SALES_INVOICE" => periodPosted.Where(x => db.ClientSalesInvoiceSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id)),
        "PURCHASE_INVOICE" => periodPosted.Where(x => db.ClientPurchaseInvoiceSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id)),
        "SALES_CREDIT_NOTE" => periodPosted.Where(x => db.ClientSalesCreditNoteSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id)),
        "PURCHASE_CREDIT_NOTE" => periodPosted.Where(x => db.ClientPurchaseCreditNoteSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id)),
        "SALES_RECEIPT" => periodPosted.Where(x => db.ClientManualSettlementOrigins.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.SourceKind == "SALES_RECEIPT")),
        "SUPPLIER_PAYMENT" => periodPosted.Where(x => db.ClientManualSettlementOrigins.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.SourceKind == "SUPPLIER_PAYMENT")),
        "NATIVE_JOURNAL" => periodPosted.Where(x =>
          !db.ClientOperationalJournalReversals.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.ReversalJournalId == x.Journal.Id) &&
          !db.ClientSalesInvoiceSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id) &&
          !db.ClientPurchaseInvoiceSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id) &&
          !db.ClientSalesCreditNoteSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id) &&
          !db.ClientPurchaseCreditNoteSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id) &&
          !db.ClientManualSettlementOrigins.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id)),
        _ => periodPosted
      };
    }
    if (counterpartyId.HasValue) periodPosted = periodPosted.Where(x =>
      db.ClientSalesInvoiceOpenItems.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.CustomerId == counterpartyId.Value) ||
      db.ClientPurchaseInvoiceOpenItems.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.SupplierId == counterpartyId.Value) ||
      db.ClientSalesCreditNoteSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.CustomerId == counterpartyId.Value) ||
      db.ClientPurchaseCreditNoteSubmissions.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.SupplierId == counterpartyId.Value) ||
      db.ClientManualSettlementOrigins.Any(s => s.FirmId == actor.FirmId && s.ClientId == clientId && s.JournalId == x.Journal.Id && s.CounterpartyId == counterpartyId.Value));
    var balanceRows = await accountScoped.Where(x => x.Journal.PostingDate <= rangeEnd)
      .GroupBy(x => new { x.Line.ClientAccountId, x.Line.AccountCode, x.Line.AccountName })
      .Select(g => new { g.Key.ClientAccountId, g.Key.AccountCode, g.Key.AccountName,
        Opening = g.Sum(x => x.Journal.PostingDate < rangeStart ? x.Line.Debit - x.Line.Credit : 0m),
        Debit = g.Sum(x => x.Journal.PostingDate >= rangeStart ? x.Line.Debit : 0m),
        Credit = g.Sum(x => x.Journal.PostingDate >= rangeStart ? x.Line.Credit : 0m) }).ToListAsync(ct);
    var balances = balanceRows.Select(x => BalanceRow(x.ClientAccountId, x.AccountCode, x.AccountName, x.Opening, x.Debit, x.Credit)).ToList();
    if (includeZeroAccounts)
    {
      var chartAccounts = await (from account in db.ClientAccounts.AsNoTracking()
        join chart in db.ClientChartVersions.AsNoTracking() on account.ChartVersionId equals chart.Id
        where account.FirmId == actor.FirmId && account.ClientId == clientId && chart.FirmId == actor.FirmId && chart.ClientId == clientId &&
          chart.Status == AccountingWorkflowStates.Approved && chart.EffectiveFrom <= rangeEnd && (chart.EffectiveTo == null || chart.EffectiveTo >= rangeStart)
        select new { account.Id, account.AccountCode, account.AccountName }).ToListAsync(ct);
      foreach (var account in chartAccounts.Where(x => (accountCodeFrom is null || string.CompareOrdinal(x.AccountCode, accountCodeFrom) >= 0) &&
        (accountCodeTo is null || string.CompareOrdinal(x.AccountCode, accountCodeTo) <= 0) && !balances.Any(b => b.AccountId == x.Id)))
        balances.Add(BalanceRow(account.Id, account.AccountCode, account.AccountName, 0m, 0m, 0m));
    }
    if (!includeZeroAccounts) balances.RemoveAll(x => x.OpeningDebit == "0" && x.OpeningCredit == "0" && x.PeriodDebit == "0" && x.PeriodCredit == "0");
    var tbRows = balances.OrderBy(x => x.AccountCode).ThenBy(x => x.AccountId).ToArray();
    string Sum(Func<ClientOperationalTrialBalanceRow, string> selector) => Format(tbRows.Sum(x => decimal.Parse(selector(x), System.Globalization.CultureInfo.InvariantCulture)));
    var trialBalance = new ClientOperationalTrialBalanceView(rangeStart.ToString("yyyy-MM-dd"), rangeEnd.ToString("yyyy-MM-dd"),
      "NATIVE_POSTED_PERIOD_ACTIVITY", Sum(x => x.OpeningDebit), Sum(x => x.OpeningCredit), Sum(x => x.PeriodDebit),
      Sum(x => x.PeriodCredit), Sum(x => x.ClosingDebit), Sum(x => x.ClosingCredit), tbRows);
    var total = await periodPosted.CountAsync(ct);
    var periodTotal = await unfilteredPeriodPosted.CountAsync(ct);
    var accountRows = await periodPosted.GroupBy(x => new { x.Line.ClientAccountId, x.Line.AccountCode, x.Line.AccountName })
      .Select(g => new
      {
        g.Key.ClientAccountId, g.Key.AccountCode, g.Key.AccountName,
        Debit = g.Sum(x => x.Line.Debit), Credit = g.Sum(x => x.Line.Credit)
      }).OrderBy(x => x.AccountCode).ThenBy(x => x.ClientAccountId).ToListAsync(ct);
    var entryRows = await periodPosted.OrderBy(x => x.Journal.PostingDate).ThenBy(x => x.Journal.JournalNumber)
      .ThenBy(x => x.Journal.Id).ThenBy(x => x.Line.LineNumber).ThenBy(x => x.Line.Id).Skip(page * pageSize).Take(pageSize)
      .Select(x => new { x.Journal.Id, x.Journal.JournalNumber, x.Journal.PostingDate, x.Line.LineNumber,
        x.Line.AccountCode, x.Line.AccountName, x.Line.Description, x.Line.Debit, x.Line.Credit }).ToListAsync(ct);
    var journalIds = entryRows.Select(x => x.Id).Distinct().ToArray();
    var links = await (from link in db.ClientOperationalJournalReversals.AsNoTracking()
      join reversal in db.ClientOperationalJournals.AsNoTracking()
        on new { link.FirmId, link.ClientId, Id = link.ReversalJournalId } equals new { reversal.FirmId, reversal.ClientId, Id = reversal.Id }
      where link.FirmId == actor.FirmId && link.ClientId == clientId && (journalIds.Contains(link.OriginalJournalId) || journalIds.Contains(link.ReversalJournalId))
      select new { link.OriginalJournalId, link.ReversalJournalId, reversal.Status }).ToListAsync(ct);
    await snapshot.CommitAsync(ct);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: ReaderRoles, InternalOnly: true), ct)).Succeeded)
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var bookkeepingActive = await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct);
    var accounts = accountRows.Select(x => new ClientOperationalLedgerAccountView(x.ClientAccountId, x.AccountCode,
      x.AccountName, x.Debit.ToString(System.Globalization.CultureInfo.InvariantCulture),
      x.Credit.ToString(System.Globalization.CultureInfo.InvariantCulture),
      (x.Debit - x.Credit).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
    var entries = entryRows.Select(x => new ClientOperationalLedgerEntryView(x.Id, x.JournalNumber,
      x.PostingDate.ToString("yyyy-MM-dd"), x.LineNumber, x.AccountCode, x.AccountName, x.Description,
      x.Debit.ToString(System.Globalization.CultureInfo.InvariantCulture),
      x.Credit.ToString(System.Globalization.CultureInfo.InvariantCulture),
      links.SingleOrDefault(l => l.ReversalJournalId == x.Id)?.OriginalJournalId,
      links.SingleOrDefault(l => l.OriginalJournalId == x.Id)?.ReversalJournalId,
      links.SingleOrDefault(l => l.OriginalJournalId == x.Id)?.Status)).ToArray();
    return CommandResult<ClientOperationalGeneralLedgerView>.Ok(new(clientId, periodId, period.PeriodCode,
      period.Currency, period.Basis, page, pageSize, total, periodTotal, snapshotThrough.ToString(System.Globalization.CultureInfo.InvariantCulture), accounts, entries, trialBalance, bookkeepingActive));
  }

  private static string? NormalizeFilter(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
  private static string Format(decimal value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
  private static ClientOperationalTrialBalanceRow BalanceRow(Guid id, string code, string name, decimal opening, decimal debit, decimal credit)
  {
    var closing = opening + debit - credit;
    return new(id, code, name, Format(Math.Max(opening, 0m)), Format(Math.Max(-opening, 0m)),
      Format(debit), Format(credit), Format(Math.Max(closing, 0m)), Format(Math.Max(-closing, 0m)));
  }
}
