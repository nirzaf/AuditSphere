using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmationCriticalityFirmScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "fk_confirmation_criticality_case_scope",
                table: "confirmation_criticalities",
                columns: new[] { "firm_id", "confirmation_case_id" },
                principalTable: "audit_confirmation_cases",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_confirmation_criticality_case_scope",
                table: "confirmation_criticalities");
        }
    }
}
