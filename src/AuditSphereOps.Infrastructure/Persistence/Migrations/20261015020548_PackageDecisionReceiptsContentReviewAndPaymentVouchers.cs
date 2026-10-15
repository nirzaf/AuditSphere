using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PackageDecisionReceiptsContentReviewAndPaymentVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_document_values",
                table: "commercial_documents");

            migrationBuilder.AddColumn<Guid>(
                name: "previous_decision_id",
                table: "financial_package_review_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_hash",
                table: "financial_package_review_decisions",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                table: "financial_package_review_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sequence",
                table: "financial_package_review_decisions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<Guid>(
                name: "receipt_id",
                table: "commercial_notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "receipt_id",
                table: "commercial_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "financial_statement_content_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    presentation_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_sha256_hex = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    previous_review_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_statement_content_reviews", x => x.id);
                    table.UniqueConstraint("AK_financial_statement_content_reviews_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_financial_statement_content_review_values", "decision IN ('APPROVED','RETURNED') AND (decision = 'APPROVED' OR length(trim(reason)) > 0) AND snapshot_sha256_hex ~ '^[0-9a-f]{64}$' AND request_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_financial_statement_content_reviews_engagements_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_content_reviews_financial_packages_firm~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_content_reviews_financial_statement_con~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.previous_review_id },
                        principalTable: "financial_statement_content_reviews",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_content_reviews_financial_statement_pre~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.presentation_revision_id },
                        principalTable: "financial_statement_presentation_revisions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_content_reviews_users_firm_id_reviewed_~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engag~3",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "previous_decision_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_review_request",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "financial_package_id", "request_id" },
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_review_intent",
                table: "financial_package_review_decisions",
                sql: "(request_id IS NULL AND request_hash IS NULL) OR (request_id IS NOT NULL AND request_hash ~ '^[0-9a-f]{64}$')");

            migrationBuilder.CreateIndex(
                name: "IX_commercial_notifications_firm_id_receipt_id",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "receipt_id" },
                unique: true,
                filter: "receipt_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications",
                sql: "delivery_state IN ('QUEUED','DISPATCHED','PROVIDER_ACCEPTED','DELIVERED','REJECTED','UNKNOWN') AND kind IN ('RECEIPT','PROPOSAL','HOLDING_LETTER','PAYMENT_VOUCHER') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state IN ('PROVIDER_ACCEPTED','DELIVERED')) = (delivered_at IS NOT NULL)) AND ((kind = 'RECEIPT') = (fee_milestone_id IS NOT NULL)) AND ((kind = 'PROPOSAL') = (proposal_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (deliverable_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (dispatch_key IS NOT NULL)) AND ((kind = 'PAYMENT_VOUCHER') = (receipt_id IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_commercial_documents_firm_id_receipt_id_kind",
                table: "commercial_documents",
                columns: new[] { "firm_id", "receipt_id", "kind" },
                unique: true,
                filter: "kind = 'PAYMENT_VOUCHER'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_document_values",
                table: "commercial_documents",
                sql: "kind IN ('QUOTATION','ENGAGEMENT_LETTER','PAYMENT_RECEIPT','COMPREHENSIVE_PROPOSAL','PAYMENT_VOUCHER') AND length(template_version) > 0 AND length(sha256_hex) = 64 AND octet_length(bytes) > 0 AND length(file_name) > 0 AND ((kind = 'PAYMENT_VOUCHER') = (receipt_id IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_financial_statement_content_review_revision",
                table: "financial_statement_content_reviews",
                columns: new[] { "firm_id", "presentation_revision_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_content_reviews_firm_id_client_id_enga~1",
                table: "financial_statement_content_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "presentation_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_content_reviews_firm_id_client_id_enga~2",
                table: "financial_statement_content_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "previous_review_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_content_reviews_firm_id_client_id_engag~",
                table: "financial_statement_content_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_content_reviews_firm_id_reviewed_by_use~",
                table: "financial_statement_content_reviews",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_statement_content_review_request",
                table: "financial_statement_content_reviews",
                columns: new[] { "firm_id", "presentation_revision_id", "request_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_commercial_documents_receipts_firm_id_receipt_id",
                table: "commercial_documents",
                columns: new[] { "firm_id", "receipt_id" },
                principalTable: "receipts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_commercial_notifications_receipts_firm_id_receipt_id",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "receipt_id" },
                principalTable: "receipts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_package_review_decisions_financial_package_review~",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "previous_decision_id" },
                principalTable: "financial_package_review_decisions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // Content review decisions are professional evidence: append-only, like formal package decisions.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_financial_statement_content_reviews_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Financial statement content reviews are immutable historical evidence.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_financial_statement_content_reviews_append_only
                BEFORE UPDATE OR DELETE ON financial_statement_content_reviews
                FOR EACH ROW EXECUTE FUNCTION enforce_financial_statement_content_reviews_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A database without content reviews, decision receipts or payment vouchers may step back. Once any exists, dropping these
            // columns and the table would erase retained professional or payment evidence, so the downgrade refuses instead.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM financial_statement_content_reviews)
                     OR EXISTS (SELECT 1 FROM financial_package_review_decisions WHERE request_id IS NOT NULL OR previous_decision_id IS NOT NULL)
                     OR EXISTS (SELECT 1 FROM commercial_documents WHERE receipt_id IS NOT NULL)
                     OR EXISTS (SELECT 1 FROM commercial_notifications WHERE receipt_id IS NOT NULL) THEN
                    RAISE EXCEPTION 'Downgrade would discard content reviews, decision receipts or payment vouchers.';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_financial_statement_content_reviews_append_only ON financial_statement_content_reviews;
                DROP FUNCTION IF EXISTS enforce_financial_statement_content_reviews_append_only();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_commercial_documents_receipts_firm_id_receipt_id",
                table: "commercial_documents");

            migrationBuilder.DropForeignKey(
                name: "FK_commercial_notifications_receipts_firm_id_receipt_id",
                table: "commercial_notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_financial_package_review_decisions_financial_package_review~",
                table: "financial_package_review_decisions");

            migrationBuilder.DropTable(
                name: "financial_statement_content_reviews");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_review_decisions_firm_id_client_id_engag~3",
                table: "financial_package_review_decisions");

            migrationBuilder.DropIndex(
                name: "ux_financial_package_review_request",
                table: "financial_package_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_review_intent",
                table: "financial_package_review_decisions");

            migrationBuilder.DropIndex(
                name: "IX_commercial_notifications_firm_id_receipt_id",
                table: "commercial_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications");

            migrationBuilder.DropIndex(
                name: "IX_commercial_documents_firm_id_receipt_id_kind",
                table: "commercial_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_document_values",
                table: "commercial_documents");

            migrationBuilder.DropColumn(
                name: "previous_decision_id",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "financial_package_review_decisions");

            migrationBuilder.DropColumn(
                name: "receipt_id",
                table: "commercial_notifications");

            migrationBuilder.DropColumn(
                name: "receipt_id",
                table: "commercial_documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications",
                sql: "delivery_state IN ('QUEUED','DISPATCHED','PROVIDER_ACCEPTED','DELIVERED','REJECTED','UNKNOWN') AND kind IN ('RECEIPT','PROPOSAL','HOLDING_LETTER') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state IN ('PROVIDER_ACCEPTED','DELIVERED')) = (delivered_at IS NOT NULL)) AND ((kind = 'RECEIPT') = (fee_milestone_id IS NOT NULL)) AND ((kind = 'PROPOSAL') = (proposal_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (deliverable_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (dispatch_key IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_document_values",
                table: "commercial_documents",
                sql: "kind IN ('QUOTATION','ENGAGEMENT_LETTER','PAYMENT_RECEIPT','COMPREHENSIVE_PROPOSAL') AND length(template_version) > 0 AND length(sha256_hex) = 64 AND octet_length(bytes) > 0 AND length(file_name) > 0");
        }
    }
}
