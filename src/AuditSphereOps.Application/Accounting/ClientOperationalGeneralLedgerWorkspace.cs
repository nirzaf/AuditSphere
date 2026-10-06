using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalLedgerAccountView(Guid AccountId, string AccountCode, string AccountName,
  string DebitMovement, string CreditMovement, string NetMovement);
public sealed record ClientOperationalLedgerEntryView(Guid JournalId, string JournalNumber, string PostingDate,
  int LineNumber, string AccountCode, string AccountName, string Description, string Debit, string Credit);
public sealed record ClientOperationalGeneralLedgerView(Guid ClientId, Guid PeriodId, string PeriodCode, string Currency,
  string Basis, int Page, int PageSize, int TotalEntries, IReadOnlyList<ClientOperationalLedgerAccountView> Accounts,
  IReadOnlyList<ClientOperationalLedgerEntryView> Entries);

/// <summary>Read-only GL projection of posted native client journals. It is not an imported or adjusted audit ledger.</summary>
public static class ClientOperationalGeneralLedgerWorkspace
{
  private static readonly string[] ReaderRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<ClientOperationalGeneralLedgerView>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid periodId, int page = 0, int pageSize = 100, CancellationToken ct = default)
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

    var posted = from line in db.ClientOperationalJournalLines.AsNoTracking()
      join journal in db.ClientOperationalJournals.AsNoTracking()
        on new { line.FirmId, line.ClientId, Id = line.JournalId } equals new { journal.FirmId, journal.ClientId, Id = journal.Id }
      where line.FirmId == actor.FirmId && line.ClientId == clientId && journal.PeriodId == periodId && journal.Status == "POSTED"
      select new { Line = line, Journal = journal };
    var total = await posted.CountAsync(ct);
    var accountRows = await posted.GroupBy(x => new { x.Line.ClientAccountId, x.Line.AccountCode, x.Line.AccountName })
      .Select(g => new
      {
        g.Key.ClientAccountId, g.Key.AccountCode, g.Key.AccountName,
        Debit = g.Sum(x => x.Line.Debit), Credit = g.Sum(x => x.Line.Credit)
      }).OrderBy(x => x.AccountCode).ThenBy(x => x.ClientAccountId).ToListAsync(ct);
    var entryRows = await posted.OrderBy(x => x.Journal.PostingDate).ThenBy(x => x.Journal.JournalNumber)
      .ThenBy(x => x.Line.LineNumber).Skip(page * pageSize).Take(pageSize)
      .Select(x => new { x.Journal.Id, x.Journal.JournalNumber, x.Journal.PostingDate, x.Line.LineNumber,
        x.Line.AccountCode, x.Line.AccountName, x.Line.Description, x.Line.Debit, x.Line.Credit }).ToListAsync(ct);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: ReaderRoles, InternalOnly: true), ct)).Succeeded)
      return CommandResult<ClientOperationalGeneralLedgerView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var accounts = accountRows.Select(x => new ClientOperationalLedgerAccountView(x.ClientAccountId, x.AccountCode,
      x.AccountName, x.Debit.ToString(System.Globalization.CultureInfo.InvariantCulture),
      x.Credit.ToString(System.Globalization.CultureInfo.InvariantCulture),
      (x.Debit - x.Credit).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
    var entries = entryRows.Select(x => new ClientOperationalLedgerEntryView(x.Id, x.JournalNumber,
      x.PostingDate.ToString("yyyy-MM-dd"), x.LineNumber, x.AccountCode, x.AccountName, x.Description,
      x.Debit.ToString(System.Globalization.CultureInfo.InvariantCulture),
      x.Credit.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
    return CommandResult<ClientOperationalGeneralLedgerView>.Ok(new(clientId, periodId, period.PeriodCode,
      period.Currency, period.Basis, page, pageSize, total, accounts, entries));
  }
}
