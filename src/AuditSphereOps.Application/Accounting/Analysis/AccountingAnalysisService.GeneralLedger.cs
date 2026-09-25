using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class AccountingAnalysisService
{
  public static async Task<CommandResult<GeneralLedgerPage>> GetGeneralLedgerPageAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid importBatchId, int page = 1, int pageSize = 100,
    CancellationToken ct = default)
  {
    if (importBatchId == Guid.Empty || page < 1 || pageSize is < 1 or > 500 ||
        (long)(page - 1) * pageSize > int.MaxValue - pageSize)
      return CommandResult<GeneralLedgerPage>.Fail(ErrorCodes.Accounting.ImportRejected, "The ledger page request is invalid.");
    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == importBatchId && x.FirmId == actor.FirmId, ct);
    if (batch is null)
      return CommandResult<GeneralLedgerPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(batch.FirmId, batch.ClientId, batch.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<GeneralLedgerPage>.Fail(auth.ErrorCode!, auth.Message!);
    if (batch.Status != "SEALED")
      return CommandResult<GeneralLedgerPage>.Fail(ErrorCodes.GateBlocked, "Only a sealed GL batch can be read.");

    var skip = (page - 1) * pageSize;
    var rows = await (from line in db.GeneralLedgerLines.AsNoTracking()
                      join transaction in db.GeneralLedgerTransactions.AsNoTracking()
                        on new { line.FirmId, line.ClientId, line.EngagementId, line.TransactionId }
                        equals new { transaction.FirmId, transaction.ClientId, transaction.EngagementId, TransactionId = transaction.Id }
                      where line.FirmId == batch.FirmId && line.ClientId == batch.ClientId &&
                            line.EngagementId == batch.EngagementId && line.ImportBatchId == batch.Id
                      orderby transaction.PostingDate, transaction.StableJournalId, line.StableLineId, line.Id
                      select new GeneralLedgerLineProjection(line.Id, transaction.StableJournalId, transaction.PostingDate,
                        line.AccountCode, line.Debit, line.Credit, line.FunctionalAmount, line.OriginalCurrency,
                        transaction.DocumentDate, transaction.ServiceDate, batch.ReceiptReference))
      .Skip(skip).Take(pageSize + 1).ToListAsync(ct);
    return CommandResult<GeneralLedgerPage>.Ok(new GeneralLedgerPage(rows.Take(pageSize).ToArray(), rows.Count > pageSize));
  }

  public static async Task<CommandResult<Guid>> CreateGeneralLedgerCompletenessBridgeAsync(
    IClientAccountingDbContext db, ActorContext actor, GeneralLedgerCompletenessRequest request,
    CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || request.EngagementId == Guid.Empty || request.PeriodId == Guid.Empty ||
        request.TrialBalanceDatasetId == Guid.Empty || request.ImportBatchId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Trim().Length > 2000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "A completeness bridge needs one scoped period, TB, GL batch and evidence reference.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.PeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting period is outside the client scope.");
    if (request.BookId is { } bookId && !await db.ClientReportingBooks.AsNoTracking().AnyAsync(x =>
      x.Id == bookId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.PeriodId == request.PeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The selected reporting book is outside the client period scope.");

    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.TrialBalanceDatasetId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.EngagementId == request.EngagementId && x.ValidationStatus == "Accepted" &&
      x.ImportState == TrialBalanceImportStates.Sealed, ct);
    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ImportBatchId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.EngagementId == request.EngagementId && x.Status == "SEALED", ct);
    if (dataset is null || batch is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The selected TB dataset or GL batch is outside the sealed engagement scope.");
    if (batch.PeriodId != request.PeriodId || batch.BookId != request.BookId ||
        dataset.PeriodId != request.PeriodId || dataset.BookId != request.BookId ||
        !string.Equals(dataset.Basis, period.Basis, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(dataset.Currency, batch.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(dataset.LegalEntityKey, batch.LegalEntityKey, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The TB and GL sources do not describe the same period, book, entity or currency.");
    var openingDataset = request.OpeningTrialBalanceDatasetId is { } openingDatasetId
      ? await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == openingDatasetId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.EngagementId == request.EngagementId && x.ValidationStatus == "Accepted" &&
        x.ImportState == TrialBalanceImportStates.Sealed, ct)
      : null;
    if (request.OpeningTrialBalanceDatasetId.HasValue && openingDataset is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The opening TB dataset is outside the sealed engagement scope.");
    if (openingDataset is not null)
    {
      var openingPeriod = openingDataset.PeriodId is { } openingPeriodId
        ? await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == openingPeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct)
        : null;
      var currentBook = request.BookId is { } currentBookId
        ? await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == currentBookId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.PeriodId == request.PeriodId, ct)
        : null;
      var openingBook = openingDataset.BookId is { } openingBookId
        ? await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == openingBookId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct)
        : null;
      if (openingPeriod is null || period.PriorPeriodId != openingPeriod.Id ||
          !string.Equals(openingDataset.Basis, period.Basis, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(openingDataset.Currency, dataset.Currency, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(openingDataset.LegalEntityKey, dataset.LegalEntityKey, StringComparison.Ordinal) ||
          currentBook is null || openingBook is null ||
          !string.Equals(currentBook.Code, openingBook.Code, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(currentBook.Basis, openingBook.Basis, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(currentBook.Currency, openingBook.Currency, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The opening TB must be the approved prior-period dataset for the same entity, book, basis and currency.");
    }
    if (await db.GeneralLedgerCompletenessBridges.AnyAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId &&
      x.TrialBalanceDatasetId == dataset.Id && x.ImportBatchId == batch.Id, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportDuplicate, "This TB and GL source pair already has a completeness bridge.");

    var tbTotals = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == dataset.Id)
      .GroupBy(x => x.AccountCode).Select(x => new { AccountCode = x.Key, Total = x.Sum(row => row.Amount) }).ToListAsync(ct);
    var openingTotals = openingDataset is null
      ? []
      : await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == openingDataset.Id)
        .GroupBy(x => x.AccountCode).Select(x => new { AccountCode = x.Key, Total = x.Sum(row => row.Amount) }).ToListAsync(ct);
    var glTotals = await db.GeneralLedgerLines.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId && x.ImportBatchId == batch.Id)
      .GroupBy(x => x.AccountCode).Select(x => new { AccountCode = x.Key, Total = x.Sum(row => row.FunctionalAmount) }).ToListAsync(ct);
    var transactionQuery = db.GeneralLedgerTransactions.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId && x.ImportBatchId == batch.Id);
    var transactionCount = await transactionQuery.CountAsync(ct);
    if (tbTotals.Count == 0 || glTotals.Count == 0 || transactionCount == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Both sources must contain at least one account and the GL batch must contain a journal.");
    var coverageStart = await transactionQuery.OrderBy(x => x.PostingDate).Select(x => x.PostingDate).FirstAsync(ct);
    var coverageEnd = await transactionQuery.OrderByDescending(x => x.PostingDate).Select(x => x.PostingDate).FirstAsync(ct);
    if (coverageStart < period.StartDate || coverageEnd > period.EndDate)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The GL batch extends outside the selected reporting period.");

    var tb = tbTotals.ToDictionary(x => x.AccountCode, x => MoneyPolicy.Normalize(x.Total), StringComparer.Ordinal);
    var opening = openingTotals.ToDictionary(x => x.AccountCode, x => MoneyPolicy.Normalize(x.Total), StringComparer.Ordinal);
    var gl = glTotals.ToDictionary(x => x.AccountCode, x => MoneyPolicy.Normalize(x.Total), StringComparer.Ordinal);
    var accounts = tb.Keys.Concat(gl.Keys).Concat(opening.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    var residuals = accounts.Select(code => new
    {
      Code = code,
      TrialBalance = tb.GetValueOrDefault(code),
      GeneralLedger = gl.GetValueOrDefault(code)
    }).Select(x => new { x.Code, x.TrialBalance, x.GeneralLedger, Difference = MoneyPolicy.Normalize(x.GeneralLedger - x.TrialBalance) }).ToArray();
    var mismatched = residuals.Count(x => x.Difference != 0m);
    var absoluteResidual = MoneyPolicy.Normalize(residuals.Sum(x => Math.Abs(x.Difference)));
    var residualDigest = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(string.Join('\n', residuals.Select(x =>
      $"{x.Code.Length}:{x.Code}:{x.TrialBalance.ToString("0.000000", CultureInfo.InvariantCulture)}:{x.GeneralLedger.ToString("0.000000", CultureInfo.InvariantCulture)}:{x.Difference.ToString("0.000000", CultureInfo.InvariantCulture)}"))));
    var rollforward = accounts.Select(code => new
    {
      Code = code,
      Opening = opening.GetValueOrDefault(code),
      Movement = gl.GetValueOrDefault(code),
      Closing = tb.GetValueOrDefault(code)
    }).Select(x => new { x.Code, x.Opening, x.Movement, x.Closing,
      Difference = MoneyPolicy.Normalize(x.Closing - x.Opening - x.Movement) }).ToArray();
    var openingMovementMismatched = rollforward.Count(x => x.Difference != 0m);
    var openingMovementResidual = MoneyPolicy.Normalize(rollforward.Sum(x => Math.Abs(x.Difference)));
    var openingMovementDigest = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(string.Join('\n', rollforward.Select(x =>
      $"{x.Code.Length}:{x.Code}:{x.Opening.ToString("0.000000", CultureInfo.InvariantCulture)}:{x.Movement.ToString("0.000000", CultureInfo.InvariantCulture)}:{x.Closing.ToString("0.000000", CultureInfo.InvariantCulture)}:{x.Difference.ToString("0.000000", CultureInfo.InvariantCulture)}"))));
    var transactionIds = await transactionQuery.Select(x => x.Id).ToListAsync(ct);
    var journalLines = await db.GeneralLedgerLines.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId && x.ImportBatchId == batch.Id)
      .Select(x => new { x.TransactionId, x.Debit, x.Credit }).ToListAsync(ct);
    var lineGroups = journalLines.GroupBy(x => x.TransactionId).ToDictionary(x => x.Key, x => x.ToArray());
    var journalExceptionCount = transactionIds.Count(id => !lineGroups.TryGetValue(id, out var lines) ||
      lines.Length < 2 || MoneyPolicy.Normalize(lines.Sum(x => x.Debit) - lines.Sum(x => x.Credit)) != 0m);
    var disclosure = new List<string>();
    if (openingDataset is null) disclosure.Add("OPENING_DATASET_NOT_PROVIDED");
    if (openingDataset is not null && openingTotals.Count == 0) disclosure.Add("OPENING_DATASET_EMPTY");
    if (journalExceptionCount > 0) disclosure.Add("MALFORMED_JOURNAL_GROUPS");
    if (batch.ExpectedTransactionCount > 0 &&
        (batch.ExpectedTransactionCount != batch.AcceptedTransactionCount || batch.ExpectedLineCount != batch.AcceptedLineCount))
      disclosure.Add("INCOMPLETE_BATCH_COUNTS");
    var incompleteExtract = disclosure.Count > 0;
    var bridge = new GeneralLedgerCompletenessBridge
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, BookId = request.BookId, TrialBalanceDatasetId = dataset.Id, ImportBatchId = batch.Id,
      OpeningTrialBalanceDatasetId = openingDataset?.Id,
      TrialBalanceHash = (dataset.NormalizedDatasetDigest.Length == 64 ? dataset.NormalizedDatasetDigest : dataset.Sha256Hex).ToLowerInvariant(),
      GeneralLedgerHash = batch.NormalizedDatasetDigest.ToLowerInvariant(), AccountResidualDigest = residualDigest,
      OpeningTrialBalanceHash = openingDataset is null ? string.Empty :
        (openingDataset.NormalizedDatasetDigest.Length == 64 ? openingDataset.NormalizedDatasetDigest : openingDataset.Sha256Hex).ToLowerInvariant(),
      OpeningMovementResidualDigest = openingMovementDigest,
      TrialBalanceAccountCount = tb.Count, GeneralLedgerAccountCount = gl.Count,
      MatchedAccountCount = residuals.Length - mismatched, MismatchedAccountCount = mismatched,
      OpeningMovementMismatchedAccountCount = openingMovementMismatched, JournalExceptionCount = journalExceptionCount,
      OpeningAmount = MoneyPolicy.Normalize(opening.Values.Sum()), MovementAmount = MoneyPolicy.Normalize(gl.Values.Sum()),
      ClosingAmount = MoneyPolicy.Normalize(tb.Values.Sum()), OpeningMovementResidual = openingMovementResidual,
      AbsoluteResidual = absoluteResidual, CoverageStart = coverageStart, CoverageEnd = coverageEnd,
      Status = mismatched == 0 ? "RECONCILED" : "UNRECONCILED", EvidenceReference = request.EvidenceReference.Trim(),
      IncompleteExtract = incompleteExtract, CompletenessDisclosure = string.Join(';', disclosure),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.GeneralLedgerCompletenessBridges.Add(bridge);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(bridge.Id);
  }

  public static async Task<CommandResult<Guid>> EnqueueGeneralLedgerCompletenessBridgeAsync(
    IClientAccountingDbContext db, ActorContext actor, GeneralLedgerCompletenessRequest request,
    IOperationStore operationStore, GeneralLedgerCompletenessHandler handler,
    CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || request.EngagementId == Guid.Empty || request.PeriodId == Guid.Empty ||
        request.TrialBalanceDatasetId == Guid.Empty || request.ImportBatchId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Trim().Length > 2000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "A completeness bridge needs one scoped period, TB, GL batch and evidence reference.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.PeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting period is outside the client scope.");
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.TrialBalanceDatasetId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.EngagementId == request.EngagementId && x.ValidationStatus == "Accepted" &&
      x.ImportState == TrialBalanceImportStates.Sealed, ct);
    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ImportBatchId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.EngagementId == request.EngagementId && x.Status == "SEALED", ct);
    if (dataset is null || batch is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The selected TB dataset or GL batch is outside the sealed engagement scope.");
    if (batch.PeriodId != request.PeriodId || batch.BookId != request.BookId ||
        dataset.PeriodId != request.PeriodId || dataset.BookId != request.BookId ||
        !string.Equals(dataset.Basis, period.Basis, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(dataset.Currency, batch.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(dataset.LegalEntityKey, batch.LegalEntityKey, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The TB and GL sources do not describe the same period, book, entity or currency.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var payload = JsonSerializer.Serialize(new
    {
      clientId = request.ClientId.ToString("D"), engagementId = request.EngagementId.ToString("D"),
      periodId = request.PeriodId.ToString("D"), bookId = request.BookId?.ToString("D"),
      trialBalanceDatasetId = request.TrialBalanceDatasetId.ToString("D"), importBatchId = request.ImportBatchId.ToString("D"),
      evidenceReference = request.EvidenceReference.Trim(), openingTrialBalanceDatasetId = request.OpeningTrialBalanceDatasetId?.ToString("D")
    });
    var evidenceDigest = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(request.EvidenceReference.Trim()));
    CommandResult<Guid> enqueued;
    try
    {
      enqueued = await operationStore.EnqueueAsync(db, new OperationRequest(
        actor.FirmId, request.ClientId, request.EngagementId, GeneralLedgerCompletenessHandler.Kind,
        request.TrialBalanceDatasetId, dataset.Revision,
        $"gl-completeness:{request.TrialBalanceDatasetId:D}:{request.ImportBatchId:D}:{dataset.Revision}:{evidenceDigest}:{request.OpeningTrialBalanceDatasetId?.ToString("D") ?? string.Empty}",
        payload, actor.UserId), handler, ct);
    }
    catch (OperationBlockedException)
    {
      enqueued = CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The completeness operation request was refused.");
    }
    if (!enqueued.Succeeded)
      return CommandResult<Guid>.Fail(enqueued.ErrorCode!, enqueued.Message!);
    await tx.CommitAsync(ct);
    return enqueued;
  }

  public static async Task<CommandResult> ReviewGeneralLedgerCompletenessAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid bridgeId, bool approve,
    CancellationToken ct = default)
  {
    var bridge = await db.GeneralLedgerCompletenessBridges.SingleOrDefaultAsync(x => x.Id == bridgeId && x.FirmId == actor.FirmId, ct);
    if (bridge is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, bridge.ClientId, bridge.EngagementId, ReviewerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    if (bridge.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The completeness preparer cannot review the same bridge.");
    if (bridge.Status is not ("RECONCILED" or "UNRECONCILED"))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "This completeness bridge has already been reviewed.");
    if (approve && bridge.Status != "RECONCILED")
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Only an exactly reconciled bridge can be approved.");
    if (approve && (bridge.IncompleteExtract || bridge.OpeningMovementResidual != 0m || bridge.JournalExceptionCount != 0))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "An incomplete or malformed GL extract cannot be approved as complete.");
    var currentTbHash = await db.TrialBalanceDatasets.AsNoTracking().Where(x => x.Id == bridge.TrialBalanceDatasetId)
      .Select(x => x.NormalizedDatasetDigest.Length == 64 ? x.NormalizedDatasetDigest : x.Sha256Hex).SingleOrDefaultAsync(ct);
    var currentGlHash = await db.SourceImportBatches.AsNoTracking().Where(x => x.Id == bridge.ImportBatchId)
      .Select(x => x.NormalizedDatasetDigest).SingleOrDefaultAsync(ct);
    var currentOpeningHash = bridge.OpeningTrialBalanceDatasetId is { } openingDatasetId
      ? await db.TrialBalanceDatasets.AsNoTracking().Where(x => x.Id == openingDatasetId)
        .Select(x => x.NormalizedDatasetDigest.Length == 64 ? x.NormalizedDatasetDigest : x.Sha256Hex).SingleOrDefaultAsync(ct)
      : string.Empty;
    if (!string.Equals(currentTbHash, bridge.TrialBalanceHash, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(currentGlHash, bridge.GeneralLedgerHash, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(currentOpeningHash, bridge.OpeningTrialBalanceHash, StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail(ErrorCodes.ManifestMismatch, "A source digest changed after the completeness bridge was prepared.");
    bridge.Status = approve ? AccountingWorkflowStates.Approved : AccountingWorkflowStates.Rejected;
    bridge.ReviewedByUserId = actor.UserId;
    bridge.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
