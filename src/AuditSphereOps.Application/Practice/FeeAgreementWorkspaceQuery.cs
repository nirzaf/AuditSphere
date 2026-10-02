using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record FeeMilestoneItem(Guid Id, string Kind, string Amount, string State, Guid? InvoiceId,
  string? InvoiceNumber, string? InvoiceStatus, string Allocated, string Outstanding, DateTimeOffset? PaidAt);
public sealed record FeeEngagementOption(Guid Id, string ServiceRoute, string PeriodStart, string PeriodEnd);
public sealed record FeeAgreementWorkspace(Guid ProposalId, Guid? AgreementId, Guid? ClientId, Guid? EngagementId,
  string Fee, string Currency, string AdvancePercent, bool CanCreate, bool CanFinance, bool ReleaseRecorded,
  IReadOnlyList<string> CreationBlockers, IReadOnlyList<FeeMilestoneItem> Milestones, IReadOnlyList<FeeEngagementOption> Engagements);

public static class FeeAgreementWorkspaceQuery
{
  private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);
  public static async Task<CommandResult<FeeAgreementWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid proposalId, CancellationToken ct = default)
  {
    var proposal = await CommercialWorkspaceQuery.ProposalAsync(db, actor, proposalId, ct);
    if (!proposal.Succeeded) return CommandResult<FeeAgreementWorkspace>.Fail(ErrorCodes.ScopeDenied, "Fee agreement unavailable.");
    var view = await FeeAgreementService.GetForProposalAsync(db, actor, proposalId, ct);
    if (!view.Succeeded) return CommandResult<FeeAgreementWorkspace>.Fail(ErrorCodes.ScopeDenied, "Fee agreement unavailable.");
    var p = proposal.Value!; var v = view.Value;
    var blockers = new List<string>();
    if (p.Status != "ACCEPTED" || p.ClientId is null) blockers.Add("Record commercial acceptance and convert the proposal to a client before creating an agreement.");
    var quote = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var fee = decimal.Parse(p.Fee, CultureInfo.InvariantCulture);
    if (quote is null || quote.Status != "APPROVED" || quote.Fee != fee) blockers.Add("Approve the current quotation and match the accepted proposal fee.");
    var advance = MoneyPolicy.Normalize(fee * FeeAgreementService.DefaultAdvancePercent / 100m, QuotationCalculator.CurrencyScale);
    if (advance <= 0 || fee - advance <= 0) blockers.Add("The accepted fee must support positive advance and balance milestones.");
    var finance = p.ClientId.HasValue && (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, p.ClientId, RequiredRoles: ["FinanceManager", "FinanceReviewer"], InternalOnly: true), ct)).Succeeded;
    var engagements = p.ClientId.HasValue && v?.Agreement.EngagementId is null
      ? await db.Engagements.AsNoTracking().Where(e => e.FirmId == actor.FirmId && e.PracticeClientId == p.ClientId)
        .OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id).Take(100)
        .Select(e => new FeeEngagementOption(e.Id, e.ServiceRoute, e.PeriodStart, e.PeriodEnd)).ToListAsync(ct) : [];
    var released = v?.Agreement.EngagementId is { } engagementId && await db.Releases.AsNoTracking().AnyAsync(r =>
      r.FirmId == actor.FirmId && r.ClientId == v.Agreement.PracticeClientId && r.EngagementId == engagementId, ct);
    if (v is not null && (v.Milestones.Count != 2 || !new[] { "ADVANCE", "BALANCE" }.All(kind => v.Milestones.Count(m => m.Milestone.Kind == kind) == 1)))
      return CommandResult<FeeAgreementWorkspace>.Fail(ErrorCodes.GateBlocked, "Fee milestones require reconciliation.");
    if (!(await CommercialWorkspaceQuery.ProposalAsync(db, actor, proposalId, ct)).Succeeded)
      return CommandResult<FeeAgreementWorkspace>.Fail(ErrorCodes.ScopeDenied, "Fee agreement unavailable.");
    return CommandResult<FeeAgreementWorkspace>.Ok(new(proposalId, v?.Agreement.Id, p.ClientId, v?.Agreement.EngagementId,
      v is null ? p.Fee : Exact(v.Agreement.AgreedFee), v?.Agreement.Currency ?? p.Currency,
      Exact(v?.Agreement.AdvancePercent ?? FeeAgreementService.DefaultAdvancePercent), blockers.Count == 0 && v is null,
      finance, released, blockers,
      v?.Milestones.Select(m => new FeeMilestoneItem(m.Milestone.Id, m.Milestone.Kind, Exact(m.Milestone.Amount), m.Milestone.State,
        m.Milestone.InvoiceId, m.InvoiceNumber, m.InvoiceStatus, Exact(m.Allocated), Exact(m.Outstanding), m.Milestone.PaidAt)).ToArray() ?? [], engagements));
  }
}
