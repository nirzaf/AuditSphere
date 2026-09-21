using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalComponentPackWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_consolidation_component_values",
                table: "consolidation_components");

            migrationBuilder.AlterColumn<Guid>(
                name: "package_id",
                table: "consolidation_components",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "external_component_pack_id",
                table: "consolidation_components",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                table: "consolidation_components",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE consolidation_components SET source_type = 'INTERNAL_PACKAGE' WHERE source_type = '';");

            migrationBuilder.CreateTable(
                name: "external_component_packs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_pack_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    period_end = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    framework = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reporting_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    period_basis = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    taxonomy_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mapping_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    raw_source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_source_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pack_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    declared_signed_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    reconciled_signed_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    reconciliation_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reconciliation_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    return_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    returned_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reconciled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_component_packs", x => x.id);
                    table.UniqueConstraint("ak_external_component_packs_scope_id", x => new { x.firm_id, x.group_id, x.scope_version_id, x.id });
                    table.CheckConstraint("ck_external_component_pack_values", "period_start ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_end ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_start <= period_end AND framework <> '' AND reporting_currency ~ '^[A-Z]{3}$' AND length(trim(period_basis)) > 0 AND length(trim(taxonomy_version)) > 0 AND length(trim(mapping_version)) > 0 AND length(trim(source_reference)) > 0 AND raw_source_hash ~ '^[0-9a-f]{64}$' AND normalized_source_digest ~ '^[0-9a-f]{64}$' AND pack_digest ~ '^[0-9a-f]{64}$' AND status IN ('SUBMITTED','RESUBMITTED','RETURNED','APPROVED') AND reconciliation_status IN ('PENDING','RECONCILED')");
                    table.ForeignKey(
                        name: "FK_external_component_packs_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_component_packs_consolidation_scope_versions_firm_~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id },
                        principalTable: "consolidation_scope_versions",
                        principalColumns: new[] { "firm_id", "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_component_packs_external_component_packs_firm_id_g~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.prior_pack_id },
                        principalTable: "external_component_packs",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_component_packs_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_component_pack_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_component_pack_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxonomy_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    source_line_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_component_pack_lines", x => x.id);
                    table.UniqueConstraint("ak_external_component_pack_lines_scope_id", x => new { x.firm_id, x.group_id, x.scope_version_id, x.id });
                    table.CheckConstraint("ck_external_component_pack_line_values", "length(trim(taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(source_line_reference)) > 0");
                    table.ForeignKey(
                        name: "FK_external_component_pack_lines_external_component_packs_firm~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.external_component_pack_id },
                        principalTable: "external_component_packs",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_components_firm_id_group_id_scope_version_id_~",
                table: "consolidation_components",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "external_component_pack_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_consolidation_component_values",
                table: "consolidation_components",
                sql: "package_hash ~ '^[0-9a-f]{64}$' AND source_type IN ('INTERNAL_PACKAGE','EXTERNAL_PACK') AND ((source_type = 'INTERNAL_PACKAGE' AND package_id IS NOT NULL AND external_component_pack_id IS NULL) OR (source_type = 'EXTERNAL_PACK' AND package_id IS NULL AND external_component_pack_id IS NOT NULL)) AND currency ~ '^[A-Z]{3}$' AND ownership_percent >= 0 AND ownership_percent <= 100");

            migrationBuilder.CreateIndex(
                name: "ix_external_component_pack_line",
                table: "external_component_pack_lines",
                columns: new[] { "firm_id", "external_component_pack_id", "taxonomy_code" });

            migrationBuilder.CreateIndex(
                name: "IX_external_component_pack_lines_firm_id_group_id_scope_versio~",
                table: "external_component_pack_lines",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "external_component_pack_id" });

            migrationBuilder.CreateIndex(
                name: "IX_external_component_packs_firm_id_client_id",
                table: "external_component_packs",
                columns: new[] { "firm_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_external_component_packs_firm_id_group_id_scope_version_id_~",
                table: "external_component_packs",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "prior_pack_id" });

            migrationBuilder.CreateIndex(
                name: "ux_external_component_pack_version",
                table: "external_component_packs",
                columns: new[] { "firm_id", "scope_version_id", "client_id", "version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_consolidation_components_external_component_packs_firm_id_g~",
                table: "consolidation_components",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "external_component_pack_id" },
                principalTable: "external_component_packs",
                principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_consolidation_components_external_component_packs_firm_id_g~",
                table: "consolidation_components");

            migrationBuilder.DropTable(
                name: "external_component_pack_lines");

            migrationBuilder.DropTable(
                name: "external_component_packs");

            migrationBuilder.DropIndex(
                name: "IX_consolidation_components_firm_id_group_id_scope_version_id_~",
                table: "consolidation_components");

            migrationBuilder.DropCheckConstraint(
                name: "ck_consolidation_component_values",
                table: "consolidation_components");

            migrationBuilder.DropColumn(
                name: "external_component_pack_id",
                table: "consolidation_components");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "consolidation_components");

            migrationBuilder.AlterColumn<Guid>(
                name: "package_id",
                table: "consolidation_components",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_consolidation_component_values",
                table: "consolidation_components",
                sql: "package_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND ownership_percent >= 0 AND ownership_percent <= 100");
        }
    }
}
