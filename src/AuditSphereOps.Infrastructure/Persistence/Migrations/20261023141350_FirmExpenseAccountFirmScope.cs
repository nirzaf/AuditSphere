using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmExpenseAccountFirmScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_firm_expenses_firm_accounts_expense_account_id",
                table: "firm_expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_firm_expenses_firm_accounts_payment_account_id",
                table: "firm_expenses");

            migrationBuilder.CreateIndex(
                name: "IX_firm_expenses_firm_id_expense_account_id",
                table: "firm_expenses",
                columns: new[] { "firm_id", "expense_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_expenses_firm_id_payment_account_id",
                table: "firm_expenses",
                columns: new[] { "firm_id", "payment_account_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_firm_expense_expense_account_scope",
                table: "firm_expenses",
                columns: new[] { "firm_id", "expense_account_id" },
                principalTable: "firm_accounts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_firm_expense_payment_account_scope",
                table: "firm_expenses",
                columns: new[] { "firm_id", "payment_account_id" },
                principalTable: "firm_accounts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_firm_expense_expense_account_scope",
                table: "firm_expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_firm_expense_payment_account_scope",
                table: "firm_expenses");

            migrationBuilder.DropIndex(
                name: "IX_firm_expenses_firm_id_expense_account_id",
                table: "firm_expenses");

            migrationBuilder.DropIndex(
                name: "IX_firm_expenses_firm_id_payment_account_id",
                table: "firm_expenses");

            migrationBuilder.AddForeignKey(
                name: "FK_firm_expenses_firm_accounts_expense_account_id",
                table: "firm_expenses",
                column: "expense_account_id",
                principalTable: "firm_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_firm_expenses_firm_accounts_payment_account_id",
                table: "firm_expenses",
                column: "payment_account_id",
                principalTable: "firm_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
