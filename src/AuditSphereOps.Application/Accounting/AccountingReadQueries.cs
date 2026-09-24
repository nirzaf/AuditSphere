using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// Read-side contracts for Module 22 (adjusted TB) and Module 24 (comparative difference).
// All queries are scope-checked, paged, and project typed DTOs — never raw entities.
public sealed record AdjustedBalanceRowDto(
  string AccountCode, decimal RawAmount, decimal AdjustedAmount, decimal Delta, string Currency);

public sealed record AdjustedBalancePage(
  Guid SnapshotId, Guid BaseDatasetId, string ResultHash,
  IReadOnlyList<AdjustedBalanceRowDto> Items, int TotalCount, int Page, int PageSize);

public sealed record ComparativeDifferenceLineDto(
  string DestinationCode, string StatementSection,
  decimal OriginalAmount, decimal RevisedAmount, decimal Delta, string Currency);

public sealed record ComparativeDifferencePage(
  Guid OriginalPackageId, Guid RevisedPackageId,
  IReadOnlyList<ComparativeDifferenceLineDto> Items, int TotalCount);

public static class AccountingReadQueries
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  /// <summary>Paged per-account raw/adjusted/delta rows for one adjusted snapshot
  /// (Module 22). Raw amounts come from the base dataset; the delta is the adjustment
  /// contribution, not a caller-supplied value.</summary>
  public static async Task<CommandResult<AdjustedBalancePage>> GetAdjustedBalancesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid snapshotId,
    int page = 1, int pageSize = 100, CancellationToken ct = default)
  {
    if (snapshotId == Guid.Empty || page < 1 || pageSize is < 1 or > 500)
      return CommandResult<AdjustedBalancePage>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The adjusted-balance page request is invalid.");
    var snapshot = await db.AdjustedTrialBalanceSnapshots.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == snapshotId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null)
      return CommandResult<AdjustedBalancePage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(snapshot.FirmId, snapshot.ClientId, snapshot.EngagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AdjustedBalancePage>.Fail(auth.ErrorCode!, auth.Message!);

    var adjustedRows = await db.AdjustedTrialBalanceRows.AsNoTracking()
      .Where(x => x.SnapshotId == snapshot.Id)
      .OrderBy(x => x.AccountCode)
      .ToListAsync(ct);
    var rawRows = await db.TrialBalanceRows.AsNoTracking()
      .Where(x => x.DatasetId == snapshot.BaseDatasetId)
      .ToDictionaryAsync(x => x.AccountCode, x => x.Amount, ct);

    var all = adjustedRows
      .Select(x =>
      {
        var raw = rawRows.GetValueOrDefault(x.AccountCode, 0m);
        return new AdjustedBalanceRowDto(
          x.AccountCode, MoneyPolicy.Normalize(raw), MoneyPolicy.Normalize(x.Amount),
          MoneyPolicy.Normalize(x.Amount - raw), x.Currency);
      })
      .ToList();
    var totalCount = all.Count;
    var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

    return CommandResult<AdjustedBalancePage>.Ok(new AdjustedBalancePage(
      snapshot.Id, snapshot.BaseDatasetId, snapshot.ResultHash, items, totalCount, page, pageSize));
  }

  /// <summary>Per-line original/revised/delta between an original and a revised package
  /// for the same client/engagement/currency (Module 24 comparative restatement bridge).</summary>
  public static async Task<CommandResult<ComparativeDifferencePage>> GetComparativeDifferenceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid originalPackageId, Guid revisedPackageId,
    CancellationToken ct = default)
  {
    if (originalPackageId == Guid.Empty || revisedPackageId == Guid.Empty || originalPackageId == revisedPackageId)
      return CommandResult<ComparativeDifferencePage>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Two distinct package ids are required.");
    var original = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == originalPackageId && x.FirmId == actor.FirmId, ct);
    if (original is null)
      return CommandResult<ComparativeDifferencePage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(original.FirmId, original.ClientId, original.EngagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<ComparativeDifferencePage>.Fail(auth.ErrorCode!, auth.Message!);

    var revised = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == revisedPackageId && x.FirmId == actor.FirmId &&
        x.ClientId == original.ClientId && x.EngagementId == original.EngagementId, ct);
    if (revised is null)
      return CommandResult<ComparativeDifferencePage>.Fail(ErrorCodes.ScopeDenied,
        "The revised package is outside the same client and engagement scope.");

    var originalLines = await db.FinancialPackageLines.AsNoTracking()
      .Where(x => x.FinancialPackageId == original.Id)
      .GroupBy(x => new { x.DestinationCode, x.StatementSection })
      .Select(g => new { g.Key.DestinationCode, g.Key.StatementSection, Total = g.Sum(x => x.Amount) })
      .ToListAsync(ct);
    var revisedLines = await db.FinancialPackageLines.AsNoTracking()
      .Where(x => x.FinancialPackageId == revised.Id)
      .GroupBy(x => new { x.DestinationCode, x.StatementSection })
      .Select(g => new { g.Key.DestinationCode, g.Key.StatementSection, Total = g.Sum(x => x.Amount) })
      .ToListAsync(ct);

    var revisedMap = revisedLines.ToDictionary(x => (x.DestinationCode, x.StatementSection), x => x.Total);
    var originalMap = originalLines.ToDictionary(x => (x.DestinationCode, x.StatementSection), x => x.Total);
    var allKeys = originalMap.Keys.Union(revisedMap.Keys).OrderBy(x => x.DestinationCode, StringComparer.Ordinal)
      .ThenBy(x => x.StatementSection, StringComparer.Ordinal).ToList();

    var items = allKeys.Select(key =>
    {
      var orig = originalMap.GetValueOrDefault(key, 0m);
      var rev = revisedMap.GetValueOrDefault(key, 0m);
      return new ComparativeDifferenceLineDto(
        key.DestinationCode, key.StatementSection,
        MoneyPolicy.Normalize(orig), MoneyPolicy.Normalize(rev),
        MoneyPolicy.Normalize(rev - orig), original.Currency);
    }).ToList();

    return CommandResult<ComparativeDifferencePage>.Ok(new ComparativeDifferencePage(
      original.Id, revised.Id, items, items.Count));
  }
}
