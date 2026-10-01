using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SteDeliverableAssembly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_releases_firm_id_client_id_engagement_id",
                table: "releases");

            migrationBuilder.AddColumn<Guid>(
                name: "acceptance_decision_id",
                table: "commercial_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "commercial_accepted_at",
                table: "commercial_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "firm_seal_specimen_id",
                table: "commercial_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "signature_specimen_id",
                table: "commercial_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "affected_taxonomy_node_id",
                table: "audit_opinion_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_signature_specimens_firm_id_id",
                table: "signature_specimens",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_reporting_taxonomy_nodes_firm_id_id",
                table: "reporting_taxonomy_nodes",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_releases_firm_id_client_id_engagement_id_id",
                table: "releases",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_audit_deliverables_firm_id_client_id_engagement_id_id",
                table: "audit_deliverables",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "firm_seal_specimens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    png_content = table.Column<byte[]>(type: "bytea", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    registered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_seal_specimens", x => x.id);
                    table.UniqueConstraint("AK_firm_seal_specimens_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_firm_seal_specimen", "version >= 1 AND length(sha256) = 64 AND octet_length(png_content) BETWEEN 1 AND 524288");
                });

            migrationBuilder.CreateTable(
                name: "signed_representation_letters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deliverable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deliverable_sha256 = table.Column<string>(type: "text", nullable: false),
                    content_sha256 = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    management_signatory = table.Column<string>(type: "text", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signed_representation_letters", x => x.id);
                    table.UniqueConstraint("AK_signed_representation_letters_firm_id_client_id_engagement_~", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_signed_representation_letter", "length(deliverable_sha256) = 64 AND length(content_sha256) = 64 AND octet_length(content) BETWEEN 1 AND 10485760 AND length(management_signatory) BETWEEN 2 AND 200");
                    table.ForeignKey(
                        name: "FK_signed_representation_letters_audit_deliverables_firm_id_cl~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.deliverable_id },
                        principalTable: "audit_deliverables",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "commercial_deliverable_bundles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    management_letter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_representation_letter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_digest = table.Column<string>(type: "text", nullable: false),
                    manifest_json = table.Column<string>(type: "text", nullable: false),
                    manifest_sha256 = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_sha256 = table.Column<string>(type: "text", nullable: false),
                    assembled_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assembled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commercial_deliverable_bundles", x => x.id);
                    table.CheckConstraint("ck_commercial_deliverable_bundle", "length(source_digest) = 64 AND length(manifest_sha256) = 64 AND length(content_sha256) = 64 AND octet_length(content) > 0");
                    table.ForeignKey(
                        name: "FK_commercial_deliverable_bundles_audit_deliverables_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.management_letter_id },
                        principalTable: "audit_deliverables",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_commercial_deliverable_bundles_audit_deliverables_firm_id_~1",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.signed_report_id },
                        principalTable: "audit_deliverables",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_commercial_deliverable_bundles_engagements_firm_id_client_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_commercial_deliverable_bundles_financial_package_artifacts_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_artifact_id },
                        principalTable: "financial_package_artifacts",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_commercial_deliverable_bundles_invoices_firm_id_balance_inv~",
                        columns: x => new { x.firm_id, x.balance_invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_commercial_deliverable_bundles_releases_firm_id_client_id_e~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_release_id },
                        principalTable: "releases",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_commercial_deliverable_bundles_signed_representation_letter~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.signed_representation_letter_id },
                        principalTable: "signed_representation_letters",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "representation_letter_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_letter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_representation_letter_verifications", x => x.id);
                    table.CheckConstraint("ck_representation_letter_verification", "length(reason) BETWEEN 10 AND 2000");
                    table.ForeignKey(
                        name: "FK_representation_letter_verifications_signed_representation_l~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.signed_letter_id },
                        principalTable: "signed_representation_letters",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_documents_firm_id_acceptance_decision_id",
                table: "commercial_documents",
                columns: new[] { "firm_id", "acceptance_decision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_documents_firm_id_firm_seal_specimen_id",
                table: "commercial_documents",
                columns: new[] { "firm_id", "firm_seal_specimen_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_documents_firm_id_signature_specimen_id",
                table: "commercial_documents",
                columns: new[] { "firm_id", "signature_specimen_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_opinion_decisions_firm_id_affected_taxonomy_node_id",
                table: "audit_opinion_decisions",
                columns: new[] { "firm_id", "affected_taxonomy_node_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_balance_invoice_id",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "balance_invoice_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~1",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_release_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~2",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "client_id", "engagement_id", "management_letter_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~3",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "client_id", "engagement_id", "signed_report_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagemen~4",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "client_id", "engagement_id", "signed_representation_letter_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_client_id_engagement~",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_artifact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_deliverable_bundles_firm_id_engagement_id_source~",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "engagement_id", "source_digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_seal_specimens_firm_id_version",
                table: "firm_seal_specimens",
                columns: new[] { "firm_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_representation_letter_verifications_firm_id_client_id_engag~",
                table: "representation_letter_verifications",
                columns: new[] { "firm_id", "client_id", "engagement_id", "signed_letter_id" });

            migrationBuilder.CreateIndex(
                name: "IX_representation_letter_verifications_firm_id_signed_letter_id",
                table: "representation_letter_verifications",
                columns: new[] { "firm_id", "signed_letter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_signed_representation_letters_firm_id_client_id_engagement_~",
                table: "signed_representation_letters",
                columns: new[] { "firm_id", "client_id", "engagement_id", "deliverable_id" });

            migrationBuilder.CreateIndex(
                name: "IX_signed_representation_letters_firm_id_deliverable_id_conten~",
                table: "signed_representation_letters",
                columns: new[] { "firm_id", "deliverable_id", "content_sha256" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_opinion_decisions_reporting_taxonomy_nodes_firm_id_af~",
                table: "audit_opinion_decisions",
                columns: new[] { "firm_id", "affected_taxonomy_node_id" },
                principalTable: "reporting_taxonomy_nodes",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_commercial_documents_acceptance_decisions_firm_id_acceptanc~",
                table: "commercial_documents",
                columns: new[] { "firm_id", "acceptance_decision_id" },
                principalTable: "acceptance_decisions",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_commercial_documents_firm_seal_specimens_firm_id_firm_seal_~",
                table: "commercial_documents",
                columns: new[] { "firm_id", "firm_seal_specimen_id" },
                principalTable: "firm_seal_specimens",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_commercial_documents_signature_specimens_firm_id_signature_~",
                table: "commercial_documents",
                columns: new[] { "firm_id", "signature_specimen_id" },
                principalTable: "signature_specimens",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
              CREATE TRIGGER signed_representation_letters_append_only BEFORE UPDATE OR DELETE ON signed_representation_letters FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
              CREATE TRIGGER representation_letter_verifications_append_only BEFORE UPDATE OR DELETE ON representation_letter_verifications FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
              CREATE TRIGGER firm_seal_specimens_append_only BEFORE UPDATE OR DELETE ON firm_seal_specimens FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
              CREATE TRIGGER commercial_deliverable_bundles_append_only BEFORE UPDATE OR DELETE ON commercial_deliverable_bundles FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
              CREATE FUNCTION require_engagement_letter_dual_keys() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF NEW.kind = 'ENGAGEMENT_LETTER' AND NOT EXISTS (
                  SELECT 1 FROM proposals p
                  JOIN quotation_versions q ON q.id = NEW.quotation_version_id AND q.firm_id = p.firm_id AND q.proposal_id = p.id
                  JOIN opportunities o ON o.id = p.opportunity_id AND o.firm_id = p.firm_id
                  JOIN client_safety_states g ON g.id = p.practice_client_id AND g.firm_id = p.firm_id
                  JOIN acceptance_decisions a ON a.id = NEW.acceptance_decision_id AND a.firm_id = p.firm_id AND a.practice_client_id = p.practice_client_id
                  JOIN signature_specimens s ON s.id = NEW.signature_specimen_id AND s.firm_id = p.firm_id AND s.user_id = NEW.created_by_user_id AND s.revoked_at IS NULL
                  JOIN firm_seal_specimens f ON f.id = NEW.firm_seal_specimen_id AND f.firm_id = p.firm_id
                  WHERE p.id = NEW.proposal_id AND p.firm_id = NEW.firm_id AND p.status = 'ACCEPTED'
                    AND p.response_at = NEW.commercial_accepted_at AND q.status = 'APPROVED' AND q.fee = p.fee
                    AND a.decision = 'Accepted' AND a.engagement_id IS NULL AND a.service_route = o.service_route
                    AND a.generation = g.input_generation AND a.decided_at IS NOT NULL AND a.decided_by_user_id IS NOT NULL
                ) THEN RAISE EXCEPTION 'Current commercial acceptance, Partner risk clearance, signature and seal are required'; END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER commercial_engagement_letter_dual_keys BEFORE INSERT ON commercial_documents FOR EACH ROW EXECUTE FUNCTION require_engagement_letter_dual_keys();
              """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER commercial_engagement_letter_dual_keys ON commercial_documents; DROP FUNCTION require_engagement_letter_dual_keys();");
            migrationBuilder.DropForeignKey(
                name: "FK_audit_opinion_decisions_reporting_taxonomy_nodes_firm_id_af~",
                table: "audit_opinion_decisions");

            migrationBuilder.DropForeignKey(
                name: "FK_commercial_documents_acceptance_decisions_firm_id_acceptanc~",
                table: "commercial_documents");

            migrationBuilder.DropForeignKey(
                name: "FK_commercial_documents_firm_seal_specimens_firm_id_firm_seal_~",
                table: "commercial_documents");

            migrationBuilder.DropForeignKey(
                name: "FK_commercial_documents_signature_specimens_firm_id_signature_~",
                table: "commercial_documents");

            migrationBuilder.DropTable(
                name: "commercial_deliverable_bundles");

            migrationBuilder.DropTable(
                name: "firm_seal_specimens");

            migrationBuilder.DropTable(
                name: "representation_letter_verifications");

            migrationBuilder.DropTable(
                name: "signed_representation_letters");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_signature_specimens_firm_id_id",
                table: "signature_specimens");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_reporting_taxonomy_nodes_firm_id_id",
                table: "reporting_taxonomy_nodes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_releases_firm_id_client_id_engagement_id_id",
                table: "releases");

            migrationBuilder.DropIndex(
                name: "IX_commercial_documents_firm_id_acceptance_decision_id",
                table: "commercial_documents");

            migrationBuilder.DropIndex(
                name: "IX_commercial_documents_firm_id_firm_seal_specimen_id",
                table: "commercial_documents");

            migrationBuilder.DropIndex(
                name: "IX_commercial_documents_firm_id_signature_specimen_id",
                table: "commercial_documents");

            migrationBuilder.DropIndex(
                name: "IX_audit_opinion_decisions_firm_id_affected_taxonomy_node_id",
                table: "audit_opinion_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_audit_deliverables_firm_id_client_id_engagement_id_id",
                table: "audit_deliverables");

            migrationBuilder.DropColumn(
                name: "acceptance_decision_id",
                table: "commercial_documents");

            migrationBuilder.DropColumn(
                name: "commercial_accepted_at",
                table: "commercial_documents");

            migrationBuilder.DropColumn(
                name: "firm_seal_specimen_id",
                table: "commercial_documents");

            migrationBuilder.DropColumn(
                name: "signature_specimen_id",
                table: "commercial_documents");

            migrationBuilder.DropColumn(
                name: "affected_taxonomy_node_id",
                table: "audit_opinion_decisions");

            migrationBuilder.CreateIndex(
                name: "IX_releases_firm_id_client_id_engagement_id",
                table: "releases",
                columns: new[] { "firm_id", "client_id", "engagement_id" });
        }
    }
}
