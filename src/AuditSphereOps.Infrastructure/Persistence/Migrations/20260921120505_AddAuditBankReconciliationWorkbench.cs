using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditBankReconciliationWorkbench : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_bank_reconciliations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ledger_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ledger_balance = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    statement_balance = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    timing_item_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    proposed_correction_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    residual = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    conclusion = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_bank_reconciliations", x => x.id);
                    table.UniqueConstraint("AK_audit_bank_reconciliations_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_bank_reconciliations_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_audit_bank_reconciliation_values", "currency ~ '^[A-Z]{3}$' AND input_generation > 0 AND status IN ('RECONCILED','UNRECONCILED','APPROVED','CHANGES_REQUIRED')");
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliations_audit_procedures_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliations_audit_schedules_firm_id_client_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.ledger_schedule_id },
                        principalTable: "audit_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliations_audit_schedules_firm_id_client_~1",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.statement_schedule_id },
                        principalTable: "audit_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliations_engagements_firm_id_client_id_en~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliations_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliations_users_firm_id_reviewed_by_user_id",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_bank_reconciliation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proposed_journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stable_item_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    item_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_bank_reconciliation_items", x => x.id);
                    table.UniqueConstraint("AK_audit_bank_reconciliation_items_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_bank_reconciliation_items_reconciliation_stable", x => new { x.firm_id, x.bank_reconciliation_id, x.stable_item_id });
                    table.CheckConstraint("ck_audit_bank_reconciliation_item_values", "length(trim(stable_item_id)) > 0 AND item_type IN ('LEDGER','STATEMENT','TIMING','PROPOSED_CORRECTION')");
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliation_items_adjustment_journals_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.proposed_journal_id },
                        principalTable: "adjustment_journals",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliation_items_audit_bank_reconciliations_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.bank_reconciliation_id },
                        principalTable: "audit_bank_reconciliations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliation_items_audit_schedules_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.source_schedule_id },
                        principalTable: "audit_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_bank_reconciliation_items_engagements_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_bank_reconciliation_items_firm_id_client_id_engageme~1",
                table: "audit_bank_reconciliation_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "proposed_journal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_bank_reconciliation_items_firm_id_client_id_engageme~2",
                table: "audit_bank_reconciliation_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_bank_reconciliation_items_firm_id_client_id_engagemen~",
                table: "audit_bank_reconciliation_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "bank_reconciliation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_bank_reconciliations_firm_id_client_id_engagement_id_~",
                table: "audit_bank_reconciliations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "ledger_schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_bank_reconciliations_firm_id_client_id_engagement_id~1",
                table: "audit_bank_reconciliations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_bank_reconciliations_firm_id_client_id_engagement_id~2",
                table: "audit_bank_reconciliations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "statement_schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_bank_reconciliations_firm_id_created_by_user_id",
                table: "audit_bank_reconciliations",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_bank_reconciliations_firm_id_reviewed_by_user_id",
                table: "audit_bank_reconciliations",
                columns: new[] { "firm_id", "reviewed_by_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_bank_reconciliation_items");

            migrationBuilder.DropTable(
                name: "audit_bank_reconciliations");
        }
    }
}
