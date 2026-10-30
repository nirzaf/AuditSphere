using System.Data;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>
/// One lead row. <see cref="LifecycleStage"/> is set on the paged view only: LEAD_INGESTION while no proposal exists for the
/// lead (STE 11-stage lifecycle, stage 1), and PROPOSAL_OR_LATER once a proposal exists, whose own stages are shown on it.
/// </summary>
public sealed record PracticeLeadListItem(
  Guid Id, string Name, string Source, string? PrimaryContactName,
  string? PrimaryContactEmail, string Status, DateTimeOffset CreatedAt, string? LifecycleStage = null,
  Guid? OwnerUserId = null, string? OwnerName = null, string? OwnerState = null);

/// <summary>Owner display state; a disabled or missing owner is shown as recorded, never replaced.</summary>
public static class PracticeLeadOwnerStates
{
  public const string Unassigned = "UNASSIGNED";
  public const string Active = "ACTIVE";
  public const string Disabled = "DISABLED";
  public const string Missing = "MISSING";
}

/// <summary>Optional narrowing filters. Owner is "me", "unassigned" or an exact user ID; none of them grants access.</summary>
public sealed record PracticeLeadFilter(string? Status = null, string? Source = null, string? Owner = null);

/// <summary>Complete counts over every lead matching the same search and filters, read with the page.</summary>
public sealed record PracticeLeadSummary(int Matching, int AwaitingQualification, int Qualified);

public sealed record PracticeLeadPage(IReadOnlyList<PracticeLeadListItem> Items, int Total, int Page, int PageSize,
  PracticeLeadSummary? Summary = null);

/// <summary>Current firm-scoped commercial lead projection for the staff workbench.</summary>
public static class PracticeLeadQuery
{
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];
  private static readonly string[] LeadStatuses =
    [CrmStates.LeadNew, CrmStates.LeadQualified, CrmStates.LeadUnqualified, CrmStates.LeadLost];

  public static async Task<CommandResult<PracticeLeadPage>> PageAsync(IAuditSphereDbContext db, ActorContext actor,
    string? search = null, int page = 0, int pageSize = 25, PracticeLeadFilter? filter = null, CancellationToken ct = default)
  {
    filter ??= new();
    if (!TryParseOwnerFilter(filter.Owner, actor.UserId, out var ownerId, out var unassigned))
      return CommandResult<PracticeLeadPage>.Fail("request.invalid", "Invalid lead owner filter.");
    if (search?.Length > 100 || page is < 0 or > 10000 || pageSize is < 1 or > 100 ||
        (filter.Status is not null && !LeadStatuses.Contains(filter.Status)) ||
        (filter.Source is not null && !LeadChannels.All.Contains(filter.Source)))
      return CommandResult<PracticeLeadPage>.Fail("request.invalid", "Invalid search, filter or pagination.");
    var auth = await AuthorizeReadAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<PracticeLeadPage>.Fail(auth.ErrorCode!, "Leads unavailable.");
    var query = db.Leads.AsNoTracking().Where(l => l.FirmId == actor.FirmId);
    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim().ToLowerInvariant();
      query = query.Where(l => l.Name.ToLower().Contains(term));
    }
    if (filter.Status is not null) query = query.Where(l => l.Status == filter.Status);
    if (filter.Source is not null) query = query.Where(l => l.Source == filter.Source);
    if (unassigned) query = query.Where(l => l.OwnerUserId == null);
    else if (ownerId is { } owner) query = query.Where(l => l.OwnerUserId == owner);

    // Rows and their complete counts come from one observation, so a summary never describes a different cohort.
    var snapshot = db.Database.CurrentTransaction is null
      ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
    PracticeLeadSummary summary;
    List<PracticeLeadListItem> staged;
    try
    {
      var counts = await query.GroupBy(l => l.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
      summary = new(counts.Sum(x => x.Count),
        counts.Where(x => x.Status == CrmStates.LeadNew).Sum(x => x.Count),
        counts.Where(x => x.Status == CrmStates.LeadQualified).Sum(x => x.Count));
      var rows = await query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id).Skip(page * pageSize).Take(pageSize)
        .Select(l => new PracticeLeadListItem(l.Id, l.Name, l.Source, l.PrimaryContactName, l.PrimaryContactEmail, l.Status,
          l.CreatedAt, null, l.OwnerUserId, null, null)).ToListAsync(ct);
      // A lead is still in lead ingestion until a proposal exists for one of its opportunities.
      var leadIds = rows.Select(r => r.Id).ToList();
      var withProposal = await db.Opportunities.AsNoTracking()
        .Where(o => o.FirmId == actor.FirmId && leadIds.Contains(o.LeadId) &&
          db.Proposals.Any(p => p.FirmId == actor.FirmId && p.OpportunityId == o.Id))
        .Select(o => o.LeadId).Distinct().ToListAsync(ct);
      var ownerIds = rows.Where(r => r.OwnerUserId.HasValue).Select(r => r.OwnerUserId!.Value).Distinct().ToList();
      var owners = await db.Users.AsNoTracking().Where(u => u.FirmId == actor.FirmId && ownerIds.Contains(u.Id))
        .Select(u => new { u.Id, u.DisplayName, u.Disabled }).ToDictionaryAsync(u => u.Id, ct);
      staged = rows.Select(r => r with
      {
        LifecycleStage = withProposal.Contains(r.Id) ? "PROPOSAL_OR_LATER" : "LEAD_INGESTION",
        OwnerName = r.OwnerUserId is { } id && owners.TryGetValue(id, out var o) ? o.DisplayName : null,
        OwnerState = r.OwnerUserId is not { } ownerKey ? PracticeLeadOwnerStates.Unassigned
          : !owners.TryGetValue(ownerKey, out var known) ? PracticeLeadOwnerStates.Missing
          : known.Disabled ? PracticeLeadOwnerStates.Disabled : PracticeLeadOwnerStates.Active
      }).ToList();
      if (snapshot is not null) await snapshot.CommitAsync(ct);
    }
    finally
    {
      if (snapshot is not null) await snapshot.DisposeAsync();
    }

    auth = await AuthorizeReadAsync(db, actor, ct);
    return auth.Succeeded ? CommandResult<PracticeLeadPage>.Ok(new(staged, summary.Matching, page, pageSize, summary))
      : CommandResult<PracticeLeadPage>.Fail(auth.ErrorCode!, "Leads unavailable.");
  }

  /// <summary>Parses an owner filter ("me", "unassigned" or a nonempty user ID); anything else is refused, never widened.</summary>
  internal static bool TryParseOwnerFilter(string? owner, Guid actorUserId, out Guid? ownerId, out bool unassigned)
  {
    ownerId = null;
    unassigned = owner == "unassigned";
    if (owner is null || unassigned) return true;
    if (owner == "me") { ownerId = actorUserId; return true; }
    if (!Guid.TryParse(owner, out var parsed) || parsed == Guid.Empty) return false;
    ownerId = parsed;
    return true;
  }

  public static async Task<CommandResult<IReadOnlyList<PracticeLeadListItem>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizeReadAsync(db, actor, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<PracticeLeadListItem>>.Fail(auth.ErrorCode!, auth.Message!);

    var leads = await db.Leads.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => new PracticeLeadListItem(x.Id, x.Name, x.Source,
        x.PrimaryContactName, x.PrimaryContactEmail, x.Status, x.CreatedAt))
      .ToListAsync(ct);

    auth = await AuthorizeReadAsync(db, actor, ct);
    return auth.Succeeded
      ? CommandResult<IReadOnlyList<PracticeLeadListItem>>.Ok(leads)
      : CommandResult<IReadOnlyList<PracticeLeadListItem>>.Fail(auth.ErrorCode!, auth.Message!);
  }

  internal static Task<CommandResult> AuthorizeReadAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);
}
