using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowReminderScheduleWindowLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_scheduled_window_count",
                table: "workflow_reminder_rule_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_reminder_rule_revision_schedule_window_bound",
                table: "workflow_reminder_rule_revisions",
                sql: "max_scheduled_window_count IS NULL OR max_scheduled_window_count BETWEEN 1 AND 100");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION guard_workflow_reminder_schedule_bound_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD.status <> 'DRAFT' OR NEW.status <> 'DRAFT')
                     AND NEW.max_scheduled_window_count IS DISTINCT FROM OLD.max_scheduled_window_count THEN
                    RAISE EXCEPTION 'reviewed workflow reminder schedule bound is immutable';
                  END IF;
                  RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_workflow_reminder_schedule_bound_guard
                  BEFORE UPDATE ON workflow_reminder_rule_revisions
                  FOR EACH ROW EXECUTE FUNCTION guard_workflow_reminder_schedule_bound_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM workflow_reminder_rule_revisions
                             WHERE max_scheduled_window_count IS NOT NULL) THEN
                    RAISE EXCEPTION 'workflow reminder schedule bounds must be retained';
                  END IF;
                END;
                $$;

                DROP TRIGGER IF EXISTS trg_workflow_reminder_schedule_bound_guard
                  ON workflow_reminder_rule_revisions;
                DROP FUNCTION IF EXISTS guard_workflow_reminder_schedule_bound_mutation();
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_reminder_rule_revision_schedule_window_bound",
                table: "workflow_reminder_rule_revisions");

            migrationBuilder.DropColumn(
                name: "max_scheduled_window_count",
                table: "workflow_reminder_rule_revisions");
        }
    }
}
