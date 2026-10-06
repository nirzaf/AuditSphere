using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientOperationalLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_operational_journals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_operational_journals", x => x.id);
                    table.UniqueConstraint("ak_client_operational_journals_scope_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_operational_journal_values", "length(trim(journal_number)) > 0 AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND revision >= 1 AND status IN ('DRAFT','SUBMITTED','RETURNED','APPROVED','POSTED') AND ((status = 'POSTED' AND posted_by_user_id IS NOT NULL AND posted_at IS NOT NULL) OR status <> 'POSTED')");
                    table.ForeignKey(
                        name: "FK_client_operational_journals_client_reporting_periods_firm_i~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_journals_practice_clients_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_journals_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_operational_journal_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_revision = table.Column<long>(type: "bigint", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_operational_journal_decisions", x => x.id);
                    table.CheckConstraint("ck_client_operational_journal_decision_values", "journal_revision >= 1 AND decision IN ('APPROVE','RETURN') AND length(trim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_client_operational_journal_decisions_client_operational_jou~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_journal_decisions_users_firm_id_actor_us~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_operational_journal_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    client_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    debit = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    credit = table.Column<decimal>(type: "numeric(19,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_operational_journal_lines", x => x.id);
                    table.CheckConstraint("ck_client_operational_journal_line_values", "line_number > 0 AND length(trim(account_code)) > 0 AND length(trim(account_name)) > 0 AND debit >= 0 AND credit >= 0 AND NOT (debit > 0 AND credit > 0) AND (debit > 0 OR credit > 0)");
                    table.ForeignKey(
                        name: "FK_client_operational_journal_lines_client_accounts_firm_id_cl~",
                        columns: x => new { x.firm_id, x.client_id, x.client_account_id },
                        principalTable: "client_accounts",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_journal_lines_client_operational_journal~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_journal_decisions_firm_id_actor_user_id",
                table: "client_operational_journal_decisions",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_operational_journal_decision_revision",
                table: "client_operational_journal_decisions",
                columns: new[] { "firm_id", "client_id", "journal_id", "journal_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_journal_lines_firm_id_client_id_client_a~",
                table: "client_operational_journal_lines",
                columns: new[] { "firm_id", "client_id", "client_account_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_operational_journal_line_number",
                table: "client_operational_journal_lines",
                columns: new[] { "firm_id", "client_id", "journal_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_journals_firm_id_created_by_user_id",
                table: "client_operational_journals",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_operational_journal_number",
                table: "client_operational_journals",
                columns: new[] { "firm_id", "client_id", "period_id", "journal_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_operational_journal_decisions");

            migrationBuilder.DropTable(
                name: "client_operational_journal_lines");

            migrationBuilder.DropTable(
                name: "client_operational_journals");
        }
    }
}
