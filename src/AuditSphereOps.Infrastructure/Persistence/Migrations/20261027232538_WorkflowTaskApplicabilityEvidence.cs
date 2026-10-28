using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskApplicabilityEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_task_applicability_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_task_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_acceptance_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    task_revision = table.Column<long>(type: "bigint", nullable: false),
                    binding_generation = table.Column<int>(type: "integer", nullable: false),
                    engagement_generation = table.Column<long>(type: "bigint", nullable: false),
                    condition_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    basis_json = table.Column<string>(type: "character varying(32768)", maxLength: 32768, nullable: false),
                    basis_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_task_applicability_proposals", x => x.id);
                    table.UniqueConstraint("AK_workflow_task_applicability_proposals_firm_id_client_id_eng~", x => new { x.firm_id, x.client_id, x.engagement_id, x.workflow_task_binding_id, x.id, x.actor_user_id });
                    table.UniqueConstraint("AK_workflow_task_applicability_proposals_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_task_applicability_proposal_values", "version >= 1 AND task_revision >= 1 AND binding_generation >= 1 AND engagement_generation >= 1 AND actor_epoch >= 0 AND length(trim(reason)) BETWEEN 1 AND 1000 AND length(basis_json) BETWEEN 2 AND 32768 AND jsonb_typeof(basis_json::jsonb) = 'object' AND basis_hash = encode(sha256(convert_to(basis_json, 'UTF8')), 'hex') AND condition_digest ~ '^[0-9a-f]{64}$' AND request_digest ~ '^[0-9a-f]{64}$' AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_workflow_task_applicability_proposals_pbc_acceptance_receip~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_acceptance_receipt_id },
                        principalTable: "pbc_acceptance_receipts",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_applicability_proposals_users_firm_id_actor_u~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_applicability_proposals_workflow_task_binding~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workflow_task_binding_id },
                        principalTable: "workflow_task_bindings",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_applicability_proposals_workflow_template_ste~",
                        columns: x => new { x.firm_id, x.template_revision_id, x.template_step_id },
                        principalTable: "workflow_template_steps",
                        principalColumns: new[] { "firm_id", "template_revision_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_task_applicability_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_task_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    basis_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_task_applicability_decisions", x => x.id);
                    table.UniqueConstraint("AK_workflow_task_applicability_decisions_firm_id_client_id_eng~", x => new { x.firm_id, x.client_id, x.engagement_id, x.workflow_task_binding_id, x.proposal_id, x.id });
                    table.UniqueConstraint("AK_workflow_task_applicability_decisions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_task_applicability_decision_values", "version >= 1 AND actor_epoch >= 0 AND actor_user_id <> proposer_user_id AND kind IN ('APPROVE','RETURN','REVOKE') AND length(trim(reason)) BETWEEN 1 AND 1000 AND basis_hash ~ '^[0-9a-f]{64}$' AND request_digest ~ '^[0-9a-f]{64}$' AND ((version = 1 AND previous_decision_id IS NULL AND kind IN ('APPROVE','RETURN')) OR (version > 1 AND previous_decision_id IS NOT NULL AND kind = 'REVOKE')) AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_workflow_task_applicability_decisions_users_firm_id_actor_u~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_applicability_decision_previous",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workflow_task_binding_id, x.proposal_id, x.previous_decision_id },
                        principalTable: "workflow_task_applicability_decisions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "workflow_task_binding_id", "proposal_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_applicability_decision_proposal",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workflow_task_binding_id, x.proposal_id, x.proposer_user_id },
                        principalTable: "workflow_task_applicability_proposals",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "workflow_task_binding_id", "id", "actor_user_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_applicability_decisions_firm_id_actor_user_id~",
                table: "workflow_task_applicability_decisions",
                columns: new[] { "firm_id", "actor_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_applicability_decisions_firm_id_client_id_en~1",
                table: "workflow_task_applicability_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workflow_task_binding_id", "proposal_id", "proposer_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_applicability_decisions_firm_id_client_id_eng~",
                table: "workflow_task_applicability_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workflow_task_binding_id", "proposal_id", "previous_decision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_applicability_decisions_firm_id_proposal_id_v~",
                table: "workflow_task_applicability_decisions",
                columns: new[] { "firm_id", "proposal_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_applicability_proposals_firm_id_actor_user_id~",
                table: "workflow_task_applicability_proposals",
                columns: new[] { "firm_id", "actor_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_applicability_proposals_firm_id_client_id_eng~",
                table: "workflow_task_applicability_proposals",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_acceptance_receipt_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_applicability_proposals_firm_id_template_revi~",
                table: "workflow_task_applicability_proposals",
                columns: new[] { "firm_id", "template_revision_id", "template_step_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_applicability_proposals_firm_id_workflow_task~",
                table: "workflow_task_applicability_proposals",
                columns: new[] { "firm_id", "workflow_task_binding_id", "version" },
                unique: true);
            migrationBuilder.Sql(WorkflowApplicabilityGuardSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(WorkflowApplicabilityGuardSql.Down);
            migrationBuilder.DropTable(
                name: "workflow_task_applicability_decisions");

            migrationBuilder.DropTable(
                name: "workflow_task_applicability_proposals");
        }
    }
}
