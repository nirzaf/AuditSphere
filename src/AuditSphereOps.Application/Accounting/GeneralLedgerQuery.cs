using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// Read-side contracts for sealed general-ledger batches. Typed filters keep every
// predicate server-side and scope-checked so no journey loads a whole batch into a
// circuit; the journal drill-down is the reviewer's explanation of one journal group.
public sealed record GeneralLedgerLineFilter(
  string? AccountCodePrefix = null, DateOnly? PostedFrom = null, DateOnly? PostedTo = null,
  string? StableJournalId = null, string? Counterparty = null);

public sealed record GeneralLedgerLineRow(
  Guid LineId, string StableJournalId, string StableLineId, DateOnly PostingDate,
  string AccountCode, decimal Debit, decimal Credit, decimal FunctionalAmount,
  string OriginalCurrency, decimal OriginalAmount, string Counterparty,
  string Branch, string CostCentre, string Department, string Project);

public sealed record GeneralLedgerLinesPage(
  IReadOnlyList<GeneralLedgerLineRow> Items, int TotalCount, int Page, int PageSize,
  decimal TotalDebit, decimal TotalCredit, bool Balanced);

public sealed record GeneralLedgerJournalLineRow(
  Guid LineId, string StableLineId, string AccountCode, decimal Debit, decimal Credit,
  decimal FunctionalAmount, string OriginalCurrency, decimal OriginalAmount, string Counterparty);

public sealed record GeneralLedgerJournalDetail(
  string StableJournalId, DateOnly PostingDate, string DocumentNumber, string Currency,
  bool IsManual, bool IsYearEnd, string? ReversalReference,
  IReadOnlyList<GeneralLedgerJournalLineRow> Lines,
  int LineCount, decimal TotalDebit, decimal TotalCredit, bool Balanced);

public static class GeneralLedgerQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  /// <summary>Paged GL lines for one sealed batch with typed, server-applied filters
  /// and exact debit/credit totals for the filtered population.</summary>
  public static async Task<CommandResult<GeneralLedgerLinesPage>> GetLinesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid importBatchId,
    GeneralLedgerLineFilter? filter = null, int page = 1, int pageSize = 100,
    CancellationToken ct = default)
  {
    if (importBatchId == Guid.Empty || page < 1 || pageSize is < 1 or > 500)
      return CommandResult<GeneralLedgerLinesPage>.Fail(ErrorCodes.Accounting.ImportRejected,
        "The ledger page request is invalid.");
    var batch = await db.SourceImportBatches.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == importBatchId && x.FirmId == actor.FirmId, ct);
    if (batch is null)
      return CommandResult<GeneralLedgerLinesPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(batch.FirmId, batch.ClientId, batch.EngagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<GeneralLedgerLinesPage>.Fail(auth.ErrorCode!, auth.Message!);
    if (batch.Status != "SEALED")
      return CommandResult<GeneralLedgerLinesPage>.Fail(ErrorCodes.GateBlocked, "Only a sealed GL batch can be read.");

    var joined = db.GeneralLedgerLines.AsNoTracking()
      .Where(x => x.FirmId == batch.FirmId && x.ClientId == batch.ClientId &&
        x.EngagementId == batch.EngagementId && x.ImportBatchId == batch.Id)
      .Join(db.GeneralLedgerTransactions.AsNoTracking()
        .Where(t => t.FirmId == batch.FirmId && t.ClientId == batch.ClientId &&
          t.EngagementId == batch.EngagementId && t.ImportBatchId == batch.Id),
        line => line.TransactionId, transaction => transaction.Id,
        (line, transaction) => new { line, transaction });
    if (filter is not null)
    {
      if (!string.IsNullOrWhiteSpace(filter.AccountCodePrefix))
      {
        var prefix = filter.AccountCodePrefix.Trim();
        joined = joined.Where(x => x.line.AccountCode.StartsWith(prefix));
      }
      if (filter.PostedFrom.HasValue)
        joined = joined.Where(x => x.transaction.PostingDate >= filter.PostedFrom.Value);
      if (filter.PostedTo.HasValue)
        joined = joined.Where(x => x.transaction.PostingDate <= filter.PostedTo.Value);
      if (!string.IsNullOrWhiteSpace(filter.StableJournalId))
        joined = joined.Where(x => x.transaction.StableJournalId == filter.StableJournalId.Trim());
      if (!string.IsNullOrWhiteSpace(filter.Counterparty))
        joined = joined.Where(x => x.line.IntercompanyCounterparty == filter.Counterparty.Trim());
    }

    var totalCount = await joined.CountAsync(ct);
    var totalDebit = totalCount > 0 ? await joined.SumAsync(x => x.line.Debit, ct) : 0m;
    var totalCredit = totalCount > 0 ? await joined.SumAsync(x => x.line.Credit, ct) : 0m;
    var items = await joined
      .OrderBy(x => x.transaction.PostingDate).ThenBy(x => x.transaction.StableJournalId)
      .ThenBy(x => x.line.StableLineId).ThenBy(x => x.line.Id)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .Select(x => new GeneralLedgerLineRow(
        x.line.Id, x.transaction.StableJournalId, x.line.StableLineId, x.transaction.PostingDate,
        x.line.AccountCode, x.line.Debit, x.line.Credit, x.line.FunctionalAmount,
        x.line.OriginalCurrency, x.line.OriginalAmount, x.line.IntercompanyCounterparty,
        x.line.Branch, x.line.CostCentre, x.line.Department, x.line.Project))
      .ToListAsync(ct);

    return CommandResult<GeneralLedgerLinesPage>.Ok(new GeneralLedgerLinesPage(
      items, totalCount, page, pageSize,
      MoneyPolicy.Normalize(totalDebit), MoneyPolicy.Normalize(totalCredit),
      MoneyPolicy.Normalize(totalDebit) == MoneyPolicy.Normalize(totalCredit)));
  }

  /// <summary>Every line of one journal group inside a sealed batch with debit/credit
  /// totals; an unbalanced journal is reported honestly instead of silently hidden.</summary>
  public static async Task<CommandResult<GeneralLedgerJournalDetail>> GetJournalDrillDownAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid importBatchId, string stableJournalId,
    CancellationToken ct = default)
  {
    if (importBatchId == Guid.Empty || string.IsNullOrWhiteSpace(stableJournalId))
      return CommandResult<GeneralLedgerJournalDetail>.Fail(ErrorCodes.Accounting.ImportRejected,
        "A sealed batch and stable journal id are required.");
    var batch = await db.SourceImportBatches.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == importBatchId && x.FirmId == actor.FirmId, ct);
    if (batch is null)
      return CommandResult<GeneralLedgerJournalDetail>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(batch.FirmId, batch.ClientId, batch.EngagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<GeneralLedgerJournalDetail>.Fail(auth.ErrorCode!, auth.Message!);
    if (batch.Status != "SEALED")
      return CommandResult<GeneralLedgerJournalDetail>.Fail(ErrorCodes.GateBlocked, "Only a sealed GL batch can be read.");

    var journal = await db.GeneralLedgerTransactions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == batch.FirmId && x.ClientId == batch.ClientId &&
        x.EngagementId == batch.EngagementId && x.ImportBatchId == batch.Id &&
        x.StableJournalId == stableJournalId.Trim(), ct);
    if (journal is null)
      return CommandResult<GeneralLedgerJournalDetail>.Fail(ErrorCodes.ScopeDenied,
        "The requested journal is not part of this sealed batch.");

    var lines = await db.GeneralLedgerLines.AsNoTracking()
      .Where(x => x.FirmId == journal.FirmId && x.ClientId == journal.ClientId &&
        x.EngagementId == journal.EngagementId && x.ImportBatchId == journal.ImportBatchId &&
        x.TransactionId == journal.Id)
      .OrderBy(x => x.StableLineId).ThenBy(x => x.Id)
      .Select(x => new GeneralLedgerJournalLineRow(
        x.Id, x.StableLineId, x.AccountCode, x.Debit, x.Credit, x.FunctionalAmount,
        x.OriginalCurrency, x.OriginalAmount, x.IntercompanyCounterparty))
      .ToListAsync(ct);
    var totalDebit = MoneyPolicy.Normalize(lines.Sum(x => x.Debit));
    var totalCredit = MoneyPolicy.Normalize(lines.Sum(x => x.Credit));

    return CommandResult<GeneralLedgerJournalDetail>.Ok(new GeneralLedgerJournalDetail(
      journal.StableJournalId, journal.PostingDate, journal.DocumentNumber, journal.Currency,
      journal.IsManual, journal.IsYearEnd, journal.ReversalReference, lines,
      lines.Count, totalDebit, totalCredit, totalDebit == totalCredit));
  }
}
