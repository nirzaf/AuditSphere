using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class ConfirmationWorkspace
{
  private const int BankSourcePageSizeMax = 5;

  public static async Task<CommandResult<ConfirmationBankSourcePage>> BankSourcesAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, int page = 0, int pageSize = 5,
    CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || page is < 0 or > 100000 || pageSize is < 1 or > BankSourcePageSizeMax)
      return CommandResult<ConfirmationBankSourcePage>.Fail("request.invalid",
        "Choose a bounded Bank source page with at most five sources.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<ConfirmationBankSourcePage>.Fail(ErrorCodes.GateBlocked,
        "Refresh Bank sources outside an active transaction.");
    if (!(await Auth(db, actor, engagementId, PrepareRoles, false, ct)).Succeeded)
      return Denied<ConfirmationBankSourcePage>();
    var clientId = await EngagementClientIdAsync(db, actor.FirmId, engagementId, ct);
    if (clientId is null) return Denied<ConfirmationBankSourcePage>();

    int total;
    List<AuditBankReconciliation> banks;
    var initial = new Dictionary<Guid, CommandResult<ConfirmationBankSourceCapture>>();
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      if (await EngagementClientIdAsync(db, actor.FirmId, engagementId, ct) != clientId)
        return Denied<ConfirmationBankSourcePage>();
      var query = db.AuditBankReconciliations.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.EngagementId == engagementId && x.Status == AuditBankReconciliationStatuses.Approved);
      total = await query.CountAsync(ct);
      banks = await query.OrderByDescending(x => x.AsOfDate).ThenBy(x => x.Id)
        .Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
      foreach (var bank in banks)
        initial[bank.Id] = await ConfirmationBankSourceResolver.CaptureCurrentAsync(
          db, actor.FirmId, clientId.Value, engagementId, bank.Id, ct);
      await snapshot.CommitAsync(ct);
    }

    var freshResults = new Dictionary<Guid, CommandResult<ConfirmationBankSourceCapture>>();
    foreach (var bank in banks)
      freshResults[bank.Id] = await ConfirmationBankSourceResolver.CaptureCurrentAsync(
        db, actor.FirmId, clientId.Value, engagementId, bank.Id, ct);
    var currentGeneration = await ConfirmationBankSourceResolver.CurrentGenerationAsync(db, actor.FirmId, clientId.Value, ct);
    var sources = banks.Select(bank => ToSourceOption(bank, initial[bank.Id], freshResults[bank.Id], currentGeneration)).ToList();

    if (await EngagementClientIdAsync(db, actor.FirmId, engagementId, ct) != clientId ||
        !(await Auth(db, actor, engagementId, PrepareRoles, false, ct)).Succeeded)
      return Denied<ConfirmationBankSourcePage>();
    return CommandResult<ConfirmationBankSourcePage>.Ok(
      new ConfirmationBankSourcePage(engagementId, page, pageSize, total, sources));
  }

  public static async Task<CommandResult<ConfirmationBankSourceOption>> BankSourceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid reconciliationId,
    CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || reconciliationId == Guid.Empty)
      return CommandResult<ConfirmationBankSourceOption>.Fail("request.invalid", "A Bank reconciliation id is required.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<ConfirmationBankSourceOption>.Fail(ErrorCodes.GateBlocked,
        "Refresh the Bank source outside an active transaction.");
    if (!(await Auth(db, actor, engagementId, PrepareRoles, false, ct)).Succeeded)
      return Denied<ConfirmationBankSourceOption>();
    var clientId = await EngagementClientIdAsync(db, actor.FirmId, engagementId, ct);
    if (clientId is null) return Denied<ConfirmationBankSourceOption>();

    AuditBankReconciliation? bank;
    CommandResult<ConfirmationBankSourceCapture> initial;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      if (await EngagementClientIdAsync(db, actor.FirmId, engagementId, ct) != clientId)
        return Denied<ConfirmationBankSourceOption>();
      bank = await db.AuditBankReconciliations.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == reconciliationId && x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId, ct);
      if (bank is null) return Denied<ConfirmationBankSourceOption>();
      initial = await ConfirmationBankSourceResolver.CaptureCurrentAsync(
        db, actor.FirmId, clientId.Value, engagementId, reconciliationId, ct);
      await snapshot.CommitAsync(ct);
    }

    var fresh = await ConfirmationBankSourceResolver.CaptureCurrentAsync(
      db, actor.FirmId, clientId.Value, engagementId, reconciliationId, ct);
    var currentGeneration = await ConfirmationBankSourceResolver.CurrentGenerationAsync(db, actor.FirmId, clientId.Value, ct);
    var result = ToSourceOption(bank, initial, fresh, currentGeneration);
    if (await EngagementClientIdAsync(db, actor.FirmId, engagementId, ct) != clientId ||
        !(await Auth(db, actor, engagementId, PrepareRoles, false, ct)).Succeeded)
      return Denied<ConfirmationBankSourceOption>();
    return CommandResult<ConfirmationBankSourceOption>.Ok(result);
  }

  private static ConfirmationBankSourceOption ToSourceOption(AuditBankReconciliation bank,
    CommandResult<ConfirmationBankSourceCapture> initial,
    CommandResult<ConfirmationBankSourceCapture> fresh, long? finalGeneration)
  {
    var current = fresh.Succeeded ? fresh.Value : null;
    var previous = initial.Succeeded ? initial.Value : null;
    var observedBank = current?.Bank ?? previous?.Bank ?? bank;
    var ledger = current?.LedgerSchedule ?? previous?.LedgerSchedule;
    var sameSnapshot = previous is not null && current is not null && previous.Eligible && current.Eligible &&
      previous.Bank.Id == current.Bank.Id && previous.CurrentGeneration == current.CurrentGeneration &&
      string.Equals(previous.SnapshotSha256, current.SnapshotSha256, StringComparison.Ordinal);
    var contextCurrent = finalGeneration is not null && current is not null &&
      current.CurrentGeneration == finalGeneration.Value;
    if (sameSnapshot && contextCurrent)
      return new ConfirmationBankSourceOption(observedBank.Id, Date(observedBank.AsOfDate),
        ledger?.EntityIdentifier ?? string.Empty, observedBank.Currency, Amount(observedBank.LedgerBalance),
        current!.SnapshotSha256, observedBank.LedgerScheduleId, observedBank.StatementScheduleId,
        Generation(observedBank.InputGeneration), true, null);

    var blocker = !initial.Succeeded ? initial.Message : !fresh.Succeeded ? fresh.Message :
      current?.Blocker ?? "The Bank source changed during this read. Refresh and review it again.";
    if (finalGeneration is null) blocker = "The current client generation is unavailable.";
    else if (!contextCurrent) blocker = "The Bank source changed during this read. Refresh and review it again.";
    return new ConfirmationBankSourceOption(observedBank.Id, Date(observedBank.AsOfDate),
      ledger?.EntityIdentifier ?? string.Empty, observedBank.Currency, null, null,
      observedBank.LedgerScheduleId, observedBank.StatementScheduleId, Generation(observedBank.InputGeneration),
      false, blocker);
  }

  private static ConfirmationBankSourceOption Blocked(ConfirmationBankSourceOption option, string blocker) =>
    option with { BookedAmount = null, SnapshotSha256 = null, Eligible = false, Blocker = blocker };

  private static async Task<Guid?> EngagementClientIdAsync(
    IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct) =>
    await db.Engagements.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == engagementId)
      .Select(x => (Guid?)x.PracticeClientId).SingleOrDefaultAsync(ct);

  private static async Task<ConfirmationBookedSourceView?> BookedSourceViewAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, AuditConfirmationCase confirmation,
    CancellationToken ct)
  {
    if (!(await Auth(db, actor, engagementId, ReadRoles, false, ct)).Succeeded) return null;
    var retained = await db.AuditConfirmationBookedSources.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == confirmation.ClientId && x.EngagementId == engagementId &&
      x.ConfirmationCaseId == confirmation.Id, ct);
    if (retained is null) return null;

    var parsed = ParseRetained(retained, confirmation);
    if (parsed is null)
    {
      if (!(await Auth(db, actor, engagementId, ReadRoles, false, ct)).Succeeded) return null;
      return Unavailable(retained, "The retained Bank source snapshot is invalid.");
    }

    if (db.Database.CurrentTransaction is not null)
    {
      var currentInTransaction = await ConfirmationBankSourceResolver.CaptureCurrentAsync(
        db, actor.FirmId, confirmation.ClientId, engagementId, retained.BankReconciliationId, ct);
      var view = CurrentView(retained, parsed, currentInTransaction, currentInTransaction,
        null, transactionSnapshot: true);
      return (await Auth(db, actor, engagementId, ReadRoles, false, ct)).Succeeded ? view : null;
    }

    CommandResult<ConfirmationBankSourceCapture> first;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      first = await ConfirmationBankSourceResolver.CaptureCurrentAsync(
        db, actor.FirmId, confirmation.ClientId, engagementId, retained.BankReconciliationId, ct);
      await snapshot.CommitAsync(ct);
    }
    var fresh = await ConfirmationBankSourceResolver.CaptureCurrentAsync(
      db, actor.FirmId, confirmation.ClientId, engagementId, retained.BankReconciliationId, ct);
    var currentGeneration = await ConfirmationBankSourceResolver.CurrentGenerationAsync(
      db, actor.FirmId, confirmation.ClientId, ct);
    var result = CurrentView(retained, parsed, first, fresh, currentGeneration, transactionSnapshot: false);
    return (await Auth(db, actor, engagementId, ReadRoles, false, ct)).Succeeded ? result : null;
  }

  private static ConfirmationBankSourceViewData? ParseRetained(
    AuditConfirmationBookedSource retained, AuditConfirmationCase confirmation)
  {
    if (retained.SourceKind != ConfirmationBankSourceResolver.SourceKind ||
        retained.FirmId != confirmation.FirmId || retained.ClientId != confirmation.ClientId ||
        retained.EngagementId != confirmation.EngagementId || retained.CreatedByUserId != confirmation.CreatedByUserId ||
        retained.CreatedAt != confirmation.CreatedAt || !IsSha256(retained.SnapshotSha256) ||
        string.IsNullOrEmpty(retained.SnapshotJson) || confirmation.AreaCode != AuditAreaCodes.CashBank)
      return null;
    ConfirmationBankSourceSnapshot? snapshot;
    try
    {
      snapshot = JsonSerializer.Deserialize<ConfirmationBankSourceSnapshot>(retained.SnapshotJson);
    }
    catch (JsonException)
    {
      return null;
    }
    if (snapshot is null || snapshot.Bank is null || snapshot.LedgerSchedule is null ||
        snapshot.StatementSchedule is null || snapshot.Items is null ||
        snapshot.Version != ConfirmationBankSourceResolver.SnapshotVersion ||
        Encoding.UTF8.GetByteCount(retained.SnapshotJson) > 16 * 1024 * 1024 ||
        !string.Equals(HashUtf8(retained.SnapshotJson), retained.SnapshotSha256, StringComparison.Ordinal) ||
        snapshot.Bank.Id != retained.BankReconciliationId || snapshot.Bank.FirmId != retained.FirmId ||
        snapshot.Bank.ClientId != retained.ClientId || snapshot.Bank.EngagementId != retained.EngagementId ||
        snapshot.Bank.LedgerScheduleId != retained.LedgerScheduleId ||
        snapshot.Bank.StatementScheduleId != retained.StatementScheduleId ||
        snapshot.LedgerSchedule.Id != retained.LedgerScheduleId ||
        snapshot.StatementSchedule.Id != retained.StatementScheduleId ||
        snapshot.LedgerSchedule.FirmId != retained.FirmId || snapshot.LedgerSchedule.ClientId != retained.ClientId ||
        snapshot.LedgerSchedule.EngagementId != retained.EngagementId ||
        snapshot.StatementSchedule.FirmId != retained.FirmId || snapshot.StatementSchedule.ClientId != retained.ClientId ||
        snapshot.StatementSchedule.EngagementId != retained.EngagementId ||
        snapshot.LedgerSchedule.ScheduleType != "BANK_LEDGER" ||
        snapshot.StatementSchedule.ScheduleType != "BANK_STATEMENT" ||
        snapshot.Bank.Status != AuditBankReconciliationStatuses.Approved ||
        snapshot.Bank.ReviewedByUserId == snapshot.Bank.CreatedByUserId ||
        snapshot.LedgerSchedule.Status != AuditScheduleStatuses.Approved ||
        snapshot.StatementSchedule.Status != AuditScheduleStatuses.Approved ||
        string.IsNullOrWhiteSpace(snapshot.LedgerSchedule.CompletenessDecision) ||
        string.IsNullOrWhiteSpace(snapshot.StatementSchedule.CompletenessDecision) ||
        snapshot.Items.Count == 0 || !IsSha256(snapshot.LedgerRowsSha256) || !IsSha256(snapshot.StatementRowsSha256) ||
        !IsSha256(snapshot.LedgerSchedule.SourceHash) || !IsSha256(snapshot.StatementSchedule.SourceHash) ||
        snapshot.LedgerBatch is null || snapshot.LedgerSchedule.SourceImportBatchId != snapshot.LedgerBatch.Id ||
        snapshot.LedgerBatch.FirmId != retained.FirmId || snapshot.LedgerBatch.ClientId != retained.ClientId ||
        snapshot.LedgerBatch.EngagementId != retained.EngagementId ||
        snapshot.LedgerBatch.Status != "SEALED" || snapshot.LedgerBatch.SourceKind != AccountingSourceKinds.GeneralLedger ||
        !IsSha256(snapshot.LedgerBatch.NormalizedDatasetDigest) ||
        snapshot.StatementSchedule.SourceImportBatchId != snapshot.StatementBatch?.Id ||
        snapshot.StatementBatch is { } statementBatch &&
          (statementBatch.FirmId != retained.FirmId || statementBatch.ClientId != retained.ClientId ||
           statementBatch.EngagementId != retained.EngagementId || statementBatch.Status != "SEALED" ||
           statementBatch.SourceKind != AccountingSourceKinds.GeneralLedger ||
           !IsSha256(statementBatch.NormalizedDatasetDigest)))
      return null;

    if (!decimal.TryParse(snapshot.Bank.LedgerBalance, NumberStyles.Number, CultureInfo.InvariantCulture, out var booked) ||
        !decimal.TryParse(snapshot.Bank.StatementBalance, NumberStyles.Number, CultureInfo.InvariantCulture, out var statementBalance) ||
        !decimal.TryParse(snapshot.Bank.TimingItemTotal, NumberStyles.Number, CultureInfo.InvariantCulture, out var timingTotal) ||
        !decimal.TryParse(snapshot.Bank.ProposedCorrectionTotal, NumberStyles.Number, CultureInfo.InvariantCulture, out var proposedTotal) ||
        !decimal.TryParse(snapshot.Bank.Residual, NumberStyles.Number, CultureInfo.InvariantCulture, out var residual) ||
        !decimal.TryParse(snapshot.LedgerSchedule.SignedControlTotal, NumberStyles.Number, CultureInfo.InvariantCulture, out var ledgerTotal) ||
        !decimal.TryParse(snapshot.StatementSchedule.SignedControlTotal, NumberStyles.Number, CultureInfo.InvariantCulture, out var statementTotal) ||
        residual != 0m || booked != confirmation.BookedAmount || booked != ledgerTotal ||
        statementBalance != statementTotal ||
        MoneyPolicy.Normalize(statementBalance - booked - timingTotal) != residual ||
        !string.Equals(snapshot.Bank.Currency, confirmation.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(snapshot.Bank.Currency, snapshot.LedgerSchedule.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(snapshot.Bank.Currency, snapshot.StatementSchedule.Currency, StringComparison.OrdinalIgnoreCase) ||
        !MatchesDate(snapshot.LedgerSchedule.AsOfDate, snapshot.Bank.AsOfDate) ||
        !MatchesDate(snapshot.StatementSchedule.AsOfDate, snapshot.Bank.AsOfDate) ||
        snapshot.Bank.InputGeneration != confirmation.InputGeneration ||
        snapshot.Bank.InputGeneration != snapshot.LedgerSchedule.InputGeneration ||
        snapshot.Bank.InputGeneration != snapshot.StatementSchedule.InputGeneration)
      return null;

    var timingFromItems = 0m;
    var proposedFromItems = 0m;
    foreach (var item in snapshot.Items)
    {
      if (item is null || !string.Equals(item.Currency, snapshot.Bank.Currency, StringComparison.OrdinalIgnoreCase) ||
          !AuditBankReconciliationItemTypes.All.Contains(item.ItemType) ||
          item.ItemType == AuditBankReconciliationItemTypes.Ledger && item.SourceScheduleId != retained.LedgerScheduleId ||
          item.ItemType == AuditBankReconciliationItemTypes.Statement && item.SourceScheduleId != retained.StatementScheduleId ||
          item.ItemType == AuditBankReconciliationItemTypes.ProposedCorrection && item.ProposedJournalId is null ||
          item.ItemType != AuditBankReconciliationItemTypes.ProposedCorrection && item.ProposedJournalId is not null ||
          !decimal.TryParse(item.SignedAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var itemAmount))
        return null;
      if (item.ItemType == AuditBankReconciliationItemTypes.Timing) timingFromItems += itemAmount;
      if (item.ItemType == AuditBankReconciliationItemTypes.ProposedCorrection) proposedFromItems += itemAmount;
    }
    if (MoneyPolicy.Normalize(timingFromItems) != timingTotal || MoneyPolicy.Normalize(proposedFromItems) != proposedTotal)
      return null;
    return new ConfirmationBankSourceViewData(snapshot, booked);
  }

  private static ConfirmationBookedSourceView CurrentView(
    AuditConfirmationBookedSource retained, ConfirmationBankSourceViewData parsed,
    CommandResult<ConfirmationBankSourceCapture> first,
    CommandResult<ConfirmationBankSourceCapture> fresh, long? finalGeneration, bool transactionSnapshot)
  {
    string currentness;
    string? blocker = null;
    if (!first.Succeeded || !fresh.Succeeded)
    {
      currentness = "UNAVAILABLE";
      blocker = fresh.Message ?? first.Message ?? "The current Bank source is unavailable.";
    }
    else
    {
      var previous = first.Value!;
      var current = fresh.Value!;
      var changedDuringRead = !string.Equals(previous.SnapshotSha256, current.SnapshotSha256, StringComparison.Ordinal);
      var generationStale = finalGeneration is not null &&
        (current.Bank.InputGeneration != finalGeneration.Value ||
          current.LedgerSchedule.InputGeneration != finalGeneration.Value ||
          current.StatementSchedule.InputGeneration != finalGeneration.Value);
      if (finalGeneration is null && !transactionSnapshot)
      {
        currentness = "UNAVAILABLE";
        blocker = "The current client generation is unavailable.";
      }
      else if (changedDuringRead || generationStale || !current.Eligible ||
          !string.Equals(current.SnapshotSha256, retained.SnapshotSha256, StringComparison.Ordinal))
      {
        currentness = "STALE";
        blocker = current.Blocker ?? (generationStale
          ? "The retained Bank source belongs to an older client generation."
          : "The current Bank source differs from the retained source snapshot.");
      }
      else if (transactionSnapshot)
      {
        currentness = "UNAVAILABLE";
        blocker = "Currentness could not be rechecked outside the active transaction.";
      }
      else
        currentness = "CURRENT";
    }

    return new ConfirmationBookedSourceView(retained.SourceKind, retained.BankReconciliationId,
      parsed.Snapshot.Bank.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      parsed.Snapshot.Bank.LedgerBalance, parsed.Snapshot.Bank.Currency, retained.SnapshotSha256, currentness,
      retained.LedgerScheduleId, retained.StatementScheduleId, parsed.Snapshot.LedgerSchedule.SourceHash,
      parsed.Snapshot.StatementSchedule.SourceHash, blocker);
  }

  private static ConfirmationBookedSourceView Unavailable(AuditConfirmationBookedSource retained, string blocker) =>
    new(ConfirmationBankSourceResolver.SourceKind, retained.BankReconciliationId, null, null, null, retained.SnapshotSha256,
      "UNAVAILABLE", retained.LedgerScheduleId, retained.StatementScheduleId, null, null, blocker);

  private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
  private static string Amount(decimal value) => value.ToString(CultureInfo.InvariantCulture);
  private static string Generation(long value) => value.ToString(CultureInfo.InvariantCulture);
  private static bool MatchesDate(DateTimeOffset? value, DateOnly expected) =>
    value.HasValue && DateOnly.FromDateTime(value.Value.UtcDateTime) == expected;
  private static bool IsSha256(string? value) => value is { Length: 64 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static string HashUtf8(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

  private sealed record ConfirmationBankSourceViewData(ConfirmationBankSourceSnapshot Snapshot, decimal BookedAmount);
}
