using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientSalesCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_client_sales_invoice_open_items_firm_id_client_id_id",
                table: "client_sales_invoice_open_items",
                columns: new[] { "firm_id", "client_id", "id" });

            migrationBuilder.CreateTable(
                name: "client_sales_credit_note_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_open_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_client_sales_credit_note_submissions", x => x.id);
                    table.UniqueConstraint("AK_client_sales_credit_note_submissions_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_sales_credit_submission", "credit_note_id<>'00000000-0000-0000-0000-000000000000'::uuid AND length(trim(credit_note_reference))>0 AND original_invoice_id<>'00000000-0000-0000-0000-000000000000'::uuid AND amount>0 AND currency ~ '^[A-Z]{3}$' AND journal_submitted_revision>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND manifest_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(trim(reason))>0 AND ((source_receipt_id IS NULL AND source_receipt_hash IS NULL AND length(trim(source_basis))>0) OR (source_receipt_id IS NOT NULL AND source_receipt_hash ~ '^[a-f0-9]{64}$')) AND length(manifest_json)>0");
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_submissions_client_bookkeeping_cou~",
                        columns: x => new { x.firm_id, x.client_id, x.customer_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_submissions_client_operational_jou~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_submissions_client_reporting_perio~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_submissions_client_sales_invoice_o~",
                        columns: x => new { x.firm_id, x.client_id, x.original_open_item_id },
                        principalTable: "client_sales_invoice_open_items",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_submissions_client_sales_invoice_s~",
                        columns: x => new { x.firm_id, x.client_id, x.original_submission_id },
                        principalTable: "client_sales_invoice_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_submissions_users_firm_id_created_~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_sales_credit_note_decisions",
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
                    preview_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_context_json = table.Column<string>(type: "character varying(500000)", maxLength: 500000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_sales_credit_note_decisions", x => x.id);
                    table.UniqueConstraint("AK_client_sales_credit_note_decisions_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_sales_credit_decision", "decision IN ('APPROVE','RETURN') AND length(trim(reason))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(review_context_json)>0");
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_decisions_client_sales_credit_note~",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_sales_credit_note_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_decisions_users_firm_id_actor_user~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_sales_credit_note_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_line_number = table.Column<int>(type: "integer", nullable: false),
                    revenue_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_sales_credit_note_lines", x => x.id);
                    table.UniqueConstraint("AK_client_sales_credit_note_lines_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_sales_credit_line", "original_line_number>0 AND amount>0 AND length(trim(revenue_account_code))>0");
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_lines_client_accounts_firm_id_clie~",
                        columns: x => new { x.firm_id, x.client_id, x.revenue_account_id },
                        principalTable: "client_accounts",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_lines_client_sales_credit_note_sub~",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_sales_credit_note_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_sales_credit_note_open_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_sales_credit_note_open_items", x => x.id);
                    table.UniqueConstraint("AK_client_sales_credit_note_open_items_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_sales_credit_open_item", "original_amount>0 AND direction='CREDIT' AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_open_items_client_bookkeeping_coun~",
                        columns: x => new { x.firm_id, x.client_id, x.customer_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_open_items_client_operational_jour~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_credit_note_open_items_client_sales_credit_not~",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_sales_credit_note_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_decisions_firm_id_actor_user_id",
                table: "client_sales_credit_note_decisions",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_decisions_firm_id_client_id_actor_~",
                table: "client_sales_credit_note_decisions",
                columns: new[] { "firm_id", "client_id", "actor_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_decisions_firm_id_client_id_submis~",
                table: "client_sales_credit_note_decisions",
                columns: new[] { "firm_id", "client_id", "submission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_lines_firm_id_client_id_revenue_ac~",
                table: "client_sales_credit_note_lines",
                columns: new[] { "firm_id", "client_id", "revenue_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_lines_firm_id_client_id_submission~",
                table: "client_sales_credit_note_lines",
                columns: new[] { "firm_id", "client_id", "submission_id", "original_line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_open_items_firm_id_client_id_credi~",
                table: "client_sales_credit_note_open_items",
                columns: new[] { "firm_id", "client_id", "credit_note_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_open_items_firm_id_client_id_custo~",
                table: "client_sales_credit_note_open_items",
                columns: new[] { "firm_id", "client_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_open_items_firm_id_client_id_journ~",
                table: "client_sales_credit_note_open_items",
                columns: new[] { "firm_id", "client_id", "journal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_open_items_firm_id_client_id_submi~",
                table: "client_sales_credit_note_open_items",
                columns: new[] { "firm_id", "client_id", "submission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_submissions_firm_id_client_id_cre~1",
                table: "client_sales_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "credit_note_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_submissions_firm_id_client_id_crea~",
                table: "client_sales_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "created_by_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_submissions_firm_id_client_id_cred~",
                table: "client_sales_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "credit_note_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_submissions_firm_id_client_id_cust~",
                table: "client_sales_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_submissions_firm_id_client_id_jour~",
                table: "client_sales_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "journal_id", "journal_submitted_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_submissions_firm_id_client_id_ori~1",
                table: "client_sales_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "original_submission_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_submissions_firm_id_client_id_orig~",
                table: "client_sales_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "original_open_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_submissions_firm_id_client_id_peri~",
                table: "client_sales_credit_note_submissions",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_credit_note_submissions_firm_id_created_by_use~",
                table: "client_sales_credit_note_submissions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.Sql(ClientSalesCreditNoteSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ClientSalesCreditNoteSql.Down);
            migrationBuilder.DropTable(
                name: "client_sales_credit_note_decisions");

            migrationBuilder.DropTable(
                name: "client_sales_credit_note_lines");

            migrationBuilder.DropTable(
                name: "client_sales_credit_note_open_items");

            migrationBuilder.DropTable(
                name: "client_sales_credit_note_submissions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_client_sales_invoice_open_items_firm_id_client_id_id",
                table: "client_sales_invoice_open_items");
        }
    }
}
