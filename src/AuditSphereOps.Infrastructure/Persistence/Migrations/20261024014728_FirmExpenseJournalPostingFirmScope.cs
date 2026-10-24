using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmExpenseJournalPostingFirmScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_firm_expenses_firm_id_journal_id",
                table: "firm_expenses",
                columns: new[] { "firm_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_expenses_firm_id_posting_id",
                table: "firm_expenses",
                columns: new[] { "firm_id", "posting_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_firm_expense_journal_scope",
                table: "firm_expenses",
                columns: new[] { "firm_id", "journal_id" },
                principalTable: "firm_journals",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_firm_expense_posting_scope",
                table: "firm_expenses",
                columns: new[] { "firm_id", "posting_id" },
                principalTable: "firm_postings",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_firm_expense_journal_scope",
                table: "firm_expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_firm_expense_posting_scope",
                table: "firm_expenses");

            migrationBuilder.DropIndex(
                name: "IX_firm_expenses_firm_id_journal_id",
                table: "firm_expenses");

            migrationBuilder.DropIndex(
                name: "IX_firm_expenses_firm_id_posting_id",
                table: "firm_expenses");
        }
    }
}
