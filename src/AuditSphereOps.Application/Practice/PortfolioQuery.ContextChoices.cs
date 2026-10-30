using System.Data;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record PortfolioContextChoice(Guid EngagementId, string ServiceRoute, string PeriodStart, string PeriodEnd, string Status);
public sealed record PortfolioContextChoiceCursor(Guid FirmId, Guid UserId, long Epoch, Guid ClientId, int PageSize,
  Guid AfterId, string ScopeStamp);
public sealed record PortfolioContextChoicePage(Guid ClientId, string ClientName, bool CanOpenClient, int PageSize,
  IReadOnlyList<PortfolioContextChoice> Items, bool HasMore, Guid? NextAfterId, string ScopeStamp, DateTimeOffset ValidUntilUtc);

// These requests use the destination owners' actual role sets and do not change their existing reads.
public static partial class WorkspaceQuery
{
  internal static string[] ContextEngagementRoles => EngagementRoles;
  internal static Task<CommandResult> AuthorizeContextClientAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, ClientId: clientId, RequiredRoles: ClientRoles, InternalOnly: true), ct);
}

public static partial class PortfolioQuery
{
  public const int ContextChoiceMaxPageSize = 100;

  public static async Task<CommandResult<PortfolioContextChoicePage>> ContextChoicesAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid clientId, int pageSize = 25, PortfolioContextChoiceCursor? cursor = null, CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || pageSize is < 1 or > ContextChoiceMaxPageSize ||
        cursor is not null && (cursor.FirmId != actor.FirmId || cursor.UserId != actor.UserId || cursor.Epoch != actor.SessionEpoch ||
          cursor.ClientId != clientId || cursor.PageSize != pageSize || cursor.AfterId == Guid.Empty || !ValidContextStamp(cursor.ScopeStamp)))
      return CommandResult<PortfolioContextChoicePage>.Fail("request.invalid", "Invalid context choices request.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<PortfolioContextChoicePage>.Fail(ErrorCodes.GateBlocked, "Context choices require an independent read snapshot.");

    var startedAt = DateTimeOffset.UtcNow;
    Scope portfolioScope;
    Scope? engagementScope;
    string name;
    bool canOpenClient;
    string scopeStamp;
    DateTimeOffset? earliestExpiry;
    List<PortfolioContextChoice> candidates;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var scope = await ScopeAsync(db, actor, ct);
      if (scope is null) return ContextDenied();
      portfolioScope = scope;
      earliestExpiry = await ContextEarliestExpiryAsync(db, actor, startedAt, ct);
      var client = await VisibleClients(db, actor, scope).Where(c => c.Id == clientId)
        .Select(c => new { c.LegalName }).SingleOrDefaultAsync(ct);
      if (client is null) return ContextDenied();
      name = client.LegalName;
      engagementScope = await ScopeAsync(db, actor, ct, WorkspaceQuery.ContextEngagementRoles, caseInsensitiveRoles: true);
      canOpenClient = (await WorkspaceQuery.AuthorizeContextClientAsync(db, actor, clientId, ct)).Succeeded;
      scopeStamp = ContextStamp(portfolioScope, engagementScope, canOpenClient);
      if (cursor is not null && cursor.ScopeStamp != scopeStamp) return ContextChanged();
      if (engagementScope is null) candidates = [];
      else
      {
        var query = VisibleEngagements(db, actor, engagementScope).Where(e => e.PracticeClientId == clientId);
        if (cursor is not null) query = query.Where(e => e.Id.CompareTo(cursor.AfterId) > 0);
        candidates = await query.OrderBy(e => e.Id).Take(pageSize + 1)
          .Select(e => new PortfolioContextChoice(e.Id, e.ServiceRoute, e.PeriodStart, e.PeriodEnd, e.Status)).ToListAsync(ct);
      }
      await snapshot.CommitAsync(ct);
    }

    // Rebuild owner-authorized scopes after the snapshot, then check the bounded candidate set.
    var currentPortfolio = await ScopeAsync(db, actor, ct);
    if (currentPortfolio is null || !await VisibleClients(db, actor, currentPortfolio).AnyAsync(c => c.Id == clientId, ct)) return ContextDenied();
    var currentEngagements = await ScopeAsync(db, actor, ct, WorkspaceQuery.ContextEngagementRoles, caseInsensitiveRoles: true);
    var currentClient = (await WorkspaceQuery.AuthorizeContextClientAsync(db, actor, clientId, ct)).Succeeded;
    if (ContextStamp(currentPortfolio, currentEngagements, currentClient) != scopeStamp ||
        !await CurrentAsync(db, actor, portfolioScope, ct) ||
        engagementScope is not null && !await CurrentAsync(db, actor, engagementScope, ct)) return ContextChanged();
    if (!await ContextCandidatesCurrentAsync(db, actor, clientId, currentEngagements, candidates, ct)) return ContextChanged();
    if (!await CurrentAsync(db, actor, currentPortfolio, ct) ||
        currentEngagements is not null && !await CurrentAsync(db, actor, currentEngagements, ct) ||
        canOpenClient && !(await WorkspaceQuery.AuthorizeContextClientAsync(db, actor, clientId, ct)).Succeeded)
      return ContextChanged();
    var currentExpiry = await ContextEarliestExpiryAsync(db, actor, startedAt, ct);
    if (!await CurrentAsync(db, actor, currentPortfolio, ct) ||
        currentEngagements is not null && !await CurrentAsync(db, actor, currentEngagements, ct) ||
        canOpenClient && !(await WorkspaceQuery.AuthorizeContextClientAsync(db, actor, clientId, ct)).Succeeded)
      return ContextChanged();
    // A client-wide grant can remain valid after an engagement moves to another client.
    if (!await ContextCandidatesCurrentAsync(db, actor, clientId, currentEngagements, candidates, ct)) return ContextChanged();
    var validUntil = startedAt.AddSeconds(30);
    if (earliestExpiry is { } firstExpiry && firstExpiry < validUntil) validUntil = firstExpiry;
    if (currentExpiry is { } laterExpiry && laterExpiry < validUntil) validUntil = laterExpiry;
    if (validUntil <= DateTimeOffset.UtcNow) return ContextChanged();
    var hasMore = candidates.Count > pageSize;
    var items = candidates.Take(pageSize).ToArray();
    return CommandResult<PortfolioContextChoicePage>.Ok(new(clientId, name, canOpenClient, pageSize, items, hasMore,
      hasMore ? items[^1].EngagementId : null, scopeStamp, validUntil));
  }

  private static async Task<bool> ContextCandidatesCurrentAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId,
    Scope? scope, IReadOnlyList<PortfolioContextChoice> candidates, CancellationToken ct)
  {
    if (candidates.Count == 0) return true;
    if (scope is null) return false;
    var ids = candidates.Select(c => c.EngagementId).ToArray();
    // Engagement-only authority retains the client/engagement pair validated by the owner.
    var scopedIds = scope.Requests.Where(r => r.ClientId == clientId && r.EngagementId.HasValue)
      .Select(r => r.EngagementId!.Value).ToArray();
    var coversClient = scope.FirmWide || scope.Clients.Contains(clientId);
    var currentIds = await db.Engagements.AsNoTracking().TagWith("PortfolioContextChoicesCandidateLinkage")
      .Where(e => e.FirmId == actor.FirmId && e.PracticeClientId == clientId && ids.Contains(e.Id) &&
        (coversClient || scopedIds.Contains(e.Id)))
      .Select(e => e.Id).ToListAsync(ct);
    return currentIds.Count == ids.Length && ids.ToHashSet().SetEquals(currentIds);
  }

  // Considering every active grant is conservative when an unrelated grant expires sooner.
  private static Task<DateTimeOffset?> ContextEarliestExpiryAsync(IAuditSphereDbContext db, ActorContext actor,
    DateTimeOffset startedAt, CancellationToken ct) => db.RoleGrants.AsNoTracking()
      .Where(g => g.FirmId == actor.FirmId && g.UserId == actor.UserId && g.RevokedAt == null && g.ExpiresAt > startedAt)
      .Select(g => g.ExpiresAt).MinAsync(ct);

  private static string ContextStamp(Scope portfolio, Scope? engagements, bool canOpenClient)
  {
    static string Part(Scope? scope) => scope is null ? "none" :
      (scope.FirmWide ? "1" : "0") + ":" + string.Join(',', scope.Clients.Order().Select(x => x.ToString("D"))) + ":" +
      string.Join(',', scope.Engagements.Order().Select(x => x.ToString("D")));
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Part(portfolio) + "|" + Part(engagements) + "|" + (canOpenClient ? "1" : "0"))));
  }

  private static bool ValidContextStamp(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
  private static CommandResult<PortfolioContextChoicePage> ContextDenied() => CommandResult<PortfolioContextChoicePage>.Fail(ErrorCodes.ScopeDenied, "Context unavailable.");
  private static CommandResult<PortfolioContextChoicePage> ContextChanged() => CommandResult<PortfolioContextChoicePage>.Fail("scope.changed", "Context changed; start again.");
}
