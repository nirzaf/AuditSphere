using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskReopening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED','TASK_DUE_DATE_CHANGED','TASK_EVIDENCE_RULE_CHANGED','TASK_DEPENDENCIES_CHANGED','TASK_CANCELLED','TASK_REOPENED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL)) AND ((kind = 'TASK_DUE_DATE_CHANGED') = (previous_due_date IS DISTINCT FROM new_due_date)) AND ((kind IN ('TASK_CANCELLED','TASK_REOPENED')) = (reason IS NOT NULL AND length(trim(reason)) BETWEEN 1 AND 2000))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE workflow_task_activities IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM workflow_task_activities WHERE kind = 'TASK_REOPENED') THEN
                    RAISE EXCEPTION 'Workflow task reopening evidence prevents rollback without losing task history.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED','TASK_DUE_DATE_CHANGED','TASK_EVIDENCE_RULE_CHANGED','TASK_DEPENDENCIES_CHANGED','TASK_CANCELLED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL)) AND ((kind = 'TASK_DUE_DATE_CHANGED') = (previous_due_date IS DISTINCT FROM new_due_date)) AND ((kind = 'TASK_CANCELLED') = (reason IS NOT NULL AND length(trim(reason)) BETWEEN 1 AND 2000))");
        }
    }
}
