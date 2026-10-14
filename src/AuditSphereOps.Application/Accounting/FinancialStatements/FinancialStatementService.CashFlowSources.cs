using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Cash-flow source lineage for a package build (CP-11). A movement is source-resolved only when its persisted contributions
/// come from the engagement's accepted general ledger, match the package period, currency and sealed identity, keep each source
/// line's sign, and together equal the movement amount. Preparer text is never lineage.
/// </summary>
public static partial class FinancialStatementService
{
  public const int MaxSourceAllocationsPerMovement = 100;

  /// <summary>
  /// Structural checks that need no database: a bounded count, six-decimal non-zero amounts, no source line repeated within one
  /// movement, and no movement whose contributions exceed its own amount or run against its sign.
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
      var total = MoneyPolicy.Normalize(allocations.Sum(x => x.SignedAmount));
      if (total != 0m && (Math.Abs(total) > Math.Abs(line.Amount) || Math.Sign(total) != Math.Sign(line.Amount)))
        return $"Source contributions for '{line.Description.Trim()}' exceed the movement amount or run against its sign.";
    }
    return null;
  }

  /// <summary>
  /// Resolves every requested contribution against the accepted general ledger for the package. Fails closed: an unaccepted,
  /// unsealed, out-of-period or wrong-currency source, or a source line over-consumed across the package, refuses the build.
  /// A package with no requested contribution resolves to an empty list without touching the accounting workspace.
  /// </summary>
  internal static async Task<CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>> ResolveCashFlowSourceLinksAsync(
    IClientAccountingDbContext? typed, ActorContext actor, FinancialPackage package,
    Guid? periodId, IReadOnlyList<CashFlowLineInput> lines, CancellationToken ct)
  {
    var requested = lines.Select((line, index) => (line, index))
      .Where(x => x.line.SourceAllocations is { Count: > 0 })
      .SelectMany(x => x.line.SourceAllocations!.Select(a => new { Movement = x.index, a.GeneralLedgerLineId, a.SignedAmount }))
      .ToArray();
    if (requested.Length == 0)
      return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Ok([]);
    if (typed is null || periodId is null)
      return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Fail(ErrorCodes.GateBlocked,
        "Cash-flow source lines need the accounting workspace and a reporting period.");

    var selected = await AccountingSourceAcceptanceService.GetSelectedSourceAsync(typed, actor, package.ClientId, package.EngagementId,
      AccountingSourceKinds.GeneralLedger, ct);
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

    // Across the whole package a source line may feed several movements, but its contributions must keep its sign and total no
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
        MoneyPolicy.Normalize(x.SignedAmount), batch.Currency);
    }).ToArray();
    return CommandResult<IReadOnlyList<ResolvedCashFlowSourceLink>>.Ok(resolved);
  }

  /// <summary>
  /// Per movement: true only when it has contributions and they total exactly its amount. A movement with no contribution, or
  /// a partial total, stays unresolved and keeps the package a draft.
  /// </summary>
  internal static IReadOnlyList<bool> ResolvedCashFlowMovements(IReadOnlyList<decimal> amounts,
    IEnumerable<(int MovementIndex, decimal SignedAmount)> contributions)
  {
    var totals = contributions.GroupBy(x => x.MovementIndex).ToDictionary(g => g.Key, g => (Total: g.Sum(x => x.SignedAmount), Count: g.Count()));
    return amounts.Select((amount, index) => totals.TryGetValue(index, out var t) && t.Count > 0 &&
      MoneyPolicy.Normalize(t.Total) == MoneyPolicy.Normalize(amount)).ToArray();
  }
}
