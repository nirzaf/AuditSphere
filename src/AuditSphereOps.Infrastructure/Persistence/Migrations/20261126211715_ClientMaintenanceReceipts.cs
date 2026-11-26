using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientMaintenanceReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_maintenance_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    previous_generation = table.Column<long>(type: "bigint", nullable: false),
                    result_generation = table.Column<long>(type: "bigint", nullable: false),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    after_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_maintenance_receipts", x => x.id);
                    table.CheckConstraint("ck_client_maintenance_receipt", "operation_kind IN ('PROFILE','CONTACT') AND actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND previous_generation >= 1 AND result_generation = previous_generation + 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND ((operation_kind = 'PROFILE' AND contact_id IS NULL) OR (operation_kind = 'CONTACT' AND contact_id IS NOT NULL)) AND length(before_json) BETWEEN 1 AND 10000000 AND length(after_json) BETWEEN 1 AND 10000000 AND jsonb_typeof(before_json::jsonb) = 'object' AND jsonb_typeof(after_json::jsonb) = 'object'");
                    table.ForeignKey(
                        name: "FK_client_maintenance_receipts_client_contacts_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.contact_id },
                        principalTable: "client_contacts",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_maintenance_receipts_practice_clients_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_maintenance_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_maintenance_receipts_firm_id_client_id_contact_id",
                table: "client_maintenance_receipts",
                columns: new[] { "firm_id", "client_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_maintenance_receipt_actor_request",
                table: "client_maintenance_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION reject_client_maintenance_receipt_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Client maintenance receipts are append-only.' USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_client_maintenance_receipts_append_only
                  BEFORE UPDATE OR DELETE ON client_maintenance_receipts
                  FOR EACH ROW EXECUTE FUNCTION reject_client_maintenance_receipt_mutation();
                CREATE TRIGGER trg_client_maintenance_receipts_no_truncate
                  BEFORE TRUNCATE ON client_maintenance_receipts
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_client_maintenance_receipt_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE client_maintenance_receipts IN ACCESS EXCLUSIVE MODE; DO $$ BEGIN IF EXISTS (SELECT 1 FROM client_maintenance_receipts) THEN RAISE EXCEPTION 'Client maintenance receipt history is append-only; downgrade refused.' USING ERRCODE = '55000'; END IF; END $$;");
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_client_maintenance_receipts_no_truncate ON client_maintenance_receipts;
                DROP TRIGGER IF EXISTS trg_client_maintenance_receipts_append_only ON client_maintenance_receipts;
                DROP FUNCTION IF EXISTS reject_client_maintenance_receipt_mutation();
                """);
            migrationBuilder.DropTable(
                name: "client_maintenance_receipts");
        }
    }
}
