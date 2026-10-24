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
    "COMPARATIVE_CONSISTENCY", "NOTE_TO_FACE_TOTALS", "CASH_FLOW_SOURCE_LINEAGE"
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
    CancellationToken ct = default)
  {
    var result = Evaluate(firmId, packageId, await db.FinancialPackageValidations.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.FinancialPackageId == packageId && RequiredCodes.Contains(x.Code)).ToListAsync(ct));
    var source = await (from package in db.FinancialPackages.AsNoTracking()
      join mapping in db.MappingVersions.AsNoTracking() on package.MappingVersionId equals mapping.Id
      join dataset in db.TrialBalanceDatasets.AsNoTracking() on mapping.DatasetId equals dataset.Id
      where package.FirmId == firmId && package.Id == packageId && mapping.FirmId == firmId && dataset.FirmId == firmId
      select dataset).SingleOrDefaultAsync(ct);
    if (source is not null && !(await NativeTrialBalanceSourceWorkspace.CheckReportingAsync(db, source, ct)).Succeeded)
      result = result with { Failed = result.Failed.Concat(["NATIVE_SOURCE_CURRENT"]).ToArray() };
    var nativeBatches = await (from allocation in db.FinancialPackageCashFlowAllocations.AsNoTracking()
      join batch in db.SourceImportBatches.AsNoTracking() on allocation.SourceImportBatchId equals batch.Id
      where allocation.FirmId == firmId && allocation.FinancialPackageId == packageId && batch.FirmId == firmId &&
        batch.OriginKind == SourceImportOrigins.NativeLedger
      select batch).Distinct().ToListAsync(ct);
    if (nativeBatches.Count > 0)
    {
      var mappingId = await db.FinancialPackages.AsNoTracking().Where(x => x.Id == packageId && x.FirmId == firmId)
        .Select(x => x.MappingVersionId).SingleAsync(ct);
      foreach (var batch in nativeBatches)
        if (!(await NativeGeneralLedgerSourceWorkspace.CheckCurrentAsync(db, batch, ct)).Succeeded ||
            !(await NativeGeneralLedgerSourceWorkspace.CheckPackagePairAsync(db, batch, mappingId, ct)).Succeeded)
          return result with { Failed = result.Failed.Concat(["NATIVE_GL_SOURCE_CURRENT"]).ToArray() };
    }
    return result;
  }
}
