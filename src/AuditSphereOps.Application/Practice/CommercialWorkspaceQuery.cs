using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record ProposalHistory(Guid Id, string Revision, string Status, string Fee, string Currency, DateTimeOffset CreatedAt);
public sealed record ProposalWorkspace(Guid Id, Guid OpportunityId, string LeadName, string ServiceRoute, string EntityScope,
  string Stage, string Revision, string Status, string ServiceProfile, string Scope, string Exclusions,
  string Deliverables, string Dependencies, string Fee, string Currency, string PeriodStart, string PeriodEnd,
  string? ResponseReason, Guid? ClientId, bool CanApprove, IReadOnlyList<ProposalHistory> Versions);

public sealed record OpportunityItem(Guid Id, string ServiceRoute, string EntityScope, string Stage,
  string ExpectedFee, string Currency, string PeriodStart, string PeriodEnd, Guid? ProposalId, string Revision);
public sealed record LeadWorkspace(Guid Id, string Name, string Status, IReadOnlyList<OpportunityItem> Opportunities);

public static class CommercialWorkspaceQuery
{
  public static async Task<CommandResult<LeadWorkspace>> LeadAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId,
      RequiredRoles: ["Administrator", "Partner", "Manager", "RelationshipManager"], InternalOnly: true, RequireFirmWide: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<LeadWorkspace>.Fail(ErrorCodes.ScopeDenied, "Lead unavailable.");
    var lead = await db.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (lead is null) return CommandResult<LeadWorkspace>.Fail(ErrorCodes.ScopeDenied, "Lead unavailable.");
    var opportunities = await db.Opportunities.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.LeadId == id)
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
    var ids = opportunities.Select(x => x.Id).ToArray();
    var proposals = await db.Proposals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && ids.Contains(x.OpportunityId))
      .GroupBy(x => x.OpportunityId).Select(g => g.OrderByDescending(x => x.Revision).First()).ToListAsync(ct);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<LeadWorkspace>.Fail(ErrorCodes.ScopeDenied, "Lead unavailable.");
    var rows = opportunities.Select(o =>
    {
      var latest = proposals.SingleOrDefault(p => p.OpportunityId == o.Id);
      return new OpportunityItem(o.Id, o.ServiceRoute, o.EntityScope, o.Stage,
        o.ExpectedFee.ToString(CultureInfo.InvariantCulture), o.Currency, o.PeriodStart, o.PeriodEnd,
        latest?.Id, (latest?.Revision ?? 0).ToString(CultureInfo.InvariantCulture));
    }).ToArray();
    return CommandResult<LeadWorkspace>.Ok(new(lead.Id, lead.Name, lead.Status, rows));
  }

  public static async Task<CommandResult<ProposalWorkspace>> ProposalAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId,
      RequiredRoles: ["Administrator", "Partner", "Manager", "RelationshipManager"], InternalOnly: true, RequireFirmWide: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");
    var p = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == id, ct);
    if (p is null) return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");
    var opportunity = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == p.OpportunityId, ct);
    if (opportunity is null) return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");
    var leadName = await db.Leads.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == opportunity.LeadId).Select(x => x.Name).SingleOrDefaultAsync(ct);
    if (leadName is null) return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");
    var versions = await db.Proposals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.OpportunityId == p.OpportunityId)
      .OrderByDescending(x => x.Revision).Take(100).ToListAsync(ct);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");
    return CommandResult<ProposalWorkspace>.Ok(new(p.Id, p.OpportunityId, leadName, opportunity.ServiceRoute, opportunity.EntityScope,
      opportunity.Stage, p.Revision.ToString(CultureInfo.InvariantCulture), p.Status, p.ServiceProfileId, p.Scope, p.Exclusions,
      p.Deliverables, p.Dependencies, p.Fee.ToString(CultureInfo.InvariantCulture), p.Currency, p.PeriodStart, p.PeriodEnd,
      p.ResponseReason, opportunity.PracticeClientId, p.PreparedByUserId.HasValue && p.PreparedByUserId != actor.UserId && p.Status == "DRAFT",
      versions.Select(v => new ProposalHistory(v.Id, v.Revision.ToString(CultureInfo.InvariantCulture), v.Status,
        v.Fee.ToString(CultureInfo.InvariantCulture), v.Currency, v.CreatedAt)).ToArray()));
  }
}
