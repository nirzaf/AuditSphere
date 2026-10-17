using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record FinancialScheduleSourceLine(Guid LineId, Guid TransactionId, string Journal,
  string AccountCode, DateOnly PostingDate, string SignedAmount, string Currency, string LineHash);
public sealed record FinancialScheduleSpecialistSource(Guid ScheduleId, string ComponentCode, string SignedAmount,
  string Currency, string ScheduleHash, long InputGeneration, long Revision);
public sealed record FinancialScheduleSources(Guid PeriodId, Guid? BookId, Guid BatchId, Guid AcceptanceId,
  string SourceHash, long InputGeneration, string Currency, int Page, bool HasMore,
  IReadOnlyList<FinancialScheduleSourceLine> Lines, IReadOnlyList<FinancialScheduleSpecialistSource> SpecialistSources);

/// <summary>Bounded source discovery for schedule preparation, never allocation or professional approval.</summary>
public static class FinancialScheduleSourceQuery
{
  public const int PageSize = 25;
  public static async Task<CommandResult<FinancialScheduleSources>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid periodId, int page = 1, CancellationToken ct = default)
  {
    if (page is < 1 or > 1000 || periodId == Guid.Empty)
      return CommandResult<FinancialScheduleSources>.Fail(ErrorCodes.Accounting.PackageInvalid, "Choose a reporting period and a source page between 1 and 1000.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Denied();
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, engagement.PracticeClientId,
      engagementId, ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<FinancialScheduleSources>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == engagement.PracticeClientId && x.Id == periodId, ct);
    if (period is null) return Denied();
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.Id == engagement.PracticeClientId, ct);
    if (safety is null) return Blocked("The current client accounting generation is unavailable.");
    var selected = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(db, actor, engagement.PracticeClientId,
      engagementId, periodId, ct);
    if (!selected.Succeeded) return CommandResult<FinancialScheduleSources>.Fail(selected.ErrorCode!, selected.Message!);
    if (selected.Value?.ImportBatchId is not Guid batchId)
      return Blocked("Independently accept the general ledger for this engagement before inspecting schedule sources.");
    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId && x.Id == batchId &&
      x.PeriodId == periodId && x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED", ct);
    if (batch is null || batch.Currency != period.Currency || !SourceAcceptanceWorkspace.ValidHash(batch.NormalizedDatasetDigest) ||
        batch.NormalizedDatasetDigest != selected.Value.SourceIdentityHash)
      return Blocked("The selected accepted GL must match this period, currency and sealed source identity.");
    var rows = await (from line in db.GeneralLedgerLines.AsNoTracking()
      join transaction in db.GeneralLedgerTransactions.AsNoTracking() on line.TransactionId equals transaction.Id
      where line.FirmId == actor.FirmId && line.ClientId == engagement.PracticeClientId && line.EngagementId == engagementId && line.ImportBatchId == batchId &&
        transaction.FirmId == actor.FirmId && transaction.ClientId == engagement.PracticeClientId && transaction.EngagementId == engagementId && transaction.ImportBatchId == batchId &&
        transaction.Currency == batch.Currency && transaction.PostingDate >= period.StartDate && transaction.PostingDate <= period.EndDate
      orderby transaction.PostingDate, transaction.Id, line.Id
      select new { Line = line, Transaction = transaction }).Skip((page - 1) * PageSize).Take(PageSize + 1).ToListAsync(ct);
    var latestAsset = await db.SpecialistAccountingSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId && x.PeriodId == periodId && x.Area == "ASSETS")
      .OrderByDescending(x => x.Revision).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var specialistSources = latestAsset is { Status: AccountingWorkflowStates.Approved, ReviewedByUserId: Guid reviewerId } &&
      reviewerId != latestAsset.CreatedByUserId && latestAsset.ReviewedAt is not null &&
      latestAsset.InputGeneration == safety.InputGeneration && safety.InputGeneration == selected.Value.InputGeneration &&
      AccountingAnalysisService.ValidateRetainedSpecialist(latestAsset) is null
      ? new[]
      {
        SpecialistSource(latestAsset, period, safety.InputGeneration, "DEPRECIATION", latestAsset.DepreciationAmount),
        SpecialistSource(latestAsset, period, safety.InputGeneration, "IMPAIRMENT", latestAsset.ImpairmentAmount)
      }.Where(x => x is not null).Select(x => x!).ToArray()
      : [];
    var current = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(db, actor, engagement.PracticeClientId, engagementId, periodId, ct);
    if (!current.Succeeded) return CommandResult<FinancialScheduleSources>.Fail(current.ErrorCode!, current.Message!);
    if (current.Value != selected.Value)
      return CommandResult<FinancialScheduleSources>.Fail(ErrorCodes.StaleRevision, "The accepted source changed during source inspection. Refresh before preparing a schedule.");
    var currentSafety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.Id == engagement.PracticeClientId, ct);
    if (currentSafety?.InputGeneration != safety.InputGeneration)
      return CommandResult<FinancialScheduleSources>.Fail(ErrorCodes.StaleRevision, "Client accounting inputs changed during source inspection. Refresh before preparing a schedule.");
    var currentAssetScheduleId = await db.SpecialistAccountingSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId && x.PeriodId == periodId && x.Area == "ASSETS")
      .OrderByDescending(x => x.Revision).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    if (currentAssetScheduleId != latestAsset?.Id)
      return CommandResult<FinancialScheduleSources>.Fail(ErrorCodes.StaleRevision, "The asset schedule changed during source inspection. Refresh before preparing a schedule.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, engagement.PracticeClientId,
      engagementId, ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<FinancialScheduleSources>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<FinancialScheduleSources>.Ok(new(periodId, batch.BookId, batchId, selected.Value.DecisionId,
      selected.Value.SourceIdentityHash, selected.Value.InputGeneration, batch.Currency, page, rows.Count > PageSize,
      rows.Take(PageSize).Select(x => new FinancialScheduleSourceLine(x.Line.Id, x.Transaction.Id, x.Transaction.StableJournalId,
        x.Line.AccountCode, x.Transaction.PostingDate, x.Line.FunctionalAmount.ToString("0.######", CultureInfo.InvariantCulture), batch.Currency,
        Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new { Version = "schedule-gl-line.v1", Source = selected.Value.SourceIdentityHash,
          Batch = batch.Id, Period = periodId, batch.BookId, x.Line, x.Transaction })))).ToArray(), specialistSources));
  }
  private static FinancialScheduleSpecialistSource? SpecialistSource(SpecialistAccountingSchedule schedule, ClientReportingPeriod period,
    long inputGeneration, string component, decimal amount) => amount > 0m
      ? new(schedule.Id, component, amount.ToString("0.######", CultureInfo.InvariantCulture), period.Currency,
        FinancialStatementService.CashFlowScheduleSourceHash(schedule, period, inputGeneration, component, amount), inputGeneration, schedule.Revision)
      : null;
  private static CommandResult<FinancialScheduleSources> Denied() => CommandResult<FinancialScheduleSources>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  private static CommandResult<FinancialScheduleSources> Blocked(string message) => CommandResult<FinancialScheduleSources>.Fail(ErrorCodes.GateBlocked, message);
}
