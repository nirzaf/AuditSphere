using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Completeness of the existing canonical package validation population. This is not
/// final report/profile approval: source-backed presentation and professional gates
/// remain separate until their own versioned evidence exists.
/// </summary>
public static class FinancialPackageValidationReadiness
{
  public static readonly string[] RequiredCodes =
  [
    "TB_BALANCED", "MAPPING_COMPLETE", "ADJUSTMENTS_REPRODUCIBLE", "PACKAGE_BALANCED",
    "STATEMENT_CROSS_CAST", "ACCOUNTING_EQUATION", "EQUITY_ROLLFORWARD", "EQUITY_PROFIT",
    "COMPARATIVE_CONSISTENCY", "NOTE_TO_FACE_TOTALS"
  ];

  public sealed record Result(IReadOnlyList<string> Missing, IReadOnlyList<string> Failed,
    IReadOnlyList<string> Ambiguous)
  {
    public bool Ready => Missing.Count == 0 && Failed.Count == 0 && Ambiguous.Count == 0;
    public string Message => $"Package validation readiness is incomplete. Missing: {string.Join(", ", Missing)}; " +
      $"failed: {string.Join(", ", Failed)}; ambiguous: {string.Join(", ", Ambiguous)}.";
  }

  public static Result Evaluate(Guid firmId, Guid packageId, IEnumerable<FinancialPackageValidation> validations)
  {
    var scoped = validations.Where(x => x.FirmId == firmId && x.FinancialPackageId == packageId)
      .GroupBy(x => x.Code, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
    return new Result(
      RequiredCodes.Where(x => !scoped.ContainsKey(x)).ToArray(),
      RequiredCodes.Where(x => scoped.TryGetValue(x, out var rows) && rows.Any(v => !v.Passed)).ToArray(),
      RequiredCodes.Where(x => scoped.TryGetValue(x, out var rows) && rows.Length != 1).ToArray());
  }

  public static async Task<Result> GetAsync(IClientAccountingDbContext db, Guid firmId, Guid packageId,
    CancellationToken ct = default) => Evaluate(firmId, packageId,
      await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FirmId == firmId && x.FinancialPackageId == packageId && RequiredCodes.Contains(x.Code))
        .ToListAsync(ct));
}
