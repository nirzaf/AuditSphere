using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdjustmentSourceDatasetScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_adjustment_journals_trial_balance_datasets_base_dataset_id",
                table: "adjustment_journals");

            migrationBuilder.DropForeignKey(
                name: "FK_adjustment_plans_trial_balance_datasets_base_dataset_id",
                table: "adjustment_plans");

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_plans_firm_id_client_id_engagement_id_base_datas~",
                table: "adjustment_plans",
                columns: new[] { "firm_id", "client_id", "engagement_id", "base_dataset_id" });

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_journals_firm_id_client_id_engagement_id_base_da~",
                table: "adjustment_journals",
                columns: new[] { "firm_id", "client_id", "engagement_id", "base_dataset_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_adjustment_journal_source_scope",
                table: "adjustment_journals",
                columns: new[] { "firm_id", "client_id", "engagement_id", "base_dataset_id" },
                principalTable: "trial_balance_datasets",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_adjustment_plan_source_scope",
                table: "adjustment_plans",
                columns: new[] { "firm_id", "client_id", "engagement_id", "base_dataset_id" },
                principalTable: "trial_balance_datasets",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_adjustment_journal_source_scope",
                table: "adjustment_journals");

            migrationBuilder.DropForeignKey(
                name: "fk_adjustment_plan_source_scope",
                table: "adjustment_plans");

            migrationBuilder.DropIndex(
                name: "IX_adjustment_plans_firm_id_client_id_engagement_id_base_datas~",
                table: "adjustment_plans");

            migrationBuilder.DropIndex(
                name: "IX_adjustment_journals_firm_id_client_id_engagement_id_base_da~",
                table: "adjustment_journals");

            migrationBuilder.AddForeignKey(
                name: "FK_adjustment_journals_trial_balance_datasets_base_dataset_id",
                table: "adjustment_journals",
                column: "base_dataset_id",
                principalTable: "trial_balance_datasets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_adjustment_plans_trial_balance_datasets_base_dataset_id",
                table: "adjustment_plans",
                column: "base_dataset_id",
                principalTable: "trial_balance_datasets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
