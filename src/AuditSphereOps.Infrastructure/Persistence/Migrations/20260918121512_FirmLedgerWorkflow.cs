using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmLedgerWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The original tables were placeholders and did not carry enough
            // information to infer an account type or a journal's authority,
            // currency, source revision, or preparer. Refuse that ambiguous
            // history before adding defaults; never turn an old row into a
            // plausible-looking posting.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM firm_accounts)
                     OR EXISTS (SELECT 1 FROM firm_journals)
                     OR EXISTS (SELECT 1 FROM firm_journal_lines) THEN
                    RAISE EXCEPTION 'Firm ledger migration requires an explicit legacy disposition for non-empty accounts or journal history.';
                  END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                UPDATE firm_periods
                   SET status = upper(trim(status))
                 WHERE status IS NOT NULL;
                DO $$
                BEGIN
                  IF EXISTS (
                    SELECT 1 FROM firm_periods
                     WHERE period_code !~ '^[0-9]{4}-(0[1-9]|1[0-2])$'
                        OR status NOT IN ('OPEN', 'CLOSED', 'REOPEN_REQUESTED')
                        OR (status = 'OPEN' AND closed_at IS NOT NULL)
                        OR (status IN ('CLOSED', 'REOPEN_REQUESTED') AND closed_at IS NULL)) THEN
                    RAISE EXCEPTION 'Firm ledger migration found an ambiguous fiscal-period row.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_firm_journals_firm_id_source_kind_source_key",
                table: "firm_journals");

            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "firm_periods",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "firm_journals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_user_id",
                table: "firm_journals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "firm_journals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "firm_journals",
                type: "text",
                nullable: false,
                defaultValue: "XXX");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "posted_at",
                table: "firm_journals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "posting_purpose",
                table: "firm_journals",
                type: "text",
                nullable: false,
                defaultValue: "LEGACY");

            migrationBuilder.AddColumn<long>(
                name: "source_revision",
                table: "firm_journals",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "firm_journal_lines",
                type: "text",
                nullable: false,
                defaultValue: "Legacy journal line");

            migrationBuilder.AddColumn<Guid>(
                name: "firm_id",
                table: "firm_journal_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "account_type",
                table: "firm_accounts",
                type: "text",
                nullable: false,
                defaultValue: "ASSET");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_firm_periods_firm_id_id",
                table: "firm_periods",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_firm_journals_firm_id_id",
                table: "firm_journals",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_firm_journal_lines_firm_id_id",
                table: "firm_journal_lines",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_firm_accounts_firm_id_id",
                table: "firm_accounts",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "firm_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    posted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reversal_of_posting_id = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_postings", x => x.id);
                    table.UniqueConstraint("AK_firm_postings_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_firm_posting_currency", "currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_firm_postings_firm_journals_firm_id_journal_id",
                        columns: x => new { x.firm_id, x.journal_id },
                        principalTable: "firm_journals",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_postings_firm_periods_firm_id_period_id",
                        columns: x => new { x.firm_id, x.period_id },
                        principalTable: "firm_periods",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_postings_firm_postings_firm_id_reversal_of_posting_id",
                        columns: x => new { x.firm_id, x.reversal_of_posting_id },
                        principalTable: "firm_postings",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_postings_users_firm_id_posted_by_user_id",
                        columns: x => new { x.firm_id, x.posted_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "period_close_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_kind = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_period_close_decisions", x => x.id);
                    table.UniqueConstraint("AK_period_decisions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_period_decision_values", "decision_kind IN ('CLOSE','REOPEN') AND length(reason) > 0");
                    table.ForeignKey(
                        name: "FK_period_close_decisions_firm_periods_firm_id_period_id",
                        columns: x => new { x.firm_id, x.period_id },
                        principalTable: "firm_periods",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_period_close_decisions_users_firm_id_decided_by_user_id",
                        columns: x => new { x.firm_id, x.decided_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firm_posting_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debit = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    credit = table.Column<decimal>(type: "numeric(19,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_posting_lines", x => x.id);
                    table.UniqueConstraint("AK_firm_posting_lines_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_firm_posting_line_values", "debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))");
                    table.ForeignKey(
                        name: "FK_firm_posting_lines_firm_accounts_firm_id_firm_account_id",
                        columns: x => new { x.firm_id, x.firm_account_id },
                        principalTable: "firm_accounts",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_posting_lines_firm_postings_firm_id_posting_id",
                        columns: x => new { x.firm_id, x.posting_id },
                        principalTable: "firm_postings",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_posting_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_key = table.Column<string>(type: "text", nullable: false),
                    request_digest = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_posting_receipts", x => x.id);
                    table.UniqueConstraint("AK_ledger_receipts_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_ledger_receipt_values", "length(request_key) > 0 AND request_digest ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_ledger_posting_receipts_firm_postings_firm_id_posting_id",
                        columns: x => new { x.firm_id, x.posting_id },
                        principalTable: "firm_postings",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_source_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "text", nullable: false),
                    source_key = table.Column<string>(type: "text", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    posting_purpose = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_source_links", x => x.id);
                    table.UniqueConstraint("AK_ledger_sources_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_ledger_source_values", "length(source_kind) > 0 AND length(source_key) > 0 AND source_revision >= 1 AND length(posting_purpose) > 0");
                    table.ForeignKey(
                        name: "FK_ledger_source_links_firm_postings_firm_id_posting_id",
                        columns: x => new { x.firm_id, x.posting_id },
                        principalTable: "firm_postings",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_period_values",
                table: "firm_periods",
                sql: "period_code ~ '^[0-9]{4}-(0[1-9]|1[0-2])$' AND status IN ('OPEN','CLOSED','REOPEN_REQUESTED') AND revision >= 1 AND ((status = 'OPEN' AND closed_at IS NULL) OR (status IN ('CLOSED','REOPEN_REQUESTED') AND closed_at IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_firm_journals_firm_id_approved_by_user_id",
                table: "firm_journals",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_journals_firm_id_created_by_user_id",
                table: "firm_journals",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_journals_firm_id_journal_number",
                table: "firm_journals",
                columns: new[] { "firm_id", "journal_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_journals_firm_id_period_id",
                table: "firm_journals",
                columns: new[] { "firm_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_journals_firm_id_source_kind_source_key_source_revisio~",
                table: "firm_journals",
                columns: new[] { "firm_id", "source_kind", "source_key", "source_revision", "posting_purpose" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_journal_values",
                table: "firm_journals",
                sql: "length(journal_number) > 0 AND length(source_kind) > 0 AND length(source_key) > 0 AND source_revision >= 1 AND length(posting_purpose) > 0 AND currency ~ '^[A-Z]{3}$' AND status IN ('DRAFT','REVIEW_REQUIRED','APPROVED','POSTED')");

            migrationBuilder.CreateIndex(
                name: "IX_firm_journal_lines_firm_id_firm_account_id",
                table: "firm_journal_lines",
                columns: new[] { "firm_id", "firm_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_journal_lines_firm_id_journal_id",
                table: "firm_journal_lines",
                columns: new[] { "firm_id", "journal_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_journal_line_values",
                table: "firm_journal_lines",
                sql: "length(description) > 0 AND debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_account_values",
                table: "firm_accounts",
                sql: "length(code) > 0 AND length(name) > 0 AND account_type IN ('ASSET','LIABILITY','EQUITY','REVENUE','EXPENSE') AND normal_side IN ('DEBIT','CREDIT')");

            migrationBuilder.CreateIndex(
                name: "IX_firm_posting_lines_firm_id_firm_account_id",
                table: "firm_posting_lines",
                columns: new[] { "firm_id", "firm_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_posting_lines_firm_id_posting_id",
                table: "firm_posting_lines",
                columns: new[] { "firm_id", "posting_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_postings_firm_id_journal_id",
                table: "firm_postings",
                columns: new[] { "firm_id", "journal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_postings_firm_id_period_id",
                table: "firm_postings",
                columns: new[] { "firm_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_postings_firm_id_posted_by_user_id",
                table: "firm_postings",
                columns: new[] { "firm_id", "posted_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_postings_firm_id_reversal_of_posting_id",
                table: "firm_postings",
                columns: new[] { "firm_id", "reversal_of_posting_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_posting_receipts_firm_id_posting_id",
                table: "ledger_posting_receipts",
                columns: new[] { "firm_id", "posting_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_posting_receipts_firm_id_request_key",
                table: "ledger_posting_receipts",
                columns: new[] { "firm_id", "request_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_source_links_firm_id_posting_id",
                table: "ledger_source_links",
                columns: new[] { "firm_id", "posting_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_source_links_firm_id_source_kind_source_key_source_r~",
                table: "ledger_source_links",
                columns: new[] { "firm_id", "source_kind", "source_key", "source_revision", "posting_purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_period_close_decisions_firm_id_decided_by_user_id",
                table: "period_close_decisions",
                columns: new[] { "firm_id", "decided_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_period_close_decisions_firm_id_period_id_decision_kind_deci~",
                table: "period_close_decisions",
                columns: new[] { "firm_id", "period_id", "decision_kind", "decided_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_firm_journal_lines_firm_accounts_firm_id_firm_account_id",
                table: "firm_journal_lines",
                columns: new[] { "firm_id", "firm_account_id" },
                principalTable: "firm_accounts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_firm_journal_lines_firm_journals_firm_id_journal_id",
                table: "firm_journal_lines",
                columns: new[] { "firm_id", "journal_id" },
                principalTable: "firm_journals",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_firm_journals_firm_periods_firm_id_period_id",
                table: "firm_journals",
                columns: new[] { "firm_id", "period_id" },
                principalTable: "firm_periods",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_firm_journals_users_firm_id_approved_by_user_id",
                table: "firm_journals",
                columns: new[] { "firm_id", "approved_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_firm_journals_users_firm_id_created_by_user_id",
                table: "firm_journals",
                columns: new[] { "firm_id", "created_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_posted_ledger_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_TABLE_NAME = 'firm_journals' THEN
                    IF OLD.status = 'POSTED' THEN
                      RAISE EXCEPTION 'Posted firm journals are immutable.' USING ERRCODE = '55000';
                    END IF;
                  ELSIF TG_TABLE_NAME = 'firm_journal_lines' THEN
                    IF EXISTS (SELECT 1 FROM firm_journals WHERE id = OLD.journal_id AND firm_id = OLD.firm_id AND status = 'POSTED') THEN
                      RAISE EXCEPTION 'Lines of posted firm journals are immutable.' USING ERRCODE = '55000';
                    END IF;
                  ELSE
                    RAISE EXCEPTION 'Ledger history is immutable.' USING ERRCODE = '55000';
                  END IF;
                  IF TG_OP = 'DELETE' THEN
                    RETURN OLD;
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_firm_journals_posted_immutable
                  BEFORE UPDATE OR DELETE ON firm_journals
                  FOR EACH ROW EXECUTE FUNCTION prevent_posted_ledger_mutation();
                CREATE TRIGGER trg_firm_journal_lines_posted_immutable
                  BEFORE UPDATE OR DELETE ON firm_journal_lines
                  FOR EACH ROW EXECUTE FUNCTION prevent_posted_ledger_mutation();
                CREATE TRIGGER trg_firm_postings_append_only
                  BEFORE UPDATE OR DELETE ON firm_postings
                  FOR EACH ROW EXECUTE FUNCTION prevent_posted_ledger_mutation();
                CREATE TRIGGER trg_firm_posting_lines_append_only
                  BEFORE UPDATE OR DELETE ON firm_posting_lines
                  FOR EACH ROW EXECUTE FUNCTION prevent_posted_ledger_mutation();
                CREATE TRIGGER trg_ledger_source_links_append_only
                  BEFORE UPDATE OR DELETE ON ledger_source_links
                  FOR EACH ROW EXECUTE FUNCTION prevent_posted_ledger_mutation();
                CREATE TRIGGER trg_ledger_posting_receipts_append_only
                  BEFORE UPDATE OR DELETE ON ledger_posting_receipts
                  FOR EACH ROW EXECUTE FUNCTION prevent_posted_ledger_mutation();
                CREATE TRIGGER trg_period_close_decisions_append_only
                  BEFORE UPDATE OR DELETE ON period_close_decisions
                  FOR EACH ROW EXECUTE FUNCTION prevent_posted_ledger_mutation();
                """);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION ensure_firm_posting_balanced(p_posting_id uuid)
                RETURNS void LANGUAGE plpgsql AS $$
                DECLARE
                  line_count bigint;
                  debit_total numeric;
                  credit_total numeric;
                BEGIN
                  IF EXISTS (SELECT 1 FROM firm_postings WHERE id = p_posting_id) THEN
                    SELECT count(*), COALESCE(sum(debit), 0), COALESCE(sum(credit), 0)
                      INTO line_count, debit_total, credit_total
                      FROM firm_posting_lines WHERE firm_posting_lines.posting_id = p_posting_id;
                    IF line_count < 2 OR debit_total <> credit_total THEN
                      RAISE EXCEPTION 'Firm posting must have at least two balanced lines.' USING ERRCODE = '23514';
                    END IF;
                  END IF;
                END;
                $$;
                CREATE OR REPLACE FUNCTION check_firm_posting_header_balanced()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  PERFORM ensure_firm_posting_balanced(COALESCE(NEW.id, OLD.id));
                  RETURN NULL;
                END;
                $$;
                CREATE OR REPLACE FUNCTION check_firm_posting_line_balanced()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  PERFORM ensure_firm_posting_balanced(COALESCE(NEW.posting_id, OLD.posting_id));
                  RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER trg_firm_postings_balanced
                  AFTER INSERT OR UPDATE ON firm_postings
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                  EXECUTE FUNCTION check_firm_posting_header_balanced();
                CREATE CONSTRAINT TRIGGER trg_firm_posting_lines_balanced
                  AFTER INSERT OR UPDATE OR DELETE ON firm_posting_lines
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                  EXECUTE FUNCTION check_firm_posting_line_balanced();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM firm_accounts)
                     OR EXISTS (SELECT 1 FROM firm_periods)
                     OR EXISTS (SELECT 1 FROM firm_journals)
                     OR EXISTS (SELECT 1 FROM firm_journal_lines)
                     OR EXISTS (SELECT 1 FROM firm_postings)
                     OR EXISTS (SELECT 1 FROM firm_posting_lines)
                     OR EXISTS (SELECT 1 FROM ledger_source_links)
                     OR EXISTS (SELECT 1 FROM ledger_posting_receipts)
                     OR EXISTS (SELECT 1 FROM period_close_decisions) THEN
                    RAISE EXCEPTION 'Firm ledger downgrade would discard accounting history.';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_firm_postings_balanced ON firm_postings;
                DROP TRIGGER IF EXISTS trg_firm_posting_lines_balanced ON firm_posting_lines;
                DROP TRIGGER IF EXISTS trg_firm_journals_posted_immutable ON firm_journals;
                DROP TRIGGER IF EXISTS trg_firm_journal_lines_posted_immutable ON firm_journal_lines;
                DROP TRIGGER IF EXISTS trg_firm_postings_append_only ON firm_postings;
                DROP TRIGGER IF EXISTS trg_firm_posting_lines_append_only ON firm_posting_lines;
                DROP TRIGGER IF EXISTS trg_ledger_source_links_append_only ON ledger_source_links;
                DROP TRIGGER IF EXISTS trg_ledger_posting_receipts_append_only ON ledger_posting_receipts;
                DROP TRIGGER IF EXISTS trg_period_close_decisions_append_only ON period_close_decisions;
                DROP FUNCTION IF EXISTS check_firm_posting_header_balanced();
                DROP FUNCTION IF EXISTS check_firm_posting_line_balanced();
                DROP FUNCTION IF EXISTS ensure_firm_posting_balanced(uuid);
                DROP FUNCTION IF EXISTS prevent_posted_ledger_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_firm_journal_lines_firm_accounts_firm_id_firm_account_id",
                table: "firm_journal_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_firm_journal_lines_firm_journals_firm_id_journal_id",
                table: "firm_journal_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_firm_journals_firm_periods_firm_id_period_id",
                table: "firm_journals");

            migrationBuilder.DropForeignKey(
                name: "FK_firm_journals_users_firm_id_approved_by_user_id",
                table: "firm_journals");

            migrationBuilder.DropForeignKey(
                name: "FK_firm_journals_users_firm_id_created_by_user_id",
                table: "firm_journals");

            migrationBuilder.DropTable(
                name: "firm_posting_lines");

            migrationBuilder.DropTable(
                name: "ledger_posting_receipts");

            migrationBuilder.DropTable(
                name: "ledger_source_links");

            migrationBuilder.DropTable(
                name: "period_close_decisions");

            migrationBuilder.DropTable(
                name: "firm_postings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_firm_periods_firm_id_id",
                table: "firm_periods");

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_period_values",
                table: "firm_periods");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_firm_journals_firm_id_id",
                table: "firm_journals");

            migrationBuilder.DropIndex(
                name: "IX_firm_journals_firm_id_approved_by_user_id",
                table: "firm_journals");

            migrationBuilder.DropIndex(
                name: "IX_firm_journals_firm_id_created_by_user_id",
                table: "firm_journals");

            migrationBuilder.DropIndex(
                name: "IX_firm_journals_firm_id_journal_number",
                table: "firm_journals");

            migrationBuilder.DropIndex(
                name: "IX_firm_journals_firm_id_period_id",
                table: "firm_journals");

            migrationBuilder.DropIndex(
                name: "IX_firm_journals_firm_id_source_kind_source_key_source_revisio~",
                table: "firm_journals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_journal_values",
                table: "firm_journals");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_firm_journal_lines_firm_id_id",
                table: "firm_journal_lines");

            migrationBuilder.DropIndex(
                name: "IX_firm_journal_lines_firm_id_firm_account_id",
                table: "firm_journal_lines");

            migrationBuilder.DropIndex(
                name: "IX_firm_journal_lines_firm_id_journal_id",
                table: "firm_journal_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_journal_line_values",
                table: "firm_journal_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_firm_accounts_firm_id_id",
                table: "firm_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_account_values",
                table: "firm_accounts");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "firm_periods");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "posted_at",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "posting_purpose",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "source_revision",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "description",
                table: "firm_journal_lines");

            migrationBuilder.DropColumn(
                name: "firm_id",
                table: "firm_journal_lines");

            migrationBuilder.DropColumn(
                name: "account_type",
                table: "firm_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_firm_journals_firm_id_source_kind_source_key",
                table: "firm_journals",
                columns: new[] { "firm_id", "source_kind", "source_key" },
                unique: true);
        }
    }
}
