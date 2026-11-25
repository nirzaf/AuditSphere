using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcceptanceFirstCommercialOnboarding : Migration
    {
        private const string LegacyLeadActivityShape = "(action <> 'LEAD_CREATED' OR (to_state = 'NEW' AND source_channel IS NOT NULL)) AND (action <> 'LEAD_QUALIFIED' OR (from_state = 'NEW' AND to_state = 'QUALIFIED')) AND (action <> 'OPPORTUNITY_CREATED' OR (opportunity_id IS NOT NULL AND service_route IS NOT NULL)) AND (action <> 'PROPOSAL_REVISION_CREATED' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND to_state = 'DRAFT')) AND (action <> 'PROPOSAL_REVIEWED' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND from_state = 'DRAFT' AND to_state = 'INTERNAL_REVIEW')) AND (action <> 'PROPOSAL_MARKED_SENT' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND from_state = 'INTERNAL_REVIEW' AND to_state = 'SENT')) AND (action NOT IN ('PROPOSAL_ACCEPTED','PROPOSAL_DECLINED') OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND from_state = 'SENT' AND ((action = 'PROPOSAL_ACCEPTED' AND to_state = 'ACCEPTED') OR (action = 'PROPOSAL_DECLINED' AND to_state = 'DECLINED')))) AND (action <> 'CLIENT_CONVERSION_LINKED' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND client_id IS NOT NULL AND to_state = 'PROSPECT'))";
        private const string AcceptanceFirstLeadActivityShape = "(action <> 'LEAD_CREATED' OR (to_state = 'NEW' AND source_channel IS NOT NULL)) AND (action <> 'LEAD_QUALIFIED' OR (from_state = 'NEW' AND to_state = 'QUALIFIED')) AND (action <> 'OPPORTUNITY_CREATED' OR (opportunity_id IS NOT NULL AND service_route IS NOT NULL)) AND (action <> 'PROPOSAL_REVISION_CREATED' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND to_state = 'DRAFT')) AND (action <> 'PROPOSAL_REVIEWED' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND from_state = 'DRAFT' AND to_state = 'INTERNAL_REVIEW')) AND (action <> 'PROPOSAL_MARKED_SENT' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND from_state = 'INTERNAL_REVIEW' AND to_state = 'SENT')) AND (action NOT IN ('PROPOSAL_ACCEPTED','PROPOSAL_DECLINED') OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND from_state = 'SENT' AND ((action = 'PROPOSAL_ACCEPTED' AND to_state = 'ACCEPTED') OR (action = 'PROPOSAL_DECLINED' AND to_state = 'DECLINED')))) AND (action <> 'CLIENT_CONVERSION_LINKED' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND client_id IS NOT NULL AND to_state IS NOT NULL))";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lead_activity_shape",
                table: "lead_activity_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lead_activity_shape",
                table: "lead_activity_events",
                sql: AcceptanceFirstLeadActivityShape);

            migrationBuilder.DropCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_acceptance_decision_evidence",
                table: "acceptance_decisions");

            migrationBuilder.AlterColumn<decimal>(
                name: "expected_fee",
                table: "opportunities",
                type: "numeric(19,6)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,6)");

            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "opportunities",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "acceptance_client_id",
                table: "opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "discovery_identity_sha256",
                table: "opportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "entity_scope_confirmed_at",
                table: "opportunities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "entity_scope_confirmed_by_user_id",
                table: "opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "opportunity_basis_sha256",
                table: "acceptance_decisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "opportunity_id",
                table: "acceptance_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_firm_id_acceptance_client_id",
                table: "opportunities",
                columns: new[] { "firm_id", "acceptance_client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_firm_id_entity_scope_confirmed_by_user_id",
                table: "opportunities",
                columns: new[] { "firm_id", "entity_scope_confirmed_by_user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities",
                sql: "stage IN ('DISCOVERY','PROPOSAL','NEGOTIATION','WON','LOST') AND length(service_route) > 0 AND length(entity_scope) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND ((expected_fee IS NULL AND currency IS NULL) OR (expected_fee IS NOT NULL AND expected_fee >= 0 AND currency ~ '^[A-Z]{3}$')) AND ((entity_scope_confirmed_by_user_id IS NULL AND entity_scope_confirmed_at IS NULL) OR (entity_scope_confirmed_by_user_id IS NOT NULL AND entity_scope_confirmed_at IS NOT NULL)) AND (discovery_identity_sha256 IS NULL OR discovery_identity_sha256 ~ '^[a-f0-9]{64}$') AND (probability IS NULL OR probability BETWEEN 0 AND 100)");

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_opportunity_generation",
                table: "acceptance_decisions",
                columns: new[] { "firm_id", "opportunity_id", "generation" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_acceptance_decision_evidence",
                table: "acceptance_decisions",
                sql: "((decision = 'Pending') OR (length(trim(rationale)) > 0 AND length(trim(evaluation_template_version)) > 0 AND evaluation_snapshot_digest ~ '^[0-9a-f]{64}$')) AND ((decision = 'AcceptedWithConditions' AND length(trim(conditions)) > 0) OR decision <> 'AcceptedWithConditions') AND ((opportunity_id IS NULL AND opportunity_basis_sha256 IS NULL) OR (opportunity_id IS NOT NULL AND ((decision = 'Pending' AND opportunity_basis_sha256 IS NULL) OR (decision <> 'Pending' AND opportunity_basis_sha256 ~ '^[0-9a-f]{64}$'))))");

            migrationBuilder.AddForeignKey(
                name: "FK_acceptance_decisions_opportunities_firm_id_opportunity_id",
                table: "acceptance_decisions",
                columns: new[] { "firm_id", "opportunity_id" },
                principalTable: "opportunities",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_practice_clients_firm_id_acceptance_client_id",
                table: "opportunities",
                columns: new[] { "firm_id", "acceptance_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_users_firm_id_entity_scope_confirmed_by_user_~",
                table: "opportunities",
                columns: new[] { "firm_id", "entity_scope_confirmed_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                LOCK TABLE lead_activity_events, opportunities, acceptance_decisions IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM lead_activity_events WHERE NOT ({LegacyLeadActivityShape}))
                       OR EXISTS (SELECT 1 FROM opportunities WHERE acceptance_client_id IS NOT NULL
                           OR discovery_identity_sha256 IS NOT NULL OR entity_scope_confirmed_by_user_id IS NOT NULL
                           OR entity_scope_confirmed_at IS NOT NULL OR expected_fee IS NULL OR currency IS NULL)
                       OR EXISTS (SELECT 1 FROM acceptance_decisions WHERE opportunity_id IS NOT NULL
                           OR opportunity_basis_sha256 IS NOT NULL) THEN
                        RAISE EXCEPTION USING ERRCODE = '55000',
                            MESSAGE = 'Retained acceptance-first CRM evidence prevents downgrade.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_lead_activity_shape",
                table: "lead_activity_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lead_activity_shape",
                table: "lead_activity_events",
                sql: LegacyLeadActivityShape);

            migrationBuilder.DropForeignKey(
                name: "FK_acceptance_decisions_opportunities_firm_id_opportunity_id",
                table: "acceptance_decisions");

            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_practice_clients_firm_id_acceptance_client_id",
                table: "opportunities");

            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_users_firm_id_entity_scope_confirmed_by_user_~",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_firm_id_acceptance_client_id",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_firm_id_entity_scope_confirmed_by_user_id",
                table: "opportunities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "ix_acceptance_opportunity_generation",
                table: "acceptance_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_acceptance_decision_evidence",
                table: "acceptance_decisions");

            migrationBuilder.DropColumn(
                name: "acceptance_client_id",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "discovery_identity_sha256",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "entity_scope_confirmed_at",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "entity_scope_confirmed_by_user_id",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "opportunity_basis_sha256",
                table: "acceptance_decisions");

            migrationBuilder.DropColumn(
                name: "opportunity_id",
                table: "acceptance_decisions");

            migrationBuilder.AlterColumn<decimal>(
                name: "expected_fee",
                table: "opportunities",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,6)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "opportunities",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities",
                sql: "stage IN ('DISCOVERY','PROPOSAL','NEGOTIATION','WON','LOST') AND length(service_route) > 0 AND length(entity_scope) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND expected_fee >= 0 AND (probability IS NULL OR probability BETWEEN 0 AND 100) AND currency ~ '^[A-Z]{3}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_acceptance_decision_evidence",
                table: "acceptance_decisions",
                sql: "((decision = 'Pending') OR (length(trim(rationale)) > 0 AND length(trim(evaluation_template_version)) > 0 AND evaluation_snapshot_digest ~ '^[0-9a-f]{64}$')) AND ((decision = 'AcceptedWithConditions' AND length(trim(conditions)) > 0) OR decision <> 'AcceptedWithConditions')");
        }
    }
}
