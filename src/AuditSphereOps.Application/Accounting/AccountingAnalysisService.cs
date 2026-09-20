using System.Globalization;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AccountingReconciliationRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? BookId, string Area,
  Guid? TrialBalanceDatasetId, Guid? ImportBatchId, IReadOnlyList<string> AccountCodes,
  DateOnly AsOfDate);

public sealed record ReconciliationItemInput(
  string StableItemId, decimal SignedAmount, string Currency, DateOnly? ItemDate,
  string Reason, string EvidenceReference, string Disposition);

public sealed record EclAssessmentRequest(
  Guid ReconciliationId, DateOnly AsOfDate, string Method, string MethodologyVersion,
  decimal ProbabilityOfDefault, decimal LossGivenDefault, decimal ManagementOverlay,
  decimal ManagementExpectedLoss, string AssumptionsHash);

public sealed record InventoryValuationRequest(
  Guid ReconciliationId, DateOnly AsOfDate, decimal Quantity, decimal UnitCost,
  decimal NrvPerUnit, decimal ObsolescenceReserve, decimal BookAmount,
  string MethodologyVersion, string AssumptionsHash);

public sealed record SpecialistScheduleRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, string Area, string MethodologyVersion,
  decimal OpeningAmount, decimal AdditionsAmount, decimal DisposalsAmount,
  decimal DepreciationAmount, decimal ImpairmentAmount, decimal InterestAmount,
  decimal CurrentPortion, decimal NonCurrentPortion, decimal CapitalMovement,
  decimal Dividends, decimal TaxPaid, decimal ManagementAmount, string AssumptionsHash,
  string EvidenceReference);

public sealed record AnalyticalReviewRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? ComparisonPeriodId,
  string Area, string Measure, decimal CurrentAmount, decimal PriorAmount,
  decimal? BudgetAmount, string DenominatorBasis, string FormulaVersion,
  string Explanation);

public sealed record JournalRiskFlagRequest(
  Guid ClientId, Guid EngagementId, Guid ImportBatchId, Guid TransactionId,
  string RuleCode, string Reason, decimal Score, string EvidenceReference);

public sealed record ReviewAccountingEvidenceRequest(
  string Kind, Guid EvidenceId, string Decision, string? Disposition = null);

public sealed record GeneralLedgerLineProjection(
  Guid LineId, string JournalId, DateOnly PostingDate, string AccountCode,
  decimal Debit, decimal Credit, decimal FunctionalAmount, string Currency);

public sealed record GeneralLedgerPage(
  IReadOnlyList<GeneralLedgerLineProjection> Rows, bool HasNextPage);

public sealed record GeneralLedgerCompletenessRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? BookId,
  Guid TrialBalanceDatasetId, Guid ImportBatchId, string EvidenceReference);

public static class AccountingEvidenceKinds
{
  public const string Ecl = "ECL";
  public const string Inventory = "INVENTORY";
  public const string Specialist = "SPECIALIST";
  public const string Analytical = "ANALYTICAL";
  public const string JournalRisk = "JOURNAL_RISK";
}

public static class AccountingEvidenceReviewDecisions
{
  public const string Approved = "APPROVED";
  public const string ChangesRequired = "CHANGES_REQUIRED";
  public const string Rejected = "REJECTED";
  public const string Cleared = "CLEARED";
  public const string Escalated = "ESCALATED";
  public const string NotAnIssue = "NOT_AN_ISSUE";
}

public static class AccountingAnalysisService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

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
                        line.AccountCode, line.Debit, line.Credit, line.FunctionalAmount, line.OriginalCurrency))
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
        !string.Equals(dataset.Currency, batch.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(dataset.LegalEntityKey, batch.LegalEntityKey, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The TB and GL sources do not describe the same period, book, entity or currency.");
    if (await db.GeneralLedgerCompletenessBridges.AnyAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId &&
      x.TrialBalanceDatasetId == dataset.Id && x.ImportBatchId == batch.Id, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportDuplicate, "This TB and GL source pair already has a completeness bridge.");

    var tbTotals = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == dataset.Id)
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
    var gl = glTotals.ToDictionary(x => x.AccountCode, x => MoneyPolicy.Normalize(x.Total), StringComparer.Ordinal);
    var accounts = tb.Keys.Concat(gl.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
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
    var bridge = new GeneralLedgerCompletenessBridge
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, BookId = request.BookId, TrialBalanceDatasetId = dataset.Id, ImportBatchId = batch.Id,
      TrialBalanceHash = (dataset.NormalizedDatasetDigest.Length == 64 ? dataset.NormalizedDatasetDigest : dataset.Sha256Hex).ToLowerInvariant(),
      GeneralLedgerHash = batch.NormalizedDatasetDigest.ToLowerInvariant(), AccountResidualDigest = residualDigest,
      TrialBalanceAccountCount = tb.Count, GeneralLedgerAccountCount = gl.Count,
      MatchedAccountCount = residuals.Length - mismatched, MismatchedAccountCount = mismatched,
      AbsoluteResidual = absoluteResidual, CoverageStart = coverageStart, CoverageEnd = coverageEnd,
      Status = mismatched == 0 ? "RECONCILED" : "UNRECONCILED", EvidenceReference = request.EvidenceReference.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.GeneralLedgerCompletenessBridges.Add(bridge);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(bridge.Id);
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
    var currentTbHash = await db.TrialBalanceDatasets.AsNoTracking().Where(x => x.Id == bridge.TrialBalanceDatasetId)
      .Select(x => x.NormalizedDatasetDigest.Length == 64 ? x.NormalizedDatasetDigest : x.Sha256Hex).SingleOrDefaultAsync(ct);
    var currentGlHash = await db.SourceImportBatches.AsNoTracking().Where(x => x.Id == bridge.ImportBatchId)
      .Select(x => x.NormalizedDatasetDigest).SingleOrDefaultAsync(ct);
    if (!string.Equals(currentTbHash, bridge.TrialBalanceHash, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(currentGlHash, bridge.GeneralLedgerHash, StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail(ErrorCodes.ManifestMismatch, "A source digest changed after the completeness bridge was prepared.");
    bridge.Status = approve ? AccountingWorkflowStates.Approved : AccountingWorkflowStates.Rejected;
    bridge.ReviewedByUserId = actor.UserId;
    bridge.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreateReconciliationAsync(
    IClientAccountingDbContext db, ActorContext actor, AccountingReconciliationRequest request,
    CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || request.EngagementId == Guid.Empty || request.PeriodId == Guid.Empty ||
        (request.TrialBalanceDatasetId.HasValue == request.ImportBatchId.HasValue) ||
        request.ImportBatchId.HasValue && !request.BookId.HasValue ||
        string.IsNullOrWhiteSpace(request.Area) || request.AccountCodes.Count == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "A reconciliation needs one exact source, reporting book and explicit account codes.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PeriodId &&
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting period is outside the client scope.");
    var clientState = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ClientId && x.FirmId == actor.FirmId, ct);
    if (clientState is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client accounting safety state is unavailable.");
    if (request.BookId is { } bookId && !await db.ClientReportingBooks.AsNoTracking().AnyAsync(x =>
      x.Id == bookId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.PeriodId == request.PeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The selected reporting book is outside the client period scope.");
    if (request.AsOfDate < period.StartDate)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The reconciliation date cannot precede the reporting period.");
    var codes = request.AccountCodes.Select(x => x.Trim()).Where(x => x.Length > 0)
      .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (codes.Length == 0 || codes.Length != request.AccountCodes.Count(x => !string.IsNullOrWhiteSpace(x)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Account selection must be unique and non-empty.");

    decimal sourceTotal;
    decimal glTotal;
    string sourceHash;
    if (request.TrialBalanceDatasetId is { } datasetId)
    {
      var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId &&
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId &&
        x.ValidationStatus == "Accepted", ct);
      if (dataset is null)
        return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The selected TB dataset is outside the engagement or not accepted.");
      var rows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == dataset.Id && codes.Contains(x.AccountCode)).ToListAsync(ct);
      if (rows.Select(x => x.AccountCode).Distinct(StringComparer.Ordinal).Count() != codes.Length)
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The selected source does not contain every requested account code.");
      sourceTotal = MoneyPolicy.Normalize(rows.Sum(x => x.Amount));
      glTotal = sourceTotal;
      sourceHash = dataset.NormalizedDatasetDigest.Length == 64 ? dataset.NormalizedDatasetDigest : dataset.Sha256Hex;
    }
    else
    {
      var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ImportBatchId &&
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId && x.Status == "SEALED", ct);
      if (batch is null)
        return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The selected GL batch is outside the engagement or not sealed.");
      if (batch.BookId != request.BookId)
        return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The selected GL batch does not belong to the requested reporting book.");
      var lines = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.ImportBatchId == batch.Id && codes.Contains(x.AccountCode)).ToListAsync(ct);
      if (lines.Select(x => x.AccountCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Length)
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The selected GL batch does not contain every requested account code.");
      sourceTotal = MoneyPolicy.Normalize(lines.Sum(x => x.FunctionalAmount));
      glTotal = MoneyPolicy.Normalize(lines.Sum(x => x.Debit - x.Credit));
      sourceHash = batch.NormalizedDatasetDigest;
    }
    var canonical = string.Join('|', request.ClientId, request.EngagementId, request.PeriodId,
      request.Area.Trim().ToUpperInvariant(), string.Join(',', codes), sourceHash);
    var reconciliation = new AccountingReconciliation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, BookId = request.BookId, Area = request.Area.Trim().ToUpperInvariant(),
      TrialBalanceDatasetId = request.TrialBalanceDatasetId, ImportBatchId = request.ImportBatchId,
      AccountSelection = string.Join(',', codes), AsOfDate = request.AsOfDate, SourceTotal = sourceTotal, GlTotal = glTotal,
      Residual = MoneyPolicy.Normalize(glTotal - sourceTotal), SourceHash = sourceHash,
      Status = MoneyPolicy.Normalize(glTotal - sourceTotal) == 0m ? "RECONCILED" : "UNRECONCILED",
      InputGeneration = clientState.InputGeneration,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    // The input digest is kept in the row; this local canonicalization prevents two
    // identical selections from being mistaken for different evidence in callers.
    _ = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(canonical));
    db.AccountingReconciliations.Add(reconciliation);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(reconciliation.Id);
  }

  public static async Task<CommandResult> AddReconciliationItemsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid reconciliationId,
    IReadOnlyList<ReconciliationItemInput> items, CancellationToken ct = default)
  {
    var reconciliation = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reconciliationId && x.FirmId == actor.FirmId, ct);
    if (reconciliation is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, reconciliation.ClientId, reconciliation.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    if (items.Count == 0 || items.GroupBy(x => x.StableItemId.Trim(), StringComparer.Ordinal).Any(x => x.Count() > 1) ||
        items.Any(x => string.IsNullOrWhiteSpace(x.StableItemId) || string.IsNullOrWhiteSpace(x.Reason) || string.IsNullOrWhiteSpace(x.EvidenceReference)))
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Reconciling items need unique identities, reasons and evidence.");
    foreach (var item in items)
      db.AccountingReconciliationItems.Add(new AccountingReconciliationItem
      {
        Id = Guid.CreateVersion7(), FirmId = reconciliation.FirmId, ClientId = reconciliation.ClientId,
        EngagementId = reconciliation.EngagementId, ReconciliationId = reconciliation.Id, StableItemId = item.StableItemId.Trim(),
        SignedAmount = MoneyPolicy.Normalize(item.SignedAmount), Currency = item.Currency.Trim().ToUpperInvariant(),
        ItemDate = item.ItemDate, AgeDays = item.ItemDate.HasValue ? Math.Max(0, reconciliation.AsOfDate.DayNumber - item.ItemDate.Value.DayNumber) : null,
        Reason = item.Reason.Trim(), EvidenceReference = item.EvidenceReference.Trim(), Disposition = item.Disposition.Trim(),
        CreatedAt = DateTimeOffset.UtcNow
      });
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ApproveReconciliationAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid reconciliationId,
    CancellationToken ct = default)
  {
    var reconciliation = await db.AccountingReconciliations.SingleOrDefaultAsync(x => x.Id == reconciliationId && x.FirmId == actor.FirmId, ct);
    if (reconciliation is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, reconciliation.ClientId, reconciliation.EngagementId, ReviewerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    if (reconciliation.Status != "RECONCILED")
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Only an exactly reconciled source can be approved.");
    if (reconciliation.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The reconciliation preparer cannot approve the same reconciliation.");
    reconciliation.Status = AccountingWorkflowStates.Approved;
    reconciliation.ReviewedByUserId = actor.UserId;
    reconciliation.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreateEclAssessmentAsync(
    IClientAccountingDbContext db, ActorContext actor, EclAssessmentRequest request,
    CancellationToken ct = default)
  {
    if (request.Method.Trim().ToUpperInvariant() != "PROVISION_MATRIX_V1" || string.IsNullOrWhiteSpace(request.MethodologyVersion) ||
        request.ProbabilityOfDefault is < 0 or > 1 || request.LossGivenDefault is < 0 or > 1 ||
        request.ManagementOverlay < 0m || !IsSha256(request.AssumptionsHash))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only the approved ECL method with explicit assumptions is enabled.");
    var reconciliation = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ReconciliationId && x.FirmId == actor.FirmId, ct);
    if (reconciliation is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, reconciliation.ClientId, reconciliation.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (reconciliation.Status != "RECONCILED")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "ECL requires a reconciled source-bound schedule.");
    var exposure = Math.Max(0m, reconciliation.SourceTotal);
    var expected = MoneyPolicy.Normalize(exposure * request.ProbabilityOfDefault * request.LossGivenDefault + request.ManagementOverlay);
    var assessment = new EclAssessment
    {
      Id = Guid.CreateVersion7(), FirmId = reconciliation.FirmId, ClientId = reconciliation.ClientId, EngagementId = reconciliation.EngagementId,
      ReconciliationId = reconciliation.Id, Version = (await db.EclAssessments.Where(x => x.FirmId == actor.FirmId && x.ReconciliationId == reconciliation.Id)
        .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1, AsOfDate = request.AsOfDate,
      ReconciliationSourceHash = reconciliation.SourceHash, InputGeneration = reconciliation.InputGeneration,
      Method = request.Method.Trim().ToUpperInvariant(), MethodologyVersion = request.MethodologyVersion.Trim(), EligibleExposure = exposure,
      ProbabilityOfDefault = request.ProbabilityOfDefault, LossGivenDefault = request.LossGivenDefault, ManagementOverlay = request.ManagementOverlay,
      CalculatedExpectedLoss = expected, ManagementExpectedLoss = MoneyPolicy.Normalize(request.ManagementExpectedLoss),
      Difference = MoneyPolicy.Normalize(expected - request.ManagementExpectedLoss), AssumptionsHash = request.AssumptionsHash.Trim().ToLowerInvariant(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.EclAssessments.Add(assessment);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(assessment.Id);
  }

  public static async Task<CommandResult<Guid>> CreateInventoryValuationAsync(
    IClientAccountingDbContext db, ActorContext actor, InventoryValuationRequest request,
    CancellationToken ct = default)
  {
    if (request.Quantity < 0m || request.UnitCost < 0m || request.NrvPerUnit < 0m || request.ObsolescenceReserve < 0m ||
        string.IsNullOrWhiteSpace(request.MethodologyVersion) || !IsSha256(request.AssumptionsHash))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Inventory valuation needs explicit non-negative inputs and assumptions.");
    var reconciliation = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ReconciliationId && x.FirmId == actor.FirmId, ct);
    if (reconciliation is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, reconciliation.ClientId, reconciliation.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (reconciliation.Status != "RECONCILED")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Inventory valuation requires a reconciled count/cost source.");
    var calculated = MoneyPolicy.Normalize(request.Quantity * Math.Min(request.UnitCost, request.NrvPerUnit) - request.ObsolescenceReserve);
    var assessment = new InventoryValuationAssessment
    {
      Id = Guid.CreateVersion7(), FirmId = reconciliation.FirmId, ClientId = reconciliation.ClientId, EngagementId = reconciliation.EngagementId,
      ReconciliationId = reconciliation.Id, Version = (await db.InventoryValuationAssessments.Where(x => x.FirmId == actor.FirmId && x.ReconciliationId == reconciliation.Id)
        .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1, AsOfDate = request.AsOfDate,
      ReconciliationSourceHash = reconciliation.SourceHash, InputGeneration = reconciliation.InputGeneration,
      Quantity = request.Quantity, UnitCost = request.UnitCost, NrvPerUnit = request.NrvPerUnit, ObsolescenceReserve = request.ObsolescenceReserve,
      BookAmount = MoneyPolicy.Normalize(request.BookAmount), CalculatedAmount = calculated,
      Difference = MoneyPolicy.Normalize(calculated - request.BookAmount), MethodologyVersion = request.MethodologyVersion.Trim(),
      AssumptionsHash = request.AssumptionsHash.Trim().ToLowerInvariant(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.InventoryValuationAssessments.Add(assessment);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(assessment.Id);
  }

  public static async Task<CommandResult<Guid>> RecordSpecialistScheduleAsync(
    IClientAccountingDbContext db, ActorContext actor, SpecialistScheduleRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Area) || string.IsNullOrWhiteSpace(request.MethodologyVersion) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || !IsSha256(request.AssumptionsHash))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Specialist schedules need an approved method, evidence and assumptions.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.Id == request.PeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The schedule period is outside the client scope.");
    var calculated = request.OpeningAmount + request.AdditionsAmount - request.DisposalsAmount -
      request.DepreciationAmount - request.ImpairmentAmount;
    var schedule = new SpecialistAccountingSchedule
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, Area = request.Area.Trim().ToUpperInvariant(), MethodologyVersion = request.MethodologyVersion.Trim(),
      OpeningAmount = MoneyPolicy.Normalize(request.OpeningAmount), AdditionsAmount = MoneyPolicy.Normalize(request.AdditionsAmount),
      DisposalsAmount = MoneyPolicy.Normalize(request.DisposalsAmount), DepreciationAmount = MoneyPolicy.Normalize(request.DepreciationAmount),
      ImpairmentAmount = MoneyPolicy.Normalize(request.ImpairmentAmount), InterestAmount = MoneyPolicy.Normalize(request.InterestAmount),
      CurrentPortion = MoneyPolicy.Normalize(request.CurrentPortion), NonCurrentPortion = MoneyPolicy.Normalize(request.NonCurrentPortion),
      CapitalMovement = MoneyPolicy.Normalize(request.CapitalMovement), Dividends = MoneyPolicy.Normalize(request.Dividends),
      TaxPaid = MoneyPolicy.Normalize(request.TaxPaid), ManagementAmount = MoneyPolicy.Normalize(request.ManagementAmount),
      CalculatedAmount = MoneyPolicy.Normalize(calculated), Difference = MoneyPolicy.Normalize(calculated - request.ManagementAmount),
      AssumptionsHash = request.AssumptionsHash.Trim().ToLowerInvariant(), EvidenceReference = request.EvidenceReference.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.SpecialistAccountingSchedules.Add(schedule);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(schedule.Id);
  }

  public static async Task<CommandResult<Guid>> CreateAnalyticalReviewAsync(
    IClientAccountingDbContext db, ActorContext actor, AnalyticalReviewRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Area) || string.IsNullOrWhiteSpace(request.Measure) ||
        string.IsNullOrWhiteSpace(request.DenominatorBasis) || string.IsNullOrWhiteSpace(request.FormulaVersion))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Analytical review needs an explicit measure, denominator and formula version.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    decimal? ratio = request.PriorAmount == 0m ? null : MoneyPolicy.Normalize((request.CurrentAmount - request.PriorAmount) / Math.Abs(request.PriorAmount));
    var canonical = string.Join('|', request.ClientId, request.EngagementId, request.PeriodId, request.Area.Trim().ToUpperInvariant(),
      request.Measure.Trim(), request.CurrentAmount.ToString("0.000000", CultureInfo.InvariantCulture),
      request.PriorAmount.ToString("0.000000", CultureInfo.InvariantCulture), request.FormulaVersion.Trim());
    var review = new AnalyticalReview
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, ComparisonPeriodId = request.ComparisonPeriodId, Area = request.Area.Trim().ToUpperInvariant(),
      Measure = request.Measure.Trim(), CurrentAmount = MoneyPolicy.Normalize(request.CurrentAmount), PriorAmount = MoneyPolicy.Normalize(request.PriorAmount),
      BudgetAmount = request.BudgetAmount.HasValue ? MoneyPolicy.Normalize(request.BudgetAmount.Value) : null, Ratio = ratio,
      DenominatorBasis = request.DenominatorBasis.Trim(), FormulaVersion = request.FormulaVersion.Trim(),
      InputHash = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(canonical)), Explanation = request.Explanation.Trim(),
      Status = ratio.HasValue ? AccountingWorkflowStates.Draft : "INSUFFICIENT_DATA", CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AnalyticalReviews.Add(review);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(review.Id);
  }

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
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.JournalRiskFlags.Add(flag);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(flag.Id);
  }

  public static async Task<CommandResult> ReviewAccountingEvidenceAsync(
    IClientAccountingDbContext db, ActorContext actor, ReviewAccountingEvidenceRequest request,
    CancellationToken ct = default)
  {
    var kind = request.Kind.Trim().ToUpperInvariant();
    var decision = request.Decision.Trim().ToUpperInvariant();
    var reviewerDecision = decision is AccountingEvidenceReviewDecisions.Approved or
      AccountingEvidenceReviewDecisions.ChangesRequired or AccountingEvidenceReviewDecisions.Rejected;
    var riskDecision = decision is AccountingEvidenceReviewDecisions.Cleared or
      AccountingEvidenceReviewDecisions.Escalated or AccountingEvidenceReviewDecisions.NotAnIssue;
    if (request.EvidenceId == Guid.Empty ||
        (kind is not (AccountingEvidenceKinds.Ecl or AccountingEvidenceKinds.Inventory or AccountingEvidenceKinds.Specialist or
          AccountingEvidenceKinds.Analytical or AccountingEvidenceKinds.JournalRisk)) ||
        (kind == AccountingEvidenceKinds.JournalRisk ? !riskDecision : !reviewerDecision))
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The accounting evidence review target or decision is invalid.");

    Guid clientId;
    Guid engagementId;
    Guid createdByUserId;
    Guid? reconciliationId = null;
    string? recordedSourceHash = null;
    long? recordedInputGeneration = null;
    Action apply;
    switch (kind)
    {
      case AccountingEvidenceKinds.Ecl:
        var ecl = await db.EclAssessments.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (ecl is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && ecl.MethodologyVersion.Length == 0)
          return CommandResult.Fail(ErrorCodes.GateBlocked, "An ECL review needs a methodology version.");
        clientId = ecl.ClientId; engagementId = ecl.EngagementId; createdByUserId = ecl.CreatedByUserId;
        reconciliationId = ecl.ReconciliationId; recordedSourceHash = ecl.ReconciliationSourceHash;
        recordedInputGeneration = ecl.InputGeneration;
        apply = () => { ecl.Status = decision; ecl.ReviewedByUserId = actor.UserId; ecl.ReviewedAt = DateTimeOffset.UtcNow; };
        break;
      case AccountingEvidenceKinds.Inventory:
        var inventory = await db.InventoryValuationAssessments.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (inventory is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && inventory.MethodologyVersion.Length == 0)
          return CommandResult.Fail(ErrorCodes.GateBlocked, "An inventory review needs a methodology version.");
        clientId = inventory.ClientId; engagementId = inventory.EngagementId; createdByUserId = inventory.CreatedByUserId;
        reconciliationId = inventory.ReconciliationId; recordedSourceHash = inventory.ReconciliationSourceHash;
        recordedInputGeneration = inventory.InputGeneration;
        apply = () => { inventory.Status = decision; inventory.ReviewedByUserId = actor.UserId; inventory.ReviewedAt = DateTimeOffset.UtcNow; };
        break;
      case AccountingEvidenceKinds.Specialist:
        var specialist = await db.SpecialistAccountingSchedules.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (specialist is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && specialist.EvidenceReference.Length == 0)
          return CommandResult.Fail(ErrorCodes.GateBlocked, "A specialist schedule review needs evidence.");
        clientId = specialist.ClientId; engagementId = specialist.EngagementId; createdByUserId = specialist.CreatedByUserId;
        apply = () => { specialist.Status = decision; specialist.ReviewedByUserId = actor.UserId; specialist.ReviewedAt = DateTimeOffset.UtcNow; };
        break;
      case AccountingEvidenceKinds.Analytical:
        var analytical = await db.AnalyticalReviews.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (analytical is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && analytical.Ratio is null)
          return CommandResult.Fail(ErrorCodes.GateBlocked, "Insufficient analytical data cannot be approved.");
        clientId = analytical.ClientId; engagementId = analytical.EngagementId; createdByUserId = analytical.CreatedByUserId;
        apply = () => { analytical.Status = decision; analytical.ReviewedByUserId = actor.UserId; analytical.ReviewedAt = DateTimeOffset.UtcNow; };
        break;
      default:
        var risk = await db.JournalRiskFlags.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (risk is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (string.IsNullOrWhiteSpace(request.Disposition))
          return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "A journal-risk review needs a recorded disposition.");
        clientId = risk.ClientId; engagementId = risk.EngagementId; createdByUserId = risk.CreatedByUserId ?? Guid.Empty;
        apply = () =>
        {
          risk.Status = decision;
          risk.Disposition = request.Disposition.Trim();
          risk.ReviewedByUserId = actor.UserId;
          risk.ReviewedAt = DateTimeOffset.UtcNow;
        };
        break;
    }

    if (reconciliationId is { } sourceReconciliationId)
    {
      var reconciliation = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == sourceReconciliationId && x.FirmId == actor.FirmId && x.ClientId == clientId &&
        x.EngagementId == engagementId, ct);
      var currentGeneration = await db.ClientSafetyStates.AsNoTracking().Where(x =>
        x.Id == clientId && x.FirmId == actor.FirmId).Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
      if (reconciliation is null || currentGeneration is null ||
          reconciliation.Status is not ("RECONCILED" or "APPROVED") ||
          !string.Equals(reconciliation.SourceHash, recordedSourceHash, StringComparison.OrdinalIgnoreCase) ||
          currentGeneration.Value != recordedInputGeneration)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The valuation source or client input generation changed; prepare a new assessment.");
    }

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, ReviewerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    if (createdByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The evidence preparer cannot review the same evidence.");
    apply();
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static bool IsSha256(string value) => value.Trim().Length == 64 && value.Trim().All(c =>
    c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
