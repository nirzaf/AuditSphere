using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ReconciliationItemReview(Guid Id, string StableItemId, string SignedAmount, string Currency,
  DateOnly? ItemDate, string Reason, string EvidenceReference, string Disposition, string DateBasis,
  string AgingBucket, bool IsCredit, DateOnly? SettlementDate, string SettlementReference);
public sealed record ReconciliationProofReview(Guid Id, string FormulaVersion, string SourceTotal, string GlTotal,
  string ItemsSignedTotal, string Residual, bool IsReconciled, int ItemCount, string? ItemManifestDigest,
  string? SourceHash, long InputGeneration, Guid CreatedByUserId, DateTimeOffset CreatedAt, bool MatchesCurrentInputs);
public sealed record ReconciliationReview(Guid Id, Guid ClientId, string ClientName, Guid EngagementId, string EngagementName,
  Guid PeriodId, string PeriodCode, Guid? BookId, string? BookCode, string Basis, string Currency, string Area,
  string AccountSelection, DateOnly AsOfDate, string AgingBasis, string AgingBucketRuleVersion, string Status,
  long Revision, Guid? SupersedesReconciliationId, Guid CreatedByUserId, DateTimeOffset CreatedAt,
  Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt, string SourceKind, Guid? SourceId, string? SourceHash,
  string? CurrentSourceHash, bool SourceAvailable, long InputGeneration, long? CurrentGeneration,
  bool IsStale, bool CanReuseApprovedEvidence, string SourceTotal, string GlTotal, string Residual,
  ReconciliationProofReview? LatestProof, IReadOnlyList<string> Blockers, IReadOnlyList<ReconciliationItemReview> Items,
  int ItemCount, int Page, bool HasMore, string ReviewBasis);

/// <summary>Read-only inspection of retained reconciliation evidence and its current exact input eligibility.</summary>
public static class ReconciliationWorkspaceQuery
{
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private const int PageSize = 25;
  private const int MaximumItems = 20000;

  public static async Task<CommandResult<ReconciliationReview>> GetAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid id, int page = 0, CancellationToken ct = default)
  {
    if (id == Guid.Empty || page < 0 || page >= MaximumItems / PageSize)
      return CommandResult<ReconciliationReview>.Fail(ErrorCodes.ScopeDenied, "This reconciliation is unavailable in the current scope.");
    var initial = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ReadRoles, InternalOnly: true), ct);
    if (!initial.Succeeded) return CommandResult<ReconciliationReview>.Fail(initial.ErrorCode!, initial.Message!);
    var first = await ProjectAsync(db, actor, id, page, ct);
    if (!first.Succeeded) return first;
    var current = await ProjectAsync(db, actor, id, page, ct);
    if (!current.Succeeded) return current;
    if (first.Value!.ReviewBasis != current.Value!.ReviewBasis)
      return CommandResult<ReconciliationReview>.Fail(ErrorCodes.GenerationStale, "The reconciliation inputs changed during inspection. Refresh the current evidence.");
    var final = await AuthorizeAsync(db, actor, current.Value.ClientId, current.Value.EngagementId, ct);
    return final.Succeeded ? current : CommandResult<ReconciliationReview>.Fail(final.ErrorCode!, final.Message!);
  }

  private static Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor, Guid client, Guid engagement, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, client, engagement, ReadRoles, InternalOnly: true), ct);

  private static async Task<CommandResult<ReconciliationReview>> ProjectAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid id, int page, CancellationToken ct)
  {
    var r = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == id, ct);
    if (r is null) return Denied();
    var auth = await AuthorizeAsync(db, actor, r.ClientId, r.EngagementId, ct);
    if (!auth.Succeeded) return Denied();
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == r.ClientId, ct);
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == r.EngagementId && x.PracticeClientId == r.ClientId, ct);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId && x.Id == r.PeriodId, ct);
    var book = r.BookId is { } bookId ? await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == r.ClientId && x.PeriodId == r.PeriodId && x.Id == bookId, ct) : null;
    if (client is null || engagement is null || period is null || r.BookId.HasValue && book is null) return Denied();
    var reportingContextMatches = book is null || book.Basis == period.Basis && book.Currency == period.Currency;
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == r.ClientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    string? currentHash = null;
    var sourceAvailable = false;
    var sourceKind = "UNBOUND";
    Guid? sourceId = null;
    if (r.TrialBalanceDatasetId is { } datasetId && r.ImportBatchId is null)
    {
      sourceKind = "TRIAL_BALANCE";
      var source = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
        x.EngagementId == r.EngagementId && x.PeriodId == r.PeriodId && x.BookId == r.BookId && x.Id == datasetId, ct);
      if (source is not null)
      {
        sourceId = datasetId;
        currentHash = Digest(source.NormalizedDatasetDigest.Length == 64 ? source.NormalizedDatasetDigest : source.Sha256Hex);
        sourceAvailable = reportingContextMatches && source.ValidationStatus == "Accepted" && source.ImportState == TrialBalanceImportStates.Sealed &&
          source.Currency == period.Currency && source.Basis == period.Basis && currentHash is not null;
      }
    }
    else if (r.ImportBatchId is { } batchId && r.TrialBalanceDatasetId is null)
    {
      sourceKind = "GENERAL_LEDGER";
      var source = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
        x.EngagementId == r.EngagementId && x.PeriodId == r.PeriodId && x.BookId == r.BookId && x.Id == batchId, ct);
      if (source is not null)
      {
        sourceId = batchId;
        currentHash = Digest(source.NormalizedDatasetDigest);
        sourceAvailable = reportingContextMatches && source.Status == "SEALED" && source.Currency == period.Currency && currentHash is not null;
      }
    }
    var allItems = await db.AccountingReconciliationItems.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
      x.EngagementId == r.EngagementId && x.ReconciliationId == r.Id).OrderBy(x => x.Id).Take(MaximumItems + 1).ToListAsync(ct);
    if (allItems.Count > MaximumItems)
      return CommandResult<ReconciliationReview>.Fail(ErrorCodes.GateBlocked, "The complete item set exceeds the supported inspection bound. No partial proof is shown.");
    var manifest = AccountingAnalysisService.ComputeReconciliationItemManifestDigest(allItems.Select(x => (x.Id, MoneyPolicy.Normalize(x.SignedAmount))));
    var proof = await db.AccountingReconciliationProofs.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
      x.EngagementId == r.EngagementId && x.ReconciliationId == r.Id).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var currentItemsTotal = MoneyPolicy.Normalize(allItems.Sum(x => x.SignedAmount));
    var proofCurrent = proof is not null && proof.FormulaVersion == AccountingAnalysisService.ReconciliationProofFormulaVersion &&
      sourceAvailable && Digest(r.SourceHash) == currentHash && generation == r.InputGeneration &&
      proof.ItemManifestDigest == manifest && proof.ItemCount == allItems.Count && proof.SourceHash == r.SourceHash &&
      proof.InputGeneration == r.InputGeneration && proof.SourceTotal == r.SourceTotal && proof.GlTotal == r.GlTotal && proof.Residual == r.Residual &&
      proof.ItemsSignedTotal == currentItemsTotal && proof.Residual == MoneyPolicy.Normalize(r.GlTotal - r.SourceTotal - currentItemsTotal) &&
      proof.IsReconciled == (proof.Residual == 0m);
    var blockers = new List<string>();
    var recordedHash = Digest(r.SourceHash);
    if (!sourceAvailable) blockers.Add("The exact accepted and sealed source is unavailable or no longer matches the reporting context.");
    if (recordedHash is null || currentHash != recordedHash) blockers.Add("The retained source digest differs from the current exact source. Prepare new source-bound evidence.");
    if (generation is null || generation != r.InputGeneration) blockers.Add("The client input generation changed or is unavailable. Retained approval cannot be reused.");
    if (!proofCurrent) blockers.Add("A retained proof of the complete current item set and source is required. Historical amounts are not current proof.");
    if (proof is not null && !proof.IsReconciled) blockers.Add("The retained proof has an unexplained residual.");
    if (r.Status == "STALE") blockers.Add("This reconciliation is explicitly stale.");
    if (allItems.Any(x => x.Currency != period.Currency || x.ItemDate > r.AsOfDate))
      blockers.Add("A retained item has a different currency or a date after the reconciliation. Current reuse is blocked.");
    if (r.Status == "APPROVED" && (r.ReviewedByUserId is null || r.ReviewedAt is null || r.ReviewedByUserId == r.CreatedByUserId))
      blockers.Add("An independent retained approval is unavailable.");
    var stale = !sourceAvailable || recordedHash is null || currentHash != recordedHash || generation is null || generation != r.InputGeneration ||
      r.Status == "STALE" || proof is not null && !proofCurrent;
    var rows = allItems.Skip(page * PageSize).Take(PageSize).Select(x => new ReconciliationItemReview(x.Id, x.StableItemId, Exact(x.SignedAmount), x.Currency,
      x.ItemDate, x.Reason, x.EvidenceReference, x.Disposition, x.DateBasis, x.AgingBucket, x.IsCredit, x.SettlementDate, x.SettlementReference)).ToArray();
    var retainedProof = proof is null ? null : new ReconciliationProofReview(proof.Id, proof.FormulaVersion, Exact(proof.SourceTotal), Exact(proof.GlTotal),
      Exact(proof.ItemsSignedTotal), Exact(proof.Residual), proof.IsReconciled, proof.ItemCount, Digest(proof.ItemManifestDigest), Digest(proof.SourceHash),
      proof.InputGeneration, proof.CreatedByUserId, proof.CreatedAt, proofCurrent);
    var result = new ReconciliationReview(r.Id, r.ClientId, client.LegalName, r.EngagementId, engagement.ServiceRoute, r.PeriodId, period.PeriodCode,
      r.BookId, book?.Code, period.Basis, period.Currency, r.Area, r.AccountSelection, r.AsOfDate, r.AgingBasis, r.AgingBucketRuleVersion, r.Status,
      r.Revision, r.SupersedesReconciliationId, r.CreatedByUserId, r.CreatedAt, r.ReviewedByUserId, r.ReviewedAt, sourceKind, sourceId, recordedHash,
      currentHash, sourceAvailable, r.InputGeneration, generation, stale, r.Status == "APPROVED" && blockers.Count == 0,
      Exact(r.SourceTotal), Exact(r.GlTotal), Exact(r.Residual), retainedProof, blockers, rows, allItems.Count, page,
      allItems.Count > (page + 1) * PageSize, "");
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch, result, manifest,
      period.Revision, period.Status, BookRevision = book?.Revision, BookStatus = book?.Status }));
    return CommandResult<ReconciliationReview>.Ok(result with { ReviewBasis = basis });
  }

  private static string Exact(decimal amount) => amount.ToString("0.000000", CultureInfo.InvariantCulture);
  private static string? Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;
  private static CommandResult<ReconciliationReview> Denied() => CommandResult<ReconciliationReview>.Fail(ErrorCodes.ScopeDenied, "This reconciliation is unavailable in the current scope.");
}
