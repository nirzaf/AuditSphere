using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalResponseProvenanceAndRenderedDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recipient_authority_basis",
                table: "commercial_notifications",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recipient_contact_id",
                table: "commercial_notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recipient_routing_id",
                table: "commercial_notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_proposals_firm_id_id",
                table: "proposals",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_commercial_notifications_firm_id_id",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_client_contacts_firm_id_id",
                table: "client_contacts",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_client_contact_routings_firm_id_id",
                table: "client_contact_routings",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "proposal_response_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commercial_notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    offer_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    dispatch_recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    recipient_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_routing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authority_basis = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    respondent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    respondent_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_response_records", x => x.id);
                    table.CheckConstraint("ck_proposal_response_record_values", "decision IN ('ACCEPTED','DECLINED') AND offer_sha256 ~ '^[a-f0-9]{64}$' AND length(dispatch_recipient) > 0 AND length(trim(respondent_name)) > 0 AND length(trim(respondent_email)) > 0 AND (channel IS NULL OR channel IN ('EMAIL','SIGNED_LETTER','IN_PERSON','PHONE')) AND (decision <> 'ACCEPTED' OR (length(trim(evidence_reference)) > 0 AND channel IS NOT NULL)) AND length(reason) <= 1000 AND ((authority_basis = 'CLIENT_COMMERCIAL_ROUTING' AND recipient_contact_id IS NOT NULL AND recipient_routing_id IS NOT NULL) OR (authority_basis IN ('PROSPECT_PRIMARY_CONTACT','LEGACY_DISPATCH_SNAPSHOT') AND recipient_contact_id IS NULL AND recipient_routing_id IS NULL))");
                    table.ForeignKey(
                        name: "FK_proposal_response_records_client_contact_routings_firm_id_r~",
                        columns: x => new { x.firm_id, x.recipient_routing_id },
                        principalTable: "client_contact_routings",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_proposal_response_records_client_contacts_firm_id_recipient~",
                        columns: x => new { x.firm_id, x.recipient_contact_id },
                        principalTable: "client_contacts",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_proposal_response_records_commercial_notifications_firm_id_~",
                        columns: x => new { x.firm_id, x.commercial_notification_id },
                        principalTable: "commercial_notifications",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_proposal_response_records_proposals_firm_id_proposal_id",
                        columns: x => new { x.firm_id, x.proposal_id },
                        principalTable: "proposals",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_proposal_response_records_users_firm_id_recorded_by_user_id",
                        columns: x => new { x.firm_id, x.recorded_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_notifications_firm_id_recipient_contact_id",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "recipient_contact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_notifications_firm_id_recipient_routing_id",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "recipient_routing_id" });

            // Preserve the authority that was captured by older dispatches without pretending their original route
            // source was recorded. Newly created sends use one of the explicit current authority bases.
            migrationBuilder.Sql("UPDATE commercial_notifications SET recipient_authority_basis = 'LEGACY_DISPATCH_SNAPSHOT' WHERE kind = 'PROPOSAL' AND recipient_authority_basis IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proposal_notification_recipient_basis",
                table: "commercial_notifications",
                sql: "kind <> 'PROPOSAL' OR ((recipient_authority_basis = 'CLIENT_COMMERCIAL_ROUTING' AND recipient_contact_id IS NOT NULL AND recipient_routing_id IS NOT NULL) OR (recipient_authority_basis IN ('PROSPECT_PRIMARY_CONTACT','LEGACY_DISPATCH_SNAPSHOT') AND recipient_contact_id IS NULL AND recipient_routing_id IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_response_records_firm_id_commercial_notification_id",
                table: "proposal_response_records",
                columns: new[] { "firm_id", "commercial_notification_id" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_response_records_firm_id_proposal_id",
                table: "proposal_response_records",
                columns: new[] { "firm_id", "proposal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposal_response_records_firm_id_recipient_contact_id",
                table: "proposal_response_records",
                columns: new[] { "firm_id", "recipient_contact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_response_records_firm_id_recipient_routing_id",
                table: "proposal_response_records",
                columns: new[] { "firm_id", "recipient_routing_id" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_response_records_firm_id_recorded_by_user_id",
                table: "proposal_response_records",
                columns: new[] { "firm_id", "recorded_by_user_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_commercial_notifications_client_contact_routings_firm_id_re~",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "recipient_routing_id" },
                principalTable: "client_contact_routings",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_commercial_notifications_client_contacts_firm_id_recipient_~",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "recipient_contact_id" },
                principalTable: "client_contacts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("CREATE TRIGGER proposal_response_records_append_only BEFORE UPDATE OR DELETE ON proposal_response_records FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM proposal_response_records) OR EXISTS (SELECT 1 FROM commercial_notifications WHERE kind = 'PROPOSAL' AND recipient_authority_basis <> 'LEGACY_DISPATCH_SNAPSHOT') THEN RAISE EXCEPTION 'proposal response and recipient provenance is acceptance evidence and cannot be downgraded'; END IF; END $$;");

            migrationBuilder.DropForeignKey(
                name: "FK_commercial_notifications_client_contact_routings_firm_id_re~",
                table: "commercial_notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_commercial_notifications_client_contacts_firm_id_recipient_~",
                table: "commercial_notifications");

            migrationBuilder.DropTable(
                name: "proposal_response_records");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_proposals_firm_id_id",
                table: "proposals");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_commercial_notifications_firm_id_id",
                table: "commercial_notifications");

            migrationBuilder.DropIndex(
                name: "IX_commercial_notifications_firm_id_recipient_contact_id",
                table: "commercial_notifications");

            migrationBuilder.DropIndex(
                name: "IX_commercial_notifications_firm_id_recipient_routing_id",
                table: "commercial_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_proposal_notification_recipient_basis",
                table: "commercial_notifications");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_client_contacts_firm_id_id",
                table: "client_contacts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_client_contact_routings_firm_id_id",
                table: "client_contact_routings");

            migrationBuilder.DropColumn(
                name: "recipient_authority_basis",
                table: "commercial_notifications");

            migrationBuilder.DropColumn(
                name: "recipient_contact_id",
                table: "commercial_notifications");

            migrationBuilder.DropColumn(
                name: "recipient_routing_id",
                table: "commercial_notifications");
        }
    }
}
