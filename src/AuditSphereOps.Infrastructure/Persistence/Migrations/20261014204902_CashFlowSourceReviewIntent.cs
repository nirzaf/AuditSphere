using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashFlowSourceReviewIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "link_digest",
                table: "financial_package_cash_flow_source_reviews",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_review_id",
                table: "financial_package_cash_flow_source_reviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_hash",
                table: "financial_package_cash_flow_source_reviews",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                table: "financial_package_cash_flow_source_reviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_cash_source_review_request",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "financial_package_id", "request_id" },
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_cash_source_review_intent",
                table: "financial_package_cash_flow_source_reviews",
                sql: "(request_id IS NULL AND request_hash IS NULL AND link_digest IS NULL) OR (request_id IS NOT NULL AND request_hash IS NOT NULL AND link_digest IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_financial_package_cash_source_review_request",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_cash_source_review_intent",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropColumn(
                name: "link_digest",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropColumn(
                name: "previous_review_id",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "financial_package_cash_flow_source_reviews");
        }
    }
}
