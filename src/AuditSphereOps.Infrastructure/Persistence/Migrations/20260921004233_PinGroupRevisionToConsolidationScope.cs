using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PinGroupRevisionToConsolidationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "group_revision",
                table: "consolidation_scope_versions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "group_revision",
                table: "consolidation_scope_versions");
        }
    }
}
