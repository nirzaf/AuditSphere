using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeRateSetEffectiveRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_exchange_rate_set_values",
                table: "exchange_rate_set_versions");

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_from",
                table: "exchange_rate_set_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_to",
                table: "exchange_rate_set_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "exchange_rate_set_versions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_exchange_rate_set_values",
                table: "exchange_rate_set_versions",
                sql: "length(trim(code)) > 0 AND length(trim(source)) > 0 AND version > 0 AND (effective_from IS NULL OR effective_to IS NULL OR effective_from <= effective_to)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_exchange_rate_set_values",
                table: "exchange_rate_set_versions");

            migrationBuilder.DropColumn(
                name: "effective_from",
                table: "exchange_rate_set_versions");

            migrationBuilder.DropColumn(
                name: "effective_to",
                table: "exchange_rate_set_versions");

            migrationBuilder.DropColumn(
                name: "version",
                table: "exchange_rate_set_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_exchange_rate_set_values",
                table: "exchange_rate_set_versions",
                sql: "length(trim(code)) > 0 AND length(trim(source)) > 0");
        }
    }
}
