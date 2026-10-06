using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePaymentTermsRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice_payment_terms_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    basis = table.Column<string>(type: "text", nullable: false),
                    terms_description = table.Column<string>(type: "text", nullable: false),
                    evidence_reference = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_payment_terms_revisions", x => x.id);
                    table.UniqueConstraint("AK_invoice_payment_terms_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_invoice_payment_terms_review", "(status = 'PENDING_REVIEW' AND reviewed_by_user_id IS NULL AND reviewed_at IS NULL AND review_reason IS NULL) OR (status = 'APPROVED' AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND length(review_reason) > 0) OR (status = 'REJECTED' AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND length(review_reason) > 0)");
                    table.CheckConstraint("ck_invoice_payment_terms_values", "revision >= 1 AND basis IN ('CONTRACTUAL_DUE_DATE','REVIEWED_TERMS_SNAPSHOT') AND length(terms_description) > 0 AND length(evidence_reference) > 0 AND status IN ('PENDING_REVIEW','APPROVED','REJECTED')");
                    table.ForeignKey(
                        name: "FK_invoice_payment_terms_revisions_invoices_firm_id_invoice_id",
                        columns: x => new { x.firm_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoice_payment_terms_revisions_users_firm_id_reviewed_by_u~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoice_payment_terms_revisions_users_firm_id_submitted_by_~",
                        columns: x => new { x.firm_id, x.submitted_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payment_terms_revisions_firm_id_invoice_id",
                table: "invoice_payment_terms_revisions",
                columns: new[] { "firm_id", "invoice_id" },
                unique: true,
                filter: "status = 'PENDING_REVIEW'");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payment_terms_revisions_firm_id_invoice_id_revision",
                table: "invoice_payment_terms_revisions",
                columns: new[] { "firm_id", "invoice_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payment_terms_revisions_firm_id_reviewed_by_user_id",
                table: "invoice_payment_terms_revisions",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payment_terms_revisions_firm_id_submitted_by_user_id",
                table: "invoice_payment_terms_revisions",
                columns: new[] { "firm_id", "submitted_by_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_payment_terms_revisions");
        }
    }
}
