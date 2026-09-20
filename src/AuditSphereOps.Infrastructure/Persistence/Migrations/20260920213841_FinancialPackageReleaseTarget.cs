using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialPackageReleaseTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_release_candidate_values",
                table: "release_candidates");

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_candidate_values",
                table: "release_candidates",
                sql: "target_kind IN ('WORKPAPER','FINANCIAL_PACKAGE') AND revision >= 1 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND status IN ('READY','ISSUED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_release_candidate_values",
                table: "release_candidates");

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_candidate_values",
                table: "release_candidates",
                sql: "target_kind = 'WORKPAPER' AND revision >= 1 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND status IN ('READY','ISSUED')");
        }
    }
}
