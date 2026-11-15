using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wu004ScopedReferenceIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_risk_band_assessments_scope_id",
                table: "risk_band_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_quotation_versions_firm_id_id",
                table: "quotation_versions",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_quotation_approvals_firm_id_id",
                table: "quotation_approvals",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_materiality_calculations_scope_id",
                table: "materiality_calculations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_eqr_cases_scope_id",
                table: "eqr_cases",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_engagement_staff_assignments_scope_id",
                table: "engagement_staff_assignments",
                columns: new[] { "firm_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_engagement_fee_agreements_firm_id_id",
                table: "engagement_fee_agreements",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_proposals_firm_client_id",
                table: "proposals",
                columns: new[] { "firm_id", "practice_client_id", "id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_client_conversion_client_scope",
                table: "client_conversions",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_client_conversion_proposal_client_scope",
                table: "client_conversions",
                columns: new[] { "firm_id", "client_id", "proposal_id" },
                principalTable: "proposals",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_client_portal_intent_client_scope",
                table: "client_portal_intents",
                columns: new[] { "firm_id", "practice_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_engagement_activation_engagement_scope",
                table: "engagement_activations",
                columns: new[] { "firm_id", "practice_client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_engagement_creation_engagement_scope",
                table: "engagement_creations",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_engagement_fee_agreement_proposal_scope",
                table: "engagement_fee_agreements",
                columns: new[] { "firm_id", "practice_client_id", "proposal_id" },
                principalTable: "proposals",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_eqr_decision_case_scope",
                table: "eqr_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "eqr_case_id" },
                principalTable: "eqr_cases",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_fee_milestone_agreement_scope",
                table: "fee_milestones",
                columns: new[] { "firm_id", "agreement_id" },
                principalTable: "engagement_fee_agreements",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_file_freeze_early_lock_deliverable_scope",
                table: "file_freeze_early_locks",
                columns: new[] { "firm_id", "client_id", "engagement_id", "report_deliverable_id" },
                principalTable: "audit_deliverables",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_journal_source_reconciliation_dataset_scope",
                table: "journal_source_reconciliations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "base_dataset_id" },
                principalTable: "trial_balance_datasets",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_materiality_calculation_mapping_scope",
                table: "materiality_calculations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "mapping_version_id" },
                principalTable: "mapping_versions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_materiality_calculation_assessment_scope",
                table: "materiality_calculations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "materiality_assessment_id" },
                principalTable: "materiality_assessments",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_materiality_rounding_effective_assessment_scope",
                table: "materiality_rounding_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "effective_assessment_id" },
                principalTable: "materiality_assessments",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_materiality_rounding_source_assessment_scope",
                table: "materiality_rounding_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_assessment_id" },
                principalTable: "materiality_assessments",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_materiality_rounding_calculation_scope",
                table: "materiality_rounding_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "materiality_calculation_id" },
                principalTable: "materiality_calculations",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_operator_command_event_operation_scope",
                table: "operator_command_events",
                columns: new[] { "firm_id", "operation_id" },
                principalTable: "durable_operations",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_quotation_approval_revocation_approval_scope",
                table: "quotation_approval_revocations",
                columns: new[] { "firm_id", "quotation_approval_id" },
                principalTable: "quotation_approvals",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_quotation_approval_version_scope",
                table: "quotation_approvals",
                columns: new[] { "firm_id", "quotation_version_id" },
                principalTable: "quotation_versions",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_quotation_version_proposal_scope",
                table: "quotation_versions",
                columns: new[] { "firm_id", "proposal_id" },
                principalTable: "proposals",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_risk_owner_assignment_assessment_scope",
                table: "risk_owner_assignments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "risk_band_assessment_id" },
                principalTable: "risk_band_assessments",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_risk_partner_clearance_assessment_scope",
                table: "risk_partner_clearances",
                columns: new[] { "firm_id", "client_id", "engagement_id", "risk_band_assessment_id" },
                principalTable: "risk_band_assessments",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_signature_application_specimen_scope",
                table: "signature_applications",
                columns: new[] { "firm_id", "specimen_id" },
                principalTable: "signature_specimens",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staffing_change_assignment_scope",
                table: "staffing_changes",
                columns: new[] { "firm_id", "engagement_id", "assignment_id" },
                principalTable: "engagement_staff_assignments",
                principalColumns: new[] { "firm_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staffing_change_engagement_scope",
                table: "staffing_changes",
                columns: new[] { "firm_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "fk_staffing_change_engagement_scope", table: "staffing_changes");
            migrationBuilder.DropForeignKey(name: "fk_staffing_change_assignment_scope", table: "staffing_changes");
            migrationBuilder.DropForeignKey(name: "fk_signature_application_specimen_scope", table: "signature_applications");
            migrationBuilder.DropForeignKey(name: "fk_risk_partner_clearance_assessment_scope", table: "risk_partner_clearances");
            migrationBuilder.DropForeignKey(name: "fk_risk_owner_assignment_assessment_scope", table: "risk_owner_assignments");
            migrationBuilder.DropForeignKey(name: "fk_quotation_version_proposal_scope", table: "quotation_versions");
            migrationBuilder.DropForeignKey(name: "fk_quotation_approval_version_scope", table: "quotation_approvals");
            migrationBuilder.DropForeignKey(name: "fk_quotation_approval_revocation_approval_scope", table: "quotation_approval_revocations");
            migrationBuilder.DropForeignKey(name: "fk_operator_command_event_operation_scope", table: "operator_command_events");
            migrationBuilder.DropForeignKey(name: "fk_materiality_rounding_calculation_scope", table: "materiality_rounding_decisions");
            migrationBuilder.DropForeignKey(name: "fk_materiality_rounding_source_assessment_scope", table: "materiality_rounding_decisions");
            migrationBuilder.DropForeignKey(name: "fk_materiality_rounding_effective_assessment_scope", table: "materiality_rounding_decisions");
            migrationBuilder.DropForeignKey(name: "fk_materiality_calculation_assessment_scope", table: "materiality_calculations");
            migrationBuilder.DropForeignKey(name: "fk_materiality_calculation_mapping_scope", table: "materiality_calculations");
            migrationBuilder.DropForeignKey(name: "fk_journal_source_reconciliation_dataset_scope", table: "journal_source_reconciliations");
            migrationBuilder.DropForeignKey(name: "fk_file_freeze_early_lock_deliverable_scope", table: "file_freeze_early_locks");
            migrationBuilder.DropForeignKey(name: "fk_fee_milestone_agreement_scope", table: "fee_milestones");
            migrationBuilder.DropForeignKey(name: "fk_eqr_decision_case_scope", table: "eqr_decisions");
            migrationBuilder.DropForeignKey(name: "fk_engagement_fee_agreement_proposal_scope", table: "engagement_fee_agreements");
            migrationBuilder.DropForeignKey(name: "fk_engagement_creation_engagement_scope", table: "engagement_creations");
            migrationBuilder.DropForeignKey(name: "fk_engagement_activation_engagement_scope", table: "engagement_activations");
            migrationBuilder.DropForeignKey(name: "fk_client_portal_intent_client_scope", table: "client_portal_intents");
            migrationBuilder.DropForeignKey(name: "fk_client_conversion_proposal_client_scope", table: "client_conversions");
            migrationBuilder.DropForeignKey(name: "fk_client_conversion_client_scope", table: "client_conversions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_risk_band_assessments_scope_id",
                table: "risk_band_assessments");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_quotation_versions_firm_id_id",
                table: "quotation_versions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_quotation_approvals_firm_id_id",
                table: "quotation_approvals");

            migrationBuilder.DropIndex(
                name: "ux_proposals_firm_client_id",
                table: "proposals");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_materiality_calculations_scope_id",
                table: "materiality_calculations");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_eqr_cases_scope_id",
                table: "eqr_cases");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_engagement_staff_assignments_scope_id",
                table: "engagement_staff_assignments");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_engagement_fee_agreements_firm_id_id",
                table: "engagement_fee_agreements");
        }
    }
}
