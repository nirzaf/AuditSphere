using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PresentationCandidateSuccessorAndReviewArtifactPin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_statement_presentation_revision_values",
                table: "financial_statement_presentation_revisions");

            migrationBuilder.AddColumn<Guid>(
                name: "predecessor_revision_id",
                table: "financial_statement_presentation_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "presentation_revision_id",
                table: "financial_package_review_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_presentation_revisions_firm_id_client_~4",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "predecessor_revision_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_statement_presentation_revision_values",
                table: "financial_statement_presentation_revisions",
                sql: "package_revision >= 1 AND package_generation >= 1 AND package_calculation_hash ~ '^[0-9a-f]{64}$' AND length(trim(framework)) > 0 AND length(trim(taxonomy_version)) > 0 AND length(trim(template_version)) > 0 AND length(trim(calculation_engine_version)) > 0 AND currency ~ '^[A-Z]{3}$' AND period_start ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_end ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND layout_hash ~ '^[0-9a-f]{64}$' AND note_source_manifest_hash ~ '^[0-9a-f]{64}$' AND revision_hash ~ '^[0-9a-f]{64}$' AND snapshot_sha256_hex ~ '^[0-9a-f]{64}$' AND octet_length(snapshot_json) BETWEEN 1 AND 8388608 AND length(trim(builder_version)) > 0 AND length(trim(renderer_version)) > 0 AND length(trim(display_policy)) > 0 AND status IN ('UNAPPROVED_DRAFT','CANDIDATE','APPROVED','SUPERSEDED') AND ((comparative_package_id IS NULL AND comparative_package_revision IS NULL AND comparative_package_generation IS NULL AND comparative_calculation_hash IS NULL AND comparative_basis IS NULL AND comparative_evidence_reference IS NULL) OR (comparative_package_id IS NOT NULL AND comparative_package_revision IS NOT NULL AND comparative_package_revision >= 1 AND comparative_package_generation IS NOT NULL AND comparative_package_generation >= 1 AND comparative_calculation_hash IS NOT NULL AND comparative_calculation_hash ~ '^[0-9a-f]{64}$' AND comparative_basis IS NOT NULL AND length(trim(comparative_basis)) > 0 AND comparative_evidence_reference IS NOT NULL AND length(trim(comparative_evidence_reference)) > 0))");

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engag~1",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "presentation_revision_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_financial_package_review_decisions_financial_statement_pres~",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "presentation_revision_id" },
                principalTable: "financial_statement_presentation_revisions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_statement_presentation_revisions_financial_statem~",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "predecessor_revision_id" },
                principalTable: "financial_statement_presentation_revisions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_package_review_decisions_financial_statement_pres~",
                table: "financial_package_review_decisions");

            migrationBuilder.DropForeignKey(
                name: "FK_financial_statement_presentation_revisions_financial_statem~",
                table: "financial_statement_presentation_revisions");

            migrationBuilder.DropIndex(
                name: "IX_financial_statement_presentation_revisions_firm_id_client_~4",
                table: "financial_statement_presentation_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_statement_presentation_revision_values",
                table: "financial_statement_presentation_revisions");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engag~1",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "predecessor_revision_id",
                table: "financial_statement_presentation_revisions");

            migrationBuilder.DropColumn(
                name: "presentation_revision_id",
                table: "financial_package_review_decisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_statement_presentation_revision_values",
                table: "financial_statement_presentation_revisions",
                sql: "package_revision >= 1 AND package_generation >= 1 AND package_calculation_hash ~ '^[0-9a-f]{64}$' AND length(trim(framework)) > 0 AND length(trim(taxonomy_version)) > 0 AND length(trim(template_version)) > 0 AND length(trim(calculation_engine_version)) > 0 AND currency ~ '^[A-Z]{3}$' AND period_start ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_end ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND layout_hash ~ '^[0-9a-f]{64}$' AND note_source_manifest_hash ~ '^[0-9a-f]{64}$' AND revision_hash ~ '^[0-9a-f]{64}$' AND snapshot_sha256_hex ~ '^[0-9a-f]{64}$' AND octet_length(snapshot_json) BETWEEN 1 AND 8388608 AND length(trim(builder_version)) > 0 AND length(trim(renderer_version)) > 0 AND length(trim(display_policy)) > 0 AND status = 'UNAPPROVED_DRAFT' AND ((comparative_package_id IS NULL AND comparative_package_revision IS NULL AND comparative_package_generation IS NULL AND comparative_calculation_hash IS NULL AND comparative_basis IS NULL AND comparative_evidence_reference IS NULL) OR (comparative_package_id IS NOT NULL AND comparative_package_revision IS NOT NULL AND comparative_package_revision >= 1 AND comparative_package_generation IS NOT NULL AND comparative_package_generation >= 1 AND comparative_calculation_hash IS NOT NULL AND comparative_calculation_hash ~ '^[0-9a-f]{64}$' AND comparative_basis IS NOT NULL AND length(trim(comparative_basis)) > 0 AND comparative_evidence_reference IS NOT NULL AND length(trim(comparative_evidence_reference)) > 0))");
        }
    }
}
