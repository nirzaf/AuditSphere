using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientPurchaseCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_purchase_credit_note_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_open_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_submitted_revision = table.Column<long>(type: "bigint", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_json = table.Column<string>(type: "character varying(500000)", maxLength: 500000, nullable: false),
                    manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_basis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_purchase_credit_note_submissions", x => x.id);
                    table.UniqueConstraint("AK_client_purchase_credit_note_submissions_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_purchase_credit_note_submission", "journal_submitted_revision>0 AND length(trim(credit_note_reference))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND amount>0 AND currency ~ '^[A-Z]{3}$' AND intent_hash ~ '^[a-f0-9]{64}$' AND manifest_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(trim(reason))>0 AND length(manifest_json)>0 AND ((original_invoice_id IS NULL AND original_submission_id IS NULL AND original_open_item_id IS NULL AND length(trim(source_basis))>0) OR (original_invoice_id IS NOT NULL AND original_submission_id IS NOT NULL AND original_open_item_id IS NOT NULL)) AND ((source_receipt_id IS NULL AND source_receipt_hash IS NULL AND length(trim(source_basis))>0) OR (source_receipt_id IS NOT NULL AND source_receipt_hash ~ '^[a-f0-9]{64}$'))");
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_submissions_client_bookkeeping_~",
                        columns: x => new { x.firm_id, x.client_id, x.supplier_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_submissions_client_operational_~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_submissions_client_purchase_inv~",
                        columns: x => new { x.firm_id, x.client_id, x.original_open_item_id },
                        principalTable: "client_purchase_invoice_open_items",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_submissions_client_purchase_in~1",
                        columns: x => new { x.firm_id, x.client_id, x.original_submission_id },
                        principalTable: "client_purchase_invoice_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_submissions_client_reporting_pe~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_submissions_users_firm_id_creat~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_purchase_credit_note_decisions",
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
                    table.PrimaryKey("PK_client_purchase_credit_note_decisions", x => x.id);
                    table.UniqueConstraint("AK_client_purchase_credit_note_decisions_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_purchase_credit_note_decision", "decision IN ('APPROVE','RETURN') AND length(trim(reason))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(review_context_json)>0");
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_decisions_client_purchase_credi~",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_purchase_credit_note_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_decisions_users_firm_id_actor_u~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_purchase_credit_note_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    original_line_number = table.Column<int>(type: "integer", nullable: true),
                    expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_purchase_credit_note_lines", x => x.id);
                    table.UniqueConstraint("AK_client_purchase_credit_note_lines_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_purchase_credit_note_line", "line_number>0 AND (original_line_number IS NULL OR original_line_number>0) AND amount>0 AND length(trim(expense_account_code))>0");
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_lines_client_accounts_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.expense_account_id },
                        principalTable: "client_accounts",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_lines_client_purchase_credit_no~",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_purchase_credit_note_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_purchase_credit_note_open_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_purchase_credit_note_open_items", x => x.id);
                    table.UniqueConstraint("AK_client_purchase_credit_note_open_items_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_purchase_credit_note_open_item", "direction='DEBIT' AND original_amount>0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_open_items_client_bookkeeping_c~",
                        columns: x => new { x.firm_id, x.client_id, x.supplier_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_open_items_client_operational_j~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_purchase_credit_note_open_items_client_purchase_cred~",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_purchase_credit_note_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_decisions_firm_id_actor_user_id",
                table: "client_purchase_credit_note_decisions",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_decisions_firm_id_client_id_act~",
                table: "client_purchase_credit_note_decisions",
                columns: new[] { "firm_id", "client_id", "actor_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_decisions_firm_id_client_id_sub~",
                table: "client_purchase_credit_note_decisions",
                columns: new[] { "firm_id", "client_id", "submission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_lines_firm_id_client_id_expense~",
                table: "client_purchase_credit_note_lines",
                columns: new[] { "firm_id", "client_id", "expense_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_lines_firm_id_client_id_submiss~",
                table: "client_purchase_credit_note_lines",
                columns: new[] { "firm_id", "client_id", "submission_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_open_items_firm_id_client_id_jo~",
                table: "client_purchase_credit_note_open_items",
                columns: new[] { "firm_id", "client_id", "journal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_open_items_firm_id_client_id_s~1",
                table: "client_purchase_credit_note_open_items",
                columns: new[] { "firm_id", "client_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_open_items_firm_id_client_id_su~",
                table: "client_purchase_credit_note_open_items",
                columns: new[] { "firm_id", "client_id", "submission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_submissions_firm_id_client_id_~1",
                table: "client_purchase_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "credit_note_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_submissions_firm_id_client_id_~2",
                table: "client_purchase_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "original_submission_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_submissions_firm_id_client_id_~3",
                table: "client_purchase_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "created_by_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_submissions_firm_id_client_id_c~",
                table: "client_purchase_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "credit_note_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_submissions_firm_id_client_id_j~",
                table: "client_purchase_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "journal_id", "journal_submitted_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_submissions_firm_id_client_id_o~",
                table: "client_purchase_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "original_open_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_submissions_firm_id_client_id_p~",
                table: "client_purchase_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_submissions_firm_id_client_id_s~",
                table: "client_purchase_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_purchase_credit_note_submissions_firm_id_created_by_~",
                table: "client_purchase_credit_note_submissions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.Sql(ClientPurchaseCreditNoteSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ClientPurchaseCreditNoteSql.Down);
            migrationBuilder.DropTable(
                name: "client_purchase_credit_note_decisions");

            migrationBuilder.DropTable(
                name: "client_purchase_credit_note_lines");

            migrationBuilder.DropTable(
                name: "client_purchase_credit_note_open_items");

            migrationBuilder.DropTable(
                name: "client_purchase_credit_note_submissions");
        }
    }
}
