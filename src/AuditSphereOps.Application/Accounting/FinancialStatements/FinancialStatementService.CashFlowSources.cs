using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Cash-flow source lineage for a package build (CP-11). Each contribution keeps its accepted general-ledger line's debit-positive
/// ledger amount as provenance. Its statement effect is a separate projection chosen by the movement's role. A cash movement is the
/// counterpart projection of a cash event, so the ledger amount reverses onto the cash flow. A noncash movement keeps the ledger sign.
/// A movement is source-resolved only when its projected contributions total its amount and every cash event it draws on is explained
/// in full. Preparer text is never lineage.
/// </summary>
public static partial class FinancialStatementService
{
  public const int MaxSourceAllocationsPerMovement = 100;

  /// <summary>
  /// The statement effect of a ledger contribution under its projection role. A cash movement projects the reversed ledger sign: a
  /// credited receivable of -100 in a journal with a +100 cash leg is a receipt of +100. A noncash movement, such as a depreciation
  /// add-back or a disclosed acquisition, keeps the ledger sign. The roles are not interchangeable.
  /// </summary>
  internal static decimal ProjectCashFlowContribution(bool isNonCash, decimal ledgerSignedAmount) =>
    isNonCash ? ledgerSignedAmount : -ledgerSignedAmount;

  /// <summary>
  /// Structural checks that need no database: a bounded count, six-decimal non-zero amounts, no source line repeated within one
  /// movement, and no movement whose projected contributions exceed its own amount or run against its sign.
  /// </summary>
  internal static string? ValidateCashFlowSourceAllocationShape(IReadOnlyList<CashFlowLineInput> lines)
  {
    foreach (var line in lines)
    {
      if (line.SourceAllocations is not { } allocations) continue;
      if (allocations.Count > MaxSourceAllocationsPerMovement)
        return $"A cash-flow movement can draw on at most {MaxSourceAllocationsPerMovement} accepted source lines.";
      if (allocations.Any(x => x.GeneralLedgerLineId == Guid.Empty || x.SignedAmount == 0m ||
          MoneyPolicy.Normalize(x.SignedAmount) != x.SignedAmount))
        return "Each source contribution needs an accepted source line and a non-zero six-decimal signed amount.";
      if (allocations.GroupBy(x => x.GeneralLedgerLineId).Any(g => g.Count() != 1))
        return "A source line can contribute to a cash-flow movement only once.";
      var total = MoneyPolicy.Normalize(ProjectCashFlowContribution(line.IsNonCash, allocations.Sum(x => x.SignedAmount)));
      if (total != 0m && (Math.Abs(total) > Math.Abs(line.Amount) || Math.Sign(total) != Math.Sign(line.Amount)))
        return $"Source contributions for '{line.Description.Trim()}' exceed the movement amount or run against its sign.";
    }
    return null;
  }

  /// <summary>
  /// Resolves every requested contribution against the accepted general ledger for the package. Fails closed: an unaccepted,
  /// unsealed, out-of-period or wrong-currency source, a cash-account line, a journal used for the wrong kind of movement, or a source
  /// line over-consumed across the package, refuses the build. A cash event that is only partly linked is not refused: its movements
  /// stay unresolved and the package remains a draft. A package with no requested contribution resolves to an empty list without
  /// touching the accounting workspace.
  /// </summary>
  internal static async Task<CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>> ResolveCashFlowSourceLinksAsync(
    IClientAccountingDbContext? typed, ActorContext actor, FinancialPackage package,
    Guid? periodId, IReadOnlyList<CashFlowLineInput> lines, CancellationToken ct)
  {
    var requested = lines.Select((line, index) => (line, index))
      .Where(x => x.line.SourceAllocations is { Count: > 0 })
      .SelectMany(x => x.line.SourceAllocations!.Select(a => new { Movement = x.index, x.line.IsNonCash, a.GeneralLedgerLineId, a.SignedAmount }))
      .ToArray();
    if (requested.Length == 0)
      return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Ok([]);
    if (typed is null || periodId is null)
      return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(ErrorCodes.GateBlocked,
        "Cash-flow source lines need the accounting workspace and a reporting period.");

    // The ledger is selected for this package's period, so another period's acceptance can neither stand in for it nor block it (CP-10).
    var selected = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(typed, actor, package.ClientId,
      package.EngagementId, periodId.Value, ct);
    if (!selected.Succeeded)
      return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(selected.ErrorCode!, selected.Message!);
    if (selected.Value?.ImportBatchId is not Guid batchId)
      return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(ErrorCodes.GateBlocked,
        "Accept the general ledger for this engagement before linking cash-flow movements to source lines.");
    var period = await typed.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == package.FirmId &&
      x.ClientId == package.ClientId && x.Id == periodId, ct);
    var batch = await typed.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == package.FirmId &&
      x.ClientId == package.ClientId && x.EngagementId == package.EngagementId && x.Id == batchId && x.PeriodId == periodId &&
      x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED", ct);
    if (period is null || batch is null || batch.Currency != package.Currency ||
        !string.Equals(batch.NormalizedDatasetDigest, selected.Value.SourceIdentityHash, StringComparison.OrdinalIgnoreCase))
      return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(ErrorCodes.GateBlocked,
        "The accepted general ledger must match this package's period, currency and sealed source identity.");

    var ids = requested.Select(x => x.GeneralLedgerLineId).Distinct().ToArray();
    var sources = await (from line in typed.GeneralLedgerLines.AsNoTracking()
      join transaction in typed.GeneralLedgerTransactions.AsNoTracking() on line.TransactionId equals transaction.Id
      where line.FirmId == package.FirmId && line.ClientId == package.ClientId && line.EngagementId == package.EngagementId &&
        line.ImportBatchId == batchId && ids.Contains(line.Id) &&
        transaction.FirmId == package.FirmId && transaction.ImportBatchId == batchId &&
        transaction.Currency == batch.Currency && transaction.PostingDate >= period.StartDate && transaction.PostingDate <= period.EndDate
      select new { line.Id, line.TransactionId, line.AccountCode, line.FunctionalAmount, transaction.StableJournalId, transaction.PostingDate })
      .ToListAsync(ct);
    if (sources.Count != ids.Length)
      return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(ErrorCodes.GateBlocked,
        "A cash-flow source line is not in the accepted general ledger for this period, client and engagement.");
    var byId = sources.ToDictionary(x => x.Id);
    // A line on one of the package's own cash accounts is a cash transfer leg, not a source for a movement, so it can never be a source.
    if (requested.Any(x => package.CashAccountCodes.Contains(byId[x.GeneralLedgerLineId].AccountCode, StringComparer.Ordinal)))
      return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "A cash-account line is a cash transfer, not a cash-flow movement. Link the movement to its non-cash counterpart line.");

    // Cash-event membership. A journal moves package cash when one of its lines sits on a package cash account, and its cash effect
    // is the sum of those legs. A cash movement may draw only on such a journal; a noncash movement only on a journal without cash.
    var transactionIds = sources.Select(x => x.TransactionId).Distinct().ToArray();
    var journalLines = await typed.GeneralLedgerLines.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.ImportBatchId == batchId && transactionIds.Contains(x.TransactionId))
      .Select(x => new { x.TransactionId, x.AccountCode, x.FunctionalAmount })
      .ToListAsync(ct);
    var cashEffect = journalLines.Where(x => package.CashAccountCodes.Contains(x.AccountCode, StringComparer.Ordinal))
      .GroupBy(x => x.TransactionId)
      .ToDictionary(g => g.Key, g => MoneyPolicy.Normalize(g.Sum(x => x.FunctionalAmount)));
    foreach (var x in requested)
    {
      var movesCash = cashEffect.ContainsKey(byId[x.GeneralLedgerLineId].TransactionId);
      if (x.IsNonCash && movesCash)
        return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(ErrorCodes.Accounting.PackageInvalid,
          "A noncash movement cannot draw on a journal that moves package cash. Link the cash movement to that journal instead.");
      if (!x.IsNonCash && !movesCash)
        return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(ErrorCodes.Accounting.PackageInvalid,
          "A cash-flow movement must draw on a journal that moves package cash. A journal without a cash leg is a noncash adjustment or disclosure.");
    }

    // Across the whole package a source line may feed several movements, but its contributions must keep its own sign and total no
    // more than its own amount. Exceeding that is over-allocation, which refuses the build.
    foreach (var group in requested.GroupBy(x => x.GeneralLedgerLineId))
    {
      var source = byId[group.Key];
      var total = MoneyPolicy.Normalize(group.Sum(x => x.SignedAmount));
      if (source.FunctionalAmount == 0m || group.Any(x => Math.Sign(x.SignedAmount) != Math.Sign(source.FunctionalAmount)) ||
          Math.Abs(total) > Math.Abs(source.FunctionalAmount))
        return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(ErrorCodes.Accounting.PackageInvalid,
          "A source line is over-allocated across the cash-flow movements or contributes against its own sign.");
    }

    // Population completeness. Across the package, the projected contributions drawn from one cash journal must equal that journal's
    // cash effect. An unlinked or partly linked cash leg is not refused; each movement that depends on it stays unresolved.
    var projectedByJournal = requested.Where(x => !x.IsNonCash)
      .GroupBy(x => byId[x.GeneralLedgerLineId].TransactionId)
      .ToDictionary(g => g.Key, g => MoneyPolicy.Normalize(g.Sum(x => ProjectCashFlowContribution(false, x.SignedAmount))));
    bool JournalExplained(Guid transactionId) =>
      projectedByJournal.TryGetValue(transactionId, out var projected) && cashEffect.TryGetValue(transactionId, out var effect) && projected == effect;

    var resolved = requested.Select(x =>
    {
      var source = byId[x.GeneralLedgerLineId];
      var hash = Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
      {
        Version = "cash-flow-gl-line.v1",
        Source = selected.Value.SourceIdentityHash,
        Batch = batchId,
        Line = source.Id,
        Transaction = source.TransactionId,
        Journal = source.StableJournalId,
        Account = source.AccountCode,
        Amount = source.FunctionalAmount.ToString("0.######", CultureInfo.InvariantCulture),
        PostingDate = source.PostingDate,
        Currency = batch.Currency,
      }));
      return new ResolvedCashFlowSourceLink(x.Movement, source.Id, source.TransactionId, batchId, hash,
        MoneyPolicy.Normalize(x.SignedAmount), batch.Currency, x.IsNonCash || JournalExplained(source.TransactionId));
    }).ToArray();
    return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Ok(resolved);
  }

  /// <summary>
  /// Per movement: true only when it has contributions, every one belongs to an explained cash event, and their projected total equals
  /// its amount exactly. A movement with no contribution, a partial total or an unexplained cash event stays unresolved and keeps the
  /// package a draft.
  /// </summary>
  internal static IReadOnlyList<bool> ResolvedCashFlowMovements(IReadOnlyList<(decimal Amount, bool IsNonCash)> movements,
    IEnumerable<(int MovementIndex, decimal LedgerSignedAmount, bool PopulationComplete)> contributions)
  {
    var totals = contributions.GroupBy(x => x.MovementIndex).ToDictionary(g => g.Key,
      g => (Total: g.Sum(x => x.LedgerSignedAmount), Count: g.Count(), Explained: g.All(x => x.PopulationComplete)));
    return movements.Select((movement, index) => totals.TryGetValue(index, out var t) && t.Count > 0 && t.Explained &&
      MoneyPolicy.Normalize(ProjectCashFlowContribution(movement.IsNonCash, t.Total)) == MoneyPolicy.Normalize(movement.Amount)).ToArray();
  }
}
