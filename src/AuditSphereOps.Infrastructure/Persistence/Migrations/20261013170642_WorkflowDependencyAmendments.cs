using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowDependencyAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.CreateTable(
                name: "workflow_task_dependency_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predecessor_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    successor_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    task_revision = table.Column<long>(type: "bigint", nullable: false),
                    predecessor_revision = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_task_dependency_revisions", x => x.id);
                    table.CheckConstraint("ck_workflow_dependency_revision_values", "predecessor_binding_id <> successor_binding_id AND version >= 1 AND task_revision >= 2 AND predecessor_revision >= 1 AND length(trim(reason)) BETWEEN 1 AND 1000 AND request_digest ~ '^[0-9a-f]{64}$' AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_workflow_task_dependency_revisions_users_firm_id_actor_user~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_dependency_revisions_workflow_task_bindings_f~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.predecessor_binding_id },
                        principalTable: "workflow_task_bindings",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_dependency_revisions_workflow_task_bindings_~1",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.successor_binding_id },
                        principalTable: "workflow_task_bindings",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED','TASK_DUE_DATE_CHANGED','TASK_EVIDENCE_RULE_CHANGED','TASK_DEPENDENCIES_CHANGED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL)) AND ((kind = 'TASK_DUE_DATE_CHANGED') = (previous_due_date IS DISTINCT FROM new_due_date))");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependency_revisions_firm_id_actor_user_id_id~",
                table: "workflow_task_dependency_revisions",
                columns: new[] { "firm_id", "actor_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependency_revisions_firm_id_client_id_engag~1",
                table: "workflow_task_dependency_revisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "successor_binding_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependency_revisions_firm_id_client_id_engage~",
                table: "workflow_task_dependency_revisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "predecessor_binding_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependency_revisions_firm_id_successor_bindi~1",
                table: "workflow_task_dependency_revisions",
                columns: new[] { "firm_id", "successor_binding_id", "predecessor_binding_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependency_revisions_firm_id_successor_bindin~",
                table: "workflow_task_dependency_revisions",
                columns: new[] { "firm_id", "successor_binding_id", "task_revision" },
                unique: true);
            migrationBuilder.Sql(WorkflowDependencyRevisionGuardSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(WorkflowDependencyRevisionGuardSql.Down);
            migrationBuilder.DropTable(
                name: "workflow_task_dependency_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED','TASK_DUE_DATE_CHANGED','TASK_EVIDENCE_RULE_CHANGED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL)) AND ((kind = 'TASK_DUE_DATE_CHANGED') = (previous_due_date IS DISTINCT FROM new_due_date))");
        }
    }
}
