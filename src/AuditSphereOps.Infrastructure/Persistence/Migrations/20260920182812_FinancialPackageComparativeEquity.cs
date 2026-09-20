using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialPackageComparativeEquity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages");

            migrationBuilder.AddColumn<string>(
                name: "comparative_basis",
                table: "financial_packages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "comparative_evidence_reference",
                table: "financial_packages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "comparative_package_id",
                table: "financial_packages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "equity_hash",
                table: "financial_packages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "financial_package_equity_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    opening_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    profit_or_loss_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    oci_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    capital_movement_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    dividends_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    closing_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_equity_lines", x => x.id);
                    table.CheckConstraint("ck_financial_package_equity_line_values", "length(trim(line_code)) > 0 AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0");
                    table.ForeignKey(
                        name: "FK_financial_package_equity_lines_financial_packages_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_package_note_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    face_destination_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_note_lines", x => x.id);
                    table.CheckConstraint("ck_financial_package_note_line_values", "length(trim(note_code)) > 0 AND length(trim(face_destination_code)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0");
                    table.ForeignKey(
                        name: "FK_financial_package_note_lines_financial_packages_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_packages_firm_id_comparative_package_id",
                table: "financial_packages",
                columns: new[] { "firm_id", "comparative_package_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages",
                sql: "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED') AND ((cash_beginning IS NULL AND cash_ending IS NULL AND supplementary_hash IS NULL AND equity_hash IS NULL) OR (cash_beginning IS NOT NULL AND cash_ending IS NOT NULL AND supplementary_hash ~ '^[0-9a-f]{64}$' AND (equity_hash IS NULL OR equity_hash ~ '^[0-9a-f]{64}$')))");

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_equity_lines_firm_id_client_id_engagement~",
                table: "financial_package_equity_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_equity_line_identity",
                table: "financial_package_equity_lines",
                columns: new[] { "firm_id", "financial_package_id", "line_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_note_lines_firm_id_client_id_engagement_i~",
                table: "financial_package_note_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_note_line_identity",
                table: "financial_package_note_lines",
                columns: new[] { "firm_id", "financial_package_id", "note_code", "face_destination_code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_packages_financial_packages_firm_id_comparative_p~",
                table: "financial_packages",
                columns: new[] { "firm_id", "comparative_package_id" },
                principalTable: "financial_packages",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_financial_package_support_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Financial package support lines are immutable; create a new package revision.' USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_financial_package_equity_lines_append_only
                  BEFORE UPDATE OR DELETE ON financial_package_equity_lines
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_package_support_mutation();
                CREATE TRIGGER trg_financial_package_note_lines_append_only
                  BEFORE UPDATE OR DELETE ON financial_package_note_lines
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_package_support_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_financial_package_equity_lines_append_only ON financial_package_equity_lines;
                DROP TRIGGER IF EXISTS trg_financial_package_note_lines_append_only ON financial_package_note_lines;
                DROP FUNCTION IF EXISTS prevent_financial_package_support_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_financial_packages_financial_packages_firm_id_comparative_p~",
                table: "financial_packages");

            migrationBuilder.DropTable(
                name: "financial_package_equity_lines");

            migrationBuilder.DropTable(
                name: "financial_package_note_lines");

            migrationBuilder.DropIndex(
                name: "IX_financial_packages_firm_id_comparative_package_id",
                table: "financial_packages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "comparative_basis",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "comparative_evidence_reference",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "comparative_package_id",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "equity_hash",
                table: "financial_packages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages",
                sql: "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED') AND ((cash_beginning IS NULL AND cash_ending IS NULL AND supplementary_hash IS NULL) OR (cash_beginning IS NOT NULL AND cash_ending IS NOT NULL AND supplementary_hash ~ '^[0-9a-f]{64}$'))");
        }
    }
}
