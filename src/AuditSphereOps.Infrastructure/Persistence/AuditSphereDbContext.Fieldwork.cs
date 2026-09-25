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
  private static void ConfigureFieldwork(ModelBuilder b)
  {
    var schedule = b.Entity<AuditSchedule>();
    schedule.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_schedules_firm_id_id");
    schedule.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_schedules_scope_id");
    schedule.Property(x => x.ScheduleType).HasMaxLength(100);
    schedule.Property(x => x.EntityIdentifier).HasMaxLength(200);
    schedule.Property(x => x.SourceReceiptReference).HasMaxLength(200);
    schedule.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    schedule.Property(x => x.SignConvention).HasMaxLength(100);
    schedule.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength();
    schedule.Property(x => x.CompletenessDecision).HasMaxLength(2000);
    schedule.Property(x => x.Status).HasMaxLength(30);
    schedule.Property(x => x.AcceptedSourceHash).HasMaxLength(64);
    schedule.HasIndex(x => new { x.FirmId, x.EngagementId, x.AcceptedSourceDecisionId })
      .HasDatabaseName("ix_audit_schedule_accepted_source");
    schedule.HasIndex(x => new { x.FirmId, x.EngagementId, x.SourceReceiptReference, x.SourceHash }).IsUnique()
      .HasDatabaseName("ux_audit_schedule_source_version");
    schedule.HasIndex(x => new { x.FirmId, x.EngagementId, x.ScheduleType, x.AsOfDate })
      .HasDatabaseName("ix_audit_schedule_scope_type_date");
    schedule.ToTable("audit_schedules", t => t.HasCheckConstraint("ck_audit_schedule_values",
      "length(trim(schedule_type)) > 0 AND length(trim(entity_identifier)) > 0" +
      " AND length(trim(source_receipt_reference)) > 0 AND length(source_hash) = 64" +
      " AND currency ~ '^[A-Z]{3}$' AND length(trim(sign_convention)) > 0" +
      " AND row_count >= 0 AND input_generation > 0 AND status IN ('PENDING_REVIEW','RECONCILED','UNRECONCILED','APPROVED','SUPERSEDED')"));
    ScopeToEngagement(schedule, nameof(AuditSchedule.FirmId), nameof(AuditSchedule.ClientId), nameof(AuditSchedule.EngagementId));
    schedule.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    schedule.HasOne<SourceImportBatch>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceImportBatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var bankReconciliation = b.Entity<AuditBankReconciliation>();
    bankReconciliation.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_bank_reconciliations_firm_id_id");
    bankReconciliation.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_bank_reconciliations_scope_id");
    bankReconciliation.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    bankReconciliation.Property(x => x.Status).HasMaxLength(30);
    bankReconciliation.Property(x => x.Conclusion).HasMaxLength(4000);
    bankReconciliation.ToTable("audit_bank_reconciliations", t => t.HasCheckConstraint("ck_audit_bank_reconciliation_values",
      "currency ~ '^[A-Z]{3}$' AND input_generation > 0 AND status IN ('RECONCILED','UNRECONCILED','APPROVED','CHANGES_REQUIRED')"));
    ScopeToEngagement(bankReconciliation, nameof(AuditBankReconciliation.FirmId), nameof(AuditBankReconciliation.ClientId), nameof(AuditBankReconciliation.EngagementId));
    bankReconciliation.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.LedgerScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliation.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.StatementScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliation.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliation.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliation.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var bankReconciliationItem = b.Entity<AuditBankReconciliationItem>();
    bankReconciliationItem.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_bank_reconciliation_items_firm_id_id");
    bankReconciliationItem.HasAlternateKey(x => new { x.FirmId, x.BankReconciliationId, x.StableItemId })
      .HasName("AK_audit_bank_reconciliation_items_reconciliation_stable");
    bankReconciliationItem.Property(x => x.StableItemId).HasMaxLength(200);
    bankReconciliationItem.Property(x => x.ItemType).HasMaxLength(30);
    bankReconciliationItem.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    bankReconciliationItem.Property(x => x.Description).HasMaxLength(1000);
    bankReconciliationItem.Property(x => x.SourceReference).HasMaxLength(500);
    bankReconciliationItem.Property(x => x.EvidenceReference).HasMaxLength(2000);
    bankReconciliationItem.ToTable("audit_bank_reconciliation_items", t => t.HasCheckConstraint("ck_audit_bank_reconciliation_item_values",
      "length(trim(stable_item_id)) > 0 AND item_type IN ('LEDGER','STATEMENT','TIMING','PROPOSED_CORRECTION')"));
    ScopeToEngagement(bankReconciliationItem, nameof(AuditBankReconciliationItem.FirmId), nameof(AuditBankReconciliationItem.ClientId), nameof(AuditBankReconciliationItem.EngagementId));
    bankReconciliationItem.HasOne<AuditBankReconciliation>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.BankReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliationItem.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliationItem.HasOne<AdjustmentJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProposedJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var scheduleRow = b.Entity<AuditScheduleRow>();
    scheduleRow.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_schedule_rows_firm_id_id");
    scheduleRow.HasAlternateKey(x => new { x.FirmId, x.ScheduleId, x.StableRowId })
      .HasName("AK_audit_schedule_rows_schedule_stable");
    scheduleRow.Property(x => x.StableRowId).HasMaxLength(200);
    scheduleRow.Property(x => x.AccountCode).HasMaxLength(100);
    scheduleRow.Property(x => x.Description).HasMaxLength(1000);
    scheduleRow.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    scheduleRow.Property(x => x.OriginalValuesJson).HasMaxLength(20000);
    scheduleRow.ToTable("audit_schedule_rows", t => t.HasCheckConstraint("ck_audit_schedule_row_values",
      "source_line_number > 0 AND length(trim(stable_row_id)) > 0 AND length(trim(account_code)) > 0" +
      " AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(original_values_json)) > 0"));
    ScopeToEngagement(scheduleRow, nameof(AuditScheduleRow.FirmId), nameof(AuditScheduleRow.ClientId), nameof(AuditScheduleRow.EngagementId));
    scheduleRow.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    scheduleRow.HasIndex(x => new { x.FirmId, x.ScheduleId, x.SourceLineNumber })
      .HasDatabaseName("ix_audit_schedule_rows_order");

    var selection = b.Entity<AuditSelection>();
    selection.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_selections_firm_id_id");
    selection.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_selections_scope_id");
    selection.Property(x => x.Method).HasMaxLength(100);
    selection.Property(x => x.Rationale).HasMaxLength(4000);
    selection.Property(x => x.Status).HasMaxLength(30);
    selection.ToTable("audit_selections", t => t.HasCheckConstraint("ck_audit_selection_values",
      "length(trim(method)) > 0 AND length(trim(rationale)) > 0 AND selected_count > 0" +
      " AND input_generation > 0 AND status IN ('SUBMITTED','REVIEWED','CHANGES_REQUIRED')"));
    ScopeToEngagement(selection, nameof(AuditSelection.FirmId), nameof(AuditSelection.ClientId), nameof(AuditSelection.EngagementId));
    selection.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selection.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selection.HasOne<PopulationVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PopulationVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selection.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selection.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var selectionItem = b.Entity<AuditSelectionItem>();
    selectionItem.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_selection_items_firm_id_id");
    selectionItem.HasAlternateKey(x => new { x.FirmId, x.SelectionId, x.StableRowId })
      .HasName("AK_audit_selection_items_selection_stable");
    selectionItem.Property(x => x.StableRowId).HasMaxLength(200);
    selectionItem.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    selectionItem.Property(x => x.InclusionReason).HasMaxLength(1000);
    selectionItem.ToTable("audit_selection_items", t => t.HasCheckConstraint("ck_audit_selection_item_values",
      "length(trim(stable_row_id)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(inclusion_reason)) > 0"));
    ScopeToEngagement(selectionItem, nameof(AuditSelectionItem.FirmId), nameof(AuditSelectionItem.ClientId), nameof(AuditSelectionItem.EngagementId));
    selectionItem.HasOne<AuditSelection>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selectionItem.HasOne<AuditScheduleRow>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleRowId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var itemTest = b.Entity<AuditItemTest>();
    itemTest.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_item_tests_firm_id_id");
    itemTest.HasAlternateKey(x => new { x.FirmId, x.SelectionItemId, x.Revision })
      .HasName("AK_audit_item_tests_item_revision");
    itemTest.Property(x => x.WorkPerformed).HasMaxLength(100000);
    itemTest.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    itemTest.Property(x => x.Result).HasMaxLength(20);
    itemTest.Property(x => x.ContradictoryEvidence).HasMaxLength(4000);
    itemTest.Property(x => x.FollowUp).HasMaxLength(4000);
    itemTest.ToTable("audit_item_tests", t => t.HasCheckConstraint("ck_audit_item_test_values",
      "revision > 0 AND length(trim(work_performed)) > 0 AND length(trim(evidence_references_json)) > 0" +
      " AND input_generation > 0 AND result IN ('PENDING','PASS','EXCEPTION','LIMITATION')"));
    ScopeToEngagement(itemTest, nameof(AuditItemTest.FirmId), nameof(AuditItemTest.ClientId), nameof(AuditItemTest.EngagementId));
    itemTest.HasOne<AuditSelection>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTest.HasOne<AuditSelectionItem>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionItemId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTest.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTest.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.TestedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var itemTestReview = b.Entity<AuditItemTestReview>();
    itemTestReview.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_item_test_reviews_firm_id_id");
    itemTestReview.Property(x => x.Decision).HasMaxLength(30);
    itemTestReview.Property(x => x.Comment).HasMaxLength(20000);
    itemTestReview.ToTable("audit_item_test_reviews", t => t.HasCheckConstraint("ck_audit_item_test_review_values",
      "test_revision > 0 AND decision IN ('REVIEWED','CHANGES_REQUIRED')"));
    ScopeToEngagement(itemTestReview, nameof(AuditItemTestReview.FirmId), nameof(AuditItemTestReview.ClientId), nameof(AuditItemTestReview.EngagementId));
    itemTestReview.HasOne<AuditItemTest>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditItemTestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTestReview.HasOne<AuditSelectionItem>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionItemId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTestReview.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var cutOff = b.Entity<AuditCutOffTestRecord>();
    cutOff.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_cut_off_tests_firm_id_id");
    cutOff.Property(x => x.PeriodEndIndicator).HasMaxLength(30);
    cutOff.Property(x => x.WorkPerformed).HasMaxLength(20000);
    cutOff.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    cutOff.HasIndex(x => new { x.FirmId, x.EngagementId, x.SelectionItemId }).IsUnique()
      .HasDatabaseName("ux_audit_cut_off_test_item");
    cutOff.ToTable("audit_cut_off_test_records", t => t.HasCheckConstraint("ck_audit_cut_off_test_values",
      "revision > 0 AND period_end_indicator IN ('BEFORE_PERIOD_END','AFTER_PERIOD_END')"));
    ScopeToEngagement(cutOff, nameof(AuditCutOffTestRecord.FirmId), nameof(AuditCutOffTestRecord.ClientId), nameof(AuditCutOffTestRecord.EngagementId));
    cutOff.HasOne<AuditSelectionItem>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionItemId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    cutOff.HasOne<AuditSelection>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var subsequent = b.Entity<AuditSubsequentMatchRecord>();
    subsequent.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_subsequent_matches_firm_id_id");
    subsequent.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    subsequent.Property(x => x.SubsequentSourceReference).HasMaxLength(200);
    subsequent.Property(x => x.State).HasMaxLength(30);
    subsequent.Property(x => x.UnmatchedReason).HasMaxLength(2000);
    subsequent.Property(x => x.EvidenceReference).HasMaxLength(2000);
    subsequent.HasIndex(x => new { x.FirmId, x.EngagementId, x.SelectionItemId }).IsUnique()
      .HasDatabaseName("ux_audit_subsequent_match_item");
    subsequent.ToTable("audit_subsequent_match_records", t => t.HasCheckConstraint("ck_audit_subsequent_match_values",
      "revision > 0 AND currency ~ '^[A-Z]{3}$' AND matched_amount >= 0 AND state IN ('MATCHED','PARTIALLY_MATCHED','UNMATCHED')"));
    ScopeToEngagement(subsequent, nameof(AuditSubsequentMatchRecord.FirmId), nameof(AuditSubsequentMatchRecord.ClientId), nameof(AuditSubsequentMatchRecord.EngagementId));
    subsequent.HasOne<AuditSelectionItem>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionItemId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    subsequent.HasOne<AuditSelection>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var openingBalance = b.Entity<OpeningBalanceVerification>();
    openingBalance.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_opening_balance_verifications_firm_id_id");
    openingBalance.Property(x => x.PriorReference).HasMaxLength(200);
    openingBalance.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    openingBalance.Property(x => x.Conclusion).HasMaxLength(30);
    openingBalance.Property(x => x.Rationale).HasMaxLength(2000);
    openingBalance.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    openingBalance.HasIndex(x => new { x.FirmId, x.EngagementId, x.AsOfDate }).IsUnique()
      .HasDatabaseName("ux_opening_balance_verification_engagement");
    openingBalance.ToTable("opening_balance_verifications", t => t.HasCheckConstraint("ck_opening_balance_verification_values",
      "revision > 0 AND length(trim(prior_reference)) > 0 AND currency ~ '^[A-Z]{3}$' " +
      "AND conclusion IN ('AGREED','DIFFERENCES_RESOLVED','DIFFERENCES_UNRESOLVED','NOT_VERIFIABLE')"));
    ScopeToEngagement(openingBalance, nameof(OpeningBalanceVerification.FirmId), nameof(OpeningBalanceVerification.ClientId), nameof(OpeningBalanceVerification.EngagementId));

    var variance = b.Entity<AnalyticalReviewVarianceInvestigation>();
    variance.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_analytical_variance_investigations_firm_id_id");
    variance.Property(x => x.AccountArea).HasMaxLength(80);
    variance.Property(x => x.PeriodReference).HasMaxLength(60);
    variance.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    variance.Property(x => x.Explanation).HasMaxLength(4000);
    variance.Property(x => x.Conclusion).HasMaxLength(30);
    variance.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    variance.HasIndex(x => new { x.FirmId, x.EngagementId, x.AccountArea, x.PeriodReference }).IsUnique()
      .HasDatabaseName("ux_analytical_variance_engagement_area_period");
    variance.ToTable("analytical_variance_investigations", t => t.HasCheckConstraint("ck_analytical_variance_values",
      "revision > 0 AND currency ~ '^[A-Z]{3}$' AND investigation_threshold >= 0 " +
      "AND conclusion IN ('EXPLAINED','UNEXPLAINED','CORROBORATED')"));
    ScopeToEngagement(variance, nameof(AnalyticalReviewVarianceInvestigation.FirmId), nameof(AnalyticalReviewVarianceInvestigation.ClientId), nameof(AnalyticalReviewVarianceInvestigation.EngagementId));

    var goingConcern = b.Entity<GoingConcernAssessment>();
    goingConcern.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_going_concern_assessments_firm_id_id");
    goingConcern.Property(x => x.ForecastReviewOutcome).HasMaxLength(4000);
    goingConcern.Property(x => x.Conclusion).HasMaxLength(40);
    goingConcern.Property(x => x.Rationale).HasMaxLength(4000);
    goingConcern.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    goingConcern.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    goingConcern.HasIndex(x => new { x.FirmId, x.EngagementId, x.AssessmentDate }).IsUnique()
      .HasDatabaseName("ux_going_concern_engagement_date");
    goingConcern.ToTable("going_concern_assessments", t => t.HasCheckConstraint("ck_going_concern_values",
      "revision > 0 AND currency ~ '^[A-Z]{3}$' AND period_covered_to >= assessment_date " +
      "AND conclusion IN ('NO_MATERIAL_UNCERTAINTY','MATERIAL_UNCERTAINTY_DISCLOSED','INADEQUATE_DISCLOSURE','NOT_ASSESSED')"));
    ScopeToEngagement(goingConcern, nameof(GoingConcernAssessment.FirmId), nameof(GoingConcernAssessment.ClientId), nameof(GoingConcernAssessment.EngagementId));

    var subsequentEvent = b.Entity<SubsequentEventReview>();
    subsequentEvent.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_subsequent_event_reviews_firm_id_id");
    subsequentEvent.Property(x => x.Description).HasMaxLength(4000);
    subsequentEvent.Property(x => x.Classification).HasMaxLength(30);
    subsequentEvent.Property(x => x.DisclosureReference).HasMaxLength(200);
    subsequentEvent.Property(x => x.Rationale).HasMaxLength(4000);
    subsequentEvent.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    subsequentEvent.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    subsequentEvent.ToTable("subsequent_event_reviews", t => t.HasCheckConstraint("ck_subsequent_event_values",
      "revision > 0 AND currency ~ '^[A-Z]{3}$' AND event_date >= period_end_date " +
      "AND classification IN ('ADJUSTING','NON_ADJUSTING','PENDING_ASSESSMENT')"));
    ScopeToEngagement(subsequentEvent, nameof(SubsequentEventReview.FirmId), nameof(SubsequentEventReview.ClientId), nameof(SubsequentEventReview.EngagementId));

    var confirmation = b.Entity<AuditConfirmationCase>();
    confirmation.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_confirmation_cases_firm_id_id");
    confirmation.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_confirmation_cases_scope_id");
    confirmation.Property(x => x.AreaCode).HasMaxLength(40);
    confirmation.Property(x => x.SourceRecordId).HasMaxLength(200);
    confirmation.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    confirmation.Property(x => x.Respondent).HasMaxLength(500);
    confirmation.Property(x => x.ContactValidationSource).HasMaxLength(2000);
    confirmation.Property(x => x.Status).HasMaxLength(30);
    confirmation.Property(x => x.DispatchReference).HasMaxLength(500);
    confirmation.ToTable("audit_confirmation_cases", t => t.HasCheckConstraint("ck_audit_confirmation_case_values",
      "length(trim(area_code)) > 0 AND length(trim(source_record_id)) > 0 AND currency ~ '^[A-Z]{3}$'" +
      " AND length(trim(respondent)) > 0 AND length(trim(contact_validation_source)) > 0" +
      " AND input_generation > 0 AND status IN ('DRAFT','APPROVED','DISPATCHED','RESPONSE_RECEIVED','NO_RESPONSE','ALTERNATIVE_REQUIRED','CLOSED')"));
    ScopeToEngagement(confirmation, nameof(AuditConfirmationCase.FirmId), nameof(AuditConfirmationCase.ClientId), nameof(AuditConfirmationCase.EngagementId));
    confirmation.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    confirmation.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var response = b.Entity<AuditConfirmationResponse>();
    response.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_confirmation_responses_firm_id_id");
    response.HasAlternateKey(x => new { x.FirmId, x.ConfirmationCaseId, x.Revision })
      .HasName("AK_audit_confirmation_responses_case_revision");
    response.Property(x => x.Origin).HasMaxLength(40);
    response.Property(x => x.Channel).HasMaxLength(40);
    response.Property(x => x.ResponseReference).HasMaxLength(500);
    response.Property(x => x.AuthenticityAssessment).HasMaxLength(4000);
    response.Property(x => x.Decision).HasMaxLength(30);
    response.ToTable("audit_confirmation_responses", t => t.HasCheckConstraint("ck_audit_confirmation_response_values",
      "revision > 0 AND length(trim(origin)) > 0 AND length(trim(channel)) > 0" +
      " AND length(trim(response_reference)) > 0 AND length(trim(authenticity_assessment)) > 0" +
      " AND decision IN ('PENDING','AGREED','DIFFERENCE','NO_RESPONSE','ALTERNATIVE_REQUIRED')"));
    ScopeToEngagement(response, nameof(AuditConfirmationResponse.FirmId), nameof(AuditConfirmationResponse.ClientId), nameof(AuditConfirmationResponse.EngagementId));
    response.HasOne<AuditConfirmationCase>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ConfirmationCaseId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    response.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    response.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var alternative = b.Entity<AuditAlternativeProcedure>();
    alternative.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_alternatives_firm_id_id");
    alternative.Property(x => x.Purpose).HasMaxLength(2000);
    alternative.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    alternative.Property(x => x.Conclusion).HasMaxLength(20000);
    alternative.Property(x => x.Status).HasMaxLength(20);
    alternative.ToTable("audit_alternative_procedures", t => t.HasCheckConstraint("ck_audit_alternative_values",
      "length(trim(purpose)) > 0 AND length(trim(evidence_references_json)) > 0 AND length(trim(conclusion)) > 0" +
      " AND status IN ('SUBMITTED','REVIEWED')"));
    ScopeToEngagement(alternative, nameof(AuditAlternativeProcedure.FirmId), nameof(AuditAlternativeProcedure.ClientId), nameof(AuditAlternativeProcedure.EngagementId));
    alternative.HasOne<AuditConfirmationCase>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ConfirmationCaseId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    alternative.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    alternative.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var assessment = b.Entity<AuditAreaAssessment>();
    assessment.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_area_assessments_firm_id_id");
    assessment.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_area_assessments_scope_id");
    assessment.Property(x => x.AreaCode).HasMaxLength(40);
    assessment.Property(x => x.AssessmentKind).HasMaxLength(60);
    assessment.Property(x => x.MethodologyReference).HasMaxLength(500);
    assessment.Property(x => x.InputSnapshotJson).HasMaxLength(100000);
    assessment.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    assessment.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    assessment.Property(x => x.Conclusion).HasMaxLength(20000);
    assessment.Property(x => x.Status).HasMaxLength(30);
    assessment.ToTable("audit_area_assessments", t => t.HasCheckConstraint("ck_audit_area_assessment_values",
      "area_code IN ('CASH_BANK','RECEIVABLES','INVENTORY','REVENUE','PAYABLES','FIXED_ASSETS','EXPENSES','PAYROLL','LOANS','EQUITY','RELATED_PARTIES','TAX_STATUTORY','JOURNALS_FRAUD','ANALYTICAL_REVIEW','GOING_CONCERN','SUBSEQUENT_EVENTS','FINANCIAL_STATEMENTS','AUDIT_DIFFERENCES')" +
      " AND length(trim(assessment_kind)) > 0 AND length(trim(methodology_reference)) > 0" +
      " AND length(trim(input_snapshot_json)) > 0 AND length(trim(evidence_references_json)) > 0" +
      " AND length(trim(conclusion)) > 0 AND input_generation > 0 AND revision > 0" +
      " AND status IN ('SUBMITTED','REVIEWED','CHANGES_REQUIRED')"));
    ScopeToEngagement(assessment, nameof(AuditAreaAssessment.FirmId), nameof(AuditAreaAssessment.ClientId), nameof(AuditAreaAssessment.EngagementId));
    assessment.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    assessment.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    assessment.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var difference = b.Entity<AuditDifference>();
    difference.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_differences_firm_id_id");
    difference.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_differences_scope_id");
    difference.Property(x => x.AccountArea).HasMaxLength(200);
    difference.Property(x => x.DifferenceType).HasMaxLength(60);
    difference.Property(x => x.Description).HasMaxLength(4000);
    difference.Property(x => x.MaterialityReference).HasMaxLength(1000);
    difference.Property(x => x.QualitativeConcerns).HasMaxLength(4000);
    difference.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    difference.Property(x => x.ManagementResponse).HasMaxLength(4000);
    difference.Property(x => x.CorrectionReference).HasMaxLength(500);
    difference.Property(x => x.CorrectionState).HasMaxLength(40);
    difference.Property(x => x.JournalImpactJson).HasMaxLength(100000);
    difference.Property(x => x.JournalImpactHash).HasMaxLength(64);
    difference.Property(x => x.Evaluation).HasMaxLength(4000);
    difference.Property(x => x.Status).HasMaxLength(30);
    difference.ToTable("audit_differences", t => t.HasCheckConstraint("ck_audit_difference_values",
      "length(trim(account_area)) > 0 AND length(trim(difference_type)) > 0 AND length(trim(description)) > 0" +
      " AND currency ~ '^[A-Z]{3}$' AND amount <> 0 AND input_generation > 0" +
      " AND status IN ('OPEN','MANAGEMENT_RESPONDED','EVALUATED','CORRECTED','VERIFIED_REFLECTED')" +
      " AND (correction_state IS NULL OR correction_state IN ('PROPOSED','AGREED','REJECTED','APPLIED_IN_REPORTING','REPORTED_POSTED_EXTERNALLY','VERIFIED_REFLECTED'))" +
      " AND (NOT corrected OR correction_state IS NULL OR (correction_state = 'VERIFIED_REFLECTED' AND proposed_journal_id IS NOT NULL AND source_reflection_reconciliation_id IS NOT NULL AND verified_adjusted_snapshot_id IS NOT NULL))"));
    ScopeToEngagement(difference, nameof(AuditDifference.FirmId), nameof(AuditDifference.ClientId), nameof(AuditDifference.EngagementId));
    difference.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.EvaluatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<AdjustmentJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProposedJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<JournalSourceReconciliation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceReflectionReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<AdjustedTrialBalanceSnapshot>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.VerifiedAdjustedSnapshotId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
  }
}
