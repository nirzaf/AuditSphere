using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientPurchaseInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_purchase_invoice_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    previous_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_revision = table.Column<long>(type: "bigint", nullable: true),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chart_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_invoice_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_supplier_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    voucher_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    receipt_date = table.Column<DateOnly>(type: "date", nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    accounting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    supply_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    snapshot_json = table.Column<string>(type: "character varying(500000)", maxLength: 500000, nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_purchase_invoice_drafts", x => x.id);
                    table.UniqueConstraint("AK_client_purchase_invoice_drafts_firm_id_client_id_invoice_id~", x => new { x.firm_id, x.client_id, x.invoice_id, x.id, x.revision });
                    table.CheckConstraint("ck_client_purchase_invoice_draft", "revision>0 AND invoice_id<>'00000000-0000-0000-0000-000000000000'::uuid AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND length(trim(voucher_reference))>0 AND length(trim(supplier_invoice_reference))>0 AND length(trim(normalized_supplier_reference))>0 AND receipt_date>=document_date AND accounting_date BETWEEN '0001-01-01' AND '9999-12-31' AND due_date>=document_date AND currency ~ '^[A-Z]{3}$' AND net_amount>=0 AND tax_amount>=0 AND gross_amount=net_amount+tax_amount AND intent_hash ~ '^[a-f0-9]{64}$' AND snapshot_hash ~ '^[a-f0-9]{64}$' AND length(snapshot_json)>0 AND ((revision=1 AND previous_revision_id IS NULL AND previous_revision IS NULL) OR (revision>1 AND previous_revision_id IS NOT NULL AND previous_revision=revision-1))");
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_drafts_client_bookkeeping_counterpa~",
                        columns: x => new { x.firm_id, x.client_id, x.supplier_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_drafts_client_chart_versions_firm_i~",
                        columns: x => new { x.firm_id, x.client_id, x.chart_version_id },
                        principalTable: "client_chart_versions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_drafts_client_purchase_invoice_draf~",
                        columns: x => new { x.firm_id, x.client_id, x.invoice_id, x.previous_revision_id, x.previous_revision },
                        principalTable: "client_purchase_invoice_drafts",
                        principalColumns: new[] { "firm_id", "client_id", "invoice_id", "id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_drafts_client_reporting_periods_fir~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_drafts_users_firm_id_created_by_use~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_purchase_invoice_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_revision = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_invoice_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_supplier_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_submitted_revision = table.Column<long>(type: "bigint", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_json = table.Column<string>(type: "character varying(500000)", maxLength: 500000, nullable: false),
                    manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_basis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_purchase_invoice_submissions", x => x.id);
                    table.UniqueConstraint("AK_client_purchase_invoice_submissions_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_purchase_invoice_submission", "journal_submitted_revision>0 AND length(trim(supplier_invoice_reference))>0 AND length(trim(normalized_supplier_reference))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND manifest_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(manifest_json)>0 AND ((source_receipt_id IS NULL AND source_receipt_hash IS NULL AND length(trim(source_basis))>0) OR (source_receipt_id IS NOT NULL AND source_receipt_hash ~ '^[a-f0-9]{64}$'))");
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_submissions_client_bookkeeping_coun~",
                        columns: x => new { x.firm_id, x.client_id, x.supplier_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_submissions_client_operational_jour~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_submissions_client_purchase_invoice~",
                        columns: x => new { x.firm_id, x.client_id, x.invoice_id, x.draft_id, x.draft_revision },
                        principalTable: "client_purchase_invoice_drafts",
                        principalColumns: new[] { "firm_id", "client_id", "invoice_id", "id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_submissions_users_firm_id_created_b~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_purchase_invoice_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    duplicate_resolution_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    preview_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_context_json = table.Column<string>(type: "character varying(500000)", maxLength: 500000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_purchase_invoice_decisions", x => x.id);
                    table.UniqueConstraint("AK_client_purchase_invoice_decisions_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_purchase_invoice_decision", "decision IN ('APPROVE','RETURN') AND length(trim(reason))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(review_context_json)>0");
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_decisions_client_purchase_invoice_s~",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_purchase_invoice_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_decisions_users_firm_id_actor_user_~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_purchase_invoice_open_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_purchase_invoice_open_items", x => x.id);
                    table.UniqueConstraint("AK_client_purchase_invoice_open_items_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_purchase_invoice_open_item", "original_amount>0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_open_items_client_bookkeeping_count~",
                        columns: x => new { x.firm_id, x.client_id, x.supplier_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_open_items_client_operational_journ~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_invoice_open_items_client_purchase_invoice_~",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_purchase_invoice_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_decisions_firm_id_actor_user_id",
                table: "client_purchase_invoice_decisions",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_decisions_firm_id_client_id_actor_u~",
                table: "client_purchase_invoice_decisions",
                columns: new[] { "firm_id", "client_id", "actor_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_decisions_firm_id_client_id_submiss~",
                table: "client_purchase_invoice_decisions",
                columns: new[] { "firm_id", "client_id", "submission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_drafts_firm_id_client_id_chart_vers~",
                table: "client_purchase_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "chart_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_drafts_firm_id_client_id_created_by~",
                table: "client_purchase_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "created_by_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_drafts_firm_id_client_id_invoice_i~1",
                table: "client_purchase_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "invoice_id", "previous_revision_id", "previous_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_drafts_firm_id_client_id_invoice_id~",
                table: "client_purchase_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "invoice_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_drafts_firm_id_client_id_period_id",
                table: "client_purchase_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_drafts_firm_id_client_id_supplier_id",
                table: "client_purchase_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_drafts_firm_id_client_id_voucher_re~",
                table: "client_purchase_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "voucher_reference" },
                unique: true,
                filter: "revision=1");

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_drafts_firm_id_created_by_user_id",
                table: "client_purchase_invoice_drafts",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_open_items_firm_id_client_id_invoic~",
                table: "client_purchase_invoice_open_items",
                columns: new[] { "firm_id", "client_id", "invoice_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_open_items_firm_id_client_id_journa~",
                table: "client_purchase_invoice_open_items",
                columns: new[] { "firm_id", "client_id", "journal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_open_items_firm_id_client_id_submis~",
                table: "client_purchase_invoice_open_items",
                columns: new[] { "firm_id", "client_id", "submission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_open_items_firm_id_client_id_suppli~",
                table: "client_purchase_invoice_open_items",
                columns: new[] { "firm_id", "client_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_submissions_firm_id_client_id_creat~",
                table: "client_purchase_invoice_submissions",
                columns: new[] { "firm_id", "client_id", "created_by_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_submissions_firm_id_client_id_invoi~",
                table: "client_purchase_invoice_submissions",
                columns: new[] { "firm_id", "client_id", "invoice_id", "draft_id", "draft_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_submissions_firm_id_client_id_journ~",
                table: "client_purchase_invoice_submissions",
                columns: new[] { "firm_id", "client_id", "journal_id", "journal_submitted_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_submissions_firm_id_client_id_suppl~",
                table: "client_purchase_invoice_submissions",
                columns: new[] { "firm_id", "client_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_invoice_submissions_firm_id_created_by_user~",
                table: "client_purchase_invoice_submissions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.Sql(ClientPurchaseInvoiceSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ClientPurchaseInvoiceSql.Down);
            migrationBuilder.DropTable(
                name: "client_purchase_invoice_decisions");

            migrationBuilder.DropTable(
                name: "client_purchase_invoice_open_items");

            migrationBuilder.DropTable(
                name: "client_purchase_invoice_submissions");

            migrationBuilder.DropTable(
                name: "client_purchase_invoice_drafts");
        }
    }
}
