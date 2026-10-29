using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SamplingRunSelectionScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_sampling_runs_audit_selections_selection_id",
                table: "audit_sampling_runs");

            migrationBuilder.CreateIndex(
                name: "IX_audit_sampling_runs_firm_id_client_id_engagement_id_selecti~",
                table: "audit_sampling_runs",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_audit_sampling_run_selection_scope",
                table: "audit_sampling_runs",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_id" },
                principalTable: "audit_selections",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_audit_sampling_run_selection_scope",
                table: "audit_sampling_runs");

            migrationBuilder.DropIndex(
                name: "IX_audit_sampling_runs_firm_id_client_id_engagement_id_selecti~",
                table: "audit_sampling_runs");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_sampling_runs_audit_selections_selection_id",
                table: "audit_sampling_runs",
                column: "selection_id",
                principalTable: "audit_selections",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
