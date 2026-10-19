using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record CreateLeadRequest(
  string Name,
  string Source,
  string? PrimaryContactName = null,
  string? PrimaryContactEmail = null,
  Guid? OwnerUserId = null,
  string? ConsentRestrictions = null,
  Guid? RequestId = null);

public sealed record CreateOpportunityRequest(
  Guid LeadId,
  string ServiceRoute,
  string EntityScope,
  string PeriodStart,
  string PeriodEnd,
  decimal ExpectedFee,
  string Currency,
  decimal? Probability = null,
  Guid? OwnerUserId = null,
  string? NextAction = null,
  Guid? RequestId = null);

public sealed record ReviseProposalRequest(
  Guid OpportunityId,
  string ServiceProfileId,
  string Scope,
  string Exclusions,
  string Deliverables,
  string Dependencies,
  decimal Fee,
  string Currency,
  string PeriodStart,
  string PeriodEnd,
  long? ExpectedRevision = null,
  Guid? RequestId = null,
  string Timeline = "");

public sealed record ProposalResponseRequest(string Decision, string? Reason = null,
  string? OfferSha256 = null, string? RespondentName = null, string? RespondentEmail = null, string? EvidenceReference = null,
  string? Channel = null);

public sealed record ConvertToClientDraftRequest(
  Guid ProposalId,
  string LegalName,
  string? CommercialName = null,
  string? RegistrationNumber = null,
  string? Jurisdiction = null,
  string? RestrictedProfile = null);

public sealed record CreateClientContactRequest(
  Guid PracticeClientId,
  string FullName,
  string Email,
  string Role,
  string? ApprovedScope = null,
  DateTimeOffset? ValidFrom = null,
  DateTimeOffset? ValidTo = null,
  bool Primary = false,
  long? ExpectedSafetyGeneration = null);

/// <summary>
/// Command-only commercial workflow. A proposal can win commercial work, but conversion
/// creates only a prospect and a pending acceptance handoff; it never activates professional work.
/// </summary>
public static partial class PracticeCrmService
{
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];

  private static void RecordLeadActivity(IAuditSphereDbContext db, ActorContext actor, Guid leadId, string action,
    DateTimeOffset occurredAt, string? fromState = null, string? toState = null, string? sourceChannel = null,
    Guid? ownerUserId = null, Guid? opportunityId = null, string? serviceRoute = null, Guid? proposalId = null,
    long? proposalRevision = null, Guid? clientId = null)
  {
    db.LeadActivityEvents.Add(new LeadActivityEvent
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, LeadId = leadId, Action = action,
      OccurredAt = occurredAt, ActorUserId = actor.UserId, FromState = fromState, ToState = toState,
      SourceChannel = sourceChannel, OwnerUserId = ownerUserId, OpportunityId = opportunityId,
      ServiceRoute = serviceRoute, ProposalId = proposalId, ProposalRevision = proposalRevision, ClientId = clientId
    });
  }

  public static async Task<CommandResult<Guid>> CreateLeadAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateLeadRequest request, CancellationToken ct = default)
  {
    var validation = ValidateLead(request);
    if (validation is not null) return CommandResult<Guid>.Fail("crm.invalid", validation);
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var ownerError = await ValidateOwnerAsync(db, actor, request.OwnerUserId, ct);
    if (ownerError is not null) return CommandResult<Guid>.Fail(ownerError.ErrorCode!, ownerError.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var guard = await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var name = request.Name.Trim();
    var source = LeadChannel(request.Source)!;
    var contactName = TrimOrNull(request.PrimaryContactName);
    var contactEmail = TrimOrNull(request.PrimaryContactEmail);
    var consentRestrictions = TrimOrNull(request.ConsentRestrictions);

    // The caller's request identity is also the new lead identity. The firm lock
    // serializes concurrent retries, and matching the stored intent prevents a
    // reused key from disclosing or returning a different lead.
    if (request.RequestId is { } requestId)
    {
      var prior = await db.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, ct);
      if (prior is not null)
      {
        if (prior.FirmId != actor.FirmId || prior.Name != name || prior.Source != source
          || prior.PrimaryContactName != contactName || prior.PrimaryContactEmail != contactEmail
          || prior.OwnerUserId != request.OwnerUserId || prior.ConsentRestrictions != consentRestrictions)
          return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
            "This request identity is already bound to a different lead request.");
        return CommandResult<Guid>.Ok(prior.Id);
      }
    }

    var email = TrimOrNull(request.PrimaryContactEmail)?.ToUpperInvariant();
    var duplicate = await db.Leads.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.Name.ToUpper() == name.ToUpper() &&
      ((email != null && x.PrimaryContactEmail != null && x.PrimaryContactEmail.ToUpper() == email) ||
       (email == null && x.PrimaryContactEmail == null)), ct);
    if (duplicate)
      return CommandResult<Guid>.Fail("crm.duplicate", "A matching lead already exists; reviewed resolution is required.");

    var createdAt = DateTimeOffset.UtcNow;
    var lead = new Lead
    {
      Id = request.RequestId ?? Guid.CreateVersion7(), FirmId = actor.FirmId, Name = name,
      Source = source, PrimaryContactName = contactName,
      PrimaryContactEmail = contactEmail, OwnerUserId = request.OwnerUserId,
      ConsentRestrictions = consentRestrictions, CreatedAt = createdAt
    };
    db.Leads.Add(lead);
    RecordLeadActivity(db, actor, lead.Id, LeadActivityActions.Created, createdAt,
      toState: CrmStates.LeadNew, sourceChannel: source, ownerUserId: request.OwnerUserId);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(lead.Id);
  }

  public static async Task<CommandResult> QualifyLeadAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid leadId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var lead = await db.Leads.SingleOrDefaultAsync(x => x.Id == leadId && x.FirmId == actor.FirmId, ct);
    if (lead is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lead.Status == CrmStates.LeadQualified) return CommandResult.Ok();
    if (lead.Status != CrmStates.LeadNew)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a new lead can be qualified.");
    lead.Status = CrmStates.LeadQualified;
    var qualifiedAt = DateTimeOffset.UtcNow;
    RecordLeadActivity(db, actor, lead.Id, LeadActivityActions.Qualified, qualifiedAt,
      fromState: CrmStates.LeadNew, toState: CrmStates.LeadQualified);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreateOpportunityAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateOpportunityRequest request,
    CancellationToken ct = default)
  {
    var validation = ValidateOpportunity(request);
    if (validation is not null) return CommandResult<Guid>.Fail("crm.invalid", validation);
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var ownerError = await ValidateOwnerAsync(db, actor, request.OwnerUserId, ct);
    if (ownerError is not null) return CommandResult<Guid>.Fail(ownerError.ErrorCode!, ownerError.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");

    auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    ownerError = await ValidateOwnerAsync(db, actor, request.OwnerUserId, ct);
    if (ownerError is not null) return CommandResult<Guid>.Fail(ownerError.ErrorCode!, ownerError.Message!);

    if (request.RequestId == Guid.Empty)
      return CommandResult<Guid>.Fail("crm.invalid", "A nonempty operation identity is required.");
    if (request.RequestId.HasValue)
    {
      var prior = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequestId.Value, ct);
      if (prior is not null)
      {
        if (prior.FirmId != actor.FirmId || prior.LeadId != request.LeadId
          || prior.ServiceRoute != request.ServiceRoute.Trim() || prior.EntityScope != request.EntityScope.Trim()
          || prior.PeriodStart != request.PeriodStart.Trim() || prior.PeriodEnd != request.PeriodEnd.Trim()
          || prior.ExpectedFee != request.ExpectedFee || prior.Currency != request.Currency.Trim().ToUpperInvariant()
          || prior.Probability != request.Probability || prior.OwnerUserId != request.OwnerUserId
          || prior.NextAction != TrimOrNull(request.NextAction))
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Operation identity unavailable or terms changed.");
        return CommandResult<Guid>.Ok(prior.Id);
      }
    }

    var lead = await db.Leads.SingleOrDefaultAsync(x => x.Id == request.LeadId && x.FirmId == actor.FirmId, ct);
    if (lead is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lead.Status != CrmStates.LeadQualified)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The lead must be qualified before discovery.");

    var opportunity = new Opportunity
    {
      Id = request.RequestId ?? Guid.CreateVersion7(), FirmId = actor.FirmId, LeadId = lead.Id,
      ServiceRoute = request.ServiceRoute.Trim(), EntityScope = request.EntityScope.Trim(),
      PeriodStart = request.PeriodStart.Trim(), PeriodEnd = request.PeriodEnd.Trim(),
      ExpectedFee = request.ExpectedFee, Currency = request.Currency.Trim().ToUpperInvariant(),
      Probability = request.Probability, OwnerUserId = request.OwnerUserId,
      NextAction = TrimOrNull(request.NextAction), Stage = CrmStates.OpportunityDiscovery,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.Opportunities.Add(opportunity);
    RecordLeadActivity(db, actor, lead.Id, LeadActivityActions.OpportunityCreated, opportunity.CreatedAt,
      toState: opportunity.Stage, ownerUserId: opportunity.OwnerUserId, opportunityId: opportunity.Id,
      serviceRoute: opportunity.ServiceRoute);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(opportunity.Id);
  }

  public static async Task<CommandResult<Guid>> ReviseProposalAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviseProposalRequest request,
    CancellationToken ct = default)
  {
    var validation = ValidateProposal(request);
    if (validation is not null) return CommandResult<Guid>.Fail("crm.invalid", validation);
    if (request.RequestId == Guid.Empty ||
        (request.RequestId.HasValue && !request.ExpectedRevision.HasValue))
      return CommandResult<Guid>.Fail("crm.invalid", "A proposal request identity requires an expected revision.");
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");

    var createRequestHash = request.RequestId.HasValue
      ? ComputeProposalCreateRequestHash(actor, request)
      : null;
    if (request.RequestId is { } requestId)
    {
      var priorRequest = await db.Proposals.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == requestId, ct);
      if (priorRequest is not null)
      {
        var matches = priorRequest.FirmId == actor.FirmId
          && priorRequest.OpportunityId == request.OpportunityId
          && priorRequest.PreparedByUserId == actor.UserId
          && priorRequest.Revision == request.ExpectedRevision!.Value + 1
          && priorRequest.CreateRequestHash == createRequestHash;
        return matches
          ? CommandResult<Guid>.Ok(priorRequest.Id)
          : CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
            "This request identity is already bound to different proposal terms.");
      }
    }

    var opportunity = await db.Opportunities
      .SingleOrDefaultAsync(x => x.Id == request.OpportunityId && x.FirmId == actor.FirmId, ct);
    if (opportunity is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (opportunity.Stage is CrmStates.OpportunityLost or CrmStates.OpportunityWon)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A closed opportunity cannot receive a proposal revision.");

    var previous = await db.Proposals
      .Where(x => x.FirmId == actor.FirmId && x.OpportunityId == opportunity.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != (previous?.Revision ?? 0))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The proposal changed; reload the current revision.");
    if (previous?.Status == CrmStates.ProposalAccepted || opportunity.PracticeClientId.HasValue)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An accepted proposal has entered professional acceptance; revise through a new decision.");

    if (previous is not null) previous.Status = CrmStates.ProposalSuperseded;
    var proposal = new Proposal
    {
      Id = request.RequestId ?? Guid.CreateVersion7(), FirmId = actor.FirmId, OpportunityId = opportunity.Id,
      PracticeClientId = opportunity.PracticeClientId, Revision = (previous?.Revision ?? 0) + 1,
      Status = CrmStates.ProposalDraft, ServiceProfileId = request.ServiceProfileId.Trim(),
      Scope = request.Scope.Trim(), Exclusions = request.Exclusions.Trim(),
      Deliverables = request.Deliverables.Trim(), Dependencies = request.Dependencies.Trim(),
      Timeline = (request.Timeline ?? string.Empty).Trim(),
      Fee = request.Fee, Currency = request.Currency.Trim().ToUpperInvariant(),
      PeriodStart = request.PeriodStart.Trim(), PeriodEnd = request.PeriodEnd.Trim(),
      SupersedesId = previous?.Id, PreparedByUserId = actor.UserId,
      CreateRequestHash = createRequestHash, CreatedAt = DateTimeOffset.UtcNow
    };
    opportunity.Stage = CrmStates.OpportunityProposal;
    db.Proposals.Add(proposal);
    RecordLeadActivity(db, actor, opportunity.LeadId, LeadActivityActions.ProposalRevisionCreated,
      proposal.CreatedAt, fromState: previous?.Status, toState: proposal.Status,
      opportunityId: opportunity.Id, serviceRoute: opportunity.ServiceRoute,
      proposalId: proposal.Id, proposalRevision: proposal.Revision);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(proposal.Id);
  }

  public static async Task<CommandResult> ApproveProposalAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == proposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (proposal.Status == CrmStates.ProposalInternalReview) return CommandResult.Ok();
    if (proposal.Status != CrmStates.ProposalDraft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft proposal can enter internal review.");
    if (!proposal.PreparedByUserId.HasValue)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "A proposal without recorded authorship cannot enter internal review.");
    if (proposal.PreparedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Proposal preparers cannot approve their own revision.");
    // A calculated quotation is authoritative for the fee: it must be approved with all its approvals standing
    // (per the approval matrix) and equal to it.
    var quotation = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (quotation is not null && (quotation.Status != QuotationStates.Approved || quotation.Fee != proposal.Fee ||
      !await QuotationService.ApprovalsStandAsync(db, actor.FirmId, quotation.Id, ct)))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The calculated quotation must be approved and match the proposal fee before internal review.");
    proposal.Status = CrmStates.ProposalInternalReview;
    proposal.ApprovedByUserId = actor.UserId;
    proposal.ApprovedAt = DateTimeOffset.UtcNow;
    var opportunity = await db.Opportunities.AsNoTracking()
      .SingleAsync(x => x.FirmId == actor.FirmId && x.Id == proposal.OpportunityId, ct);
    RecordLeadActivity(db, actor, opportunity.LeadId, LeadActivityActions.ProposalReviewed, proposal.ApprovedAt.Value,
      fromState: CrmStates.ProposalDraft, toState: proposal.Status, opportunityId: opportunity.Id,
      serviceRoute: opportunity.ServiceRoute, proposalId: proposal.Id, proposalRevision: proposal.Revision);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> SendProposalAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == proposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (proposal.Status == CrmStates.ProposalSent)
    {
      var priorDispatch = await db.CommercialNotifications.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ProposalId == proposal.Id && x.Kind == CommercialNotificationKinds.Proposal, ct);
      if (proposal.SentAt is null || string.IsNullOrWhiteSpace(proposal.SentOfferSha256) || priorDispatch is null ||
          priorDispatch.DocumentId is not { } priorDocumentId || priorDispatch.QuotationVersionId is not { } priorQuotationId ||
          !string.Equals(proposal.SentOfferSha256, priorDispatch.OfferSha256, StringComparison.OrdinalIgnoreCase))
        return CommandResult.Fail(ErrorCodes.GenerationStale,
          "The proposal is marked sent but has no matching immutable offer dispatch; reconcile its evidence before retrying.");
      var priorArtifact = await db.CommercialDocuments.AsNoTracking()
        .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == priorDocumentId, ct);
      var priorQuotation = await db.QuotationVersions.AsNoTracking()
        .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == priorQuotationId, ct);
      if (priorArtifact is null || priorQuotation is null || priorQuotation.ProposalId != proposal.Id ||
          priorArtifact.ProposalId != proposal.Id || priorArtifact.QuotationVersionId != priorQuotationId ||
          priorArtifact.Kind != CommercialDocumentKinds.Quotation || priorArtifact.Bytes.Length == 0 ||
          !string.Equals(priorArtifact.Sha256Hex, priorDispatch.OfferSha256, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(priorArtifact.Sha256Hex, Hashing.Sha256Hex(priorArtifact.Bytes), StringComparison.OrdinalIgnoreCase))
        return CommandResult.Fail(ErrorCodes.GenerationStale,
          "The proposal dispatch no longer resolves to its exact rendered quotation; reconcile its evidence before retrying.");
      return CommandResult.Ok();
    }
    if (proposal.Status != CrmStates.ProposalInternalReview)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only an internally reviewed proposal can be sent.");
    var opportunity = await db.Opportunities.SingleAsync(x => x.Id == proposal.OpportunityId && x.FirmId == actor.FirmId, ct);
    var lead = await db.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == opportunity.LeadId && x.FirmId == actor.FirmId, ct);
    if (lead is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    // Existing clients use the effective designated COMMERCIAL contact. A prospect has no client-contact
    // aggregate yet, so its captured lead primary contact is the initial route; the address is snapshotted here
    // and can never be replaced by a later edit to the lead.
    var clientId = proposal.PracticeClientId ?? opportunity.PracticeClientId;
    Guid? recipientContactId = null;
    Guid? recipientRoutingId = null;
    string recipientName;
    string recipient;
    string recipientAuthorityBasis;
    if (clientId.HasValue)
    {
      var resolution = await ResolveCorrespondenceRecipientAsync(db, actor, clientId.Value,
        CorrespondencePurposes.Commercial, DateOnly.FromDateTime(DateTime.UtcNow), ct: ct);
      if (resolution.Status != RecipientResolutionStatus.Resolved || resolution.ClientContactId is not { } contactId ||
          resolution.RoutingId is not { } routingId || string.IsNullOrWhiteSpace(resolution.Email))
      {
        var code = resolution.Status == RecipientResolutionStatus.Denied ? ErrorCodes.ScopeDenied : ErrorCodes.GateBlocked;
        return CommandResult.Fail(code, resolution.Message ?? "A current designated COMMERCIAL contact is required before dispatch.");
      }
      recipientContactId = contactId;
      recipientRoutingId = routingId;
      recipientName = resolution.FullName ?? string.Empty;
      recipient = resolution.Email.Trim();
      recipientAuthorityBasis = ProposalRecipientAuthorityBases.ClientCommercialRouting;
    }
    else
    {
      recipient = (lead.PrimaryContactEmail ?? string.Empty).Trim();
      if (recipient.Length == 0)
        return CommandResult.Fail(ErrorCodes.GateBlocked,
          "Record the prospect's primary commercial contact before dispatch; a proposal cannot be sent without an exact recipient.");
      recipientName = lead.PrimaryContactName ?? lead.Name;
      recipientAuthorityBasis = ProposalRecipientAuthorityBases.ProspectPrimaryContact;
    }
    if (string.IsNullOrWhiteSpace(recipientName) || recipientName.Length > 200 || recipient.Length > 320 ||
        !System.Net.Mail.MailAddress.TryCreate(recipient, out var parsedRecipient))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The selected commercial recipient must have a valid name and email address.");
    recipient = parsedRecipient.Address;
    var quotation = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var document = await db.CommercialDocuments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id && x.Kind == CommercialDocumentKinds.Quotation)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    // The offer identity is the artifact bound to one approved quotation revision (STE 4.1.2): a reviewed
    // document from an older quotation cannot be dispatched under a newer revision — regenerate first.
    if (quotation is null || quotation.Status != QuotationStates.Approved)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The current quotation is missing or is not approved; approve a current revision before dispatch.");
    if (document is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "Generate the current rendered quotation from the approved quotation revision before dispatch. A non-rendered offer cannot be sent.");
    if (document.QuotationVersionId != quotation.Id)
      return CommandResult.Fail(ErrorCodes.StaleRevision,
        $"The reviewed quotation artifact belongs to quotation revision {document.QuotationVersionId}, not the current revision {quotation.Id}. Generate the current brief before dispatch.");
    if (document.Bytes.Length == 0 ||
        !string.Equals(document.Sha256Hex, Hashing.Sha256Hex(document.Bytes), StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "Generate the current rendered quotation from the approved quotation revision before dispatch. A non-rendered offer cannot be sent.");
    // A dispatched offer must stand on a currently approved, unexpired quotation (STE 4.1.2): a revoked required
    // approval or an elapsed validity window invalidates the offer before it can leave the firm.
    if (quotation.ValidUntil is { } validUntil && validUntil < DateTimeOffset.UtcNow)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The approved quotation's validity window has expired; reprice the proposal to issue a fresh offer.");
    if (!await QuotationService.ApprovalsStandAsync(db, actor.FirmId, quotation.Id, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "A required quotation approval has been revoked; record a fresh approval before dispatch.");
    var offerSha256 = document.Sha256Hex;
    var existing = await db.CommercialNotifications.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id, ct);
    if (existing is not null)
      return CommandResult.Fail(ErrorCodes.GenerationStale,
        "A prior proposal dispatch record already exists. Reconcile that delivery outcome or create a new proposal revision before dispatching again.");
    db.CommercialNotifications.Add(new CommercialNotification
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Kind = CommercialNotificationKinds.Proposal,
      ProposalId = proposal.Id, PracticeClientId = proposal.PracticeClientId ?? opportunity.PracticeClientId,
      QuotationVersionId = quotation.Id,
      DocumentId = document.Id, OfferSha256 = offerSha256,
      RecipientContactId = recipientContactId, RecipientRoutingId = recipientRoutingId,
      RecipientAuthorityBasis = recipientAuthorityBasis, Recipient = recipient,
      Subject = $"Proposal revision {proposal.Revision} — {lead.Name} ({opportunity.ServiceRoute})",
      Body = $"Dear {recipientName},\n\nPlease find our proposal revision {proposal.Revision} for {opportunity.ServiceRoute} " +
             $"covering {proposal.PeriodStart} to {proposal.PeriodEnd}, fee {proposal.Fee.ToString("N2", CultureInfo.InvariantCulture)} {proposal.Currency}.\n\n" +
             $"Offer identity: {offerSha256}\n\n{lead.Name}",
      CreatedAt = DateTimeOffset.UtcNow
    });
    proposal.Status = CrmStates.ProposalSent;
    proposal.SentAt = DateTimeOffset.UtcNow;
    proposal.SentOfferSha256 = offerSha256;
    opportunity.Stage = CrmStates.OpportunityNegotiation;
    RecordLeadActivity(db, actor, opportunity.LeadId, LeadActivityActions.ProposalMarkedSent, proposal.SentAt.Value,
      fromState: CrmStates.ProposalInternalReview, toState: proposal.Status, opportunityId: opportunity.Id,
      serviceRoute: opportunity.ServiceRoute, proposalId: proposal.Id, proposalRevision: proposal.Revision);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> RecordProposalResponseAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, ProposalResponseRequest request,
    CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (CrmStates.ProposalAccepted or CrmStates.ProposalDeclined))
      return CommandResult.Fail("crm.invalid", "Proposal response must be ACCEPTED or DECLINED.");
    if (decision == CrmStates.ProposalDeclined && string.IsNullOrWhiteSpace(request.Reason))
      return CommandResult.Fail("crm.invalid", "A declined proposal needs a reason.");
    // The response channel is part of the commercial evidence (STE 4.1.3): required on acceptance, optional on decline.
    var channel = ResponseChannel(request.Channel);
    if (!string.IsNullOrWhiteSpace(request.Channel) && channel is null)
      return CommandResult.Fail("crm.invalid", "Response channel must be EMAIL, SIGNED_LETTER, IN_PERSON or PHONE.");
    if (decision == CrmStates.ProposalAccepted && channel is null)
      return CommandResult.Fail("crm.invalid", "Acceptance must record the channel the response arrived by.");
    var citedOffer = (request.OfferSha256 ?? string.Empty).Trim();
    if (citedOffer.Length != 64 || !citedOffer.All(Uri.IsHexDigit))
      return CommandResult.Fail("crm.invalid", "A proposal response must cite the exact dispatched offer SHA-256 identity.");
    var respondentName = TrimOrNull(request.RespondentName);
    var respondentEmail = TrimOrNull(request.RespondentEmail);
    if (respondentName is null || respondentEmail is null)
      return CommandResult.Fail("crm.invalid", "Record the responding person's name and email for every proposal response.");
    var evidenceReference = TrimOrNull(request.EvidenceReference);
    if (decision == CrmStates.ProposalAccepted && evidenceReference is null)
      return CommandResult.Fail("crm.invalid", "Acceptance must cite the received written or contemporaneous response evidence.");
    var reason = TrimOrNull(request.Reason);
    if (respondentName.Length > 200 || respondentEmail.Length > 320 || evidenceReference?.Length > 1000 ||
        reason?.Length > 1000 || !System.Net.Mail.MailAddress.TryCreate(respondentEmail, out var parsedRespondentEmail))
      return CommandResult.Fail("crm.invalid", "Response fields exceed their supported limits or the email address is invalid.");
    respondentEmail = parsedRespondentEmail.Address;
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == proposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (proposal.Status == decision)
    {
      // A replay is idempotent only when it reproduces the full immutable response. Legacy mutable fields are
      // not treated as a durable response receipt because they do not identify who recorded the response.
      var prior = await db.ProposalResponseRecords.AsNoTracking()
        .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id, ct);
      if (prior is null)
        return CommandResult.Fail(ErrorCodes.ProtectedState,
          "This proposal has a legacy response without an immutable response record; do not record a second response over it.");
      var sameResponse = prior.Decision == decision &&
        string.Equals(prior.OfferSha256, citedOffer, StringComparison.OrdinalIgnoreCase) &&
        prior.RespondentName == respondentName &&
        string.Equals(prior.RespondentEmail, respondentEmail, StringComparison.OrdinalIgnoreCase) &&
        prior.EvidenceReference == evidenceReference && prior.Channel == channel && prior.Reason == reason;
      return sameResponse ? CommandResult.Ok()
        : CommandResult.Fail("crm.conflict", "A different immutable response is already recorded for this proposal.");
    }
    if (proposal.Status != CrmStates.ProposalSent)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a sent proposal can receive a response.");
    var dispatch = await db.CommercialNotifications.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id &&
        x.Kind == CommercialNotificationKinds.Proposal, ct);
    if (dispatch is null || dispatch.OfferSha256 is null || proposal.SentOfferSha256 is null ||
        !string.Equals(dispatch.OfferSha256, proposal.SentOfferSha256, StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The proposal has no consistent exact-offer dispatch record.");
    if (!string.Equals(proposal.SentOfferSha256, citedOffer, StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail("crm.stale-offer",
        "The cited offer does not match the dispatched proposal revision; a superseded or revised offer cannot receive this response.");
    if (!string.Equals(respondentEmail, dispatch.Recipient, StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail("crm.unauthorized-respondent",
        "The responding email must match the exact recipient frozen into the proposal dispatch; a later lead-contact edit does not grant acceptance authority.");
    if (dispatch.RecipientAuthorityBasis is not { } authorityBasis || !ProposalRecipientAuthorityBases.All.Contains(authorityBasis) ||
        (authorityBasis == ProposalRecipientAuthorityBases.ClientCommercialRouting &&
          (!dispatch.RecipientContactId.HasValue || !dispatch.RecipientRoutingId.HasValue)) ||
        (authorityBasis != ProposalRecipientAuthorityBases.ClientCommercialRouting &&
          (dispatch.RecipientContactId.HasValue || dispatch.RecipientRoutingId.HasValue)))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The dispatched recipient authority snapshot is incomplete.");

    // Only acceptance binds legal/commercial terms. It must still cite the current approved quotation and the
    // exact stored rendered bytes which were dispatched; a later quotation cannot silently replace them.
    if (decision == CrmStates.ProposalAccepted)
    {
      if (dispatch.DocumentId is not { } documentId || dispatch.QuotationVersionId is not { } quotationId)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The dispatch is not bound to a rendered quotation revision.");
      // The offer must still stand when the response arrives: an elapsed validity window or a revoked required
      // approval invalidates acceptance without touching the historical documents (STE 4.1.2).
      var boundQuotation = await db.QuotationVersions.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id)
        .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
      if (boundQuotation is null || boundQuotation.Id != quotationId || boundQuotation.Status != QuotationStates.Approved)
        return CommandResult.Fail("crm.revoked-approval",
          "The dispatched quotation is no longer the current approved offer; record a new approved revision before acceptance.");
      if (boundQuotation.ValidUntil is { } validUntil && validUntil < DateTimeOffset.UtcNow)
        return CommandResult.Fail("crm.expired-offer", "The dispatched quotation expired before the response was recorded.");
      if (!await QuotationService.ApprovalsStandAsync(db, actor.FirmId, quotationId, ct))
        return CommandResult.Fail("crm.revoked-approval", "A required quotation approval was revoked before the response was recorded.");
      var document = await db.CommercialDocuments.AsNoTracking()
        .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == documentId && x.QuotationVersionId == quotationId &&
          x.Kind == CommercialDocumentKinds.Quotation, ct);
      if (document is null || document.Bytes.Length == 0 ||
          !string.Equals(document.Sha256Hex, dispatch.OfferSha256, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(document.Sha256Hex, Hashing.Sha256Hex(document.Bytes), StringComparison.OrdinalIgnoreCase))
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The rendered quotation bytes no longer match the dispatched offer identity.");
    }

    // PostgreSQL timestamptz stores microseconds. Keep the mutable proposal projection and its
    // immutable receipt on that same precision even when the host clock exposes 100 ns ticks.
    var instant = DateTimeOffset.UtcNow;
    var recordedAt = new DateTimeOffset(instant.Ticks - instant.Ticks % 10, TimeSpan.Zero);
    db.ProposalResponseRecords.Add(new ProposalResponseRecord
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProposalId = proposal.Id,
      CommercialNotificationId = dispatch.Id, Decision = decision, OfferSha256 = citedOffer.ToLowerInvariant(),
      DispatchRecipient = dispatch.Recipient, RecipientContactId = dispatch.RecipientContactId,
      RecipientRoutingId = dispatch.RecipientRoutingId, AuthorityBasis = authorityBasis,
      RespondentName = respondentName, RespondentEmail = respondentEmail, EvidenceReference = evidenceReference,
      Channel = channel, Reason = reason, RecordedByUserId = actor.UserId, RecordedAt = recordedAt
    });
    proposal.Status = decision;
    proposal.ResponseAt = recordedAt;
    proposal.ResponseReason = reason;
    proposal.ResponseOfferSha256 = citedOffer.ToLowerInvariant();
    proposal.RespondentName = respondentName;
    proposal.RespondentEmail = respondentEmail;
    proposal.ResponseEvidenceReference = evidenceReference;
    proposal.ResponseChannel = channel;
    var opportunity = await db.Opportunities.SingleAsync(x => x.Id == proposal.OpportunityId && x.FirmId == actor.FirmId, ct);
    opportunity.Stage = decision == CrmStates.ProposalAccepted
      ? CrmStates.OpportunityWon : CrmStates.OpportunityLost;
    RecordLeadActivity(db, actor, opportunity.LeadId,
      decision == CrmStates.ProposalAccepted ? LeadActivityActions.ProposalAccepted : LeadActivityActions.ProposalDeclined,
      recordedAt, fromState: CrmStates.ProposalSent, toState: decision, opportunityId: opportunity.Id,
      serviceRoute: opportunity.ServiceRoute, proposalId: proposal.Id, proposalRevision: proposal.Revision);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> ConvertToClientDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, ConvertToClientDraftRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.LegalName))
      return CommandResult<Guid>.Fail("crm.invalid", "Legal name is required.");
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    // Serialize canonical-client conversion per firm. This makes repeated conversion idempotent
    // without creating a generic repository or relying only on a uniqueness exception.
    var guard = await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == request.ProposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (proposal.PracticeClientId.HasValue) return CommandResult<Guid>.Ok(proposal.PracticeClientId.Value);
    if (proposal.Status != CrmStates.ProposalAccepted)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only an accepted commercial proposal can open client acceptance.");
    if (!await ProposalResponseEvidence.IsCurrentAcceptedAsync(db, actor.FirmId, proposal, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Client conversion requires an immutable acceptance record tied to the exact current rendered quotation.");

    var opportunity = await db.Opportunities.SingleOrDefaultAsync(
      x => x.Id == proposal.OpportunityId && x.FirmId == actor.FirmId, ct);
    if (opportunity is null || opportunity.Stage != CrmStates.OpportunityWon)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The commercial opportunity is not won.");

    var legalName = request.LegalName.Trim();
    var registrationNumber = TrimOrNull(request.RegistrationNumber);
    var jurisdiction = TrimOrNull(request.Jurisdiction);
    var normalizedName = legalName.ToUpperInvariant();
    var normalizedRegistration = registrationNumber?.ToUpperInvariant();
    var normalizedJurisdiction = jurisdiction?.ToUpperInvariant();
    var candidates = await db.PracticeClients
      .Where(x => x.FirmId == actor.FirmId &&
        (x.LegalName.ToUpper() == normalizedName ||
          (normalizedRegistration != null && normalizedJurisdiction != null &&
           x.RegistrationNumber != null && x.Jurisdiction != null &&
           x.RegistrationNumber.ToUpper() == normalizedRegistration &&
           x.Jurisdiction.ToUpper() == normalizedJurisdiction)))
      .Take(2).ToListAsync(ct);
    if (candidates.Count > 1)
      return CommandResult<Guid>.Fail("crm.duplicate", "More than one canonical client matches this identity; reviewed resolution is required.");
    var existingClient = candidates.SingleOrDefault();
    if (existingClient is not null && !string.Equals(existingClient.LegalName.Trim(), legalName, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail("crm.duplicate", "A matching registration candidate has a different legal name; reviewed resolution is required.");
    if (existingClient is not null &&
        ((registrationNumber is not null && existingClient.RegistrationNumber is not null &&
          !string.Equals(existingClient.RegistrationNumber, registrationNumber, StringComparison.OrdinalIgnoreCase)) ||
         (jurisdiction is not null && existingClient.Jurisdiction is not null &&
          !string.Equals(existingClient.Jurisdiction, jurisdiction, StringComparison.OrdinalIgnoreCase))))
      return CommandResult<Guid>.Fail("crm.duplicate", "The canonical client identity conflicts with the supplied registration details.");
    PracticeClient client;
    long clientGeneration = 1;
    var createdClient = existingClient is null;
    if (createdClient)
    {
      var lead = await db.Leads.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == opportunity.LeadId, ct);
      if (!System.Net.Mail.MailAddress.TryCreate(lead.PrimaryContactEmail, out _))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Add a valid primary client contact email before client conversion and portal onboarding.");
      client = new PracticeClient
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, LegalName = legalName,
        CommercialName = TrimOrNull(request.CommercialName),
        RegistrationNumber = registrationNumber,
        Jurisdiction = jurisdiction,
        RestrictedProfile = TrimOrNull(request.RestrictedProfile),
        Status = CrmStates.ClientProspect, CreatedAt = DateTimeOffset.UtcNow
      };
      db.PracticeClients.Add(client);
      db.ClientSafetyStates.Add(new ClientSafetyState
      {
        Id = client.Id, FirmId = actor.FirmId, InputGeneration = 1
      });
    }
    else
    {
      client = existingClient!;
      var clientGuard = await db.ClientSafetyStates.FromSqlInterpolated(
        $"SELECT * FROM client_safety_states WHERE id = {client.Id} AND firm_id = {actor.FirmId} FOR UPDATE")
        .SingleOrDefaultAsync(ct);
      if (clientGuard is null)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
      clientGuard.InputGeneration++;
      clientGeneration = clientGuard.InputGeneration;
    }

    proposal.PracticeClientId = client.Id;
    opportunity.PracticeClientId = client.Id;
    var pending = await db.AcceptanceDecisions.AnyAsync(x =>
      x.FirmId == actor.FirmId && x.PracticeClientId == client.Id &&
      x.EngagementId == null && x.ServiceRoute == opportunity.ServiceRoute && x.Decision == "Pending", ct);
    if (!pending)
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = client.Id,
        Decision = "Pending", ServiceRoute = opportunity.ServiceRoute, Generation = clientGeneration
      });
    RecordLeadActivity(db, actor, opportunity.LeadId, LeadActivityActions.ClientConversionLinked,
      DateTimeOffset.UtcNow, fromState: proposal.Status, toState: client.Status, opportunityId: opportunity.Id,
      serviceRoute: opportunity.ServiceRoute, proposalId: proposal.Id, proposalRevision: proposal.Revision,
      clientId: client.Id);
    await RecordPortalIntentAsync(db, actor.FirmId, proposal.Id, client.Id, opportunity.LeadId, ct);
    await db.SaveChangesAsync(ct);
    auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(client.Id);
  }
  /// <summary>
  /// Conversion is the explicit portal trigger: the lead's primary contact becomes the client's primary contact and a
  /// portal intent is recorded once per client. It grants nothing; activation later marks it ready to invite.
  /// </summary>
  private static async Task RecordPortalIntentAsync(
    IAuditSphereDbContext db, Guid firmId, Guid proposalId, Guid clientId, Guid leadId, CancellationToken ct)
  {
    var lead = await db.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == leadId && x.FirmId == firmId, ct);
    if (lead?.PrimaryContactEmail is not { Length: > 3 } email) return;
    if (await db.ClientPortalIntents.AnyAsync(x => x.FirmId == firmId && x.PracticeClientId == clientId, ct)) return;
    var contact = await db.ClientContacts.FirstOrDefaultAsync(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.Primary, ct);
    if (contact is null)
    {
      contact = new ClientContact
      {
        Id = Guid.CreateVersion7(), FirmId = firmId, PracticeClientId = clientId,
        FullName = lead.PrimaryContactName ?? email, Email = email, Role = "Primary contact", Primary = true
      };
      db.ClientContacts.Add(contact);
    }
    var now = DateTimeOffset.UtcNow;
    db.ClientPortalIntents.Add(new ClientPortalIntent
    {
      Id = Guid.CreateVersion7(), FirmId = firmId, PracticeClientId = clientId, ClientContactId = contact.Id,
      RecipientEmail = contact.Email, SourceProposalId = proposalId, CreatedAt = now, UpdatedAt = now
    });
  }

  public static async Task<CommandResult<Guid>> CreateClientContactAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateClientContactRequest request,
    CancellationToken ct = default)
  {
    var validation = ValidateContact(request);
    if (validation is not null) return CommandResult<Guid>.Fail("crm.invalid", validation);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: request.PracticeClientId,
        RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: request.PracticeClientId,
        RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await db.Users.FromSqlInterpolated(
      $"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    var clientGuard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {request.PracticeClientId} AND firm_id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (clientGuard is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    if (request.ExpectedSafetyGeneration.HasValue && clientGuard.InputGeneration != request.ExpectedSafetyGeneration.Value)
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Client context changed. Review current contacts before submitting again.");
    var clientExists = await db.PracticeClients.AsNoTracking()
      .AnyAsync(x => x.Id == request.PracticeClientId && x.FirmId == actor.FirmId, ct);
    if (!clientExists) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (clientGuard.InputGeneration == long.MaxValue)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client revision capacity is exhausted.");
    await db.ClientSafetyStates.Where(x => x.Id == request.PracticeClientId && x.FirmId == actor.FirmId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1), ct);

    if (request.Primary)
    {
      await db.ClientContacts
        .Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == request.PracticeClientId && x.Primary)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Primary, false), ct);
    }

    var contact = new ClientContact
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      PracticeClientId = request.PracticeClientId, FullName = request.FullName.Trim(),
      Email = request.Email.Trim(), Role = request.Role.Trim(),
      ApprovedScope = TrimOrNull(request.ApprovedScope), ValidFrom = request.ValidFrom,
      ValidTo = request.ValidTo, Primary = request.Primary
    };
    db.ClientContacts.Add(contact);
    await db.SaveChangesAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: request.PracticeClientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(contact.Id);
  }

  private static async Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);

  private static async Task<CommandResult?> ValidateOwnerAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid? ownerUserId, CancellationToken ct)
  {
    if (!ownerUserId.HasValue) return null;
    var valid = await db.Users.AsNoTracking().AnyAsync(
      x => x.Id == ownerUserId.Value && x.FirmId == actor.FirmId &&
        !x.Disabled && x.UserKind == "Staff", ct);
    return valid ? null : CommandResult.Fail(ErrorCodes.ScopeDenied,
      "The assigned owner is not an active staff user in this firm.");
  }

  internal static Task<FirmSafetyState?> LockFirmAsync(
    IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static string? ValidateLead(CreateLeadRequest request)
  {
    if (request.RequestId == Guid.Empty)
      return "A nonempty operation identity is required.";
    var error = Required(request.Name, "Lead name") ?? Required(request.Source, "Lead source");
    if (error is not null) return error;
    if (LeadChannel(request.Source) is null)
      return "Lead source must be a supported channel: Phone, WhatsApp, Email, Web Form, Referral or In-person.";
    return string.IsNullOrWhiteSpace(request.PrimaryContactEmail) ? null : EmailError(request.PrimaryContactEmail);
  }

  /// <summary>The canonical response channel, matched case-insensitively against the approved set; null when absent or unsupported.</summary>
  internal static string? ResponseChannel(string? channel) =>
    string.IsNullOrWhiteSpace(channel) ? null
      : ProposalResponseChannels.All.FirstOrDefault(c => string.Equals(c, channel.Trim(), StringComparison.OrdinalIgnoreCase));

  /// <summary>The canonical channel for a lead source, matched case-insensitively against the owner-approved set; null when unsupported.</summary>
  internal static string? LeadChannel(string? source) =>
    string.IsNullOrWhiteSpace(source) ? null
      : LeadChannels.All.FirstOrDefault(channel => string.Equals(channel, source.Trim(), StringComparison.OrdinalIgnoreCase));

  private static string? ValidateOpportunity(CreateOpportunityRequest request)
  {
    var error = Required(request.ServiceRoute, "Service route")
      ?? Required(request.EntityScope, "Entity scope")
      ?? Required(request.PeriodStart, "Period start")
      ?? Required(request.PeriodEnd, "Period end")
      ?? CurrencyError(request.Currency)
      ?? ExactMoneyError(request.ExpectedFee, "Expected fee");
    if (error is not null) return error;
    error = PeriodError(request.PeriodStart, request.PeriodEnd);
    if (error is not null) return error;
    if (request.Probability is < 0 or > 100 ||
        (request.Probability.HasValue && MoneyPolicy.Normalize(request.Probability.Value) != request.Probability.Value))
      return "Probability must be between 0 and 100 with at most 6 decimals.";
    return null;
  }

  private static string? ValidateProposal(ReviseProposalRequest request)
  {
    var error = Required(request.ServiceProfileId, "Service profile")
      ?? Required(request.Scope, "Proposal scope")
      ?? Required(request.Deliverables, "Deliverables")
      ?? Required(request.PeriodStart, "Period start")
      ?? Required(request.PeriodEnd, "Period end")
      ?? CurrencyError(request.Currency)
      ?? ExactMoneyError(request.Fee, "Proposal fee")
      ?? ((request.Timeline ?? string.Empty).Trim().Length > MaxProposalTimelineLength ? $"Timeline must be at most {MaxProposalTimelineLength} characters." : null);
    return error ?? PeriodError(request.PeriodStart, request.PeriodEnd);
  }

  public const int MaxProposalTimelineLength = 4000;

  private sealed record ProposalCreateRequestFingerprint(
    Guid FirmId,
    Guid ActorUserId,
    Guid RequestId,
    Guid OpportunityId,
    long ExpectedRevision,
    string ServiceProfileId,
    string Scope,
    string Exclusions,
    string Deliverables,
    string Dependencies,
    string Fee,
    string Currency,
    string PeriodStart,
    string PeriodEnd,
    // Written only when a timeline is offered, so the identity of every proposal created without one is unchanged.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Timeline = null);

  private static string ComputeProposalCreateRequestHash(ActorContext actor, ReviseProposalRequest request)
  {
    var canonical = JsonSerializer.Serialize(new ProposalCreateRequestFingerprint(
      actor.FirmId, actor.UserId, request.RequestId!.Value, request.OpportunityId,
      request.ExpectedRevision!.Value, request.ServiceProfileId.Trim(), request.Scope.Trim(),
      request.Exclusions.Trim(), request.Deliverables.Trim(), request.Dependencies.Trim(),
      request.Fee.ToString("G29", CultureInfo.InvariantCulture),
      request.Currency.Trim().ToUpperInvariant(), request.PeriodStart.Trim(),
      request.PeriodEnd.Trim(),
      string.IsNullOrWhiteSpace(request.Timeline) ? null : request.Timeline.Trim()));
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
  }

  private static string? ValidateContact(CreateClientContactRequest request)
  {
    var error = Required(request.FullName, "Contact name")
      ?? Required(request.Email, "Contact email")
      ?? Required(request.Role, "Contact role")
      ?? EmailError(request.Email);
    if (error is not null) return error;
    return request.ValidTo.HasValue && request.ValidFrom.HasValue && request.ValidTo < request.ValidFrom
      ? "Contact validity end must not be before its start."
      : null;
  }

  private static string? PeriodError(string start, string end)
  {
    var parsedStart = DateOnly.TryParseExact(start.Trim(), "yyyy-MM-dd",
      CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate);
    var parsedEnd = DateOnly.TryParseExact(end.Trim(), "yyyy-MM-dd",
      CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate);
    if (!parsedStart || !parsedEnd) return "Period dates must use valid ISO dates (yyyy-MM-dd).";
    return startDate <= endDate ? null : "Period start must not be after period end.";
  }

  private static string? Required(string value, string name) =>
    string.IsNullOrWhiteSpace(value) ? $"{name} is required." : null;

  private static string? CurrencyError(string currency) =>
    currency.Trim().Length == 3 && currency.All(char.IsLetter) ? null : "Currency must be a three-letter code.";

  private static string? EmailError(string email)
  {
    var value = email.Trim();
    var at = value.IndexOf('@');
    return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 &&
      !value.Any(char.IsWhiteSpace) ? null : "Contact email is invalid.";
  }

  private static string? ExactMoneyError(decimal amount, string name) =>
    amount >= 0 && MoneyPolicy.Normalize(amount) == amount ? null : $"{name} must be non-negative and have at most 6 decimals.";

  private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
