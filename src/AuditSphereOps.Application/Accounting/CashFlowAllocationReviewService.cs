using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record CashFlowAllocationReviewContribution(Guid GeneralLedgerLineId, Guid GeneralLedgerTransactionId,
  Guid SourceImportBatchId, string SourceLineHash, decimal SignedAmount, string Currency, Guid? PreparedByUserId);

public sealed record CashFlowAllocationReviewMovement(Guid FinancialPackageId, Guid CashFlowLineId, string Section, string Description,
  decimal Amount, string Currency, DateOnly? MovementDate, string? ActivityCode,
  IReadOnlyList<CashFlowAllocationReviewContribution> Contributions);

public sealed record CashFlowAllocationReviewView(Guid FinancialPackageId, string SourceManifestHash,
  bool Required, bool Complete, bool CanReview, string Decision, string? EvidenceReference,
  string? Comment, Guid? DecidedByUserId, DateTimeOffset? DecidedAt,
  IReadOnlyList<CashFlowAllocationReviewMovement> Movements);

public sealed record CashFlowAllocationReviewRequest(string ExpectedSourceManifestHash, string Decision,
  string EvidenceReference, string? Comment);

/// <summary>
/// Independent preparer/reviewer decision bound to the exact persisted contribution manifest.
/// It is separate from final package approval and becomes stale when any source allocation changes.
/// </summary>
public static class CashFlowAllocationReviewService
{
  public const string ReviewStage = "CASH_FLOW_ALLOCATION_REVIEW";
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<CashFlowAllocationReviewView>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid packageId, CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == packageId, ct);
    if (package is null)
      return CommandResult<CashFlowAllocationReviewView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<CashFlowAllocationReviewView>.Fail(auth.ErrorCode!, auth.Message!);
    var state = await ReadStateAsync(db, package, ct);
    if (!state.Succeeded || state.Value is null)
      return CommandResult<CashFlowAllocationReviewView>.Fail(state.ErrorCode!, state.Message!);
    var canReview = false;
    var reviewAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, ReviewRoles, InternalOnly: true), ct);
    if (reviewAuth.Succeeded)
      canReview = state.Value.Complete && state.Value.Movements.SelectMany(x => x.Contributions)
        .All(x => x.PreparedByUserId is { } preparer && preparer != actor.UserId);
    return CommandResult<CashFlowAllocationReviewView>.Ok(state.Value with { CanReview = canReview });
  }

  public static async Task<CommandResult<Guid>> DecideAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid packageId, CashFlowAllocationReviewRequest request, CancellationToken ct = default)
  {
    var decision = request.Decision?.Trim().ToUpperInvariant() ?? string.Empty;
    var evidence = request.EvidenceReference?.Trim() ?? string.Empty;
    var comment = request.Comment?.Trim() ?? string.Empty;
    var expectedHash = request.ExpectedSourceManifestHash?.Trim().ToLowerInvariant() ?? string.Empty;
    if (packageId == Guid.Empty || expectedHash.Length != 64 || !expectedHash.All(char.IsAsciiHexDigit) ||
        decision is not (FinancialPackageReviewDecisions.Approved or FinancialPackageReviewDecisions.ChangesRequired or FinancialPackageReviewDecisions.Rejected) ||
        evidence.Length is < 1 or > 2000 || comment.Length > 4000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "A current manifest, review decision and bounded evidence reference are required.");
    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == packageId, ct);
    if (package is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, ReviewRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var state = await ReadStateAsync(db, package, ct);
    if (!state.Succeeded || state.Value is null)
      return CommandResult<Guid>.Fail(state.ErrorCode!, state.Message!);
    if (!state.Value.Required || !state.Value.Complete)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "A complete, source-linked cash-flow schedule is required before allocation review.");
    if (!string.Equals(expectedHash, state.Value.SourceManifestHash, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "The source allocations changed. Refresh the exact schedule before recording a review decision.");
    if (state.Value.Movements.SelectMany(x => x.Contributions)
        .Any(x => x.PreparedByUserId is null || x.PreparedByUserId == actor.UserId))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "An allocation preparer cannot approve their own schedule; legacy contributions without a preparer identity require a new reviewed build.");
    var existing = await db.FinancialPackageCashFlowAllocationReviews.AsNoTracking().Where(x =>
        x.FirmId == package.FirmId && x.FinancialPackageId == package.Id && x.PackageRevision == package.Revision &&
        x.SourceManifestHash == expectedHash).ToListAsync(ct);
    if (existing.Any(x => x.Decision != FinancialPackageReviewDecisions.Approved))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "A prior reviewer requested changes or rejected this exact immutable schedule. Build a new package revision before review.");
    if (existing.Any(x => x.DecidedByUserId == actor.UserId))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
        "This reviewer already recorded a decision for the exact source manifest.");
    var review = new FinancialPackageCashFlowAllocationReview
    {
      Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
      EngagementId = package.EngagementId, FinancialPackageId = package.Id,
      PackageRevision = package.Revision, PackageGeneration = package.Generation,
      PackageCalculationHash = package.CalculationHash, SourceManifestHash = expectedHash,
      Decision = decision, EvidenceReference = evidence, Comment = comment,
      DecidedByUserId = actor.UserId, DecidedAt = DateTimeOffset.UtcNow
    };
    db.FinancialPackageCashFlowAllocationReviews.Add(review);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
        "A source-allocation review was recorded concurrently. Reload the current decision before retrying.");
    }
    return CommandResult<Guid>.Ok(review.Id);
  }

  /// <summary>Fail-closed gate used by client, staff review, release and package approval paths.</summary>
  public static async Task<bool> HasCurrentApprovalAsync(IClientAccountingDbContext db, FinancialPackage package,
    CancellationToken ct = default)
  {
    var state = await ReadStateAsync(db, package, ct);
    if (!state.Succeeded || state.Value is null) return false;
    if (!state.Value.Required) return true;
    if (!state.Value.Complete) return false;
    var reviews = await db.FinancialPackageCashFlowAllocationReviews.AsNoTracking().Where(x =>
        x.FirmId == package.FirmId && x.FinancialPackageId == package.Id && x.PackageRevision == package.Revision &&
        x.PackageGeneration == package.Generation && x.PackageCalculationHash == package.CalculationHash &&
        x.SourceManifestHash == state.Value.SourceManifestHash)
      .OrderByDescending(x => x.DecidedAt).ToListAsync(ct);
    return reviews.Count > 0 && reviews.All(x => x.Decision == FinancialPackageReviewDecisions.Approved) &&
      reviews.Select(x => x.DecidedByUserId).Distinct().Count() == reviews.Count &&
      reviews.All(x => state.Value.Movements.SelectMany(m => m.Contributions).All(c => c.PreparedByUserId != x.DecidedByUserId));
  }

  private static async Task<CommandResult<CashFlowAllocationReviewView>> ReadStateAsync(
    IClientAccountingDbContext db, FinancialPackage package, CancellationToken ct)
  {
    var comparativeId = package.ComparativePackageId;
    var packageIds = comparativeId is { } priorId ? new[] { package.Id, priorId } : [package.Id];
    var lines = await db.FinancialPackageCashFlowLines.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
    var allocationRows = await db.FinancialPackageCashFlowAllocations.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.CashFlowLineId).ThenBy(x => x.GeneralLedgerLineId).ToListAsync(ct);
    var noteLines = await db.FinancialPackageNoteLines.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var disclosures = await db.FinancialPackageDisclosures.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var sourceManifest = await FinancialStatementService.ComputePresentationSourceManifestHashAsync(
      db, package.FirmId, packageIds, noteLines, disclosures, ct);
    var movements = lines.Select(line => new CashFlowAllocationReviewMovement(line.FinancialPackageId, line.Id, line.Section,
      line.Description, line.Amount, line.Currency, line.MovementDate, line.ActivityCode,
      allocationRows.Where(x => x.CashFlowLineId == line.Id).Select(x => new CashFlowAllocationReviewContribution(
        x.GeneralLedgerLineId, x.GeneralLedgerTransactionId, x.SourceImportBatchId, x.SourceLineHash,
        x.SignedAmount, x.Currency, x.PreparedByUserId)).ToArray())).ToArray();
    var complete = movements.Length > 0 && movements.All(m => m.Contributions.Count > 0 &&
      m.Contributions.All(c => c.PreparedByUserId is not null && c.Currency == m.Currency) &&
      m.Contributions.Sum(c => c.SignedAmount) == m.Amount);
    var decision = await db.FinancialPackageCashFlowAllocationReviews.AsNoTracking().Where(x =>
        x.FirmId == package.FirmId && x.FinancialPackageId == package.Id && x.PackageRevision == package.Revision &&
        x.PackageGeneration == package.Generation && x.PackageCalculationHash == package.CalculationHash &&
        x.SourceManifestHash == sourceManifest)
      .OrderByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    return CommandResult<CashFlowAllocationReviewView>.Ok(new CashFlowAllocationReviewView(package.Id,
      sourceManifest, lines.Length > 0, complete, false, decision?.Decision ?? "PENDING",
      decision?.EvidenceReference, decision?.Comment, decision?.DecidedByUserId, decision?.DecidedAt, movements));
  }
}
