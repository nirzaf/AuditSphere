using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261017140000_BillingCommandReceipts")]
public sealed class BillingCommandReceipts : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.CreateTable(
      name: "billing_command_receipts",
      columns: table => new
      {
        id = table.Column<Guid>(type: "uuid", nullable: false),
        firm_id = table.Column<Guid>(type: "uuid", nullable: false),
        client_id = table.Column<Guid>(type: "uuid", nullable: false),
        actor_id = table.Column<Guid>(type: "uuid", nullable: false),
        actor_epoch = table.Column<long>(type: "bigint", nullable: false),
        request_id = table.Column<Guid>(type: "uuid", nullable: false),
        action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
        request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
        result_id = table.Column<Guid>(type: "uuid", nullable: false),
        outcome_json = table.Column<string>(type: "text", nullable: false),
        created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
      },
      constraints: table =>
      {
        table.PrimaryKey("PK_billing_command_receipts", x => x.id);
        table.CheckConstraint("ck_billing_command_receipt_values",
          "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
          "action IN ('RECORD_RECEIPT','ALLOCATE_RECEIPT','ISSUE_CREDIT_NOTE') AND request_hash ~ '^[0-9a-f]{64}$' AND " +
          "result_id <> '00000000-0000-0000-0000-000000000000'::uuid AND length(outcome_json) BETWEEN 1 AND 2000 AND " +
          "jsonb_typeof(outcome_json::jsonb) = 'object' AND outcome_json::jsonb ->> 'Action' = action AND " +
          "outcome_json::jsonb ->> 'ResultId' = result_id::text");
        table.ForeignKey("FK_billing_command_receipts_users_firm_id_actor_id", x => new { x.firm_id, x.actor_id },
          principalTable: "users", principalColumns: new[] { "firm_id", "id" }, onDelete: ReferentialAction.Restrict);
        table.ForeignKey("FK_billing_command_receipts_practice_clients_firm_id_client_id", x => new { x.firm_id, x.client_id },
          principalTable: "practice_clients", principalColumns: new[] { "firm_id", "id" }, onDelete: ReferentialAction.Restrict);
      });

    migrationBuilder.CreateIndex("ux_billing_command_receipts_actor_request", "billing_command_receipts",
      new[] { "firm_id", "actor_id", "request_id" }, unique: true);
    migrationBuilder.CreateIndex("ix_billing_command_receipts_firm_id_client_id_created_at", "billing_command_receipts",
      new[] { "firm_id", "client_id", "created_at" });
    migrationBuilder.Sql("""
      CREATE FUNCTION prevent_billing_command_receipt_mutation() RETURNS trigger AS $$
      BEGIN
        RAISE EXCEPTION 'billing command receipts are immutable' USING ERRCODE = '55000';
      END;
      $$ LANGUAGE plpgsql;
      CREATE TRIGGER trg_billing_command_receipts_immutable
      BEFORE UPDATE OR DELETE ON billing_command_receipts
      FOR EACH ROW EXECUTE FUNCTION prevent_billing_command_receipt_mutation();
      CREATE TRIGGER trg_billing_command_receipts_no_truncate
      BEFORE TRUNCATE ON billing_command_receipts
      FOR EACH STATEMENT EXECUTE FUNCTION prevent_billing_command_receipt_mutation();
      """);
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_billing_command_receipts_no_truncate ON billing_command_receipts; DROP TRIGGER IF EXISTS trg_billing_command_receipts_immutable ON billing_command_receipts; DROP FUNCTION IF EXISTS prevent_billing_command_receipt_mutation();");
    migrationBuilder.DropTable("billing_command_receipts");
  }
}
