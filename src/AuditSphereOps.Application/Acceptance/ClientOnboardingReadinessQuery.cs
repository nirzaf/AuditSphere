using System.Data;
using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AuditSphereOps.Application.Acceptance;

/// <summary>Status vocabulary for the independently evaluated onboarding cards (WF-03). Labels only: none satisfies a gate.</summary>
public static class OnboardingReadinessStatuses
{
  public const string Current = "CURRENT";
  public const string Missing = "MISSING";
  public const string Stale = "STALE";
  public const string Conditional = "CONDITIONAL";
  public const string Declined = "DECLINED";
  public const string Deferred = "DEFERRED";
  public const string Unavailable = "UNAVAILABLE";
  public const string AwaitingClient = "AWAITING_CLIENT";
  public const string NotDispatched = "NOT_DISPATCHED";
  public const string Superseded = "SUPERSEDED";
  public const string QuotationNotApproved = "QUOTATION_NOT_APPROVED";
  public const string Eligible = "ELIGIBLE";
  public const string Issued = "ISSUED";
  public const string NotEligible = "NOT_ELIGIBLE";
  public const string NoAgreement = "NO_AGREEMENT";
  public const string NotLinked = "NOT_LINKED";
  public const string NotInvoiced = "NOT_INVOICED";
  public const string InvoicedUnpaid = "INVOICED_UNPAID";
  public const string PartiallyAllocated = "PARTIALLY_ALLOCATED";
  public const string Paid = "PAID";
  public const string Active = "ACTIVE";
  public const string ReadyForReview = "READY_FOR_REVIEW";
  public const string Blocked = "BLOCKED";
}

public sealed record OnboardingReadinessBlocker(string Code, string Label);

public sealed record OnboardingReadinessAction(string Key, string Label, string Href);

/// <summary>
/// The approval vocabulary the STE v2.2 onboarding interface shows (section 4.1.3): Pending, Awaiting Evidence, Approved, Rejected or
/// Stale. Only Approved clears a key. Each internal status maps to exactly one of these, and anything unrecognised is Pending.
/// </summary>
public static class OnboardingDisplayStatus
{
  public const string Pending = "PENDING";
  public const string AwaitingEvidence = "AWAITING_EVIDENCE";
  public const string Approved = "APPROVED";
  public const string Rejected = "REJECTED";
  public const string Stale = "STALE";

  public static string ForClient(string status) => status switch
  {
    OnboardingReadinessStatuses.Current => Approved,
    OnboardingReadinessStatuses.AwaitingClient => AwaitingEvidence,
    OnboardingReadinessStatuses.Declined => Rejected,
    OnboardingReadinessStatuses.Superseded or OnboardingReadinessStatuses.Stale => Stale,
    _ => Pending,
  };

  public static string ForPartner(string status) => status switch
  {
    OnboardingReadinessStatuses.Current => Approved,
    OnboardingReadinessStatuses.Declined => Rejected,
    OnboardingReadinessStatuses.Stale => Stale,
    OnboardingReadinessStatuses.Unavailable => AwaitingEvidence,
    _ => Pending,
  };
}

/// <summary>
/// Client commercial consent for the exact dispatched offer. Amounts are exact strings (ADR-0014), never floats. Owner is the
/// authorized contact who recorded the response; the evidence link opens the offer the response answered.
/// </summary>
public sealed record ClientCommercialCard(string Status, string Label, string? Fee, string? Currency, long? QuotationRevision,
  DateTimeOffset? RespondedAtUtc, string? OfferSha256Prefix, string DisplayStatus = OnboardingDisplayStatus.Pending,
  string? Owner = null, string? EvidenceHref = null);

/// <summary>
/// The Partner's unconditional risk decision for this client and service, with the decider's display name when recorded. The
/// evidence link opens the acceptance checklist that the decision was made against.
/// </summary>
public sealed record PartnerRiskCard(string Status, string Label, string ServiceRoute, DateTimeOffset? DecidedAtUtc, string? DecidedByName,
  long? Generation, string DisplayStatus = OnboardingDisplayStatus.Pending, string? EvidenceHref = null);

public sealed record EngagementLetterCard(string Status, string Label, Guid? DocumentId);

/// <summary>
/// Advance payment from the milestone state, which is the activation predicate. Amounts are exact strings. A user-entered flag,
/// an invoice total or a receipt total alone never reads as Paid.
/// </summary>
public sealed record AdvancePaymentCard(string Status, string Label, string? Amount, string? Allocated, string? Outstanding,
  string? InvoiceStatus, string? Currency, string? AdvancePercent, string PreparationState);

public sealed record ActivationCard(string Status, string Label);

/// <summary>
/// <paramref name="CanGenerateLetter"/> and <paramref name="CanActivate"/> are server-computed for the requesting actor, so the
/// page never offers an action the protected command would refuse. They never change a card's status.
/// </summary>
public sealed record OnboardingReadinessReport(
  Guid EngagementId,
  Guid ClientId,
  string ServiceRoute,
  ClientCommercialCard ClientApproval,
  PartnerRiskCard PartnerRisk,
  EngagementLetterCard EngagementLetter,
  AdvancePaymentCard AdvancePayment,
  ActivationCard Activation,
  bool CanGenerateLetter,
  bool CanActivate,
  IReadOnlyList<OnboardingReadinessBlocker> Blockers,
  IReadOnlyList<OnboardingReadinessAction> NextActions,
  DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// Dual-Key onboarding readiness (WF-03): two independently evaluated approval keys, the engagement letter and the 50% advance
/// payment, each from its own persisted evidence. It reuses the protected evidence predicates and read models, writes nothing,
/// and never treats a green card as authority: the protected letter and activation commands re-check on execution.
/// </summary>
public static class ClientOnboardingReadinessQuery
{
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];

  public static async Task<CommandResult<OnboardingReadinessReport>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default,
    bool automaticDraftingEnabled = false)
  {
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<OnboardingReadinessReport>.Fail(ErrorCodes.GateBlocked,
        "Onboarding readiness requires an independent read snapshot.");

    CommandResult<OnboardingReadinessReport> result;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      // Capture the report's as-of time immediately before its first database read.
      var evaluatedAtUtc = DateTimeOffset.UtcNow;
      result = await ReadWithinSnapshotAsync(db, actor, engagementId, evaluatedAtUtc, ct, automaticDraftingEnabled);
      if (!result.Succeeded) return result;
      await snapshot.CommitAsync(ct);
    }

    var report = result.Value!;
    var currentCommercial = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: CommercialRoles,
        InternalOnly: true, RequireFirmWide: true), ct);
    if (!currentCommercial.Succeeded)
      return CommandResult<OnboardingReadinessReport>.Fail(currentCommercial.ErrorCode!, currentCommercial.Message!);

    // These flags authorize different actions from the broad commercial read. Do not return a snapshot-era true flag
    // if its exact role or signature authority has since been revoked; do not recompute any business card outside the snapshot.
    if (report.CanGenerateLetter && !await CanGenerateLetterAsync(db, actor, ct))
      return CommandResult<OnboardingReadinessReport>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");
    if (report.CanActivate)
    {
      var currentActivation = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, report.ClientId, engagementId, RequiredRoles: ["Partner"], InternalOnly: true), ct);
      if (!currentActivation.Succeeded)
        return CommandResult<OnboardingReadinessReport>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");
    }

    return result;
  }

  private static async Task<CommandResult<OnboardingReadinessReport>> ReadWithinSnapshotAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, DateTimeOffset evaluatedAtUtc,
    CancellationToken ct, bool automaticDraftingEnabled)
  {
    if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.RepeatableRead)
      return CommandResult<OnboardingReadinessReport>.Fail(ErrorCodes.GateBlocked,
        "Onboarding projection requires its caller's RepeatableRead snapshot.");

    // Firm-wide: the client's Partner risk decision and payment detail are firm commercial evidence. An engagement-only grant
    // must not read a client-level decision or its decider, even for a client that has no proposal yet.
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return CommandResult<OnboardingReadinessReport>.Fail(auth.ErrorCode!, auth.Message!);

    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return CommandResult<OnboardingReadinessReport>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");
    var clientId = engagement.PracticeClientId;
    var blockers = new List<OnboardingReadinessBlocker>();
    var actions = new List<OnboardingReadinessAction>();

    // The proposal this engagement is bound to: the linked fee agreement first, otherwise the newest exact-period proposal.
    var proposal = await DualKeyEvidence.ResolveEngagementProposalAsync(db, actor.FirmId, engagementId,
      clientId, engagement.ServiceRoute, engagement.PeriodStart, engagement.PeriodEnd, ct);
    var quotation = proposal is null ? null : await db.QuotationVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var proposalHref = proposal is null ? null : $"/app/practice/proposals/{proposal.Id}";
    var assessmentHref = $"/app/clients/{clientId}/assessment";

    // 1. Client commercial consent for the exact dispatched offer.
    var clientBase = await ClientCommercialAsync(db, actor.FirmId, proposal, quotation, blockers, ct);
    var clientCard = clientBase with
    {
      DisplayStatus = OnboardingDisplayStatus.ForClient(clientBase.Status), Owner = proposal?.RespondentName, EvidenceHref = proposalHref,
    };
    var clientCurrent = clientCard.Status == OnboardingReadinessStatuses.Current;
    if (!clientCurrent && proposalHref is not null)
      actions.Add(new("OPEN_PROPOSAL", "Review the dispatched offer and client response", proposalHref));

    // 2. The Partner's unconditional risk decision, evaluated on the engagement's service route (as activation is).
    var latestRisk = await EngagementRiskEvidence.LatestAsync(db, actor.FirmId, engagement, ct);
    var inputGeneration = await DualKeyEvidence.InputGenerationAsync(db, actor.FirmId, clientId, ct);
    var riskBase = await PartnerRiskAsync(db, actor.FirmId, latestRisk, inputGeneration, engagement.ServiceRoute, blockers, ct);
    var riskCard = riskBase with { DisplayStatus = OnboardingDisplayStatus.ForPartner(riskBase.Status), EvidenceHref = assessmentHref };
    var partnerCurrent = riskCard.Status == OnboardingReadinessStatuses.Current;
    if (!partnerCurrent) actions.Add(new("OPEN_ACCEPTANCE", "Review the acceptance checklist and risk clearance", assessmentHref));

    // The protected letter guard reads the opportunity's route for the risk decision. A different route cannot be matched to this engagement.
    var opportunity = proposal is null ? null : await db.Opportunities.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == proposal.OpportunityId, ct);
    var routeMatches = proposal is null || opportunity is not null && opportunity.ServiceRoute == engagement.ServiceRoute;
    if (!routeMatches)
      blockers.Add(new("ROUTE_MISMATCH", "The proposal's service route differs from this engagement, so its letter and clearance cannot be matched to it."));
    var letterTemplateReady = opportunity is not null && EngagementLetterTemplates.Resolve(opportunity.ServiceRoute) is not null;

    // 3. Engagement letter: issued only for the current clearance and the newest quotation. A letter for an earlier one is stale.
    var currentRisk = partnerCurrent ? latestRisk : null;
    var letterCard = await LetterAsync(db, actor.FirmId, proposal, quotation, currentRisk,
      bothKeysCurrent: clientCurrent && partnerCurrent && routeMatches, letterTemplateReady, blockers, ct);
    var canGenerateLetter = letterCard.Status == OnboardingReadinessStatuses.Eligible && await CanGenerateLetterAsync(db, actor, ct);
    if (canGenerateLetter && proposalHref is not null) actions.Add(new("OPEN_LETTER", "Generate the Engagement Letter", proposalHref));

    // 4. Advance payment: the 50% advance milestone, linked to this engagement, from the protected fee-agreement read model.
    FeeAgreementWorkspace? fee = null;
    if (proposal is not null)
    {
      var feeView = await FeeAgreementWorkspaceQuery.ReadWithinSnapshotAsync(db, actor, proposal.Id, evaluatedAtUtc,
        automaticDraftingEnabled, ct);
      if (!feeView.Succeeded) return CommandResult<OnboardingReadinessReport>.Fail(feeView.ErrorCode!, feeView.Message!);
      fee = feeView.Value;
    }
    var advance = fee?.Milestones.SingleOrDefault(m => m.Kind == FeeMilestoneKinds.Advance);
    AdvancePaymentCard advanceCard;
    var advancePaid = false;
    if (fee?.AgreementId is null || advance is null)
    {
      advanceCard = new(OnboardingReadinessStatuses.NoAgreement, "No fee agreement yet; the 50% advance milestone is not established.",
        null, null, null, null, fee?.Currency, fee?.AdvancePercent, fee?.AdvancePreparation.State ?? AdvanceInvoicePreparationStates.NotApplicable);
      blockers.Add(new("FEE_AGREEMENT_REQUIRED", "Create the fee agreement and its 50% advance milestone."));
    }
    else if (fee.EngagementId != engagementId)
    {
      advanceCard = new(OnboardingReadinessStatuses.NotLinked,
        "The fee agreement is not linked to this engagement; activation refuses an unlinked agreement.",
        advance.Amount, advance.Allocated, advance.Outstanding, advance.InvoiceStatus, fee.Currency, fee.AdvancePercent,
        fee.AdvancePreparation.State);
      blockers.Add(new("FEE_AGREEMENT_NOT_LINKED", "Link the fee agreement to this engagement."));
      if (proposalHref is not null) actions.Add(new("OPEN_FEE_AGREEMENT", "Link the fee agreement to this engagement", proposalHref));
    }
    else
    {
      advancePaid = advance.State == FeeMilestoneStates.Paid;
      var status = advancePaid ? OnboardingReadinessStatuses.Paid
        : decimal.Parse(advance.Allocated, CultureInfo.InvariantCulture) > 0m ? OnboardingReadinessStatuses.PartiallyAllocated
        : advance.InvoiceId is not null ? OnboardingReadinessStatuses.InvoicedUnpaid
        : OnboardingReadinessStatuses.NotInvoiced;
      var label = status switch
      {
        OnboardingReadinessStatuses.Paid => "Advance is paid and allocated; the milestone is marked paid.",
        OnboardingReadinessStatuses.PartiallyAllocated => $"Advance partly allocated; {advance.Outstanding} outstanding.",
        OnboardingReadinessStatuses.InvoicedUnpaid => "Advance invoice exists; no receipt is allocated yet.",
        _ => "Advance invoice not yet prepared.",
      };
      advanceCard = new(status, label, advance.Amount, advance.Allocated, advance.Outstanding, advance.InvoiceStatus, fee.Currency,
        fee.AdvancePercent, fee.AdvancePreparation.State);
      if (!advancePaid)
      {
        blockers.Add(new("ADVANCE_NOT_ALLOCATED", $"Advance payment is not fully allocated: {advance.Outstanding} outstanding."));
        actions.Add(new("OPEN_FINANCE", "Prepare and allocate the advance invoice in finance", "/app/finance"));
      }
    }

    // Open engagement holds stop activation whatever the commercial keys say.
    var holds = await db.EngagementHolds.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && !x.Released)
      .Select(x => x.Reason).ToListAsync(ct);
    foreach (var hold in holds) blockers.Add(new("ENGAGEMENT_HOLD_OPEN", $"Open hold: {hold}"));

    // 5. Activation. Activation requires a Partner on this engagement and re-checks every guard; the card is a readiness fact only.
    var activated = await db.EngagementActivations.AsNoTracking()
      .AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    var canActivate = !activated && (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, RequiredRoles: ["Partner"], InternalOnly: true), ct)).Succeeded;
    var letterIssued = letterCard.Status == OnboardingReadinessStatuses.Issued;
    var readyForReview = clientCurrent && partnerCurrent && routeMatches && letterIssued && advancePaid && holds.Count == 0;
    var activation = activated
      ? new ActivationCard(OnboardingReadinessStatuses.Active, "Engagement is activated.")
      : readyForReview
        ? new ActivationCard(OnboardingReadinessStatuses.ReadyForReview, canActivate
          ? "Every card is satisfied. Open the reviewed activation; it re-checks every guard before it activates."
          : "Every card is satisfied. A Partner on this engagement must open the reviewed activation.")
        : new ActivationCard(OnboardingReadinessStatuses.Blocked,
          blockers.Count > 0 ? blockers[0].Label : "Activation is not yet available.");
    if (readyForReview && canActivate)
      actions.Add(new("REVIEW_ACTIVATION", "Review and activate the engagement", $"/app/engagements/{engagementId}/activation"));

    return CommandResult<OnboardingReadinessReport>.Ok(new OnboardingReadinessReport(
      engagementId, clientId, engagement.ServiceRoute, clientCard, riskCard, letterCard, advanceCard, activation,
      canGenerateLetter, canActivate, blockers, actions, evaluatedAtUtc));
  }

  /// <summary>The letter command's own actor rule: a firm-wide Partner who has a registered, unrevoked signature.</summary>
  private static async Task<bool> CanGenerateLetterAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    var partner = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Partner"], InternalOnly: true, RequireFirmWide: true), ct);
    return partner.Succeeded && await db.SignatureSpecimens.AsNoTracking()
      .AnyAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null, ct);
  }

  private static async Task<ClientCommercialCard> ClientCommercialAsync(IAuditSphereDbContext db, Guid firmId, Proposal? proposal,
    QuotationVersion? quotation, List<OnboardingReadinessBlocker> blockers, CancellationToken ct)
  {
    if (proposal is null || proposal.SentAt is null)
    {
      blockers.Add(new("CLIENT_ACCEPTANCE_REQUIRED", "Dispatch the approved offer to the client."));
      return new(OnboardingReadinessStatuses.NotDispatched, "The offer has not been dispatched to the client.", null, null, null, null, null);
    }
    var prefix = proposal.SentOfferSha256 is { Length: >= 12 } sent ? sent[..12] : null;
    var fee = proposal.Fee.ToString(CultureInfo.InvariantCulture);
    var revision = quotation?.Revision;
    switch (proposal.Status)
    {
      case CrmStates.ProposalDeclined:
        blockers.Add(new("CLIENT_OFFER_DECLINED", "The client declined the dispatched offer; revise and resend or close the opportunity."));
        return new(OnboardingReadinessStatuses.Declined, "Client declined the dispatched offer.", fee, proposal.Currency, revision, proposal.ResponseAt, prefix);
      case CrmStates.ProposalSuperseded:
        blockers.Add(new("CLIENT_ACCEPTANCE_STALE", "The dispatched offer was superseded; the client must accept the current offer."));
        return new(OnboardingReadinessStatuses.Superseded, "The dispatched offer was superseded.", fee, proposal.Currency, revision, null, prefix);
      case CrmStates.ProposalSent:
        blockers.Add(new("CLIENT_ACCEPTANCE_REQUIRED", "Client commercial acceptance of the dispatched offer is required."));
        return new(OnboardingReadinessStatuses.AwaitingClient, "Awaiting the client's response to the dispatched offer.", fee, proposal.Currency, revision, null, prefix);
      case CrmStates.ProposalAccepted:
        if (await DualKeyEvidence.CommercialAcceptanceStandsAsync(db, firmId, proposal, ct))
          return new(OnboardingReadinessStatuses.Current, "The client accepted the exact current offer.", fee, proposal.Currency, revision, proposal.ResponseAt, prefix);
        if (quotation is not { Status: QuotationStates.Approved } || quotation.Fee != proposal.Fee)
        {
          blockers.Add(new("QUOTATION_NOT_APPROVED", "Approve the current quotation and match the accepted fee before the offer can stand."));
          return new(OnboardingReadinessStatuses.QuotationNotApproved, "The current quotation is not approved for this fee.", fee, proposal.Currency, revision, proposal.ResponseAt, prefix);
        }
        blockers.Add(new("CLIENT_ACCEPTANCE_STALE", "Client acceptance does not cite the current dispatched offer; re-record acceptance against the exact current offer."));
        return new(OnboardingReadinessStatuses.Stale, "The client's acceptance cites a different offer or quotation revision.", fee, proposal.Currency, revision, proposal.ResponseAt, prefix);
      default:
        blockers.Add(new("CLIENT_ACCEPTANCE_REQUIRED", "Dispatch the approved offer to the client."));
        return new(OnboardingReadinessStatuses.NotDispatched, "The offer has not been dispatched to the client.", fee, proposal.Currency, revision, null, prefix);
    }
  }

  private static async Task<PartnerRiskCard> PartnerRiskAsync(IAuditSphereDbContext db, Guid firmId, AuditSphereOps.Domain.Acceptance.AcceptanceDecision? latest,
    long? inputGeneration, string serviceRoute, List<OnboardingReadinessBlocker> blockers, CancellationToken ct)
  {
    var missingLabel = "No unconditional Partner risk clearance is recorded for this service.";
    if (latest is null)
    {
      blockers.Add(new("PARTNER_ACCEPTANCE_REQUIRED", missingLabel));
      return new(OnboardingReadinessStatuses.Missing, missingLabel, serviceRoute, null, null, null);
    }
    var decidedBy = latest.DecidedByUserId is { } userId
      ? await db.Users.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == userId).Select(x => x.DisplayName).FirstOrDefaultAsync(ct)
      : null;
    switch (latest.Decision)
    {
      case "Accepted" when latest.DecidedByUserId is null || latest.DecidedAt is null:
        blockers.Add(new("PARTNER_ACCEPTANCE_REQUIRED", missingLabel));
        return new(OnboardingReadinessStatuses.Missing, missingLabel, serviceRoute, latest.DecidedAt, null, latest.Generation);
      case "Accepted" when inputGeneration is null:
        blockers.Add(new("SAFETY_STATE_UNAVAILABLE", "The client safety state is unavailable, so the risk clearance cannot be confirmed."));
        return new(OnboardingReadinessStatuses.Unavailable, "Client safety state unavailable.", serviceRoute, latest.DecidedAt, decidedBy, latest.Generation);
      case "Accepted" when latest.Generation != inputGeneration:
        blockers.Add(new("PARTNER_ACCEPTANCE_STALE", "The Partner risk clearance is stale: client inputs changed since it was given."));
        return new(OnboardingReadinessStatuses.Stale, "Partner clearance predates the current client inputs.", serviceRoute, latest.DecidedAt, decidedBy, latest.Generation);
      case "Accepted":
        return new(OnboardingReadinessStatuses.Current, "Unconditional Partner risk clearance is current.", serviceRoute, latest.DecidedAt, decidedBy, latest.Generation);
      case "AcceptedWithConditions":
        blockers.Add(new("PARTNER_ACCEPTANCE_CONDITIONAL", "The latest Partner decision is conditional; only an unconditional clearance opens the letter."));
        return new(OnboardingReadinessStatuses.Conditional, "Partner decision is conditional.", serviceRoute, latest.DecidedAt, decidedBy, latest.Generation);
      case "Declined":
        blockers.Add(new("PARTNER_ACCEPTANCE_DECLINED", "The Partner declined the risk clearance for this service."));
        return new(OnboardingReadinessStatuses.Declined, "Partner declined the risk clearance.", serviceRoute, latest.DecidedAt, decidedBy, latest.Generation);
      default:
        blockers.Add(new("PARTNER_ACCEPTANCE_REQUIRED", "The latest Partner decision is deferred; an unconditional clearance is required."));
        return new(OnboardingReadinessStatuses.Deferred, "Partner decision is deferred.", serviceRoute, latest.DecidedAt, decidedBy, latest.Generation);
    }
  }

  /// <summary>
  /// The letter's evidence, in the same order the protected command refuses: both keys (with the route match), the letter template,
  /// the commercial profile and the firm seal. An issued letter is current only for the present clearance and newest quotation.
  /// </summary>
  private static async Task<EngagementLetterCard> LetterAsync(IAuditSphereDbContext db, Guid firmId, Proposal? proposal,
    QuotationVersion? quotation, AuditSphereOps.Domain.Acceptance.AcceptanceDecision? currentRisk, bool bothKeysCurrent, bool letterTemplateReady,
    List<OnboardingReadinessBlocker> blockers, CancellationToken ct)
  {
    var letter = proposal is null ? null : await db.CommercialDocuments.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ProposalId == proposal.Id && x.Kind == CommercialDocumentKinds.EngagementLetter)
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
    if (letter is not null)
    {
      var current = bothKeysCurrent && currentRisk is not null && letter.AcceptanceDecisionId == currentRisk.Id &&
        quotation is not null && letter.QuotationVersionId == quotation.Id;
      if (current) return new(OnboardingReadinessStatuses.Issued, "Engagement letter issued for the current clearance and offer.", letter.Id);
      blockers.Add(new("LETTER_STALE", "The issued engagement letter belongs to an earlier clearance or quotation revision; a new approved revision is needed."));
      return new(OnboardingReadinessStatuses.Stale, "The issued letter is stale for the current evidence.", letter.Id);
    }

    blockers.Add(new("LETTER_NOT_ISSUED", "Engagement letter not issued for the current accepted offer."));
    if (!bothKeysCurrent)
      return new(OnboardingReadinessStatuses.NotEligible, "Both approval keys must be current, on the engagement's service route, before a letter can be issued.", null);
    if (!letterTemplateReady)
      return new(OnboardingReadinessStatuses.NotEligible, "No engagement letter template is configured for this service route.", null);
    var profileReady = await db.FirmCommercialProfiles.AsNoTracking().AnyAsync(x => x.FirmId == firmId, ct);
    if (!profileReady)
      return new(OnboardingReadinessStatuses.NotEligible, "Configure the firm commercial profile before the letter can be generated.", null);
    var sealReady = await db.FirmSealSpecimens.AsNoTracking().AnyAsync(x => x.FirmId == firmId, ct);
    if (!sealReady)
      return new(OnboardingReadinessStatuses.NotEligible, "Register the official firm seal before the letter can be generated.", null);
    return new(OnboardingReadinessStatuses.Eligible,
      "Both keys, the template, the profile and the seal are ready. Only a firm-wide Partner with a registered signature can generate the letter.", null);
  }
}
