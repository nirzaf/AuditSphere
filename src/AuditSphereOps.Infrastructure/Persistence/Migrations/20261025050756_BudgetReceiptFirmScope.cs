using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetReceiptFirmScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_budget_approvals_engagement_budgets_budget_id",
                table: "budget_approvals");

            migrationBuilder.DropForeignKey(
                name: "FK_budget_approvals_engagements_engagement_id",
                table: "budget_approvals");

            migrationBuilder.DropForeignKey(
                name: "FK_budget_preparations_engagement_budgets_budget_id",
                table: "budget_preparations");

            migrationBuilder.DropForeignKey(
                name: "FK_budget_preparations_engagements_engagement_id",
                table: "budget_preparations");

            migrationBuilder.DropIndex(
                name: "IX_budget_preparations_budget_id",
                table: "budget_preparations");

            migrationBuilder.DropIndex(
                name: "IX_budget_preparations_engagement_id",
                table: "budget_preparations");

            migrationBuilder.DropIndex(
                name: "IX_budget_approvals_budget_id",
                table: "budget_approvals");

            migrationBuilder.DropIndex(
                name: "IX_budget_approvals_engagement_id",
                table: "budget_approvals");

            migrationBuilder.CreateIndex(
                name: "IX_budget_preparations_firm_id_budget_id",
                table: "budget_preparations",
                columns: new[] { "firm_id", "budget_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_preparations_firm_id_engagement_id",
                table: "budget_preparations",
                columns: new[] { "firm_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_approvals_firm_id_budget_id",
                table: "budget_approvals",
                columns: new[] { "firm_id", "budget_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_approvals_firm_id_engagement_id",
                table: "budget_approvals",
                columns: new[] { "firm_id", "engagement_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_budget_approvals_engagement_budgets_firm_id_budget_id",
                table: "budget_approvals",
                columns: new[] { "firm_id", "budget_id" },
                principalTable: "engagement_budgets",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_budget_approvals_engagements_firm_id_engagement_id",
                table: "budget_approvals",
                columns: new[] { "firm_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_budget_preparations_engagement_budgets_firm_id_budget_id",
                table: "budget_preparations",
                columns: new[] { "firm_id", "budget_id" },
                principalTable: "engagement_budgets",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_budget_preparations_engagements_firm_id_engagement_id",
                table: "budget_preparations",
                columns: new[] { "firm_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_budget_approvals_engagement_budgets_firm_id_budget_id",
                table: "budget_approvals");

            migrationBuilder.DropForeignKey(
                name: "FK_budget_approvals_engagements_firm_id_engagement_id",
                table: "budget_approvals");

            migrationBuilder.DropForeignKey(
                name: "FK_budget_preparations_engagement_budgets_firm_id_budget_id",
                table: "budget_preparations");

            migrationBuilder.DropForeignKey(
                name: "FK_budget_preparations_engagements_firm_id_engagement_id",
                table: "budget_preparations");

            migrationBuilder.DropIndex(
                name: "IX_budget_preparations_firm_id_budget_id",
                table: "budget_preparations");

            migrationBuilder.DropIndex(
                name: "IX_budget_preparations_firm_id_engagement_id",
                table: "budget_preparations");

            migrationBuilder.DropIndex(
                name: "IX_budget_approvals_firm_id_budget_id",
                table: "budget_approvals");

            migrationBuilder.DropIndex(
                name: "IX_budget_approvals_firm_id_engagement_id",
                table: "budget_approvals");

            migrationBuilder.CreateIndex(
                name: "IX_budget_preparations_budget_id",
                table: "budget_preparations",
                column: "budget_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_preparations_engagement_id",
                table: "budget_preparations",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "IX_budget_approvals_budget_id",
                table: "budget_approvals",
                column: "budget_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_approvals_engagement_id",
                table: "budget_approvals",
                column: "engagement_id");

            migrationBuilder.AddForeignKey(
                name: "FK_budget_approvals_engagement_budgets_budget_id",
                table: "budget_approvals",
                column: "budget_id",
                principalTable: "engagement_budgets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_budget_approvals_engagements_engagement_id",
                table: "budget_approvals",
                column: "engagement_id",
                principalTable: "engagements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_budget_preparations_engagement_budgets_budget_id",
                table: "budget_preparations",
                column: "budget_id",
                principalTable: "engagement_budgets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_budget_preparations_engagements_engagement_id",
                table: "budget_preparations",
                column: "engagement_id",
                principalTable: "engagements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
