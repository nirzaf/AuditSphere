using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record PortfolioClient(Guid Id, string Name, int Engagements);
public sealed record PortfolioPage(IReadOnlyList<PortfolioClient> Items, int Total, int Page, int PageSize);

/// <summary>Bounded current-grant portfolio projection for browser clients.</summary>
public static partial class PortfolioQuery
{
  public static readonly string[] Roles = ["Administrator", "Partner", "Manager", "Reviewer", "Senior", "Staff"];

  public static async Task<CommandResult<PortfolioPage>> ListAsync(IAuditSphereDbContext db,
    ActorContext actor, string? search = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
  {
    if (page < 0 || page > 10000 || pageSize < 1 || pageSize > 100 || search?.Length > 100)
      return CommandResult<PortfolioPage>.Fail("request.invalid", "Invalid pagination or search.");
    var scope = await ScopeAsync(db, actor, ct);
    if (scope is null) return CommandResult<PortfolioPage>.Fail(ErrorCodes.ScopeDenied, "Access unavailable.");
    var result = await ListScopedAsync(db, actor, scope, search, page, pageSize, ct);
    if (!await CurrentAsync(db, actor, scope, ct))
      return CommandResult<PortfolioPage>.Fail(ErrorCodes.ScopeDenied, "Access unavailable.");
    return CommandResult<PortfolioPage>.Ok(result);
  }

  private static async Task<PortfolioPage> ListScopedAsync(IAuditSphereDbContext db, ActorContext actor,
    Scope scope, string? search, int page, int pageSize, CancellationToken ct)
  {
    var visibleEngagements = VisibleEngagements(db, actor, scope);
    var query = VisibleClients(db, actor, scope);
    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim().ToLowerInvariant();
      query = query.Where(c => c.LegalName.ToLower().Contains(term) || c.Id.ToString().Contains(term));
    }
    var total = await query.CountAsync(ct);
    var rows = await query.OrderBy(c => c.LegalName).ThenBy(c => c.Id).Skip(page * pageSize).Take(pageSize)
      .Select(c => new PortfolioClient(c.Id, c.LegalName, visibleEngagements.Count(e => e.PracticeClientId == c.Id))).ToListAsync(ct);
    return new(rows, total, page, pageSize);
  }
}
