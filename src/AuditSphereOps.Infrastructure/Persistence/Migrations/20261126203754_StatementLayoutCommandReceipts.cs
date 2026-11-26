using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatementLayoutCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "statement_layout_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    layout_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_layout_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    result_layout_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    result_version_number = table.Column<int>(type: "integer", nullable: false),
                    input_json = table.Column<string>(type: "text", maxLength: 120000, nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_layout_command_receipts", x => x.id);
                    table.CheckConstraint("ck_statement_layout_command_receipt_values", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND action IN ('SAVE','PUBLISH') AND request_hash ~ '^[0-9a-f]{64}$' AND (expected_layout_hash IS NULL OR expected_layout_hash ~ '^[0-9a-f]{64}$') AND result_layout_hash ~ '^[0-9a-f]{64}$' AND result_status IN ('DRAFT','VALIDATED','PUBLISHED') AND result_version_number >= 1 AND length(input_json) BETWEEN 1 AND 120000 AND length(result_json) BETWEEN 1 AND 1000000 AND jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(result_json::jsonb) = 'object'");
                    table.ForeignKey(
                        name: "FK_statement_layout_command_receipts_practice_clients_firm_id_~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_layout_command_receipts_statement_layout_versions~",
                        columns: x => new { x.firm_id, x.client_id, x.layout_version_id },
                        principalTable: "statement_layout_versions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_layout_command_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_statement_layout_command_receipt_client_history",
                table: "statement_layout_command_receipts",
                columns: new[] { "firm_id", "client_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_layout_command_receipts_firm_id_actor_id",
                table: "statement_layout_command_receipts",
                columns: new[] { "firm_id", "actor_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_layout_command_receipts_firm_id_client_id_layout_~",
                table: "statement_layout_command_receipts",
                columns: new[] { "firm_id", "client_id", "layout_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_statement_layout_command_receipt_actor_request",
                table: "statement_layout_command_receipts",
                columns: new[] { "firm_id", "client_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.Sql(@"
                CREATE FUNCTION reject_statement_layout_command_receipt_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Statement-layout command receipts are append-only.' USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_statement_layout_command_receipts_append_only
                  BEFORE UPDATE OR DELETE ON statement_layout_command_receipts
                  FOR EACH ROW EXECUTE FUNCTION reject_statement_layout_command_receipt_mutation();
                CREATE TRIGGER trg_statement_layout_command_receipts_no_truncate
                  BEFORE TRUNCATE ON statement_layout_command_receipts
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_statement_layout_command_receipt_mutation();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_statement_layout_command_receipts_no_truncate ON statement_layout_command_receipts;
                DROP TRIGGER IF EXISTS trg_statement_layout_command_receipts_append_only ON statement_layout_command_receipts;
                DROP FUNCTION IF EXISTS reject_statement_layout_command_receipt_mutation();
            ");
            migrationBuilder.DropTable(
                name: "statement_layout_command_receipts");
        }
    }
}
