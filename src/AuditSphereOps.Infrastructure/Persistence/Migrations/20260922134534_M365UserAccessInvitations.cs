using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M365UserAccessInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_role_grants_firm_id_id",
                table: "role_grants",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "m365_directory_user_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    object_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    user_principal_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    mail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    enabled_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    user_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_directory_user_observations", x => x.id);
                    table.UniqueConstraint("AK_m365_directory_observations_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_m365_directory_observation_values", "length(trim(tenant_id)) > 0 AND length(trim(object_id)) > 0 AND length(trim(display_name)) > 0 AND enabled_state IN ('ENABLED','DISABLED','UNKNOWN') AND length(trim(source)) > 0");
                    table.ForeignKey(
                        name: "FK_m365_directory_user_observations_m365_connection_revisions_~",
                        columns: x => new { x.firm_id, x.connection_revision_id },
                        principalTable: "m365_connection_revisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "m365_user_access_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    destination_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    delivery_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    provider_correlation_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_access_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_user_access_invitations", x => x.id);
                    table.UniqueConstraint("AK_m365_user_invitations_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_m365_user_invitation_values", "length(trim(recipient_email)) > 0 AND length(trim(destination_path)) > 0 AND destination_path NOT LIKE '%://%' AND delivery_state IN ('NOT_SENT','QUEUED','PROVIDER_ACCEPTED','FAILED','UNKNOWN','COPIED') AND attempt_count >= 0");
                    table.ForeignKey(
                        name: "FK_m365_user_access_invitations_role_grants_firm_id_role_grant~",
                        columns: x => new { x.firm_id, x.role_grant_id },
                        principalTable: "role_grants",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_m365_user_access_invitations_users_firm_id_user_id",
                        columns: x => new { x.firm_id, x.user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_active_role_grant_identity",
                table: "role_grants",
                columns: new[] { "firm_id", "user_id", "role", "client_id", "engagement_id" },
                unique: true,
                filter: "revoked_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_m365_directory_user_observations_firm_id_connection_revisio~",
                table: "m365_directory_user_observations",
                columns: new[] { "firm_id", "connection_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_directory_user_observations_firm_id_tenant_id_object_i~",
                table: "m365_directory_user_observations",
                columns: new[] { "firm_id", "tenant_id", "object_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_user_access_invitations_firm_id_role_grant_id",
                table: "m365_user_access_invitations",
                columns: new[] { "firm_id", "role_grant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_m365_user_access_invitations_firm_id_user_id",
                table: "m365_user_access_invitations",
                columns: new[] { "firm_id", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "m365_directory_user_observations");

            migrationBuilder.DropTable(
                name: "m365_user_access_invitations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_role_grants_firm_id_id",
                table: "role_grants");

            migrationBuilder.DropIndex(
                name: "ux_active_role_grant_identity",
                table: "role_grants");
        }
    }
}
