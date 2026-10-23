using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmExpenseActorFirmScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_firm_expenses_firm_id_reviewed_by_user_id",
                table: "firm_expenses",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_firm_expense_prepared_by_user_scope",
                table: "firm_expenses",
                columns: new[] { "firm_id", "prepared_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_firm_expense_reviewed_by_user_scope",
                table: "firm_expenses",
                columns: new[] { "firm_id", "reviewed_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_firm_expense_prepared_by_user_scope",
                table: "firm_expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_firm_expense_reviewed_by_user_scope",
                table: "firm_expenses");

            migrationBuilder.DropIndex(
                name: "IX_firm_expenses_firm_id_reviewed_by_user_id",
                table: "firm_expenses");
        }
    }
}
