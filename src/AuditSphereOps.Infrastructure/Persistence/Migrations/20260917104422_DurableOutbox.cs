using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DurableOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM durable_operations) THEN
                    RAISE EXCEPTION 'DurableOutbox requires disposition of legacy operations before upgrade; no rows were changed';
                  END IF;
                END $$;
                """);
            migrationBuilder.RenameColumn(
                name: "attempt",
                table: "durable_operations",
                newName: "attempt_count");

            migrationBuilder.AlterColumn<string>(
                name: "request_digest",
                table: "durable_operations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "durable_operations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "schema_version",
                table: "durable_operations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "attempt_token",
                table: "durable_operations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "authority_mode",
                table: "durable_operations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "claimed_epoch",
                table: "durable_operations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "durable_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                table: "durable_operations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "error_code",
                table: "durable_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "execution_group",
                table: "durable_operations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "execution_mode",
                table: "durable_operations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "expected_revision",
                table: "durable_operations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "is_reconciliation",
                table: "durable_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                table: "durable_operations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "originator_id",
                table: "durable_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "request_bytes",
                table: "durable_operations",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "result_digest",
                table: "durable_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_identity",
                table: "durable_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_id",
                table: "durable_operations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_engagements_firm_id_practice_client_id_id",
                table: "engagements",
                columns: new[] { "firm_id", "practice_client_id", "id" });

            migrationBuilder.CreateTable(
                name: "client_safety_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_safety_states", x => x.id);
                    table.CheckConstraint("ck_client_generation", "input_generation >= 1");
                    table.ForeignKey(
                        name: "FK_client_safety_states_practice_clients_firm_id_id",
                        columns: x => new { x.firm_id, x.id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firm_safety_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operating_mode = table.Column<string>(type: "text", nullable: false),
                    deployment_epoch = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_safety_states", x => x.id);
                    table.CheckConstraint("ck_firm_safety", "deployment_epoch >= 1 AND operating_mode IN ('LOCAL_ONLY','RECOVERY_QUARANTINE')");
                });

            migrationBuilder.CreateTable(
                name: "operation_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<long>(type: "bigint", nullable: false),
                    owner = table.Column<string>(type: "text", nullable: false),
                    reconciliation = table.Column<bool>(type: "boolean", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_operation_attempts_durable_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "durable_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operation_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    executor = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_operation_events_durable_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "durable_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_durable_operations_firm_id_client_id_engagement_id",
                table: "durable_operations",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_durable_operations_firm_id_execution_group_status_next_atte~",
                table: "durable_operations",
                columns: new[] { "firm_id", "execution_group", "status", "next_attempt_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_operation_firm_key",
                table: "durable_operations",
                columns: new[] { "firm_id", "idempotency_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_counters",
                table: "durable_operations",
                sql: "attempt_token >= 0 AND attempt_count >= 0 AND expected_revision >= 1 AND schema_version >= 1 AND claimed_epoch >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_lease",
                table: "durable_operations",
                sql: "(status IN ('CLAIMED','REMOTE_STARTED','VERIFYING','CANCEL_REQUESTED') AND lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL AND attempt_token > 0) OR (status NOT IN ('CLAIMED','REMOTE_STARTED','VERIFYING','CANCEL_REQUESTED') AND lease_owner IS NULL AND lease_expires_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_mode",
                table: "durable_operations",
                sql: "execution_mode IN ('LOCAL','SIMULATED','LIVE') AND authority_mode IN ('LOCAL_VALIDATION','SIMULATION')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_request",
                table: "durable_operations",
                sql: "length(idempotency_key) > 0 AND request_digest ~ '^[0-9a-f]{64}$' AND octet_length(request_bytes) > 0 AND length(payload_json) <= 16384");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_result",
                table: "durable_operations",
                sql: "status <> 'COMPLETED' OR (completed_at IS NOT NULL AND result_identity IS NOT NULL AND length(result_identity) > 0 AND result_digest IS NOT NULL AND result_digest ~ '^[0-9a-f]{64}$')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_scope",
                table: "durable_operations",
                sql: "engagement_id IS NULL OR client_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_state",
                table: "durable_operations",
                sql: "status IN ('PENDING','CLAIMED','REMOTE_STARTED','VERIFYING','COMPLETED','RETRY_WAIT','AUTHORIZATION_BLOCKED','PROVIDER_BLOCKED','RESULT_UNCERTAIN','DEAD_LETTER','CANCEL_REQUESTED','CANCELLED_WITH_DISPOSITION')");

            migrationBuilder.CreateIndex(
                name: "IX_client_safety_states_firm_id_id",
                table: "client_safety_states",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_attempts_operation_id_token",
                table: "operation_attempts",
                columns: new[] { "operation_id", "token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_events_operation_id_occurred_at",
                table: "operation_events",
                columns: new[] { "operation_id", "occurred_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_durable_operations_engagements_firm_id_client_id_engagement~",
                table: "durable_operations",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_durable_operations_firm_safety_states_firm_id",
                table: "durable_operations",
                column: "firm_id",
                principalTable: "firm_safety_states",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_durable_operations_practice_clients_firm_id_client_id",
                table: "durable_operations",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                INSERT INTO firm_safety_states (id, operating_mode, deployment_epoch)
                  SELECT DISTINCT firm_id, 'LOCAL_ONLY', 1 FROM practice_clients;
                INSERT INTO client_safety_states (id, firm_id, input_generation)
                  SELECT id, firm_id, 1 FROM practice_clients;
                CREATE FUNCTION enforce_operation_evidence() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Operation evidence is append-only';
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_operation_attempt_append BEFORE UPDATE OR DELETE ON operation_attempts
                  FOR EACH ROW EXECUTE FUNCTION enforce_operation_evidence();
                CREATE TRIGGER trg_operation_event_append BEFORE UPDATE OR DELETE ON operation_events
                  FOR EACH ROW EXECUTE FUNCTION enforce_operation_evidence();
                CREATE FUNCTION enforce_operation_request() RETURNS trigger AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Operation history cannot be deleted'; END IF;
                  IF ROW(NEW.id, NEW.firm_id, NEW.client_id, NEW.engagement_id, NEW.operation_kind,
                    NEW.schema_version, NEW.execution_group, NEW.execution_mode, NEW.authority_mode,
                    NEW.target_id, NEW.expected_revision, NEW.originator_id, NEW.correlation_id,
                    NEW.idempotency_key, NEW.payload_json, NEW.request_bytes, NEW.request_digest, NEW.created_at)
                    IS DISTINCT FROM ROW(OLD.id, OLD.firm_id, OLD.client_id, OLD.engagement_id, OLD.operation_kind,
                    OLD.schema_version, OLD.execution_group, OLD.execution_mode, OLD.authority_mode,
                    OLD.target_id, OLD.expected_revision, OLD.originator_id, OLD.correlation_id,
                    OLD.idempotency_key, OLD.payload_json, OLD.request_bytes, OLD.request_digest, OLD.created_at)
                    OR NEW.attempt_token < OLD.attempt_token OR NEW.attempt_count < OLD.attempt_count
                    OR (OLD.status = 'COMPLETED' AND NEW IS DISTINCT FROM OLD) THEN
                    RAISE EXCEPTION 'Operation request, counters and completed result cannot be rewritten';
                  END IF;
                  RETURN NEW;
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_operation_immutable_request BEFORE UPDATE OR DELETE ON durable_operations
                  FOR EACH ROW EXECUTE FUNCTION enforce_operation_request();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM durable_operations) THEN
                    RAISE EXCEPTION 'DurableOutbox rollback would discard operation evidence; use a coordinated recovery plan';
                  END IF;
                END $$;
                DROP TRIGGER trg_operation_immutable_request ON durable_operations;
                DROP FUNCTION enforce_operation_request();
                DROP TRIGGER trg_operation_attempt_append ON operation_attempts;
                DROP TRIGGER trg_operation_event_append ON operation_events;
                DROP FUNCTION enforce_operation_evidence();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_durable_operations_engagements_firm_id_client_id_engagement~",
                table: "durable_operations");

            migrationBuilder.DropForeignKey(
                name: "FK_durable_operations_firm_safety_states_firm_id",
                table: "durable_operations");

            migrationBuilder.DropForeignKey(
                name: "FK_durable_operations_practice_clients_firm_id_client_id",
                table: "durable_operations");

            migrationBuilder.DropTable(
                name: "client_safety_states");

            migrationBuilder.DropTable(
                name: "firm_safety_states");

            migrationBuilder.DropTable(
                name: "operation_attempts");

            migrationBuilder.DropTable(
                name: "operation_events");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_engagements_firm_id_practice_client_id_id",
                table: "engagements");

            migrationBuilder.DropIndex(
                name: "IX_durable_operations_firm_id_client_id_engagement_id",
                table: "durable_operations");

            migrationBuilder.DropIndex(
                name: "IX_durable_operations_firm_id_execution_group_status_next_atte~",
                table: "durable_operations");

            migrationBuilder.DropIndex(
                name: "ux_operation_firm_key",
                table: "durable_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_counters",
                table: "durable_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_lease",
                table: "durable_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_mode",
                table: "durable_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_request",
                table: "durable_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_result",
                table: "durable_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_scope",
                table: "durable_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_state",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "schema_version",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "attempt_token",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "authority_mode",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "claimed_epoch",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "error_code",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "execution_group",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "execution_mode",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "expected_revision",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "is_reconciliation",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "originator_id",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "request_bytes",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "result_digest",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "result_identity",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "target_id",
                table: "durable_operations");

            migrationBuilder.RenameColumn(
                name: "attempt_count",
                table: "durable_operations",
                newName: "attempt");

            migrationBuilder.AlterColumn<string>(
                name: "request_digest",
                table: "durable_operations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "durable_operations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
