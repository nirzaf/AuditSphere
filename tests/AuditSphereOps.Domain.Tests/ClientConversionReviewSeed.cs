using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal static class ClientConversionReviewSeed
{
  internal static async Task<Guid> PopulateAsync(AuditSphereDbContext db, PbcSeed.Fixture f)
  {
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"RelationshipManager")); await db.SaveChangesAsync();
    var author=PbcSeed.Actor(f.Staff,"RelationshipManager"); var reviewer=PbcSeed.Actor(f.Admin,"Administrator");
    var lead=await PracticeCrmService.CreateLeadAsync(db,author,new("Reviewed prospect","Referral","Primary contact","contact@example.test"));
    if(!lead.Succeeded)throw new InvalidOperationException(lead.ErrorCode);
    var qualified=await PracticeCrmService.QualifyLeadAsync(db,author,lead.Value);
    if(!qualified.Succeeded)throw new InvalidOperationException(qualified.ErrorCode);
    var opportunity=await PracticeCrmService.CreateOpportunityAsync(db,author,new(lead.Value,"AccountingOnly","PROSPECT", "2026-01-01","2026-12-31",12000m,"QAR",60m));
    if(!opportunity.Succeeded)throw new InvalidOperationException(opportunity.ErrorCode);
    var proposal=await PracticeCrmService.ReviseProposalAsync(db,author,new(opportunity.Value,"ACCOUNTING-2026","Monthly accounting","Tax filing","Trial balance","Client source ledgers",12000m,"QAR","2026-01-01","2026-12-31"));
    if(!proposal.Succeeded)throw new InvalidOperationException(proposal.ErrorCode);
    var approved=await PracticeCrmService.ApproveProposalAsync(db,reviewer,proposal.Value);
    if(!approved.Succeeded)throw new InvalidOperationException(approved.ErrorCode);
    await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, author, proposal.Value);
    var sent=await PracticeCrmService.SendProposalAsync(db,author,proposal.Value);
    if(!sent.Succeeded)throw new InvalidOperationException(sent.ErrorCode);
    var accepted=await PracticeCrmService.RecordProposalResponseAsync(db,author,proposal.Value,
      new("ACCEPTED",null,(await db.Proposals.AsNoTracking().SingleAsync(x=>x.Id==proposal.Value)).SentOfferSha256,"Primary contact","contact@example.test","Signed acceptance letter", Channel: AuditSphereOps.Domain.Practice.ProposalResponseChannels.Email));
    if(!accepted.Succeeded)throw new InvalidOperationException(accepted.ErrorCode);
    return proposal.Value;
  }
}

/// <summary>Creates an explicitly synthetic approved quotation artifact for tests focused on later commercial transitions.</summary>
internal static class ProposalDispatchTestSetup
{
  public static async Task EnsureCurrentQuotationArtifactAsync(AuditSphereDbContext db, ActorContext actor, Guid proposalId)
  {
    var proposal = await db.Proposals.AsNoTracking()
      .SingleAsync(x => x.FirmId == actor.FirmId && x.Id == proposalId);
    var quotation = await db.QuotationVersions.Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync();
    if (quotation is null)
    {
      quotation = new QuotationVersion
      {
        Id = Guid.NewGuid(), FirmId = actor.FirmId, ProposalId = proposalId, Revision = 1,
        Currency = proposal.Currency, LinesJson = "[]", ComplexityFactor = 1m, BaseAmount = Math.Max(1m, proposal.Fee),
        Fee = proposal.Fee, InputHash = new string('a', 64), Status = QuotationStates.Approved,
        RequiredApprovalsJson = "[]", CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow,
        ApprovedAt = DateTimeOffset.UtcNow, ValidUntil = DateTimeOffset.UtcNow.AddDays(30)
      };
      db.QuotationVersions.Add(quotation);
      await db.SaveChangesAsync();
    }
    if (quotation.Status != QuotationStates.Approved || quotation.Fee != proposal.Fee)
      throw new InvalidOperationException("The test must arrange a current approved quotation before proposal dispatch.");
    if (await db.CommercialDocuments.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.QuotationVersionId == quotation.Id && x.Kind == CommercialDocumentKinds.Quotation)) return;

    var profile = await db.FirmCommercialProfiles.Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
    if (profile is null)
    {
      profile = new FirmCommercialProfile
      {
        Id = Guid.NewGuid(), FirmId = actor.FirmId, Version = 1, LegalName = "Synthetic Test Firm",
        AccentColorHex = "#2B6CB0", CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      };
      db.FirmCommercialProfiles.Add(profile);
    }
    var bytes = CommercialDocumentRenderer.RenderDocx(new CommercialDocumentModel(profile,
      "Synthetic approved quotation", $"TEST-{quotation.Id:N}", DateOnly.FromDateTime(DateTime.UtcNow),
      "Synthetic test client", [new DocumentSection("Approved commercial terms", [
        $"Scope: {proposal.Scope}",
        $"Service period: {proposal.PeriodStart} through {proposal.PeriodEnd}",
        $"Approved fee: {quotation.Fee.ToString("N2", CultureInfo.InvariantCulture)} {quotation.Currency}",
        "Synthetic artifact bound to the approved quotation revision for a downstream workflow test."])],
      SignatureBlock: false));
    db.CommercialDocuments.Add(new CommercialDocument
    {
      Id = Guid.NewGuid(), FirmId = actor.FirmId, ProposalId = proposalId, QuotationVersionId = quotation.Id,
      Kind = CommercialDocumentKinds.Quotation, TemplateVersion = CommercialDocumentRenderer.QuotationTemplate,
      ProfileVersion = profile.Version, FileName = $"Quotation-TEST-{quotation.Id:N}.docx",
      ContentType = CommercialDocumentRenderer.DocxContentType, Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
  }
}
