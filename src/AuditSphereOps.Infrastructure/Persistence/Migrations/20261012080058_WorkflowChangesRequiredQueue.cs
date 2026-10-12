using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowChangesRequiredQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings");

            migrationBuilder.AddColumn<Guid>(
                name: "review_procedure_result_id",
                table: "workflow_task_bindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_workflow_task_bindings_review_result",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "client_id", "engagement_id", "review_procedure_result_id" },
                unique: true,
                filter: "review_procedure_result_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings",
                sql: "ordinal BETWEEN 1 AND 100000 AND generation >= 1 AND revision >= 1 AND (planned_effort_minutes IS NULL OR planned_effort_minutes BETWEEN 1 AND 1000000) AND due_date_policy IN ('MANUAL','FIXED','TEMPLATE_OFFSET') AND assignment_source IN ('MANUAL','TEMPLATE','MANUAL_REASSIGNMENT','AUTOMATION') AND (assigned_role IS NULL OR assigned_role IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND length(source_key) BETWEEN 1 AND 160 AND creation_request_digest ~ '^[0-9a-f]{64}$' AND ((template_revision_id IS NULL AND template_step_id IS NULL) OR (template_revision_id IS NOT NULL AND template_step_id IS NOT NULL)) AND (review_procedure_result_id IS NULL OR source_key LIKE 'REVIEW-REWORK:%') AND (cancellation_reason IS NULL OR length(trim(cancellation_reason)) > 0) AND (superseded_at IS NULL OR (supersession_reason IS NOT NULL AND length(trim(supersession_reason)) > 0))");

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_task_bindings_audit_procedure_results_firm_id_clie~",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "client_id", "engagement_id", "review_procedure_result_id" },
                principalTable: "audit_procedure_results",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_task_bindings_audit_procedure_results_firm_id_clie~",
                table: "workflow_task_bindings");

            migrationBuilder.DropIndex(
                name: "UX_workflow_task_bindings_review_result",
                table: "workflow_task_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings");

            migrationBuilder.DropColumn(
                name: "review_procedure_result_id",
                table: "workflow_task_bindings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings",
                sql: "ordinal BETWEEN 1 AND 100000 AND generation >= 1 AND revision >= 1 AND (planned_effort_minutes IS NULL OR planned_effort_minutes BETWEEN 1 AND 1000000) AND due_date_policy IN ('MANUAL','FIXED','TEMPLATE_OFFSET') AND assignment_source IN ('MANUAL','TEMPLATE','MANUAL_REASSIGNMENT','AUTOMATION') AND (assigned_role IS NULL OR assigned_role IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND length(source_key) BETWEEN 1 AND 160 AND creation_request_digest ~ '^[0-9a-f]{64}$' AND ((template_revision_id IS NULL AND template_step_id IS NULL) OR (template_revision_id IS NOT NULL AND template_step_id IS NOT NULL)) AND (cancellation_reason IS NULL OR length(trim(cancellation_reason)) > 0) AND (superseded_at IS NULL OR (supersession_reason IS NOT NULL AND length(trim(supersession_reason)) > 0))");
        }
    }
}
