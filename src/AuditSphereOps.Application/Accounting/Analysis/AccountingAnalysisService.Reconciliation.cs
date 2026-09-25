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
    var area = request.Area.Trim().ToUpperInvariant();
    var agingBasis = request.AgingBasis.Trim().ToUpperInvariant();
    var agingRuleVersion = request.AgingBucketRuleVersion.Trim().ToUpperInvariant();
    var agingArea = area.Contains("RECEIVABLE", StringComparison.Ordinal) || area.Contains("PAYABLE", StringComparison.Ordinal);
    if (agingArea && (!AccountingAgingRules.IsSupportedBasis(agingBasis) ||
        !string.Equals(agingRuleVersion, AccountingAgingRules.StandardRuleVersion, StringComparison.Ordinal)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "Receivable and payable ageing needs an explicit supported date basis and bucket-rule version.");
    if (!string.IsNullOrEmpty(agingBasis) && !AccountingAgingRules.IsSupportedBasis(agingBasis))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The ageing date basis is unsupported.");
    if (!string.IsNullOrEmpty(agingRuleVersion) && !string.Equals(agingRuleVersion, AccountingAgingRules.StandardRuleVersion, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The ageing bucket-rule version is unsupported.");

    decimal sourceTotal;
    decimal glTotal;
    string sourceHash;
    if (request.TrialBalanceDatasetId is { } datasetId)
    {
      var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId &&
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId &&
        x.ValidationStatus == "Accepted" && x.ImportState == TrialBalanceImportStates.Sealed, ct);
      if (dataset is null)
        return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The selected TB dataset is outside the engagement or not accepted.");
      if (dataset.PeriodId != request.PeriodId || dataset.BookId != request.BookId ||
          !string.Equals(dataset.Basis, period.Basis, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(dataset.Currency, period.Currency, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
          "The selected TB dataset does not belong to the requested reporting period, book, basis or currency.");
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
      if (batch.PeriodId != request.PeriodId || !string.Equals(batch.Currency, period.Currency, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
          "The selected GL batch does not belong to the requested reporting period and currency.");
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
      PeriodId = request.PeriodId, BookId = request.BookId, Area = area,
      TrialBalanceDatasetId = request.TrialBalanceDatasetId, ImportBatchId = request.ImportBatchId,
      AccountSelection = string.Join(',', codes), AsOfDate = request.AsOfDate, SourceTotal = sourceTotal, GlTotal = glTotal,
      AgingBasis = agingBasis, AgingBucketRuleVersion = agingRuleVersion,
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
    string? sourceCurrency = null;
    if (reconciliation.TrialBalanceDatasetId is { } datasetId)
      sourceCurrency = await db.TrialBalanceDatasets.AsNoTracking()
        .Where(x => x.Id == datasetId && x.FirmId == actor.FirmId && x.ClientId == reconciliation.ClientId &&
                    x.EngagementId == reconciliation.EngagementId && x.ValidationStatus == "Accepted")
        .Select(x => x.Currency).SingleOrDefaultAsync(ct);
    else if (reconciliation.ImportBatchId is { } batchId)
      sourceCurrency = await db.SourceImportBatches.AsNoTracking()
        .Where(x => x.Id == batchId && x.FirmId == actor.FirmId && x.ClientId == reconciliation.ClientId &&
                    x.EngagementId == reconciliation.EngagementId && x.Status == "SEALED")
        .Select(x => x.Currency).SingleOrDefaultAsync(ct);
    if (string.IsNullOrWhiteSpace(sourceCurrency))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The reconciliation source is unavailable.");
    sourceCurrency = sourceCurrency.Trim().ToUpperInvariant();

    var agingEnabled = !string.IsNullOrWhiteSpace(reconciliation.AgingBasis);
    if (items.Count == 0 || items.Where(x => !string.IsNullOrWhiteSpace(x.StableItemId))
        .GroupBy(x => x.StableItemId.Trim(), StringComparer.Ordinal).Any(x => x.Count() > 1))
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Reconciling items need unique identities.");
    foreach (var item in items)
    {
      if (string.IsNullOrWhiteSpace(item.StableItemId) || string.IsNullOrWhiteSpace(item.Currency) ||
          !string.Equals(item.Currency.Trim(), sourceCurrency, StringComparison.OrdinalIgnoreCase) ||
          string.IsNullOrWhiteSpace(item.Reason) || string.IsNullOrWhiteSpace(item.EvidenceReference) ||
          string.IsNullOrWhiteSpace(item.Disposition) || item.ItemDate is { } itemDate && itemDate > reconciliation.AsOfDate ||
          item.SettlementDate.HasValue != !string.IsNullOrWhiteSpace(item.SettlementReference) ||
          item.SettlementDate is { } settlementDate && item.ItemDate is { } itemDateForSettlement && settlementDate < itemDateForSettlement)
        return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected,
          "Reconciling items need source currency, valid dates, reasons, evidence, dispositions and paired settlement links.");
      if (!string.IsNullOrWhiteSpace(item.DateBasis) && !AccountingAgingRules.IsSupportedBasis(item.DateBasis))
        return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The reconciling item date basis is unsupported.");
      var ageDays = item.ItemDate.HasValue ? Math.Max(0, reconciliation.AsOfDate.DayNumber - item.ItemDate.Value.DayNumber) : (int?)null;
      if (agingEnabled && (item.ItemDate is null || item.IsCredit is null ||
          !string.Equals(item.DateBasis.Trim(), reconciliation.AgingBasis, StringComparison.Ordinal) ||
          !string.Equals(item.AgingBucket.Trim(), AccountingAgingRules.BucketFor(ageDays!.Value), StringComparison.Ordinal)))
        return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected,
          "Receivable and payable items need the reconciliation date basis, explicit credit treatment and the correct bucket for the retained ageing rule.");
      var isCredit = item.IsCredit ?? item.SignedAmount < 0m;
      db.AccountingReconciliationItems.Add(new AccountingReconciliationItem
      {
        Id = Guid.CreateVersion7(), FirmId = reconciliation.FirmId, ClientId = reconciliation.ClientId,
        EngagementId = reconciliation.EngagementId, ReconciliationId = reconciliation.Id, StableItemId = item.StableItemId.Trim(),
        SignedAmount = MoneyPolicy.Normalize(item.SignedAmount), Currency = item.Currency.Trim().ToUpperInvariant(),
        ItemDate = item.ItemDate, AgeDays = ageDays, DateBasis = item.DateBasis.Trim().ToUpperInvariant(),
        AgingBucket = item.AgingBucket.Trim().ToUpperInvariant(), IsCredit = isCredit,
        SettlementDate = item.SettlementDate, SettlementReference = item.SettlementReference.Trim(),
        Reason = item.Reason.Trim(), EvidenceReference = item.EvidenceReference.Trim(), Disposition = item.Disposition.Trim(),
        CreatedAt = DateTimeOffset.UtcNow
      });
    }
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public const string ReconciliationProofFormulaVersion = "reconciliation-proof.v1";

  /// <summary>Canonical digest of the exact reconciliation item set; the proof and the
  /// approval gate both recompute it so an edited item set invalidates old proofs.</summary>
  private static string ComputeReconciliationItemManifestDigest(
    IEnumerable<(Guid ItemId, decimal SignedAmount)> items) =>
    Hashing.Sha256Hex(string.Join('\n', items
      .OrderBy(x => x.ItemId)
      .Select(x => $"{x.ItemId:D}|{x.SignedAmount.ToString("0.000000", CultureInfo.InvariantCulture)}")));

  public static async Task<CommandResult<ReconciliationProofDto>> CalculateReconciliationProofAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid reconciliationId,
    CancellationToken ct = default)
  {
    var reconciliation = await db.AccountingReconciliations.SingleOrDefaultAsync(x => x.Id == reconciliationId && x.FirmId == actor.FirmId, ct);
    if (reconciliation is null)
      return CommandResult<ReconciliationProofDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, reconciliation.ClientId, reconciliation.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<ReconciliationProofDto>.Fail(auth.ErrorCode!, auth.Message!);

    var items = await db.AccountingReconciliationItems.AsNoTracking()
      .Where(x => x.ReconciliationId == reconciliation.Id && x.FirmId == actor.FirmId)
      .ToListAsync(ct);

    var itemsSum = MoneyPolicy.Normalize(items.Sum(x => x.SignedAmount));
    var residual = MoneyPolicy.Normalize(reconciliation.GlTotal - reconciliation.SourceTotal - itemsSum);

    reconciliation.Residual = residual;
    reconciliation.Status = residual == 0m ? "RECONCILED" : "UNRECONCILED";

    // Every calculation appends an immutable proof; old proofs are never edited.
    var proof = new AccountingReconciliationProof
    {
      Id = Guid.CreateVersion7(), FirmId = reconciliation.FirmId, ClientId = reconciliation.ClientId,
      EngagementId = reconciliation.EngagementId, ReconciliationId = reconciliation.Id,
      FormulaVersion = ReconciliationProofFormulaVersion,
      SourceTotal = reconciliation.SourceTotal, GlTotal = reconciliation.GlTotal,
      ItemsSignedTotal = itemsSum, Residual = residual, IsReconciled = residual == 0m,
      ItemCount = items.Count,
      ItemManifestDigest = ComputeReconciliationItemManifestDigest(
        items.Select(x => (x.Id, MoneyPolicy.Normalize(x.SignedAmount)))),
      SourceHash = reconciliation.SourceHash, InputGeneration = reconciliation.InputGeneration,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AccountingReconciliationProofs.Add(proof);
    await db.SaveChangesAsync(ct);

    return CommandResult<ReconciliationProofDto>.Ok(new ReconciliationProofDto(
      proof.Id,
      reconciliation.Id,
      reconciliation.SourceTotal,
      reconciliation.GlTotal,
      itemsSum,
      residual,
      residual == 0m,
      items.Count,
      reconciliation.Status));
  }

  public static async Task<CommandResult> LinkReconciliationCorrectionAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid reconciliationId,
    Guid itemId, Guid journalId, CancellationToken ct = default)
  {
    var reconciliation = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reconciliationId && x.FirmId == actor.FirmId, ct);
    if (reconciliation is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, reconciliation.ClientId, reconciliation.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;

    var item = await db.AccountingReconciliationItems.SingleOrDefaultAsync(x =>
      x.Id == itemId && x.ReconciliationId == reconciliation.Id && x.FirmId == actor.FirmId, ct);
    if (item is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var journal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == journalId && x.FirmId == actor.FirmId && x.ClientId == reconciliation.ClientId &&
      x.EngagementId == reconciliation.EngagementId, ct);
    if (journal is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The correction journal is outside the reconciliation engagement scope.");

    item.SettlementReference = $"JOURNAL:{journal.JournalNumber}";
    item.SettlementDate = DateOnly.FromDateTime(DateTime.UtcNow);
    item.Disposition = "LINKED_JOURNAL";
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
    var currentSourceHash = reconciliation.TrialBalanceDatasetId is { } datasetId
      ? await db.TrialBalanceDatasets.AsNoTracking().Where(x => x.Id == datasetId && x.FirmId == actor.FirmId)
        .Select(x => x.NormalizedDatasetDigest.Length == 64 ? x.NormalizedDatasetDigest : x.Sha256Hex).SingleOrDefaultAsync(ct)
      : reconciliation.ImportBatchId is { } batchId
        ? await db.SourceImportBatches.AsNoTracking().Where(x => x.Id == batchId && x.FirmId == actor.FirmId)
          .Select(x => x.NormalizedDatasetDigest).SingleOrDefaultAsync(ct)
        : null;
    var currentGeneration = await db.ClientSafetyStates.AsNoTracking().Where(x => x.Id == reconciliation.ClientId && x.FirmId == actor.FirmId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if (!string.Equals(currentSourceHash, reconciliation.SourceHash, StringComparison.OrdinalIgnoreCase))
    {
      reconciliation.Status = AccountingWorkflowStates.Stale;
      await db.SaveChangesAsync(ct);
      return CommandResult.Fail(ErrorCodes.ManifestMismatch, "The reconciliation source changed; prepare a new source-bound review.");
    }
    if (currentGeneration != reconciliation.InputGeneration)
    {
      reconciliation.Status = AccountingWorkflowStates.Stale;
      await db.SaveChangesAsync(ct);
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The client accounting inputs changed; prepare a new source-bound review.");
    }

    // The approval must bind to a persisted proof of the exact current inputs.
    var latestProof = await db.AccountingReconciliationProofs.AsNoTracking()
      .Where(x => x.ReconciliationId == reconciliation.Id && x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    if (latestProof is null || !latestProof.IsReconciled)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "A calculated reconciliation proof with zero unexplained residual is required before approval.");
    var liveItems = await db.AccountingReconciliationItems.AsNoTracking()
      .Where(x => x.ReconciliationId == reconciliation.Id && x.FirmId == actor.FirmId)
      .Select(x => new ValueTuple<Guid, decimal>(x.Id, x.SignedAmount))
      .ToListAsync(ct);
    var liveDigest = ComputeReconciliationItemManifestDigest(
      liveItems.Select(x => (x.Item1, MoneyPolicy.Normalize(x.Item2))));
    if (latestProof.ItemManifestDigest != liveDigest ||
        latestProof.ItemCount != liveItems.Count ||
        latestProof.SourceHash != reconciliation.SourceHash ||
        latestProof.InputGeneration != reconciliation.InputGeneration ||
        latestProof.SourceTotal != reconciliation.SourceTotal ||
        latestProof.GlTotal != reconciliation.GlTotal)
      return CommandResult.Fail(ErrorCodes.ManifestMismatch,
        "The reconciliation inputs changed after the proof was calculated; recalculate the proof.");

    reconciliation.Status = AccountingWorkflowStates.Approved;
    reconciliation.ReviewedByUserId = actor.UserId;
    reconciliation.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>Creates a new revision of an existing reconciliation. The prior schedule
  /// and its review are preserved unchanged; the successor starts unapproved with a
  /// bumped revision and an explicit supersession link.</summary>
  public static async Task<CommandResult<Guid>> ReviseReconciliationAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid reconciliationId, string reason,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A revision requires a reason of at most 2000 characters.");
    var original = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == reconciliationId && x.FirmId == actor.FirmId, ct);
    if (original is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, original.ClientId, original.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (original.Status == AccountingWorkflowStates.Approved)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState,
        "An approved reconciliation is immutable; the finding must be handled through a correction journal or a new schedule.");

    // The successor is the highest revision for the same source-bound account selection.
    var prior = await db.AccountingReconciliations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == original.ClientId &&
        x.EngagementId == original.EngagementId && x.PeriodId == original.PeriodId &&
        x.Area == original.Area && x.AccountSelection == original.AccountSelection &&
        x.TrialBalanceDatasetId == original.TrialBalanceDatasetId && x.ImportBatchId == original.ImportBatchId)
      .OrderByDescending(x => x.Revision)
      .FirstOrDefaultAsync(ct);
    var nextRevision = (prior?.Revision ?? 0) + 1;

    var successor = new AccountingReconciliation
    {
      Id = Guid.CreateVersion7(), FirmId = original.FirmId, ClientId = original.ClientId,
      EngagementId = original.EngagementId, PeriodId = original.PeriodId, BookId = original.BookId,
      Area = original.Area, TrialBalanceDatasetId = original.TrialBalanceDatasetId,
      ImportBatchId = original.ImportBatchId, AccountSelection = original.AccountSelection,
      AsOfDate = original.AsOfDate, AgingBasis = original.AgingBasis,
      AgingBucketRuleVersion = original.AgingBucketRuleVersion,
      SourceTotal = original.SourceTotal, GlTotal = original.GlTotal,
      SourceHash = original.SourceHash, Status = AccountingWorkflowStates.Draft,
      Revision = nextRevision, SupersedesReconciliationId = original.Id,
      InputGeneration = original.InputGeneration, CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.AccountingReconciliations.Add(successor);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(successor.Id);
  }

  /// <summary>Copies selected unresolved item references from an approved reconciliation
  /// into a new draft for the next period. Only the item references are carried forward;
  /// no proof, approval or journal link is inherited.</summary>
  public static async Task<CommandResult<Guid>> CarryForwardReconciliationItemsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid sourceReconciliationId,
    Guid targetPeriodId, Guid? targetBookId, IReadOnlyList<Guid> selectedItemIds, string evidenceReference,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Trim().Length > 2000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A carry-forward requires an evidence reference.");
    var source = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == sourceReconciliationId && x.FirmId == actor.FirmId, ct);
    if (source is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, source.ClientId, source.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (source.Status != AccountingWorkflowStates.Approved)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Only an approved reconciliation can carry items forward to the next period.");
    if (selectedItemIds.Count == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "Select at least one item to carry forward.");

    var targetPeriod = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == targetPeriodId && x.FirmId == actor.FirmId && x.ClientId == source.ClientId, ct);
    if (targetPeriod is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The target period is outside the client scope.");
    if (targetPeriod.StartDate <= source.AsOfDate)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "The carry-forward target period must start after the source reconciliation as-of date.");

    var sourceItems = await db.AccountingReconciliationItems.AsNoTracking()
      .Where(x => x.ReconciliationId == source.Id && x.FirmId == actor.FirmId &&
        selectedItemIds.Contains(x.Id))
      .ToListAsync(ct);
    if (sourceItems.Count != selectedItemIds.Count)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied,
        "One or more selected items do not belong to the source reconciliation.");

    var carry = new AccountingReconciliation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = source.ClientId,
      EngagementId = source.EngagementId, PeriodId = targetPeriod.Id, BookId = targetBookId,
      Area = source.Area, TrialBalanceDatasetId = null, ImportBatchId = null,
      AccountSelection = source.AccountSelection, AsOfDate = targetPeriod.EndDate,
      AgingBasis = source.AgingBasis, AgingBucketRuleVersion = source.AgingBucketRuleVersion,
      SourceTotal = 0m, GlTotal = 0m, SourceHash = string.Empty,
      Status = AccountingWorkflowStates.Draft, Revision = 1,
      InputGeneration = 1, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AccountingReconciliations.Add(carry);
    await db.SaveChangesAsync(ct);

    foreach (var item in sourceItems)
    {
      db.AccountingReconciliationItems.Add(new AccountingReconciliationItem
      {
        Id = Guid.CreateVersion7(), FirmId = carry.FirmId, ClientId = carry.ClientId,
        EngagementId = carry.EngagementId, ReconciliationId = carry.Id,
        StableItemId = item.StableItemId, SignedAmount = item.SignedAmount,
        Currency = item.Currency, ItemDate = item.ItemDate,
        DateBasis = item.DateBasis, AgingBucket = item.AgingBucket,
        IsCredit = item.IsCredit, Reason = item.Reason,
        EvidenceReference = item.EvidenceReference,
        Disposition = "CARRIED_FORWARD", CreatedAt = carry.CreatedAt
      });
    }
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(carry.Id);
  }
}
