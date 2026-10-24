using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

internal sealed record ConfirmationBankSourceCapture(
  AuditBankReconciliation Bank,
  AuditSchedule LedgerSchedule,
  AuditSchedule StatementSchedule,
  ConfirmationBankSourceSnapshot Snapshot,
  string SnapshotJson,
  string SnapshotSha256,
  long CurrentGeneration,
  bool Eligible,
  string? BlockerCode,
  string? Blocker);

/// <summary>Resolves the explicitly selected, independently reviewed Bank reconciliation.
/// It never owns a transaction: writers lock the exact source rows before calling it.</summary>
internal static class ConfirmationBankSourceResolver
{
  internal const string SourceKind = "BANK_RECONCILIATION";
  internal const string SnapshotVersion = "bank-confirmation-booked-source.v1";
  private const int MaximumSnapshotBytes = 16 * 1024 * 1024;

  internal static async Task<CommandResult> LockForPublicationAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    IEnumerable<Guid> reconciliationIds, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "A typed Bank source requires the existing confirmation scope transaction.");

    var bankIds = reconciliationIds.Where(x => x != Guid.Empty).Distinct().Order().ToArray();
    if (bankIds.Length == 0)
      return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "A typed Bank reconciliation id is required.");

    var banks = new List<AuditBankReconciliation>(bankIds.Length);
    foreach (var id in bankIds)
    {
      var bank = await db.AuditBankReconciliations.FromSqlInterpolated(
          $"SELECT * FROM audit_bank_reconciliations WHERE firm_id = {firmId} AND client_id = {clientId} AND engagement_id = {engagementId} AND id = {id} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if (bank is null)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      banks.Add(bank);
    }

    var scheduleIds = banks.SelectMany(x => new[] { x.LedgerScheduleId, x.StatementScheduleId })
      .Where(x => x != Guid.Empty).Distinct().Order().ToArray();
    if (banks.Any(x => x.LedgerScheduleId == Guid.Empty || x.StatementScheduleId == Guid.Empty ||
        x.LedgerScheduleId == x.StatementScheduleId))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The selected Bank source has incomplete schedule links.");

    foreach (var id in bankIds)
    {
      _ = await db.AuditBankReconciliationItems.FromSqlInterpolated(
          $"SELECT * FROM audit_bank_reconciliation_items WHERE firm_id = {firmId} AND client_id = {clientId} AND engagement_id = {engagementId} AND bank_reconciliation_id = {id} ORDER BY id FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    }

    var schedules = new List<AuditSchedule>(scheduleIds.Length);
    foreach (var id in scheduleIds)
    {
      var schedule = await db.AuditSchedules.FromSqlInterpolated(
          $"SELECT * FROM audit_schedules WHERE firm_id = {firmId} AND client_id = {clientId} AND engagement_id = {engagementId} AND id = {id} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if (schedule is null)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "A linked Bank schedule is unavailable.");
      schedules.Add(schedule);
    }

    foreach (var id in scheduleIds)
    {
      _ = await db.AuditScheduleRows.FromSqlInterpolated(
          $"SELECT * FROM audit_schedule_rows WHERE firm_id = {firmId} AND client_id = {clientId} AND engagement_id = {engagementId} AND schedule_id = {id} ORDER BY id FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    }

    var batchIds = schedules.Where(x => x.SourceImportBatchId.HasValue)
      .Select(x => x.SourceImportBatchId!.Value).Distinct().Order().ToArray();
    if (batchIds.Length > 0 && db is not IClientAccountingDbContext)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The selected Bank source has no supported GL source context.");
    if (batchIds.Length > 0)
    {
      var accounting = (IClientAccountingDbContext)db;
      foreach (var id in batchIds)
      {
        var batch = await accounting.SourceImportBatches.FromSqlInterpolated(
            $"SELECT * FROM source_import_batches WHERE firm_id = {firmId} AND client_id = {clientId} AND engagement_id = {engagementId} AND id = {id} FOR SHARE")
          .AsNoTracking().SingleOrDefaultAsync(ct);
        if (batch is null)
          return CommandResult.Fail(ErrorCodes.GenerationStale, "A linked GL source batch is unavailable.");
      }
    }

    return CommandResult.Ok();
  }

  internal static async Task<CommandResult<ConfirmationBankSourceCapture>> ResolveForPublicationAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    ConfirmationBookedSourcePin? pin, CancellationToken ct)
  {
    if (pin is null || !string.Equals(pin.Kind, SourceKind, StringComparison.Ordinal) ||
        pin.ReconciliationId == Guid.Empty || !IsSha256(pin.ExpectedSnapshotSha256))
      return CommandResult<ConfirmationBankSourceCapture>.Fail(ErrorCodes.AuditPlanning.Invalid,
        "Select a current, typed Bank source and review its source digest.");

    var captured = await CaptureCurrentAsync(db, firmId, clientId, engagementId, pin.ReconciliationId, ct);
    if (!captured.Succeeded)
      return CommandResult<ConfirmationBankSourceCapture>.Fail(captured.ErrorCode!, captured.Message!);
    if (!captured.Value!.Eligible)
      return CommandResult<ConfirmationBankSourceCapture>.Fail(captured.Value.BlockerCode!, captured.Value.Blocker!);
    if (!string.Equals(captured.Value.SnapshotSha256, pin.ExpectedSnapshotSha256, StringComparison.Ordinal))
      return CommandResult<ConfirmationBankSourceCapture>.Fail(ErrorCodes.GenerationStale,
        "The selected Bank source changed. Refresh and review the current source again.");
    return captured;
  }

  internal static async Task<CommandResult<ConfirmationBankSourceCapture>> CaptureCurrentAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid reconciliationId,
    CancellationToken ct)
  {
    var bank = await db.AuditBankReconciliations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == reconciliationId && x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId, ct);
    if (bank is null)
      return CommandResult<ConfirmationBankSourceCapture>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (bank.LedgerScheduleId == Guid.Empty || bank.StatementScheduleId == Guid.Empty ||
        bank.LedgerScheduleId == bank.StatementScheduleId)
      return CommandResult<ConfirmationBankSourceCapture>.Fail(ErrorCodes.GenerationStale,
        "The Bank reconciliation has incomplete linked schedules.");

    var schedules = await db.AuditSchedules.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && (x.Id == bank.LedgerScheduleId || x.Id == bank.StatementScheduleId)).ToListAsync(ct);
    if (schedules.Count != 2)
      return CommandResult<ConfirmationBankSourceCapture>.Fail(ErrorCodes.GenerationStale,
        "A linked Bank schedule is unavailable.");
    var ledger = schedules.Single(x => x.Id == bank.LedgerScheduleId);
    var statement = schedules.Single(x => x.Id == bank.StatementScheduleId);

    var ledgerRows = await ScheduleRowsAsync(db, firmId, clientId, engagementId, ledger.Id, ct);
    var statementRows = await ScheduleRowsAsync(db, firmId, clientId, engagementId, statement.Id, ct);
    var items = await db.AuditBankReconciliationItems.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.BankReconciliationId == bank.Id)
      .OrderBy(x => x.ItemType).ThenBy(x => x.StableItemId).ThenBy(x => x.Id).ToListAsync(ct);

    var accounting = db as IClientAccountingDbContext;
    var batchIds = new[] { ledger.SourceImportBatchId, statement.SourceImportBatchId }
      .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
    List<SourceImportBatch> batches = [];
    if (accounting is not null && batchIds.Length > 0)
      batches = await accounting.SourceImportBatches.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
        x.EngagementId == engagementId && batchIds.Contains(x.Id)).ToListAsync(ct);
    var ledgerBatch = ledger.SourceImportBatchId is { } ledgerBatchId ? batches.SingleOrDefault(x => x.Id == ledgerBatchId) : null;
    var statementBatch = statement.SourceImportBatchId is { } statementBatchId ? batches.SingleOrDefault(x => x.Id == statementBatchId) : null;

    if (bank.ReviewedByUserId is null || bank.ReviewedAt is null)
      return CommandResult<ConfirmationBankSourceCapture>.Fail(ErrorCodes.GateBlocked,
        "The Bank reconciliation has no retained independent review evidence.");

    var ledgerRowSnapshots = ledgerRows.Select(RowSnapshot).ToArray();
    var statementRowSnapshots = statementRows.Select(RowSnapshot).ToArray();
    var snapshot = new ConfirmationBankSourceSnapshot(
      SnapshotVersion,
      new ConfirmationBankSnapshot(bank.Id, bank.FirmId, bank.ClientId, bank.EngagementId, bank.ProcedureId,
        bank.LedgerScheduleId, bank.StatementScheduleId, bank.AsOfDate, bank.Currency,
        Amount(bank.LedgerBalance), Amount(bank.StatementBalance), Amount(bank.TimingItemTotal),
        Amount(bank.ProposedCorrectionTotal), Amount(bank.Residual), bank.InputGeneration, bank.Status,
        bank.Conclusion, bank.CreatedByUserId, bank.CreatedAt, bank.ReviewedByUserId.Value, bank.ReviewedAt.Value),
      ScheduleSnapshot(ledger), ScheduleSnapshot(statement),
      ledgerBatch is null ? null : BatchSnapshot(ledgerBatch),
      statementBatch is null ? null : BatchSnapshot(statementBatch),
      HashJson(ledgerRowSnapshots), HashJson(statementRowSnapshots), items.Select(ItemSnapshot).ToArray());
    var snapshotJson = JsonSerializer.Serialize(snapshot);
    var snapshotByteCount = Encoding.UTF8.GetByteCount(snapshotJson);
    var snapshotSha256 = HashUtf8(snapshotJson);

    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if (generation is null || generation < 1)
      return CommandResult<ConfirmationBankSourceCapture>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var blockerCode = ErrorCodes.GateBlocked;
    string? blocker = null;
    void Block(string message, string code = ErrorCodes.GateBlocked)
    {
      if (blocker is null)
      {
        blocker = message;
        blockerCode = code;
      }
    }

    if (snapshotByteCount > MaximumSnapshotBytes)
      Block("The complete Bank source snapshot exceeds the supported 16 MiB evidence limit.");
    if (bank.Status != AuditBankReconciliationStatuses.Approved)
      Block("Only an independently approved Bank reconciliation can provide a booked balance.");
    if (bank.ReviewedByUserId == bank.CreatedByUserId)
      Block("The Bank reconciliation review is not independent.", ErrorCodes.ScopeDenied);
    if (bank.Residual != 0m)
      Block("The Bank reconciliation has an unreconciled residual.");
    if (bank.InputGeneration != generation.Value)
      Block("The Bank reconciliation belongs to an older client generation.", ErrorCodes.GenerationStale);

    var asOf = bank.AsOfDate;
    var scheduleBlocker = ValidateSchedule(ledger, ledgerRows, firmId, clientId, engagementId, "BANK_LEDGER", asOf, bank.Currency);
    if (scheduleBlocker is not null) Block(scheduleBlocker);
    scheduleBlocker = ValidateSchedule(statement, statementRows, firmId, clientId, engagementId, "BANK_STATEMENT", asOf, bank.Currency);
    if (scheduleBlocker is not null) Block(scheduleBlocker);
    if (ledger.InputGeneration != generation.Value || statement.InputGeneration != generation.Value)
      Block("A linked approved Bank schedule belongs to an older client generation.", ErrorCodes.GenerationStale);
    if (ledger.SignedControlTotal != bank.LedgerBalance || statement.SignedControlTotal != bank.StatementBalance)
      Block("The Bank reconciliation balances no longer match its linked approved schedules.", ErrorCodes.GenerationStale);

    var itemBlocker = ValidateItems(items, bank, ledger, statement);
    if (itemBlocker is not null) Block(itemBlocker);

    var batchBlocker = await ValidateBatchAsync(db, accounting, ledger, ledgerRows, ledgerBatch, asOf, ct);
    if (batchBlocker is not null) Block(batchBlocker.Message, batchBlocker.Code);
    batchBlocker = await ValidateBatchAsync(db, accounting, statement, statementRows, statementBatch, asOf, ct);
    if (batchBlocker is not null) Block(batchBlocker.Message, batchBlocker.Code);

    return CommandResult<ConfirmationBankSourceCapture>.Ok(new ConfirmationBankSourceCapture(
      bank, ledger, statement, snapshot, snapshotJson, snapshotSha256, generation.Value,
      blocker is null, blockerCode, blocker));
  }

  internal static async Task<long?> CurrentGenerationAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct) =>
    await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);

  private static async Task<List<AuditScheduleRow>> ScheduleRowsAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid scheduleId, CancellationToken ct) =>
    await db.AuditScheduleRows.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && x.ScheduleId == scheduleId)
      .OrderBy(x => x.StableRowId).ThenBy(x => x.Id).ToListAsync(ct);

  private static string? ValidateSchedule(AuditSchedule schedule, IReadOnlyList<AuditScheduleRow> rows,
    Guid firmId, Guid clientId, Guid engagementId, string requiredType, DateOnly asOfDate, string currency)
  {
    if (schedule.FirmId != firmId || schedule.ClientId != clientId || schedule.EngagementId != engagementId ||
        !string.Equals(schedule.ScheduleType, requiredType, StringComparison.OrdinalIgnoreCase))
      return "The linked Bank schedule has the wrong scope or source type.";
    if (schedule.Status != AuditScheduleStatuses.Approved || string.IsNullOrWhiteSpace(schedule.CompletenessDecision))
      return "Both linked Bank schedules must have an approved completeness decision.";
    if (schedule.AsOfDate is null || DateOnly.FromDateTime(schedule.AsOfDate.Value.UtcDateTime) != asOfDate ||
        schedule.PeriodStart is { } start && asOfDate < start || schedule.PeriodEnd is { } end && asOfDate > end)
      return "The Bank source and linked schedule dates do not agree.";
    if (!SameCurrency(schedule.Currency, currency) || !IsCurrency(schedule.Currency) ||
        string.IsNullOrWhiteSpace(schedule.EntityIdentifier) || string.IsNullOrWhiteSpace(schedule.SourceReceiptReference) ||
        !IsSha256(schedule.SourceHash))
      return "The linked Bank schedule source identity or currency is incomplete.";
    if (rows.Count == 0 || schedule.RowCount != rows.Count ||
        rows.Any(x => x.FirmId != firmId || x.ClientId != clientId || x.EngagementId != engagementId ||
          x.ScheduleId != schedule.Id || !SameCurrency(x.Currency, schedule.Currency)) ||
        rows.Select(x => x.StableRowId).Distinct(StringComparer.Ordinal).Count() != rows.Count ||
        rows.Sum(x => x.SignedAmount) != schedule.SignedControlTotal)
      return "The linked Bank schedule rows are missing, incomplete or inconsistent with their control total.";
    return null;
  }

  private static string? ValidateItems(IReadOnlyList<AuditBankReconciliationItem> items,
    AuditBankReconciliation bank, AuditSchedule ledger, AuditSchedule statement)
  {
    if (items.Count == 0 || items.Any(x => x.FirmId != bank.FirmId || x.ClientId != bank.ClientId ||
        x.EngagementId != bank.EngagementId || x.BankReconciliationId != bank.Id ||
        !AuditBankReconciliationItemTypes.All.Contains(x.ItemType) || !SameCurrency(x.Currency, bank.Currency) ||
        x.ItemType == AuditBankReconciliationItemTypes.Ledger && x.SourceScheduleId != ledger.Id ||
        x.ItemType == AuditBankReconciliationItemTypes.Statement && x.SourceScheduleId != statement.Id ||
        x.ItemType == AuditBankReconciliationItemTypes.Timing && x.SourceScheduleId is { } timingSource &&
          timingSource != ledger.Id && timingSource != statement.Id ||
        x.ItemType == AuditBankReconciliationItemTypes.ProposedCorrection && x.ProposedJournalId is null ||
        x.ItemType != AuditBankReconciliationItemTypes.ProposedCorrection && x.ProposedJournalId is not null))
      return "The approved Bank reconciliation items have incomplete or unsupported typed lineage.";

    var timing = MoneyPolicy.Normalize(items.Where(x => x.ItemType == AuditBankReconciliationItemTypes.Timing)
      .Sum(x => x.SignedAmount));
    var proposed = MoneyPolicy.Normalize(items.Where(x => x.ItemType == AuditBankReconciliationItemTypes.ProposedCorrection)
      .Sum(x => x.SignedAmount));
    if (timing != bank.TimingItemTotal || proposed != bank.ProposedCorrectionTotal ||
        MoneyPolicy.Normalize(bank.StatementBalance - bank.LedgerBalance - timing) != bank.Residual)
      return "The approved Bank reconciliation item totals no longer match the stored reconciliation.";
    return null;
  }

  private static async Task<SourceBlocker?> ValidateBatchAsync(
    IAuditSphereDbContext db, IClientAccountingDbContext? accounting, AuditSchedule schedule,
    IReadOnlyList<AuditScheduleRow> rows, SourceImportBatch? batch, DateOnly asOfDate, CancellationToken ct)
  {
    if (schedule.SourceImportBatchId is null)
    {
      if (string.Equals(schedule.ScheduleType, "BANK_LEDGER", StringComparison.OrdinalIgnoreCase))
        return new(ErrorCodes.GateBlocked, "The approved bank-ledger schedule has no sealed GL source batch.");
      return await ValidateAcceptedSourcePinAsync(db, accounting, schedule, null, ct);
    }
    if (accounting is null || batch is null)
      return new(ErrorCodes.GenerationStale, "The linked sealed GL source batch is unavailable.");
    if (batch.FirmId != schedule.FirmId || batch.ClientId != schedule.ClientId || batch.EngagementId != schedule.EngagementId ||
        batch.SourceKind != AccountingSourceKinds.GeneralLedger || batch.Status != "SEALED" ||
        !IsSha256(batch.NormalizedDatasetDigest) ||
        !string.Equals(schedule.SourceHash, batch.NormalizedDatasetDigest, StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(schedule.SourceHash, batch.RawFileSha256Hex, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(batch.LegalEntityKey, schedule.EntityIdentifier, StringComparison.Ordinal) ||
        !SameCurrency(batch.Currency, schedule.Currency) || batch.ExpectedLineCount < 1 ||
        batch.AcceptedLineCount != batch.ExpectedLineCount || batch.RowCount != batch.AcceptedLineCount ||
        batch.AcceptedChunkCount != batch.ExpectedChunkCount || batch.AcceptedTransactionCount != batch.ExpectedTransactionCount)
      return new(ErrorCodes.GenerationStale, "The linked GL source batch is not sealed, complete, hash-matched and in the schedule context.");
    if (batch.OriginKind == SourceImportOrigins.External && !IsSha256(batch.RawFileSha256Hex) ||
        batch.OriginKind == SourceImportOrigins.NativeLedger && batch.RawFileSha256Hex.Length != 0 ||
        batch.OriginKind is not (SourceImportOrigins.External or SourceImportOrigins.NativeLedger))
      return new(ErrorCodes.GenerationStale, "The linked GL source batch has unsupported origin or digest metadata.");

    var period = await accounting.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == batch.PeriodId && x.FirmId == batch.FirmId && x.ClientId == batch.ClientId, ct);
    if (period is null || !SameCurrency(period.Currency, batch.Currency) || asOfDate < period.StartDate || asOfDate > period.EndDate)
      return new(ErrorCodes.GenerationStale, "The linked GL source batch period does not cover the Bank source date and currency.");

    var accountCodes = rows.Select(x => x.AccountCode.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
    if (accountCodes.Length == 0)
      return new(ErrorCodes.GenerationStale, "The bank-ledger schedule has no GL account rows.");
    var lines = await accounting.GeneralLedgerLines.AsNoTracking().Where(x => x.FirmId == batch.FirmId &&
      x.ClientId == batch.ClientId && x.EngagementId == batch.EngagementId && x.ImportBatchId == batch.Id &&
      accountCodes.Contains(x.AccountCode.ToUpper())).ToListAsync(ct);
    if (lines.Count == 0 || accountCodes.Any(code => lines.All(x => x.AccountCode.Trim().ToUpperInvariant() != code)) ||
        MoneyPolicy.Normalize(lines.Sum(x => x.Debit - x.Credit)) != schedule.GlControlTotal)
      return new(ErrorCodes.GenerationStale, "The linked GL batch no longer covers the schedule accounts and control total.");

    if (batch.OriginKind == SourceImportOrigins.NativeLedger)
    {
      var current = await NativeGeneralLedgerSourceWorkspace.CheckCurrentAsync(db, batch, ct);
      if (!current.Succeeded)
        return new(ErrorCodes.GenerationStale, current.Message ?? "The native GL source is no longer current.");
    }
    return await ValidateAcceptedSourcePinAsync(db, accounting, schedule, batch, ct);
  }

  private static async Task<SourceBlocker?> ValidateAcceptedSourcePinAsync(
    IAuditSphereDbContext db, IClientAccountingDbContext? accounting, AuditSchedule schedule,
    SourceImportBatch? batch, CancellationToken ct)
  {
    if (schedule.AcceptedSourceDecisionId is null)
    {
      if (!string.IsNullOrEmpty(schedule.AcceptedSourceHash))
        return new(ErrorCodes.GenerationStale, "The optional accepted-source pin is incomplete.");
      return null;
    }
    if (!IsSha256(schedule.AcceptedSourceHash))
      return new(ErrorCodes.GenerationStale, "The optional accepted-source pin has an invalid digest.");
    var decision = await db.SourceAcceptanceDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == schedule.AcceptedSourceDecisionId && x.FirmId == schedule.FirmId && x.ClientId == schedule.ClientId &&
      x.EngagementId == schedule.EngagementId && x.Decision == "ACCEPTED" &&
      x.SourceIdentityHash == schedule.AcceptedSourceHash, ct);
    if (decision is null)
      return new(ErrorCodes.GenerationStale, "The optional accepted-source pin no longer resolves to its retained decision.");
    if (decision.SourceKind == AccountingSourceKinds.GeneralLedger)
    {
      if (batch is null || decision.ImportBatchId != batch.Id || decision.SourceIdentityHash != batch.NormalizedDatasetDigest)
        return new(ErrorCodes.GenerationStale, "The accepted GL pin does not identify the linked sealed batch.");
    }
    else if (decision.SourceKind == AccountingSourceKinds.TrialBalance && accounting is not null &&
        decision.TrialBalanceDatasetId is { } datasetId)
    {
      var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == datasetId && x.FirmId == schedule.FirmId && x.ClientId == schedule.ClientId &&
        x.EngagementId == schedule.EngagementId && x.ImportState == TrialBalanceImportStates.Sealed &&
        x.ValidationStatus == "Accepted" && x.Balanced, ct);
      if (dataset is null || decision.SourceIdentityHash != MappedTrialBalanceSource.Digest(dataset))
        return new(ErrorCodes.GenerationStale, "The accepted TB pin no longer identifies its sealed source revision.");
      var period = dataset.PeriodId is { } periodId
        ? await accounting.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == periodId && x.FirmId == schedule.FirmId && x.ClientId == schedule.ClientId, ct)
        : null;
      var scheduleAsOf = schedule.AsOfDate is { } asOf
        ? DateOnly.FromDateTime(asOf.UtcDateTime)
        : (DateOnly?)null;
      if (period is null || scheduleAsOf is null || scheduleAsOf.Value < period.StartDate ||
          scheduleAsOf.Value > period.EndDate ||
          !SameCurrency(dataset.Currency, schedule.Currency) ||
          !string.Equals(dataset.LegalEntityKey, schedule.EntityIdentifier, StringComparison.Ordinal))
        return new(ErrorCodes.GenerationStale, "The accepted TB pin does not match the linked schedule entity, date and currency context.");
      var current = await NativeTrialBalanceSourceWorkspace.CheckCurrentAsync(db, dataset, ct);
      if (!current.Succeeded)
        return new(ErrorCodes.GenerationStale, current.Message ?? "The accepted TB source is no longer current.");
      if (batch?.OriginKind == SourceImportOrigins.NativeLedger)
      {
        var native = await accounting.NativeGeneralLedgerSources.AsNoTracking().SingleOrDefaultAsync(x =>
          x.ImportBatchId == batch.Id && x.FirmId == batch.FirmId && x.ClientId == batch.ClientId &&
          x.EngagementId == batch.EngagementId, ct);
        if (native is null || dataset.Id != native.TrialBalanceDatasetId ||
            decision.SourceIdentityHash != native.NativeTrialBalanceSourceHash)
          return new(ErrorCodes.GenerationStale, "The accepted TB pin does not identify the native GL source lineage.");
      }
    }
    else
      return new(ErrorCodes.GenerationStale, "The optional accepted-source pin has unsupported GL lineage.");
    return null;
  }

  private static ConfirmationBankScheduleSnapshot ScheduleSnapshot(AuditSchedule x) => new(
    x.Id, x.FirmId, x.ClientId, x.EngagementId, x.ScheduleType, x.EntityIdentifier, x.SourceReceiptReference,
    x.AsOfDate, x.PeriodStart, x.PeriodEnd, x.Currency, x.SignConvention, x.SourceHash, x.SourceImportBatchId,
    x.AcceptedSourceDecisionId, x.AcceptedSourceHash, x.RowCount, Amount(x.SignedControlTotal),
    Amount(x.GlControlTotal), Amount(x.Residual), x.InputGeneration, x.CompletenessDecision, x.Status,
    x.CreatedByUserId, x.CreatedAt);

  private static ConfirmationBankBatchSnapshot BatchSnapshot(SourceImportBatch x) => new(
    x.Id, x.FirmId, x.ClientId, x.EngagementId, x.PeriodId, x.BookId, x.SourceKind, x.OriginKind,
    x.ProfileVersion, x.ParserVersion, x.RawFileSha256Hex, x.NormalizedDatasetDigest, x.LegalEntityKey,
    x.Currency, x.RowCount, x.ExpectedChunkCount, x.ExpectedTransactionCount, x.ExpectedLineCount,
    x.AcceptedChunkCount, x.AcceptedTransactionCount, x.AcceptedLineCount, x.Status, x.ReceiptReference,
    x.CreatedByUserId, x.CreatedAt);

  private static ConfirmationBankScheduleRowSnapshot RowSnapshot(AuditScheduleRow x) => new(
    x.Id, x.StableRowId, x.SourceLineNumber, x.AccountCode, x.Description, Amount(x.SignedAmount), x.Currency,
    x.TransactionDate, x.PostingDate, x.DeliveryDate, x.ServiceDate, x.OriginalValuesJson, x.CreatedAt);

  private static ConfirmationBankItemSnapshot ItemSnapshot(AuditBankReconciliationItem x) => new(
    x.Id, x.SourceScheduleId, x.ProposedJournalId, x.StableItemId, x.ItemType, Amount(x.SignedAmount), x.Currency,
    x.Description, x.SourceReference, x.EvidenceReference, x.CreatedAt);

  private static string HashJson<T>(T value) => HashUtf8(JsonSerializer.Serialize(value));
  private static string HashUtf8(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
  private static string Amount(decimal value) => value.ToString(CultureInfo.InvariantCulture);
  private static bool IsSha256(string? value) => value is { Length: 64 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
  private static bool IsCurrency(string value) => value.Length == 3 && value.All(c => c is >= 'A' and <= 'Z');
  private static bool SameCurrency(string left, string right) =>
    string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

  private sealed record SourceBlocker(string Code, string Message);
}
