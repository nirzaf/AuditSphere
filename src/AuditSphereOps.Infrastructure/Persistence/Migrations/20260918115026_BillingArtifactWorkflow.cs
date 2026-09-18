using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BillingArtifactWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "receipts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recorded_by_user_id",
                table: "receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "receipts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_user_id",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "posted_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "invoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sent_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "firm_id",
                table: "invoice_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "source_id",
                table: "invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_kind",
                table: "invoice_lines",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "source_revision",
                table: "invoice_lines",
                type: "bigint",
                nullable: true);

            // Preserve placeholder billing rows before tightening scope, currency and status invariants.
            migrationBuilder.Sql("""
                UPDATE billing_accounts SET currency = upper(trim(currency));
                UPDATE invoices SET
                    status = CASE lower(trim(status))
                        WHEN 'draft' THEN 'DRAFT'
                        WHEN 'issued' THEN 'POSTED'
                        WHEN 'paid' THEN 'SENT'
                        WHEN 'credited' THEN 'SENT'
                        ELSE upper(trim(status)) END,
                    revision = CASE WHEN revision < 1 THEN 1 ELSE revision END;
                UPDATE invoices i SET currency = upper(trim(a.currency))
                  FROM billing_accounts a
                 WHERE a.firm_id = i.firm_id AND a.id = i.billing_account_id AND i.currency IS NULL;
                UPDATE invoice_lines l SET firm_id = i.firm_id
                  FROM invoices i
                 WHERE i.id = l.invoice_id AND l.firm_id = '00000000-0000-0000-0000-000000000000';
                UPDATE receipts r SET status = 'RECORDED', currency = upper(trim(a.currency))
                  FROM billing_accounts a
                 WHERE a.firm_id = r.firm_id AND a.id = r.billing_account_id;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM billing_accounts WHERE currency !~ '^[A-Z]{3}$') THEN
                    RAISE EXCEPTION 'billing migration stopped: invalid billing account currency';
                  END IF;
                  IF EXISTS (SELECT 1 FROM invoices i LEFT JOIN billing_accounts a
                    ON a.firm_id = i.firm_id AND a.id = i.billing_account_id
                    WHERE a.id IS NULL OR i.currency IS NULL OR i.currency <> a.currency
                       OR i.status NOT IN ('DRAFT','REVIEW_REQUIRED','APPROVED','POSTED','SENT','CANCELLED')
                       OR i.revision < 1 OR i.total <> i.subtotal + i.tax) THEN
                    RAISE EXCEPTION 'billing migration stopped: invoice scope, currency, state or total is ambiguous';
                  END IF;
                  IF EXISTS (SELECT 1 FROM invoice_lines l LEFT JOIN invoices i
                    ON i.firm_id = l.firm_id AND i.id = l.invoice_id
                    WHERE i.id IS NULL OR l.description = '' OR l.quantity <= 0 OR l.unit_price < 0
                       OR l.line_total <> round(l.quantity * l.unit_price, 6)) THEN
                    RAISE EXCEPTION 'billing migration stopped: invoice line is ambiguous';
                  END IF;
                  IF EXISTS (SELECT 1 FROM receipts r LEFT JOIN billing_accounts a
                    ON a.firm_id = r.firm_id AND a.id = r.billing_account_id
                    WHERE a.id IS NULL OR r.status <> 'RECORDED' OR r.currency IS NULL
                       OR r.currency <> a.currency OR r.amount <= 0 OR length(r.reference) = 0) THEN
                    RAISE EXCEPTION 'billing migration stopped: receipt scope or currency is ambiguous';
                  END IF;
                  IF EXISTS (SELECT 1 FROM allocations a
                    LEFT JOIN receipts r ON r.firm_id = a.firm_id AND r.id = a.receipt_id
                    LEFT JOIN invoices i ON i.firm_id = a.firm_id AND i.id = a.invoice_id
                    WHERE r.id IS NULL OR i.id IS NULL OR a.amount <= 0) THEN
                    RAISE EXCEPTION 'billing migration stopped: receipt allocation is ambiguous';
                  END IF;
                END $$;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_receipts_firm_id_id",
                table: "receipts",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_invoices_firm_id_id",
                table: "invoices",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_invoice_lines_firm_id_id",
                table: "invoice_lines",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_billing_accounts_firm_id_id",
                table: "billing_accounts",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_receipt_allocations_firm_id_id",
                table: "allocations",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "billing_source_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_source_allocations", x => x.id);
                    table.UniqueConstraint("AK_billing_sources_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_billing_source_values", "length(source_kind) > 0 AND source_revision >= 1 AND quantity > 0 AND amount > 0");
                    table.ForeignKey(
                        name: "FK_billing_source_allocations_invoice_lines_firm_id_invoice_li~",
                        columns: x => new { x.firm_id, x.invoice_line_id },
                        principalTable: "invoice_lines",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "credit_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note_number = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_notes", x => x.id);
                    table.UniqueConstraint("AK_credit_notes_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_credit_note_values", "length(note_number) > 0 AND currency ~ '^[A-Z]{3}$' AND amount > 0 AND length(reason) > 0 AND status = 'ISSUED'");
                    table.ForeignKey(
                        name: "FK_credit_notes_billing_accounts_firm_id_billing_account_id",
                        columns: x => new { x.firm_id, x.billing_account_id },
                        principalTable: "billing_accounts",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_notes_invoices_firm_id_invoice_id",
                        columns: x => new { x.firm_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_notes_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firm_finance_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    functional_currency = table.Column<string>(type: "text", nullable: false),
                    profile_kind = table.Column<string>(type: "text", nullable: false),
                    approved = table.Column<bool>(type: "boolean", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_finance_profiles", x => x.id);
                    table.UniqueConstraint("AK_finance_profiles_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_finance_profile_approval", "(approved = false AND approved_at IS NULL AND approved_by_user_id IS NULL) OR (approved = true AND approved_at IS NOT NULL AND approved_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_finance_profile_currency", "functional_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_finance_profile_kind", "profile_kind IN ('TEST','PRODUCTION')");
                    table.ForeignKey(
                        name: "FK_firm_finance_profiles_users_firm_id_approved_by_user_id",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipts_firm_id_billing_account_id_received_at",
                table: "receipts",
                columns: new[] { "firm_id", "billing_account_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_receipts_firm_id_recorded_by_user_id",
                table: "receipts",
                columns: new[] { "firm_id", "recorded_by_user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipt_values",
                table: "receipts",
                sql: "amount > 0 AND (currency IS NULL OR currency ~ '^[A-Z]{3}$') AND length(reference) > 0 AND status = 'RECORDED'");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_firm_id_approved_by_user_id",
                table: "invoices",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_firm_id_billing_account_id",
                table: "invoices",
                columns: new[] { "firm_id", "billing_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_firm_id_created_by_user_id",
                table: "invoices",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_cancel",
                table: "invoices",
                sql: "(status = 'CANCELLED' AND cancelled_at IS NOT NULL AND length(cancellation_reason) > 0) OR status <> 'CANCELLED'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_state",
                table: "invoices",
                sql: "status IN ('DRAFT','REVIEW_REQUIRED','APPROVED','POSTED','SENT','CANCELLED') AND revision >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_values",
                table: "invoices",
                sql: "length(invoice_number) > 0 AND (currency IS NULL OR currency ~ '^[A-Z]{3}$') AND subtotal >= 0 AND tax >= 0 AND total >= 0 AND total = subtotal + tax");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_firm_id_invoice_id",
                table: "invoice_lines",
                columns: new[] { "firm_id", "invoice_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_line_values",
                table: "invoice_lines",
                sql: "length(description) > 0 AND quantity > 0 AND unit_price >= 0 AND line_total >= 0 AND line_total = round(quantity * unit_price, 6) AND ((length(source_kind) = 0 AND source_id IS NULL AND source_revision IS NULL) OR (length(source_kind) > 0 AND source_id IS NOT NULL AND source_revision >= 1))");

            migrationBuilder.CreateIndex(
                name: "IX_billing_accounts_firm_id_practice_client_id",
                table: "billing_accounts",
                columns: new[] { "firm_id", "practice_client_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_billing_account_currency",
                table: "billing_accounts",
                sql: "currency ~ '^[A-Z]{3}$'");

            migrationBuilder.CreateIndex(
                name: "IX_allocations_firm_id_invoice_id",
                table: "allocations",
                columns: new[] { "firm_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "IX_allocations_firm_id_receipt_id_invoice_id",
                table: "allocations",
                columns: new[] { "firm_id", "receipt_id", "invoice_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipt_allocation_amount",
                table: "allocations",
                sql: "amount > 0");

            migrationBuilder.CreateIndex(
                name: "IX_billing_source_allocations_firm_id_invoice_line_id",
                table: "billing_source_allocations",
                columns: new[] { "firm_id", "invoice_line_id" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_source_allocations_firm_id_source_kind_source_id_so~",
                table: "billing_source_allocations",
                columns: new[] { "firm_id", "source_kind", "source_id", "source_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_firm_id_billing_account_id",
                table: "credit_notes",
                columns: new[] { "firm_id", "billing_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_firm_id_created_by_user_id",
                table: "credit_notes",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_firm_id_invoice_id",
                table: "credit_notes",
                columns: new[] { "firm_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_firm_id_note_number",
                table: "credit_notes",
                columns: new[] { "firm_id", "note_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_finance_profiles_firm_id",
                table: "firm_finance_profiles",
                column: "firm_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_finance_profiles_firm_id_approved_by_user_id",
                table: "firm_finance_profiles",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_allocations_invoices_firm_id_invoice_id",
                table: "allocations",
                columns: new[] { "firm_id", "invoice_id" },
                principalTable: "invoices",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_allocations_receipts_firm_id_receipt_id",
                table: "allocations",
                columns: new[] { "firm_id", "receipt_id" },
                principalTable: "receipts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_billing_accounts_practice_clients_firm_id_practice_client_id",
                table: "billing_accounts",
                columns: new[] { "firm_id", "practice_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invoice_lines_invoices_firm_id_invoice_id",
                table: "invoice_lines",
                columns: new[] { "firm_id", "invoice_id" },
                principalTable: "invoices",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invoices_billing_accounts_firm_id_billing_account_id",
                table: "invoices",
                columns: new[] { "firm_id", "billing_account_id" },
                principalTable: "billing_accounts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invoices_users_firm_id_approved_by_user_id",
                table: "invoices",
                columns: new[] { "firm_id", "approved_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invoices_users_firm_id_created_by_user_id",
                table: "invoices",
                columns: new[] { "firm_id", "created_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_receipts_billing_accounts_firm_id_billing_account_id",
                table: "receipts",
                columns: new[] { "firm_id", "billing_account_id" },
                principalTable: "billing_accounts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_receipts_users_firm_id_recorded_by_user_id",
                table: "receipts",
                columns: new[] { "firm_id", "recorded_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_billing_history_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_TABLE_NAME = 'invoices' THEN
                    IF TG_OP = 'DELETE' THEN
                      IF OLD.status IN ('POSTED','SENT','CANCELLED') THEN
                        RAISE EXCEPTION 'posted or cancelled invoice history is immutable';
                      END IF;
                      RETURN OLD;
                    END IF;
                    IF OLD.status = 'SENT' OR OLD.status = 'CANCELLED' OR
                       (OLD.status = 'POSTED' AND NEW.status <> 'SENT') THEN
                      RAISE EXCEPTION 'posted invoice history is immutable';
                    END IF;
                  ELSIF TG_TABLE_NAME = 'invoice_lines' THEN
                    IF EXISTS (SELECT 1 FROM invoices i WHERE i.firm_id = OLD.firm_id
                      AND i.id = OLD.invoice_id AND i.status IN ('POSTED','SENT','CANCELLED')) THEN
                      RAISE EXCEPTION 'posted invoice lines are immutable';
                    END IF;
                  ELSE
                    RAISE EXCEPTION 'billing history is append-only';
                  END IF;
                  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_invoices_immutable_posted
                  BEFORE UPDATE OR DELETE ON invoices
                  FOR EACH ROW EXECUTE FUNCTION prevent_billing_history_mutation();
                CREATE TRIGGER trg_invoice_lines_immutable_posted
                  BEFORE UPDATE OR DELETE ON invoice_lines
                  FOR EACH ROW EXECUTE FUNCTION prevent_billing_history_mutation();
                CREATE TRIGGER trg_receipts_append_only
                  BEFORE UPDATE OR DELETE ON receipts
                  FOR EACH ROW EXECUTE FUNCTION prevent_billing_history_mutation();
                CREATE TRIGGER trg_allocations_append_only
                  BEFORE UPDATE OR DELETE ON allocations
                  FOR EACH ROW EXECUTE FUNCTION prevent_billing_history_mutation();
                CREATE TRIGGER trg_credit_notes_append_only
                  BEFORE UPDATE OR DELETE ON credit_notes
                  FOR EACH ROW EXECUTE FUNCTION prevent_billing_history_mutation();
                CREATE TRIGGER trg_billing_source_allocations_append_only
                  BEFORE UPDATE OR DELETE ON billing_source_allocations
                  FOR EACH ROW EXECUTE FUNCTION prevent_billing_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_invoices_immutable_posted ON invoices;
                DROP TRIGGER IF EXISTS trg_invoice_lines_immutable_posted ON invoice_lines;
                DROP TRIGGER IF EXISTS trg_receipts_append_only ON receipts;
                DROP TRIGGER IF EXISTS trg_allocations_append_only ON allocations;
                DROP TRIGGER IF EXISTS trg_credit_notes_append_only ON credit_notes;
                DROP TRIGGER IF EXISTS trg_billing_source_allocations_append_only ON billing_source_allocations;
                DROP FUNCTION IF EXISTS prevent_billing_history_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_allocations_invoices_firm_id_invoice_id",
                table: "allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_allocations_receipts_firm_id_receipt_id",
                table: "allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_billing_accounts_practice_clients_firm_id_practice_client_id",
                table: "billing_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_invoice_lines_invoices_firm_id_invoice_id",
                table: "invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_invoices_billing_accounts_firm_id_billing_account_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_invoices_users_firm_id_approved_by_user_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_invoices_users_firm_id_created_by_user_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_receipts_billing_accounts_firm_id_billing_account_id",
                table: "receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_receipts_users_firm_id_recorded_by_user_id",
                table: "receipts");

            migrationBuilder.DropTable(
                name: "billing_source_allocations");

            migrationBuilder.DropTable(
                name: "credit_notes");

            migrationBuilder.DropTable(
                name: "firm_finance_profiles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_receipts_firm_id_id",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "IX_receipts_firm_id_billing_account_id_received_at",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "IX_receipts_firm_id_recorded_by_user_id",
                table: "receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipt_values",
                table: "receipts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_invoices_firm_id_id",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_firm_id_approved_by_user_id",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_firm_id_billing_account_id",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_firm_id_created_by_user_id",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_cancel",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_state",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_values",
                table: "invoices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_invoice_lines_firm_id_id",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "IX_invoice_lines_firm_id_invoice_id",
                table: "invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_line_values",
                table: "invoice_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_billing_accounts_firm_id_id",
                table: "billing_accounts");

            migrationBuilder.DropIndex(
                name: "IX_billing_accounts_firm_id_practice_client_id",
                table: "billing_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_billing_account_currency",
                table: "billing_accounts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_receipt_allocations_firm_id_id",
                table: "allocations");

            migrationBuilder.DropIndex(
                name: "IX_allocations_firm_id_invoice_id",
                table: "allocations");

            migrationBuilder.DropIndex(
                name: "IX_allocations_firm_id_receipt_id_invoice_id",
                table: "allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipt_allocation_amount",
                table: "allocations");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "recorded_by_user_id",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "status",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "posted_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "sent_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "firm_id",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "source_kind",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "source_revision",
                table: "invoice_lines");
        }
    }
}
