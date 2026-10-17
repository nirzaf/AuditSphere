using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditProcedureCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_procedure_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_procedure_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_revision = table.Column<int>(type: "integer", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    expected_result_revision = table.Column<long>(type: "bigint", nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workpaper_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    result_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_procedure_command_receipts", x => x.id);
                    table.CheckConstraint("ck_audit_procedure_command_receipt_values", "action IN ('SUBMIT_RESULT','REVIEW_RESULT') AND actor_epoch > 0 AND definition_revision >= 0 AND input_generation > 0 AND expected_result_revision >= 0 AND result_revision > 0 AND length(request_hash) = 64 AND result_id IS NOT NULL AND ((action = 'SUBMIT_RESULT' AND decision IS NULL AND review_id IS NULL) OR (action = 'REVIEW_RESULT' AND review_id IS NOT NULL AND decision IN ('REVIEWED','CHANGES_REQUIRED')))");
                    table.ForeignKey(
                        name: "FK_audit_procedure_command_receipts_audit_procedures_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_command_receipts_engagements_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_command_receipts_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_procedure_command_procedure_committed",
                table: "audit_procedure_command_receipts",
                columns: new[] { "firm_id", "audit_procedure_id", "committed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_command_receipts_firm_id_client_id_engageme~",
                table: "audit_procedure_command_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_procedure_command_request",
                table: "audit_procedure_command_receipts",
                columns: new[] { "firm_id", "actor_user_id", "request_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_audit_procedure_command_receipt_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Audit procedure command receipts are append-only.';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_audit_procedure_command_receipt_immutable
                  BEFORE UPDATE OR DELETE ON audit_procedure_command_receipts
                  FOR EACH ROW EXECUTE FUNCTION prevent_audit_procedure_command_receipt_mutation();
                CREATE TRIGGER trg_audit_procedure_command_receipt_no_truncate
                  BEFORE TRUNCATE ON audit_procedure_command_receipts
                  FOR EACH STATEMENT EXECUTE FUNCTION prevent_audit_procedure_command_receipt_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM audit_procedure_command_receipts) THEN
                    RAISE EXCEPTION 'Refusing to remove retained audit procedure command receipts.';
                  END IF;
                END;
                $$;
                DROP TRIGGER IF EXISTS trg_audit_procedure_command_receipt_no_truncate ON audit_procedure_command_receipts;
                DROP TRIGGER IF EXISTS trg_audit_procedure_command_receipt_immutable ON audit_procedure_command_receipts;
                DROP FUNCTION IF EXISTS prevent_audit_procedure_command_receipt_mutation();
                """);
            migrationBuilder.DropTable(
                name: "audit_procedure_command_receipts");
        }
    }
}
