using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindFinancialPackagesToReportingContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages");

            migrationBuilder.AddColumn<string>(
                name: "basis",
                table: "financial_packages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "book_id",
                table: "financial_packages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "period_id",
                table: "financial_packages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_packages_firm_id_client_id_book_id",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "book_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_packages_firm_id_client_id_period_id",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages",
                sql: "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED') AND (period_id IS NULL OR length(trim(basis)) > 0) AND (book_id IS NULL OR period_id IS NOT NULL) AND ((cash_beginning IS NULL AND cash_ending IS NULL AND supplementary_hash IS NULL AND equity_hash IS NULL) OR (cash_beginning IS NOT NULL AND cash_ending IS NOT NULL AND supplementary_hash ~ '^[0-9a-f]{64}$' AND (equity_hash IS NULL OR equity_hash ~ '^[0-9a-f]{64}$')))");

            migrationBuilder.AddForeignKey(
                name: "FK_financial_packages_client_reporting_books_firm_id_client_id~",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "book_id" },
                principalTable: "client_reporting_books",
                principalColumns: new[] { "firm_id", "client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_packages_client_reporting_periods_firm_id_client_~",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "period_id" },
                principalTable: "client_reporting_periods",
                principalColumns: new[] { "firm_id", "client_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_packages_client_reporting_books_firm_id_client_id~",
                table: "financial_packages");

            migrationBuilder.DropForeignKey(
                name: "FK_financial_packages_client_reporting_periods_firm_id_client_~",
                table: "financial_packages");

            migrationBuilder.DropIndex(
                name: "IX_financial_packages_firm_id_client_id_book_id",
                table: "financial_packages");

            migrationBuilder.DropIndex(
                name: "IX_financial_packages_firm_id_client_id_period_id",
                table: "financial_packages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "basis",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "book_id",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "period_id",
                table: "financial_packages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages",
                sql: "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED') AND ((cash_beginning IS NULL AND cash_ending IS NULL AND supplementary_hash IS NULL AND equity_hash IS NULL) OR (cash_beginning IS NOT NULL AND cash_ending IS NOT NULL AND supplementary_hash ~ '^[0-9a-f]{64}$' AND (equity_hash IS NULL OR equity_hash ~ '^[0-9a-f]{64}$')))");
        }
    }
}
