using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Completion;
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
  string? ConsentRestrictions = null);

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
  string? NextAction = null);

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
  long? ExpectedRevision = null);

public sealed record ProposalResponseRequest(string Decision, string? Reason = null);

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
  bool Primary = false);

/// <summary>
/// Command-only commercial workflow. A proposal can win commercial work, but conversion
/// creates only a prospect and a pending acceptance handoff; it never activates professional work.
/// </summary>
public static class PracticeCrmService
{
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];

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
    var email = TrimOrNull(request.PrimaryContactEmail)?.ToUpperInvariant();
    var duplicate = await db.Leads.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.Name.ToUpper() == name.ToUpper() &&
      ((email != null && x.PrimaryContactEmail != null && x.PrimaryContactEmail.ToUpper() == email) ||
       (email == null && x.PrimaryContactEmail == null)), ct);
    if (duplicate)
      return CommandResult<Guid>.Fail("crm.duplicate", "A matching lead already exists; reviewed resolution is required.");

    var lead = new Lead
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Name = name,
      Source = request.Source.Trim(), PrimaryContactName = TrimOrNull(request.PrimaryContactName),
      PrimaryContactEmail = TrimOrNull(request.PrimaryContactEmail), OwnerUserId = request.OwnerUserId,
      ConsentRestrictions = TrimOrNull(request.ConsentRestrictions), CreatedAt = DateTimeOffset.UtcNow
    };
    db.Leads.Add(lead);
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

    var lead = await db.Leads.SingleOrDefaultAsync(x => x.Id == request.LeadId && x.FirmId == actor.FirmId, ct);
    if (lead is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lead.Status != CrmStates.LeadQualified)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The lead must be qualified before discovery.");

    var opportunity = new Opportunity
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, LeadId = lead.Id,
      ServiceRoute = request.ServiceRoute.Trim(), EntityScope = request.EntityScope.Trim(),
      PeriodStart = request.PeriodStart.Trim(), PeriodEnd = request.PeriodEnd.Trim(),
      ExpectedFee = request.ExpectedFee, Currency = request.Currency.Trim().ToUpperInvariant(),
      Probability = request.Probability, OwnerUserId = request.OwnerUserId,
      NextAction = TrimOrNull(request.NextAction), Stage = CrmStates.OpportunityDiscovery,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.Opportunities.Add(opportunity);
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
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");

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
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, OpportunityId = opportunity.Id,
      PracticeClientId = opportunity.PracticeClientId, Revision = (previous?.Revision ?? 0) + 1,
      Status = CrmStates.ProposalDraft, ServiceProfileId = request.ServiceProfileId.Trim(),
      Scope = request.Scope.Trim(), Exclusions = request.Exclusions.Trim(),
      Deliverables = request.Deliverables.Trim(), Dependencies = request.Dependencies.Trim(),
      Fee = request.Fee, Currency = request.Currency.Trim().ToUpperInvariant(),
      PeriodStart = request.PeriodStart.Trim(), PeriodEnd = request.PeriodEnd.Trim(),
      SupersedesId = previous?.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    opportunity.Stage = CrmStates.OpportunityProposal;
    db.Proposals.Add(proposal);
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
    proposal.Status = CrmStates.ProposalInternalReview;
    proposal.ApprovedByUserId = actor.UserId;
    proposal.ApprovedAt = DateTimeOffset.UtcNow;
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
    if (proposal.Status == CrmStates.ProposalSent) return CommandResult.Ok();
    if (proposal.Status != CrmStates.ProposalInternalReview)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only an internally reviewed proposal can be sent.");
    proposal.Status = CrmStates.ProposalSent;
    proposal.SentAt = DateTimeOffset.UtcNow;
    var opportunity = await db.Opportunities.SingleAsync(x => x.Id == proposal.OpportunityId && x.FirmId == actor.FirmId, ct);
    opportunity.Stage = CrmStates.OpportunityNegotiation;
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
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == proposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (proposal.Status == decision) return CommandResult.Ok();
    if (proposal.Status != CrmStates.ProposalSent)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a sent proposal can receive a response.");
    proposal.Status = decision;
    proposal.ResponseAt = DateTimeOffset.UtcNow;
    proposal.ResponseReason = TrimOrNull(request.Reason);
    var opportunity = await db.Opportunities.SingleAsync(x => x.Id == proposal.OpportunityId && x.FirmId == actor.FirmId, ct);
    opportunity.Stage = decision == CrmStates.ProposalAccepted
      ? CrmStates.OpportunityWon : CrmStates.OpportunityLost;
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

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == request.ProposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (proposal.PracticeClientId.HasValue) return CommandResult<Guid>.Ok(proposal.PracticeClientId.Value);
    if (proposal.Status != CrmStates.ProposalAccepted)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only an accepted commercial proposal can open client acceptance.");

    var opportunity = await db.Opportunities.SingleOrDefaultAsync(
      x => x.Id == proposal.OpportunityId && x.FirmId == actor.FirmId, ct);
    if (opportunity is null || opportunity.Stage != CrmStates.OpportunityWon)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The commercial opportunity is not won.");

    // Serialize canonical-client conversion per firm. This makes repeated conversion idempotent
    // without creating a generic repository or relying only on a uniqueness exception.
    var guard = await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");

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
      .ToListAsync(ct);
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
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(client.Id);
  }

  public static async Task<CommandResult<Guid>> CreateClientContactAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateClientContactRequest request,
    CancellationToken ct = default)
  {
    var validation = ValidateContact(request);
    if (validation is not null) return CommandResult<Guid>.Fail("crm.invalid", validation);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: request.PracticeClientId,
        RequiredRoles: CommercialRoles), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var clientGuard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {request.PracticeClientId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (clientGuard is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var clientExists = await db.PracticeClients.AsNoTracking()
      .AnyAsync(x => x.Id == request.PracticeClientId && x.FirmId == actor.FirmId, ct);
    if (!clientExists) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    clientGuard.InputGeneration++;

    if (request.Primary)
    {
      var previousPrimary = await db.ClientContacts
        .Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == request.PracticeClientId && x.Primary)
        .ToListAsync(ct);
      foreach (var previous in previousPrimary) previous.Primary = false;
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
    await tx.CommitAsync(ct);
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

  private static Task<FirmSafetyState?> LockFirmAsync(
    IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static string? ValidateLead(CreateLeadRequest request)
  {
    var error = Required(request.Name, "Lead name") ?? Required(request.Source, "Lead source");
    if (error is not null) return error;
    return string.IsNullOrWhiteSpace(request.PrimaryContactEmail) ? null : EmailError(request.PrimaryContactEmail);
  }

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
      ?? ExactMoneyError(request.Fee, "Proposal fee");
    return error ?? PeriodError(request.PeriodStart, request.PeriodEnd);
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
