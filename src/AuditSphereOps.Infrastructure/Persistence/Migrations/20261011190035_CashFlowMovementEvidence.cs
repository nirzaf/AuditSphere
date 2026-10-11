using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashFlowMovementEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_cash_flow_values",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.AddColumn<string[]>(
                name: "cash_account_codes",
                table: "financial_packages",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "closing_cash_evidence_references",
                table: "financial_packages",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "opening_cash_evidence_references",
                table: "financial_packages",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<string>(
                name: "activity_code",
                table: "financial_package_cash_flow_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "classification_rationale",
                table: "financial_package_cash_flow_lines",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "movement_date",
                table: "financial_package_cash_flow_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "source_references",
                table: "financial_package_cash_flow_lines",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_cash_flow_values",
                table: "financial_package_cash_flow_lines",
                sql: "section IN ('OPERATING','INVESTING','FINANCING') AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND ((movement_date IS NULL AND activity_code IS NULL AND classification_rationale IS NULL AND cardinality(source_references) = 0) OR (movement_date IS NOT NULL AND length(trim(activity_code)) > 0 AND cardinality(source_references) > 0 AND length(trim(classification_rationale)) > 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_cash_flow_values",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.DropColumn(
                name: "cash_account_codes",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "closing_cash_evidence_references",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "opening_cash_evidence_references",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "activity_code",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.DropColumn(
                name: "classification_rationale",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.DropColumn(
                name: "movement_date",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.DropColumn(
                name: "source_references",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_cash_flow_values",
                table: "financial_package_cash_flow_lines",
                sql: "section IN ('OPERATING','INVESTING','FINANCING') AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$'");
        }
    }
}
