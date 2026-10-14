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
/// the newest decision is an independent approval, and the accepted general ledger it cites is still the current one.
/// </summary>
public sealed record CashFlowSourceReviewState(Guid FinancialPackageId, int Movements, int ResolvedMovements, int SourceLinks,
  Guid? CurrentSourceImportBatchId, bool Approved, IReadOnlyList<CashFlowSourceReviewEntry> Reviews);

/// <summary>Independent review of cash-flow source links (CP-11). The preparer who resolved a link can never review it.</summary>
public static partial class FinancialStatementService
{
  private static readonly string[] CashFlowSourceReviewRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] CashFlowSourceReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  public const int MaxCashFlowSourceReviewEntries = 50;

  private sealed record CashFlowLineage(IReadOnlyList<FinancialPackageCashFlowAllocation> Allocations, IReadOnlyList<bool> Resolved,
    int Movements, Guid? CurrentBatchId)
  {
    public bool AllResolved => Movements > 0 && Resolved.All(x => x);
  }

  public static async Task<CommandResult<Guid>> RecordCashFlowSourceReviewAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid packageId, string? decision, string? reason, CancellationToken ct = default)
  {
    var normalized = decision?.Trim().ToUpperInvariant() ?? string.Empty;
    var text = reason?.Trim() ?? string.Empty;
    if (packageId == Guid.Empty || normalized is not ("APPROVED" or "RETURNED") || text.Length > 2000 ||
        (normalized == "RETURNED" && text.Length == 0))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "Choose approve or return, and give a reason when returning.");
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == packageId, ct);
    if (package is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, package.ClientId, package.EngagementId, CashFlowSourceReviewRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var lineage = await LoadCashFlowLineageAsync(db, actor, package, ct);
    if (!lineage.Succeeded)
      return CommandResult<Guid>.Fail(lineage.ErrorCode!, lineage.Message!);
    var current = lineage.Value!;
    if (current.Allocations.Count == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "This package has no source-linked cash-flow movements to review.");
    if (current.Allocations.Any(x => x.CreatedByUserId == Guid.Empty))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A source link has no recorded preparer. Rebuild the package before review.");
    if (current.Allocations.Any(x => x.CreatedByUserId == actor.UserId))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The preparer cannot review the source links they resolved.");
    if (normalized == "APPROVED")
    {
      if (!current.AllResolved)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Every cash-flow movement must be fully allocated before approval.");
      if (current.CurrentBatchId is not Guid currentBatch || current.Allocations.Any(x => x.SourceImportBatchId != currentBatch))
        return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
          "The accepted general ledger changed after these links were resolved. Rebuild the package before review.");
    }
    var batchId = current.Allocations[0].SourceImportBatchId;
    var review = new FinancialPackageCashFlowSourceReview
    {
      Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId, EngagementId = package.EngagementId,
      FinancialPackageId = package.Id, Decision = normalized, Reason = text, SourceImportBatchId = batchId,
      ReviewedByUserId = actor.UserId, ReviewedAt = DateTimeOffset.UtcNow
    };
    db.FinancialPackageCashFlowSourceReviews.Add(review);
    await db.SaveChangesAsync(ct);
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
      .OrderByDescending(x => x.ReviewedAt).ThenByDescending(x => x.Id).Take(MaxCashFlowSourceReviewEntries).ToListAsync(ct);
    var latest = reviews.FirstOrDefault();
    var approved = current.AllResolved && current.CurrentBatchId is Guid batch && latest is { Decision: "APPROVED" } &&
      latest.SourceImportBatchId == batch && current.Allocations.All(x => x.SourceImportBatchId == batch);
    return CommandResult<CashFlowSourceReviewState>.Ok(new CashFlowSourceReviewState(package.Id, current.Movements,
      current.Resolved.Count(x => x), current.Allocations.Count, current.CurrentBatchId, approved,
      reviews.Select(x => new CashFlowSourceReviewEntry(x.Id, x.Decision, x.Reason, x.ReviewedByUserId, x.ReviewedAt, x.SourceImportBatchId)).ToArray()));
  }

  /// <summary>Presentation read: true only for a current independent approval. Any failure reads as not approved.</summary>
  internal static async Task<bool> CashFlowSourceApprovedAsync(IClientAccountingDbContext db, ActorContext actor, FinancialPackage package,
    CancellationToken ct)
  {
    var state = await GetCashFlowSourceReviewAsync(db, actor, package.Id, ct);
    return state.Succeeded && state.Value!.Approved;
  }

  private static async Task<CommandResult<CashFlowLineage>> LoadCashFlowLineageAsync(IClientAccountingDbContext db, ActorContext actor,
    FinancialPackage package, CancellationToken ct)
  {
    var movements = await db.FinancialPackageCashFlowLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id).OrderBy(x => x.Id).ToListAsync(ct);
    var allocations = await db.FinancialPackageCashFlowAllocations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id).OrderBy(x => x.Id).ToListAsync(ct);
    var resolved = ResolvedCashFlowMovements(movements.Select(x => x.Amount).ToArray(),
      movements.Select((line, index) => (line, index)).SelectMany(x => allocations
        .Where(a => a.CashFlowLineId == x.line.Id).Select(a => (MovementIndex: x.index, a.SignedAmount))));
    var selected = await AccountingSourceAcceptanceService.GetSelectedSourceAsync(db, actor, package.ClientId, package.EngagementId,
      AccountingSourceKinds.GeneralLedger, ct);
    if (!selected.Succeeded)
      return CommandResult<CashFlowLineage>.Fail(selected.ErrorCode!, selected.Message!);
    return CommandResult<CashFlowLineage>.Ok(new CashFlowLineage(allocations, resolved, movements.Count, selected.Value?.ImportBatchId));
  }
}
