using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepositoryBindingAndCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid?>(
                name: "repository_binding_id",
                table: "document_references",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "repository_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    site_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    drive_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    root_folder_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    classification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    desired_access = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    observed_access = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    capability_profile = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repository_bindings", x => x.id);
                    table.UniqueConstraint("AK_repository_bindings_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_repository_bindings_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_repository_binding_values", "length(trim(tenant_id)) > 0 AND length(trim(site_id)) > 0 AND length(trim(drive_id)) > 0 AND length(trim(root_folder_id)) > 0 AND length(trim(classification)) > 0 AND length(trim(desired_access)) > 0 AND length(trim(observed_access)) > 0 AND length(trim(capability_profile)) > 0");
                    table.ForeignKey(
                        name: "FK_repository_bindings_engagements_firm_id_client_id_engagemen~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_repository_bindings_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "integration_capabilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    health_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tested_permissions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    tested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_capabilities", x => x.id);
                    table.UniqueConstraint("AK_integration_capabilities_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_integration_capability_values", "length(trim(health_status)) > 0 AND length(trim(tested_permissions)) > 0 AND ((tested_at IS NULL AND health_status IN ('UNKNOWN','BLOCKED')) OR tested_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_integration_capabilities_repository_bindings_firm_id_reposi~",
                        columns: x => new { x.firm_id, x.repository_binding_id },
                        principalTable: "repository_bindings",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sync_cursors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cursor = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_cursors", x => x.id);
                    table.UniqueConstraint("AK_sync_cursors_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_sync_cursor_values", "length(trim(cursor)) > 0 AND generation >= 1");
                    table.ForeignKey(
                        name: "FK_sync_cursors_repository_bindings_firm_id_repository_binding~",
                        columns: x => new { x.firm_id, x.repository_binding_id },
                        principalTable: "repository_bindings",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_references_firm_id_client_id_engagement_id_reposit~",
                table: "document_references",
                columns: new[] { "firm_id", "client_id", "engagement_id", "repository_binding_id" });

            migrationBuilder.CreateIndex(
                name: "ux_integration_capability_binding",
                table: "integration_capabilities",
                columns: new[] { "firm_id", "repository_binding_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sync_cursor_binding",
                table: "sync_cursors",
                columns: new[] { "firm_id", "repository_binding_id" },
                unique: true);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM document_references WHERE repository_binding_id IS NULL) THEN
                    RAISE EXCEPTION 'repository binding migration requires every existing document reference to be mapped before activation';
                  END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "repository_binding_id",
                table: "document_references",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_document_references_repository_bindings_firm_id_client_id_e~",
                table: "document_references",
                columns: new[] { "firm_id", "client_id", "engagement_id", "repository_binding_id" },
                principalTable: "repository_bindings",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_document_references_repository_bindings_firm_id_client_id_e~",
                table: "document_references");

            migrationBuilder.DropTable(
                name: "integration_capabilities");

            migrationBuilder.DropTable(
                name: "sync_cursors");

            migrationBuilder.DropTable(
                name: "repository_bindings");

            migrationBuilder.DropIndex(
                name: "IX_document_references_firm_id_client_id_engagement_id_reposit~",
                table: "document_references");

            migrationBuilder.DropColumn(
                name: "repository_binding_id",
                table: "document_references");
        }
    }
}
