using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Domain.Tests;
internal static class EngagementActivationReviewSeed
{
  /// <param name="advanceState">
  /// STE 4.1.5 / C-02: the activation gate requires a linked fee agreement whose 50% advance is fully paid, so the
  /// default seeds a <c>PAID</c> advance. Pass another milestone state to model an unpaid advance, or <c>null</c> to
  /// leave the engagement with no linked fee agreement at all.
  /// </param>
  internal static async Task<Guid> PopulateAsync(AuditSphereDbContext db,PbcSeed.Fixture f,string? advanceState=FeeMilestoneStates.Paid)
  {
    const long generation=9007199254740993;
    await db.Engagements.Where(e=>e.Id==f.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.Status,"Draft")
      .SetProperty(e=>e.ProfessionalWorkBlocked,true).SetProperty(e=>e.Generation,generation).SetProperty(e=>e.ServiceRoute,"FinancialStatementAudit")
      .SetProperty(e=>e.ServiceProfileId,"Synthetic annual audit").SetProperty(e=>e.PeriodStart,"2026-01-01").SetProperty(e=>e.PeriodEnd,"2026-12-31"));
    await db.ClientSafetyStates.Where(c=>c.Id==f.ClientId).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.InputGeneration,generation));
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Partner",f.ClientId,f.EngagementId));
    var id=Guid.NewGuid();db.AcceptanceDecisions.Add(new(){Id=id,FirmId=f.FirmId,PracticeClientId=f.ClientId,ServiceRoute="FinancialStatementAudit",
      Decision="Accepted",Path=AcceptancePaths.NewClient,Generation=generation,Rationale="Synthetic owner-approved test evidence",
      EvaluationTemplateVersion="synthetic.v1",EvaluationSnapshotDigest=new string('a',64),DecidedByUserId=f.Staff.Id,DecidedAt=DateTimeOffset.Parse("2026-10-01T12:00:00Z")});
    if(advanceState is not null) LinkFeeAgreement(db,f.FirmId,f.ClientId,f.EngagementId,f.Staff.Id,advanceState);
    await db.SaveChangesAsync();return id;
  }

  /// <summary>
  /// Stages the linked commercial chain the STE 4.1.5 / C-02 advance gate requires: lead → opportunity → accepted
  /// proposal → fee agreement → advance milestone. Rows are staged only, so the caller owns SaveChanges.
  /// </summary>
  internal static void LinkFeeAgreement(AuditSphereDbContext db,Guid firmId,Guid clientId,Guid engagementId,Guid userId,
    string advanceState,string serviceRoute="FinancialStatementAudit")
  {
    var leadId=Guid.NewGuid();
    db.Leads.Add(new Lead{Id=leadId,FirmId=firmId,Name="Synthetic lead",Source=LeadChannels.Email,Status=CrmStates.LeadQualified,
      CreatedAt=DateTimeOffset.UtcNow});
    var opportunityId=Guid.NewGuid();
    db.Opportunities.Add(new Opportunity{Id=opportunityId,FirmId=firmId,LeadId=leadId,PracticeClientId=clientId,
      ServiceRoute=serviceRoute,EntityScope="Synthetic entity",PeriodStart="2026-01-01",PeriodEnd="2026-12-31",
      ExpectedFee=20000m,Currency="QAR",Stage=CrmStates.OpportunityWon,CreatedAt=DateTimeOffset.UtcNow});
    var proposalId=Guid.NewGuid();
    var now=DateTimeOffset.UtcNow;
    var offerBytes=System.Text.Encoding.UTF8.GetBytes("Synthetic approved offer for activation fixture.");
    var offerHash=Hashing.Sha256Hex(offerBytes);
    var quotationId=Guid.NewGuid();
    var dispatchId=Guid.NewGuid();
    var documentId=Guid.NewGuid();
    db.Proposals.Add(new Proposal{Id=proposalId,FirmId=firmId,OpportunityId=opportunityId,PracticeClientId=clientId,Revision=1,
      Status=CrmStates.ProposalAccepted,ServiceProfileId="Synthetic annual audit",Scope="Synthetic engagement scope",
      Deliverables="Synthetic engagement deliverables",PeriodStart="2026-01-01",PeriodEnd="2026-12-31",Fee=20000m,Currency="QAR",
      SentAt=now,SentOfferSha256=offerHash,ResponseAt=now,ResponseOfferSha256=offerHash,RespondentName="Synthetic owner",
      RespondentEmail="owner@example.test",ResponseEvidenceReference="Synthetic signed acceptance",ResponseChannel=ProposalResponseChannels.Email,
      CreatedAt=now});
    db.QuotationVersions.Add(new QuotationVersion{Id=quotationId,FirmId=firmId,ProposalId=proposalId,Revision=1,Currency="QAR",
      LinesJson="[]",ComplexityFactor=1m,BaseAmount=20000m,Fee=20000m,InputHash=new string('a',64),Status=QuotationStates.Approved,
      RequiredApprovalsJson="[]",CreatedByUserId=userId,CreatedAt=now,ApprovedAt=now});
    db.CommercialDocuments.Add(new CommercialDocument{Id=documentId,FirmId=firmId,ProposalId=proposalId,QuotationVersionId=quotationId,
      Kind=CommercialDocumentKinds.Quotation,TemplateVersion="synthetic-quotation-v1",ProfileVersion=1,FileName="synthetic-offer.docx",
      ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document",Bytes=offerBytes,Sha256Hex=offerHash,
      CreatedByUserId=userId,CreatedAt=now});
    db.CommercialNotifications.Add(new CommercialNotification{Id=dispatchId,FirmId=firmId,Kind=CommercialNotificationKinds.Proposal,
      ProposalId=proposalId,PracticeClientId=clientId,QuotationVersionId=quotationId,DocumentId=documentId,OfferSha256=offerHash,
      RecipientAuthorityBasis=ProposalRecipientAuthorityBases.ProspectPrimaryContact,Recipient="owner@example.test",
      Subject="Synthetic approved audit offer",Body="Synthetic offer dispatch for activation test.",CreatedAt=now});
    db.ProposalResponseRecords.Add(new ProposalResponseRecord{Id=Guid.NewGuid(),FirmId=firmId,ProposalId=proposalId,
      CommercialNotificationId=dispatchId,Decision=CrmStates.ProposalAccepted,OfferSha256=offerHash,DispatchRecipient="owner@example.test",
      AuthorityBasis=ProposalRecipientAuthorityBases.ProspectPrimaryContact,RespondentName="Synthetic owner",RespondentEmail="owner@example.test",
      EvidenceReference="Synthetic signed acceptance",Channel=ProposalResponseChannels.Email,RecordedByUserId=userId,RecordedAt=now});
    var agreementId=Guid.NewGuid();
    db.EngagementFeeAgreements.Add(new EngagementFeeAgreement{Id=agreementId,FirmId=firmId,ProposalId=proposalId,
      QuotationVersionId=quotationId,PracticeClientId=clientId,EngagementId=engagementId,Currency="QAR",AgreedFee=20000m,
      AdvancePercent=50m,CreatedByUserId=userId,CreatedAt=now});
    // ck_fee_milestone_values: PLANNED has no invoice, INVOICED has one, PAID has an invoice, a receipt and a paid time.
    db.FeeMilestones.Add(new FeeMilestone{Id=Guid.CreateVersion7(),FirmId=firmId,AgreementId=agreementId,
      Kind=FeeMilestoneKinds.Advance,Amount=10000m,State=advanceState,
      InvoiceId=advanceState==FeeMilestoneStates.Planned?null:Guid.CreateVersion7(),
      ReceiptId=advanceState==FeeMilestoneStates.Paid?Guid.CreateVersion7():null,
      PaidAt=advanceState==FeeMilestoneStates.Paid?DateTimeOffset.UtcNow:null,CreatedAt=DateTimeOffset.UtcNow});
  }
}
