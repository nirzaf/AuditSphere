using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record MappingSourceAccount(string AccountCode, string AccountName, decimal Amount, string Currency);
public sealed record MappingCandidate(string Code, string Name);
public sealed record MappingSuggestion(string AccountCode, string AccountName, IReadOnlyList<MappingCandidate> Candidates, string State);
public sealed record MappingSplit(string AccountCode, int DestinationCount, decimal TotalFraction);
public sealed record MappingImpact(string DestinationCode, string StatementSection, decimal Amount, int SourceCount);
public sealed record MappingComparison(string SourceAccountCode, string Prior, string Current, string Status);
public sealed record MappingAllocationView(string SourceAccountCode, string DestinationCode, string StatementSection, decimal Fraction, string Rationale);
public sealed record MappingWorkbench(Guid Id, string Status, Guid DatasetId, string ChartLabel, string TaxonomyVersion, string TaxonomyScope, string PeriodStart,
  string PeriodEnd, long Version, long Generation, int SourceAccountCount, IReadOnlyList<MappingSourceAccount> Unmapped, IReadOnlyList<MappingSuggestion> Suggestions,
  IReadOnlyList<MappingSplit> Splits, IReadOnlyList<MappingImpact> Impacts, Guid? PriorMappingId, long? PriorVersion, string? PriorStatus, int PriorAllocationCount,
  IReadOnlyList<MappingComparison> Comparison, IReadOnlyList<MappingAllocationView> Allocations);

/// <summary>
/// Read-only mapping workbench: lineage, prior comparison, unmapped accounts, review-only name-token suggestions, split
/// allocations and report impact. Suggestions are never allocations; approval stays with the guarded mapping command.
/// </summary>
public static class MappingWorkbenchQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];

  public static async Task<CommandResult<MappingWorkbench>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid mappingId, CancellationToken ct = default)
  {
    var mapping = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mappingId && x.FirmId == actor.FirmId, ct);
    if (mapping is null) return CommandResult<MappingWorkbench>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, mapping.ClientId, mapping.EngagementId, Roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<MappingWorkbench>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var taxonomy = await db.ReportingTaxonomyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Code == mapping.TaxonomyVersion, ct);
    var chart = mapping.ClientChartVersionId is { } chartId
      ? await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == mapping.ClientId && x.Id == chartId, ct) : null;
    var allocations = await db.MappingAllocations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == mapping.ClientId &&
        x.EngagementId == mapping.EngagementId && x.MappingVersionId == mapping.Id)
      .OrderBy(x => x.SourceAccountCode).ThenBy(x => x.DestinationCode).ToListAsync(ct);
    var rows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == mapping.DatasetId).OrderBy(x => x.AccountCode)
      .Select(x => new MappingSourceAccount(x.AccountCode, x.AccountName, x.Amount, x.Currency)).ToListAsync(ct);
    var sources = rows.GroupBy(x => x.AccountCode, StringComparer.Ordinal)
      .Select(x => new MappingSourceAccount(x.Key, x.First().AccountName, x.Sum(y => y.Amount), x.First().Currency)).ToList();
    var mapped = allocations.Select(x => x.SourceAccountCode).ToHashSet(StringComparer.Ordinal);
    var unmapped = sources.Where(x => x.Amount != 0m && !mapped.Contains(x.AccountCode)).ToList();
    var nodes = taxonomy is null ? [] : await db.ReportingTaxonomyNodes.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomy.Id && x.IsPosting).Select(x => new MappingCandidate(x.Code, x.Name)).ToListAsync(ct);
    var suggestions = unmapped.Select(account =>
    {
      var tokens = Tokens(account.AccountName);
      var ranked = nodes.Select(node => new { Node = node, Score = tokens.Intersect(Tokens(node.Name), StringComparer.OrdinalIgnoreCase).Count() })
        .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Node.Code, StringComparer.Ordinal).ToArray();
      var best = ranked.FirstOrDefault()?.Score ?? 0;
      var candidates = ranked.Where(x => x.Score == best).Select(x => x.Node).Take(5).ToArray();
      return new MappingSuggestion(account.AccountCode, account.AccountName, candidates,
        candidates.Length > 1 ? "AMBIGUOUS · REVIEW" : candidates.Length == 1 ? "CANDIDATE · REVIEW" : "NO CANDIDATE");
    }).Where(x => x.Candidates.Count > 0).ToList();
    var splits = allocations.GroupBy(x => x.SourceAccountCode, StringComparer.Ordinal).Where(x => x.Count() > 1)
      .Select(x => new MappingSplit(x.Key, x.Select(y => y.DestinationCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(), x.Sum(y => y.Fraction))).ToList();
    var amounts = sources.ToDictionary(x => x.AccountCode, x => x.Amount, StringComparer.Ordinal);
    var impacts = allocations.GroupBy(x => new { x.DestinationCode, x.StatementSection })
      .Select(x => new MappingImpact(x.Key.DestinationCode, x.Key.StatementSection, MoneyPolicy.Normalize(x.Sum(y => amounts.GetValueOrDefault(y.SourceAccountCode) * y.Fraction)),
        x.Select(y => y.SourceAccountCode).Distinct(StringComparer.Ordinal).Count())).OrderBy(x => x.DestinationCode).ToList();
    var prior = await db.MappingVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == mapping.ClientId &&
      x.EngagementId == mapping.EngagementId && x.Version < mapping.Version).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    var comparison = new List<MappingComparison>();
    var priorCount = 0;
    if (prior is not null)
    {
      var priorAllocations = await db.MappingAllocations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.MappingVersionId == prior.Id).ToListAsync(ct);
      priorCount = priorAllocations.Count;
      var current = allocations.GroupBy(x => x.SourceAccountCode, StringComparer.Ordinal).ToDictionary(x => x.Key, Summary, StringComparer.Ordinal);
      var before = priorAllocations.GroupBy(x => x.SourceAccountCode, StringComparer.Ordinal).ToDictionary(x => x.Key, Summary, StringComparer.Ordinal);
      comparison = current.Keys.Concat(before.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Select(code =>
      {
        var p = before.GetValueOrDefault(code, "—"); var c = current.GetValueOrDefault(code, "—");
        return new MappingComparison(code, p, c, p == "—" ? "NEW" : c == "—" ? "REMOVED" : string.Equals(p, c, StringComparison.Ordinal) ? "UNCHANGED" : "CHANGED");
      }).ToList();
    }
    return CommandResult<MappingWorkbench>.Ok(new(mapping.Id, mapping.Status, mapping.DatasetId,
      chart is null ? "Legacy/unbound" : $"v{chart.Version} · {chart.SourceScope} · {chart.Status}", mapping.TaxonomyVersion,
      taxonomy is null ? "Unavailable" : $"{taxonomy.OverlayScope} · base {taxonomy.BaseTaxonomyVersionId?.ToString("D") ?? "none"}",
      mapping.PeriodStart, mapping.PeriodEnd, mapping.Version, mapping.Generation, sources.Count, unmapped, suggestions, splits, impacts,
      prior?.Id, prior?.Version, prior?.Status, priorCount, comparison,
      allocations.Select(x => new MappingAllocationView(x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Fraction, x.Rationale)).ToList()));
  }

  private static HashSet<string> Tokens(string value) => value.Split([' ', '/', '-', '_', '.', ',', ':', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
    .Where(x => x.Length >= 3).ToHashSet(StringComparer.OrdinalIgnoreCase);

  private static string Summary(IEnumerable<MappingAllocation> rows) =>
    string.Join("; ", rows.OrderBy(x => x.DestinationCode, StringComparer.Ordinal).Select(x => $"{x.DestinationCode} ({x.Fraction * 100m:0.00}%)"));
}
