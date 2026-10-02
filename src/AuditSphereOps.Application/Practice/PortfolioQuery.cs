using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record PortfolioClient(Guid Id, string Name, int Engagements);
public sealed record PortfolioPage(IReadOnlyList<PortfolioClient> Items, int Total, int Page, int PageSize);

/// <summary>Bounded current-grant portfolio projection for browser clients.</summary>
public static class PortfolioQuery
{
  public static readonly string[] Roles = ["Administrator", "Partner", "Manager", "Reviewer", "Senior", "Staff"];

  public static async Task<CommandResult<PortfolioPage>> ListAsync(IAuditSphereDbContext db,
    ActorContext actor, string? search = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
  {
    if (page < 0 || page > 10000 || pageSize < 1 || pageSize > 100 || search?.Length > 100)
      return CommandResult<PortfolioPage>.Fail("request.invalid", "Invalid pagination or search.");
    var grants = await db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId &&
      g.UserId == actor.UserId && g.RevokedAt == null && Roles.Contains(g.Role))
      .Select(g => new { g.ClientId, g.EngagementId }).Distinct().ToListAsync(ct);
    var scopes = new List<AuthorizationRequest>();
    foreach (var g in grants)
    {
      var request = new AuthorizationRequest(actor.FirmId, g.ClientId, g.EngagementId, Roles,
        InternalOnly: true, RequireFirmWide: g.ClientId is null && g.EngagementId is null);
      if ((await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded) scopes.Add(request);
    }
    if (scopes.Count == 0) return CommandResult<PortfolioPage>.Fail(ErrorCodes.ScopeDenied, "Access unavailable.");
    var firmWide = scopes.Any(s => s.RequireFirmWide);
    var clients = scopes.Where(s => s.ClientId.HasValue && !s.EngagementId.HasValue).Select(s => s.ClientId!.Value).ToArray();
    var engagements = scopes.Where(s => s.EngagementId.HasValue).Select(s => s.EngagementId!.Value).ToArray();
    var visibleEngagements = db.Engagements.AsNoTracking().Where(e => e.FirmId == actor.FirmId &&
      (firmWide || clients.Contains(e.PracticeClientId) || engagements.Contains(e.Id)));
    var query = db.PracticeClients.AsNoTracking().Where(c => c.FirmId == actor.FirmId &&
      (firmWide || clients.Contains(c.Id) || visibleEngagements.Any(e => e.PracticeClientId == c.Id)));
    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim().ToLowerInvariant();
      query = query.Where(c => c.LegalName.ToLower().Contains(term) || c.Id.ToString().Contains(term));
    }
    var total = await query.CountAsync(ct);
    var rows = await query.OrderBy(c => c.LegalName).ThenBy(c => c.Id).Skip(page * pageSize).Take(pageSize)
      .Select(c => new PortfolioClient(c.Id, c.LegalName, visibleEngagements.Count(e => e.PracticeClientId == c.Id))).ToListAsync(ct);
    foreach (var scope in scopes)
      if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, scope, ct)).Succeeded)
        return CommandResult<PortfolioPage>.Fail(ErrorCodes.ScopeDenied, "Access unavailable.");
    return CommandResult<PortfolioPage>.Ok(new(rows, total, page, pageSize));
  }
}
