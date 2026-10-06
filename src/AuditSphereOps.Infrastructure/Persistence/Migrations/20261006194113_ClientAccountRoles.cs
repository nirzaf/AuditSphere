using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientAccountRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_account_role_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chart_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    proposed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_account_role_configurations", x => x.id);
                    table.UniqueConstraint("AK_client_account_role_configurations_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_account_role_config", "role IN ('AR','AP','TAX_RECOVERABLE','TAX_PAYABLE','REVENUE','PURCHASE_EXPENSE','PURCHASE_ASSET','RETAINED_EARNINGS','ROUNDING','FX') AND length(trim(reason))>0 AND (effective_to IS NULL OR effective_to>=effective_from)");
                    table.ForeignKey(
                        name: "FK_client_account_role_configurations_client_accounts_firm_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.account_id },
                        principalTable: "client_accounts",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_account_role_configurations_client_chart_versions_fi~",
                        columns: x => new { x.firm_id, x.client_id, x.chart_version_id },
                        principalTable: "client_chart_versions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_account_role_configurations_users_firm_id_proposed_b~",
                        columns: x => new { x.firm_id, x.proposed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_account_role_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    configuration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_account_role_decisions", x => x.id);
                    table.CheckConstraint("ck_client_account_role_decision", "decision IN ('APPROVE','REJECT') AND length(trim(reason))>0");
                    table.ForeignKey(
                        name: "FK_client_account_role_decisions_client_account_role_configura~",
                        columns: x => new { x.firm_id, x.client_id, x.configuration_id },
                        principalTable: "client_account_role_configurations",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_account_role_decisions_users_firm_id_reviewed_by_use~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_account_role_configurations_firm_id_client_id_accoun~",
                table: "client_account_role_configurations",
                columns: new[] { "firm_id", "client_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_account_role_configurations_firm_id_client_id_chart_~",
                table: "client_account_role_configurations",
                columns: new[] { "firm_id", "client_id", "chart_version_id", "role", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "IX_client_account_role_configurations_firm_id_proposed_by_user~",
                table: "client_account_role_configurations",
                columns: new[] { "firm_id", "proposed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_account_role_decisions_firm_id_client_id_configurati~",
                table: "client_account_role_decisions",
                columns: new[] { "firm_id", "client_id", "configuration_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_account_role_decisions_firm_id_reviewed_by_user_id",
                table: "client_account_role_decisions",
                columns: new[] { "firm_id", "reviewed_by_user_id" });
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_client_account_role_history() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN RAISE EXCEPTION 'Client account roles and review decisions retain immutable history.' USING ERRCODE='23514'; END; $fn$;
                CREATE TRIGGER immutable_client_account_role_config BEFORE UPDATE OR DELETE ON client_account_role_configurations FOR EACH ROW EXECUTE FUNCTION guard_client_account_role_history();
                CREATE TRIGGER immutable_client_account_role_decision BEFORE UPDATE OR DELETE ON client_account_role_decisions FOR EACH ROW EXECUTE FUNCTION guard_client_account_role_history();
                CREATE FUNCTION guard_client_account_role_review() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE proposal client_account_role_configurations%ROWTYPE; account client_accounts%ROWTYPE;
                BEGIN
                  PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
                  SELECT * INTO proposal FROM client_account_role_configurations WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.configuration_id;
                  IF proposal.id IS NULL OR proposal.proposed_by_user_id=NEW.reviewed_by_user_id THEN
                    RAISE EXCEPTION 'Independent account-role review required.' USING ERRCODE='23514';
                  END IF;
                  IF NEW.decision='APPROVE' THEN
                    SELECT * INTO account FROM client_accounts WHERE firm_id=proposal.firm_id AND client_id=proposal.client_id AND id=proposal.account_id AND chart_version_id=proposal.chart_version_id;
                    IF account.id IS NULL OR NOT account.is_posting OR account.status<>'ACTIVE'
                       OR NOT ((proposal.role IN ('AR','TAX_RECOVERABLE','PURCHASE_ASSET') AND account.account_type='ASSET')
                         OR (proposal.role IN ('AP','TAX_PAYABLE') AND account.account_type='LIABILITY')
                         OR (proposal.role='REVENUE' AND account.account_type='INCOME')
                         OR (proposal.role='PURCHASE_EXPENSE' AND account.account_type='EXPENSE')
                         OR (proposal.role='RETAINED_EARNINGS' AND account.account_type='EQUITY')
                         OR (proposal.role IN ('ROUNDING','FX') AND account.account_type IN ('INCOME','EXPENSE')))
                       OR NOT EXISTS (SELECT 1 FROM client_chart_versions c WHERE c.firm_id=proposal.firm_id AND c.client_id=proposal.client_id AND c.id=proposal.chart_version_id AND c.status='APPROVED' AND c.effective_from<=proposal.effective_from AND (c.effective_to IS NULL OR proposal.effective_to<=c.effective_to)) THEN
                      RAISE EXCEPTION 'Role account/chart incompatible.' USING ERRCODE='23514';
                    END IF;
                    IF EXISTS (SELECT 1 FROM client_account_role_configurations c JOIN client_account_role_decisions d ON d.configuration_id=c.id AND d.firm_id=c.firm_id AND d.client_id=c.client_id
                      WHERE c.firm_id=proposal.firm_id AND c.client_id=proposal.client_id AND c.chart_version_id=proposal.chart_version_id AND d.decision='APPROVE'
                        AND (c.role=proposal.role OR (c.account_id=proposal.account_id AND (c.role IN ('AR','AP') OR proposal.role IN ('AR','AP'))))
                        AND (proposal.effective_to IS NULL OR c.effective_from<=proposal.effective_to) AND (c.effective_to IS NULL OR c.effective_to>=proposal.effective_from)) THEN
                      RAISE EXCEPTION 'Approved role intervals overlap.' USING ERRCODE='23514';
                    END IF;
                    IF proposal.role IN ('AR','AP') AND EXISTS (SELECT 1 FROM client_operational_journal_lines l JOIN client_operational_journals j ON j.id=l.journal_id AND j.firm_id=l.firm_id AND j.client_id=l.client_id
                       WHERE l.firm_id=proposal.firm_id AND l.client_id=proposal.client_id AND l.client_account_id=proposal.account_id AND j.status='POSTED' AND j.posting_date>=proposal.effective_from AND (proposal.effective_to IS NULL OR j.posting_date<=proposal.effective_to)) THEN
                      RAISE EXCEPTION 'Control role cannot reclassify posted generic activity.' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END; $fn$;
                CREATE TRIGGER scoped_client_account_role_review BEFORE INSERT ON client_account_role_decisions FOR EACH ROW EXECUTE FUNCTION guard_client_account_role_review();
                CREATE FUNCTION guard_client_generic_control_post() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  IF NEW.status='POSTED' THEN
                    PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
                    IF EXISTS (SELECT 1 FROM client_operational_journal_lines l JOIN client_account_role_configurations c ON c.account_id=l.client_account_id AND c.firm_id=l.firm_id AND c.client_id=l.client_id
                       JOIN client_account_role_decisions d ON d.configuration_id=c.id AND d.firm_id=c.firm_id AND d.client_id=c.client_id
                       WHERE l.firm_id=NEW.firm_id AND l.client_id=NEW.client_id AND l.journal_id=NEW.id AND d.decision='APPROVE' AND c.role IN ('AR','AP') AND c.effective_from<=NEW.posting_date AND (c.effective_to IS NULL OR c.effective_to>=NEW.posting_date)) THEN
                      RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END; $fn$;
                CREATE TRIGGER client_generic_control_post BEFORE INSERT OR UPDATE ON client_operational_journals FOR EACH ROW EXECUTE FUNCTION guard_client_generic_control_post();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER client_generic_control_post ON client_operational_journals;
                DROP FUNCTION guard_client_generic_control_post();
                DROP TRIGGER scoped_client_account_role_review ON client_account_role_decisions;
                DROP FUNCTION guard_client_account_role_review();
                DROP TRIGGER immutable_client_account_role_decision ON client_account_role_decisions;
                DROP TRIGGER immutable_client_account_role_config ON client_account_role_configurations;
                DROP FUNCTION guard_client_account_role_history();
                """);
            migrationBuilder.DropTable(
                name: "client_account_role_decisions");

            migrationBuilder.DropTable(
                name: "client_account_role_configurations");
        }
    }
}
