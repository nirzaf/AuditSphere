using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientPortalOnboardingAndDelegation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_portal_first_sign_ins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_path = table.Column<string>(type: "text", nullable: false),
                    sign_in_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terms_version = table.Column<string>(type: "text", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_portal_first_sign_ins", x => x.id);
                    table.CheckConstraint("ck_client_portal_first_sign_in_values", "identity_path IN ('PROVISIONED_MEMBER','EXTERNAL_IDENTITY','UNOBSERVED_IDENTITY') AND length(terms_version) > 0 AND (identity_path <> 'PROVISIONED_MEMBER' OR sign_in_observed_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_client_portal_first_sign_ins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_portal_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practice_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_email = table.Column<string>(type: "text", nullable: false),
                    source_proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    activated_engagement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_portal_intents", x => x.id);
                    table.CheckConstraint("ck_client_portal_intent_values", "state IN ('AWAITING_ACCEPTANCE','READY_TO_INVITE','INVITED') AND length(recipient_email) > 3 AND (state = 'AWAITING_ACCEPTANCE' OR activated_engagement_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_client_portal_intents_client_contacts_client_contact_id",
                        column: x => x.client_contact_id,
                        principalTable: "client_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_portal_intents_practice_clients_practice_client_id",
                        column: x => x.practice_client_id,
                        principalTable: "practice_clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pbc_request_delegations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegate_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_request_delegations", x => x.id);
                    table.CheckConstraint("ck_pbc_request_delegation_values", "delegator_user_id <> delegate_user_id AND ((revoked_at IS NULL) = (revoked_by_user_id IS NULL))");
                    table.ForeignKey(
                        name: "FK_pbc_request_delegations_pbc_requests_pbc_request_id",
                        column: x => x.pbc_request_id,
                        principalTable: "pbc_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_portal_first_sign_ins_firm_id_user_id",
                table: "client_portal_first_sign_ins",
                columns: new[] { "firm_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_portal_first_sign_ins_user_id",
                table: "client_portal_first_sign_ins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_client_portal_intents_client_contact_id",
                table: "client_portal_intents",
                column: "client_contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_client_portal_intents_firm_id_practice_client_id",
                table: "client_portal_intents",
                columns: new[] { "firm_id", "practice_client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_portal_intents_practice_client_id",
                table: "client_portal_intents",
                column: "practice_client_id");

            migrationBuilder.CreateIndex(
                name: "IX_pbc_request_delegations_firm_id_delegate_user_id",
                table: "pbc_request_delegations",
                columns: new[] { "firm_id", "delegate_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_request_delegations_firm_id_pbc_request_id_delegate_use~",
                table: "pbc_request_delegations",
                columns: new[] { "firm_id", "pbc_request_id", "delegate_user_id" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_pbc_request_delegations_pbc_request_id",
                table: "pbc_request_delegations",
                column: "pbc_request_id");
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_client_portal_first_sign_in_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'a client portal first sign-in record is immutable evidence';
                END;
                $$;
                CREATE TRIGGER trg_client_portal_first_sign_ins_append_only BEFORE UPDATE OR DELETE ON client_portal_first_sign_ins
                  FOR EACH ROW EXECUTE FUNCTION prevent_client_portal_first_sign_in_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_client_portal_first_sign_ins_append_only ON client_portal_first_sign_ins;
                DROP FUNCTION IF EXISTS prevent_client_portal_first_sign_in_mutation();
                """);

            migrationBuilder.DropTable(
                name: "client_portal_first_sign_ins");

            migrationBuilder.DropTable(
                name: "client_portal_intents");

            migrationBuilder.DropTable(
                name: "pbc_request_delegations");
        }
    }
}
