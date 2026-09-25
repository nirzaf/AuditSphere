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
  private const string JournalRiskCriteriaVersion = "journal-risk.v1";

  private const int MaxJournalRiskTransactions = 5_000;

  public static async Task<CommandResult<Guid>> AddJournalRiskFlagAsync(
    IClientAccountingDbContext db, ActorContext actor, JournalRiskFlagRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.RuleCode) || string.IsNullOrWhiteSpace(request.Reason) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || request.Score is < 0 or > 100)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "A journal flag needs a rule, reason, evidence and bounded score.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await db.GeneralLedgerTransactions.AnyAsync(x => x.Id == request.TransactionId && x.FirmId == actor.FirmId &&
        x.ClientId == request.ClientId && x.EngagementId == request.EngagementId && x.ImportBatchId == request.ImportBatchId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The flagged journal is outside the selected import scope.");
    var flag = new JournalRiskFlag
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      ImportBatchId = request.ImportBatchId, TransactionId = request.TransactionId, RuleCode = request.RuleCode.Trim().ToUpperInvariant(),
      Reason = request.Reason.Trim(), Score = request.Score, EvidenceReference = request.EvidenceReference.Trim(),
      SelectedForTesting = request.SelectedForTesting, ManagementExplanation = request.ManagementExplanation.Trim(),
      CorroborationReference = request.CorroborationReference.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.JournalRiskFlags.Add(flag);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(flag.Id);
  }

  public static async Task<CommandResult<IReadOnlyList<JournalRiskCandidate>>> AnalyzeJournalRiskAsync(
    IClientAccountingDbContext db, ActorContext actor, JournalRiskAnalysisRequest request,
    CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || request.EngagementId == Guid.Empty || request.ImportBatchId == Guid.Empty ||
        request.HighValueThreshold <= 0m || request.HighValueThreshold != MoneyPolicy.Normalize(request.HighValueThreshold) ||
        request.YearEndWindowDays is < 0 or > 90)
      return CommandResult<IReadOnlyList<JournalRiskCandidate>>.Fail(
        ErrorCodes.Accounting.ReconciliationRejected, "The journal risk analysis parameters are invalid.");

    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ImportBatchId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.EngagementId == request.EngagementId && x.SourceKind == "GL" && x.Status == "SEALED", ct);
    if (batch is null)
      return CommandResult<IReadOnlyList<JournalRiskCandidate>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<JournalRiskCandidate>>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == batch.PeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null || request.YearEnd < period.StartDate || request.YearEnd > period.EndDate)
      return CommandResult<IReadOnlyList<JournalRiskCandidate>>.Fail(
        ErrorCodes.Accounting.ReconciliationRejected, "The analysis year-end is outside the imported reporting period.");

    var transactions = await db.GeneralLedgerTransactions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.EngagementId == request.EngagementId && x.ImportBatchId == batch.Id)
      .OrderBy(x => x.PostingDate).ThenBy(x => x.StableJournalId).ThenBy(x => x.Id)
      .Take(MaxJournalRiskTransactions + 1).ToListAsync(ct);
    if (transactions.Count > MaxJournalRiskTransactions)
      return CommandResult<IReadOnlyList<JournalRiskCandidate>>.Fail(
        ErrorCodes.Accounting.ImportRejected, "Journal risk analysis is bounded; select a smaller sealed batch.");

    var transactionIds = transactions.Select(x => x.Id).ToArray();
    var amounts = transactionIds.Length == 0
      ? new Dictionary<Guid, decimal>()
      : await db.GeneralLedgerLines.AsNoTracking().Where(x =>
          x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId &&
          x.ImportBatchId == batch.Id && transactionIds.Contains(x.TransactionId))
        .GroupBy(x => x.TransactionId).Select(x => new { x.Key, Amount = x.Sum(line => line.Debit) })
        .ToDictionaryAsync(x => x.Key, x => MoneyPolicy.Normalize(x.Amount), ct);

    var candidates = new List<JournalRiskCandidate>();
    foreach (var transaction in transactions)
    {
      var amount = amounts.GetValueOrDefault(transaction.Id);
      var sourceOriginAvailable = !string.IsNullOrWhiteSpace(transaction.SourceUser) &&
        !string.IsNullOrWhiteSpace(transaction.SourceSystem);
      var daysFromYearEnd = Math.Abs(transaction.PostingDate.DayNumber - request.YearEnd.DayNumber);
      void Add(string ruleCode, string reason, decimal score) => candidates.Add(new JournalRiskCandidate(
        JournalRiskCriteriaVersion, transaction.Id, transaction.StableJournalId, transaction.PostingDate,
        ruleCode, reason, score, sourceOriginAvailable, amount));

      if (transaction.IsManual)
        Add("MANUAL_ENTRY", "Review indicator: manual journal requires corroboration; it is not a fraud conclusion.", 70m);
      if (transaction.IsYearEnd || daysFromYearEnd <= request.YearEndWindowDays)
        Add("YEAR_END_ENTRY", "Review indicator: posting is within the configured year-end window; it is not a fraud conclusion.", 60m);
      if (amount >= request.HighValueThreshold)
        Add("HIGH_VALUE_ENTRY", $"Review indicator: debit amount {amount.ToString("0.00", CultureInfo.InvariantCulture)} meets the configured threshold; it is not a fraud conclusion.", 80m);
      if (!string.IsNullOrWhiteSpace(transaction.ReversalReference))
        Add("REVERSAL_ENTRY", "Review indicator: journal has a reversal reference and requires linkage review; it is not a fraud conclusion.", 50m);
      if (!sourceOriginAvailable)
        Add("MISSING_SOURCE_ORIGIN", "Review indicator: source user or source system is unavailable; origin could not be corroborated.", 90m);
    }
    return CommandResult<IReadOnlyList<JournalRiskCandidate>>.Ok(candidates);
  }
}
