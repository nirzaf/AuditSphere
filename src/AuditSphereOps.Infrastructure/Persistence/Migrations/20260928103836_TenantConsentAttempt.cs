using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantConsentAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "m365_tenant_consent_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    setup_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiating_session_epoch = table.Column<long>(type: "bigint", nullable: false),
                    initiator_object_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expected_tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    application_client_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    state_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    returned_tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_tenant_consent_attempts", x => x.id);
                    table.UniqueConstraint("AK_m365_tenant_consent_attempts_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_m365_consent_attempt_values", "length(trim(initiator_object_id)) > 0 AND length(trim(expected_tenant_id)) > 0 AND length(trim(application_client_id)) > 0 AND state_hash ~ '^[0-9a-f]{64}$' AND state IN ('PENDING','RETURNED_UNVERIFIED','DENIED','EXPIRED') AND initiating_session_epoch >= 0 AND expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_m365_tenant_consent_attempts_m365_setup_drafts_firm_id_setu~",
                        columns: x => new { x.firm_id, x.setup_draft_id },
                        principalTable: "m365_setup_drafts",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_m365_tenant_consent_attempts_users_firm_id_initiated_by_use~",
                        columns: x => new { x.firm_id, x.initiated_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_m365_tenant_consent_attempts_firm_id_initiated_by_user_id_c~",
                table: "m365_tenant_consent_attempts",
                columns: new[] { "firm_id", "initiated_by_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_tenant_consent_attempts_firm_id_setup_draft_id",
                table: "m365_tenant_consent_attempts",
                columns: new[] { "firm_id", "setup_draft_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_tenant_consent_attempts_state_hash",
                table: "m365_tenant_consent_attempts",
                column: "state_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "m365_tenant_consent_attempts");
        }
    }
}
