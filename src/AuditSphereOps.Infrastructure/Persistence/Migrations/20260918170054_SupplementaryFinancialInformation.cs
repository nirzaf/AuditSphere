using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplementaryFinancialInformation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages");

            migrationBuilder.AddColumn<decimal>(
                name: "cash_beginning",
                table: "financial_packages",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_ending",
                table: "financial_packages",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplementary_hash",
                table: "financial_packages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "financial_package_cash_flow_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_cash_flow_lines", x => x.id);
                    table.CheckConstraint("ck_financial_package_cash_flow_values", "section IN ('OPERATING','INVESTING','FINANCING') AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_financial_package_cash_flow_lines_financial_packages_firm_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_package_disclosures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    response = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    not_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_disclosures", x => x.id);
                    table.CheckConstraint("ck_financial_package_disclosure_values", "length(trim(code)) > 0 AND ((not_applicable = false AND length(trim(response)) > 0) OR (not_applicable = true AND length(trim(rationale)) > 0))");
                    table.ForeignKey(
                        name: "FK_financial_package_disclosures_financial_packages_firm_id_cl~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages",
                sql: "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED') AND ((cash_beginning IS NULL AND cash_ending IS NULL AND supplementary_hash IS NULL) OR (cash_beginning IS NOT NULL AND cash_ending IS NOT NULL AND supplementary_hash ~ '^[0-9a-f]{64}$'))");

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_lines_firm_id_client_id_engagem~",
                table: "financial_package_cash_flow_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_cash_flow_line_identity",
                table: "financial_package_cash_flow_lines",
                columns: new[] { "firm_id", "financial_package_id", "section", "description" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_disclosures_firm_id_client_id_engagement_~",
                table: "financial_package_disclosures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_disclosure_code",
                table: "financial_package_disclosures",
                columns: new[] { "firm_id", "financial_package_id", "code" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_financial_package_cash_flow_lines_append_only
                  BEFORE UPDATE OR DELETE ON financial_package_cash_flow_lines
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                CREATE TRIGGER trg_financial_package_disclosures_append_only
                  BEFORE UPDATE OR DELETE ON financial_package_disclosures
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM financial_package_cash_flow_lines)
                     OR EXISTS (SELECT 1 FROM financial_package_disclosures)
                     OR EXISTS (SELECT 1 FROM financial_packages WHERE supplementary_hash IS NOT NULL) THEN
                    RAISE EXCEPTION 'Supplementary financial-statement downgrade would discard immutable accounting evidence.';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_financial_package_cash_flow_lines_append_only ON financial_package_cash_flow_lines;
                DROP TRIGGER IF EXISTS trg_financial_package_disclosures_append_only ON financial_package_disclosures;
                """);
            migrationBuilder.DropTable(
                name: "financial_package_cash_flow_lines");

            migrationBuilder.DropTable(
                name: "financial_package_disclosures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "cash_beginning",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "cash_ending",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "supplementary_hash",
                table: "financial_packages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages",
                sql: "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED')");
        }
    }
}
