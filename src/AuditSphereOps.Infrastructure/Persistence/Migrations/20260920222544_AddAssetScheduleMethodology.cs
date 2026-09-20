using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetScheduleMethodology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_specialist_schedule_values",
                table: "specialist_accounting_schedules");

            migrationBuilder.AddColumn<decimal>(
                name: "closing_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "depreciation_method",
                table: "specialist_accounting_schedules",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "useful_life_months",
                table: "specialist_accounting_schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_specialist_schedule_values",
                table: "specialist_accounting_schedules",
                sql: "length(trim(area)) > 0 AND length(trim(methodology_version)) > 0 AND (area <> 'ASSETS' OR (length(trim(depreciation_method)) > 0 AND useful_life_months > 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_specialist_schedule_values",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "closing_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "depreciation_method",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "useful_life_months",
                table: "specialist_accounting_schedules");

            migrationBuilder.AddCheckConstraint(
                name: "ck_specialist_schedule_values",
                table: "specialist_accounting_schedules",
                sql: "length(trim(area)) > 0 AND length(trim(methodology_version)) > 0");
        }
    }
}
