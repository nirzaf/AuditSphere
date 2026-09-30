using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>A mapped line with the allocation's audit area, used for statements and their drill-down.</summary>
public sealed record MappedStatementLine(string SourceAccountCode, string DestinationCode, string StatementSection, string? AuditArea, decimal Amount);

/// <summary>
/// The engagement's current approved mapping over its sealed, balanced trial balance, expanded into mapped lines with
/// the same allocation arithmetic as financial packages. Shared by the materiality engine and statement drill-down.
/// </summary>
internal static class MappedTrialBalanceSource
{
  internal sealed record Source(MappingVersion Mapping, TrialBalanceDataset Dataset, IReadOnlyList<MappedStatementLine> Lines)
  {
    public IReadOnlyList<MappedBenchmarkLine> BenchmarkLines => Lines.Select(x => new MappedBenchmarkLine(x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Amount)).ToList();
  }

  internal static IQueryable<MappingVersion> CurrentMappingQuery(IAuditSphereDbContext db, Guid firmId, Guid engagementId) =>
    db.MappingVersions.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId && x.Status == AccountingPackageStates.MappingApproved)
      .OrderByDescending(x => x.ApprovedAt).ThenByDescending(x => x.Version);

  internal static string Digest(TrialBalanceDataset dataset) =>
    string.IsNullOrEmpty(dataset.NormalizedDatasetDigest) ? dataset.Sha256Hex : dataset.NormalizedDatasetDigest;

  internal static async Task<Source?> LoadAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var mapping = await CurrentMappingQuery(db, firmId, engagementId).FirstOrDefaultAsync(ct);
    if (mapping is null) return null;
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mapping.DatasetId && x.FirmId == firmId, ct);
    if (dataset is null || dataset.ImportState != TrialBalanceImportStates.Sealed || !dataset.Balanced) return null;
    var balances = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == dataset.Id)
      .GroupBy(x => x.AccountCode).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
    var allocations = await db.MappingAllocations.AsNoTracking().Where(x => x.FirmId == firmId && x.MappingVersionId == mapping.Id).ToListAsync(ct);
    var areas = allocations.GroupBy(x => (x.SourceAccountCode, x.DestinationCode)).ToDictionary(g => g.Key, g => g.First().AuditArea);
    var lines = FinancialStatementCalculator.BuildPackageLines(balances,
        allocations.Select(x => new MappingAllocationInput(x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Fraction, x.Rationale, x.AuditArea)).ToList(),
        dataset.Currency)
      .Select(x => new MappedStatementLine(x.SourceAccountCode, x.DestinationCode, x.StatementSection, areas.GetValueOrDefault((x.SourceAccountCode, x.DestinationCode)), x.Amount))
      .ToList();
    return new(mapping, dataset, lines);
  }
}
