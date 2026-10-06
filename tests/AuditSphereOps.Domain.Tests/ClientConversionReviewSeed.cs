using AuditSphereOps.Application.Practice;
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
    var sent=await PracticeCrmService.SendProposalAsync(db,author,proposal.Value);
    if(!sent.Succeeded)throw new InvalidOperationException(sent.ErrorCode);
    var accepted=await PracticeCrmService.RecordProposalResponseAsync(db,author,proposal.Value,
      new("ACCEPTED",null,(await db.Proposals.AsNoTracking().SingleAsync(x=>x.Id==proposal.Value)).SentOfferSha256,"Primary contact","contact@example.test","Signed acceptance letter"));
    if(!accepted.Succeeded)throw new InvalidOperationException(accepted.ErrorCode);
    return proposal.Value;
  }
}
