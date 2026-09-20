using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindValuationEvidenceToSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "input_generation",
                table: "inventory_valuation_assessments",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "reconciliation_source_hash",
                table: "inventory_valuation_assessments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "input_generation",
                table: "ecl_assessments",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "reconciliation_source_hash",
                table: "ecl_assessments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "input_generation",
                table: "inventory_valuation_assessments");

            migrationBuilder.DropColumn(
                name: "reconciliation_source_hash",
                table: "inventory_valuation_assessments");

            migrationBuilder.DropColumn(
                name: "input_generation",
                table: "ecl_assessments");

            migrationBuilder.DropColumn(
                name: "reconciliation_source_hash",
                table: "ecl_assessments");
        }
    }
}
