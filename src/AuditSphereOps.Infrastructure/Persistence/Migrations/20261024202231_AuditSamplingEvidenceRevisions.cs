using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditSamplingEvidenceRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_audit_subsequent_match_item",
                table: "audit_subsequent_match_records");

            migrationBuilder.DropIndex(
                name: "ux_audit_cut_off_test_item",
                table: "audit_cut_off_test_records");

            migrationBuilder.CreateIndex(
                name: "ux_audit_subsequent_match_item_revision",
                table: "audit_subsequent_match_records",
                columns: new[] { "firm_id", "engagement_id", "selection_item_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_audit_cut_off_test_item_revision",
                table: "audit_cut_off_test_records",
                columns: new[] { "firm_id", "engagement_id", "selection_item_id", "revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_audit_subsequent_match_item_revision",
                table: "audit_subsequent_match_records");

            migrationBuilder.DropIndex(
                name: "ux_audit_cut_off_test_item_revision",
                table: "audit_cut_off_test_records");

            migrationBuilder.CreateIndex(
                name: "ux_audit_subsequent_match_item",
                table: "audit_subsequent_match_records",
                columns: new[] { "firm_id", "engagement_id", "selection_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_audit_cut_off_test_item",
                table: "audit_cut_off_test_records",
                columns: new[] { "firm_id", "engagement_id", "selection_item_id" },
                unique: true);
        }
    }
}
