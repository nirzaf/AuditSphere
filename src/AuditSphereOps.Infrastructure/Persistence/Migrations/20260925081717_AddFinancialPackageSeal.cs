using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialPackageSeal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_package_seals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_revision = table.Column<long>(type: "bigint", nullable: false),
                    package_generation = table.Column<long>(type: "bigint", nullable: false),
                    package_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    content_manifest_digest = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    artifact_manifest_digest = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    artifact_count = table.Column<int>(type: "integer", nullable: false),
                    total_byte_length = table.Column<long>(type: "bigint", nullable: false),
                    artifact_manifest_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    seal_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sealed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_seals", x => x.id);
                    table.CheckConstraint("ck_financial_package_seal_values", "artifact_count > 0 AND total_byte_length > 0 AND length(trim(seal_version)) > 0 AND package_hash ~ '^[0-9a-f]{64}$' AND content_manifest_digest ~ '^[0-9a-f]{64}$' AND artifact_manifest_digest ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_financial_package_seals_financial_packages_firm_id_client_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_seals_firm_id_client_id_engagement_id_fin~",
                table: "financial_package_seals",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_seal_identity",
                table: "financial_package_seals",
                columns: new[] { "firm_id", "financial_package_id", "package_revision", "package_generation", "artifact_manifest_digest" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_financial_package_seals_append_only
                  BEFORE UPDATE OR DELETE ON financial_package_seals
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_financial_package_seals_append_only ON financial_package_seals;
                """);

            migrationBuilder.DropTable(
                name: "financial_package_seals");
        }
    }
}
