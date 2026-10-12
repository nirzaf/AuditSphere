using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskActivityBindingGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_workflow_task_activities_append_only ON workflow_task_activities;
                DROP FUNCTION IF EXISTS prevent_workflow_task_activity_mutation();
                CREATE OR REPLACE FUNCTION prevent_workflow_task_activity_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'INSERT' THEN
                    IF NOT EXISTS (
                      SELECT 1 FROM workflow_task_bindings
                      WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id
                        AND engagement_id = NEW.engagement_id AND id = NEW.workflow_task_binding_id
                        AND work_task_id = NEW.work_task_id
                    ) THEN
                      RAISE EXCEPTION 'workflow task activity must match its exact task binding';
                    END IF;
                    RETURN NEW;
                  END IF;
                  RAISE EXCEPTION 'workflow task activity is append-only';
                END;
                $$;
                CREATE TRIGGER trg_workflow_task_activities_append_only
                BEFORE INSERT OR UPDATE OR DELETE ON workflow_task_activities
                FOR EACH ROW EXECUTE FUNCTION prevent_workflow_task_activity_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_workflow_task_activities_append_only ON workflow_task_activities;
                DROP FUNCTION IF EXISTS prevent_workflow_task_activity_mutation();
                CREATE OR REPLACE FUNCTION prevent_workflow_task_activity_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'workflow task activity is append-only';
                END;
                $$;
                CREATE TRIGGER trg_workflow_task_activities_append_only
                BEFORE UPDATE OR DELETE ON workflow_task_activities
                FOR EACH ROW EXECUTE FUNCTION prevent_workflow_task_activity_mutation();
                """);
        }
    }
}
