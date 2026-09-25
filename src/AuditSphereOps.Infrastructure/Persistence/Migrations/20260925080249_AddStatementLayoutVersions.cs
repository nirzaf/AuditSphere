using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStatementLayoutVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "statement_layout_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    framework_policy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    layout_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    validation_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    supersedes_layout_id = table.Column<Guid>(type: "uuid", nullable: true),
                    publish_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_layout_versions", x => x.id);
                    table.UniqueConstraint("AK_statement_layout_versions_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_statement_layout_versions_scope_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_statement_layout_version_values", "version_number >= 1 AND line_count >= 0 AND status IN ('DRAFT','VALIDATED','PUBLISHED') AND ((status = 'PUBLISHED' AND published_by_user_id IS NOT NULL AND published_at IS NOT NULL) OR (status <> 'PUBLISHED' AND published_by_user_id IS NULL AND published_at IS NULL))");
                    table.ForeignKey(
                        name: "FK_statement_layout_versions_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_layout_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layout_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    line_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    section = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    line_order = table.Column<int>(type: "integer", nullable: false),
                    display_sign = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    taxonomy_node_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    child_line_codes_json = table.Column<string>(type: "text", nullable: true),
                    referenced_line_codes_json = table.Column<string>(type: "text", nullable: true),
                    operations_json = table.Column<string>(type: "text", nullable: true),
                    denominator_line_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    zero_denominator_yields_zero = table.Column<bool>(type: "boolean", nullable: false),
                    is_subtotal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_layout_lines", x => x.id);
                    table.CheckConstraint("ck_statement_layout_line_values", "line_order >= 0 AND length(trim(line_code)) > 0 AND length(trim(label)) > 0 AND line_kind IN ('HEADING','MAPPED_TAXONOMY_BALANCE','SUM_CHILD_LINES','TOTAL_REFERENCED_LINES','RATIO') AND section IN ('FINANCIAL_POSITION','PROFIT_OR_LOSS','OCI','CHANGES_IN_EQUITY','CASH_FLOWS','NOTES')");
                    table.ForeignKey(
                        name: "FK_statement_layout_lines_statement_layout_versions_firm_id_la~",
                        columns: x => new { x.firm_id, x.layout_version_id },
                        principalTable: "statement_layout_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_statement_layout_line_code",
                table: "statement_layout_lines",
                columns: new[] { "firm_id", "layout_version_id", "line_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_statement_layout_version_identity",
                table: "statement_layout_versions",
                columns: new[] { "firm_id", "client_id", "framework_policy", "name", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "statement_layout_lines");

            migrationBuilder.DropTable(
                name: "statement_layout_versions");
        }
    }
}
