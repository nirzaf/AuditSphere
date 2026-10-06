using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttributeStrataSamplingPreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_sampling_run_values",
                table: "audit_sampling_runs");

            migrationBuilder.AddColumn<string>(
                name: "ordering_policy",
                table: "audit_sampling_runs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preview_digest",
                table: "audit_sampling_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_sampling_runs_firm_id_preview_digest",
                table: "audit_sampling_runs",
                columns: new[] { "firm_id", "preview_digest" },
                unique: true,
                filter: "preview_digest IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_sampling_run_values",
                table: "audit_sampling_runs",
                sql: "method IN ('MUS','KEY_ITEM','RANDOM','SYSTEMATIC','STRATIFIED','ATTRIBUTE_STRATA') AND selected_count > 0 AND selected_count <= population_count AND length(source_digest) = 64 AND length(selection_digest) = 64 AND (preview_digest IS NULL OR length(preview_digest) = 64)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_sampling_runs_firm_id_preview_digest",
                table: "audit_sampling_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_sampling_run_values",
                table: "audit_sampling_runs");

            migrationBuilder.DropColumn(
                name: "ordering_policy",
                table: "audit_sampling_runs");

            migrationBuilder.DropColumn(
                name: "preview_digest",
                table: "audit_sampling_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_sampling_run_values",
                table: "audit_sampling_runs",
                sql: "method IN ('MUS','KEY_ITEM','RANDOM','SYSTEMATIC','STRATIFIED') AND selected_count > 0 AND selected_count <= population_count AND length(source_digest) = 64 AND length(selection_digest) = 64");
        }
    }
}
