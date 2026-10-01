using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SystematicAuditSampling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_sampling_run_values",
                table: "audit_sampling_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_sampling_run_values",
                table: "audit_sampling_runs",
                sql: "method IN ('MUS','KEY_ITEM','RANDOM','SYSTEMATIC','STRATIFIED') AND selected_count > 0 AND selected_count <= population_count AND length(source_digest) = 64 AND length(selection_digest) = 64");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_sampling_run_values",
                table: "audit_sampling_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_sampling_run_values",
                table: "audit_sampling_runs",
                sql: "method IN ('MUS','KEY_ITEM','RANDOM','STRATIFIED') AND selected_count > 0 AND selected_count <= population_count AND length(source_digest) = 64 AND length(selection_digest) = 64");
        }
    }
}
