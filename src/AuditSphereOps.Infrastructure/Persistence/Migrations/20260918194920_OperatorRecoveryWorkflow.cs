using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperatorRecoveryWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_operation_request() RETURNS trigger AS $$
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
                    OR NEW.attempt_token < OLD.attempt_token
                    OR (NEW.attempt_count < OLD.attempt_count AND NEW.status != 'RETRY_WAIT')
                    OR (OLD.status = 'COMPLETED' AND NEW IS DISTINCT FROM OLD) THEN
                    RAISE EXCEPTION 'Operation request, counters and completed result cannot be rewritten';
                  END IF;
                  RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_operation_request() RETURNS trigger AS $$
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
                """);
        }
    }
}
