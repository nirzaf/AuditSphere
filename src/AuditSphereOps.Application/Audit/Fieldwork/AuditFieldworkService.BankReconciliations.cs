using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditFieldworkService
{
  public static async Task<CommandResult<BankReconciliationValue>> CreateBankReconciliationAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateBankReconciliationRequest request,
    CancellationToken ct = default)
  {
    if (request is null || request.EngagementId == Guid.Empty || request.LedgerScheduleId == Guid.Empty ||
        request.StatementScheduleId == Guid.Empty || request.LedgerScheduleId == request.StatementScheduleId ||
        !ValidItems(request.Items))
      return Invalid<BankReconciliationValue>("A bank reconciliation requires distinct typed source items and evidence.");

    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<BankReconciliationValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    await LockConfirmationScopeAsync(db, actor, request.EngagementId, ct);
    auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<BankReconciliationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.ProcedureId is not null && !await IsApplicableProcedureAsync(db, actor.FirmId, auth.ClientId,
        request.EngagementId, request.ProcedureId.Value, ct))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GateBlocked, "The linked procedure is not applicable.");

    var schedules = await db.AuditSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId &&
      (x.Id == request.LedgerScheduleId || x.Id == request.StatementScheduleId)).ToListAsync(ct);
    if (schedules.Count != 2 || schedules.Any(x => x.Status != AuditScheduleStatuses.Approved))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GateBlocked, "Bank reconciliation sources must be independently approved.");
    var generation = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    if (schedules.Any(x => x.InputGeneration != generation))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GenerationStale,
        "The approved bank schedule inputs are stale.");
    var ledger = schedules.Single(x => x.Id == request.LedgerScheduleId);
    var statement = schedules.Single(x => x.Id == request.StatementScheduleId);
    if (!string.Equals(ledger.ScheduleType, "BANK_LEDGER", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(statement.ScheduleType, "BANK_STATEMENT", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(ledger.Currency, statement.Currency, StringComparison.OrdinalIgnoreCase) ||
        !MatchesAsOfDate(ledger.AsOfDate, request.AsOfDate) || !MatchesAsOfDate(statement.AsOfDate, request.AsOfDate))
      return Invalid<BankReconciliationValue>("Bank ledger and statement sources must use the same approved date and currency.");

    var normalizedItems = NormalizeBankItems(request.Items);
    if (!ValidBankItemLineage(normalizedItems, ledger.Id, statement.Id))
      return Invalid<BankReconciliationValue>("Ledger, statement, timing and proposed-correction items require distinct typed lineage.");

    var proposedJournalIds = normalizedItems.Where(x => x.Input.ProposedJournalId is not null)
      .Select(x => x.Input.ProposedJournalId!.Value).Distinct().ToArray();
    if (proposedJournalIds.Length > 0)
    {
      var journals = await db.AdjustmentJournals.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId && proposedJournalIds.Contains(x.Id)).ToListAsync(ct);
      if (journals.Count != proposedJournalIds.Length || journals.Any(x => x.Status != "Draft" ||
          !string.Equals(x.Currency, ledger.Currency, StringComparison.OrdinalIgnoreCase)))
        return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GateBlocked,
          "Proposed bank corrections must reference same-currency draft journals in this engagement.");
    }

    var (timingTotal, proposedTotal, residual) = CalculateBankReconciliation(ledger, statement, normalizedItems);
    var reconciliation = new AuditBankReconciliation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ProcedureId = request.ProcedureId, LedgerScheduleId = ledger.Id, StatementScheduleId = statement.Id,
      AsOfDate = request.AsOfDate, Currency = ledger.Currency, LedgerBalance = ledger.SignedControlTotal,
      StatementBalance = statement.SignedControlTotal, TimingItemTotal = timingTotal,
      ProposedCorrectionTotal = proposedTotal, Residual = residual, InputGeneration = generation,
      Status = residual == 0m ? AuditBankReconciliationStatuses.Reconciled : AuditBankReconciliationStatuses.Unreconciled,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditBankReconciliations.Add(reconciliation);
    db.AuditBankReconciliationItems.AddRange(normalizedItems.Select(x => new AuditBankReconciliationItem
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      BankReconciliationId = reconciliation.Id, SourceScheduleId = x.Input.SourceScheduleId,
      ProposedJournalId = x.Input.ProposedJournalId, StableItemId = x.StableItemId, ItemType = x.ItemType,
      SignedAmount = MoneyPolicy.Normalize(x.Input.SignedAmount), Currency = ledger.Currency,
      Description = x.Description, SourceReference = x.SourceReference, EvidenceReference = x.EvidenceReference,
      CreatedAt = reconciliation.CreatedAt
    }));
    auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<BankReconciliationValue>.Fail(auth.ErrorCode!, auth.Message!);
    await db.SaveChangesAsync(ct);
    auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<BankReconciliationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<BankReconciliationValue>.Ok(ToBankReconciliationValue(reconciliation));
  }

  internal static async Task<CommandResult<BankReconciliationValue>> PreviewBankReconciliationAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateBankReconciliationRequest? request, CancellationToken ct = default)
  {
    if (request is null || request.EngagementId == Guid.Empty || request.LedgerScheduleId == Guid.Empty ||
        request.StatementScheduleId == Guid.Empty || request.LedgerScheduleId == request.StatementScheduleId ||
        !ValidItems(request.Items))
      return Invalid<BankReconciliationValue>("A bank reconciliation requires distinct typed source items and evidence.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult<BankReconciliationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.ProcedureId is not null && !await IsApplicableProcedureAsync(db, actor.FirmId, auth.ClientId,
        request.EngagementId, request.ProcedureId.Value, ct))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GateBlocked, "The linked procedure is not applicable.");
    var schedules = await db.AuditSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId &&
      (x.Id == request.LedgerScheduleId || x.Id == request.StatementScheduleId)).ToListAsync(ct);
    if (schedules.Count != 2 || schedules.Any(x => x.Status != AuditScheduleStatuses.Approved))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GateBlocked, "Bank reconciliation sources must be independently approved.");
    var generation = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    if (schedules.Any(x => x.InputGeneration != generation))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GenerationStale, "The approved bank schedule inputs are stale.");
    var ledger = schedules.Single(x => x.Id == request.LedgerScheduleId);
    var statement = schedules.Single(x => x.Id == request.StatementScheduleId);
    if (!string.Equals(ledger.ScheduleType, "BANK_LEDGER", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(statement.ScheduleType, "BANK_STATEMENT", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(ledger.Currency, statement.Currency, StringComparison.OrdinalIgnoreCase) ||
        !MatchesAsOfDate(ledger.AsOfDate, request.AsOfDate) || !MatchesAsOfDate(statement.AsOfDate, request.AsOfDate))
      return Invalid<BankReconciliationValue>("Bank ledger and statement sources must use the same approved date and currency.");
    var normalized = NormalizeBankItems(request.Items);
    if (!ValidBankItemLineage(normalized, ledger.Id, statement.Id))
      return Invalid<BankReconciliationValue>("Ledger, statement, timing and proposed-correction items require distinct typed lineage.");
    var proposedJournalIds = normalized.Where(x => x.Input.ProposedJournalId is not null).Select(x => x.Input.ProposedJournalId!.Value).Distinct().ToArray();
    if (proposedJournalIds.Length > 0)
    {
      var journals = await db.AdjustmentJournals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId &&
        x.EngagementId == request.EngagementId && proposedJournalIds.Contains(x.Id)).ToListAsync(ct);
      if (journals.Count != proposedJournalIds.Length || journals.Any(x => x.Status != "Draft" ||
          !string.Equals(x.Currency, ledger.Currency, StringComparison.OrdinalIgnoreCase)))
        return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GateBlocked,
          "Proposed bank corrections must reference same-currency draft journals in this engagement.");
    }
    var totals = CalculateBankReconciliation(ledger, statement, normalized);
    return CommandResult<BankReconciliationValue>.Ok(new(Guid.Empty,
      totals.Residual == 0m ? AuditBankReconciliationStatuses.Reconciled : AuditBankReconciliationStatuses.Unreconciled,
      ledger.Currency, ledger.SignedControlTotal, statement.SignedControlTotal, totals.TimingTotal, totals.ProposedTotal, totals.Residual));
  }

  public static async Task<CommandResult<BankReconciliationValue>> ReviewBankReconciliationAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewBankReconciliationRequest request,
    CancellationToken ct = default)
  {
    if (request is null || request.BankReconciliationId == Guid.Empty || !ValidText(request.Decision, 30) ||
        !ValidText(request.Conclusion, 4000))
      return Invalid<BankReconciliationValue>("Bank reconciliation review requires a decision and conclusion.");
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditBankReconciliationStatuses.Approved or AuditBankReconciliationStatuses.ChangesRequired) ||
        string.IsNullOrWhiteSpace(request.Conclusion))
      return Invalid<BankReconciliationValue>("Bank reconciliation review requires a decision and conclusion.");
    var existing = await db.AuditBankReconciliations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.BankReconciliationId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<BankReconciliationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (existing is null)
      return Denied<BankReconciliationValue>();
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    await LockConfirmationScopeAsync(db, actor, existing.EngagementId, ct);
    var parent = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == existing.EngagementId, ct);
    if (parent is null || parent.PracticeClientId != existing.ClientId)
      return Denied<BankReconciliationValue>();
    var live = await db.AuditBankReconciliations.FromSqlInterpolated(
      $"SELECT * FROM audit_bank_reconciliations WHERE firm_id = {actor.FirmId} AND client_id = {existing.ClientId} AND engagement_id = {existing.EngagementId} AND id = {request.BankReconciliationId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (live is null) return Denied<BankReconciliationValue>();
    auth = await AuthorizeEntityAsync(db, actor, live, ReviewRoles, ct);
    if (!auth.Succeeded) return CommandResult<BankReconciliationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (live.CreatedByUserId == actor.UserId)
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same bank reconciliation.");
    if (live.Status == AuditBankReconciliationStatuses.Approved)
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.ProtectedState, "An approved bank reconciliation is immutable.");
    var currentGeneration = await CurrentGenerationAsync(db, live.ClientId, live.FirmId, ct);
    if (live.InputGeneration != currentGeneration)
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GenerationStale, "The bank reconciliation inputs are stale.");
    if (decision == AuditBankReconciliationStatuses.Approved && live.Residual != 0m)
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GateBlocked, "An unreconciled bank residual cannot be approved.");
    var sources = await db.AuditSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == live.ClientId && x.EngagementId == live.EngagementId &&
      (x.Id == live.LedgerScheduleId || x.Id == live.StatementScheduleId)).ToListAsync(ct);
    if (sources.Count != 2 || sources.Any(x => x.Status != AuditScheduleStatuses.Approved))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GenerationStale, "The approved bank sources changed; refresh the reconciliation.");
    if (sources.Any(x => x.InputGeneration != currentGeneration))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GenerationStale,
        "The approved bank schedule inputs are stale; refresh the reconciliation.");
    live.Status = decision;
    live.Conclusion = request.Conclusion.Trim();
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<BankReconciliationValue>.Ok(ToBankReconciliationValue(live));
  }

  private static bool ValidText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
    !value.Any(char.IsControl);

  private static bool ValidItems(IReadOnlyList<BankReconciliationItemInput?>? items) => items is { Count: > 0 and <= 1000 } &&
    items.All(x => x is not null && ValidText(x.StableItemId, 200) && ValidText(x.Description, 1000) &&
      ValidText(x.SourceReference, 500) && ValidText(x.EvidenceReference, 2000) && ValidText(x.ItemType, 30) &&
      AuditBankReconciliationItemTypes.All.Contains(x.ItemType.Trim().ToUpperInvariant()) &&
      decimal.Round(x.SignedAmount, 6) == x.SignedAmount && x.SignedAmount is >= -9_999_999_999_999.999999m and <= 9_999_999_999_999.999999m) &&
    items.Select(x => x!.StableItemId.Trim()).Distinct(StringComparer.Ordinal).Count() == items.Count;

  private static (BankReconciliationItemInput Input, string ItemType, string StableItemId, string Description,
    string SourceReference, string EvidenceReference)[] NormalizeBankItems(IReadOnlyList<BankReconciliationItemInput?> items) => items.Select(x =>
      (Input: x!, ItemType: x!.ItemType.Trim().ToUpperInvariant(), StableItemId: x.StableItemId.Trim(),
        Description: x.Description.Trim(), SourceReference: x.SourceReference.Trim(), EvidenceReference: x.EvidenceReference.Trim())).ToArray();

  private static bool ValidBankItemLineage(
    IReadOnlyList<(BankReconciliationItemInput Input, string ItemType, string StableItemId, string Description, string SourceReference, string EvidenceReference)> items,
    Guid ledgerId, Guid statementId) => !items.Any(x =>
      x.ItemType == AuditBankReconciliationItemTypes.Ledger && x.Input.SourceScheduleId != ledgerId ||
      x.ItemType == AuditBankReconciliationItemTypes.Statement && x.Input.SourceScheduleId != statementId ||
      x.ItemType == AuditBankReconciliationItemTypes.Timing && x.Input.SourceScheduleId is not null &&
        x.Input.SourceScheduleId != ledgerId && x.Input.SourceScheduleId != statementId ||
      x.ItemType == AuditBankReconciliationItemTypes.ProposedCorrection && x.Input.ProposedJournalId is null ||
      x.ItemType != AuditBankReconciliationItemTypes.ProposedCorrection && x.Input.ProposedJournalId is not null);

  private static (decimal TimingTotal, decimal ProposedTotal, decimal Residual) CalculateBankReconciliation(
    AuditSchedule ledger, AuditSchedule statement,
    IReadOnlyList<(BankReconciliationItemInput Input, string ItemType, string StableItemId, string Description, string SourceReference, string EvidenceReference)> items)
  {
    var timingTotal = MoneyPolicy.Normalize(items.Where(x => x.ItemType == AuditBankReconciliationItemTypes.Timing).Sum(x => x.Input.SignedAmount));
    var proposedTotal = MoneyPolicy.Normalize(items.Where(x => x.ItemType == AuditBankReconciliationItemTypes.ProposedCorrection).Sum(x => x.Input.SignedAmount));
    var residual = MoneyPolicy.Normalize(statement.SignedControlTotal - ledger.SignedControlTotal - timingTotal);
    return (timingTotal, proposedTotal, residual);
  }

  private static BankReconciliationValue ToBankReconciliationValue(AuditBankReconciliation reconciliation) =>
    new(reconciliation.Id, reconciliation.Status, reconciliation.Currency, reconciliation.LedgerBalance,
      reconciliation.StatementBalance, reconciliation.TimingItemTotal, reconciliation.ProposedCorrectionTotal, reconciliation.Residual);
}
