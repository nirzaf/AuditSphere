using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record CashFlowSourceReviewEntry(Guid Id, string Decision, string Reason, Guid ReviewedByUserId,
  DateTimeOffset ReviewedAt, Guid SourceImportBatchId);

/// <summary>
/// The current state of a package's source-linked cash-flow movements. Approved is true only when every movement is fully allocated,
/// every cash event is explained, the newest decision is an independent approval, and the accepted general ledger it cites is still the
/// current one. LinkDigest and LatestReviewId are what a reviewer submits back, so a decision binds to the exact links it reviewed.
/// </summary>
public sealed record CashFlowSourceReviewState(Guid FinancialPackageId, int Movements, int ResolvedMovements, int SourceLinks,
  Guid? CurrentSourceImportBatchId, bool Approved, bool CanReview, IReadOnlyList<CashFlowSourceReviewEntry> Reviews,
  string LinkDigest, Guid? LatestReviewId);

/// <summary>
/// Independent review of cash-flow source links (CP-11). The preparer who resolved a link can never review it. A decision carries the
/// caller's original intent (RequestId), the link digest it reviewed and the latest decision it saw. A retry of the same intent returns the
/// stored decision; a stale or different request is refused under the package lock.
/// </summary>
public static partial class FinancialStatementService
{
  private static readonly string[] CashFlowSourceReviewRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] CashFlowSourceReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  public const int MaxCashFlowSourceReviewEntries = 50;

  private sealed record CashFlowLineage(IReadOnlyList<FinancialPackageCashFlowLine> Lines,
    IReadOnlyList<FinancialPackageCashFlowAllocation> Allocations, IReadOnlyList<bool> Resolved, Guid? CurrentBatchId)
  {
    public int Movements => Lines.Count;
    public bool AllResolved => Movements > 0 && Resolved.All(x => x);
  }

  public static async Task<CommandResult<Guid>> RecordCashFlowSourceReviewAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid packageId, string? decision, string? reason, Guid requestId, string? expectedLinkDigest, Guid? expectedPreviousReviewId,
    CancellationToken ct = default)
  {
    var normalized = decision?.Trim().ToUpperInvariant() ?? string.Empty;
    var text = reason?.Trim() ?? string.Empty;
    var digest = expectedLinkDigest?.Trim().ToLowerInvariant() ?? string.Empty;
    if (packageId == Guid.Empty || requestId == Guid.Empty || normalized is not ("APPROVED" or "RETURNED") || text.Length > 2000 ||
        (normalized == "RETURNED" && text.Length == 0) || digest.Length != 64 || !digest.All(Uri.IsHexDigit))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Choose approve or return, give a reason when returning, and identify the exact source links you reviewed.");
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == packageId, ct);
    if (package is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, package.ClientId, package.EngagementId, CashFlowSourceReviewRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    // Lock order: the package row, then this package's decisions. Authority is checked again once the lock is held, because a grant can
    // be revoked while this command waits. Opposing decisions therefore serialize instead of winning by wall-clock order.
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var locked = await db.FinancialPackages.FromSqlInterpolated(
        $"SELECT * FROM financial_packages WHERE firm_id = {actor.FirmId} AND id = {packageId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (locked is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    // Share locks fence what the decision depends on. Source acceptance holds the client safety row for update, so an acceptance cannot land
    // between this check and the commit. The reviewer's grant rows are held too, so a revocation waits for this decision to finish.
    await db.ClientSafetyStates.FromSqlInterpolated(
        $"SELECT * FROM client_safety_states WHERE firm_id = {actor.FirmId} AND id = {locked.ClientId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    await db.RoleGrants.FromSqlInterpolated(
        $"SELECT * FROM role_grants WHERE firm_id = {actor.FirmId} AND user_id = {actor.UserId} AND revoked_at IS NULL FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    // Authority is checked again only after the locks are held, so a grant revoked while this command waited is refused.
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, locked.ClientId, locked.EngagementId, CashFlowSourceReviewRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    // The same original intent recovers the stored decision. The same identity with a different payload is a conflict, never an overwrite.
    var intentHash = ReviewIntentHash(normalized, text, digest);
    var recorded = await db.FinancialPackageCashFlowSourceReviews.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.FinancialPackageId == packageId && x.RequestId == requestId, ct);
    if (recorded is not null)
    {
      // A request identity belongs to the reviewer who recorded it. Another reviewer presenting it is refused, not given that decision.
      if (recorded.ReviewedByUserId != actor.UserId)
        return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This request was already used by another reviewer.");
      return recorded.RequestHash == intentHash
        ? CommandResult<Guid>.Ok(recorded.Id)
        : CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This request was already used for a different source decision.");
    }

    var lineage = await LoadCashFlowLineageAsync(db, actor, locked, ct);
    if (!lineage.Succeeded)
      return CommandResult<Guid>.Fail(lineage.ErrorCode!, lineage.Message!);
    var current = lineage.Value!;
    if (current.Allocations.Count == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "This package has no source-linked cash-flow movements to review.");
    if (current.Allocations.Any(x => x.CreatedByUserId == Guid.Empty))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A source link has no recorded preparer. Rebuild the package before review.");
    if (current.Allocations.Any(x => x.CreatedByUserId == actor.UserId))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The preparer cannot review the source links they resolved.");
    if (current.Allocations.Any(x => current.Lines.All(line => line.Id != x.CashFlowLineId)))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A source link points at a movement that is no longer on this package. Rebuild the package before review.");

    // The reviewer decides on the latest decision and the links they saw. Anything else is stale.
    var latest = await LatestCashFlowSourceReviewAsync(db, actor.FirmId, packageId, ct);
    if (latest?.Id != expectedPreviousReviewId)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "A source decision was recorded after this review was loaded. Reload the review before deciding.");
    var linkDigest = CashFlowLinkDigest(locked, current.Lines, current.Allocations);
    if (!string.Equals(linkDigest, digest, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "The source links changed after this review was loaded. Reload the review before deciding.");

    if (normalized == "APPROVED")
    {
      if (!current.AllResolved)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Every cash-flow movement must be fully allocated before approval.");
      if (!await LineageValidatedAsync(db, locked.FirmId, locked.Id, ct))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          "Every cash event must be fully explained by its movements before approval. Rebuild the package.");
      if (current.CurrentBatchId is not Guid currentBatch || current.Allocations.Any(x => x.SourceImportBatchId != currentBatch))
        return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
          "The accepted general ledger changed after these links were resolved. Rebuild the package before review.");
    }
    var review = new FinancialPackageCashFlowSourceReview
    {
      Id = Guid.CreateVersion7(), FirmId = locked.FirmId, ClientId = locked.ClientId, EngagementId = locked.EngagementId,
      FinancialPackageId = locked.Id, Decision = normalized, Reason = text, SourceImportBatchId = current.Allocations[0].SourceImportBatchId,
      ReviewedByUserId = actor.UserId, ReviewedAt = DateTimeOffset.UtcNow, RequestId = requestId, RequestHash = intentHash,
      LinkDigest = linkDigest, PreviousReviewId = expectedPreviousReviewId
    };
    db.FinancialPackageCashFlowSourceReviews.Add(review);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_financial_package_cash_source_review_request", StringComparison.Ordinal) == true)
    {
      // Only the request-identity uniqueness maps to a conflict. Any other database failure is a fault and propagates.
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This source decision was recorded concurrently. Reload the review.");
    }
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(review.Id);
  }

  public static async Task<CommandResult<CashFlowSourceReviewState>> GetCashFlowSourceReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid packageId, CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == packageId, ct);
    if (package is null)
      return CommandResult<CashFlowSourceReviewState>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, package.ClientId, package.EngagementId, CashFlowSourceReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<CashFlowSourceReviewState>.Fail(auth.ErrorCode!, auth.Message!);
    var lineage = await LoadCashFlowLineageAsync(db, actor, package, ct);
    if (!lineage.Succeeded)
      return CommandResult<CashFlowSourceReviewState>.Fail(lineage.ErrorCode!, lineage.Message!);
    var current = lineage.Value!;
    var reviews = await db.FinancialPackageCashFlowSourceReviews.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id)
      .OrderByDescending(x => x.Sequence).Take(MaxCashFlowSourceReviewEntries).ToListAsync(ct);
    var latest = reviews.FirstOrDefault();
    // Approval also requires the persisted lineage result: the build recorded every cash event as explained. A resolved-looking link alone is not enough.
    var lineagePassed = await LineageValidatedAsync(db, package.FirmId, package.Id, ct);
    var approved = lineagePassed && current.AllResolved && current.CurrentBatchId is Guid batch && latest is { Decision: "APPROVED" } &&
      latest.SourceImportBatchId == batch && current.Allocations.All(x => x.SourceImportBatchId == batch);
    var canReview = actor.Roles.Any(CashFlowSourceReviewRoles.Contains) && current.Allocations.Count > 0 &&
      current.Allocations.All(x => x.CreatedByUserId != Guid.Empty && x.CreatedByUserId != actor.UserId);
    return CommandResult<CashFlowSourceReviewState>.Ok(new CashFlowSourceReviewState(package.Id, current.Movements,
      current.Resolved.Count(x => x), current.Allocations.Count, current.CurrentBatchId, approved, canReview,
      reviews.Select(x => new CashFlowSourceReviewEntry(x.Id, x.Decision, x.Reason, x.ReviewedByUserId, x.ReviewedAt, x.SourceImportBatchId)).ToArray(),
      CashFlowLinkDigest(package, current.Lines, current.Allocations), latest?.Id));
  }

  /// <summary>Presentation read: true only for a current independent approval. Any failure reads as not approved.</summary>
  internal static async Task<bool> CashFlowSourceApprovedAsync(IClientAccountingDbContext db, FinancialPackage package, CancellationToken ct)
  {
    var movements = await db.FinancialPackageCashFlowLines.AsNoTracking().Where(x =>
      x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
      x.FinancialPackageId == package.Id).OrderBy(x => x.Id).ToListAsync(ct);
    if (movements.Count == 0) return true;
    var allocations = await db.FinancialPackageCashFlowAllocations.AsNoTracking().Where(x =>
      x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
      x.FinancialPackageId == package.Id).ToListAsync(ct);
    if (allocations.Count == 0 || allocations.Any(x => x.CreatedByUserId == Guid.Empty || x.Currency != package.Currency)) return false;
    if (!await LineageValidatedAsync(db, package.FirmId, package.Id, ct)) return false;
    var movementIndexes = movements.Select((line, index) => (line.Id, index)).ToDictionary(x => x.Id, x => x.index);
    if (allocations.Any(x => !movementIndexes.ContainsKey(x.CashFlowLineId)) ||
        !ResolvedCashFlowMovements(movements.Select(x => (x.Amount, x.IsNonCash)).ToArray(),
          allocations.Select(x => (movementIndexes[x.CashFlowLineId], x.SignedAmount, true))).All(x => x)) return false;

    if (package.PeriodId is not Guid periodId) return false;
    // The accepted ledger is the one for this package's period. Another period's acceptance never changes this package's approval.
    var accepted = await (from d in db.SourceAcceptanceDecisions.AsNoTracking()
        join b in db.SourceImportBatches.AsNoTracking() on d.ImportBatchId equals b.Id
        where d.FirmId == package.FirmId && d.ClientId == package.ClientId && d.EngagementId == package.EngagementId &&
          d.SourceKind == AccountingSourceKinds.GeneralLedger && d.Decision == "ACCEPTED" && b.PeriodId == periodId
        orderby d.CreatedAt descending, d.Id descending
        select d).FirstOrDefaultAsync(ct);
    if (accepted is null || allocations.Any(x => x.SourceImportBatchId != accepted.ImportBatchId) ||
        !await db.SourceImportBatches.AsNoTracking().AnyAsync(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
          x.EngagementId == package.EngagementId && x.Id == accepted.ImportBatchId && x.SourceKind == AccountingSourceKinds.GeneralLedger &&
          x.Status == "SEALED" && x.NormalizedDatasetDigest == accepted.SourceIdentityHash, ct)) return false;
    var latest = await LatestCashFlowSourceReviewAsync(db, package.FirmId, package.Id, ct);
    return latest is { Decision: "APPROVED" } && latest.SourceImportBatchId == accepted.ImportBatchId &&
      allocations.All(x => x.CreatedByUserId != latest.ReviewedByUserId);
  }

  /// <summary>
  /// Digest of the exact resolved links a reviewer decides on: the package identity and revision, then each movement's ledger links in a
  /// stable order, including who resolved each link. A changed link, movement, batch, preparer or package generation changes it.
  /// </summary>
  internal static string CashFlowLinkDigest(FinancialPackage package, IReadOnlyList<FinancialPackageCashFlowLine> movements,
    IReadOnlyList<FinancialPackageCashFlowAllocation> allocations)
  {
    var position = movements.Select((line, index) => (line.Id, index)).ToDictionary(x => x.Id, x => x.index);
    // The movement facts are part of what the reviewer decides on: a changed amount, section or classification changes the digest.
    var movementFacts = movements.Select(m => new
    {
      m.Section, m.Description, Amount = MoneyPolicy.Normalize(m.Amount), m.IsNonCash, m.ActivityCode,
    }).ToArray();
    var links = allocations.OrderBy(x => position.GetValueOrDefault(x.CashFlowLineId, -1)).ThenBy(x => x.GeneralLedgerLineId)
      .Select(x => new
      {
        Movement = position.GetValueOrDefault(x.CashFlowLineId, -1), x.GeneralLedgerLineId, x.GeneralLedgerTransactionId,
        x.SourceImportBatchId, x.SourceLineHash, SignedAmount = MoneyPolicy.Normalize(x.SignedAmount), x.Currency, x.CreatedByUserId,
      }).ToArray();
    return Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      Version = "cash-flow-link-digest.v2", PackageId = package.Id, package.CalculationHash, package.Revision, package.Generation,
      Movements = movementFacts, Links = links,
    }));
  }

  private static string ReviewIntentHash(string decision, string reason, string linkDigest) =>
    Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      Version = "cash-flow-source-review-intent.v1", Decision = decision, Reason = reason, LinkDigest = linkDigest,
    }));

  private static Task<FinancialPackageCashFlowSourceReview?> LatestCashFlowSourceReviewAsync(IClientAccountingDbContext db, Guid firmId,
    Guid packageId, CancellationToken ct) =>
    db.FinancialPackageCashFlowSourceReviews.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.FinancialPackageId == packageId)
      .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);

  /// <summary>True only when the build recorded CASH_FLOW_SOURCE_LINEAGE as passed: every movement resolved and every cash event explained.</summary>
  private static Task<bool> LineageValidatedAsync(IClientAccountingDbContext db, Guid firmId, Guid packageId, CancellationToken ct) =>
    db.FinancialPackageValidations.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.FinancialPackageId == packageId &&
      x.Code == "CASH_FLOW_SOURCE_LINEAGE" && x.Passed, ct);

  private static async Task<CommandResult<CashFlowLineage>> LoadCashFlowLineageAsync(IClientAccountingDbContext db, ActorContext actor,
    FinancialPackage package, CancellationToken ct)
  {
    var movements = await db.FinancialPackageCashFlowLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id).OrderBy(x => x.Id).ToListAsync(ct);
    var allocations = await db.FinancialPackageCashFlowAllocations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id).OrderBy(x => x.Id).ToListAsync(ct);
    // Stored links were proven explained when the package was built. LineageValidatedAsync fences that result before a stored link counts.
    var resolved = ResolvedCashFlowMovements(movements.Select(x => (x.Amount, x.IsNonCash)).ToArray(),
      movements.Select((line, index) => (line, index)).SelectMany(x => allocations
        .Where(a => a.CashFlowLineId == x.line.Id).Select(a => (MovementIndex: x.index, LedgerSignedAmount: a.SignedAmount, PopulationComplete: true))));
    if (package.PeriodId is not Guid periodId)
      return CommandResult<CashFlowLineage>.Fail(ErrorCodes.GateBlocked, "This package has no reporting period, so its source cannot be selected.");
    var selected = await AccountingSourceAcceptanceService.GetSelectedGeneralLedgerForPeriodAsync(db, actor, package.ClientId,
      package.EngagementId, periodId, ct);
    if (!selected.Succeeded)
      return CommandResult<CashFlowLineage>.Fail(selected.ErrorCode!, selected.Message!);
    return CommandResult<CashFlowLineage>.Ok(new CashFlowLineage(movements, allocations, resolved, selected.Value?.ImportBatchId));
  }
}
