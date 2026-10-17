using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialPackageNoteSourceLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_note_line_values",
                table: "financial_package_note_lines");

            migrationBuilder.AddColumn<string>(
                name: "source_lineage_json",
                table: "financial_package_note_lines",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_note_line_values",
                table: "financial_package_note_lines",
                sql: "length(trim(note_code)) > 0 AND length(trim(face_destination_code)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0 AND jsonb_typeof(source_lineage_json) = 'array'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_note_line_values",
                table: "financial_package_note_lines");

            migrationBuilder.DropColumn(
                name: "source_lineage_json",
                table: "financial_package_note_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_note_line_values",
                table: "financial_package_note_lines",
                sql: "length(trim(note_code)) > 0 AND length(trim(face_destination_code)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0");
        }
    }
}
