using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialStatementReviewCompositionBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_release_values",
                table: "releases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_review_values",
                table: "financial_package_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_deliverable_bundle",
                table: "commercial_deliverable_bundles");

            migrationBuilder.RenameIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engag~1",
                table: "financial_package_review_decisions",
                newName: "IX_financial_package_review_decisions_firm_id_client_id_engag~2");

            migrationBuilder.RenameIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~4",
                table: "commercial_deliverable_bundles",
                newName: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~5");

            migrationBuilder.RenameIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~3",
                table: "commercial_deliverable_bundles",
                newName: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~4");

            migrationBuilder.RenameIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~2",
                table: "commercial_deliverable_bundles",
                newName: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~3");

            migrationBuilder.AddColumn<Guid>(
                name: "pinned_composition_id",
                table: "releases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pinned_composition_manifest_digest",
                table: "releases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "composition_manifest_digest",
                table: "financial_package_review_decisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "presentation_composition_id",
                table: "financial_package_review_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "composition_manifest_digest",
                table: "commercial_deliverable_bundles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "financial_statement_composition_id",
                table: "commercial_deliverable_bundles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_financial_statement_compositions_scope_id",
                table: "financial_statement_compositions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_releases_firm_id_client_id_engagement_id_pinned_composition~",
                table: "releases",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pinned_composition_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_values",
                table: "releases",
                sql: "package_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND length(authorized_release_key) > 0 AND ((pinned_composition_id IS NULL AND pinned_composition_manifest_digest IS NULL) OR (pinned_composition_id IS NOT NULL AND pinned_composition_manifest_digest ~ '^[0-9a-f]{64}$'))");

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engag~1",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "presentation_composition_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_review_values",
                table: "financial_package_review_decisions",
                sql: "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$' AND artifact_sha256_hex ~ '^[0-9a-f]{64}$' AND length(trim(artifact_version)) > 0 AND stage IN ('MANAGEMENT_APPROVAL','ACCOUNTING_REVIEW','PARTNER_APPROVAL') AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED') AND evidence_mode IN ('SIGNED_IN','OFFLINE') AND length(trim(evidence_reference)) > 0 AND ((presentation_composition_id IS NULL AND composition_manifest_digest IS NULL) OR (presentation_composition_id IS NOT NULL AND composition_manifest_digest ~ '^[0-9a-f]{64}$')) AND ((evidence_mode = 'SIGNED_IN' AND decided_by_user_id IS NOT NULL) OR evidence_mode = 'OFFLINE')");

            migrationBuilder.CreateIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~2",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_statement_composition_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_deliverable_bundle",
                table: "commercial_deliverable_bundles",
                sql: "length(source_digest) = 64 AND length(manifest_sha256) = 64 AND length(content_sha256) = 64 AND octet_length(content) > 0 AND ((financial_statement_composition_id IS NULL AND composition_manifest_digest IS NULL) OR (financial_statement_composition_id IS NOT NULL AND composition_manifest_digest ~ '^[0-9a-f]{64}$'))");

            migrationBuilder.AddForeignKey(
                name: "FK_commercial_deliverable_bundles_financial_statement_composit~",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_statement_composition_id" },
                principalTable: "financial_statement_compositions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_package_review_decisions_financial_statement_comp~",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "presentation_composition_id" },
                principalTable: "financial_statement_compositions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_releases_financial_statement_compositions_firm_id_client_id~",
                table: "releases",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pinned_composition_id" },
                principalTable: "financial_statement_compositions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_commercial_deliverable_bundles_financial_statement_composit~",
                table: "commercial_deliverable_bundles");

            migrationBuilder.DropForeignKey(
                name: "FK_financial_package_review_decisions_financial_statement_comp~",
                table: "financial_package_review_decisions");

            migrationBuilder.DropForeignKey(
                name: "FK_releases_financial_statement_compositions_firm_id_client_id~",
                table: "releases");

            migrationBuilder.DropIndex(
                name: "IX_releases_firm_id_client_id_engagement_id_pinned_composition~",
                table: "releases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_release_values",
                table: "releases");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_financial_statement_compositions_scope_id",
                table: "financial_statement_compositions");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engag~1",
                table: "financial_package_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_review_values",
                table: "financial_package_review_decisions");

            migrationBuilder.DropIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~2",
                table: "commercial_deliverable_bundles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_deliverable_bundle",
                table: "commercial_deliverable_bundles");

            migrationBuilder.DropColumn(
                name: "pinned_composition_id",
                table: "releases");

            migrationBuilder.DropColumn(
                name: "pinned_composition_manifest_digest",
                table: "releases");

            migrationBuilder.DropColumn(
                name: "composition_manifest_digest",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "presentation_composition_id",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "composition_manifest_digest",
                table: "commercial_deliverable_bundles");

            migrationBuilder.DropColumn(
                name: "financial_statement_composition_id",
                table: "commercial_deliverable_bundles");

            migrationBuilder.RenameIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engag~2",
                table: "financial_package_review_decisions",
                newName: "IX_financial_package_review_decisions_firm_id_client_id_engag~1");

            migrationBuilder.RenameIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~5",
                table: "commercial_deliverable_bundles",
                newName: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~4");

            migrationBuilder.RenameIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~4",
                table: "commercial_deliverable_bundles",
                newName: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~3");

            migrationBuilder.RenameIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~3",
                table: "commercial_deliverable_bundles",
                newName: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_values",
                table: "releases",
                sql: "package_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND length(authorized_release_key) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_review_values",
                table: "financial_package_review_decisions",
                sql: "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$' AND artifact_sha256_hex ~ '^[0-9a-f]{64}$' AND length(trim(artifact_version)) > 0 AND stage IN ('MANAGEMENT_APPROVAL','ACCOUNTING_REVIEW','PARTNER_APPROVAL') AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED') AND evidence_mode IN ('SIGNED_IN','OFFLINE') AND length(trim(evidence_reference)) > 0 AND ((evidence_mode = 'SIGNED_IN' AND decided_by_user_id IS NOT NULL) OR evidence_mode = 'OFFLINE')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_deliverable_bundle",
                table: "commercial_deliverable_bundles",
                sql: "length(source_digest) = 64 AND length(manifest_sha256) = 64 AND length(content_sha256) = 64 AND octet_length(content) > 0");
        }
    }
}
