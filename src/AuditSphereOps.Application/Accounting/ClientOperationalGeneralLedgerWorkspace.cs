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
  string Basis, int Page, int PageSize, int TotalEntries, IReadOnlyList<ClientOperationalLedgerAccountView> Accounts,
  IReadOnlyList<ClientOperationalLedgerEntryView> Entries, ClientOperationalTrialBalanceView TrialBalance);

/// <summary>Read-only GL projection of posted native client journals. It is not an imported or adjusted audit ledger.</summary>
public static class ClientOperationalGeneralLedgerWorkspace
{
  private static readonly string[] ReaderRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<ClientOperationalGeneralLedgerView>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid periodId, int page = 0, int pageSize = 100, CancellationToken ct = default, DateOnly? fromDate = null, DateOnly? toDate = null, bool includeZeroAccounts = false)
  {
    if (page is < 0 or > 10000 || pageSize is < 1 or > 200)
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a supported ledger page and page size.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: ReaderRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<ClientOperationalGeneralLedgerView>.Fail(auth.ErrorCode!, "Access denied.");
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct);
    if (profile is null || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.GateBlocked, "The current accepted native bookkeeping service is required.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == periodId, ct);
    if (period is null)
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var rangeStart = fromDate ?? period.StartDate;
    var rangeEnd = toDate ?? period.EndDate;
    if (rangeStart < period.StartDate || rangeEnd > period.EndDate || rangeStart > rangeEnd)
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose dates within the selected reporting period.");

    await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    var posted = from line in db.ClientOperationalJournalLines.AsNoTracking()
      join journal in db.ClientOperationalJournals.AsNoTracking()
        on new { line.FirmId, line.ClientId, Id = line.JournalId } equals new { journal.FirmId, journal.ClientId, Id = journal.Id }
      where line.FirmId == actor.FirmId && line.ClientId == clientId && journal.PeriodId == periodId && journal.Status == "POSTED"
      select new { Line = line, Journal = journal };
    var periodPosted = posted.Where(x => x.Journal.PostingDate >= rangeStart && x.Journal.PostingDate <= rangeEnd);
    var balanceRows = await posted.Where(x => x.Journal.PostingDate <= rangeEnd)
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
      foreach (var account in chartAccounts.Where(x => !balances.Any(b => b.AccountId == x.Id)))
        balances.Add(BalanceRow(account.Id, account.AccountCode, account.AccountName, 0m, 0m, 0m));
    }
    if (!includeZeroAccounts) balances.RemoveAll(x => x.OpeningDebit == "0" && x.OpeningCredit == "0" && x.PeriodDebit == "0" && x.PeriodCredit == "0");
    var tbRows = balances.OrderBy(x => x.AccountCode).ThenBy(x => x.AccountId).ToArray();
    string Sum(Func<ClientOperationalTrialBalanceRow, string> selector) => Format(tbRows.Sum(x => decimal.Parse(selector(x), System.Globalization.CultureInfo.InvariantCulture)));
    var trialBalance = new ClientOperationalTrialBalanceView(rangeStart.ToString("yyyy-MM-dd"), rangeEnd.ToString("yyyy-MM-dd"),
      "NATIVE_POSTED_PERIOD_ACTIVITY", Sum(x => x.OpeningDebit), Sum(x => x.OpeningCredit), Sum(x => x.PeriodDebit),
      Sum(x => x.PeriodCredit), Sum(x => x.ClosingDebit), Sum(x => x.ClosingCredit), tbRows);
    var total = await periodPosted.CountAsync(ct);
    var accountRows = await periodPosted.GroupBy(x => new { x.Line.ClientAccountId, x.Line.AccountCode, x.Line.AccountName })
      .Select(g => new
      {
        g.Key.ClientAccountId, g.Key.AccountCode, g.Key.AccountName,
        Debit = g.Sum(x => x.Line.Debit), Credit = g.Sum(x => x.Line.Credit)
      }).OrderBy(x => x.AccountCode).ThenBy(x => x.ClientAccountId).ToListAsync(ct);
    var entryRows = await periodPosted.OrderBy(x => x.Journal.PostingDate).ThenBy(x => x.Journal.JournalNumber)
      .ThenBy(x => x.Line.LineNumber).Skip(page * pageSize).Take(pageSize)
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
    if (!await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.GateBlocked, "The bookkeeping service decision changed while loading the ledger.");
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
      period.Currency, period.Basis, page, pageSize, total, accounts, entries, trialBalance));
  }
  private static string Format(decimal value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
  private static ClientOperationalTrialBalanceRow BalanceRow(Guid id, string code, string name, decimal opening, decimal debit, decimal credit)
  {
    var closing = opening + debit - credit;
    return new(id, code, name, Format(Math.Max(opening, 0m)), Format(Math.Max(-opening, 0m)),
      Format(debit), Format(credit), Format(Math.Max(closing, 0m)), Format(Math.Max(-closing, 0m)));
  }
}
