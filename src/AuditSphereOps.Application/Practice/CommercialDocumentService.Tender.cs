using System.Globalization;
using System.Text.Json;
using System.Xml;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using DocumentFormat.OpenXml.CustomProperties;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.VariantTypes;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class CommercialDocumentService
{
  private const string TenderTemplateVersion = "COMMERCIAL-TENDER-v1";
  private const string TenderInputHashPropertyName = "AuditSphereTenderInputSha256";
  private const string CustomPropertyFormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";
  private const string TenderInputChangedMessage =
    "The existing proposal is unverifiable or its reviewed inputs changed; create and approve a new quotation revision before generating revised content. The saved artifact remains available.";

  /// <summary>Five reviewed chapters tied to one approved price revision. No invented registrations, CVs or credentials.</summary>
  public static async Task<CommandResult<CommercialDocument>> GenerateTenderAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid proposalId, string assignedTeamCvs, string deliverablesTimeline, bool reviewed, CancellationToken ct = default, Guid? expectedQuotationId = null, long? expectedProfileVersion = null)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<CommercialDocument>.Fail(auth.ErrorCode!, auth.Message!);
    if (!reviewed || string.IsNullOrWhiteSpace(assignedTeamCvs) || string.IsNullOrWhiteSpace(deliverablesTimeline) ||
        assignedTeamCvs.Length > 16000 || deliverablesTimeline.Length > 8000)
      return CommandResult<CommercialDocument>.Fail("commercial.invalid", "Review the assigned team CVs and deliverables timeline before generating the proposal.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<CommercialDocument>.Fail(auth.ErrorCode!, auth.Message!);

    var proposal = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == proposalId, ct);
    if (proposal is null) return CommandResult<CommercialDocument>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var quote = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (quote is null || quote.Status != QuotationStates.Approved || quote.Fee != proposal.Fee)
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "A current approved quotation matching the proposal fee is required.");
    if (expectedQuotationId.HasValue && expectedQuotationId != quote.Id)
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.StaleRevision, "The reviewed quotation changed.");
    if (expectedProfileVersion.HasValue)
    {
      var currentProfileVersion = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId)
        .OrderByDescending(x => x.Version).Select(x => (long?)x.Version).FirstOrDefaultAsync(ct);
      if (currentProfileVersion != expectedProfileVersion)
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.StaleRevision, "The reviewed commercial profile changed.");
    }
    var previous = await db.CommercialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.QuotationVersionId == quote.Id && x.Kind == CommercialDocumentKinds.ComprehensiveProposal, ct);
    var profile = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (profile is null)
      return previous is not null
        ? CommandResult<CommercialDocument>.Fail(ErrorCodes.StaleRevision, TenderInputChangedMessage)
        : CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "Configure the reviewed firm history, registrations, industry credentials and methodology chapters in Commercial settings.");
    var profileReady = !new[] { profile.FirmHistoryAndRegistrations, profile.IndustryCredentials, profile.AuditMethodology }.Any(string.IsNullOrWhiteSpace);
    var opportunity = await db.Opportunities.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == proposal.OpportunityId, ct);
    var lead = await db.Leads.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == opportunity.LeadId, ct);
    var clientName = proposal.PracticeClientId.HasValue
      ? await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == proposal.PracticeClientId).Select(x => x.LegalName).SingleAsync(ct)
      : lead.Name;
    var advance = MoneyPolicy.Normalize(quote.Fee * 0.5m, QuotationCalculator.CurrencyScale);
    var balance = quote.Fee - advance;
    var reference = $"{proposalId:N}-R{quote.Revision}";
    var reviewedTeamCvs = assignedTeamCvs.Trim();
    var reviewedTimeline = deliverablesTimeline.Trim();
    var inputHash = TenderInputHash(actor.FirmId, proposal, quote, profile, opportunity.ServiceRoute,
      clientName, reference, advance, balance, reviewedTeamCvs, reviewedTimeline);

    if (previous is not null)
    {
      // Verify the bytes before opening the package to inspect its input-hash property.
      if (previous.Bytes is null ||
          !string.Equals(previous.Sha256Hex, Hashing.Sha256Hex(previous.Bytes), StringComparison.OrdinalIgnoreCase))
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.StaleRevision, TenderInputChangedMessage);
      if (previous.ProposalId != proposalId || previous.TemplateVersion != TenderTemplateVersion ||
          !profileReady || previous.ProfileVersion != profile.Version)
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.StaleRevision, TenderInputChangedMessage);
      var previousInputHash = ReadTenderInputHash(previous.Bytes);
      if (!string.Equals(previousInputHash, inputHash, StringComparison.OrdinalIgnoreCase))
        return CommandResult<CommercialDocument>.Fail(ErrorCodes.StaleRevision, TenderInputChangedMessage);
      return CommandResult<CommercialDocument>.Ok(previous);
    }

    if (!profileReady)
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "Configure the reviewed firm history, registrations, industry credentials and methodology chapters in Commercial settings.");
    var now = DateTimeOffset.UtcNow;
    IReadOnlyList<DocumentSection> chapters = [
      new("1. Firm profile, history and commercial registrations", [profile.FirmHistoryAndRegistrations]),
      new("2. Assigned Engagement Partner and audit team CVs", [reviewedTeamCvs]),
      new("3. Industry credentials and portfolio evidence", [profile.IndustryCredentials]),
      new("4. Audit methodology", [profile.AuditMethodology, $"Service: {opportunity.ServiceRoute}. Scope: {proposal.Scope}."]),
      new("5. Fee schedule and deliverables timeline", [
        $"Period: {proposal.PeriodStart} to {proposal.PeriodEnd}. Agreed fee: {quote.Fee.ToString("N2", CultureInfo.InvariantCulture)} {quote.Currency}.",
        $"Advance 50%: {advance.ToString("N2", CultureInfo.InvariantCulture)} {quote.Currency}; final balance: {balance.ToString("N2", CultureInfo.InvariantCulture)} {quote.Currency}.",
        proposal.Deliverables, reviewedTimeline])];
    var renderedBytes = CommercialDocumentRenderer.RenderDocx(new(profile, "Comprehensive proposal", reference,
      DateOnly.FromDateTime(now.UtcDateTime), clientName, chapters, false));
    var bytes = AddTenderInputHash(renderedBytes, inputHash);
    var document = new CommercialDocument { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProposalId = proposalId,
      QuotationVersionId = quote.Id, Kind = CommercialDocumentKinds.ComprehensiveProposal, TemplateVersion = TenderTemplateVersion,
      ProfileVersion = profile.Version, FileName = $"ComprehensiveProposal-{reference}.docx", ContentType = CommercialDocumentRenderer.DocxContentType,
      Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes), CreatedByUserId = actor.UserId, CreatedAt = now };
    db.CommercialDocuments.Add(document);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<CommercialDocument>.Ok(document);
  }

  // Bump SchemaVersion when rendered inputs change; generation time and actor are intentionally excluded for replay.
  private static string TenderInputHash(Guid firmId, Proposal proposal, QuotationVersion quote,
    FirmCommercialProfile profile, string serviceRoute, string clientName, string reference,
    decimal advance, decimal balance, string assignedTeamCvs, string deliverablesTimeline) =>
    Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new TenderReviewedInputs(
      SchemaVersion: 1,
      TemplateVersion: TenderTemplateVersion,
      FirmId: firmId,
      ProposalId: proposal.Id,
      ProposalRevision: proposal.Revision,
      QuotationVersionId: quote.Id,
      QuotationRevision: quote.Revision,
      ProfileId: profile.Id,
      ProfileVersion: profile.Version,
      LogoSha256Hex: profile.LogoSha256Hex,
      FirmLegalName: profile.LegalName,
      FirmAddress: profile.Address,
      ContactEmail: profile.ContactEmail,
      ContactPhone: profile.ContactPhone,
      AccentColorHex: profile.AccentColorHex,
      ClosingText: profile.ClosingText,
      FirmHistoryAndRegistrations: profile.FirmHistoryAndRegistrations,
      IndustryCredentials: profile.IndustryCredentials,
      AuditMethodology: profile.AuditMethodology,
      ServiceRoute: serviceRoute,
      ClientName: clientName,
      Reference: reference,
      ProposalScope: proposal.Scope,
      PeriodStart: proposal.PeriodStart,
      PeriodEnd: proposal.PeriodEnd,
      Fee: quote.Fee,
      Currency: quote.Currency,
      Advance: advance,
      Balance: balance,
      Deliverables: proposal.Deliverables,
      AssignedTeamCvs: assignedTeamCvs,
      DeliverablesTimeline: deliverablesTimeline)));

  private static byte[] AddTenderInputHash(byte[] renderedBytes, string inputHash)
  {
    using var stream = new MemoryStream();
    stream.Write(renderedBytes);
    stream.Position = 0;
    using (var document = WordprocessingDocument.Open(stream, true))
    {
      var propertiesPart = document.AddCustomFilePropertiesPart();
      propertiesPart.Properties = new Properties(
        new CustomDocumentProperty
        {
          FormatId = CustomPropertyFormatId,
          PropertyId = 2,
          Name = TenderInputHashPropertyName,
          VTLPWSTR = new VTLPWSTR(inputHash)
        });
      propertiesPart.Properties.Save();
    }
    return stream.ToArray();
  }

  private static string? ReadTenderInputHash(byte[] bytes)
  {
    try
    {
      using var stream = new MemoryStream(bytes, writable: false);
      using var document = WordprocessingDocument.Open(stream, false);
      var matches = document.CustomFilePropertiesPart?.Properties?.Elements<CustomDocumentProperty>()
        .Where(property => string.Equals(property.Name?.Value, TenderInputHashPropertyName, StringComparison.OrdinalIgnoreCase))
        .ToArray() ?? [];
      if (matches.Length != 1) return null;

      var property = matches[0];
      if (!string.Equals(property.Name?.Value, TenderInputHashPropertyName, StringComparison.Ordinal) ||
          property.PropertyId is null || property.PropertyId.Value < 2 ||
          !string.Equals(property.FormatId?.Value, CustomPropertyFormatId, StringComparison.OrdinalIgnoreCase) ||
          property.ChildElements.Count != 1 || property.FirstChild is not VTLPWSTR valueElement)
        return null;
      var value = valueElement.Text;
      return IsSha256Hex(value) ? value : null;
    }
    catch (Exception exception) when (exception is OpenXmlPackageException or InvalidDataException or IOException or XmlException or ArgumentException)
    {
      return null;
    }
  }

  private static bool IsSha256Hex(string? value) => value is { Length: 64 } && value.All(character =>
    character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

  private sealed record TenderReviewedInputs(
    int SchemaVersion,
    string TemplateVersion,
    Guid FirmId,
    Guid ProposalId,
    long ProposalRevision,
    Guid QuotationVersionId,
    long QuotationRevision,
    Guid ProfileId,
    long ProfileVersion,
    string? LogoSha256Hex,
    string FirmLegalName,
    string FirmAddress,
    string ContactEmail,
    string ContactPhone,
    string AccentColorHex,
    string ClosingText,
    string FirmHistoryAndRegistrations,
    string IndustryCredentials,
    string AuditMethodology,
    string ServiceRoute,
    string ClientName,
    string Reference,
    string ProposalScope,
    string PeriodStart,
    string PeriodEnd,
    decimal Fee,
    string Currency,
    decimal Advance,
    decimal Balance,
    string Deliverables,
    string AssignedTeamCvs,
    string DeliverablesTimeline);
}
