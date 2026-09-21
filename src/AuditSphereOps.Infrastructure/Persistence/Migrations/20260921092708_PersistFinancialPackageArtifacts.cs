using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistFinancialPackageArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_review_values",
                table: "financial_package_review_decisions");

            migrationBuilder.AddColumn<string>(
                name: "artifact_sha256_hex",
                table: "financial_package_review_decisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "artifact_version",
                table: "financial_package_review_decisions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "financial_package_artifact_id",
                table: "financial_package_review_decisions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "financial_package_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_revision = table.Column<long>(type: "bigint", nullable: false),
                    package_generation = table.Column<long>(type: "bigint", nullable: false),
                    package_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    artifact_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    framework_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    template_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    artifact_sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    artifact_bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_artifacts", x => x.id);
                    table.UniqueConstraint("AK_financial_package_artifacts_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_financial_package_artifact_values", "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$' AND length(trim(artifact_version)) > 0 AND length(trim(framework_version)) > 0 AND length(trim(template_version)) > 0 AND artifact_sha256_hex ~ '^[0-9a-f]{64}$' AND octet_length(artifact_bytes) > 0");
                    table.ForeignKey(
                        name: "FK_financial_package_artifacts_financial_packages_firm_id_clie~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_package_artifacts_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engage~",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_artifact_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_review_values",
                table: "financial_package_review_decisions",
                sql: "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$' AND artifact_sha256_hex ~ '^[0-9a-f]{64}$' AND length(trim(artifact_version)) > 0 AND stage IN ('MANAGEMENT_APPROVAL','ACCOUNTING_REVIEW','PARTNER_APPROVAL') AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED') AND evidence_mode IN ('SIGNED_IN','OFFLINE') AND length(trim(evidence_reference)) > 0 AND ((evidence_mode = 'SIGNED_IN' AND decided_by_user_id IS NOT NULL) OR evidence_mode = 'OFFLINE')");

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_artifacts_firm_id_client_id_engagement_id~",
                table: "financial_package_artifacts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_artifacts_firm_id_created_by_user_id",
                table: "financial_package_artifacts",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_artifact_version",
                table: "financial_package_artifacts",
                columns: new[] { "firm_id", "financial_package_id", "package_revision", "package_generation", "artifact_version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_package_review_decisions_financial_package_artifa~",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_artifact_id" },
                principalTable: "financial_package_artifacts",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_package_review_decisions_financial_package_artifa~",
                table: "financial_package_review_decisions");

            migrationBuilder.DropTable(
                name: "financial_package_artifacts");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engage~",
                table: "financial_package_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_review_values",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "artifact_sha256_hex",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "artifact_version",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "financial_package_artifact_id",
                table: "financial_package_review_decisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_review_values",
                table: "financial_package_review_decisions",
                sql: "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$' AND stage IN ('MANAGEMENT_APPROVAL','ACCOUNTING_REVIEW','PARTNER_APPROVAL') AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED') AND evidence_mode IN ('SIGNED_IN','OFFLINE') AND length(trim(evidence_reference)) > 0 AND ((evidence_mode = 'SIGNED_IN' AND decided_by_user_id IS NOT NULL) OR evidence_mode = 'OFFLINE')");
        }
    }
}
