using AuditSphereOps.Application.Acceptance;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record SaveCommercialProfileRequest(
  string LegalName, string Address, string ContactEmail, string ContactPhone, string AccentColorHex, string ClosingText,
  string FirmHistoryAndRegistrations = "", string IndustryCredentials = "", string AuditMethodology = "", long? ExpectedVersion = null,
  string? LogoPngBase64 = null, bool RemoveLogo = false);

public sealed record GeneratedProposalDocuments(CommercialDocument Quotation, CommercialDocument EngagementLetter);

/// <summary>
/// One-click Quotation and Engagement Letter from an approved commercial record. Documents are immutable, keyed to the
/// exact approved quotation version, and retain their template version, profile version and SHA-256. Generating again
/// returns the same documents; a changed quotation produces a new pair. No document is produced from an unapproved
/// quotation or a proposal fee that disagrees with it.
/// </summary>
public static partial class CommercialDocumentService
{
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];
  private static readonly string[] ProfileRoles = ["Administrator", "Partner"];
  internal const string QuotationTimelineRequiredMessage = "Add an estimated execution timeline in a new proposal revision before generating a new brief quotation.";
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task<CommandResult<Guid>> SaveProfileAsync(
    IAuditSphereDbContext db, ActorContext actor, SaveCommercialProfileRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeProfileAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var name = (request.LegalName ?? string.Empty).Trim();
    var color = (request.AccentColorHex ?? string.Empty).Trim();
    if (name.Length is < 2 or > 200) return CommandResult<Guid>.Fail("commercial.invalid", "Enter the firm's legal name (2 to 200 characters).");
    if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
      return CommandResult<Guid>.Fail("commercial.invalid", "The accent colour must be a #RRGGBB value.");
    if ((request.Address ?? string.Empty).Length > 500 || (request.ClosingText ?? string.Empty).Length > 4000 ||
        (request.ContactEmail ?? string.Empty).Length > 200 || (request.ContactPhone ?? string.Empty).Length > 60)
      return CommandResult<Guid>.Fail("commercial.invalid", "A profile field is too long.");
    if (new[] { request.FirmHistoryAndRegistrations, request.IndustryCredentials, request.AuditMethodology }.Any(x => (x ?? "").Length > 16000))
      return CommandResult<Guid>.Fail("commercial.invalid", "Tender profile chapters must each be at most 16,000 characters.");

    if (request.LogoPngBase64 is not null && request.RemoveLogo)
      return CommandResult<Guid>.Fail("commercial.logo-invalid", "Choose either a replacement PNG or Remove logo, not both.");

    byte[]? replacementLogo = null;
    string? replacementLogoHash = null;
    if (request.LogoPngBase64 is { } encodedLogo)
    {
      const string invalidLogo = "Upload a PNG logo of at most 128 KiB.";
      const int maxBase64Length = 174764;
      if (encodedLogo.Length is < 1 or > maxBase64Length ||
          !System.Text.RegularExpressions.Regex.IsMatch(encodedLogo,
            "^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$"))
        return CommandResult<Guid>.Fail("commercial.logo-invalid", invalidLogo);
      try { replacementLogo = System.Convert.FromBase64String(encodedLogo); }
      catch (FormatException) { return CommandResult<Guid>.Fail("commercial.logo-invalid", invalidLogo); }
      if (replacementLogo.Length is < 1 or > 131072 || System.Convert.ToBase64String(replacementLogo) != encodedLogo)
        return CommandResult<Guid>.Fail("commercial.logo-invalid", invalidLogo);
      var size = AuditDeliverableRenderer.ReadPngSize(replacementLogo);
      if (size is null || size.Value.Width is < 16 or > 1024 || size.Value.Height is < 16 or > 512 ||
          !AuditDeliverableRenderer.IsRenderablePng(replacementLogo))
        return CommandResult<Guid>.Fail("commercial.logo-invalid",
          "The logo must be a readable PNG between 16x16 and 1024x512 pixels.");
      replacementLogoHash = Hashing.Sha256Hex(replacementLogo);
    }

    var logoChangeRequested = request.LogoPngBase64 is not null || request.RemoveLogo;
    if (logoChangeRequested && (request.ExpectedVersion is null or < 0))
      return CommandResult<Guid>.Fail("commercial.logo-review-required",
        "Reload the firm profile and review its version before changing the logo.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizeProfileAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var latest = await db.FirmCommercialProfiles.Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);

    if (latest is not null && ((latest.LogoPngBytes is null) != (latest.LogoSha256Hex is null) ||
        (latest.LogoPngBytes is not null && !string.Equals(Hashing.Sha256Hex(latest.LogoPngBytes), latest.LogoSha256Hex, StringComparison.Ordinal))))
      return CommandResult<Guid>.Fail("commercial.logo-integrity",
        "The stored firm logo cannot be verified. Review the retained profile before changing branding.");

    var resolvedLogoBytes = request.RemoveLogo ? null : replacementLogo ?? latest?.LogoPngBytes;
    var resolvedLogoHash = request.RemoveLogo ? null : replacementLogo is not null ? replacementLogoHash : latest?.LogoSha256Hex;
    if (latest is not null && latest.LegalName == name && latest.Address == (request.Address ?? string.Empty).Trim() &&
        latest.ContactEmail == (request.ContactEmail ?? string.Empty).Trim() && latest.ContactPhone == (request.ContactPhone ?? string.Empty).Trim() &&
        string.Equals(latest.AccentColorHex, color, StringComparison.OrdinalIgnoreCase) && latest.ClosingText == (request.ClosingText ?? string.Empty).Trim() &&
        latest.FirmHistoryAndRegistrations == (request.FirmHistoryAndRegistrations ?? "").Trim() &&
        latest.IndustryCredentials == (request.IndustryCredentials ?? "").Trim() && latest.AuditMethodology == (request.AuditMethodology ?? "").Trim() &&
        latest.LogoSha256Hex == resolvedLogoHash &&
        (latest.LogoPngBytes is null ? resolvedLogoBytes is null :
          resolvedLogoBytes is not null && latest.LogoPngBytes.AsSpan().SequenceEqual(resolvedLogoBytes)))
      return CommandResult<Guid>.Ok(latest.Id);
    if (request.ExpectedVersion.HasValue && request.ExpectedVersion != (latest?.Version ?? 0))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Commercial profile changed; review its current version.");
    var profile = new FirmCommercialProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Version = (latest?.Version ?? 0) + 1, LegalName = name,
      Address = (request.Address ?? string.Empty).Trim(), ContactEmail = (request.ContactEmail ?? string.Empty).Trim(),
      ContactPhone = (request.ContactPhone ?? string.Empty).Trim(), AccentColorHex = color.ToUpperInvariant(),
      ClosingText = (request.ClosingText ?? string.Empty).Trim(),
      FirmHistoryAndRegistrations = (request.FirmHistoryAndRegistrations ?? "").Trim(), IndustryCredentials = (request.IndustryCredentials ?? "").Trim(),
      AuditMethodology = (request.AuditMethodology ?? "").Trim(),
      LogoPngBytes = resolvedLogoBytes?.ToArray(), LogoSha256Hex = resolvedLogoHash,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.FirmCommercialProfiles.Add(profile);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(profile.Id);
  }

  public static async Task<CommandResult<FirmCommercialProfile?>> GetProfileAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<FirmCommercialProfile?>.Fail(auth.ErrorCode!, auth.Message!);
    var profile = await db.FirmCommercialProfiles.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<FirmCommercialProfile?>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<FirmCommercialProfile?>.Ok(profile);
  }

  /// <summary>Brief quotation is available before client/risk acceptance; never generates an engagement letter.</summary>
  public static Task<CommandResult<CommercialDocument>> GenerateBriefQuotationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default, Guid? expectedQuotationId = null, long? expectedProfileVersion = null) => GenerateDocumentAsync(db, actor, proposalId, false, ct, expectedQuotationId, expectedProfileVersion);

  /// <summary>Only a current Partner may generate a letter after both persisted approval keys pass.</summary>
  public static Task<CommandResult<CommercialDocument>> GenerateEngagementLetterAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default, Guid? expectedQuotationId = null, long? expectedProfileVersion = null) => GenerateDocumentAsync(db, actor, proposalId, true, ct, expectedQuotationId, expectedProfileVersion);

  public static async Task<CommandResult<GeneratedProposalDocuments>> GenerateProposalDocumentsAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    // Preflight this immutable proposal term before either member of the pair can be created.
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<GeneratedProposalDocuments>.Fail(auth.ErrorCode!, auth.Message!);
    var proposal = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == proposalId, ct);
    if (proposal is null) return CommandResult<GeneratedProposalDocuments>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (string.IsNullOrWhiteSpace(proposal.Timeline))
    {
      var currentQuotationId = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
        .OrderByDescending(x => x.Revision).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
      if (currentQuotationId is null || !await db.CommercialDocuments.AsNoTracking().AnyAsync(x =>
          x.FirmId == actor.FirmId && x.QuotationVersionId == currentQuotationId && x.Kind == CommercialDocumentKinds.Quotation, ct))
        return CommandResult<GeneratedProposalDocuments>.Fail(ErrorCodes.GateBlocked, QuotationTimelineRequiredMessage);
    }
    var letter = await GenerateEngagementLetterAsync(db, actor, proposalId, ct);
    if (!letter.Succeeded) return CommandResult<GeneratedProposalDocuments>.Fail(letter.ErrorCode!, letter.Message!);
    var quotation = await GenerateBriefQuotationAsync(db, actor, proposalId, ct);
    return quotation.Succeeded ? CommandResult<GeneratedProposalDocuments>.Ok(new(quotation.Value!, letter.Value!))
      : CommandResult<GeneratedProposalDocuments>.Fail(quotation.ErrorCode!, quotation.Message!);
  }

  private static async Task<CommandResult<CommercialDocument>> GenerateDocumentAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, bool engagementLetter, CancellationToken ct = default, Guid? expectedQuotationId = null, long? expectedProfileVersion = null)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<CommercialDocument>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<CommercialDocument>.Fail(auth.ErrorCode!, auth.Message!);

    var proposal = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult<CommercialDocument>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var quotation = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (quotation is null || quotation.Status != QuotationStates.Approved || quotation.Fee != proposal.Fee)
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked,
        "Documents are generated only from an approved quotation whose fee matches the proposal.");
    var profile = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (profile is null)
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "Configure the firm commercial profile (name, address, branding) before generating documents.");

    if ((expectedQuotationId.HasValue && expectedQuotationId != quotation.Id)
      || (expectedProfileVersion.HasValue && expectedProfileVersion != profile.Version))
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.StaleRevision, "The reviewed quotation or commercial profile changed.");

    var opportunity = await db.Opportunities.AsNoTracking().SingleAsync(x => x.Id == proposal.OpportunityId && x.FirmId == actor.FirmId, ct);
    // STE 4.1.4: the letter template comes from the approved service route. A route with no configured template fails closed.
    var letterTemplate = engagementLetter ? EngagementLetterTemplates.Resolve(opportunity.ServiceRoute) : null;
    if (engagementLetter && letterTemplate is null)
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked,
        $"The engagement letter template is not configured for service route '{opportunity.ServiceRoute}'.");
    AuditSphereOps.Domain.Acceptance.AcceptanceDecision? riskDecision = null;
    if (engagementLetter)
    {
      var partnerAuth = await AuthorizeAsync(db, actor, ["Partner"], ct);
      if (!partnerAuth.Succeeded) return CommandResult<CommercialDocument>.Fail(partnerAuth.ErrorCode!, partnerAuth.Message!);
      if (proposal.Status != CrmStates.ProposalAccepted || proposal.SentAt is null || proposal.ResponseAt is null || proposal.PracticeClientId is not { } clientId)
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "Record client acceptance of the current quotation and open client acceptance before generating the engagement letter.");
      var guard = await db.ClientSafetyStates.FromSqlInterpolated(
        $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
      riskDecision = await EngagementRiskEvidence.ForPeriodAsync(db, actor.FirmId, clientId,
        opportunity.ServiceRoute, proposal.PeriodStart, proposal.PeriodEnd, ct);
      if (guard is null || riskDecision is null || riskDecision.Decision != "Accepted" || riskDecision.DecidedByUserId is null || riskDecision.DecidedAt is null)
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "A current unconditional Partner acceptance decision for this client and service is required.");
      if (riskDecision.Generation != guard.InputGeneration)
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.GenerationStale, "The Partner risk clearance is stale; complete the current acceptance checklist first.");
      // The letter must use the exact accepted commercial terms (STE 4.1.3): a dispatched offer binds one
      // quotation revision, and the recorded acceptance must cite that dispatched offer identity.
      var dispatch = await db.CommercialNotifications.AsNoTracking().SingleOrDefaultAsync(
        x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id && x.Kind == CommercialNotificationKinds.Proposal, ct);
      if (dispatch is null)
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked,
          "A recorded dispatch of the approved offer is required before generating the engagement letter.");
      if (dispatch.QuotationVersionId != quotation.Id)
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.StaleRevision,
          "The client accepted a different quotation revision than the current one; use a newly approved commercial revision with fresh acceptance for reissue.");
      var response = await db.ProposalResponseRecords.AsNoTracking()
        .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id, ct);
      var dispatchedQuotation = dispatch.DocumentId is { } quotationDocumentId
        ? await db.CommercialDocuments.AsNoTracking()
          .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == quotationDocumentId, ct)
        : null;
      if (!ProposalResponseEvidence.MatchesAcceptedResponse(proposal, dispatch, response, quotation, dispatchedQuotation))
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.GenerationStale,
          "A matching immutable client response to the exact current rendered offer is required before generating the engagement letter.");
    }

    var existing = await db.CommercialDocuments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.QuotationVersionId == quotation.Id &&
        (x.Kind == CommercialDocumentKinds.Quotation || x.Kind == CommercialDocumentKinds.EngagementLetter)).ToListAsync(ct);
    var existingQuote = existing.SingleOrDefault(x => x.Kind == CommercialDocumentKinds.Quotation);
    var existingLetter = existing.SingleOrDefault(x => x.Kind == CommercialDocumentKinds.EngagementLetter);
    var previous = engagementLetter ? existingLetter : existingQuote;
    if (previous is not null)
    {
      if (engagementLetter && previous.AcceptanceDecisionId != riskDecision?.Id)
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.GenerationStale, "The historical letter belongs to an earlier risk clearance; use a newly approved commercial revision for reissue.");
      return CommandResult<CommercialDocument>.Ok(previous);
    }

    if (!engagementLetter && string.IsNullOrWhiteSpace(proposal.Timeline))
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, QuotationTimelineRequiredMessage);

    var lead = await db.Leads.AsNoTracking().SingleAsync(x => x.Id == opportunity.LeadId && x.FirmId == actor.FirmId, ct);
    var clientName = proposal.PracticeClientId.HasValue
      ? await db.PracticeClients.AsNoTracking().Where(x => x.Id == proposal.PracticeClientId && x.FirmId == actor.FirmId).Select(x => x.LegalName).SingleAsync(ct)
      : lead.Name;
    var addressee = string.IsNullOrWhiteSpace(lead.PrimaryContactName) ? clientName : $"{clientName} (attention {lead.PrimaryContactName})";
    var lines = JsonSerializer.Deserialize<List<QuotationLineResult>>(quotation.LinesJson, Json) ?? [];
    SignatureSpecimen? signature = null;
    FirmSealSpecimen? seal = null;
    if (engagementLetter)
    {
      signature = await db.SignatureSpecimens.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null, ct);
      seal = await db.FirmSealSpecimens.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
      if (signature is null || seal is null) return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "Register your approved Partner signature and the official firm seal before generating the engagement letter.");
    }
    var now = DateTimeOffset.UtcNow;
    var shortId = proposal.Id.ToString("N")[..8].ToUpperInvariant();
    var reference = $"{shortId}-R{quotation.Revision}";
    var advance = MoneyPolicy.Normalize(quotation.Fee * 50m / 100m, QuotationCalculator.CurrencyScale);
    var balance = quotation.Fee - advance;

    CommercialDocument Store(string kind, string template, string prefix, byte[] bytes) => new()
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProposalId = proposal.Id, QuotationVersionId = quotation.Id, Kind = kind,
      TemplateVersion = template, ProfileVersion = profile.Version, FileName = $"{prefix}-{reference}.docx",
      ContentType = CommercialDocumentRenderer.DocxContentType, Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes),
      CreatedByUserId = actor.UserId, CreatedAt = now, AcceptanceDecisionId = riskDecision?.Id, CommercialAcceptedAt = engagementLetter ? proposal.ResponseAt : null, SignatureSpecimenId = signature?.Id, FirmSealSpecimenId = seal?.Id
    };

    var engagement = new DocumentSection("Engagement", [
      $"Service: {opportunity.ServiceRoute}", $"Entity scope: {opportunity.EntityScope}",
      $"Period: {proposal.PeriodStart} to {proposal.PeriodEnd}", $"Service profile: {proposal.ServiceProfileId}"]);
    var scope = new DocumentSection("Scope of work", [proposal.Scope]);
    var deliverables = new DocumentSection("Deliverables", [proposal.Deliverables]);
    // The timeline is an offered term: it is rendered into the artifact, so it is part of the identity the client accepts.
    var timeline = string.IsNullOrWhiteSpace(proposal.Timeline) ? null : new DocumentSection("Timeline", [proposal.Timeline]);
    var exclusions = string.IsNullOrWhiteSpace(proposal.Exclusions) ? null : new DocumentSection("Exclusions", [proposal.Exclusions]);
    var dependencies = string.IsNullOrWhiteSpace(proposal.Dependencies) ? null : new DocumentSection("Client responsibilities and dependencies", [proposal.Dependencies]);
    var terms = quotation.NonStandardTerms ? new DocumentSection("Non-standard terms", [quotation.NonStandardTermsNote ?? string.Empty]) : null;
    var payment = new DocumentSection("Payment terms", [
      $"The agreed fee is {Money(quotation.Fee, quotation.Currency)}.",
      $"50% ({Money(advance, quotation.Currency)}) is payable in advance to activate the engagement and is recorded manually on receipt.",
      $"The remaining 50% ({Money(balance, quotation.Currency)}) is invoiced on delivery and sign-off of the final report."]);

    var feeTable = new DocumentTable(["Role", "Activity", "Hours", "Rate / hour", "Amount"],
      lines.Select(l => (IReadOnlyList<string>)[l.Role, l.Activity, l.Hours.ToString("0.##", CultureInfo.InvariantCulture),
        l.RatePerHour.ToString("N2", CultureInfo.InvariantCulture), l.Amount.ToString("N2", CultureInfo.InvariantCulture)]).Concat(
      [
        ["Subtotal (hours × rate)", "", "", "", quotation.BaseAmount.ToString("N2", CultureInfo.InvariantCulture)],
        [$"Complexity factor × {quotation.ComplexityFactor:0.####}", "", "", "", Signed(quotation.ComplexityAmount)],
        [$"Risk premium {quotation.RiskPremiumPercent:0.####}%", "", "", "", Signed(quotation.RiskPremiumAmount)],
        [$"Discount {quotation.DiscountPercent:0.####}%", "", "", "", Signed(-quotation.DiscountAmount)],
        [$"Total fee ({quotation.Currency})", "", "", "", quotation.Fee.ToString("N2", CultureInfo.InvariantCulture)],
      ]).ToList(), [2, 3, 4]);

    var quoteSections = new List<DocumentSection?> { engagement, scope, deliverables, timeline, exclusions, dependencies,
      new("Fee", [], feeTable), terms, payment };
    var letterSections = new List<DocumentSection?> {
      new(letterTemplate?.Heading ?? "Engagement letter", [$"We are pleased to confirm the terms on which {profile.LegalName} will act for {clientName}."]),
      engagement, scope, deliverables, timeline, exclusions, dependencies,
      new("Firm contact", [profile.Address, profile.ContactEmail, profile.ContactPhone]),
      new("Reviewed engagement terms", [profile.ClosingText]),
      new("Fee", [$"Total agreed fee: {Money(quotation.Fee, quotation.Currency)}."], null), terms, payment };

    var quote = Store(CommercialDocumentKinds.Quotation, CommercialDocumentRenderer.QuotationTemplate, "Quotation",
      CommercialDocumentRenderer.RenderDocx(new(profile, "Quotation", reference, DateOnly.FromDateTime(now.UtcDateTime), addressee,
        quoteSections.Where(x => x is not null).Select(x => x!).ToList(), false)));
    var letterBytes = engagementLetter
      ? AuditDeliverableRenderer.RenderDocx(new(profile.LegalName, "Engagement letter", reference, DateOnly.FromDateTime(now.UtcDateTime), addressee,
        letterSections.Where(x => x is not null).Select(x => x!).Append(new DocumentSection("Acceptance", ["For the client: ________________________   Date: __________"])).ToList(),
        "Engagement Partner", new(signature!.PngContent, signature.WidthPixels, signature.HeightPixels,
          (await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId, ct)).DisplayName, "Engagement Partner", DateOnly.FromDateTime(now.UtcDateTime)), seal!.PngContent))
      : [];
    var selected = engagementLetter
      ? Store(CommercialDocumentKinds.EngagementLetter, letterTemplate!.Id, "EngagementLetter", letterBytes)
      : quote;
    if (engagementLetter)
    {
      var agreement = await FeeAgreementService.EnsureReviewedAgreementWithinTransactionAsync(db, actor, proposal.Id, ct);
      if (!agreement.Succeeded) return CommandResult<CommercialDocument>.Fail(agreement.ErrorCode!, agreement.Message!);
    }
    db.CommercialDocuments.Add(selected);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return CommandResult<CommercialDocument>.Fail("commercial.conflict", "The documents were generated concurrently; reload."); }
    await tx.CommitAsync(ct);
    return CommandResult<CommercialDocument>.Ok(selected);
  }

  public static async Task<CommandResult<IReadOnlyList<CommercialDocument>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<CommercialDocument>>.Fail(auth.ErrorCode!, auth.Message!);
    IReadOnlyList<CommercialDocument> documents = await db.CommercialDocuments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<CommercialDocument>>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<IReadOnlyList<CommercialDocument>>.Ok(documents);
  }

  /// <summary>Bytes of one document, for firm staff or authorized client users for their client's documents.</summary>
  public static async Task<CommandResult<CommercialDocument>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid documentId, CancellationToken ct = default)
  {
    var document = await db.CommercialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.FirmId == actor.FirmId, ct);
    if (document is null) return CommandResult<CommercialDocument>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var staffAuth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (staffAuth.Succeeded) return CommandResult<CommercialDocument>.Ok(document);

    var clientUser = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" && !x.Disabled, ct);
    if (clientUser is not null && clientUser.SessionEpoch == actor.SessionEpoch)
    {
      Guid? documentClientId = null;
      Guid? documentEngagementId = null;
      if (document.Kind == CommercialDocumentKinds.PaymentReceipt && document.FeeMilestoneId is { } milestoneId)
      {
        var milestone = await db.FeeMilestones.AsNoTracking()
          .SingleOrDefaultAsync(x => x.Id == milestoneId && x.FirmId == actor.FirmId, ct);
        if (milestone is not null)
        {
          var agreement = await db.EngagementFeeAgreements.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == milestone.AgreementId && x.FirmId == actor.FirmId, ct);
          if (agreement is not null)
          {
            var proposal = await db.Proposals.AsNoTracking()
              .SingleOrDefaultAsync(x => x.Id == agreement.ProposalId && x.FirmId == actor.FirmId, ct);
            var documentLineageMatches =
              (document.ProposalId is null && document.QuotationVersionId is null) ||
              (document.ProposalId == agreement.ProposalId && document.QuotationVersionId == agreement.QuotationVersionId);
            var proposalMatchesAgreement = proposal?.PracticeClientId == agreement.PracticeClientId;
            var engagementMatchesAgreement = true;
            if (agreement.EngagementId is { } engagementId)
            {
              engagementMatchesAgreement = await db.Engagements.AsNoTracking().AnyAsync(x =>
                x.Id == engagementId && x.FirmId == actor.FirmId &&
                x.PracticeClientId == agreement.PracticeClientId, ct);
            }

            if (documentLineageMatches && proposalMatchesAgreement && engagementMatchesAgreement)
            {
              documentClientId = agreement.PracticeClientId;
              documentEngagementId = agreement.EngagementId;
            }
          }
        }
      }

      // Client users only receive explicitly client-shared documents (official payment receipts);
      // internal quotations, tenders and letters are never downloadable through the portal. The
      // grant must cover the exact client and, for engagement documents, the exact engagement:
      // an engagement-scoped grant never reaches a sibling engagement, and no null-client grant
      // widens access (STE-REM-03).
      if (documentClientId.HasValue && document.Kind == CommercialDocumentKinds.PaymentReceipt)
      {
        var now = DateTimeOffset.UtcNow;
        var clientId = documentClientId.Value;
        var engagementId = documentEngagementId;
        var hasGrant = await db.Users.AsNoTracking().AnyAsync(user =>
          user.Id == actor.UserId &&
          user.FirmId == actor.FirmId &&
          user.UserKind == "Client" &&
          !user.Disabled &&
          user.SessionEpoch == actor.SessionEpoch &&
          db.RoleGrants.Any(g =>
            g.FirmId == actor.FirmId &&
            g.UserId == actor.UserId &&
            g.Role == "ClientUser" &&
            g.ClientId == clientId &&
            (g.EngagementId == null || (engagementId.HasValue && g.EngagementId == engagementId.Value)) &&
            g.RevokedAt == null &&
            (g.ExpiresAt == null || g.ExpiresAt > now)), ct);
        if (hasGrant) return CommandResult<CommercialDocument>.Ok(document);
      }
    }

    return CommandResult<CommercialDocument>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }

  private static string Money(decimal amount, string currency) => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";
  private static string Signed(decimal amount) => (amount < 0 ? "−" : amount > 0 ? "+" : "") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

  private static Task<CommandResult> AuthorizeProfileAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ProfileRoles, InternalOnly: true, RequireFirmWide: true), ct);

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);
}
