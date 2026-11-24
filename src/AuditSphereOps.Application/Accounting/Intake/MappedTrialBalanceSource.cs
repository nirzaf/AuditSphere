using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>A mapped line with the allocation's audit area, used for statements and their drill-down.</summary>
public sealed record MappedStatementLine(string SourceAccountCode, string DestinationCode, string StatementSection, string? AuditArea, decimal Amount);

/// <summary>
/// The current approved mapping for one unambiguous engagement reporting context, expanded into mapped lines with
/// the same allocation arithmetic as financial packages. Shared by the materiality engine and statement drill-down.
/// </summary>
internal static class MappedTrialBalanceSource
{
  internal sealed record Source(MappingVersion Mapping, TrialBalanceDataset Dataset, IReadOnlyList<MappedStatementLine> Lines)
  {
    public IReadOnlyList<MappedBenchmarkLine> BenchmarkLines => Lines.Select(x => new MappedBenchmarkLine(x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Amount)).ToList();
  }

  internal static async Task<(MappingVersion Mapping, TrialBalanceDataset Dataset)?> ResolveAsync(
    IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct, Guid? mappingId = null)
  {
    if (mappingId == Guid.Empty) return null;
    var candidates = from mapping in db.MappingVersions.AsNoTracking()
      join dataset in db.TrialBalanceDatasets.AsNoTracking()
        on new { mapping.FirmId, mapping.ClientId, mapping.EngagementId, mapping.DatasetId }
        equals new { dataset.FirmId, dataset.ClientId, dataset.EngagementId, DatasetId = dataset.Id }
      where mapping.FirmId == firmId && mapping.EngagementId == engagementId && mapping.Status == AccountingPackageStates.MappingApproved &&
        db.Engagements.Any(e => e.FirmId == firmId && e.Id == engagementId && e.PracticeClientId == mapping.ClientId) &&
        dataset.ImportState == TrialBalanceImportStates.Sealed && dataset.ValidationStatus == "Accepted" &&
        dataset.Balanced && dataset.ControlTotal == 0m
      select new { Mapping = mapping, Dataset = dataset };

    // Without an explicit selection, every candidate must share one context.
    // A supplied mapping anchors the context, but must still be its latest revision.
    var context = mappingId is { } id
      ? candidates.Where(candidate => candidates.Any(anchor => anchor.Mapping.Id == id &&
        anchor.Dataset.PeriodId == candidate.Dataset.PeriodId &&
        anchor.Dataset.BookId == candidate.Dataset.BookId &&
        anchor.Dataset.Basis == candidate.Dataset.Basis &&
        anchor.Dataset.Currency == candidate.Dataset.Currency &&
        anchor.Dataset.LegalEntityKey == candidate.Dataset.LegalEntityKey &&
        anchor.Mapping.PeriodStart == candidate.Mapping.PeriodStart && anchor.Mapping.PeriodEnd == candidate.Mapping.PeriodEnd))
      : candidates.Where(candidate => !candidates.Any(other =>
        other.Dataset.PeriodId != candidate.Dataset.PeriodId ||
        other.Dataset.BookId != candidate.Dataset.BookId ||
        other.Dataset.Basis != candidate.Dataset.Basis ||
        other.Dataset.Currency != candidate.Dataset.Currency ||
        other.Dataset.LegalEntityKey != candidate.Dataset.LegalEntityKey ||
        other.Mapping.PeriodStart != candidate.Mapping.PeriodStart || other.Mapping.PeriodEnd != candidate.Mapping.PeriodEnd));
    var selected = await context
      .OrderByDescending(x => x.Mapping.ApprovedAt).ThenByDescending(x => x.Mapping.Version)
      .ThenByDescending(x => x.Mapping.Id)
      .FirstOrDefaultAsync(ct);
    return selected is null || mappingId.HasValue && selected.Mapping.Id != mappingId.Value
      ? null : (selected.Mapping, selected.Dataset);
  }

  internal static string Digest(TrialBalanceDataset dataset) =>
    dataset.SourceKind == NativeTrialBalanceSourceWorkspace.SourceKind ? dataset.Sha256Hex :
    string.IsNullOrEmpty(dataset.NormalizedDatasetDigest) ? dataset.Sha256Hex : dataset.NormalizedDatasetDigest;

  internal static async Task<Source?> LoadAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var selected = await ResolveAsync(db, firmId, engagementId, ct);
    if (selected is null) return null;
    var (mapping, dataset) = selected.Value;
    if (!(await NativeTrialBalanceSourceWorkspace.CheckReportingAsync(db, dataset, ct)).Succeeded) return null;
    var balances = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == dataset.Id)
      .GroupBy(x => x.AccountCode).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
    var allocations = await db.MappingAllocations.AsNoTracking().Where(x => x.FirmId == firmId && x.MappingVersionId == mapping.Id).ToListAsync(ct);
    return Build(mapping, dataset, balances, allocations);
  }

  internal static Source Build(MappingVersion mapping, TrialBalanceDataset dataset,
    IReadOnlyDictionary<string, decimal> balances, IReadOnlyList<MappingAllocation> allocations)
  {
    var areas = allocations.GroupBy(x => (x.SourceAccountCode, x.DestinationCode)).ToDictionary(g => g.Key, g => g.First().AuditArea);
    var lines = FinancialStatementCalculator.BuildPackageLines(balances,
        allocations.Select(x => new MappingAllocationInput(x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Fraction, x.Rationale, x.AuditArea)).ToList(),
        dataset.Currency)
      .Select(x => new MappedStatementLine(x.SourceAccountCode, x.DestinationCode, x.StatementSection, areas.GetValueOrDefault((x.SourceAccountCode, x.DestinationCode)), x.Amount))
      .ToList();
    return new(mapping, dataset, lines);
  }
}
