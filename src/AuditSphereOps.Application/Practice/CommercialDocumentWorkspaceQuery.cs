using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record CommercialArtifact(Guid Id, Guid? QuotationId, string Kind, string TemplateVersion,
  string ProfileVersion, string FileName, string ContentType, string Sha256, DateTimeOffset CreatedAt);
public sealed record CommercialDocumentWorkspace(Guid ProposalId, Guid? QuotationId, string? ProfileVersion,
  string? FirmName, string? History, string? Credentials, string? Methodology,
  IReadOnlyList<string> CommonBlockers, IReadOnlyList<string> LetterBlockers, IReadOnlyList<string> TenderBlockers,
  IReadOnlyList<CommercialArtifact> Documents);

public static class CommercialDocumentWorkspaceQuery
{
  public static async Task<CommandResult<CommercialDocumentWorkspace>> GetAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    var authorized = await CommercialWorkspaceQuery.ProposalAsync(db, actor, proposalId, ct);
    if (!authorized.Succeeded) return CommandResult<CommercialDocumentWorkspace>.Fail(ErrorCodes.ScopeDenied, "Documents unavailable.");
    var proposal = await db.Proposals.AsNoTracking().SingleAsync(p => p.FirmId == actor.FirmId && p.Id == proposalId, ct);
    var quotation = await db.QuotationVersions.AsNoTracking().Where(q => q.FirmId == actor.FirmId && q.ProposalId == proposalId)
      .OrderByDescending(q => q.Revision).FirstOrDefaultAsync(ct);
    var profile = await CommercialDocumentService.GetProfileAsync(db, actor, ct);
    if (!profile.Succeeded) return CommandResult<CommercialDocumentWorkspace>.Fail(ErrorCodes.ScopeDenied, "Documents unavailable.");
    var p = profile.Value;
    var common = new List<string>();
    if (quotation is null || quotation.Status != "APPROVED" || quotation.Fee != proposal.Fee)
      common.Add("Approve the current quotation and ensure its fee matches the proposal.");
    if (p is null) common.Add("Configure the firm commercial profile before generating documents.");
    var letter = new List<string>(common);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Partner"], InternalOnly: true, RequireFirmWide: true), ct)).Succeeded)
      letter.Add("A current firm-wide Partner must generate the engagement letter.");
    if (proposal.Status != "ACCEPTED" || proposal.ResponseAt is null || proposal.PracticeClientId is null)
      letter.Add("Record client commercial acceptance and convert the proposal to a prospect client.");
    if (proposal.PracticeClientId is { } clientId)
    {
      var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == clientId, ct);
      var decision = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId
        && x.EngagementId == null && x.ServiceRoute == authorized.Value!.ServiceRoute && x.Decision != "Pending")
        .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
      if (safety is null || decision is null || decision.Decision != "Accepted" || decision.Generation != safety.InputGeneration
        || decision.DecidedByUserId is null || decision.DecidedAt is null)
        letter.Add("Complete the current acceptance checklist and record unconditional Partner risk clearance for this service.");
    }
    if (!await db.SignatureSpecimens.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null, ct)
      || !await db.FirmSealSpecimens.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId, ct))
      letter.Add("Register the approved Partner signature and official firm seal.");
    var tender = new List<string>(common);
    if (p is not null && new[] { p.FirmHistoryAndRegistrations, p.IndustryCredentials, p.AuditMethodology }.Any(string.IsNullOrWhiteSpace))
      tender.Add("Configure reviewed firm history, industry credentials and methodology chapters in Commercial settings.");
    // Only metadata is selected here; bytes remain behind the existing authorized download endpoint.
    var documents = await db.CommercialDocuments.AsNoTracking().Where(d => d.FirmId == actor.FirmId && d.ProposalId == proposalId)
      .OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id).Take(100)
      .Select(d => new { d.Id, d.QuotationVersionId, d.Kind, d.TemplateVersion, d.ProfileVersion, d.FileName, d.ContentType, d.Sha256Hex, d.CreatedAt }).ToListAsync(ct);
    if (!(await CommercialWorkspaceQuery.ProposalAsync(db, actor, proposalId, ct)).Succeeded)
      return CommandResult<CommercialDocumentWorkspace>.Fail(ErrorCodes.ScopeDenied, "Documents unavailable.");
    return CommandResult<CommercialDocumentWorkspace>.Ok(new(proposalId, quotation?.Id,
      p?.Version.ToString(CultureInfo.InvariantCulture), p?.LegalName, p?.FirmHistoryAndRegistrations, p?.IndustryCredentials, p?.AuditMethodology,
      common, letter, tender, documents.Select(d => new CommercialArtifact(d.Id, d.QuotationVersionId, d.Kind, d.TemplateVersion,
        d.ProfileVersion.ToString(CultureInfo.InvariantCulture), d.FileName, d.ContentType, d.Sha256Hex, d.CreatedAt)).ToArray()));
  }
}
