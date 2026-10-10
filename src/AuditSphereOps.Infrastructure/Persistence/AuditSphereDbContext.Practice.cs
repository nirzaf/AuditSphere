using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Microsoft365;

namespace AuditSphereOps.Infrastructure.Persistence;

// DbContext: one owner, snake_case, composite (firm,client,engagement) FK discipline (§§27, 42).
// Money decimal(19,6); IDs uuid v7-compatible; immutable TB rows have no update path.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigurePractice(ModelBuilder b)
  {
    b.Entity<PracticeClient>().HasIndex(x => new { x.FirmId, x.LegalName }).IsUnique();
    b.Entity<WorkTask>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_work_tasks_firm_id_id");
    b.Entity<TimeEntry>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_time_entries_firm_id_id");
    b.Entity<RateCardVersion>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_rate_cards_firm_id_id");
    b.Entity<EngagementBudget>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_budgets_firm_id_id");
    b.Entity<BudgetLine>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_budget_lines_firm_id_id");
    b.Entity<BillingAccount>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_billing_accounts_firm_id_id");
    b.Entity<FirmFinanceProfile>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_finance_profiles_firm_id_id");
    b.Entity<Invoice>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_invoices_firm_id_id");
    b.Entity<InvoicePaymentTermsRevision>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_invoice_payment_terms_firm_id_id");
    b.Entity<InvoiceLine>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_invoice_lines_firm_id_id");
    b.Entity<Receipt>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_receipts_firm_id_id");
    b.Entity<ReceiptAllocation>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_receipt_allocations_firm_id_id");
    b.Entity<ReceiptAllocationReversal>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_receipt_allocation_reversals_firm_id_id");
    b.Entity<CreditNote>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_credit_notes_firm_id_id");
    b.Entity<BillingSourceAllocation>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_billing_sources_firm_id_id");
    b.Entity<FirmAccount>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_accounts_firm_id_id");
    b.Entity<FirmPeriod>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_periods_firm_id_id");
    b.Entity<FirmJournal>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_journals_firm_id_id");
    b.Entity<FirmJournalLine>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_journal_lines_firm_id_id");
    b.Entity<FirmPosting>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_postings_firm_id_id");
    b.Entity<FirmPostingLine>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_posting_lines_firm_id_id");
    b.Entity<LedgerSourceLink>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_ledger_sources_firm_id_id");
    b.Entity<LedgerPostingReceipt>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_ledger_receipts_firm_id_id");
    b.Entity<PeriodCloseDecision>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_period_decisions_firm_id_id");
    b.Entity<Lead>().HasIndex(x => new { x.FirmId, x.Status, x.CreatedAt });
    b.Entity<Opportunity>().HasIndex(x => new { x.FirmId, x.LeadId, x.Stage });
    b.Entity<Proposal>().HasIndex(x => new { x.FirmId, x.OpportunityId, x.Revision }).IsUnique();
    b.Entity<WorkTask>().HasIndex(x => new { x.FirmId, x.Status, x.CreatedAt });
    b.Entity<WorkTask>().HasIndex(x => new { x.FirmId, x.ClientId, x.ReportingPeriodId, x.Status, x.DueDate });
    b.Entity<TimeEntry>().HasIndex(x => new { x.FirmId, x.UserId, x.WorkDate, x.Status });
    b.Entity<RateCardVersion>().HasIndex(x => new { x.FirmId, x.Role, x.Activity, x.Currency, x.Version }).IsUnique();
    b.Entity<EngagementBudget>().HasIndex(x => new { x.FirmId, x.EngagementId, x.Version }).IsUnique();
    b.Entity<BudgetLine>().HasIndex(x => new { x.FirmId, x.EngagementBudgetId, x.Role, x.Activity, x.Phase, x.RiskArea }).IsUnique()
      .AreNullsDistinct(false).HasDatabaseName("IX_budget_lines_firm_budget_role_activity_phase_area");
    b.Entity<BillingAccount>().HasIndex(x => new { x.FirmId, x.PracticeClientId }).IsUnique();
    b.Entity<FirmFinanceProfile>().HasIndex(x => x.FirmId).IsUnique();
    b.Entity<Invoice>().HasIndex(x => new { x.FirmId, x.InvoiceNumber }).IsUnique();
    b.Entity<InvoicePaymentTermsRevision>().HasIndex(x => new { x.FirmId, x.InvoiceId, x.Revision }).IsUnique();
    b.Entity<InvoicePaymentTermsRevision>().HasIndex(x => new { x.FirmId, x.InvoiceId })
      .HasFilter("status = 'PENDING_REVIEW'").IsUnique();
    b.Entity<InvoiceLine>().HasIndex(x => new { x.FirmId, x.InvoiceId });
    b.Entity<Receipt>().HasIndex(x => new { x.FirmId, x.BillingAccountId, x.ReceivedAt });
    b.Entity<ReceiptAllocation>().HasIndex(x => new { x.FirmId, x.ReceiptId, x.InvoiceId }).IsUnique();
    b.Entity<ReceiptAllocationReversal>().HasIndex(x => new { x.FirmId, x.ReceiptAllocationId, x.Revision }).IsUnique();
    b.Entity<ReceiptAllocationReversal>().HasIndex(x => new { x.FirmId, x.ReceiptAllocationId })
      .HasFilter("status = 'PENDING_REVIEW'").IsUnique();
    b.Entity<CreditNote>().HasIndex(x => new { x.FirmId, x.NoteNumber }).IsUnique();
    b.Entity<BillingSourceAllocation>().HasIndex(x => new { x.FirmId, x.SourceKind, x.SourceId, x.SourceRevision }).IsUnique();
    b.Entity<FirmAccount>().HasIndex(x => new { x.FirmId, x.Code }).IsUnique();
    b.Entity<FirmJournal>().HasIndex(x => new { x.FirmId, x.JournalNumber }).IsUnique();
    b.Entity<FirmJournal>().HasIndex(x => new { x.FirmId, x.SourceKind, x.SourceKey, x.SourceRevision, x.PostingPurpose }).IsUnique();
    b.Entity<FirmPeriod>().HasIndex(x => new { x.FirmId, x.PeriodCode }).IsUnique();
    b.Entity<FirmJournalLine>().HasIndex(x => new { x.FirmId, x.JournalId });
    b.Entity<FirmPosting>().HasIndex(x => new { x.FirmId, x.JournalId }).IsUnique();
    b.Entity<FirmPostingLine>().HasIndex(x => new { x.FirmId, x.PostingId });
    b.Entity<LedgerSourceLink>().HasIndex(x => new { x.FirmId, x.SourceKind, x.SourceKey, x.SourceRevision, x.PostingPurpose }).IsUnique();
    b.Entity<LedgerPostingReceipt>().HasIndex(x => new { x.FirmId, x.RequestKey }).IsUnique();
    b.Entity<PeriodCloseDecision>().HasIndex(x => new { x.FirmId, x.PeriodId, x.DecisionKind, x.DecidedAt });
    b.Entity<Lead>().ToTable("leads", t => t.HasCheckConstraint("ck_lead_status",
      "status IN ('NEW','QUALIFIED','UNQUALIFIED','LOST') AND length(name) > 0 AND length(source) > 0"));
    b.Entity<Opportunity>().ToTable("opportunities", t => t.HasCheckConstraint("ck_opportunity_state",
      "stage IN ('DISCOVERY','PROPOSAL','NEGOTIATION','WON','LOST') AND length(service_route) > 0 AND length(entity_scope) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND expected_fee >= 0 AND (probability IS NULL OR probability BETWEEN 0 AND 100) AND currency ~ '^[A-Z]{3}$'"));
    b.Entity<Proposal>().Property(x => x.CreateRequestHash).HasMaxLength(64);
    b.Entity<Proposal>().ToTable("proposals", t =>
    {
      t.HasCheckConstraint("ck_proposal_state",
        "status IN ('DRAFT','INTERNAL_REVIEW','SENT','ACCEPTED','DECLINED','SUPERSEDED') AND revision >= 1");
      t.HasCheckConstraint("ck_proposal_content",
        "length(service_profile_id) > 0 AND length(scope) > 0 AND length(deliverables) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND fee >= 0 AND currency ~ '^[A-Z]{3}$'");
      t.HasCheckConstraint("ck_proposal_create_request_hash",
        "create_request_hash IS NULL OR create_request_hash ~ '^[a-f0-9]{64}$'");
    });
    b.Entity<PracticeClient>().Property(x => x.TaxRegistrationNumber).HasMaxLength(64);
    b.Entity<PracticeClient>().Property(x => x.EntityType).HasMaxLength(64);
    b.Entity<PracticeClient>().ToTable("practice_clients", t => t.HasCheckConstraint("ck_practice_client_name",
      "length(legal_name) > 0"));
    b.Entity<ClientContact>().Property(x => x.Phone).HasMaxLength(32);
    b.Entity<ClientContact>().Property(x => x.Title).HasMaxLength(100);
    b.Entity<ClientContact>().Property(x => x.SignatoryAuthority).HasMaxLength(64);
    b.Entity<ClientContact>().ToTable("client_contacts", t => t.HasCheckConstraint("ck_client_contact",
      "length(full_name) > 0 AND length(email) > 0 AND length(role) > 0 AND (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from)"));
    b.Entity<ClientRelationship>().ToTable("client_relationships", t =>
    {
      t.HasCheckConstraint("ck_client_relationship_parties", "primary_client_id <> related_client_id");
      t.HasCheckConstraint("ck_client_relationship_kind", "relationship_kind IN ('PARENT','SUBSIDIARY','AFFILIATE')");
      t.HasCheckConstraint("ck_client_relationship_ownership", "ownership_percentage IS NULL OR (ownership_percentage >= 0 AND ownership_percentage <= 100)");
      t.HasCheckConstraint("ck_client_relationship_effective", "effective_to IS NULL OR effective_from IS NULL OR effective_to >= effective_from");
    });
    b.Entity<ClientRelationship>().HasIndex(x => new { x.FirmId, x.PrimaryClientId, x.RelatedClientId, x.RelationshipKind })
      .HasFilter("revoked_at IS NULL").IsUnique();
    b.Entity<ClientRelationship>().HasIndex(x => new { x.FirmId, x.RelatedClientId });
    b.Entity<ClientContactRouting>().ToTable("client_contact_routings", t =>
    {
      t.HasCheckConstraint("ck_client_contact_routing_purpose", "purpose IN ('COMMERCIAL','FINANCE','AUDIT_FIELDWORK','COMPLETION')");
      t.HasCheckConstraint("ck_client_contact_routing_effective", "effective_to IS NULL OR effective_from IS NULL OR effective_to >= effective_from");
    });
    b.Entity<ClientContactRouting>().HasIndex(x => new { x.FirmId, x.PracticeClientId, x.Purpose, x.ClientContactId })
      .HasFilter("revoked_at IS NULL");
    b.Entity<ClientContactRouting>().HasIndex(x => new { x.FirmId, x.PracticeClientId, x.Purpose })
      .HasFilter("revoked_at IS NULL AND is_primary_for_purpose = true");
    b.Entity<CorrespondenceDispatchRecord>().ToTable("correspondence_dispatch_records", t =>
    {
      t.HasCheckConstraint("ck_correspondence_dispatch_purpose", "purpose IN ('COMMERCIAL','FINANCE','AUDIT_FIELDWORK','COMPLETION')");
      t.HasCheckConstraint("ck_correspondence_dispatch_content", "length(recipient_name) > 0 AND length(recipient_email) > 0 AND length(document_type) > 0 AND length(document_reference) > 0 AND length(document_sha256) = 64 AND document_revision >= 1");
    });
    b.Entity<CorrespondenceDispatchRecord>().HasIndex(x => new { x.FirmId, x.PracticeClientId, x.DispatchedAt });
    b.Entity<CorrespondenceDispatchRecord>().HasIndex(x => new { x.FirmId, x.EngagementId });
    b.Entity<WorkTask>().Property(x => x.FsliCode).HasMaxLength(100);
    b.Entity<WorkTask>().ToTable("work_tasks", t =>
    {
      t.HasCheckConstraint("ck_work_task_state",
        "status IN ('OPEN','IN_PROGRESS','COMPLETED','CANCELLED') AND length(title) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL) AND (reporting_period_id IS NULL OR client_id IS NOT NULL)");
      t.HasCheckConstraint("ck_work_task_fsli_snapshot",
        "(mapping_version_id IS NULL AND fsli_code IS NULL) OR (mapping_version_id IS NOT NULL AND client_id IS NOT NULL AND engagement_id IS NOT NULL AND fsli_code IS NOT NULL AND length(trim(fsli_code)) > 0)");
    });
    b.Entity<TimeEntry>().Property(x => x.FsliCode).HasMaxLength(100);
    b.Entity<TimeEntry>().ToTable("time_entries", t =>
    {
      t.HasCheckConstraint("ck_time_entry_state",
        "status IN ('DRAFT','SUBMITTED','APPROVED','SUPERSEDED') AND revision >= 1 AND start_minute BETWEEN 0 AND 1439 AND duration_minutes BETWEEN 1 AND 1440 AND start_minute + duration_minutes <= 1440");
      t.HasCheckConstraint("ck_time_entry_content",
        "length(role) > 0 AND length(activity) > 0 AND billable_classification IN ('BILLABLE','NON_BILLABLE','NO_CHARGE') AND narrative_visibility IN ('INTERNAL','CLIENT_VISIBLE') AND ((billable_classification = 'BILLABLE' AND rate_card_version_id IS NOT NULL AND rate_per_hour IS NOT NULL AND rate_per_hour >= 0) OR billable_classification <> 'BILLABLE')");
      t.HasCheckConstraint("ck_time_entry_fsli_snapshot",
        "(mapping_version_id IS NULL AND fsli_code IS NULL) OR (mapping_version_id IS NOT NULL AND client_id IS NOT NULL AND engagement_id IS NOT NULL AND fsli_code IS NOT NULL AND length(trim(fsli_code)) > 0)");
    });
    b.Entity<RateCardVersion>().ToTable("rate_card_versions", t => t.HasCheckConstraint("ck_rate_card_state",
      "version >= 1 AND length(role) > 0 AND length(activity) > 0 AND currency ~ '^[A-Z]{3}$' AND rate_per_hour >= 0 AND status IN ('DRAFT','APPROVED','SUPERSEDED')"));
    b.Entity<EngagementBudget>().ToTable("engagement_budgets", t => t.HasCheckConstraint("ck_budget_state",
      "version >= 1 AND currency ~ '^[A-Z]{3}$' AND status IN ('DRAFT','APPROVED','SUPERSEDED')"));
    b.Entity<BudgetLine>().ToTable("budget_lines", t => t.HasCheckConstraint("ck_budget_line_values",
      "length(role) > 0 AND length(activity) > 0 AND forecast_minutes > 0 AND rate_per_hour >= 0 AND forecast_cost >= 0"));
    b.Entity<BillingAccount>().ToTable("billing_accounts", t => t.HasCheckConstraint("ck_billing_account_currency",
      "currency ~ '^[A-Z]{3}$'"));
    b.Entity<FirmFinanceProfile>().ToTable("firm_finance_profiles", t =>
    {
      t.HasCheckConstraint("ck_finance_profile_currency", "functional_currency ~ '^[A-Z]{3}$'");
      t.HasCheckConstraint("ck_finance_profile_kind", "profile_kind IN ('TEST','PRODUCTION')");
      t.HasCheckConstraint("ck_finance_profile_approval", "(approved = false AND approved_at IS NULL AND approved_by_user_id IS NULL) OR (approved = true AND approved_at IS NOT NULL AND approved_by_user_id IS NOT NULL)");
    });
    b.Entity<Invoice>().ToTable("invoices", t =>
    {
      t.HasCheckConstraint("ck_invoice_state", "status IN ('DRAFT','REVIEW_REQUIRED','APPROVED','POSTED','SENT','CANCELLED') AND revision >= 1");
      t.HasCheckConstraint("ck_invoice_values", "length(invoice_number) > 0 AND (currency IS NULL OR currency ~ '^[A-Z]{3}$') AND subtotal >= 0 AND tax >= 0 AND total >= 0 AND total = subtotal + tax");
      t.HasCheckConstraint("ck_invoice_cancel", "(status = 'CANCELLED' AND cancelled_at IS NOT NULL AND length(cancellation_reason) > 0) OR status <> 'CANCELLED'");
    });
    b.Entity<InvoicePaymentTermsRevision>().ToTable("invoice_payment_terms_revisions", t =>
    {
      t.HasCheckConstraint("ck_invoice_payment_terms_values",
        "revision >= 1 AND basis IN ('CONTRACTUAL_DUE_DATE','REVIEWED_TERMS_SNAPSHOT') AND length(terms_description) > 0 AND length(evidence_reference) > 0 AND status IN ('PENDING_REVIEW','APPROVED','REJECTED')");
      t.HasCheckConstraint("ck_invoice_payment_terms_review",
        "(status = 'PENDING_REVIEW' AND reviewed_by_user_id IS NULL AND reviewed_at IS NULL AND review_reason IS NULL) OR (status = 'APPROVED' AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND length(review_reason) > 0) OR (status = 'REJECTED' AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND length(review_reason) > 0)");
    });
    b.Entity<InvoiceLine>().ToTable("invoice_lines", t => t.HasCheckConstraint("ck_invoice_line_values",
      "length(description) > 0 AND quantity > 0 AND unit_price >= 0 AND line_total >= 0 AND line_total = round(quantity * unit_price, 6) AND ((length(source_kind) = 0 AND source_id IS NULL AND source_revision IS NULL) OR (length(source_kind) > 0 AND source_id IS NOT NULL AND source_revision >= 1))"));
    b.Entity<Receipt>().ToTable("receipts", t => t.HasCheckConstraint("ck_receipt_values",
      "amount > 0 AND (currency IS NULL OR currency ~ '^[A-Z]{3}$') AND length(reference) > 0 AND status = 'RECORDED'"));
    b.Entity<ReceiptAllocation>().ToTable("allocations", t => t.HasCheckConstraint("ck_receipt_allocation_amount",
      "amount > 0"));
    b.Entity<ReceiptAllocationReversal>().ToTable("receipt_allocation_reversals", t =>
    {
      t.HasCheckConstraint("ck_receipt_allocation_reversal_values",
        "revision >= 1 AND amount > 0 AND length(reference) > 0 AND length(reason) > 0 AND status IN ('PENDING_REVIEW','APPROVED','REJECTED')");
      t.HasCheckConstraint("ck_receipt_allocation_reversal_review",
        "(status = 'PENDING_REVIEW' AND reviewed_by_user_id IS NULL AND reviewed_at IS NULL AND review_reason IS NULL) OR (status IN ('APPROVED','REJECTED') AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND length(review_reason) > 0)");
    });
    b.Entity<CreditNote>().ToTable("credit_notes", t => t.HasCheckConstraint("ck_credit_note_values",
      "length(note_number) > 0 AND currency ~ '^[A-Z]{3}$' AND amount > 0 AND length(reason) > 0 AND status = 'ISSUED'"));
    b.Entity<BillingSourceAllocation>().ToTable("billing_source_allocations", t => t.HasCheckConstraint("ck_billing_source_values",
      "length(source_kind) > 0 AND source_revision >= 1 AND quantity > 0 AND amount > 0"));
    b.Entity<FirmAccount>().ToTable("firm_accounts", t => t.HasCheckConstraint("ck_firm_account_values",
      "length(code) > 0 AND length(name) > 0 AND account_type IN ('ASSET','LIABILITY','EQUITY','REVENUE','EXPENSE') AND normal_side IN ('DEBIT','CREDIT')"));
    b.Entity<FirmPeriod>().ToTable("firm_periods", t =>
    {
      t.HasCheckConstraint("ck_firm_period_values", "period_code ~ '^[0-9]{4}-(0[1-9]|1[0-2])$' AND status IN ('OPEN','CLOSED','REOPEN_REQUESTED') AND revision >= 1 AND ((status = 'OPEN' AND closed_at IS NULL) OR (status IN ('CLOSED','REOPEN_REQUESTED') AND closed_at IS NOT NULL))");
    });
    b.Entity<FirmJournal>().Property(x => x.SupportingEvidenceFileName).HasMaxLength(255);
    b.Entity<FirmJournal>().Property(x => x.SupportingEvidenceContentType).HasMaxLength(255);
    b.Entity<FirmJournal>().Property(x => x.SupportingEvidenceSha256).HasMaxLength(64);
    b.Entity<FirmJournal>().ToTable("firm_journals", t =>
    {
      t.HasCheckConstraint("ck_firm_journal_values",
        "length(journal_number) > 0 AND length(source_kind) > 0 AND length(source_key) > 0 AND source_revision >= 1 AND length(posting_purpose) > 0 AND currency ~ '^[A-Z]{3}$' AND status IN ('DRAFT','REVIEW_REQUIRED','APPROVED','POSTED')");
      t.HasCheckConstraint("ck_firm_journal_supporting_evidence",
        "(supporting_evidence_content IS NULL AND supporting_evidence_file_name IS NULL AND supporting_evidence_content_type IS NULL AND supporting_evidence_sha256 IS NULL AND supporting_evidence_uploaded_by_user_id IS NULL AND supporting_evidence_uploaded_at IS NULL) OR (supporting_evidence_content IS NOT NULL AND octet_length(supporting_evidence_content) BETWEEN 1 AND 5242880 AND length(supporting_evidence_file_name) BETWEEN 1 AND 255 AND length(supporting_evidence_content_type) BETWEEN 1 AND 255 AND supporting_evidence_sha256 ~ '^[0-9a-f]{64}$' AND supporting_evidence_uploaded_by_user_id IS NOT NULL AND supporting_evidence_uploaded_at IS NOT NULL)");
    });
    b.Entity<FirmJournalLine>().ToTable("firm_journal_lines", t => t.HasCheckConstraint("ck_firm_journal_line_values",
      "length(description) > 0 AND debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))"));
    b.Entity<FirmPosting>().ToTable("firm_postings", t => t.HasCheckConstraint("ck_firm_posting_currency",
      "currency ~ '^[A-Z]{3}$'"));
    b.Entity<FirmPostingLine>().ToTable("firm_posting_lines", t => t.HasCheckConstraint("ck_firm_posting_line_values",
      "debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))"));
    b.Entity<LedgerSourceLink>().ToTable("ledger_source_links", t => t.HasCheckConstraint("ck_ledger_source_values",
      "length(source_kind) > 0 AND length(source_key) > 0 AND source_revision >= 1 AND length(posting_purpose) > 0"));
    b.Entity<LedgerPostingReceipt>().ToTable("ledger_posting_receipts", t => t.HasCheckConstraint("ck_ledger_receipt_values",
      "length(request_key) > 0 AND request_digest ~ '^[0-9a-f]{64}$'"));
    b.Entity<PeriodCloseDecision>().ToTable("period_close_decisions", t => t.HasCheckConstraint("ck_period_decision_values",
      "decision_kind IN ('CLOSE','REOPEN') AND length(reason) > 0"));
    b.Entity<Lead>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.OwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Opportunity>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.OwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Proposal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Proposal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PreparedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Opportunity>().HasOne<Lead>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.LeadId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Opportunity>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Proposal>().HasOne<Opportunity>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.OpportunityId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Proposal>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ClientContact>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkTask>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkTask>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkTask>().HasOne<MappingVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.MappingVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkTask>().HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.ReportingPeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkTask>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AssigneeUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<WorkTask>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TaskId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<MappingVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.MappingVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.UserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<RateCardVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RateCardVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<TimeEntry>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.SupersedesId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RateCardVersion>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RateCardVersion>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<EngagementBudget>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<EngagementBudget>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<EngagementBudget>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<BudgetLine>().HasOne<EngagementBudget>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementBudgetId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<BudgetLine>().HasOne<RateCardVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RateCardVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<BillingAccount>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmFinanceProfile>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Invoice>().HasOne<BillingAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.BillingAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<InvoicePaymentTermsRevision>().HasOne<Invoice>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.InvoiceId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<InvoicePaymentTermsRevision>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.SubmittedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<InvoicePaymentTermsRevision>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Invoice>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Invoice>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<InvoiceLine>().HasOne<Invoice>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.InvoiceId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Receipt>().HasOne<BillingAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.BillingAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Receipt>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RecordedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ReceiptAllocation>().HasOne<Receipt>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReceiptId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ReceiptAllocation>().HasOne<Invoice>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.InvoiceId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ReceiptAllocationReversal>().HasOne<ReceiptAllocation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReceiptAllocationId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ReceiptAllocationReversal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.SubmittedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ReceiptAllocationReversal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<CreditNote>().HasOne<BillingAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.BillingAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<CreditNote>().HasOne<Invoice>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.InvoiceId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<CreditNote>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<BillingSourceAllocation>().HasOne<InvoiceLine>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.InvoiceLineId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournal>().HasOne<FirmPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.SupportingEvidenceUploadedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournalLine>().HasOne<FirmJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournalLine>().HasOne<FirmAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.FirmAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPosting>().HasOne<FirmPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPosting>().HasOne<FirmJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPosting>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PostedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPosting>().HasOne<FirmPosting>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReversalOfPostingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPostingLine>().HasOne<FirmPosting>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PostingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPostingLine>().HasOne<FirmAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.FirmAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<LedgerSourceLink>().HasOne<FirmPosting>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PostingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<LedgerPostingReceipt>().HasOne<FirmPosting>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PostingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<PeriodCloseDecision>().HasOne<FirmPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<PeriodCloseDecision>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.DecidedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
