using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveStructuredExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archive_structured_exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    schema = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archive_structured_exports", x => x.id);
                    table.UniqueConstraint("AK_archive_structured_exports_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_archive_structured_exports_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_archive_structured_export_values", "version >= 1 AND schema = 'records-export.v1' AND length(trim(payload_json)) > 0 AND content_hash ~ '^[0-9a-f]{64}$' AND byte_count > 0");
                    table.ForeignKey(
                        name: "FK_archive_structured_exports_archive_manifests_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_manifest_id },
                        principalTable: "archive_manifests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_structured_exports_archives_firm_id_client_id_engag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_id },
                        principalTable: "archives",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_structured_exports_engagements_firm_id_client_id_en~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_archive_structured_exports_firm_id_client_id_engagement_id_~",
                table: "archive_structured_exports",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_structured_exports_firm_id_client_id_engagement_id~1",
                table: "archive_structured_exports",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_manifest_id" });

            migrationBuilder.CreateIndex(
                name: "ux_archive_structured_export_manifest_version",
                table: "archive_structured_exports",
                columns: new[] { "firm_id", "archive_manifest_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM archive_structured_exports) THEN
                        RAISE EXCEPTION 'Structured archive export downgrade would discard immutable archive payloads.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "archive_structured_exports");
        }
    }
}
