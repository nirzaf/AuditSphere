using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_task_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creation_request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    phase = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    source_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    planned_effort_minutes = table.Column<int>(type: "integer", nullable: true),
                    due_date_policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    assigned_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    assignee_user_id_snapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    assignment_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    supersession_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_task_bindings", x => x.id);
                    table.UniqueConstraint("AK_workflow_task_bindings_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_task_binding_phase", "phase IN ('CLIENT_ONBOARDING','AUDIT_PLANNING','AUDIT_FIELDWORK','MANAGER_REVIEW','PARTNER_SIGNOFF','REPORT_DELIVERY','ARCHIVE_CLOSE')");
                    table.CheckConstraint("ck_workflow_task_binding_values", "ordinal BETWEEN 1 AND 100000 AND generation >= 1 AND revision >= 1 AND (planned_effort_minutes IS NULL OR planned_effort_minutes BETWEEN 1 AND 1000000) AND due_date_policy IN ('MANUAL','FIXED','TEMPLATE_OFFSET') AND assignment_source IN ('MANUAL','TEMPLATE','MANUAL_REASSIGNMENT','AUTOMATION') AND (assigned_role IS NULL OR assigned_role IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND length(source_key) BETWEEN 1 AND 160 AND creation_request_digest ~ '^[0-9a-f]{64}$' AND ((template_revision_id IS NULL AND template_step_id IS NULL) OR (template_revision_id IS NOT NULL AND template_step_id IS NOT NULL)) AND (cancellation_reason IS NULL OR length(trim(cancellation_reason)) > 0) AND (superseded_at IS NULL OR (supersession_reason IS NOT NULL AND length(trim(supersession_reason)) > 0))");
                    table.ForeignKey(
                        name: "FK_workflow_task_bindings_engagements_firm_id_client_id_engage~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_bindings_users_firm_id_assignee_user_id_snaps~",
                        columns: x => new { x.firm_id, x.assignee_user_id_snapshot },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_bindings_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_bindings_work_tasks_firm_id_work_task_id",
                        columns: x => new { x.firm_id, x.work_task_id },
                        principalTable: "work_tasks",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_bindings_firm_id_assignee_user_id_snapshot_ph~",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "assignee_user_id_snapshot", "phase" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_bindings_firm_id_client_id_engagement_id",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_bindings_firm_id_created_by_user_id",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_bindings_firm_id_engagement_id_phase_ordinal",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "engagement_id", "phase", "ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_bindings_firm_id_work_task_id",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "work_task_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_workflow_task_bindings_source_generation",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "engagement_id", "source_key", "generation" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION guard_workflow_task_binding() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE task_client_id uuid;
                DECLARE task_engagement_id uuid;
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'workflow task bindings are retained';
                  END IF;

                  IF TG_OP = 'UPDATE' THEN
                    IF NEW.id IS DISTINCT FROM OLD.id OR
                       NEW.firm_id IS DISTINCT FROM OLD.firm_id OR
                       NEW.client_id IS DISTINCT FROM OLD.client_id OR
                       NEW.engagement_id IS DISTINCT FROM OLD.engagement_id OR
                       NEW.work_task_id IS DISTINCT FROM OLD.work_task_id OR
                       NEW.creation_request_digest IS DISTINCT FROM OLD.creation_request_digest OR
                       NEW.template_revision_id IS DISTINCT FROM OLD.template_revision_id OR
                       NEW.template_step_id IS DISTINCT FROM OLD.template_step_id OR
                       NEW.source_key IS DISTINCT FROM OLD.source_key OR
                       NEW.generation IS DISTINCT FROM OLD.generation OR
                       NEW.created_at IS DISTINCT FROM OLD.created_at OR
                       NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id THEN
                      RAISE EXCEPTION 'workflow task binding identity is immutable';
                    END IF;
                    IF OLD.revision = 9223372036854775807 OR NEW.revision <> OLD.revision + 1 THEN
                      RAISE EXCEPTION 'workflow task binding revision must advance exactly once';
                    END IF;
                  END IF;

                  SELECT client_id, engagement_id INTO task_client_id, task_engagement_id
                    FROM work_tasks WHERE firm_id = NEW.firm_id AND id = NEW.work_task_id;
                  IF NOT FOUND OR task_client_id IS DISTINCT FROM NEW.client_id OR
                     task_engagement_id IS DISTINCT FROM NEW.engagement_id THEN
                    RAISE EXCEPTION 'workflow task binding scope must match its work task';
                  END IF;
                  RETURN NEW;
                END;
                $$;

                CREATE TRIGGER guard_workflow_task_binding
                BEFORE INSERT OR UPDATE OR DELETE ON workflow_task_bindings
                FOR EACH ROW EXECUTE FUNCTION guard_workflow_task_binding();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS guard_workflow_task_binding ON workflow_task_bindings; DROP FUNCTION IF EXISTS guard_workflow_task_binding();");
            migrationBuilder.DropTable(
                name: "workflow_task_bindings");
        }
    }
}
