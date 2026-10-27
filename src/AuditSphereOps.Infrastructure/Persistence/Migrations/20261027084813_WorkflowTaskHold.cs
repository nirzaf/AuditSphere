using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "held_at",
                table: "workflow_task_bindings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hold_reason",
                table: "workflow_task_bindings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings",
                sql: "ordinal BETWEEN 1 AND 100000 AND generation >= 1 AND revision >= 1 AND (planned_effort_minutes IS NULL OR planned_effort_minutes BETWEEN 1 AND 1000000) AND due_date_policy IN ('MANUAL','FIXED','TEMPLATE_OFFSET') AND assignment_source IN ('MANUAL','TEMPLATE','MANUAL_REASSIGNMENT','AUTOMATION') AND (assigned_role IS NULL OR assigned_role IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND length(source_key) BETWEEN 1 AND 160 AND creation_request_digest ~ '^[0-9a-f]{64}$' AND ((template_revision_id IS NULL AND template_step_id IS NULL) OR (template_revision_id IS NOT NULL AND template_step_id IS NOT NULL)) AND (review_procedure_result_id IS NULL OR source_key LIKE 'REVIEW-REWORK:%') AND ((reminder_rule_revision_id IS NULL) = (reminder_operation_id IS NULL)) AND (cancellation_reason IS NULL OR length(trim(cancellation_reason)) > 0) AND (superseded_at IS NULL OR (supersession_reason IS NOT NULL AND length(trim(supersession_reason)) > 0)) AND ((held_at IS NULL) = (hold_reason IS NULL)) AND (hold_reason IS NULL OR length(trim(hold_reason)) BETWEEN 1 AND 2000)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED','TASK_DUE_DATE_CHANGED','TASK_EVIDENCE_RULE_CHANGED','TASK_DEPENDENCIES_CHANGED','TASK_CANCELLED','TASK_REOPENED','TASK_SUSPENDED','TASK_RESUMED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL)) AND ((kind = 'TASK_DUE_DATE_CHANGED') = (previous_due_date IS DISTINCT FROM new_due_date)) AND ((kind IN ('TASK_CANCELLED','TASK_REOPENED','TASK_SUSPENDED','TASK_RESUMED')) = (reason IS NOT NULL AND length(trim(reason)) BETWEEN 1 AND 2000))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE workflow_task_activities IN ACCESS EXCLUSIVE MODE;
                LOCK TABLE workflow_task_bindings IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM workflow_task_activities WHERE kind IN ('TASK_SUSPENDED','TASK_RESUMED'))
                     OR EXISTS (SELECT 1 FROM workflow_task_bindings WHERE held_at IS NOT NULL) THEN
                    RAISE EXCEPTION 'Workflow task hold evidence prevents rollback without losing task history.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.DropColumn(
                name: "held_at",
                table: "workflow_task_bindings");

            migrationBuilder.DropColumn(
                name: "hold_reason",
                table: "workflow_task_bindings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings",
                sql: "ordinal BETWEEN 1 AND 100000 AND generation >= 1 AND revision >= 1 AND (planned_effort_minutes IS NULL OR planned_effort_minutes BETWEEN 1 AND 1000000) AND due_date_policy IN ('MANUAL','FIXED','TEMPLATE_OFFSET') AND assignment_source IN ('MANUAL','TEMPLATE','MANUAL_REASSIGNMENT','AUTOMATION') AND (assigned_role IS NULL OR assigned_role IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND length(source_key) BETWEEN 1 AND 160 AND creation_request_digest ~ '^[0-9a-f]{64}$' AND ((template_revision_id IS NULL AND template_step_id IS NULL) OR (template_revision_id IS NOT NULL AND template_step_id IS NOT NULL)) AND (review_procedure_result_id IS NULL OR source_key LIKE 'REVIEW-REWORK:%') AND ((reminder_rule_revision_id IS NULL) = (reminder_operation_id IS NULL)) AND (cancellation_reason IS NULL OR length(trim(cancellation_reason)) > 0) AND (superseded_at IS NULL OR (supersession_reason IS NOT NULL AND length(trim(supersession_reason)) > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED','TASK_DUE_DATE_CHANGED','TASK_EVIDENCE_RULE_CHANGED','TASK_DEPENDENCIES_CHANGED','TASK_CANCELLED','TASK_REOPENED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL)) AND ((kind = 'TASK_DUE_DATE_CHANGED') = (previous_due_date IS DISTINCT FROM new_due_date)) AND ((kind IN ('TASK_CANCELLED','TASK_REOPENED')) = (reason IS NOT NULL AND length(trim(reason)) BETWEEN 1 AND 2000))");
        }
    }
}
