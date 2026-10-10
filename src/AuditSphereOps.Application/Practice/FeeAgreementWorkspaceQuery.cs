using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record FeeMilestoneItem(Guid Id, string Kind, string Amount, string State, Guid? InvoiceId,
  string? InvoiceNumber, string? InvoiceStatus, string Allocated, string Outstanding, DateTimeOffset? PaidAt);
public sealed record FeeEngagementOption(Guid Id, string ServiceRoute, string PeriodStart, string PeriodEnd);

/// <summary>States of the 50% advance invoice from the Engagement Letter to an official invoice (STE 4.1.5).</summary>
public static class AdvanceInvoicePreparationStates
{
  public const string NotApplicable = "NOT_APPLICABLE";
  public const string AwaitingLetter = "AWAITING_ENGAGEMENT_LETTER";
  public const string PendingAutomationDisabled = "PENDING_AUTOMATION_DISABLED";
  public const string AwaitingAutomation = "AWAITING_AUTOMATION";
  public const string Queued = "QUEUED";
  public const string Blocked = "BLOCKED";
  public const string DraftCreated = "DRAFT_CREATED";
  public const string Cancelled = "CANCELLED";
  public const string OfficialInvoice = "OFFICIAL_INVOICE";
}

/// <summary>Explicit preparation state shown to Finance. Queued is never reported as drafted, and a draft is never reported as official.</summary>
public sealed record AdvanceInvoicePreparation(string State, string Message);

public sealed record FeeAgreementWorkspace(Guid ProposalId, Guid? AgreementId, Guid? ClientId, Guid? EngagementId,
  string Fee, string Currency, string AdvancePercent, bool CanCreate, bool CanFinance, bool ReleaseRecorded,
  IReadOnlyList<string> CreationBlockers, IReadOnlyList<FeeMilestoneItem> Milestones, IReadOnlyList<FeeEngagementOption> Engagements,
  AdvanceInvoicePreparation AdvancePreparation);

public static class FeeAgreementWorkspaceQuery
{
  private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);

  /// <param name="automaticDraftingEnabled">The API host's view of <c>AutomaticFeeInvoices:Enabled</c>; the worker enforces it.</param>
  public static async Task<CommandResult<FeeAgreementWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid proposalId, CancellationToken ct = default, bool automaticDraftingEnabled = false)
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
    var preparation = await AdvancePreparationAsync(db, actor.FirmId, v, automaticDraftingEnabled, ct);
    return CommandResult<FeeAgreementWorkspace>.Ok(new(proposalId, v?.Agreement.Id, p.ClientId, v?.Agreement.EngagementId,
      v is null ? p.Fee : Exact(v.Agreement.AgreedFee), v?.Agreement.Currency ?? p.Currency,
      Exact(v?.Agreement.AdvancePercent ?? FeeAgreementService.DefaultAdvancePercent), blockers.Count == 0 && v is null,
      finance, released, blockers,
      v?.Milestones.Select(m => new FeeMilestoneItem(m.Milestone.Id, m.Milestone.Kind, Exact(m.Milestone.Amount), m.Milestone.State,
        m.Milestone.InvoiceId, m.InvoiceNumber, m.InvoiceStatus, Exact(m.Allocated), Exact(m.Outstanding), m.Milestone.PaidAt)).ToArray() ?? [], engagements,
      preparation));
  }

  /// <summary>
  /// Derives the advance-invoice state from authoritative records only: the advance milestone, its invoice status, the current
  /// letter, and the automatic-drafting operations. When automatic drafting is off the state says so explicitly, so the
  /// preparation is never presented as complete.
  /// </summary>
  internal static async Task<AdvanceInvoicePreparation> AdvancePreparationAsync(IAuditSphereDbContext db, Guid firmId,
    FeeAgreementView? view, bool automaticDraftingEnabled, CancellationToken ct)
  {
    var advance = view?.Milestones.SingleOrDefault(m => m.Milestone.Kind == FeeMilestoneKinds.Advance);
    if (view is null || advance is null)
      return new(AdvanceInvoicePreparationStates.NotApplicable, "Create the fee agreement to establish the 50% advance milestone.");

    if (advance.Milestone.InvoiceId is not null)
    {
      return advance.InvoiceStatus switch
      {
        BillingStates.InvoiceDraft => new(AdvanceInvoicePreparationStates.DraftCreated,
          "The advance invoice draft exists. Finance review and posting are still required before it is official."),
        BillingStates.InvoiceCancelled => new(AdvanceInvoicePreparationStates.Cancelled,
          "The advance invoice was cancelled. Finance must prepare a replacement through the normal review."),
        var status => new(AdvanceInvoicePreparationStates.OfficialInvoice,
          $"The advance invoice is {status}. Delivery is recorded separately from posting."),
      };
    }

    if (!await AutomaticFeeInvoiceHandler.HasCurrentLetterAsync(db, view.Agreement, ct))
      return new(AdvanceInvoicePreparationStates.AwaitingLetter,
        "Generate the current approved Engagement Letter before the advance invoice can be prepared.");

    var statuses = await db.DurableOperations.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.TargetId == advance.Milestone.Id && x.OperationKind == AutomaticFeeInvoiceHandler.Kind)
      .Select(x => x.Status).ToListAsync(ct);
    if (statuses.Any(s => s is OperationState.AUTHORIZATION_BLOCKED or OperationState.PROVIDER_BLOCKED or OperationState.DEAD_LETTER))
      return new(AdvanceInvoicePreparationStates.Blocked,
        "Automatic drafting was blocked. Finance must resolve the blocker before the draft can be retried.");
    if (statuses.Any(s => s is not (OperationState.COMPLETED or OperationState.CANCELLED_WITH_DISPOSITION)))
      return new(AdvanceInvoicePreparationStates.Queued, "The advance invoice draft is queued. Queued is not yet a draft.");

    return automaticDraftingEnabled
      ? new(AdvanceInvoicePreparationStates.AwaitingAutomation, "Automatic drafting is enabled and will create one advance invoice draft.")
      : new(AdvanceInvoicePreparationStates.PendingAutomationDisabled,
        "Advance-invoice preparation is pending: automatic drafting is disabled, so Finance must prepare the draft manually.");
  }
}
