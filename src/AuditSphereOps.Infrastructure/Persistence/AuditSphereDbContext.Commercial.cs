using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

// Commercial calculation, approval matrix, branded documents and the agreed-fee milestone cycle.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureCommercial(ModelBuilder b)
  {
    b.Entity<EngagementActivation>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId }).IsUnique();
      e.HasIndex(x => new { x.FirmId, x.ActivatedByUserId, x.RequestId }).IsUnique().HasFilter("request_id IS NOT NULL");
      e.Property(x => x.RequestHash).HasMaxLength(64);
      e.Property(x => x.ReviewBasis).HasMaxLength(64);
      e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AuditSphereOps.Domain.Acceptance.AcceptanceDecision>().WithMany()
        .HasForeignKey(x => new { x.FirmId, x.AcceptanceDecisionId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("engagement_activations", t =>
      {
        t.HasCheckConstraint("ck_engagement_activation_values", "client_generation >= 1 AND acceptance_path IN ('NEW_CLIENT','CONTINUANCE')");
        t.HasCheckConstraint("ck_engagement_activation_request", "(request_id IS NULL AND request_hash IS NULL AND review_basis IS NULL AND actor_epoch IS NULL AND engagement_generation IS NULL AND result_generation IS NULL) OR (request_id IS NOT NULL AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash IS NOT NULL AND review_basis IS NOT NULL AND actor_epoch IS NOT NULL AND engagement_generation IS NOT NULL AND result_generation IS NOT NULL AND request_hash ~ '^[a-f0-9]{64}$' AND review_basis ~ '^[a-f0-9]{64}$' AND actor_epoch >= 1 AND engagement_generation >= 1 AND result_generation > engagement_generation AND result_generation - engagement_generation = 1)");
      });
    });
    b.Entity<QuotationVersion>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ProposalId, x.Revision }).IsUnique();
      e.HasOne<Proposal>().WithMany().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Restrict);
      e.Property(x => x.LinesJson).HasColumnType("jsonb");
      e.Property(x => x.RequiredApprovalsJson).HasColumnType("jsonb");
      e.Property(x => x.ComplexityFactor).HasPrecision(9, 4);
      e.Property(x => x.RiskPremiumPercent).HasPrecision(9, 4);
      e.Property(x => x.DiscountPercent).HasPrecision(9, 4);
      e.ToTable("quotation_versions", t => t.HasCheckConstraint("ck_quotation_version_values",
        "revision >= 1 AND currency ~ '^[A-Z]{3}$' AND complexity_factor >= 0.5 AND complexity_factor <= 3 AND risk_premium_percent >= 0 AND risk_premium_percent <= 100 AND discount_percent >= 0 AND discount_percent <= 100 AND base_amount > 0 AND fee >= 0 AND length(input_hash) = 64 AND status IN ('DRAFT','PENDING_APPROVAL','APPROVED','SUPERSEDED') AND ((status = 'APPROVED') = (approved_at IS NOT NULL) OR status = 'SUPERSEDED')"));
    });
    b.Entity<CommercialApprovalRule>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.Kind, x.ThresholdPercent, x.Version }).IsUnique();
      e.Property(x => x.ThresholdPercent).HasPrecision(9, 4);
      e.ToTable("commercial_approval_rules", t => t.HasCheckConstraint("ck_commercial_rule_values",
        "kind IN ('DISCOUNT_OVER_PERCENT','NON_STANDARD_TERMS') AND version >= 1 AND length(required_role) > 0 AND ((kind = 'DISCOUNT_OVER_PERCENT' AND threshold_percent >= 0 AND threshold_percent < 100) OR (kind = 'NON_STANDARD_TERMS' AND threshold_percent IS NULL))"));
    });
    b.Entity<QuotationApproval>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.QuotationVersionId, x.RuleKey }).IsUnique();
      e.HasOne<QuotationVersion>().WithMany().HasForeignKey(x => x.QuotationVersionId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("quotation_approvals", t => t.HasCheckConstraint("ck_quotation_approval_values",
        "length(rule_key) > 0 AND length(required_role) > 0 AND length(reason) > 0"));
    });
    b.Entity<FirmCommercialProfile>(e =>
    {
      e.Property(x => x.FirmHistoryAndRegistrations).HasMaxLength(16000);
      e.Property(x => x.IndustryCredentials).HasMaxLength(16000);
      e.Property(x => x.AuditMethodology).HasMaxLength(16000);
      e.HasIndex(x => new { x.FirmId, x.Version }).IsUnique();
      e.ToTable("firm_commercial_profiles", t => t.HasCheckConstraint("ck_firm_commercial_profile_values",
        "version >= 1 AND length(legal_name) > 0 AND accent_color_hex ~ '^#[0-9A-Fa-f]{6}$'"));
    });
    b.Entity<CommercialDocument>(e =>
    {
      e.HasOne<AuditSphereOps.Domain.Completion.SignatureSpecimen>().WithMany().HasForeignKey(x => new { x.FirmId, x.SignatureSpecimenId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AuditSphereOps.Domain.Completion.FirmSealSpecimen>().WithMany().HasForeignKey(x => new { x.FirmId, x.FirmSealSpecimenId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AuditSphereOps.Domain.Acceptance.AcceptanceDecision>().WithMany()
        .HasForeignKey(x => new { x.FirmId, x.AcceptanceDecisionId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.HasIndex(x => new { x.FirmId, x.ProposalId, x.Kind, x.CreatedAt });
      e.HasIndex(x => new { x.FirmId, x.QuotationVersionId, x.Kind }).IsUnique().HasFilter("kind IN ('QUOTATION','ENGAGEMENT_LETTER','COMPREHENSIVE_PROPOSAL')");
      e.HasIndex(x => new { x.FirmId, x.FeeMilestoneId, x.Kind }).IsUnique().HasFilter("kind = 'PAYMENT_RECEIPT'");
      e.ToTable("commercial_documents", t => t.HasCheckConstraint("ck_commercial_document_values",
        "kind IN ('QUOTATION','ENGAGEMENT_LETTER','PAYMENT_RECEIPT','COMPREHENSIVE_PROPOSAL') AND length(template_version) > 0 AND length(sha256_hex) = 64 AND octet_length(bytes) > 0 AND length(file_name) > 0"));
    });
    b.Entity<EngagementFeeAgreement>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ProposalId }).IsUnique();
      e.HasOne<Proposal>().WithMany().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Restrict);
      e.Property(x => x.AdvancePercent).HasPrecision(9, 4);
      e.ToTable("engagement_fee_agreements", t => t.HasCheckConstraint("ck_fee_agreement_values",
        "currency ~ '^[A-Z]{3}$' AND agreed_fee > 0 AND advance_percent > 0 AND advance_percent < 100"));
    });
    b.Entity<FeeMilestone>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.AgreementId, x.Kind }).IsUnique();
      e.HasOne<EngagementFeeAgreement>().WithMany().HasForeignKey(x => x.AgreementId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("fee_milestones", t => t.HasCheckConstraint("ck_fee_milestone_values",
        "kind IN ('ADVANCE','BALANCE') AND amount > 0 AND state IN ('PLANNED','INVOICED','PAID') AND ((state = 'PLANNED' AND invoice_id IS NULL) OR (state <> 'PLANNED' AND invoice_id IS NOT NULL)) AND ((state = 'PAID') = (receipt_id IS NOT NULL AND paid_at IS NOT NULL))"));
    });
    b.Entity<CommercialNotification>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.FeeMilestoneId }).IsUnique();
      e.HasIndex(x => new { x.FirmId, x.DeliveryState, x.CreatedAt });
      e.HasIndex(x => new { x.FirmId, x.ProposalId }).IsUnique().HasFilter("proposal_id IS NOT NULL");
      e.ToTable("commercial_notifications", t => t.HasCheckConstraint("ck_commercial_notification_values",
        "delivery_state IN ('QUEUED','SENT','FAILED') AND kind IN ('RECEIPT','PROPOSAL') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state = 'SENT') = (delivered_at IS NOT NULL)) AND " +
        "((kind = 'RECEIPT') = (fee_milestone_id IS NOT NULL)) AND ((kind = 'PROPOSAL') = (proposal_id IS NOT NULL))"));
    });
  }
}
