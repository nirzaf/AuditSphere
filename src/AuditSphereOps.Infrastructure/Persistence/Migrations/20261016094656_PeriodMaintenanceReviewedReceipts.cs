using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PeriodMaintenanceReviewedReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_client_period_restatement_scope_id",
                table: "client_period_restatements",
                columns: new[] { "firm_id", "client_id", "id" });

            migrationBuilder.CreateTable(
                name: "period_maintenance_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    restatement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    input_json = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_period_maintenance_receipts", x => x.id);
                    table.CheckConstraint("ck_period_maintenance_receipt_values", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(input_json) BETWEEN 1 AND 30000 AND length(before_json) BETWEEN 1 AND 1000000 AND length(result_json) BETWEEN 1 AND 1000000 AND ((action = 'ROLL_FORWARD' AND result_period_id IS NOT NULL AND restatement_id IS NULL) OR (action IN ('CREATE_RESTATEMENT','APPROVE_RESTATEMENT') AND result_period_id IS NULL AND restatement_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_period_maintenance_receipts_client_period_restatements_firm~",
                        columns: x => new { x.firm_id, x.client_id, x.restatement_id },
                        principalTable: "client_period_restatements",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_period_maintenance_receipts_client_reporting_periods_firm_i~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_period_maintenance_receipts_client_reporting_periods_firm_~1",
                        columns: x => new { x.firm_id, x.client_id, x.result_period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_period_maintenance_receipts_practice_clients_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_period_maintenance_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_period_maintenance_receipts_firm_id_client_id_period_id",
                table: "period_maintenance_receipts",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ux_period_maintenance_actor_request",
                table: "period_maintenance_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_period_maintenance_restatement_action",
                table: "period_maintenance_receipts",
                columns: new[] { "firm_id", "client_id", "restatement_id", "action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_period_maintenance_result_period",
                table: "period_maintenance_receipts",
                columns: new[] { "firm_id", "client_id", "result_period_id" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_period_maintenance_receipt_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Reviewed period maintenance receipts are immutable.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER period_maintenance_receipt_immutable
                BEFORE UPDATE OR DELETE ON period_maintenance_receipts
                FOR EACH ROW EXECUTE FUNCTION reject_period_maintenance_receipt_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Serialize with receipt insertion; downgrade may never discard retained reviewed evidence.
            migrationBuilder.Sql("""
                LOCK TABLE period_maintenance_receipts IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM period_maintenance_receipts) THEN
                    RAISE EXCEPTION 'Cannot remove retained reviewed period maintenance receipts.' USING ERRCODE = '55000';
                  END IF;
                END;
                $$;
                DROP TRIGGER period_maintenance_receipt_immutable ON period_maintenance_receipts;
                DROP FUNCTION reject_period_maintenance_receipt_mutation();
                """);
            migrationBuilder.DropTable(
                name: "period_maintenance_receipts");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_client_period_restatement_scope_id",
                table: "client_period_restatements");
        }
    }
}
