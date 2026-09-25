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
  private static void ConfigureClientAccountingSchedules(ModelBuilder b)
  {
    var reconciliation = b.Entity<AccountingReconciliation>();
    reconciliation.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).HasName("ak_accounting_reconciliations_scope_id");
    reconciliation.Property(x => x.Area).HasMaxLength(80);
    reconciliation.Property(x => x.AccountSelection).HasMaxLength(2000);
    reconciliation.Property(x => x.AgingBasis).HasMaxLength(30);
    reconciliation.Property(x => x.AgingBucketRuleVersion).HasMaxLength(60);
    reconciliation.Property(x => x.SourceHash).HasMaxLength(64);
    reconciliation.Property(x => x.Status).HasMaxLength(30);
    reconciliation.HasIndex(x => new { x.FirmId, x.EngagementId, x.PeriodId, x.Area }).HasDatabaseName("ix_accounting_reconciliation_area");
    reconciliation.ToTable("accounting_reconciliations", t => t.HasCheckConstraint("ck_accounting_reconciliation_values",
      "length(trim(area)) > 0 AND length(trim(account_selection)) > 0"));
    ScopeToEngagement(reconciliation, nameof(AccountingReconciliation.FirmId), nameof(AccountingReconciliation.ClientId), nameof(AccountingReconciliation.EngagementId));

    var reconProof = b.Entity<AccountingReconciliationProof>();
    reconProof.Property(x => x.FormulaVersion).HasMaxLength(40);
    reconProof.Property(x => x.ItemManifestDigest).HasMaxLength(64);
    reconProof.Property(x => x.SourceHash).HasMaxLength(64);
    reconProof.HasIndex(x => new { x.FirmId, x.ReconciliationId, x.CreatedAt }).HasDatabaseName("ix_accounting_reconciliation_proof");
    reconProof.ToTable("accounting_reconciliation_proofs", t => t.HasCheckConstraint("ck_accounting_reconciliation_proof_values",
      "length(trim(formula_version)) > 0 AND item_manifest_digest ~ '^[0-9a-f]{64}$' AND item_count >= 0"));
    reconProof.HasOne<AccountingReconciliation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    ScopeToEngagement(reconProof, nameof(AccountingReconciliationProof.FirmId), nameof(AccountingReconciliationProof.ClientId), nameof(AccountingReconciliationProof.EngagementId));

    var reconItem = b.Entity<AccountingReconciliationItem>();
    reconItem.Property(x => x.StableItemId).HasMaxLength(200);
    reconItem.Property(x => x.Currency).HasMaxLength(3);
    reconItem.Property(x => x.DateBasis).HasMaxLength(30);
    reconItem.Property(x => x.AgingBucket).HasMaxLength(30);
    reconItem.Property(x => x.SettlementReference).HasMaxLength(2000);
    reconItem.Property(x => x.Reason).HasMaxLength(1000);
    reconItem.Property(x => x.EvidenceReference).HasMaxLength(2000);
    reconItem.Property(x => x.Disposition).HasMaxLength(100);
    reconItem.HasIndex(x => new { x.FirmId, x.ReconciliationId, x.StableItemId }).IsUnique()
      .HasDatabaseName("ux_accounting_reconciliation_item");
    ScopeToEngagement(reconItem, nameof(AccountingReconciliationItem.FirmId), nameof(AccountingReconciliationItem.ClientId), nameof(AccountingReconciliationItem.EngagementId));
    reconItem.HasOne<AccountingReconciliation>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var ecl = b.Entity<EclAssessment>();
    ecl.Property(x => x.Method).HasMaxLength(100);
    ecl.Property(x => x.MethodologyVersion).HasMaxLength(100);
    ecl.Property(x => x.ReconciliationSourceHash).HasMaxLength(64);
    ecl.Property(x => x.AssumptionsHash).HasMaxLength(64);
    ecl.Property(x => x.Status).HasMaxLength(30);
    ecl.HasIndex(x => new { x.FirmId, x.ReconciliationId, x.Version }).IsUnique().HasDatabaseName("ux_ecl_assessment_version");
    ecl.ToTable("ecl_assessments", t => t.HasCheckConstraint("ck_ecl_assessment_values",
      "length(trim(method)) > 0 AND length(trim(methodology_version)) > 0 AND eligible_exposure >= 0 AND probability_of_default BETWEEN 0 AND 1 AND loss_given_default BETWEEN 0 AND 1 AND booked_amount >= 0"));
    ScopeToEngagement(ecl, nameof(EclAssessment.FirmId), nameof(EclAssessment.ClientId), nameof(EclAssessment.EngagementId));
    ecl.HasOne<AccountingReconciliation>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    ecl.HasOne<AdjustmentJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProposedJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var inventory = b.Entity<InventoryValuationAssessment>();
    inventory.Property(x => x.ReconciliationSourceHash).HasMaxLength(64);
    inventory.Property(x => x.MethodologyVersion).HasMaxLength(100);
    inventory.Property(x => x.AssumptionsHash).HasMaxLength(64);
    inventory.Property(x => x.Status).HasMaxLength(30);
    inventory.HasIndex(x => new { x.FirmId, x.ReconciliationId, x.Version }).IsUnique().HasDatabaseName("ux_inventory_valuation_version");
    inventory.ToTable("inventory_valuation_assessments", t => t.HasCheckConstraint("ck_inventory_valuation_values",
      "quantity >= 0 AND unit_cost >= 0 AND nrv_per_unit >= 0 AND obsolescence_reserve >= 0 AND book_amount >= 0"));
    ScopeToEngagement(inventory, nameof(InventoryValuationAssessment.FirmId), nameof(InventoryValuationAssessment.ClientId), nameof(InventoryValuationAssessment.EngagementId));
    inventory.HasOne<AccountingReconciliation>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    inventory.HasOne<AdjustmentJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProposedJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var specialist = b.Entity<SpecialistAccountingSchedule>();
    specialist.Property(x => x.Area).HasMaxLength(80);
    specialist.Property(x => x.MethodologyVersion).HasMaxLength(100);
    specialist.Property(x => x.DepreciationMethod).HasMaxLength(80);
    specialist.Property(x => x.PayrollContractReference).HasMaxLength(200);
    specialist.Property(x => x.PayrollBankPaymentReference).HasMaxLength(200);
    specialist.Property(x => x.LoanCovenantReference).HasMaxLength(200);
    specialist.Property(x => x.RelatedPartyDisclosureReference).HasMaxLength(200);
    specialist.Property(x => x.TaxJurisdiction).HasMaxLength(100);
    specialist.Property(x => x.TaxRuleVersion).HasMaxLength(100);
    specialist.Property(x => x.TaxReturnEvidenceReference).HasMaxLength(200);
    specialist.Property(x => x.TaxPaymentEvidenceReference).HasMaxLength(200);
    specialist.Property(x => x.TaxCorrespondenceReference).HasMaxLength(200);
    specialist.Property(x => x.ForecastOwner).HasMaxLength(200);
    specialist.Property(x => x.ForecastSensitivityReference).HasMaxLength(200);
    specialist.Property(x => x.ForecastSensitivityResult).HasMaxLength(4000);
    specialist.Property(x => x.AssumptionsHash).HasMaxLength(64);
    specialist.Property(x => x.EvidenceReference).HasMaxLength(2000);
    specialist.Property(x => x.ReviewConclusion).HasMaxLength(4000);
    specialist.Property(x => x.Status).HasMaxLength(30);
    specialist.HasIndex(x => new { x.FirmId, x.EngagementId, x.PeriodId, x.Area }).HasDatabaseName("ix_specialist_schedule_area");
    specialist.ToTable("specialist_accounting_schedules", t => t.HasCheckConstraint("ck_specialist_schedule_values",
      "length(trim(area)) > 0 AND length(trim(methodology_version)) > 0 AND (area <> 'ASSETS' OR (length(trim(depreciation_method)) > 0 AND useful_life_months > 0)) AND (payroll_gross_amount IS NULL OR payroll_gross_amount >= 0) AND (payroll_deductions_amount IS NULL OR payroll_deductions_amount >= 0) AND (payroll_net_amount IS NULL OR payroll_net_amount >= 0) AND (loan_repayment_amount IS NULL OR loan_repayment_amount >= 0) AND (tax_base_amount IS NULL OR tax_base_amount >= 0) AND (tax_rate IS NULL OR tax_rate >= 0) AND (forecast_cash_input_amount IS NULL OR forecast_cash_input_amount >= 0) AND (forecast_debt_input_amount IS NULL OR forecast_debt_input_amount >= 0)"));
    ScopeToEngagement(specialist, nameof(SpecialistAccountingSchedule.FirmId), nameof(SpecialistAccountingSchedule.ClientId), nameof(SpecialistAccountingSchedule.EngagementId));

    var analysis = b.Entity<AnalyticalReview>();
    analysis.Property(x => x.Area).HasMaxLength(80);
    analysis.Property(x => x.Measure).HasMaxLength(100);
    analysis.Property(x => x.Currency).HasMaxLength(3);
    analysis.Property(x => x.DenominatorBasis).HasMaxLength(200);
    analysis.Property(x => x.FormulaVersion).HasMaxLength(100);
    analysis.Property(x => x.MovementFlags).HasMaxLength(200);
    analysis.Property(x => x.SeasonalityExplanation).HasMaxLength(2000);
    analysis.Property(x => x.InputSnapshotJson).HasMaxLength(8000);
    analysis.Property(x => x.InputHash).HasMaxLength(64);
    analysis.Property(x => x.Explanation).HasMaxLength(4000);
    analysis.Property(x => x.ReviewConclusion).HasMaxLength(4000);
    analysis.Property(x => x.Status).HasMaxLength(30);
    analysis.HasIndex(x => new { x.FirmId, x.EngagementId, x.PeriodId, x.Area, x.Measure }).HasDatabaseName("ix_analytical_review_measure");
    analysis.ToTable("analytical_reviews", t => t.HasCheckConstraint("ck_analytical_review_values",
      "length(trim(area)) > 0 AND length(trim(measure)) > 0 AND length(trim(currency)) = 3 AND length(trim(denominator_basis)) > 0 AND length(trim(formula_version)) > 0"));
    ScopeToEngagement(analysis, nameof(AnalyticalReview.FirmId), nameof(AnalyticalReview.ClientId), nameof(AnalyticalReview.EngagementId));

    var flag = b.Entity<JournalRiskFlag>();
    flag.Property(x => x.RuleCode).HasMaxLength(100);
    flag.Property(x => x.Reason).HasMaxLength(2000);
    flag.Property(x => x.ManagementExplanation).HasMaxLength(4000);
    flag.Property(x => x.CorroborationReference).HasMaxLength(2000);
    flag.Property(x => x.Status).HasMaxLength(30);
    flag.Property(x => x.Disposition).HasMaxLength(2000);
    flag.Property(x => x.EvidenceReference).HasMaxLength(2000);
    flag.HasIndex(x => new { x.FirmId, x.TransactionId, x.RuleCode }).IsUnique().HasDatabaseName("ux_journal_risk_flag_rule");
    flag.ToTable("journal_risk_flags", t => t.HasCheckConstraint("ck_journal_risk_flag_values",
      "length(trim(rule_code)) > 0 AND length(trim(reason)) > 0 AND score >= 0 AND score <= 100"));
    ScopeToEngagement(flag, nameof(JournalRiskFlag.FirmId), nameof(JournalRiskFlag.ClientId), nameof(JournalRiskFlag.EngagementId));

    var evidenceLink = b.Entity<AccountingEvidenceAuditLink>();
    evidenceLink.Property(x => x.EvidenceKind).HasMaxLength(30);
    evidenceLink.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.EvidenceKind, x.EvidenceId, x.AuditProcedureResultId })
      .IsUnique().HasDatabaseName("ux_accounting_evidence_audit_link");
    evidenceLink.ToTable("accounting_evidence_audit_links", t => t.HasCheckConstraint("ck_accounting_evidence_audit_link_values",
      "evidence_kind IN ('ECL','INVENTORY','SPECIALIST','ANALYTICAL','JOURNAL_RISK')"));
    ScopeToEngagement(evidenceLink, nameof(AccountingEvidenceAuditLink.FirmId), nameof(AccountingEvidenceAuditLink.ClientId), nameof(AccountingEvidenceAuditLink.EngagementId));
    evidenceLink.HasOne<AuditProcedureResult>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditProcedureResultId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    evidenceLink.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.LinkedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
  }
}
