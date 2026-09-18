using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PracticeCrmWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "proposals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_user_id",
                table: "proposals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "proposals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "deliverables",
                table: "proposals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "dependencies",
                table: "proposals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "exclusions",
                table: "proposals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "fee",
                table: "proposals",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "period_end",
                table: "proposals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "period_start",
                table: "proposals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "practice_client_id",
                table: "proposals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "response_at",
                table: "proposals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "response_reason",
                table: "proposals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "proposals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sent_at",
                table: "proposals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_id",
                table: "proposals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "jurisdiction",
                table: "practice_clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_number",
                table: "practice_clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "restricted_profile",
                table: "practice_clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "opportunities",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "entity_scope",
                table: "opportunities",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "expected_fee",
                table: "opportunities",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "next_action",
                table: "opportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_user_id",
                table: "opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "period_end",
                table: "opportunities",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "period_start",
                table: "opportunities",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "practice_client_id",
                table: "opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "probability",
                table: "opportunities",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consent_restrictions",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_user_id",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primary_contact_email",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primary_contact_name",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approved_scope",
                table: "client_contacts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "valid_from",
                table: "client_contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "valid_to",
                table: "client_contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "decided_by_user_id",
                table: "acceptance_decisions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "decided_at",
                table: "acceptance_decisions",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            // Preserve known legacy state spellings. Refuse incomplete legacy proposals
            // instead of inventing scope, deliverables, currency, or a service profile.
            migrationBuilder.Sql("""
                UPDATE leads SET status = upper(status);
                UPDATE opportunities
                SET stage = CASE WHEN upper(stage) = 'QUALIFIED' THEN 'DISCOVERY' ELSE upper(stage) END;
                UPDATE proposals SET status = upper(status);
                DO $do$
                BEGIN
                  IF EXISTS (
                    SELECT 1 FROM proposals
                    WHERE length(service_profile_id) = 0
                       OR length(scope) = 0
                       OR length(deliverables) = 0
                       OR length(currency) <> 3
                  ) THEN
                    RAISE EXCEPTION 'PracticeCrmWorkflow requires complete legacy proposal scope, deliverables, service profile, and currency';
                  END IF;
                END
                $do$;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_opportunities_firm_id_id",
                table: "opportunities",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_leads_firm_id_id",
                table: "leads",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_proposals_firm_id_opportunity_id_revision",
                table: "proposals",
                columns: new[] { "firm_id", "opportunity_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposals_firm_id_practice_client_id",
                table: "proposals",
                columns: new[] { "firm_id", "practice_client_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_proposal_content",
                table: "proposals",
                sql: "length(service_profile_id) > 0 AND length(scope) > 0 AND length(deliverables) > 0 AND length(currency) = 3");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proposal_state",
                table: "proposals",
                sql: "status IN ('DRAFT','INTERNAL_REVIEW','SENT','ACCEPTED','DECLINED','SUPERSEDED') AND revision >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_practice_client_name",
                table: "practice_clients",
                sql: "length(legal_name) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_firm_id_lead_id_stage",
                table: "opportunities",
                columns: new[] { "firm_id", "lead_id", "stage" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_firm_id_practice_client_id",
                table: "opportunities",
                columns: new[] { "firm_id", "practice_client_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities",
                sql: "stage IN ('DISCOVERY','PROPOSAL','NEGOTIATION','WON','LOST') AND length(service_route) > 0 AND length(entity_scope) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_leads_firm_id_status_created_at",
                table: "leads",
                columns: new[] { "firm_id", "status", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_lead_status",
                table: "leads",
                sql: "status IN ('NEW','QUALIFIED','UNQUALIFIED','LOST') AND length(name) > 0 AND length(source) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_client_contacts_firm_id_practice_client_id",
                table: "client_contacts",
                columns: new[] { "firm_id", "practice_client_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_contact",
                table: "client_contacts",
                sql: "length(full_name) > 0 AND length(email) > 0 AND length(role) > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_client_contacts_practice_clients_firm_id_practice_client_id",
                table: "client_contacts",
                columns: new[] { "firm_id", "practice_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_leads_firm_id_lead_id",
                table: "opportunities",
                columns: new[] { "firm_id", "lead_id" },
                principalTable: "leads",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_practice_clients_firm_id_practice_client_id",
                table: "opportunities",
                columns: new[] { "firm_id", "practice_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_proposals_opportunities_firm_id_opportunity_id",
                table: "proposals",
                columns: new[] { "firm_id", "opportunity_id" },
                principalTable: "opportunities",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_proposals_practice_clients_firm_id_practice_client_id",
                table: "proposals",
                columns: new[] { "firm_id", "practice_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_client_contacts_practice_clients_firm_id_practice_client_id",
                table: "client_contacts");

            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_leads_firm_id_lead_id",
                table: "opportunities");

            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_practice_clients_firm_id_practice_client_id",
                table: "opportunities");

            migrationBuilder.DropForeignKey(
                name: "FK_proposals_opportunities_firm_id_opportunity_id",
                table: "proposals");

            migrationBuilder.DropForeignKey(
                name: "FK_proposals_practice_clients_firm_id_practice_client_id",
                table: "proposals");

            migrationBuilder.DropIndex(
                name: "IX_proposals_firm_id_opportunity_id_revision",
                table: "proposals");

            migrationBuilder.DropIndex(
                name: "IX_proposals_firm_id_practice_client_id",
                table: "proposals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_proposal_content",
                table: "proposals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_proposal_state",
                table: "proposals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_practice_client_name",
                table: "practice_clients");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_opportunities_firm_id_id",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_firm_id_lead_id_stage",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_firm_id_practice_client_id",
                table: "opportunities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_leads_firm_id_id",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_leads_firm_id_status_created_at",
                table: "leads");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lead_status",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_client_contacts_firm_id_practice_client_id",
                table: "client_contacts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_client_contact",
                table: "client_contacts");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "deliverables",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "dependencies",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "exclusions",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "fee",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "period_end",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "period_start",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "practice_client_id",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "response_at",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "response_reason",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "sent_at",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "supersedes_id",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "jurisdiction",
                table: "practice_clients");

            migrationBuilder.DropColumn(
                name: "registration_number",
                table: "practice_clients");

            migrationBuilder.DropColumn(
                name: "restricted_profile",
                table: "practice_clients");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "entity_scope",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "expected_fee",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "next_action",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "period_end",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "period_start",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "practice_client_id",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "probability",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "consent_restrictions",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "primary_contact_email",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "primary_contact_name",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "approved_scope",
                table: "client_contacts");

            migrationBuilder.DropColumn(
                name: "valid_from",
                table: "client_contacts");

            migrationBuilder.DropColumn(
                name: "valid_to",
                table: "client_contacts");

            migrationBuilder.AlterColumn<Guid>(
                name: "decided_by_user_id",
                table: "acceptance_decisions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "decided_at",
                table: "acceptance_decisions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
