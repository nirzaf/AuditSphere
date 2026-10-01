using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientSharePointSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_share_point_sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ownership_marker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    site_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    drive_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    root_item_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    staff_group_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    membership_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    member_object_ids_json = table.Column<string>(type: "text", nullable: false),
                    desired_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    creation_dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creation_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_membership_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_share_point_sites", x => x.id);
                    table.UniqueConstraint("AK_client_share_point_sites_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_client_sharepoint_site_state", "state IN ('REQUESTED','READY') AND membership_state IN ('PENDING','VERIFIED','PARTIAL')");
                    table.ForeignKey(
                        name: "FK_client_share_point_sites_m365_connection_revisions_firm_id_~",
                        columns: x => new { x.firm_id, x.connection_revision_id },
                        principalTable: "m365_connection_revisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_share_point_sites_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_share_point_sites_users_firm_id_requested_by_user_id",
                        columns: x => new { x.firm_id, x.requested_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_share_point_sites_firm_id_client_id",
                table: "client_share_point_sites",
                columns: new[] { "firm_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_share_point_sites_firm_id_connection_revision_id",
                table: "client_share_point_sites",
                columns: new[] { "firm_id", "connection_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_share_point_sites_firm_id_requested_by_user_id",
                table: "client_share_point_sites",
                columns: new[] { "firm_id", "requested_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_share_point_sites_tenant_id_requested_url",
                table: "client_share_point_sites",
                columns: new[] { "tenant_id", "requested_url" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_share_point_sites");
        }
    }
}
