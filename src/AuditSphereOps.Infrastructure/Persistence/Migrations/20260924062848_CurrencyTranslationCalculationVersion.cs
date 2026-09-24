using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CurrencyTranslationCalculationVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_translation_result_input",
                table: "translation_results");

            migrationBuilder.AddColumn<string>(
                name: "calculation_version",
                table: "translation_results",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "COMPONENT_TRANSLATION_V1");

            migrationBuilder.CreateIndex(
                name: "ux_translation_result_input",
                table: "translation_results",
                columns: new[] { "firm_id", "scope_version_id", "component_id", "rate_set_version_id", "translation_policy_version_id", "calculation_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_translation_result_input",
                table: "translation_results");

            migrationBuilder.DropColumn(
                name: "calculation_version",
                table: "translation_results");

            migrationBuilder.CreateIndex(
                name: "ux_translation_result_input",
                table: "translation_results",
                columns: new[] { "firm_id", "scope_version_id", "component_id", "rate_set_version_id", "translation_policy_version_id" },
                unique: true);
        }
    }
}
