using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class CommercialDocumentService
{
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
    if (previous is not null) return CommandResult<CommercialDocument>.Ok(previous);
    var profile = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (profile is null || new[] { profile.FirmHistoryAndRegistrations, profile.IndustryCredentials, profile.AuditMethodology }.Any(string.IsNullOrWhiteSpace))
      return CommandResult<CommercialDocument>.Fail(ErrorCodes.GateBlocked, "Configure the reviewed firm history, registrations, industry credentials and methodology chapters in Commercial settings.");
    var opportunity = await db.Opportunities.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == proposal.OpportunityId, ct);
    var lead = await db.Leads.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == opportunity.LeadId, ct);
    var clientName = proposal.PracticeClientId.HasValue
      ? await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == proposal.PracticeClientId).Select(x => x.LegalName).SingleAsync(ct)
      : lead.Name;
    var advance = MoneyPolicy.Normalize(quote.Fee * 0.5m, QuotationCalculator.CurrencyScale);
    var reference = $"{proposalId:N}-R{quote.Revision}";
    var now = DateTimeOffset.UtcNow;
    IReadOnlyList<DocumentSection> chapters = [
      new("1. Firm profile, history and commercial registrations", [profile.FirmHistoryAndRegistrations]),
      new("2. Assigned Engagement Partner and audit team CVs", [assignedTeamCvs.Trim()]),
      new("3. Industry credentials and portfolio evidence", [profile.IndustryCredentials]),
      new("4. Audit methodology", [profile.AuditMethodology, $"Service: {opportunity.ServiceRoute}. Scope: {proposal.Scope}."]),
      new("5. Fee schedule and deliverables timeline", [
        $"Period: {proposal.PeriodStart} to {proposal.PeriodEnd}. Agreed fee: {quote.Fee.ToString("N2", CultureInfo.InvariantCulture)} {quote.Currency}.",
        $"Advance 50%: {advance.ToString("N2", CultureInfo.InvariantCulture)} {quote.Currency}; final balance: {(quote.Fee - advance).ToString("N2", CultureInfo.InvariantCulture)} {quote.Currency}.",
        proposal.Deliverables, deliverablesTimeline.Trim()])];
    var bytes = CommercialDocumentRenderer.RenderDocx(new(profile, "Comprehensive proposal", reference,
      DateOnly.FromDateTime(now.UtcDateTime), clientName, chapters, false));
    var document = new CommercialDocument { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProposalId = proposalId,
      QuotationVersionId = quote.Id, Kind = CommercialDocumentKinds.ComprehensiveProposal, TemplateVersion = "COMMERCIAL-TENDER-v1",
      ProfileVersion = profile.Version, FileName = $"ComprehensiveProposal-{reference}.docx", ContentType = CommercialDocumentRenderer.DocxContentType,
      Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes), CreatedByUserId = actor.UserId, CreatedAt = now };
    db.CommercialDocuments.Add(document);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<CommercialDocument>.Ok(document);
  }
}
