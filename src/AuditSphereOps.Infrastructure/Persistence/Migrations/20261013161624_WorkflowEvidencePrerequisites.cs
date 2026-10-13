using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowEvidencePrerequisites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.CreateTable(
                name: "workflow_task_evidence_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_task_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_key = table.Column<Guid>(type: "uuid", nullable: false),
                    dependency_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audit_procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    task_revision = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_task_evidence_rules", x => x.id);
                    table.CheckConstraint("ck_workflow_evidence_rule_values", "dependency_type = 'DOMAIN_EVIDENCE_SATISFIED' AND version >= 1 AND task_revision >= 2\nAND state IN ('REQUIRED','WITHDRAWN') AND length(trim(reason)) BETWEEN 1 AND 1000\nAND request_digest ~ '^[0-9a-f]{64}$'\nAND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid\nAND rule_key <> '00000000-0000-0000-0000-000000000000'::uuid\nAND ((kind = 'CURRENT_MATERIALITY_APPROVED' AND source_id = '00000000-0000-0000-0000-000000000000'::uuid AND pbc_request_id IS NULL AND audit_procedure_id IS NULL)\n  OR (kind = 'PBC_ACCEPTED_OR_CLOSED' AND pbc_request_id IS NOT NULL AND source_id = pbc_request_id AND audit_procedure_id IS NULL)\n  OR (kind = 'CURRENT_PROCEDURE_REVIEWED' AND audit_procedure_id IS NOT NULL AND source_id = audit_procedure_id AND pbc_request_id IS NULL))");
                    table.ForeignKey(
                        name: "FK_workflow_task_evidence_rules_audit_procedures_firm_id_clien~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_evidence_rules_pbc_requests_firm_id_client_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_request_id },
                        principalTable: "pbc_requests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_evidence_rules_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_evidence_rules_workflow_task_bindings_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workflow_task_binding_id },
                        principalTable: "workflow_task_bindings",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED','TASK_DUE_DATE_CHANGED','TASK_EVIDENCE_RULE_CHANGED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL)) AND ((kind = 'TASK_DUE_DATE_CHANGED') = (previous_due_date IS DISTINCT FROM new_due_date))");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_evidence_rules_firm_id_actor_user_id_idempote~",
                table: "workflow_task_evidence_rules",
                columns: new[] { "firm_id", "actor_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_evidence_rules_firm_id_client_id_engagement_~1",
                table: "workflow_task_evidence_rules",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_evidence_rules_firm_id_client_id_engagement_~2",
                table: "workflow_task_evidence_rules",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workflow_task_binding_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_evidence_rules_firm_id_client_id_engagement_i~",
                table: "workflow_task_evidence_rules",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_evidence_rules_firm_id_workflow_task_binding_~",
                table: "workflow_task_evidence_rules",
                columns: new[] { "firm_id", "workflow_task_binding_id", "task_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_evidence_rules_firm_id_workflow_task_binding~1",
                table: "workflow_task_evidence_rules",
                columns: new[] { "firm_id", "workflow_task_binding_id", "rule_key", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_evidence_rules_firm_id_workflow_task_binding~2",
                table: "workflow_task_evidence_rules",
                columns: new[] { "firm_id", "workflow_task_binding_id", "kind", "source_id", "version" },
                unique: true);
            migrationBuilder.Sql(WorkflowEvidenceRuleGuardSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(WorkflowEvidenceRuleGuardSql.Down);
            migrationBuilder.DropTable(
                name: "workflow_task_evidence_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_activity_values",
                table: "workflow_task_activities",
                sql: "kind IN ('TASK_CREATED','TASK_ASSIGNED','TASK_REASSIGNED','TASK_COMPLETED','TASK_DUE_DATE_CHANGED') AND task_revision >= 1 AND ((kind IN ('TASK_ASSIGNED','TASK_REASSIGNED')) = (assignee_user_id IS NOT NULL)) AND ((kind = 'TASK_DUE_DATE_CHANGED') = (previous_due_date IS DISTINCT FROM new_due_date))");
        }
    }
}
