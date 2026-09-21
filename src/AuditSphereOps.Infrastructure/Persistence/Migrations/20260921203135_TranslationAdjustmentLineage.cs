using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TranslationAdjustmentLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "foreign_exchange_adjustment",
                table: "translation_results",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_adjustment",
                table: "translation_results",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "foreign_exchange_adjustment",
                table: "translation_results");

            migrationBuilder.DropColumn(
                name: "rounding_adjustment",
                table: "translation_results");
        }
    }
}
