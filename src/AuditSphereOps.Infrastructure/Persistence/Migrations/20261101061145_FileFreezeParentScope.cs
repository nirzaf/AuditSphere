using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FileFreezeParentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_engagement_file_freeze_firm_engagement_id",
                table: "engagement_file_freezes",
                columns: new[] { "firm_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_engagement_file_freeze_scope_report_id",
                table: "engagement_file_freezes",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id", "report_deliverable_id" });

            // The EF global-ID relationships remain intact. These additional PostgreSQL references enforce
            // the scope already carried by each row without changing workflow or approval behavior.
            migrationBuilder.AddForeignKey(
                name: "fk_engagement_file_freeze_engagement_scope",
                table: "engagement_file_freezes",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_engagement_file_freeze_deliverable_scope",
                table: "engagement_file_freezes",
                columns: new[] { "firm_id", "client_id", "engagement_id", "report_deliverable_id" },
                principalTable: "audit_deliverables",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_file_freeze_amendment_freeze_scope",
                table: "file_freeze_amendments",
                columns: new[] { "firm_id", "engagement_id", "freeze_id" },
                principalTable: "engagement_file_freezes",
                principalColumns: new[] { "firm_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_file_freeze_early_lock_parent_scope",
                table: "file_freeze_early_locks",
                columns: new[] { "firm_id", "client_id", "engagement_id", "freeze_id", "report_deliverable_id" },
                principalTable: "engagement_file_freezes",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id", "report_deliverable_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_file_freeze_early_lock_parent_scope",
                table: "file_freeze_early_locks");

            migrationBuilder.DropForeignKey(
                name: "fk_file_freeze_amendment_freeze_scope",
                table: "file_freeze_amendments");

            migrationBuilder.DropForeignKey(
                name: "fk_engagement_file_freeze_deliverable_scope",
                table: "engagement_file_freezes");

            migrationBuilder.DropForeignKey(
                name: "fk_engagement_file_freeze_engagement_scope",
                table: "engagement_file_freezes");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_engagement_file_freeze_scope_report_id",
                table: "engagement_file_freezes");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_engagement_file_freeze_firm_engagement_id",
                table: "engagement_file_freezes");
        }
    }
}
