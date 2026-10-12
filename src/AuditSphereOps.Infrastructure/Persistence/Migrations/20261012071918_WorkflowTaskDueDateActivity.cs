using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskDueDateActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.AddColumn<DateOnly>(
                name: "new_due_date",
                table: "workflow_task_activities",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "previous_due_date",
                table: "workflow_task_activities",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED','TASK_DUE_DATE_CHANGED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL)) AND ((kind = 'TASK_DUE_DATE_CHANGED') = (previous_due_date IS DISTINCT FROM new_due_date))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM workflow_task_activities WHERE kind = 'TASK_DUE_DATE_CHANGED') THEN
                    RAISE EXCEPTION 'Cannot remove due-date history while immutable events exist';
                  END IF;
                END;
                $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.DropColumn(
                name: "new_due_date",
                table: "workflow_task_activities");

            migrationBuilder.DropColumn(
                name: "previous_due_date",
                table: "workflow_task_activities");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL))");
        }
    }
}
