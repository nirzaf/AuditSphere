using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationAgingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "aging_basis",
                table: "accounting_reconciliations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "aging_bucket_rule_version",
                table: "accounting_reconciliations",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "aging_bucket",
                table: "accounting_reconciliation_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "date_basis",
                table: "accounting_reconciliation_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_credit",
                table: "accounting_reconciliation_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "settlement_date",
                table: "accounting_reconciliation_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_reference",
                table: "accounting_reconciliation_items",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aging_basis",
                table: "accounting_reconciliations");

            migrationBuilder.DropColumn(
                name: "aging_bucket_rule_version",
                table: "accounting_reconciliations");

            migrationBuilder.DropColumn(
                name: "aging_bucket",
                table: "accounting_reconciliation_items");

            migrationBuilder.DropColumn(
                name: "date_basis",
                table: "accounting_reconciliation_items");

            migrationBuilder.DropColumn(
                name: "is_credit",
                table: "accounting_reconciliation_items");

            migrationBuilder.DropColumn(
                name: "settlement_date",
                table: "accounting_reconciliation_items");

            migrationBuilder.DropColumn(
                name: "settlement_reference",
                table: "accounting_reconciliation_items");
        }
    }
}
