using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskPeriodDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_task_state",
                table: "work_tasks");

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "work_tasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reporting_period_id",
                table: "work_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_firm_id_client_id_reporting_period_id_status_due~",
                table: "work_tasks",
                columns: new[] { "firm_id", "client_id", "reporting_period_id", "status", "due_date" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_task_state",
                table: "work_tasks",
                sql: "status IN ('OPEN','IN_PROGRESS','COMPLETED','CANCELLED') AND length(title) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL) AND (reporting_period_id IS NULL OR client_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_work_tasks_client_reporting_periods_firm_id_client_id_repor~",
                table: "work_tasks",
                columns: new[] { "firm_id", "client_id", "reporting_period_id" },
                principalTable: "client_reporting_periods",
                principalColumns: new[] { "firm_id", "client_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_work_tasks_client_reporting_periods_firm_id_client_id_repor~",
                table: "work_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_tasks_firm_id_client_id_reporting_period_id_status_due~",
                table: "work_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_task_state",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "reporting_period_id",
                table: "work_tasks");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_task_state",
                table: "work_tasks",
                sql: "status IN ('OPEN','IN_PROGRESS','COMPLETED','CANCELLED') AND length(title) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL)");
        }
    }
}
