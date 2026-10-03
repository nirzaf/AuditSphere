using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record PortfolioMetrics(int Clients, int Engagements, int OpenHolds, int PendingOperations,
  int ReadyCandidates, int IssuedReleases);
public sealed record PortfolioCandidate(Guid Id, Guid ClientId, Guid EngagementId, string ClientName,
  string TargetKind, string TargetRevision, string Status, DateTimeOffset CreatedAt);
public sealed record PortfolioPackage(Guid Id, Guid ClientId, Guid EngagementId, string ClientName,
  string Framework, string PeriodStart, string PeriodEnd, string Status);
public sealed record PortfolioWorkspace(string Search, PortfolioPage Clients, PortfolioMetrics Metrics,
  bool HasActiveMicrosoftConfiguration, int RecentLimit, int CandidateTotal, int PackageTotal,
  IReadOnlyList<PortfolioCandidate> Candidates, IReadOnlyList<PortfolioPackage> Packages);
public sealed record PortfolioExport(string FileName, string Csv, int ClientCount, int CandidateCount, int PackageCount);

public static partial class PortfolioQuery
{
  public const int RecentLimit = 25;
  public const int ExportClientLimit = 1000;

  public static async Task<CommandResult<PortfolioWorkspace>> WorkspaceAsync(IAuditSphereDbContext db,
    ActorContext actor, string? search = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
  {
    if (page < 0 || page > 10000 || pageSize < 1 || pageSize > 100 || search?.Length > 100)
      return CommandResult<PortfolioWorkspace>.Fail("request.invalid", "Invalid pagination or search.");
    var scope = await ScopeAsync(db, actor, ct);
    if (scope is null) return CommandResult<PortfolioWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access unavailable.");
    return await WorkspaceScopedAsync(db, actor, scope, search, page, pageSize, ct);
  }

  private static async Task<CommandResult<PortfolioWorkspace>> WorkspaceScopedAsync(IAuditSphereDbContext db,
    ActorContext actor, Scope scope, string? search, int page, int pageSize, CancellationToken ct)
  {
    var clients = await ListScopedAsync(db, actor, scope, search, page, pageSize, ct);
    var engagements = VisibleEngagements(db, actor, scope);
    var visibleClients = VisibleClients(db, actor, scope);
    // Both the immutable client and engagement must match the currently visible relationship.
    var candidates = db.ReleaseCandidates.AsNoTracking().Where(r => r.FirmId == actor.FirmId &&
      engagements.Any(e => e.Id == r.EngagementId && e.PracticeClientId == r.ClientId));
    var releases = db.Releases.AsNoTracking().Where(r => r.FirmId == actor.FirmId &&
      engagements.Any(e => e.Id == r.EngagementId && e.PracticeClientId == r.ClientId));
    var packages = db.FinancialPackages.AsNoTracking().Where(r => r.FirmId == actor.FirmId &&
      engagements.Any(e => e.Id == r.EngagementId && e.PracticeClientId == r.ClientId));
    var operations = db.DurableOperations.AsNoTracking().Where(o => o.FirmId == actor.FirmId &&
      (o.EngagementId.HasValue
        ? engagements.Any(e => e.Id == o.EngagementId && (!o.ClientId.HasValue || e.PracticeClientId == o.ClientId))
        : scope.FirmWide || o.ClientId.HasValue && scope.Clients.Contains(o.ClientId.Value)) &&
      o.Status != OperationState.COMPLETED && o.Status != OperationState.CANCELLED_WITH_DISPOSITION);
    var metrics = new PortfolioMetrics(await visibleClients.CountAsync(ct), await engagements.CountAsync(ct),
      await db.EngagementHolds.CountAsync(h => h.FirmId == actor.FirmId && !h.Released &&
        engagements.Any(e => e.Id == h.EngagementId), ct), await operations.CountAsync(ct),
      await candidates.CountAsync(c => c.Status == ReleaseStates.Ready, ct), await releases.CountAsync(ct));
    var candidateRows = await (from r in candidates
      join c in visibleClients on r.ClientId equals c.Id
      orderby r.CreatedAt descending, r.Id
      select new { r.Id, r.ClientId, r.EngagementId, c.LegalName, r.TargetKind, r.TargetRevision, r.Status, r.CreatedAt })
      .Take(RecentLimit).ToListAsync(ct);
    var packageRows = await (from r in packages
      join c in visibleClients on r.ClientId equals c.Id
      orderby r.CreatedAt descending, r.Id
      select new PortfolioPackage(r.Id, r.ClientId, r.EngagementId, c.LegalName,
        r.Framework, r.PeriodStart, r.PeriodEnd, r.Status)).Take(RecentLimit).ToListAsync(ct);
    var term = search?.Trim() ?? "";
    var recentCandidates = candidateRows.Select(r => new PortfolioCandidate(r.Id, r.ClientId, r.EngagementId,
      r.LegalName, r.TargetKind, r.TargetRevision.ToString(CultureInfo.InvariantCulture), r.Status, r.CreatedAt))
      .Where(r => Matches(term, $"{r.ClientName} {r.Id:D} {r.Status} {r.TargetRevision}")).ToArray();
    var recentPackages = packageRows.Where(r => Matches(term,
      $"{r.ClientName} {r.Id:D} {r.Framework} {r.PeriodStart} {r.PeriodEnd} {r.Status}")).ToArray();
    var active = await db.Microsoft365ConnectionRevisions.AsNoTracking().AnyAsync(r => r.FirmId == actor.FirmId &&
      r.State == Microsoft365RevisionStates.Active, ct);
    var candidateTotal = await candidates.CountAsync(ct);
    var packageTotal = await packages.CountAsync(ct);
    if (!await CurrentAsync(db, actor, scope, ct))
      return CommandResult<PortfolioWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access unavailable.");
    return CommandResult<PortfolioWorkspace>.Ok(new(term, clients, metrics, active, RecentLimit,
      candidateTotal, packageTotal, recentCandidates, recentPackages));
  }

  public static async Task<CommandResult<PortfolioExport>> ExportAsync(IAuditSphereDbContext db,
    ActorContext actor, string? search = null, CancellationToken ct = default)
  {
    if (search?.Length > 100) return CommandResult<PortfolioExport>.Fail("request.invalid", "Invalid search.");
    var scope = await ScopeAsync(db, actor, ct);
    if (scope is null) return CommandResult<PortfolioExport>.Fail(ErrorCodes.ScopeDenied, "Access unavailable.");
    // Reuse the same authorized scope snapshot for every export section and recheck it before delivery.
    var workspace = await WorkspaceScopedAsync(db, actor, scope, search, 0, 25, ct);
    if (!workspace.Succeeded) return CommandResult<PortfolioExport>.Fail(workspace.ErrorCode ?? ErrorCodes.ScopeDenied,
      workspace.Message ?? "Access unavailable.");
    var query = VisibleClients(db, actor, scope);
    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim().ToLowerInvariant();
      query = query.Where(c => c.LegalName.ToLower().Contains(term) || c.Id.ToString().Contains(term));
    }
    var engagements = VisibleEngagements(db, actor, scope);
    var rows = await query.OrderBy(c => c.LegalName).ThenBy(c => c.Id).Take(ExportClientLimit + 1)
      .Select(c => new PortfolioClient(c.Id, c.LegalName, engagements.Count(e => e.PracticeClientId == c.Id))).ToListAsync(ct);
    if (rows.Count > ExportClientLimit)
      return CommandResult<PortfolioExport>.Fail("export.limit", "Narrow the search before exporting. The client export limit was exceeded.");
    var lines = new List<string> { "record_type,client,identifier,status,period,details" };
    lines.AddRange(rows.Select(c => CsvRow("CLIENT", c.Name, c.Id.ToString("D"), "SCOPED", "",
      $"Visible engagements: {c.Engagements}")));
    lines.AddRange(workspace.Value!.Candidates.Select(c => CsvRow("RELEASE_CANDIDATE", c.ClientName,
      c.Id.ToString("D"), c.Status, "", $"Target revision: {c.TargetRevision}")));
    lines.AddRange(workspace.Value.Packages.Select(p => CsvRow("FINANCIAL_PACKAGE", p.ClientName,
      p.Id.ToString("D"), p.Status, $"{p.PeriodStart} to {p.PeriodEnd}", p.Framework)));
    if (!await CurrentAsync(db, actor, scope, ct))
      return CommandResult<PortfolioExport>.Fail(ErrorCodes.ScopeDenied, "Access unavailable.");
    return CommandResult<PortfolioExport>.Ok(new("auditsphere-portfolio.csv", string.Join('\n', lines),
      rows.Count, workspace.Value.Candidates.Count, workspace.Value.Packages.Count));
  }

  private static bool Matches(string term, string content) => content.Contains(term, StringComparison.OrdinalIgnoreCase);
  private static string CsvRow(params string[] fields) => string.Join(',', fields.Select(Csv));
  private static string Csv(string value)
  {
    var first = value.AsSpan().TrimStart();
    var formula = first.Length > 0 && first[0] is '=' or '+' or '-' or '@';
    var control = value.Length > 0 && value[0] is '\t' or '\r' or '\n';
    return "\"" + ((formula || control ? "'" : "") + value).Replace("\"", "\"\"") + "\"";
  }
}
