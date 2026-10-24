using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmationBookedSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_confirmation_booked_sources",
                columns: table => new
                {
                    confirmation_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    bank_reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    snapshot_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_confirmation_booked_sources", x => x.confirmation_case_id);
                    table.CheckConstraint("ck_confirmation_booked_source", "source_kind = 'BANK_RECONCILIATION'\nAND ledger_schedule_id <> statement_schedule_id\nAND snapshot_sha256 ~ '^[0-9a-f]{64}$'\nAND octet_length(snapshot_json) BETWEEN 2 AND 16777216\nAND snapshot_sha256 = encode(sha256(convert_to(snapshot_json, 'UTF8')), 'hex')\nAND jsonb_typeof(snapshot_json::jsonb) = 'object'\nAND COALESCE(snapshot_json::jsonb->>'Version' = 'bank-confirmation-booked-source.v1', false)\nAND COALESCE(snapshot_json::jsonb->'Bank' @> jsonb_build_object(\n  'Id', bank_reconciliation_id, 'FirmId', firm_id, 'ClientId', client_id,\n  'EngagementId', engagement_id, 'LedgerScheduleId', ledger_schedule_id,\n  'StatementScheduleId', statement_schedule_id), false)");
                    table.ForeignKey(
                        name: "FK_audit_confirmation_booked_sources_audit_bank_reconciliation~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.bank_reconciliation_id },
                        principalTable: "audit_bank_reconciliations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_booked_sources_audit_confirmation_cases_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.confirmation_case_id },
                        principalTable: "audit_confirmation_cases",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_booked_sources_audit_schedules_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.ledger_schedule_id },
                        principalTable: "audit_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_booked_sources_audit_schedules_firm_id_~1",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.statement_schedule_id },
                        principalTable: "audit_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_booked_sources_users_firm_id_created_by_~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_booked_sources_firm_id_client_id_engage~1",
                table: "audit_confirmation_booked_sources",
                columns: new[] { "firm_id", "client_id", "engagement_id", "confirmation_case_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_booked_sources_firm_id_client_id_engage~2",
                table: "audit_confirmation_booked_sources",
                columns: new[] { "firm_id", "client_id", "engagement_id", "ledger_schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_booked_sources_firm_id_client_id_engage~3",
                table: "audit_confirmation_booked_sources",
                columns: new[] { "firm_id", "client_id", "engagement_id", "statement_schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_booked_sources_firm_id_client_id_engagem~",
                table: "audit_confirmation_booked_sources",
                columns: new[] { "firm_id", "client_id", "engagement_id", "bank_reconciliation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_booked_sources_firm_id_created_by_user_id",
                table: "audit_confirmation_booked_sources",
                columns: new[] { "firm_id", "created_by_user_id" });
            migrationBuilder.Sql(ConfirmationBookedSourceSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ConfirmationBookedSourceSql.Down);
            migrationBuilder.DropTable(
                name: "audit_confirmation_booked_sources");
        }
    }
}
