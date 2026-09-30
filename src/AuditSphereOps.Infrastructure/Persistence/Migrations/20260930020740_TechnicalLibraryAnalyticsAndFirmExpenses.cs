using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TechnicalLibraryAnalyticsAndFirmExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "firm_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    payee = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_file_name = table.Column<string>(type: "text", nullable: false),
                    evidence_content_type = table.Column<string>(type: "text", nullable: false),
                    evidence_content = table.Column<byte[]>(type: "bytea", nullable: false),
                    evidence_sha256 = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    prepared_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_comment = table.Column<string>(type: "text", nullable: true),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    posting_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_expenses", x => x.id);
                    table.CheckConstraint("ck_firm_expense_values", "category IN ('RENT','SALARIES','PETTY_CASH','UTILITIES','OTHER') AND status IN ('DRAFT','SUBMITTED','APPROVED','POSTED','REJECTED') AND amount > 0 AND currency ~ '^[A-Z]{3}$' AND length(evidence_sha256) = 64 AND octet_length(evidence_content) BETWEEN 1 AND 5242880 AND (reviewed_by_user_id IS NULL OR reviewed_by_user_id <> prepared_by_user_id) AND ((status = 'POSTED') = (posting_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_firm_expenses_firm_accounts_expense_account_id",
                        column: x => x.expense_account_id,
                        principalTable: "firm_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_expenses_firm_accounts_payment_account_id",
                        column: x => x.payment_account_id,
                        principalTable: "firm_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_cost_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hourly_cost = table.Column<decimal>(type: "numeric(19,6)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_cost_rates", x => x.id);
                    table.CheckConstraint("ck_staff_cost_rate_values", "hourly_cost > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_staff_cost_rates_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "technical_library_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    audience = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technical_library_documents", x => x.id);
                    table.CheckConstraint("ck_technical_library_document_values", "category IN ('IFRS','ISA','FIRM_GUIDANCE') AND audience IN ('ALL_STAFF','PARTNERS_MANAGERS') AND length(code) BETWEEN 1 AND 40 AND length(title) > 0");
                });

            migrationBuilder.CreateTable(
                name: "technical_library_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    source_reference = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    content_sha256 = table.Column<string>(type: "text", nullable: false),
                    prepared_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technical_library_versions", x => x.id);
                    table.CheckConstraint("ck_technical_library_version_values", "version >= 1 AND status IN ('DRAFT','PUBLISHED','SUPERSEDED') AND length(content_sha256) = 64 AND length(source_reference) > 0 AND ((status = 'DRAFT') = (approved_by_user_id IS NULL)) AND (approved_by_user_id IS NULL OR approved_by_user_id <> prepared_by_user_id)");
                    table.ForeignKey(
                        name: "FK_technical_library_versions_technical_library_documents_docu~",
                        column: x => x.document_id,
                        principalTable: "technical_library_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_firm_expenses_expense_account_id",
                table: "firm_expenses",
                column: "expense_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_firm_expenses_firm_id_expense_date",
                table: "firm_expenses",
                columns: new[] { "firm_id", "expense_date" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_expenses_payment_account_id",
                table: "firm_expenses",
                column: "payment_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_cost_rates_firm_id_user_id_effective_from",
                table: "staff_cost_rates",
                columns: new[] { "firm_id", "user_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_cost_rates_user_id",
                table: "staff_cost_rates",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_technical_library_documents_firm_id_code",
                table: "technical_library_documents",
                columns: new[] { "firm_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_technical_library_versions_document_id_version",
                table: "technical_library_versions",
                columns: new[] { "document_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_technical_library_versions_single_draft",
                table: "technical_library_versions",
                column: "document_id",
                unique: true,
                filter: "status = 'DRAFT'");
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_published_library_versions() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' AND OLD.status <> 'DRAFT' THEN RAISE EXCEPTION 'published library versions are retained'; END IF;
                  IF TG_OP = 'UPDATE' AND OLD.status <> 'DRAFT' AND (NEW.body IS DISTINCT FROM OLD.body OR NEW.content_sha256 IS DISTINCT FROM OLD.content_sha256
                     OR NEW.source_reference IS DISTINCT FROM OLD.source_reference OR (OLD.status = 'SUPERSEDED' AND NEW.status <> 'SUPERSEDED')) THEN
                    RAISE EXCEPTION 'a published library version is immutable';
                  END IF;
                  RETURN COALESCE(NEW, OLD);
                END;
                $$;
                CREATE TRIGGER trg_technical_library_versions_protect BEFORE UPDATE OR DELETE ON technical_library_versions FOR EACH ROW EXECUTE FUNCTION protect_published_library_versions();
                CREATE FUNCTION prevent_cost_rate_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'staff cost rates are append-only';
                END;
                $$;
                CREATE TRIGGER trg_staff_cost_rates_append_only BEFORE UPDATE OR DELETE ON staff_cost_rates FOR EACH ROW EXECUTE FUNCTION prevent_cost_rate_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_staff_cost_rates_append_only ON staff_cost_rates;
                DROP FUNCTION IF EXISTS prevent_cost_rate_mutation();
                DROP TRIGGER IF EXISTS trg_technical_library_versions_protect ON technical_library_versions;
                DROP FUNCTION IF EXISTS protect_published_library_versions();
                """);

            migrationBuilder.DropTable(
                name: "firm_expenses");

            migrationBuilder.DropTable(
                name: "staff_cost_rates");

            migrationBuilder.DropTable(
                name: "technical_library_versions");

            migrationBuilder.DropTable(
                name: "technical_library_documents");
        }
    }
}
