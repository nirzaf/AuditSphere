using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
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
    if (request.EngagementId == Guid.Empty || request.LedgerScheduleId == Guid.Empty ||
        request.StatementScheduleId == Guid.Empty || request.LedgerScheduleId == request.StatementScheduleId ||
        request.Items is null || request.Items.Count == 0 ||
        request.Items.Select(x => x.StableItemId.Trim()).Distinct(StringComparer.Ordinal).Count() != request.Items.Count ||
        request.Items.Any(x => string.IsNullOrWhiteSpace(x.StableItemId) || string.IsNullOrWhiteSpace(x.Description) ||
          string.IsNullOrWhiteSpace(x.SourceReference) || string.IsNullOrWhiteSpace(x.EvidenceReference) ||
          !AuditBankReconciliationItemTypes.All.Contains(x.ItemType.Trim().ToUpperInvariant())))
      return Invalid<BankReconciliationValue>("A bank reconciliation requires distinct typed source items and evidence.");

    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
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
    var ledger = schedules.Single(x => x.Id == request.LedgerScheduleId);
    var statement = schedules.Single(x => x.Id == request.StatementScheduleId);
    if (!string.Equals(ledger.ScheduleType, "BANK_LEDGER", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(statement.ScheduleType, "BANK_STATEMENT", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(ledger.Currency, statement.Currency, StringComparison.OrdinalIgnoreCase) ||
        !MatchesAsOfDate(ledger.AsOfDate, request.AsOfDate) || !MatchesAsOfDate(statement.AsOfDate, request.AsOfDate))
      return Invalid<BankReconciliationValue>("Bank ledger and statement sources must use the same approved date and currency.");

    var ledgerId = ledger.Id;
    var statementId = statement.Id;
    var normalizedItems = request.Items.Select(x => new
    {
      Input = x,
      ItemType = x.ItemType.Trim().ToUpperInvariant(),
      StableItemId = x.StableItemId.Trim(),
      Description = x.Description.Trim(),
      SourceReference = x.SourceReference.Trim(),
      EvidenceReference = x.EvidenceReference.Trim()
    }).ToArray();
    if (normalizedItems.Any(x =>
        x.ItemType == AuditBankReconciliationItemTypes.Ledger && x.Input.SourceScheduleId != ledgerId ||
        x.ItemType == AuditBankReconciliationItemTypes.Statement && x.Input.SourceScheduleId != statementId ||
        x.ItemType == AuditBankReconciliationItemTypes.Timing && x.Input.SourceScheduleId is not null &&
          x.Input.SourceScheduleId != ledgerId && x.Input.SourceScheduleId != statementId ||
        x.ItemType == AuditBankReconciliationItemTypes.ProposedCorrection && x.Input.ProposedJournalId is null ||
        x.ItemType != AuditBankReconciliationItemTypes.ProposedCorrection && x.Input.ProposedJournalId is not null))
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

    var generation = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    var timingTotal = MoneyPolicy.Normalize(normalizedItems.Where(x => x.ItemType == AuditBankReconciliationItemTypes.Timing)
      .Sum(x => x.Input.SignedAmount));
    var proposedTotal = MoneyPolicy.Normalize(normalizedItems.Where(x => x.ItemType == AuditBankReconciliationItemTypes.ProposedCorrection)
      .Sum(x => x.Input.SignedAmount));
    var residual = MoneyPolicy.Normalize(statement.SignedControlTotal - ledger.SignedControlTotal - timingTotal);
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
    await db.SaveChangesAsync(ct);
    return CommandResult<BankReconciliationValue>.Ok(ToBankReconciliationValue(reconciliation));
  }

  public static async Task<CommandResult<BankReconciliationValue>> ReviewBankReconciliationAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewBankReconciliationRequest request,
    CancellationToken ct = default)
  {
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
    if (existing.CreatedByUserId == actor.UserId)
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same bank reconciliation.");
    if (existing.Status == AuditBankReconciliationStatuses.Approved)
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.ProtectedState, "An approved bank reconciliation is immutable.");
    if (existing.InputGeneration != await CurrentGenerationAsync(db, existing.ClientId, existing.FirmId, ct))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GenerationStale, "The bank reconciliation inputs are stale.");
    if (decision == AuditBankReconciliationStatuses.Approved && existing.Residual != 0m)
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GateBlocked, "An unreconciled bank residual cannot be approved.");
    var sources = await db.AuditSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == existing.ClientId && x.EngagementId == existing.EngagementId &&
      (x.Id == existing.LedgerScheduleId || x.Id == existing.StatementScheduleId)).ToListAsync(ct);
    if (sources.Count != 2 || sources.Any(x => x.Status != AuditScheduleStatuses.Approved))
      return CommandResult<BankReconciliationValue>.Fail(ErrorCodes.GenerationStale, "The approved bank sources changed; refresh the reconciliation.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditBankReconciliations.SingleAsync(x => x.Id == existing.Id && x.FirmId == actor.FirmId, ct);
    live.Status = decision;
    live.Conclusion = request.Conclusion.Trim();
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<BankReconciliationValue>.Ok(ToBankReconciliationValue(live));
  }

  private static BankReconciliationValue ToBankReconciliationValue(AuditBankReconciliation reconciliation) =>
    new(reconciliation.Id, reconciliation.Status, reconciliation.Currency, reconciliation.LedgerBalance,
      reconciliation.StatementBalance, reconciliation.TimingItemTotal, reconciliation.ProposedCorrectionTotal, reconciliation.Residual);
}
