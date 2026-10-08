using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>
/// One lead row. <see cref="LifecycleStage"/> is set on the paged view only: LEAD_INGESTION while no proposal exists for the
/// lead (STE 11-stage lifecycle, stage 1), and PROPOSAL_OR_LATER once a proposal exists, whose own stages are shown on it.
/// </summary>
public sealed record PracticeLeadListItem(
  Guid Id, string Name, string Source, string? PrimaryContactName,
  string? PrimaryContactEmail, string Status, DateTimeOffset CreatedAt, string? LifecycleStage = null);
public sealed record PracticeLeadPage(IReadOnlyList<PracticeLeadListItem> Items, int Total, int Page, int PageSize);

/// <summary>Current firm-scoped commercial lead projection for the staff workbench.</summary>
public static class PracticeLeadQuery
{
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];

  public static async Task<CommandResult<PracticeLeadPage>> PageAsync(IAuditSphereDbContext db, ActorContext actor,
    string? search = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
  {
    if (search?.Length > 100 || page is < 0 or > 10000 || pageSize is < 1 or > 100)
      return CommandResult<PracticeLeadPage>.Fail("request.invalid", "Invalid search or pagination.");
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded) return CommandResult<PracticeLeadPage>.Fail(auth.ErrorCode!, "Leads unavailable.");
    var query = db.Leads.AsNoTracking().Where(l => l.FirmId == actor.FirmId);
    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim().ToLowerInvariant();
      query = query.Where(l => l.Name.ToLower().Contains(term));
    }
    var total = await query.CountAsync(ct);
    var rows = await query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id).Skip(page * pageSize).Take(pageSize)
      .Select(l => new PracticeLeadListItem(l.Id, l.Name, l.Source, l.PrimaryContactName, l.PrimaryContactEmail, l.Status, l.CreatedAt)).ToListAsync(ct);
    // A lead is still in lead ingestion until a proposal exists for one of its opportunities.
    var leadIds = rows.Select(r => r.Id).ToList();
    var withProposal = await db.Opportunities.AsNoTracking()
      .Where(o => o.FirmId == actor.FirmId && leadIds.Contains(o.LeadId) &&
        db.Proposals.Any(p => p.FirmId == actor.FirmId && p.OpportunityId == o.Id))
      .Select(o => o.LeadId).Distinct().ToListAsync(ct);
    var staged = rows.Select(r => r with { LifecycleStage = withProposal.Contains(r.Id) ? "PROPOSAL_OR_LATER" : "LEAD_INGESTION" }).ToList();

    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    return auth.Succeeded ? CommandResult<PracticeLeadPage>.Ok(new(staged, total, page, pageSize))
      : CommandResult<PracticeLeadPage>.Fail(auth.ErrorCode!, "Leads unavailable.");
  }

  public static async Task<CommandResult<IReadOnlyList<PracticeLeadListItem>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles,
      InternalOnly: true, RequireFirmWide: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<PracticeLeadListItem>>.Fail(auth.ErrorCode!, auth.Message!);

    var leads = await db.Leads.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => new PracticeLeadListItem(x.Id, x.Name, x.Source,
        x.PrimaryContactName, x.PrimaryContactEmail, x.Status, x.CreatedAt))
      .ToListAsync(ct);

    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    return auth.Succeeded
      ? CommandResult<IReadOnlyList<PracticeLeadListItem>>.Ok(leads)
      : CommandResult<IReadOnlyList<PracticeLeadListItem>>.Fail(auth.ErrorCode!, auth.Message!);
  }
}
