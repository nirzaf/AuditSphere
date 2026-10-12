using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_task_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_task_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_revision = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_task_activities", x => x.id);
                    table.UniqueConstraint("AK_workflow_task_activities_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_task_activity_values", "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_workflow_task_activities_engagements_firm_id_client_id_enga~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_activities_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_activities_users_firm_id_assignee_user_id",
                        columns: x => new { x.firm_id, x.assignee_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_activities_work_tasks_firm_id_work_task_id",
                        columns: x => new { x.firm_id, x.work_task_id },
                        principalTable: "work_tasks",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_activities_workflow_task_bindings_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workflow_task_binding_id },
                        principalTable: "workflow_task_bindings",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_activities_engagement_time",
                table: "workflow_task_activities",
                columns: new[] { "firm_id", "engagement_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_activities_firm_id_actor_user_id",
                table: "workflow_task_activities",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_activities_firm_id_assignee_user_id",
                table: "workflow_task_activities",
                columns: new[] { "firm_id", "assignee_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_activities_firm_id_client_id_engagement_id_wo~",
                table: "workflow_task_activities",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workflow_task_binding_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_activities_task_time",
                table: "workflow_task_activities",
                columns: new[] { "firm_id", "work_task_id", "occurred_at", "id" });

            migrationBuilder.Sql("""
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_task_activities");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_workflow_task_activity_mutation();");
        }
    }
}
