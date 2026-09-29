using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record SaveCommercialProfileRequest(
  string LegalName, string Address, string ContactEmail, string ContactPhone, string AccentColorHex, string ClosingText);

public sealed record GeneratedProposalDocuments(CommercialDocument Quotation, CommercialDocument EngagementLetter);

/// <summary>
/// One-click Quotation and Engagement Letter from an approved commercial record. Documents are immutable, keyed to the
/// exact approved quotation version, and retain their template version, profile version and SHA-256. Generating again
/// returns the same documents; a changed quotation produces a new pair. No document is produced from an unapproved
/// quotation or a proposal fee that disagrees with it.
/// </summary>
public static class CommercialDocumentService
{
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];
  private static readonly string[] ProfileRoles = ["Administrator", "Partner"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task<CommandResult<Guid>> SaveProfileAsync(
    IAuditSphereDbContext db, ActorContext actor, SaveCommercialProfileRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ProfileRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var name = (request.LegalName ?? string.Empty).Trim();
    var color = (request.AccentColorHex ?? string.Empty).Trim();
    if (name.Length is < 2 or > 200) return CommandResult<Guid>.Fail("commercial.invalid", "Enter the firm's legal name (2 to 200 characters).");
    if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
      return CommandResult<Guid>.Fail("commercial.invalid", "The accent colour must be a #RRGGBB value.");
    if ((request.Address ?? string.Empty).Length > 500 || (request.ClosingText ?? string.Empty).Length > 4000 ||
        (request.ContactEmail ?? string.Empty).Length > 200 || (request.ContactPhone ?? string.Empty).Length > 60)
      return CommandResult<Guid>.Fail("commercial.invalid", "A profile field is too long.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var latest = await db.FirmCommercialProfiles.Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (latest is not null && latest.LegalName == name && latest.Address == (request.Address ?? string.Empty).Trim() &&
        latest.ContactEmail == (request.ContactEmail ?? string.Empty).Trim() && latest.ContactPhone == (request.ContactPhone ?? string.Empty).Trim() &&
        string.Equals(latest.AccentColorHex, color, StringComparison.OrdinalIgnoreCase) && latest.ClosingText == (request.ClosingText ?? string.Empty).Trim())
      return CommandResult<Guid>.Ok(latest.Id);
    var profile = new FirmCommercialProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Version = (latest?.Version ?? 0) + 1, LegalName = name,
      Address = (request.Address ?? string.Empty).Trim(), ContactEmail = (request.ContactEmail ?? string.Empty).Trim(),
      ContactPhone = (request.ContactPhone ?? string.Empty).Trim(), AccentColorHex = color.ToUpperInvariant(),
      ClosingText = (request.ClosingText ?? string.Empty).Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
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
    return CommandResult<FirmCommercialProfile?>.Ok(await db.FirmCommercialProfiles.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct));
  }

  public static async Task<CommandResult<GeneratedProposalDocuments>> GenerateProposalDocumentsAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<GeneratedProposalDocuments>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<GeneratedProposalDocuments>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");

    var proposal = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult<GeneratedProposalDocuments>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var quotation = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (quotation is null || quotation.Status != QuotationStates.Approved || quotation.Fee != proposal.Fee)
      return CommandResult<GeneratedProposalDocuments>.Fail(ErrorCodes.GateBlocked,
        "Documents are generated only from an approved quotation whose fee matches the proposal.");
    var profile = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (profile is null)
      return CommandResult<GeneratedProposalDocuments>.Fail(ErrorCodes.GateBlocked, "Configure the firm commercial profile (name, address, branding) before generating documents.");

    var existing = await db.CommercialDocuments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.QuotationVersionId == quotation.Id &&
        (x.Kind == CommercialDocumentKinds.Quotation || x.Kind == CommercialDocumentKinds.EngagementLetter)).ToListAsync(ct);
    var existingQuote = existing.SingleOrDefault(x => x.Kind == CommercialDocumentKinds.Quotation);
    var existingLetter = existing.SingleOrDefault(x => x.Kind == CommercialDocumentKinds.EngagementLetter);
    if (existingQuote is not null && existingLetter is not null)
      return CommandResult<GeneratedProposalDocuments>.Ok(new(existingQuote, existingLetter));

    var opportunity = await db.Opportunities.AsNoTracking().SingleAsync(x => x.Id == proposal.OpportunityId && x.FirmId == actor.FirmId, ct);
    var lead = await db.Leads.AsNoTracking().SingleAsync(x => x.Id == opportunity.LeadId && x.FirmId == actor.FirmId, ct);
    var clientName = proposal.PracticeClientId.HasValue
      ? await db.PracticeClients.AsNoTracking().Where(x => x.Id == proposal.PracticeClientId && x.FirmId == actor.FirmId).Select(x => x.LegalName).SingleAsync(ct)
      : lead.Name;
    var addressee = string.IsNullOrWhiteSpace(lead.PrimaryContactName) ? clientName : $"{clientName} (attention {lead.PrimaryContactName})";
    var lines = JsonSerializer.Deserialize<List<QuotationLineResult>>(quotation.LinesJson, Json) ?? [];
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
      CreatedByUserId = actor.UserId, CreatedAt = now
    };

    var engagement = new DocumentSection("Engagement", [
      $"Service: {opportunity.ServiceRoute}", $"Entity scope: {opportunity.EntityScope}",
      $"Period: {proposal.PeriodStart} to {proposal.PeriodEnd}", $"Service profile: {proposal.ServiceProfileId}"]);
    var scope = new DocumentSection("Scope of work", [proposal.Scope]);
    var deliverables = new DocumentSection("Deliverables", [proposal.Deliverables]);
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

    var quoteSections = new List<DocumentSection?> { engagement, scope, deliverables, exclusions, dependencies,
      new("Fee", [], feeTable), terms, payment };
    var letterSections = new List<DocumentSection?> {
      new("Introduction", [$"We are pleased to confirm the terms on which {profile.LegalName} will act for {clientName}."]),
      engagement, scope, deliverables, exclusions, dependencies,
      new("Fee", [$"Total agreed fee: {Money(quotation.Fee, quotation.Currency)}."], null), terms, payment };

    var quote = Store(CommercialDocumentKinds.Quotation, CommercialDocumentRenderer.QuotationTemplate, "Quotation",
      CommercialDocumentRenderer.RenderDocx(new(profile, "Quotation", reference, DateOnly.FromDateTime(now.UtcDateTime), addressee,
        quoteSections.Where(x => x is not null).Select(x => x!).ToList(), false)));
    var letter = Store(CommercialDocumentKinds.EngagementLetter, CommercialDocumentRenderer.EngagementLetterTemplate, "EngagementLetter",
      CommercialDocumentRenderer.RenderDocx(new(profile, "Engagement letter", reference, DateOnly.FromDateTime(now.UtcDateTime), addressee,
        letterSections.Where(x => x is not null).Select(x => x!).ToList(), true)));
    db.CommercialDocuments.Add(quote);
    db.CommercialDocuments.Add(letter);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return CommandResult<GeneratedProposalDocuments>.Fail("commercial.conflict", "The documents were generated concurrently; reload."); }
    await tx.CommitAsync(ct);
    return CommandResult<GeneratedProposalDocuments>.Ok(new(quote, letter));
  }

  public static async Task<CommandResult<IReadOnlyList<CommercialDocument>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<CommercialDocument>>.Fail(auth.ErrorCode!, auth.Message!);
    IReadOnlyList<CommercialDocument> documents = await db.CommercialDocuments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    return CommandResult<IReadOnlyList<CommercialDocument>>.Ok(documents);
  }

  /// <summary>Bytes of one document, only for a firm-wide commercial identity in the same firm.</summary>
  public static async Task<CommandResult<CommercialDocument>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid documentId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<CommercialDocument>.Fail(auth.ErrorCode!, auth.Message!);
    var document = await db.CommercialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.FirmId == actor.FirmId, ct);
    return document is null ? CommandResult<CommercialDocument>.Fail(ErrorCodes.ScopeDenied, "Access denied.") : CommandResult<CommercialDocument>.Ok(document);
  }

  private static string Money(decimal amount, string currency) => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";
  private static string Signed(decimal amount) => (amount < 0 ? "−" : amount > 0 ? "+" : "") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);
}
