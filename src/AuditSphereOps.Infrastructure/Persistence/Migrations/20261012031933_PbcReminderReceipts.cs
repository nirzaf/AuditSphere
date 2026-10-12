using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcReminderReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_pbc_communications_scope_id",
                table: "pbc_communications",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "pbc_reminder_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    communication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_reminder_receipts", x => x.id);
                    table.CheckConstraint("ck_pbc_reminder_receipt_values", "rule_revision_id <> '00000000-0000-0000-0000-000000000000'::uuid AND action_key ~ '^(PRE_DUE:[0-9]{4}-[0-9]{2}-[0-9]{2}|OVERDUE:[0-9]{1,6})$'");
                    table.ForeignKey(
                        name: "FK_pbc_reminder_receipts_pbc_communications_firm_id_client_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.communication_id },
                        principalTable: "pbc_communications",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_reminder_receipts_pbc_requests_firm_id_client_id_engage~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_request_id },
                        principalTable: "pbc_requests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_reminder_receipts_firm_id_client_id_engagement_id_commu~",
                table: "pbc_reminder_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "communication_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_reminder_receipts_firm_id_client_id_engagement_id_pbc_r~",
                table: "pbc_reminder_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_reminder_receipts_firm_id_engagement_id_created_at",
                table: "pbc_reminder_receipts",
                columns: new[] { "firm_id", "engagement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "UX_pbc_reminder_receipts_action",
                table: "pbc_reminder_receipts",
                columns: new[] { "firm_id", "rule_revision_id", "pbc_request_id", "action_key" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_pbc_reminder_receipt_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'PBC reminder receipts are append-only.' USING ERRCODE = '55000';
                END; $$;
                CREATE TRIGGER trg_pbc_reminder_receipts_append_only
                  BEFORE UPDATE OR DELETE ON pbc_reminder_receipts
                  FOR EACH ROW EXECUTE FUNCTION prevent_pbc_reminder_receipt_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_pbc_reminder_receipts_append_only ON pbc_reminder_receipts; DROP FUNCTION IF EXISTS prevent_pbc_reminder_receipt_mutation();");
            migrationBuilder.DropTable(
                name: "pbc_reminder_receipts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_pbc_communications_scope_id",
                table: "pbc_communications");
        }
    }
}
