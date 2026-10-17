using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>Accepted-ledger source attribution for numeric note amounts. A link establishes provenance only; it is not a note review.</summary>
public static partial class FinancialStatementService
{
  public const int MaxNoteSourceAllocations = 100;

  internal static async Task<CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>>
    ResolveNoteSourceLinksAsync(IClientAccountingDbContext? db, ActorContext actor, FinancialPackage package,
      Guid? periodId, IReadOnlyList<NoteLineInput> notes, CancellationToken ct)
  {
    var requested = notes.Select((note, index) => (note, index))
      .Where(x => x.note.SourceAllocations is { Count: > 0 }).ToArray();
    if (requested.Length == 0)
      return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Ok(
        new Dictionary<int, IReadOnlyList<ResolvedNoteSource>>());
    if (db is null || periodId is null)
      return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Fail(ErrorCodes.GateBlocked,
        "Note source lines require the client-accounting workspace and a reporting period.");
    if (requested.Any(x => x.note.SourceAllocations!.Count > MaxNoteSourceAllocations ||
        x.note.SourceAllocations.Any(a => a.GeneralLedgerLineId == Guid.Empty || a.SignedAmount == 0m ||
          MoneyPolicy.Normalize(a.SignedAmount) != a.SignedAmount ||
          x.note.SourceAllocations.Count(n => n.GeneralLedgerLineId == a.GeneralLedgerLineId) != 1)))
      return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Fail(ErrorCodes.Accounting.PackageInvalid,
        $"Each note amount may link up to {MaxNoteSourceAllocations} distinct, non-zero six-decimal source contributions.");
    foreach (var x in requested)
      if (MoneyPolicy.Normalize(x.note.SourceAllocations!.Sum(a => a.SignedAmount)) != x.note.Amount)
        return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Fail(ErrorCodes.Accounting.PackageSupplementaryInvalid,
          $"Accepted source contributions for note '{x.note.NoteCode.Trim()}' must total its entered note amount exactly.");

    var selected = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(db, actor,
      package.ClientId, package.EngagementId, periodId.Value, ct);
    if (!selected.Succeeded)
      return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Fail(selected.ErrorCode!, selected.Message!);
    if (selected.Value?.ImportBatchId is not Guid batchId)
      return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Fail(ErrorCodes.GateBlocked,
        "Accept the general ledger for this engagement before linking note amounts to source lines.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == package.FirmId &&
      x.ClientId == package.ClientId && x.Id == periodId, ct);
    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == package.FirmId &&
      x.ClientId == package.ClientId && x.EngagementId == package.EngagementId && x.Id == batchId && x.PeriodId == periodId &&
      x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED", ct);
    if (period is null || batch is null || batch.BookId != package.BookId || batch.Currency != package.Currency ||
        batch.NormalizedDatasetDigest != selected.Value.SourceIdentityHash)
      return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Fail(ErrorCodes.GateBlocked,
        "The accepted general ledger must match this package's period, book, currency and sealed source identity.");

    var sourceIds = requested.SelectMany(x => x.note.SourceAllocations!).Select(x => x.GeneralLedgerLineId).Distinct().ToArray();
    var sources = await (from line in db.GeneralLedgerLines.AsNoTracking()
      join transaction in db.GeneralLedgerTransactions.AsNoTracking() on line.TransactionId equals transaction.Id
      where line.FirmId == package.FirmId && line.ClientId == package.ClientId && line.EngagementId == package.EngagementId &&
        line.ImportBatchId == batchId && sourceIds.Contains(line.Id) && transaction.FirmId == package.FirmId &&
        transaction.ClientId == package.ClientId && transaction.EngagementId == package.EngagementId &&
        transaction.ImportBatchId == batchId && transaction.Currency == batch.Currency &&
        transaction.PostingDate >= period.StartDate && transaction.PostingDate <= period.EndDate
      select new { Line = line, Transaction = transaction }).ToListAsync(ct);
    if (sources.Count != sourceIds.Length)
      return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Fail(ErrorCodes.GateBlocked,
        "A note source line is outside the accepted general ledger for this period, client or engagement.");
    var sourceById = sources.ToDictionary(x => x.Line.Id);
    foreach (var group in requested.SelectMany(x => x.note.SourceAllocations!).GroupBy(x => x.GeneralLedgerLineId))
    {
      var sourceAmount = sourceById[group.Key].Line.FunctionalAmount;
      var total = MoneyPolicy.Normalize(group.Sum(x => x.SignedAmount));
      if (sourceAmount == 0m || group.Any(x => Math.Sign(x.SignedAmount) != Math.Sign(sourceAmount)) ||
          Math.Abs(total) > Math.Abs(sourceAmount))
        return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Fail(ErrorCodes.Accounting.PackageInvalid,
          "A note source line is over-allocated or a contribution runs against its accepted ledger sign.");
    }

    var resolved = new Dictionary<int, IReadOnlyList<ResolvedNoteSource>>();
    foreach (var x in requested)
    {
      var links = x.note.SourceAllocations!.OrderBy(a => a.GeneralLedgerLineId).Select(a =>
      {
        var source = sourceById[a.GeneralLedgerLineId];
        var lineHash = Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
        {
          Version = "statement-note-gl-line.v1", Source = selected.Value.SourceIdentityHash, Batch = batchId,
          Period = periodId, batch.BookId, Line = source.Line, source.Transaction
        }));
        return new ResolvedNoteSource(source.Line.Id, source.Transaction.Id, batchId,
          selected.Value.SourceIdentityHash, lineHash, source.Line.AccountCode, source.Transaction.PostingDate,
          MoneyPolicy.Normalize(a.SignedAmount), batch.Currency);
      }).ToArray();
      resolved.Add(x.index, links);
    }
    var current = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(db, actor,
      package.ClientId, package.EngagementId, periodId.Value, ct);
    if (!current.Succeeded || current.Value != selected.Value)
      return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Fail(ErrorCodes.StaleRevision,
        "The accepted general ledger changed while note source lines were being resolved. Refresh and retry the package build.");
    return CommandResult<IReadOnlyDictionary<int, IReadOnlyList<ResolvedNoteSource>>>.Ok(resolved);
  }
}
