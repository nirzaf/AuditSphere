using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffCostRateFirmScopedUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_staff_cost_rates_users_user_id",
                table: "staff_cost_rates");

            migrationBuilder.AddForeignKey(
                name: "fk_staff_cost_rate_user_scope",
                table: "staff_cost_rates",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_staff_cost_rate_user_scope",
                table: "staff_cost_rates");

            migrationBuilder.AddForeignKey(
                name: "FK_staff_cost_rates_users_user_id",
                table: "staff_cost_rates",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
