using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseCandidateCompositionPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_release_candidate_values",
                table: "release_candidates");

            migrationBuilder.AddColumn<Guid>(
                name: "composition_id",
                table: "release_candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "composition_manifest_digest",
                table: "release_candidates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "presentation_revision_id",
                table: "release_candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_candidate_values",
                table: "release_candidates",
                sql: "target_kind IN ('WORKPAPER','FINANCIAL_PACKAGE') AND revision >= 1 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND status IN ('READY','ISSUED') AND ((composition_id IS NULL AND presentation_revision_id IS NULL AND composition_manifest_digest IS NULL)   OR (composition_id IS NOT NULL AND presentation_revision_id IS NOT NULL AND composition_manifest_digest ~ '^[0-9a-f]{64}$'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_release_candidate_values",
                table: "release_candidates");

            migrationBuilder.DropColumn(
                name: "composition_id",
                table: "release_candidates");

            migrationBuilder.DropColumn(
                name: "composition_manifest_digest",
                table: "release_candidates");

            migrationBuilder.DropColumn(
                name: "presentation_revision_id",
                table: "release_candidates");

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_candidate_values",
                table: "release_candidates",
                sql: "target_kind IN ('WORKPAPER','FINANCIAL_PACKAGE') AND revision >= 1 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND status IN ('READY','ISSUED')");
        }
    }
}
