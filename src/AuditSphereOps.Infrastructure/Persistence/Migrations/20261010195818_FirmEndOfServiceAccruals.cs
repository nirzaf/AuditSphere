using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmEndOfServiceAccruals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "firm_end_of_service_treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    measurement_treatment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    accountant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    accountant_credential = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    provision_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmation_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_end_of_service_treatments", x => x.id);
                    table.UniqueConstraint("AK_firm_end_of_service_treatments_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_firm_end_of_service_treatment_values", "version >= 1 AND length(measurement_treatment) >= 20 AND length(accountant_name) > 0 AND length(accountant_credential) > 0 AND provision_account_id <> expense_account_id AND status IN ('RECORDED','CONFIRMED') AND ((status = 'CONFIRMED') = (confirmed_by_user_id IS NOT NULL AND confirmed_at IS NOT NULL AND length(confirmation_note) >= 5)) AND (confirmed_by_user_id IS NULL OR confirmed_by_user_id <> recorded_by_user_id)");
                    table.ForeignKey(
                        name: "FK_firm_end_of_service_treatments_firm_accounts_firm_id_expens~",
                        columns: x => new { x.firm_id, x.expense_account_id },
                        principalTable: "firm_accounts",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_end_of_service_treatments_firm_accounts_firm_id_provis~",
                        columns: x => new { x.firm_id, x.provision_account_id },
                        principalTable: "firm_accounts",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_end_of_service_treatments_users_firm_id_confirmed_by_u~",
                        columns: x => new { x.firm_id, x.confirmed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_end_of_service_treatments_users_firm_id_recorded_by_us~",
                        columns: x => new { x.firm_id, x.recorded_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firm_end_of_service_accruals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    treatment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", precision: 18, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    inputs = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    calculation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_end_of_service_accruals", x => x.id);
                    table.CheckConstraint("ck_firm_end_of_service_accrual_values", "amount > 0 AND length(method) >= 5 AND length(inputs) >= 5 AND length(reason) >= 5");
                    table.ForeignKey(
                        name: "FK_firm_end_of_service_accruals_firm_end_of_service_treatments~",
                        columns: x => new { x.firm_id, x.treatment_id },
                        principalTable: "firm_end_of_service_treatments",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_end_of_service_accruals_firm_journals_firm_id_journal_~",
                        columns: x => new { x.firm_id, x.journal_id },
                        principalTable: "firm_journals",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_end_of_service_accruals_firm_periods_firm_id_period_id",
                        columns: x => new { x.firm_id, x.period_id },
                        principalTable: "firm_periods",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_end_of_service_accruals_users_firm_id_created_by_user_~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_firm_end_of_service_accruals_firm_id_created_by_user_id",
                table: "firm_end_of_service_accruals",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_end_of_service_accruals_firm_id_journal_id",
                table: "firm_end_of_service_accruals",
                columns: new[] { "firm_id", "journal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_end_of_service_accruals_firm_id_period_id",
                table: "firm_end_of_service_accruals",
                columns: new[] { "firm_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_end_of_service_accruals_firm_id_treatment_id",
                table: "firm_end_of_service_accruals",
                columns: new[] { "firm_id", "treatment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_end_of_service_treatments_firm_id_confirmed_by_user_id",
                table: "firm_end_of_service_treatments",
                columns: new[] { "firm_id", "confirmed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_end_of_service_treatments_firm_id_expense_account_id",
                table: "firm_end_of_service_treatments",
                columns: new[] { "firm_id", "expense_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_end_of_service_treatments_firm_id_provision_account_id",
                table: "firm_end_of_service_treatments",
                columns: new[] { "firm_id", "provision_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_end_of_service_treatments_firm_id_recorded_by_user_id",
                table: "firm_end_of_service_treatments",
                columns: new[] { "firm_id", "recorded_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_end_of_service_treatments_firm_id_version",
                table: "firm_end_of_service_treatments",
                columns: new[] { "firm_id", "version" },
                unique: true);

            // The treatment is evidence of a human decision: its content never changes, and the only transition is
            // RECORDED -> CONFIRMED. A confirmed version is final; a change is a new version.
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_firm_end_of_service_treatment() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'End-of-service treatment history is append-only.' USING ERRCODE = '55000';
                  END IF;
                  IF NEW.id <> OLD.id OR NEW.firm_id <> OLD.firm_id OR NEW.version <> OLD.version
                     OR NEW.measurement_treatment <> OLD.measurement_treatment OR NEW.accountant_name <> OLD.accountant_name
                     OR NEW.accountant_credential <> OLD.accountant_credential OR NEW.provision_account_id <> OLD.provision_account_id
                     OR NEW.expense_account_id <> OLD.expense_account_id OR NEW.recorded_by_user_id <> OLD.recorded_by_user_id
                     OR NEW.recorded_at <> OLD.recorded_at THEN
                    RAISE EXCEPTION 'A recorded end-of-service treatment is immutable; record a new version.' USING ERRCODE = '55000';
                  END IF;
                  IF OLD.status = 'CONFIRMED' THEN
                    RAISE EXCEPTION 'A confirmed end-of-service treatment is final.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_firm_end_of_service_treatments_guard
                  BEFORE UPDATE OR DELETE ON firm_end_of_service_treatments
                  FOR EACH ROW EXECUTE FUNCTION guard_firm_end_of_service_treatment();
                """);

            // The entered basis is recorded once, against a draft accrual journal prepared under a confirmed treatment.
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_firm_end_of_service_accrual() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP <> 'INSERT' THEN
                    RAISE EXCEPTION 'End-of-service accrual basis is append-only.' USING ERRCODE = '55000';
                  END IF;
                  IF NOT EXISTS (SELECT 1 FROM firm_journals j WHERE j.id = NEW.journal_id AND j.firm_id = NEW.firm_id
                       AND j.posting_purpose = 'END_OF_SERVICE_ACCRUAL' AND j.status = 'DRAFT' AND j.period_id = NEW.period_id) THEN
                    RAISE EXCEPTION 'An accrual basis belongs to a draft end-of-service journal in the same period.' USING ERRCODE = '55000';
                  END IF;
                  IF NOT EXISTS (SELECT 1 FROM firm_end_of_service_treatments t WHERE t.id = NEW.treatment_id AND t.firm_id = NEW.firm_id
                       AND t.status = 'CONFIRMED') THEN
                    RAISE EXCEPTION 'An accrual requires a confirmed end-of-service treatment.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_firm_end_of_service_accruals_guard
                  BEFORE INSERT OR UPDATE OR DELETE ON firm_end_of_service_accruals
                  FOR EACH ROW EXECUTE FUNCTION guard_firm_end_of_service_accrual();
                """);

            // Second lock behind the Application gate: no end-of-service journal reaches the posted ledger unless its
            // basis exists under the firm's latest treatment version and that version is confirmed.
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_firm_end_of_service_posting() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM firm_journals j WHERE j.id = NEW.journal_id AND j.firm_id = NEW.firm_id
                       AND j.posting_purpose = 'END_OF_SERVICE_ACCRUAL')
                     AND NOT EXISTS (
                       SELECT 1 FROM firm_end_of_service_accruals a
                       JOIN firm_end_of_service_treatments t ON t.id = a.treatment_id AND t.firm_id = a.firm_id
                       WHERE a.journal_id = NEW.journal_id AND a.firm_id = NEW.firm_id AND t.status = 'CONFIRMED'
                         AND t.version = (SELECT max(v.version) FROM firm_end_of_service_treatments v WHERE v.firm_id = NEW.firm_id)) THEN
                    RAISE EXCEPTION 'End-of-service accruals cannot be posted without a confirmed accounting treatment and a recorded basis.'
                      USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_firm_postings_end_of_service_guard
                  BEFORE INSERT ON firm_postings
                  FOR EACH ROW EXECUTE FUNCTION guard_firm_end_of_service_posting();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_firm_postings_end_of_service_guard ON firm_postings;
                DROP FUNCTION IF EXISTS guard_firm_end_of_service_posting();
                DROP TRIGGER IF EXISTS trg_firm_end_of_service_accruals_guard ON firm_end_of_service_accruals;
                DROP FUNCTION IF EXISTS guard_firm_end_of_service_accrual();
                DROP TRIGGER IF EXISTS trg_firm_end_of_service_treatments_guard ON firm_end_of_service_treatments;
                DROP FUNCTION IF EXISTS guard_firm_end_of_service_treatment();
                """);

            migrationBuilder.DropTable(
                name: "firm_end_of_service_accruals");

            migrationBuilder.DropTable(
                name: "firm_end_of_service_treatments");
        }
    }
}
