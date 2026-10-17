using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialPackageCashFlowCalculationRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_cash_flow_values",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.AddColumn<string>(
                name: "calculation_role",
                table: "financial_package_cash_flow_lines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_cash_flow_values",
                table: "financial_package_cash_flow_lines",
                sql: "section IN ('OPERATING','INVESTING','FINANCING') AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND ((movement_date IS NULL AND activity_code IS NULL AND classification_rationale IS NULL AND cardinality(source_references) = 0) OR (movement_date IS NOT NULL AND length(trim(activity_code)) > 0 AND cardinality(source_references) > 0 AND length(trim(classification_rationale)) > 0)) AND (calculation_role IS NULL OR (calculation_role = 'STARTING_PROFIT_SUBTOTAL' AND section = 'OPERATING' AND NOT is_non_cash) OR (calculation_role = 'NONCASH_PROFIT_ADJUSTMENT' AND section = 'OPERATING' AND is_non_cash) OR (calculation_role = 'OPERATING_BALANCE_MOVEMENT' AND section = 'OPERATING') OR (calculation_role = 'INVESTING_CASH_MOVEMENT' AND section = 'INVESTING' AND NOT is_non_cash) OR (calculation_role = 'FINANCING_CASH_MOVEMENT' AND section = 'FINANCING' AND NOT is_non_cash) OR (calculation_role = 'NONCASH_INVESTING_FINANCING_DISCLOSURE' AND section IN ('INVESTING','FINANCING') AND is_non_cash))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_cash_flow_values",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.DropColumn(
                name: "calculation_role",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_cash_flow_values",
                table: "financial_package_cash_flow_lines",
                sql: "section IN ('OPERATING','INVESTING','FINANCING') AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND ((movement_date IS NULL AND activity_code IS NULL AND classification_rationale IS NULL AND cardinality(source_references) = 0) OR (movement_date IS NOT NULL AND length(trim(activity_code)) > 0 AND cardinality(source_references) > 0 AND length(trim(classification_rationale)) > 0))");
        }
    }
}
