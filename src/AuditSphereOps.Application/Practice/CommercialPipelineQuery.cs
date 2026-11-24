using System.Data;
using System.Globalization;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>Optional narrowing filters. Owner is "me", "unassigned" or an exact user ID; none of them grants access.</summary>
public sealed record CommercialPipelineFilter(string? Owner = null, string? ServiceRoute = null, string? Currency = null,
  Guid? ClientId = null);

/// <summary>Continuation inside one stage: the last card already shown, in CreatedAt-descending, Id-ascending order.</summary>
public sealed record CommercialPipelineCursor(string Stage, DateTimeOffset BeforeCreatedAt, Guid BeforeId);

/// <summary>Complete expected-fee total for one currency; currencies are never combined or converted.</summary>
public sealed record CommercialPipelineCurrencyTotal(string Currency, int Count, string ExpectedFee);

/// <summary>
/// One opportunity, counted once. The proposal fields use the lead workspace's rule (highest revision for the
/// opportunity); <see cref="ProposalRevisions"/> counts every revision without counting the opportunity again.
/// </summary>
public sealed record CommercialPipelineCard(Guid Id, Guid LeadId, string LeadName, Guid? ClientId, string? ClientName,
  string ServiceRoute, string PeriodStart, string PeriodEnd, string? ExpectedFee, string? Currency, string? Probability,
  Guid? OwnerUserId, string? OwnerName, string OwnerState, string? NextAction, DateTimeOffset CreatedAt,
  Guid? ProposalId, string? ProposalRevision, string? ProposalStatus, int ProposalRevisions,
  string AcceptanceProcess, string AcceptanceStatus, bool CanPrepareCommercial);

/// <summary>A stage column: complete count and totals for the filtered cohort, plus one bounded page of cards.</summary>
public sealed record CommercialPipelineStage(string Stage, int Count, IReadOnlyList<CommercialPipelineCurrencyTotal> Totals,
  int UnavailableCurrencyCount, IReadOnlyList<CommercialPipelineCard> Items, bool HasMore);

public sealed record CommercialPipelineClient(Guid Id, string Name);
public sealed record CommercialPipeline(IReadOnlyList<CommercialPipelineStage> Stages, int PageSize,
  IReadOnlyList<CommercialPipelineClient>? Clients = null, bool ClientsHaveMore = false);

/// <summary>
/// Read-only cross-lead opportunity board grouped by the existing opportunity stages. It performs no stage change;
/// stages move only through the existing proposal and conversion commands.
/// </summary>
public static class CommercialPipelineQuery
{
  public const int DefaultPageSize = 10;
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];
  public static readonly string[] Stages =
  [
    CrmStates.OpportunityDiscovery, CrmStates.OpportunityProposal, CrmStates.OpportunityNegotiation,
    CrmStates.OpportunityWon, CrmStates.OpportunityLost
  ];

  /// <summary>
  /// Without a cursor returns every stage; with one returns only that stage's next page. A cursor that no longer
  /// belongs to the filtered stage is refused, so a stale continuation cannot mix two cohorts.
  /// </summary>
  public static async Task<CommandResult<CommercialPipeline>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    CommercialPipelineFilter? filter = null, CommercialPipelineCursor? cursor = null, int pageSize = DefaultPageSize,
    CancellationToken ct = default)
  {
    filter ??= new();
    if (cursor is not null) cursor = cursor with { BeforeCreatedAt = cursor.BeforeCreatedAt.ToUniversalTime() };
    var service = filter.ServiceRoute?.Trim();
    var currency = filter.Currency?.Trim().ToUpperInvariant();
    if (!PracticeLeadQuery.TryParseOwnerFilter(filter.Owner, actor.UserId, out var ownerId, out var unassigned) ||
        pageSize is < 1 or > 50 || service is { Length: 0 or > 100 } || filter.ClientId == Guid.Empty ||
        (currency is not null && (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))) ||
        (cursor is not null && (!Stages.Contains(cursor.Stage) || cursor.BeforeId == Guid.Empty)))
      return CommandResult<CommercialPipeline>.Fail("request.invalid", "Invalid pipeline filter or continuation.");
    var auth = await AuthorizeReadAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<CommercialPipeline>.Fail(auth.ErrorCode!, "Pipeline unavailable.");

    var query = db.Opportunities.AsNoTracking().Where(o => o.FirmId == actor.FirmId);
    if (service is not null) query = query.Where(o => o.ServiceRoute == service);
    if (currency is not null) query = query.Where(o => o.Currency == currency);
    if (unassigned) query = query.Where(o => o.OwnerUserId == null);
    else if (ownerId is { } owner) query = query.Where(o => o.OwnerUserId == owner);
    var clientOptionsQuery = query;
    if (filter.ClientId is { } clientId) query = query.Where(o => (o.AcceptanceClientId ?? o.PracticeClientId) == clientId);
    var stages = cursor is null ? Stages : [cursor.Stage];

    // Counts, totals and cards come from one observation, so a column never describes a different cohort.
    var snapshot = db.Database.CurrentTransaction is null
      ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
    var columns = new List<CommercialPipelineStage>();
    var clientOptions = new List<CommercialPipelineClient>();
    try
    {
      clientOptions = await db.PracticeClients.AsNoTracking().Where(c => c.FirmId == actor.FirmId &&
          clientOptionsQuery.Any(o => (o.AcceptanceClientId ?? o.PracticeClientId) == c.Id))
        .OrderBy(c => c.CommercialName ?? c.LegalName).ThenBy(c => c.Id)
        .Select(c => new CommercialPipelineClient(c.Id, c.CommercialName ?? c.LegalName)).Take(101).ToListAsync(ct);
      var groups = await query.Where(o => stages.Contains(o.Stage) && o.ExpectedFee != null && o.Currency != null)
        .GroupBy(o => new { o.Stage, o.Currency })
        .Select(g => new { g.Key.Stage, g.Key.Currency, Count = g.Count(), Total = g.Sum(o => o.ExpectedFee!.Value) })
        .ToListAsync(ct);
      var stageCounts = await query.Where(o => stages.Contains(o.Stage)).GroupBy(o => o.Stage)
        .Select(g => new { Stage = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Stage, x => x.Count, ct);
      var pages = new Dictionary<string, List<Opportunity>>();
      foreach (var stage in stages)
      {
        var stageQuery = query.Where(o => o.Stage == stage);
        if (cursor is not null)
        {
          if (!await stageQuery.AnyAsync(o => o.CreatedAt == cursor.BeforeCreatedAt && o.Id == cursor.BeforeId, ct))
            return CommandResult<CommercialPipeline>.Fail("request.invalid", "The pipeline continuation no longer matches. Reload the board.");
          stageQuery = stageQuery.Where(o => o.CreatedAt < cursor.BeforeCreatedAt ||
            (o.CreatedAt == cursor.BeforeCreatedAt && o.Id.CompareTo(cursor.BeforeId) > 0));
        }
        pages[stage] = await stageQuery.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(pageSize + 1).ToListAsync(ct);
      }

      var shown = pages.Values.SelectMany(x => x.Take(pageSize)).ToList();
      var ids = shown.Select(o => o.Id).ToList();
      var leadIds = shown.Select(o => o.LeadId).Distinct().ToList();
      var clientIds = shown.Select(o => o.AcceptanceClientId ?? o.PracticeClientId).Where(x => x.HasValue)
        .Select(x => x!.Value).Distinct().ToList();
      var userIds = shown.Where(o => o.OwnerUserId.HasValue).Select(o => o.OwnerUserId!.Value).Distinct().ToList();
      var leads = await db.Leads.AsNoTracking().Where(l => l.FirmId == actor.FirmId && leadIds.Contains(l.Id))
        .ToDictionaryAsync(l => l.Id, l => l.Name, ct);
      var clients = await db.PracticeClients.AsNoTracking().Where(c => c.FirmId == actor.FirmId && clientIds.Contains(c.Id))
        .ToDictionaryAsync(c => c.Id, c => c.CommercialName ?? c.LegalName, ct);
      var owners = await db.Users.AsNoTracking().Where(u => u.FirmId == actor.FirmId && userIds.Contains(u.Id))
        .Select(u => new { u.Id, u.DisplayName, u.Disabled }).ToDictionaryAsync(u => u.Id, ct);
      var generations = await db.ClientSafetyStates.AsNoTracking().Where(s => s.FirmId == actor.FirmId && clientIds.Contains(s.Id))
        .ToDictionaryAsync(s => s.Id, s => s.InputGeneration, ct);
      var clientsById = await db.PracticeClients.AsNoTracking().Where(c => c.FirmId == actor.FirmId && clientIds.Contains(c.Id))
        .ToDictionaryAsync(c => c.Id, ct);
      var acceptanceDecisions = await db.AcceptanceDecisions.AsNoTracking().Where(d => d.FirmId == actor.FirmId &&
          ids.Contains(d.OpportunityId!.Value) && clientIds.Contains(d.PracticeClientId))
        .ToListAsync(ct);
      var priorAccepted = await db.AcceptanceDecisions.AsNoTracking().Where(d => d.FirmId == actor.FirmId &&
          clientIds.Contains(d.PracticeClientId) &&
          (d.Decision == "Accepted" || d.Decision == "AcceptedWithConditions"))
        .Select(d => new { d.PracticeClientId, d.ServiceRoute }).Distinct().ToListAsync(ct);
      var proposals = await db.Proposals.AsNoTracking().Where(p => p.FirmId == actor.FirmId && ids.Contains(p.OpportunityId))
        .Select(p => new { p.Id, p.OpportunityId, p.Revision, p.Status }).ToListAsync(ct);
      var proposalsByOpportunity = proposals.GroupBy(p => p.OpportunityId).ToDictionary(g => g.Key, g => g.ToList());

      CommercialPipelineCard Card(Opportunity o)
      {
        var revisions = proposalsByOpportunity.GetValueOrDefault(o.Id) ?? [];
        var latest = revisions.OrderByDescending(p => p.Revision).FirstOrDefault();
        var known = o.OwnerUserId is { } key && owners.TryGetValue(key, out var found) ? found : null;
        var clientId = o.AcceptanceClientId ?? o.PracticeClientId;
        var currentDecision = acceptanceDecisions.Where(d => d.OpportunityId == o.Id && d.PracticeClientId == clientId &&
            generations.GetValueOrDefault(d.PracticeClientId) == d.Generation)
          .OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id).FirstOrDefault();
        var latestDecision = currentDecision ?? acceptanceDecisions.Where(d => d.OpportunityId == o.Id && d.PracticeClientId == clientId)
          .OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id).FirstOrDefault();
        var hasStaleDecision = latestDecision is not null &&
          (!generations.TryGetValue(latestDecision.PracticeClientId, out var decisionGeneration) ||
           latestDecision.Generation != decisionGeneration);
        var hasPrior = clientId is { } prospectiveClient && priorAccepted.Any(d => d.PracticeClientId == prospectiveClient && d.ServiceRoute == o.ServiceRoute);
        var canPrepare = clientId is { } acceptedClientId && clientsById.TryGetValue(acceptedClientId, out var client) &&
          generations.TryGetValue(acceptedClientId, out var currentGeneration) && currentDecision is
            { Decision: "Accepted", Conditions: null, DecidedByUserId: not null, DecidedAt: not null } &&
          currentDecision.Generation == currentGeneration && string.Equals(currentDecision.OpportunityBasisSha256,
            OpportunityAcceptanceGate.BasisSha256(o, client), StringComparison.Ordinal);
        var acceptanceStatus = canPrepare ? "ACCEPTED" : hasStaleDecision ? "STALE_ACCEPTANCE" : latestDecision?.Decision switch
        {
          "Pending" => "PENDING_ACCEPTANCE", "Declined" => "DECLINED", "Deferred" => "DEFERRED",
          "AcceptedWithConditions" => "CONDITIONAL", "Accepted" => "STALE_ACCEPTANCE",
          _ => hasPrior ? "CONTINUANCE_REQUIRED" : "PENDING_ACCEPTANCE"
        };
        return new(o.Id, o.LeadId, leads.GetValueOrDefault(o.LeadId, "Lead"), clientId,
          clientId is { } clientKey ? clients.GetValueOrDefault(clientKey) : null, o.ServiceRoute, o.PeriodStart, o.PeriodEnd,
          o.ExpectedFee?.ToString(CultureInfo.InvariantCulture), o.Currency, o.Probability?.ToString(CultureInfo.InvariantCulture),
          o.OwnerUserId, known?.DisplayName,
          o.OwnerUserId is null ? PracticeLeadOwnerStates.Unassigned : known is null ? PracticeLeadOwnerStates.Missing
            : known.Disabled ? PracticeLeadOwnerStates.Disabled : PracticeLeadOwnerStates.Active,
          o.NextAction, o.CreatedAt, latest?.Id, latest?.Revision.ToString(CultureInfo.InvariantCulture), latest?.Status,
          revisions.Count, hasPrior ? "CONTINUANCE" : "CLIENT_ACCEPTANCE", acceptanceStatus, canPrepare);
      }

      foreach (var stage in stages)
      {
        var stageGroups = groups.Where(g => g.Stage == stage).ToList();
        var valid = stageGroups.Where(g => g.Currency is { Length: 3 } code && code.All(char.IsAsciiLetterUpper))
          .OrderBy(g => g.Currency, StringComparer.Ordinal).ToList();
        var count = stageCounts.GetValueOrDefault(stage);
        columns.Add(new(stage, count,
          valid.Select(g => new CommercialPipelineCurrencyTotal(g.Currency!, g.Count, g.Total.ToString(CultureInfo.InvariantCulture))).ToList(),
          count - valid.Sum(g => g.Count), pages[stage].Take(pageSize).Select(Card).ToList(), pages[stage].Count > pageSize));
      }
      if (snapshot is not null) await snapshot.CommitAsync(ct);
    }
    finally
    {
      if (snapshot is not null) await snapshot.DisposeAsync();
    }

    auth = await AuthorizeReadAsync(db, actor, ct);
    return auth.Succeeded ? CommandResult<CommercialPipeline>.Ok(new(columns, pageSize,
      clientOptions.Take(100).ToArray(), clientOptions.Count > 100))
      : CommandResult<CommercialPipeline>.Fail(auth.ErrorCode!, "Pipeline unavailable.");
  }
  internal static Task<CommandResult> AuthorizeReadAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);
}
